using System;
using System.IO;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.MASTER_PRINT_CONSOLE)]
    public class PrintConsole : INodeMessage
    {
        public int id;
        public string message;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(message);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            message = reader.ReadString();
        }
    }
}
