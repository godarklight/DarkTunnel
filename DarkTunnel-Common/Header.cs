using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using DarkTunnel.Common.Messages;

//This class is currently not thread safe.

namespace DarkTunnel.Common
{
    public static class Header
    {
        private static bool loaded = false;
        private static Dictionary<MessageType, Type> mt2t = new Dictionary<MessageType, Type>();
        private static Dictionary<Type, MessageType> t2mt = new Dictionary<Type, MessageType>();
        private static byte[] buildBytes = new byte[1496];
        private static byte[] sendBytes = new byte[1500];

        public static byte[] FrameMessage(IMessage message)
        {
            if (!loaded)
            {
                loaded = true;
                Load();
            }

            using (MemoryStream ms = new MemoryStream(buildBytes, true))
            {
                using (BinaryWriter bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
                {
                    message.Serialize(bw);
                }
                short type = (short)t2mt[message.GetType()];
                short length = (short)ms.Position;
                BitConverter.GetBytes(type).CopyTo(sendBytes, 4);
                BitConverter.GetBytes(length).CopyTo(sendBytes, 6);
                if (length > 0)
                {
                    Array.Copy(buildBytes, 0, sendBytes, 8, length);
                }
            }
            return sendBytes;
        }

        public static IMessage DeframeMessage(BinaryReader br)
        {
            if (!loaded)
            {
                loaded = true;
                Load();
            }
            if (br.ReadByte() != 'D' || br.ReadByte() != 'T' || br.ReadByte() != '0' || br.ReadByte() != '1')
            {
                return null;
            }
            short type = br.ReadInt16();
            short length = br.ReadInt16();

            if (!Enum.IsDefined(typeof(MessageType), (int)type))
            {
                return null;
            }
            if (length != br.BaseStream.Length - 8)
            {
                return null;
            }
            Type messageType = mt2t[(MessageType)type];
            IMessage message = (IMessage)Activator.CreateInstance(messageType);
            if (length > 0)
            {
                message.Deserialize(br);
            }
            return message;
        }

        public static void Load()
        {
            //Only need to write this once
            sendBytes[0] = (byte)'D';
            sendBytes[1] = (byte)'T';
            sendBytes[2] = (byte)'0';
            sendBytes[3] = (byte)'1';
            
            //Find all message types
            foreach (Type t in Assembly.GetExecutingAssembly().GetExportedTypes())
            {
                MessageTypeAttribute mta = t.GetCustomAttribute<MessageTypeAttribute>();
                if (mta != null)
                {
                    t2mt[t] = mta.type;
                    mt2t[mta.type] = t;
                }
            }
        }
    }
}
