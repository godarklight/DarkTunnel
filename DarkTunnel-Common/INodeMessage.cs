using System;
using System.IO;

namespace DarkTunnel.Common
{
    public interface INodeMessage : IMessage
    {
        int GetID();
    }
}
