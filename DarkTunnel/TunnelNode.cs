using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DarkTunnel.Common;
using DarkTunnel.Common.Messages;

namespace DarkTunnel
{
    public class TunnelNode
    {
        private bool running = true;
        private Random random = new Random();
        private Thread mainLoop;
        private NodeOptions options;
        private TcpListener tcpServer;
        private Socket udp;
        private UdpConnection connection;
        private List<Client> clients = new List<Client>();
        private Dictionary<int, Client> clientMapping = new Dictionary<int, Client>();
        private IPAddress[] masterServerAddresses = Dns.GetHostAddresses("darktunnel.godarklight.privatedns.org");
        //Master state
        private long nextMasterTime = 0;

        public TunnelNode(NodeOptions options)
        {
            this.options = options;
            if (options.isServer)
            {
                SetupUDPSocket(options.localPort);
            }
            else
            {
                SetupTCPServer();
                SetupUDPSocket(0);
            }
            connection = new UdpConnection(udp, ReceiveCallback);
            mainLoop = new Thread(new ThreadStart(MainLoop));
            mainLoop.Start();
        }

        public void Stop()
        {
            connection.Stop();
            if (tcpServer != null)
            {
                tcpServer.Stop();
            }
            udp.Close();
        }

        private void SetupUDPSocket(int port)
        {
            udp = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            udp.DualMode = true;
            udp.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }

        private void SetupTCPServer()
        {
            tcpServer = new TcpListener(new IPEndPoint(IPAddress.IPv6Any, options.localPort));
            tcpServer.Start();
            tcpServer.BeginAcceptTcpClient(ConnectCallback, null);
        }

        public void MainLoop()
        {
            //This is the cleanup/heartbeating loop
            while (running)
            {
                long currentTime = DateTime.UtcNow.Ticks;
                for (int i = clients.Count; i > 0; i--)
                {
                    Client c = clients[i];
                    c.Loop(connection);
                    if (!c.connected)
                    {
                        if (clientMapping.ContainsKey(c.id))
                        {
                            clientMapping.Remove(c.id);
                        }
                        clients.Remove(c);
                    }

                }
                if (options.isServer && options.masterServerID != 0 && currentTime > nextMasterTime)
                {
                    //Send master registers every minute
                    nextMasterTime = currentTime + DateTime.UtcNow.Ticks + TimeSpan.TicksPerMinute;
                    MasterServerPublishRequest mspr = new MasterServerPublishRequest();
                    mspr.id = options.masterServerID;
                    mspr.secret = options.masterServerSecret;
                    mspr.localPort = options.localPort;
                    foreach (IPAddress masterAddr in masterServerAddresses)
                    {
                        connection.Send(mspr, new IPEndPoint(masterAddr, 16702));
                    }
                }
                Thread.Sleep(50);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                TcpClient clientTcp = tcpServer.EndAcceptTcpClient(ar);
                Client c = new Client();
                c.tcp = clientTcp;
                c.tcp.NoDelay = true;
                c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
                c.id = random.Next();
                Console.WriteLine($"New TCP Client {c.id}");
                ConnectUDPClient(c);
                clientMapping[c.id] = c;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error accepting socket: {e}");
            }
            tcpServer.BeginAcceptTcpClient(ConnectCallback, null);
        }

        private void ConnectUDPClient(Client c)
        {
            if (options.masterServerID == 0)
            {
                foreach (IPEndPoint endpoint in options.endpoints)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        NewConnectionRequest ncr = new NewConnectionRequest();
                        ncr.id = c.id;
                        connection.Send(ncr, endpoint);
                    }
                }
            }
            else
            {
                foreach (IPAddress addr in masterServerAddresses)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        MasterServerInfoRequest msir = new MasterServerInfoRequest();
                        msir.server = c.id;
                        connection.Send(msir, new IPEndPoint(addr, 16702));
                    }
                }
            }
        }

        private void ReceiveCallback(IMessage message, IPEndPoint endpoint)
        {
            if (options.isServer && message is NewConnectionRequest)
            {
                NewConnectionRequest nc = message as NewConnectionRequest;
                NewConnectionReply ncr = new NewConnectionReply();
                ncr.id = nc.id;
                Client c = null;
                if (!clientMapping.ContainsKey(ncr.id))
                {
                    c = new Client();
                    c.id = nc.id;
                    c.tcp = new TcpClient(AddressFamily.InterNetworkV6);
                    c.tcp.NoDelay = true;
                    c.tcp.Connect(options.endpoints[0]);
                    c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
                    clientMapping.Add(c.id, c);
                }
                else
                {
                    c = clientMapping[nc.id];
                }
                //Prefer IPv6
                if (c.udpEndpoint == null || c.udpEndpoint.AddressFamily == AddressFamily.InterNetwork && endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Console.WriteLine($"NCR endpoint: {endpoint}");
                    c.udpEndpoint = endpoint;
                }
            }
            if (!options.isServer && message is NewConnectionReply)
            {
                NewConnectionReply ncr = message as NewConnectionReply;
                if (clientMapping.ContainsKey(ncr.id))
                {
                    Client c = clientMapping[ncr.id];
                    //Prefer IPv6
                    if (c.udpEndpoint == null || c.udpEndpoint.AddressFamily == AddressFamily.InterNetwork && endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        Console.WriteLine($"NCR endpoint: {endpoint}");
                        c.udpEndpoint = endpoint;
                    }
                }
            }
            if (message is MasterServerInfoReply)
            {
                MasterServerInfoReply msir = message as MasterServerInfoReply;
                Client c = null;
                if (clientMapping.ContainsKey(msir.client))
                {
                    c = clientMapping[msir.client];
                }
                if (c == null)
                {
                    return;
                }
                if (msir.server != options.masterServerID)
                {
                    //Shouldn't happen but we should probably check this.
                    return;
                }
                if (!msir.status)
                {
                    Console.WriteLine($"Cannot connect: {msir.message}");
                    return;
                }
                foreach (IPEndPoint msirEndpoint in msir.endpoints)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        NewConnectionRequest ncr = new NewConnectionRequest();
                        ncr.id = msir.client;
                        Console.WriteLine($"MSIR connect: {msirEndpoint}");
                        connection.Send(ncr, msirEndpoint);
                    }
                }
            }
        }

        private void TCPReceiveCallback(IAsyncResult ar)
        {
            Client c = ar.AsyncState as Client;
            try
            {
                //TODO:
                int bytesRead = c.tcp.GetStream().EndRead(ar);
                Console.WriteLine($"TCP RECV {bytesRead}");
                c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
            }
            catch
            {
                c.Disconnect();
            }
        }
    }
}
