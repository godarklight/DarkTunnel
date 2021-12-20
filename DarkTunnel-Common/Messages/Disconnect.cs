using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.DISCONNECT)]
    public class Disconnect : INodeMessage
    {
        public int id;
        public string reason;
        public String ep;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(reason);
            writer.Write(ep);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            reason = reader.ReadString();
            ep = reader.ReadString();
        }
    }
}
