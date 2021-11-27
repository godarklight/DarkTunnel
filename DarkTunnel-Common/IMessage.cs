using System;
using System.IO;

namespace DarkTunnel.Common
{
    public interface IMessage
    {
        void Serialize(BinaryWriter writer);
        void Deserialize(BinaryReader reader);
    }
}
