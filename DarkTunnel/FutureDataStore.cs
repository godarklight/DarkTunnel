using System;
using System.Collections.Generic;
using DarkTunnel.Common.Messages;

namespace DarkTunnel
{
    public class FutureDataStore
    {
        private SortedList<long, Data> futureData = new SortedList<long, Data>();

        public void StoreData(Data d)
        {
            if (!futureData.ContainsKey(d.streamPos))
            {
                futureData.Add(d.streamPos, d);
            }
            else
            {
                Data test = futureData[d.streamPos];
                if (d.tcpData.Length > test.tcpData.Length)
                {
                    futureData[d.streamPos] = d;
                }
            }
        }

        public Data GetData(long currentRecvPos)
        {
            //Deletes all data from the past
            SetReceivePos(currentRecvPos);

            if (futureData.Count > 0)
            {
                Data canditate = futureData.Values[0];
                //We have current data!
                if (canditate.streamPos <= currentRecvPos)
                {
                    futureData.Remove(canditate.streamPos);
                    return canditate;
                }
            }
            return null;
        }

        private void SetReceivePos(long currentRecvPos)
        {
            while (futureData.Count > 0)
            {
                Data d = futureData.Values[0];
                if (d.streamPos + d.tcpData.Length <= currentRecvPos)
                {
                    futureData.Remove(d.streamPos);
                }
                else
                {
                    return;
                }
            }
        }
    }
}