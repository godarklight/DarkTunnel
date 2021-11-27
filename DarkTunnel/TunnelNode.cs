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
        private TokenBucket connectionBucket;

        public TunnelNode(NodeOptions options)
        {
            this.connectionBucket = new TokenBucket();
            connectionBucket.rateBytesPerSecond = options.uploadSpeed * 1024;
            //1 second connnection buffer
            connectionBucket.totalBytes = connectionBucket.rateBytesPerSecond;
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
                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    Client c = clients[i];
                    if (!c.connected)
                    {
                        if (clientMapping.ContainsKey(c.id))
                        {
                            clientMapping.Remove(c.id);
                        }
                        clients.Remove(c);
                    }
                    else
                    {
                        c.Loop();
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
                Thread.Sleep(5);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                TcpClient clientTcp = tcpServer.EndAcceptTcpClient(ar);
                Client c = new Client(options, connection, connectionBucket);
                c.tcp = clientTcp;
                c.tcp.NoDelay = true;
                c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
                c.id = random.Next();
                Console.WriteLine($"New TCP Client {c.id}");
                ConnectUDPClient(c);
                clients.Add(c);
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
                        ncr.downloadRate = options.downloadSpeed;
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
            if (message is INodeMessage)
            {
                int clientID = ((INodeMessage)message).GetID();
                if (clientMapping.ContainsKey(clientID))
                {
                    Client c = clientMapping[clientID];
                    c.lastUdpRecvTime = DateTime.UtcNow.Ticks;
                }
            }
            if (options.isServer && message is NewConnectionRequest)
            {
                NewConnectionRequest nc = message as NewConnectionRequest;
                NewConnectionReply ncr = new NewConnectionReply();
                ncr.id = nc.id;
                ncr.downloadRate = options.downloadSpeed;
                Client c = null;
                if (!clientMapping.ContainsKey(ncr.id))
                {
                    c = new Client(options, connection, connectionBucket);
                    c.id = nc.id;
                    c.tcp = new TcpClient(options.endpoints[0].AddressFamily);
                    c.tcp.NoDelay = true;
                    c.tcp.Connect(options.endpoints[0]);
                    c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
                    clients.Add(c);
                    clientMapping.Add(c.id, c);
                }
                else
                {
                    c = clientMapping[nc.id];
                }
                //Clamp to the clients download speed
                Console.WriteLine($"Client {nc.id} download rate is {nc.downloadRate}KB/s");
                if (nc.downloadRate < options.uploadSpeed)
                {
                    c.bucket.rateBytesPerSecond = nc.downloadRate * 1024;
                    c.bucket.totalBytes = c.bucket.rateBytesPerSecond;
                }
                //Prefer IPv6
                if (c.udpEndpoint == null || c.udpEndpoint.AddressFamily == AddressFamily.InterNetwork && endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Console.WriteLine($"Client endpoint {c.id} set to: {endpoint}");
                    c.udpEndpoint = endpoint;
                }
                connection.Send(ncr, endpoint);
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
                        Console.WriteLine($"Server endpoint {c.id} set to: {endpoint}");
                        c.udpEndpoint = endpoint;
                    }
                    //Clamp to the servers download speed
                    Console.WriteLine($"Servers download rate is {ncr.downloadRate}KB/s");
                    if (ncr.downloadRate < options.uploadSpeed)
                    {
                        c.bucket.rateBytesPerSecond = ncr.downloadRate * 1024;
                        c.bucket.totalBytes = c.bucket.rateBytesPerSecond;
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
                        ncr.downloadRate = options.downloadSpeed;
                        Console.WriteLine($"MSIR connect: {msirEndpoint}");
                        connection.Send(ncr, msirEndpoint);
                    }
                }
            }
            if (message is MasterServerPublishReply)
            {
                MasterServerPublishReply mspr = message as MasterServerPublishReply;
                Console.WriteLine($"Publish Reply for {mspr.id}, registered {mspr.status}, {mspr.message}");
            }
            if (message is Data)
            {
                Data d = message as Data;
                if (clientMapping.ContainsKey(d.id))
                {
                    Client c = clientMapping[d.id];
                    c.ReceiveData(d, true);
                }
            }
            if (message is Ack)
            {
                Ack ack = message as Ack;
                if (clientMapping.ContainsKey(ack.id))
                {
                    Client c = clientMapping[ack.id];
                    c.ReceiveAck(ack);
                }
            }
            if (message is PrintConsole)
            {
                PrintConsole pc = message as PrintConsole;
                Console.WriteLine($"Remote Message: {pc.message}");
            }
        }

        private void TCPReceiveCallback(IAsyncResult ar)
        {
            Client c = ar.AsyncState as Client;
            try
            {

                int bytesRead = c.tcp.GetStream().EndRead(ar);
                if (bytesRead == 0)
                {
                    c.Disconnect();
                    return;
                }
                else
                {
                    c.txQueue.Write(c.buffer, 0, bytesRead);
                    //If our txqueue is full we need to wait before we can write to it.
                    while (c.txQueue.AvailableWrite < c.buffer.Length)
                    {
                        if (!c.connected)
                        {
                            return;
                        }
                        Thread.Sleep(10);
                    }
                }
                c.tcp.GetStream().BeginRead(c.buffer, 0, c.buffer.Length, TCPReceiveCallback, c);
            }
            catch
            {
                c.Disconnect();
            }
        }
    }
}
