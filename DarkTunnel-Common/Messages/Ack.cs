using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.ACK)]
    public class Ack : INodeMessage
    {
        public int id;
        public long streamAck;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(streamAck);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            streamAck = reader.ReadInt64();
        }
    }
}
