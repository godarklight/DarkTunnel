using System;
using System.Collections.Generic;
using System.Net;

namespace DarkTunnel.Master
{
    class PublishEntry
    {
        public int secret;
        public long lastPublishTime;
        public List<IPEndPoint> endpoints = new List<IPEndPoint>();
    }
}