using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnityLauncher.Editor.UTP {
    public class UtpLogEntryMessage : UtpMessageBase
    {
        public string Message { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public LogSeverity Severity { get; set; }
        public string Stacktrace { get; set; }
        public int Line { get; set; }
        public string File { get; set; }
    }
}