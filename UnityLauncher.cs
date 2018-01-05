using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityLogWrapper
{
    public static class UnityLauncher
    {
        public enum RunResult
        {
            Failure,
            Success,
            FailedToStart
        }

        public enum ProcessResult
        {
            UseExitCode,
            IgnoreExitCode,
            FailedRun
        }
        public static RunResult Run(string args)
        {
            File.Delete(Program.LogFile);
            RunLogger.LogInfo($"Will now run:\n{Program.UnityExecutable} {args}");
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Program.UnityExecutable,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var started = process.Start();
            RunLogger.LogInfo($"Unity process spawned with pid: {process.Id}");
            StreamReader fs;
            var processResult = CheckForCleanupEntry(process);
            process.WaitForExit();
            if (processResult == ProcessResult.FailedRun)
            {
                RunLogger.LogInfo("CheckForCleanupEntry flagged a failed run. Aborting");
                return RunResult.Failure;
            }
            RunLogger.LogInfo($"Exeuction Done! Exit code: {process.ExitCode}");


            if (process.ExitCode != 0)
            {
                if (processResult == ProcessResult.IgnoreExitCode)
                {
                    RunLogger.LogInfo("Exit code not 0, but this was expected in this case. Ignoring it");
                }
                else
                {
                    RunLogger.LogError("Exit code not 0, run failed.");
                    return RunResult.Failure;    
                }                  
            
            }
            return RunResult.Success;
        }

        private static ProcessResult CheckForCleanupEntry(Process process)
        {
            var fs = new FileStream(Program.LogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using (var stream = new StreamReader(fs))
            {
                var waitingForDeath = false;
                var waitingForDeathCounter = 10;
                while (true)
                {
                    var line = stream.ReadLine();
                    if (IsExitMessage(line))
                    {
                        RunLogger.LogInfo("Found editor shutdown log print. Waiting 10 seconds for process to quit");
                        waitingForDeath = true;
                    }
                    if (waitingForDeath)
                    {
                        Thread.Sleep(1000);
                        if (waitingForDeathCounter-- <= 0)
                        {
                            RunLogger.LogInfo("Editor did not quit after 10 seconds. Forcibly quitting and whitelisting the exit code");
                            process.Kill();
                            return ProcessResult.IgnoreExitCode;
                        }
                    }

                    if (process.HasExited)
                    {
                        if (waitingForDeath)
                        {
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }

                        while ((line = stream.ReadLine()) != null)
                        {
                            if (!IsExitMessage(line))
                                continue;
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }
                        RunLogger.LogResultError("The unity process has exited, but did not print the proper cleanup, did it crash? Marking as failed");
                        return ProcessResult.FailedRun;
                    }
                }
            }

        }

        private static bool IsExitMessage(IEnumerable<string> readLines)
        {
            return readLines.Any(IsExitMessage);
        }

        private static bool IsExitMessage(string line)
        {
            if (line == "Cleanup mono")
                return true;
            if (line == "Exiting batchmode successfully now!")
                return true;
            return false;
        }
    }
}