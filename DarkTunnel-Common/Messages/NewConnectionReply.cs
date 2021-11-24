using System;
using System.IO;
using System.Text;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.NEW_CONNECTION_REPLY)]
    public class NewConnectionReply : IMessage
    {
        public int id;
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
        }
    }
}
