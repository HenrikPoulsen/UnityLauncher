using System;

namespace UnityLauncher.Editor {
    public class UtpLogEntryMessage : UtpMessageBase
    {
        public string Message { get; set; }
        public string Severity { get; set; }
        public string Stacktrace { get; set; }
        public int Line { get; set; }
        public string File { get; set; }
    }
}