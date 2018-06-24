using System;

namespace UnityLauncher.Core 
{
    public enum ProcessResult
    {
        None,
        UseExitCode,
        Timeout,
        IgnoreExitCode,
        FailedRun
    }
}