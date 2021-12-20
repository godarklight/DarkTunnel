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
            mainLoop.Name = "TunnelNode-MainLoop";
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
            tcpServer = new TcpListener(new IPEndPoint(IPAddress.Any, options.localPort));
            //tcpServer.Server.DualMode = true;
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
                            MediationClient.Remove(clientMapping[c.id].localTCPEndpoint);
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
                Thread.Sleep(100);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                TcpClient tcp = tcpServer.EndAcceptTcpClient(ar);
                int newID = random.Next();
                Client c = new Client(options, newID, connection, tcp, connectionBucket);
                Console.WriteLine($"New TCP Client {c.id} from {tcp.Client.RemoteEndPoint}");
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
                        ncr.protocol_version = Header.PROTOCOL_VERSION;
                        ncr.downloadRate = options.downloadSpeed;
                        ncr.ep = $"end{c.localTCPEndpoint}";
                        connection.Send(ncr, endpoint);
                    }
                }
            }
            else
            {
                foreach (IPAddress addr in masterServerAddresses)
                {
                    for (int i = 0; i < 4; i++)
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
                ncr.protocol_version = Header.PROTOCOL_VERSION;
                ncr.downloadRate = options.downloadSpeed;
                //Do not connect protocol-incompatible clients.
                if (nc.protocol_version != Header.PROTOCOL_VERSION)
                {
                    return;
                }
                Client c = null;
                if (!clientMapping.ContainsKey(ncr.id))
                {
                    TcpClient tcp = new TcpClient(AddressFamily.InterNetwork);
                    //tcp.Client.DualMode = true;
                    try
                    {
                        tcp.Connect(options.endpoints[0]);
                        c = new Client(options, nc.id, connection, tcp, connectionBucket);
                        clients.Add(c);
                        clientMapping.Add(c.id, c);
                        MediationClient.Add(c.localTCPEndpoint);
                        //add mapping for local tcp client and remote IP
                    }
                    catch
                    {
                        Disconnect dis = new Disconnect();
                        dis.id = nc.id;
                        dis.reason = "TCP server is currently not running";
                        dis.ep = $"end{c.localTCPEndpoint}";
                        connection.Send(dis, endpoint);
                        return;
                    }
                }
                else
                {
                    c = clientMapping[nc.id];
                }
                ncr.ep = $"end{c.localTCPEndpoint}";
                connection.Send(ncr, endpoint);
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
            }
            if (!options.isServer && message is NewConnectionReply)
            {
                NewConnectionReply ncr = message as NewConnectionReply;
                if (ncr.protocol_version != Header.PROTOCOL_VERSION)
                {
                    Console.WriteLine($"Unable to connect to incompatible server, our version: {Header.PROTOCOL_VERSION}, server: {ncr.protocol_version}");
                    return;
                }
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
                        ncr.protocol_version = Header.PROTOCOL_VERSION;
                        ncr.downloadRate = options.downloadSpeed;
                        ncr.ep = $"end{c.localTCPEndpoint}";
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
                else
                {
                    
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
                else
                {
                    
                }
            }
            if (message is PingRequest)
            {
                PingRequest pr = message as PingRequest;
                if (clientMapping.ContainsKey(pr.id))
                {
                    Client c = clientMapping[pr.id];
                    PingReply preply = new PingReply();
                    preply.id = pr.id;
                    preply.sendTime = pr.sendTime;
                    preply.ep = $"end{c.localTCPEndpoint}";
                    connection.Send(preply, endpoint);
                }
            }
            if (message is PingReply)
            {
                PingReply pr = message as PingReply;
                long currentTime = DateTime.UtcNow.Ticks;
                long timeDelta = currentTime - pr.sendTime;
                int timeMs = (int)(timeDelta / TimeSpan.TicksPerMillisecond);
                if (clientMapping.ContainsKey(pr.id))
                {
                    Client c = clientMapping[pr.id];
                    c.latency = timeMs;
                }
            }
            if (message is PrintConsole)
            {
                PrintConsole pc = message as PrintConsole;
                Console.WriteLine($"Remote Message: {pc.message}");
            }
            if (message is Disconnect)
            {
                Disconnect dis = message as Disconnect;
                if (clientMapping.ContainsKey(dis.id))
                {
                    Client c = clientMapping[dis.id];
                    c.Disconnect("Remote side requested a disconnect");
                    Console.WriteLine($"Stream {dis.id} remotely disconnected because: {dis.reason}");
                }
            }
        }


    }
}
