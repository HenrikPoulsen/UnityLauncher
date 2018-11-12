using System.Collections.Generic;

namespace UnityLauncher.Editor.UTP
{
    public class UtpTestPlanMessage : UtpMessageBase
    {
        public List<string> Tests { get; set; }
    }
}