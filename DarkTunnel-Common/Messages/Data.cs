using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.DATA)]
    public class Data : INodeMessage
    {
        public int id;
        public long streamPos;
        public long streamAck;
        public byte[] tcpData;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(streamPos);
            writer.Write(streamAck);
            writer.Write((short)tcpData.Length);
            writer.Write(tcpData);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            streamPos = reader.ReadInt64();
            streamAck = reader.ReadInt64();
            int length = reader.ReadInt16();
            tcpData = reader.ReadBytes(length);
        }
    }
}
