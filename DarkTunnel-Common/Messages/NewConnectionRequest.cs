using System;
using System.IO;
using System.Text;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.NEW_CONNECTION_REQUEST)]
    public class NewConnectionRequest : INodeMessage
    {
        public int id;
        public int protocol_version;
        public int downloadRate;

        public int GetID()
        {
            return id;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(protocol_version);
            writer.Write(downloadRate);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            protocol_version = reader.ReadInt32();
            downloadRate = reader.ReadInt32();
        }
    }
}
