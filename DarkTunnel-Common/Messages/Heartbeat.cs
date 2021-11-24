using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.HEARTBEAT)]
    public class Heartbeat : IMessage
    {
        public void Serialize(BinaryWriter writer)
        {
        }
        public void Deserialize(BinaryReader reader)
        {
        }
    }
}
