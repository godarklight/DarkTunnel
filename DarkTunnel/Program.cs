using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DarkTunnel
{
    class Program
    {
        public static void Main(string[] args)
        {
            NodeOptions options = new NodeOptions();

            if (!File.Exists("config.txt"))
            {
                Console.WriteLine("Unable to find config.txt");
                Console.WriteLine("Creating a default:");
                Console.WriteLine("c) Create a client config file");
                Console.WriteLine("s) Create a server config file");
                Console.WriteLine("Any other key: Quit");
                ConsoleKeyInfo cki = Console.ReadKey();
                if (cki.KeyChar == 'c')
                {
                    options.isServer = false;
                    options.masterServerID = 0;
                    options.localPort = 25565;
                    using (StreamWriter sw = new StreamWriter("config.txt"))
                    {
                        options.Save(sw);
                    }
                }
                if (cki.KeyChar == 's')
                {
                    options.isServer = true;
                    options.endpoint = "127.0.0.1:25565";
                    options.localPort = 26702;
                    using (StreamWriter sw = new StreamWriter("config.txt"))
                    {
                        options.Save(sw);
                    }
                }
                return;
            }

            using (StreamReader sr = new StreamReader("config.txt"))
            {
                if (!options.Load(sr))
                {
                    Console.WriteLine("Failed to load config.txt");
                    return;
                }
            }
            
            TcpClient tcpClient = new TcpClient();

            UdpClient udpClient = new UdpClient(options.mediationClientPort);

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){
                int SIO_UDP_CONNRESET = -1744830452;
                udpClient.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }

            IPEndPoint ep = new IPEndPoint(IPAddress.Parse("150.136.166.80"), 6510);

            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);

            MediationClient mc = new MediationClient(tcpClient, udpClient, ep, options.remoteIP, options.mediationClientPort, endpoint, options.isServer);

            mc.TrackedClient();
            
            if(options.isServer){
                mc.UdpServer();
            } else {
                mc.UdpClient();
            }

            TunnelNode tn = new TunnelNode(options);
            if (options.isServer)
            {
                Console.WriteLine($"Server forwarding {options.endpoints[0]} to UDP port {options.localPort}");
                if (options.masterServerID != 0)
                {
                    Console.WriteLine($"Server registering with master ID {options.masterServerID}");
                }
            }
            else
            {
                if (options.masterServerID != 0)
                {
                    Console.WriteLine($"Client forwarding TCP port {options.localPort} to UDP server {options.masterServerID}");
                }
                else
                {
                    Console.WriteLine($"Client forwarding TCP port {options.localPort} to UDP server {options.endpoints[0]}");
                }
            }

            Console.WriteLine("Press q or ctrl+c to quit.");
            bool hasConsole = true;
            bool running = true;
            Console.CancelKeyPress += (s, e) => { running = false; tn.Stop(); };
            while (running)
            {
                if (hasConsole)
                {
                    try
                    {
                        ConsoleKeyInfo cki = Console.ReadKey(false);
                        if (cki.KeyChar == 'q')
                        {
                            running = false;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        Console.WriteLine("Program does not have a console, not listening for console input.");
                        hasConsole = false;
                    }
                }
                else
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }
    }
}
