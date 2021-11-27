using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.MASTER_SERVER_INFO_REPLY)]
    public class MasterServerInfoReply : IMessage
    {
        public int server;
        public int client;
        public bool status;
        public string message;
        public List<IPEndPoint> endpoints = new List<IPEndPoint>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(server);
            writer.Write(client);
            writer.Write(status);
            writer.Write(message);
            writer.Write(endpoints.Count);
            foreach (IPEndPoint endpoint in endpoints)
            {
                writer.Write(endpoint.Address.ToString());
                writer.Write(endpoint.Port);
            }
        }
        public void Deserialize(BinaryReader reader)
        {
            endpoints.Clear();
            server = reader.ReadInt32();
            client = reader.ReadInt32();
            status = reader.ReadBoolean();
            message = reader.ReadString();
            int endpointNum = reader.ReadInt32();
            for (int i = 0; i < endpointNum; i++)
            {
                IPAddress address = IPAddress.Parse(reader.ReadString());
                int port = reader.ReadInt32();
                IPEndPoint endpoint = new IPEndPoint(address, port);
                endpoints.Add(endpoint);
            }
        }
    }
}
