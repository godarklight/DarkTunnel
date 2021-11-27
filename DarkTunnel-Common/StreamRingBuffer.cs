//This class currently has a bug where after 18.5 petabytes it will crap out. Luckily I don't care!
//Read is multithread safe, write is not.

using System;

namespace DarkTunnel.Common
{
    public class StreamRingBuffer
    {
        private byte[] internalBuffer;

        public StreamRingBuffer(int size)
        {
            internalBuffer = new byte[size];
        }

        public long StreamReadPos
        {
            private set;
            get;
        }

        public long StreamWritePos
        {
            private set;
            get;
        }

        public int AvailableRead
        {
            get
            {
                return (int)(StreamWritePos - StreamReadPos);
            }
        }

        public int AvailableWrite
        {
            get
            {
                return internalBuffer.Length - 1 - AvailableRead;
            }
        }

        public void Write(byte[] source, int offset, int size)
        {
            if (size > AvailableWrite)
            {
                throw new ArgumentOutOfRangeException("Buffer full");
            }
            //Because this is a ring buffer we have to "wrap around", so two writes.

            int firstWriteLength = internalBuffer.Length - (int)(StreamWritePos % internalBuffer.Length);
            if (firstWriteLength > size)
            {
                firstWriteLength = size;
            }
            Array.Copy(source, offset, internalBuffer, StreamWritePos % internalBuffer.Length, firstWriteLength);

            int secondWriteLength = size - firstWriteLength;
            if (secondWriteLength > 0)
            {
                Array.Copy(source, offset + firstWriteLength, internalBuffer, 0, secondWriteLength);
            }

            StreamWritePos += size;
        }

        public int Read(byte[] dest, int offset, long ReadPos, int size)
        {
            long readDelta = ReadPos - StreamReadPos;
            if (readDelta < 0 || AvailableRead - readDelta - size < 0)
            {
                throw new ArgumentOutOfRangeException("Stream trying to read from a non-written area.");
            }            
            int firstRead = internalBuffer.Length - (int)(ReadPos % internalBuffer.Length);
            if (firstRead > size)
            {
                firstRead = size;
            }
            int secondRead = size - firstRead;
            Array.Copy(internalBuffer, ReadPos % internalBuffer.Length, dest, offset, firstRead);
            if (secondRead > 0)
            {
                Array.Copy(internalBuffer, 0, dest, offset + firstRead, secondRead);
            }
            return size;
        }

        public void MarkFree(long position)
        {
            if (position < StreamReadPos)
            {
                throw new ArgumentOutOfRangeException("Stream attempting to free a non-written area.");
            }
            StreamReadPos = position;
        }
    }
}
