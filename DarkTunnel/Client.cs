using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
        public long lastUdpPingTime;
        public long lastUdpSendAckTime;
        public TcpClient tcp;
        public IPEndPoint udpEndpoint;
        public byte[] buffer = new byte[1024];
        public StreamRingBuffer txQueue = new StreamRingBuffer(16 * 1024 * 1024);
        public FutureDataStore fds = new FutureDataStore();
        public TokenBucket bucket;
        private long currentRecvPos;
        private long currentSendPos;
        private long lastWriteResetTime;
        private long lastUdpRecvAckTime;
        private long lastTCPResetTime;
        private const long TIMEOUT = 10 * TimeSpan.TicksPerSecond;
        private const long PING = 2 * TimeSpan.TicksPerSecond;
        private long ACK_TIME = 10 * TimeSpan.TicksPerMillisecond;
        private UdpConnection connection;
        private NodeOptions options;
        private long ackSafe;
        private Thread clientThread;
        public AutoResetEvent sendEvent = new AutoResetEvent(false);
        public int latency;

        public Client(NodeOptions options, int clientID, UdpConnection connection, TcpClient tcp, TokenBucket parentBucket)
        {
            this.id = clientID;
            this.tcp = tcp;
            this.connection = connection;
            this.options = options;

            tcp.NoDelay = true;
            tcp.GetStream().BeginRead(buffer, 0, buffer.Length, TCPReceiveCallback, null);

            bucket = new TokenBucket();
            bucket.rateBytesPerSecond = options.uploadSpeed * 1024;
            bucket.totalBytes = bucket.rateBytesPerSecond;
            bucket.parent = parentBucket;

            clientThread = new Thread(new ThreadStart(Loop));
            clientThread.Name = $"ClientThread-{id}";
            clientThread.Start();
        }

        public void Loop()
        {
            while (connected)
            {
                long currentTime = DateTime.UtcNow.Ticks;

                //Disconnect if we hit the timeout
                if (currentTime - lastUdpRecvTime > TIMEOUT)
                {
                    Disconnect("UDP Receive Timeout");
                }

                //Only do the following if we are connected
                if (udpEndpoint != null)
                {

                    CheckPing();

                    SendData();

                    //Send buffered TCP data to the UDP server
                    if (txQueue.AvailableRead == 0)
                    {
                        //Ran out of TCP data
                        sendEvent.WaitOne(100);
                    }
                }
            }
        }

        private void CheckPing()
        {
            long currentTime = DateTime.UtcNow.Ticks;
            if (currentTime - lastUdpPingTime > PING)
            {
                lastUdpPingTime = currentTime;
                PingRequest pr = new PingRequest();
                pr.id = id;
                pr.sendTime = currentTime;
                connection.Send(pr, udpEndpoint);
            }
        }

        private void SendAck(bool force)
        {
            long currentTime = DateTime.UtcNow.Ticks;
            //Send acks to let the other side know we have received data.
            if (force || (currentTime - lastUdpSendAckTime) > ACK_TIME)
            {
                lastUdpSendTime = currentTime;
                lastUdpSendAckTime = currentTime;
                Ack ack = new Ack();
                ack.id = id;
                ack.streamAck = currentRecvPos;
                connection.Send(ack, udpEndpoint);
            }
        }

        public void ReceiveAck(Ack ack)
        {
            if (ack.streamAck > ackSafe)
            {
                lastUdpRecvAckTime = DateTime.UtcNow.Ticks;
                ackSafe = ack.streamAck;
            }
        }

        private void SendData()
        {
            long currentTime = DateTime.UtcNow.Ticks;

            //MarkFree is not thread safe with Read
            if (txQueue.StreamReadPos < ackSafe)
            {
                txQueue.MarkFree(ackSafe);
            }

            //Don't send old data.
            if (currentSendPos < txQueue.StreamReadPos)
            {
                lastWriteResetTime = currentTime;
                currentSendPos = txQueue.StreamReadPos;
            }

            //If we don't have much data to send let's jump back to the unack'd position to send earlier than the RTT
            float dataToSend = txQueue.AvailableRead / (float)(bucket.rateBytesPerSecond);
            if (dataToSend < 0.2f || latency < options.minRetransmitTime)
            {
                if (currentTime - lastWriteResetTime > options.minRetransmitTime * TimeSpan.TicksPerMillisecond)
                {
                    lastWriteResetTime = currentTime;
                    currentSendPos = txQueue.StreamReadPos;
                }
            }
            else
            {
                //We have a lot of data to send, so let's wait for ACK's to stop changing before doing a position reset.
                if (currentTime - lastUdpRecvAckTime > 50 * TimeSpan.TicksPerMillisecond)
                {
                    //Bias to let the acks flow again, and also build up data in the remote buffer
                    lastWriteResetTime = currentTime;
                    lastUdpRecvAckTime = currentTime + (4 * latency * TimeSpan.TicksPerMillisecond);
                    currentSendPos = txQueue.StreamReadPos;
                }
            }

            //Ran out of bytes to send
            long bytesToWrite = txQueue.StreamWritePos - currentSendPos;
            if (bytesToWrite == 0)
            {
                Thread.Sleep(10);
                return;
            }

            //Rate limit
            if (bucket.currentBytes < 500)
            {
                Thread.Sleep(10);
                return;
            }

            //Clamp to 500 byte packets
            if (bytesToWrite > 500)
            {
                bytesToWrite = 500;
            }

            //Send data
            Data d = new Data();
            d.id = id;
            d.streamPos = currentSendPos;
            d.streamAck = currentRecvPos;
            d.tcpData = new byte[bytesToWrite];
            txQueue.Read(d.tcpData, 0, currentSendPos, (int)bytesToWrite);
            lastUdpSendAckTime = currentTime;
            lastUdpSendTime = currentTime;
            connection.Send(d, udpEndpoint);
            currentSendPos += bytesToWrite;
            bucket.Take((int)bytesToWrite);
        }



        public void ReceiveData(Data d, bool fromUDP)
        {
            if (d.streamAck > ackSafe)
            {
                lastUdpRecvAckTime = DateTime.UtcNow.Ticks;
                ackSafe = d.streamAck;
            }

            //Data from the past
            if (d.streamPos + d.tcpData.Length <= currentRecvPos)
            {
                if (d.streamPos + d.tcpData.Length == currentRecvPos)
                {
                    SendAck(true);
                }
                return;
            }

            //Data in the future
            if (d.streamPos > currentRecvPos)
            {
                fds.StoreData(d);
                return;
            }

            //Exact packet we need, include partial matches
            int offset = (int)(currentRecvPos - d.streamPos);
            tcp.GetStream().Write(d.tcpData, offset, d.tcpData.Length - offset);
            currentRecvPos += d.tcpData.Length - offset;

            //Handle out of order data
            Data future = null;
            while ((future = fds.GetData(currentRecvPos)) != null)
            {
                offset = (int)(currentRecvPos - future.streamPos);
                tcp.GetStream().Write(future.tcpData, offset, future.tcpData.Length - offset);
                currentRecvPos += future.tcpData.Length - offset;
            }
            SendAck(false);
        }

        public void TCPReceiveCallback(IAsyncResult ar)
        {
            try
            {

                int bytesRead = tcp.GetStream().EndRead(ar);
                if (bytesRead == 0)
                {
                    Disconnect("TCP connection was closed.");
                    return;
                }
                else
                {
                    txQueue.Write(buffer, 0, bytesRead);
                    sendEvent.Set();
                    //If our txqueue is full we need to wait before we can write to it.
                    while (txQueue.AvailableWrite < buffer.Length)
                    {
                        if (!connected)
                        {
                            return;
                        }
                        Thread.Sleep(10);
                    }
                }
                tcp.GetStream().BeginRead(buffer, 0, buffer.Length, TCPReceiveCallback, null);
            }
            catch
            {
                Disconnect("TCP connection was closed.");
            }
        }

        public void Disconnect(string reason)
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
                if (reason != null && udpEndpoint != null)
                {
                    Disconnect dis = new Disconnect();
                    dis.id = id;
                    dis.reason = reason;
                    connection.Send(dis, udpEndpoint);
                }
            }
        }
    }
}