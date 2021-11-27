using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DarkTunnel.Common;
using DarkTunnel.Common.Messages;

namespace DarkTunnel.Master
{
    public class MasterServer
    {
        private Socket udp;
        private UdpConnection connection;
        private ConcurrentDictionary<int, PublishEntry> published = new ConcurrentDictionary<int, PublishEntry>();

        public MasterServer(int port)
        {
            SetupUDPSocket(port);
            connection = new UdpConnection(udp, ReceiveCallback);
        }

        public void Stop()
        {
            connection.Stop();
            udp.Close();
        }

        private void SetupUDPSocket(int port)
        {
            udp = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            udp.DualMode = true;
            udp.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }

        private void ReceiveCallback(IMessage message, IPEndPoint endpoint)
        {
            if (message is MasterServerInfoRequest)
            {
                MasterServerInfoRequest msi = message as MasterServerInfoRequest;
                MasterServerInfoReply msir = new MasterServerInfoReply();
                msir.server = msi.server;
                msir.client = msi.client;
                msir.status = false;
                msir.message = "ID not found";
                if (published.TryGetValue(msi.server, out PublishEntry entry))
                {
                    msir.status = true;
                    msir.message = "OK";
                    msir.endpoints = entry.endpoints;
                }
                Console.WriteLine($"MSIR: {msir.client} connecting to {msi.server}, status: {msir.message}");
                connection.Send(message, endpoint);
            }
            if (message is MasterServerPublishRequest)
            {
                MasterServerPublishRequest msp = message as MasterServerPublishRequest;
                MasterServerPublishReply mspr = new MasterServerPublishReply();
                mspr.id = msp.id;
                mspr.status = false;
                mspr.message = "ID already registered to another server";
                if (published.TryGetValue(mspr.id, out PublishEntry entry))
                {
                    if (msp.secret == entry.secret)
                    {
                        if (!entry.endpoints.Contains(endpoint))
                        {
                            entry.endpoints.Add(endpoint);
                        }
                        entry.lastPublishTime = DateTime.UtcNow.Ticks;
                        mspr.status = true;
                        mspr.message = "Updated OK";
                    }
                }
                else
                {
                    PublishEntry entry2 = new PublishEntry();
                    entry2.secret = msp.secret;
                    entry2.lastPublishTime = DateTime.UtcNow.Ticks;
                    if (!entry2.endpoints.Contains(endpoint))
                    {
                        entry2.endpoints.Add(endpoint);
                    }
                    published.TryAdd(msp.id, entry2);
                    mspr.status = true;
                    mspr.message = "Registered OK";
                }
                Console.WriteLine($"MSPR: {mspr.id} status {mspr.message}");
                connection.Send(mspr, endpoint);
            }
        }
    }
}
