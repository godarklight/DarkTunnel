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
        public TcpClient tcp;
        public IPEndPoint udpEndpoint;
        public byte[] buffer = new byte[1024];
        private const long TIMEOUT = 10 * TimeSpan.TicksPerSecond;
        private const long HEARTBEAT = 2 * TimeSpan.TicksPerSecond;

        public void Loop(UdpConnection connection)
        {
            if (!connected)
            {
                return;
            }
            long currentTime = DateTime.UtcNow.Ticks;
            if (udpEndpoint != null && (currentTime - lastUdpSendTime) > HEARTBEAT)
            {
                lastUdpSendTime = currentTime;
                Heartbeat hb = new Heartbeat();
                connection.Send(hb, udpEndpoint);
            }
            if (currentTime - lastUdpRecvTime > TIMEOUT)
            {
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (connected)
            {
                connected = false;
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