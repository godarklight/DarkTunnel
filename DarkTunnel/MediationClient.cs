using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Buffers;
using System.Threading;
using System.Collections.Generic;
using System.Timers;

namespace DarkTunnel {
    public class MediationClient {

        private TcpClient tcpClient;
        private UdpClient udpClient;
        private NetworkStream tcpClientStream;
        private Thread tcpClientThread;
        private Thread udpClientThread;
        private Thread udpServerThread;
        private IPEndPoint ep;
        private IPEndPoint programEndpoint;
        private String intendedIP = "";
        private int intendedPort = 0;
        private int localAppPort = 0;
        private int holePunchReceivedCount = 0;
        private bool connected = false;
        private String remoteIP = "";
        private int mediationClientPort = 0;
        private bool isServer = false;
        private List<IPEndPoint> connectedClients = new List<IPEndPoint>();
        public static Dictionary<IPEndPoint, IPEndPoint> mapping = new Dictionary<IPEndPoint, IPEndPoint>();
        public static IPEndPoint mostRecentEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 65535);
        public MediationClient(TcpClient tcpClient, UdpClient udpClient, IPEndPoint ep, String remoteIP, int mediationClientPort, IPEndPoint programEndpoint, bool isServer){
            this.tcpClient = tcpClient;
            this.udpClient = udpClient;
            this.ep = ep;
            this.remoteIP = remoteIP;
            this.mediationClientPort = mediationClientPort;
            this.programEndpoint = programEndpoint;
            this.isServer = isServer;
        }

        public static void Add(IPEndPoint localEP){
            Console.WriteLine($"bettttttttt {localEP} and {mostRecentEP}");
            mapping.Add(localEP, mostRecentEP);
        }

        public static void Remove(IPEndPoint localEP){
            mapping.Remove(localEP);
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e){
            //If not connected to remote endpoint, send remote IP to mediator
            if(!connected || isServer){
                byte[] sendBuffer = new byte[1500];
                sendBuffer = Encoding.ASCII.GetBytes(intendedIP);
                udpClient.Send(sendBuffer, sendBuffer.Length, ep);
                Console.WriteLine("Sent");
            }
            //If connected to remote endpoint, send keep alive msg
            if(connected){
                Byte[] sendBuffer = new byte[1500];
                sendBuffer = Encoding.ASCII.GetBytes("hi");
                if(isServer){
                    foreach(var client in connectedClients){
                        udpClient.Send(sendBuffer, sendBuffer.Length, client);
                    }
                } else {
                    udpClient.Send(sendBuffer, sendBuffer.Length, new IPEndPoint(IPAddress.Parse(intendedIP), intendedPort));
                }
                Console.WriteLine("Keep alive");
            }
        }

        public void TrackedClient(){
            //Attempt to connect to mediator
            try{
                tcpClient.Connect(ep);
            }
            catch(Exception e){
                Console.WriteLine(e);
            }
            //Once connected, begin listening
            if(tcpClient.Connected){
                Console.WriteLine("Connected");
                tcpClientStream = tcpClient.GetStream();

                tcpClientThread = new Thread(new ThreadStart(TcpListenLoop));
                tcpClientThread.Start();
            }
        }

        public void UdpClient(){
            //Set client intendedIP to remote endpoint IP
            intendedIP = remoteIP;
            //Try to send initial msg to mediator
            try{
                byte[] sendBuffer = new byte[1500];
                sendBuffer = Encoding.ASCII.GetBytes("check");
                udpClient.Send(sendBuffer, sendBuffer.Length, ep);
            }
            catch(Exception e){
                Console.WriteLine(e);
            }
            //Begin listening
            udpClientThread = new Thread(new ThreadStart(UdpClientListenLoop));
            udpClientThread.Start();
            //Start timer for hole punch init and keep alive
            System.Timers.Timer Timer = new System.Timers.Timer(500);
            Timer.Elapsed += OnTimedEvent;
            Timer.AutoReset = true;
            Timer.Enabled = true;
        }

