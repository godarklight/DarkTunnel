using System;

namespace DarkTunnel.Common
{
    public enum MessageType
    {
        //HEARTBEAT is not used, ACKs do the job of keeping the UDP connection alive
        HEARTBEAT = 0,
        DISCONNECT = 1,
        NEW_CONNECTION_REQUEST = 10,
        NEW_CONNECTION_REPLY = 11,
        DATA = 30,
        ACK = 31,
        MASTER_SERVER_INFO_REQUEST = 100,
        MASTER_SERVER_INFO_REPLY = 101,
        MASTER_SERVER_PUBLISH_REQUEST = 110,
        MASTER_SERVER_PUBLISH_REPLY = 111,
        MASTER_PRINT_CONSOLE = 120,
    }
}
