using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.PING_REQUEST)]
    public class PingRequest : INodeMessage
    {
        public int id;
        public long sendTime;
        public String ep;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(sendTime);
            writer.Write(ep);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            sendTime = reader.ReadInt64();
            ep = reader.ReadString();
        }
    }
}
