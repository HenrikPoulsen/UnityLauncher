using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityLauncher.Editor.UTP
{
    public class UtpActionMessage : UtpMessageBase
    {
        public string Name { get; set; }
        public string Description { get; set; }
        [JsonProperty("duration", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long Duration { get; set; }
        [JsonProperty("errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> Errors { get; set; }
    }
}