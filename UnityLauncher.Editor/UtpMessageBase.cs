using System;

namespace UnityLauncher.Editor {
    public class UtpMessageBase
    {
        public int Version { get; set; }
        public string Type { get; set; }
        public string Phase { get; set; }
        public long Time { get; set; }
        public int ProcessId { get; set; }
    }
}