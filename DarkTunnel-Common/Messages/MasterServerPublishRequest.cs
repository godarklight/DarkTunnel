using System;
using System.IO;
using System.Text;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.MASTER_SERVER_PUBLISH_REQUEST)]
    public class MasterServerPublishRequest : IMessage
    {
        public int id;
        public int secret;
        public int localPort;
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(secret);
            writer.Write(localPort);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            secret = reader.ReadInt32();
            localPort = reader.ReadInt32();
        }
    }
}
