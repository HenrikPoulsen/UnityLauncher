using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnityLauncher.Editor.UTP {
    public class UtpMessageBase
    {
        public int Version { get; set; }
        public string Type { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public UtpPhase Phase { get; set; }
        public long Time { get; set; }
        public int ProcessId { get; set; }
    }
}