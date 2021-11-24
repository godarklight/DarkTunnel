using System;
using System.IO;
using System.Text;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.MASTER_SERVER_INFO_REQUEST)]
    public class MasterServerInfoRequest : IMessage
    {
        public int server;
        public int client;
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(server);
            writer.Write(client);
        }
        public void Deserialize(BinaryReader reader)
        {
            server = reader.ReadInt32();
            client = reader.ReadInt32();
        }
    }
}
