using Newtonsoft.Json;

namespace UnityLauncher.Editor.UTP
{
    public class UtpTestStatusMessage : UtpMessageBase
    {
        public string Name { get; set; }
        public TestStateEnum State { get; set; }
        [JsonProperty("message", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Message { get; set; }
        [JsonProperty("duration", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? Duration { get; set; }
        [JsonProperty("durationMicroseconds", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? DurationMicroseconds { get; set; }
    }
}