        public void UdpServer(){
            //Set client intendedIP to something no client will have
            intendedIP = "0.0.0.0";
            //Try to send initial msg to mediator
            try{
                byte[] sendBuffer = new byte[1500];
                sendBuffer = Encoding.ASCII.GetBytes("check");
                udpClient.Send(sendBuffer, sendBuffer.Length, ep);
            }
            catch(Exception e){
                Console.WriteLine(e);
            }
            //Begin listening
            udpServerThread = new Thread(new ThreadStart(UdpServerListenLoop));
            udpServerThread.Start();
            //Start timer for hole punch init and keep alive
            System.Timers.Timer Timer = new System.Timers.Timer(500);
            Timer.Elapsed += OnTimedEvent;
            Timer.AutoReset = true;
            Timer.Enabled = true;
        }

        public void Send(IPEndPoint sendEP, String sendMSG){
            //Init buffer with max size of ethernet frame payload limit 
            byte[] sendBuffer = new byte[1500];
            Console.WriteLine("Writing: " + sendMSG);
            //Convert string into bytes
            sendBuffer = ASCIIEncoding.ASCII.GetBytes(sendMSG);
            //Send bytes to specified endpoint
            udpClient.Send(sendBuffer, sendBuffer.Length, sendEP);
        }

        public void UdpClientListenLoop(){
            //Init an IPEndPoint that will be populated with the sender's info
            IPEndPoint listenEP = new IPEndPoint(IPAddress.IPv6Any, mediationClientPort);
            while(true){
                byte[] recvBuffer = udpClient.Receive(ref listenEP);

                Console.WriteLine("Received UDP: {0} bytes from {1}:{2}", recvBuffer.Length, listenEP.Address.ToString(), listenEP.Port.ToString());

                if(listenEP.Address.ToString() == "127.0.0.1" && listenEP.Port != mediationClientPort){
                    localAppPort = listenEP.Port;
                }

                if(listenEP.Address.ToString() == intendedIP){
                    Console.WriteLine("pog");
                    holePunchReceivedCount++;
                    if(holePunchReceivedCount >= 10 && !connected){
                        try{
                            tcpClientStream.Close();
                            tcpClientThread.Interrupt();
                            tcpClient.Close();
                        }
                        catch(Exception e){
                            Console.WriteLine(e);
                        }

                        connected = true;
                    }
                }

                String receivedIP = "";
                int receivedPort = 0;

                if(listenEP.Address.ToString() == "150.136.166.80"){
                    String[] msgArray = Encoding.ASCII.GetString(recvBuffer).Split(":");

                    receivedIP = msgArray[0];
                    receivedPort = 0;
                    if(msgArray.Length > 1){
                        receivedPort = int.Parse(msgArray[1]);
                    }
                }

                if(receivedIP == intendedIP && holePunchReceivedCount < 10){
                    intendedPort = receivedPort;
                    Console.WriteLine(intendedIP);
                    Console.WriteLine(intendedPort);
                    if(intendedPort != 0){
                        byte[] sendBuffer = new byte[1500];
                        sendBuffer = Encoding.ASCII.GetBytes("check");
                        udpClient.Send(sendBuffer, sendBuffer.Length, new IPEndPoint(IPAddress.Parse(intendedIP), intendedPort));
                        Console.WriteLine("punching");
                    }
                }

                if(connected && receivedIP != "hi" && listenEP.Address.ToString() == "127.0.0.1"){
                    String recvStr = Encoding.ASCII.GetString(recvBuffer);
                    /*
                    int splitPos = recvStr.IndexOf("end");
                    int removeLength = recvStr.Length - splitPos;
                    if(splitPos > 0){
                        recvStr.Remove(splitPos, removeLength);
                        recvBuffer = Encoding.ASCII.GetBytes(recvStr);
                    }
                    */
                    udpClient.Send(recvBuffer, recvBuffer.Length, new IPEndPoint(IPAddress.Parse(intendedIP), intendedPort));
                    Console.WriteLine("huh");
                }

                if(connected && receivedIP != "hi" && listenEP.Address.ToString() == intendedIP){
                    udpClient.Send(recvBuffer, recvBuffer.Length, new IPEndPoint(IPAddress.Parse("127.0.0.1"), localAppPort));
                    Console.WriteLine("huh 2");
                }
            }
        }

