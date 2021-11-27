using System;
using System.IO;
using System.Text;

namespace DarkTunnel.Common.Messages
{
    [MessageTypeAttribute(MessageType.MASTER_SERVER_PUBLISH_REPLY)]
    public class MasterServerPublishReply : IMessage
    {
        public int id;
        public bool status;
        public string message;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(id);
            writer.Write(status);
            writer.Write(message);
        }
        public void Deserialize(BinaryReader reader)
        {
            id = reader.ReadInt32();
            status = reader.ReadBoolean();
            message = reader.ReadString();
        }
    }
}
