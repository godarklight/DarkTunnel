using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.DISCONNECT)]
    public class Disconnect : IMessage
    {
        public string reason;
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(reason);
        }
        public void Deserialize(BinaryReader reader)
        {
            reason = reader.ReadString();
        }
    }
}