        public void UdpServerListenLoop(){
            IPEndPoint listenEP = new IPEndPoint(IPAddress.IPv6Any, mediationClientPort);
            while(true){
                Console.WriteLine(mapping.Count);
                byte[] recvBuffer = udpClient.Receive(ref listenEP);

                mostRecentEP = listenEP;

                Console.WriteLine("Received UDP: {0} bytes from {1}:{2}", recvBuffer.Length, listenEP.Address.ToString(), listenEP.Port.ToString());

                if(listenEP.Address.ToString() != "127.0.0.1" && listenEP.Port != mediationClientPort){
                    localAppPort = listenEP.Port;
                }

                if(!connectedClients.Exists(element => element.Address.ToString() == listenEP.Address.ToString()) && listenEP.Address.ToString() == intendedIP){
                    connectedClients.Add(listenEP);
                    Console.WriteLine("added {0}:{1} to list", listenEP.Address.ToString(), listenEP.Port.ToString());
                }

                if(listenEP.Address.ToString() == intendedIP){
                    Console.WriteLine("pog");
                    holePunchReceivedCount++;
                    if(holePunchReceivedCount >= 10 && !connected){
                        connected = true;
                    }
                }

                String receivedIP = "";
                int receivedPort = 0;

                if(listenEP.Address.ToString() == "150.136.166.80"){
                    String[] msgArray = Encoding.ASCII.GetString(recvBuffer).Split(":");

                    receivedIP = msgArray[0];
                    receivedPort = 0;
                    if(msgArray.Length > 1){
                        receivedPort = int.Parse(msgArray[1]);
                    }

                    if(msgArray.Length > 2){
                        String type = msgArray[2];
                        if(type == "clientreq" && intendedIP != receivedIP && intendedPort != receivedPort){
                            intendedIP = receivedIP;
                            intendedPort = receivedPort;
                            holePunchReceivedCount = 0;
                        }
                    }
                }


                if(receivedIP == intendedIP && holePunchReceivedCount < 10){
                    intendedPort = receivedPort;
                    Console.WriteLine(intendedIP);
                    Console.WriteLine(intendedPort);
                    if(intendedPort != 0){
                        byte[] sendBuffer = new byte[1500];
                        sendBuffer = Encoding.ASCII.GetBytes("check");
                        udpClient.Send(sendBuffer, sendBuffer.Length, new IPEndPoint(IPAddress.Parse(intendedIP), intendedPort));
                        Console.WriteLine("punching");
                    }
                }

                if(connected && receivedIP != "hi" && listenEP.Address.ToString() == "127.0.0.1"){
                    String recvStr = Encoding.ASCII.GetString(recvBuffer);
                    int splitPos = recvStr.IndexOf("end");
                    int removeLength = recvStr.Length - splitPos;
                    if(splitPos > 0){
                        String[] recvSplit = recvStr.Split("end");
                        String endpointStr = recvSplit[1];
                        String[] endpointSplit = endpointStr.Split(":");
                        String address = endpointSplit[0];
                        int port = int.Parse(endpointSplit[1]);
                        Console.WriteLine($"{address}:{port}");

                        recvStr.Remove(splitPos, removeLength);
                        //recvBuffer = Encoding.ASCII.GetBytes(recvStr);

                        IPEndPoint destEP = mapping[new IPEndPoint(IPAddress.Parse(address), port)];

                        Console.WriteLine(destEP);
                        
                        udpClient.Send(recvBuffer, recvBuffer.Length, destEP);
                    }
                    Console.WriteLine("huh");
                }

                foreach(var client in connectedClients){
                    if(connected && receivedIP != "hi" && listenEP.Address.ToString() == client.Address.ToString()){
                        udpClient.Send(recvBuffer, recvBuffer.Length, programEndpoint);
                        Console.WriteLine("huh 2");
                    }
                }
            }
        }

        public void TcpListenLoop(){
            while(tcpClient.Connected){
                try{
                    byte[] recvBuffer = new byte[tcpClient.ReceiveBufferSize];
                    int bytesRead = tcpClientStream.Read(recvBuffer, 0, tcpClient.ReceiveBufferSize);
                    Console.WriteLine("Received: " + Encoding.ASCII.GetString(recvBuffer, 0, bytesRead));
                }
                catch(Exception e){
                    Console.WriteLine(e);
                }
            }
        }
    }
}
