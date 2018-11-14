using System.Collections.Generic;

namespace UnityLauncher.Editor.UTP
{
    public class UtpAssemblyCompilationErrorsMessage : UtpMessageBase
    {
        public string Assembly { get; set; }
        public List<string> Errors { get; set; }
    }
}