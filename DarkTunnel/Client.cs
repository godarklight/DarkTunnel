using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using DarkTunnel.Common;
using DarkTunnel.Common.Messages;

namespace DarkTunnel
{
    public class Client
    {
        public bool connected = true;
        public int id;
        public long lastUdpRecvTime = DateTime.UtcNow.Ticks;
        public long lastUdpSendTime;
        public long lastUdpAckTime;
        public TcpClient tcp;
        public IPEndPoint udpEndpoint;
        public byte[] buffer = new byte[1024];
        public StreamRingBuffer txQueue = new StreamRingBuffer(1024 * 1024);
        public TokenBucket bucket;
        private long currentRecvPos;
        private long currentSendPos;
        private long nextWriteResetTime;
        private const long TIMEOUT = 10 * TimeSpan.TicksPerSecond;
        private const long HEARTBEAT = 2 * TimeSpan.TicksPerSecond;
        private const long ACK_TIME = 100 * TimeSpan.TicksPerMillisecond;
        private UdpConnection connection;
        private NodeOptions options;
        private ConcurrentQueue<Data> futureData = new ConcurrentQueue<Data>();

        public Client(NodeOptions options, UdpConnection connection, TokenBucket connectionBucket)
        {
            this.connection = connection;
            this.options = options;
            bucket = new TokenBucket();
            bucket.rateBytesPerSecond = options.uploadSpeed * 1024;
            //1 second of buffer.
            bucket.totalBytes = bucket.rateBytesPerSecond;
        }

        public void Loop()
        {
            if (!connected)
            {
                return;
            }

            long currentTime = DateTime.UtcNow.Ticks;

            //Disconnect if we hit the timeout
            if (currentTime - lastUdpRecvTime > TIMEOUT)
            {
                Disconnect();
            }

            //Only do the following if we are connected
            if (udpEndpoint == null)
            {
                return;
            }

            //Send acks to let the other side know we have received data.
            if ((currentTime - lastUdpAckTime) > ACK_TIME)
            {
                lastUdpSendTime = currentTime;
                lastUdpAckTime = currentTime;
                Ack ack = new Ack();
                ack.id = id;
                ack.streamAck = currentRecvPos;
                connection.Send(ack, udpEndpoint);
            }

            //Send buffered TCP data to the UDP server
            SendData();
        }

        public void SendData()
        {
            long currentTime = DateTime.UtcNow.Ticks;
            if (currentTime > nextWriteResetTime)
            {
                nextWriteResetTime = currentTime + (options.minRetransmitTime * TimeSpan.TicksPerMillisecond);
                currentSendPos = txQueue.StreamReadPos;
            }
            while (currentSendPos < txQueue.StreamWritePos)
            {
                if (bucket.currentBytes < 500)
                {
                    return;
                }
                long bytesToWrite = txQueue.StreamWritePos - currentSendPos;
                if (bytesToWrite == 0)
                {
                    return;
                }
                if (bytesToWrite > 500)
                {
                    bytesToWrite = 500;
                }
                Data d = new Data();
                d.id = id;
                d.streamPos = currentSendPos;
                d.streamAck = currentRecvPos;
                d.tcpData = new byte[bytesToWrite];
                txQueue.Read(d.tcpData, 0, currentSendPos, (int)bytesToWrite);
                lastUdpAckTime = currentTime;
                lastUdpSendTime = currentTime;
                connection.Send(d, udpEndpoint);
                currentSendPos += bytesToWrite;
            }
        }

        public void ReceiveData(Data d, bool fromUDP)
        {
            if (d.streamAck > txQueue.StreamReadPos)
            {
                txQueue.MarkFree(d.streamAck);
            }
            //Data from the past
            if (d.streamPos + d.tcpData.Length <= currentRecvPos)
            {
                Console.WriteLine($"Past {d.tcpData.Length}");
                return;
            }
            //Exact packet we need, include partial matches
            if (currentRecvPos - d.streamPos < d.tcpData.Length)
            {
                int offset = (int)(currentRecvPos - d.streamPos);
                Console.WriteLine($"TCP SEND {d.tcpData.Length - offset}");
                tcp.GetStream().Write(d.tcpData, offset, d.tcpData.Length - offset);
                currentRecvPos += d.tcpData.Length - offset;
                return;
            }
            //Future packet
            futureData.Enqueue(d);
            Console.WriteLine($"Future {d.tcpData.Length}");
            if (fromUDP)
            {
                ProcessFutureData();
            }
        }

        private void ProcessFutureData()
        {
            int count = futureData.Count;
            for (int i = 0; i < count; i++)
            {
                if (futureData.TryDequeue(out Data d))
                {
                    ReceiveData(d, false);
                }
                else
                {
                    return;
                }
            }
        }

        public void Disconnect()
        {
            if (connected)
            {
                connected = false;
                Console.WriteLine($"Disconnected stream {id}");
                try
                {
                    tcp.Close();
                    tcp = null;
                }
                catch
                {
                }
            }
        }
    }
}