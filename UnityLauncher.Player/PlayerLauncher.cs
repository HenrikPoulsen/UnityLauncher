using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityLauncher.Core;

namespace UnityLauncher.Player
{
    public class PlayerLauncher
    {
        public static RunResult Run(string args)
        {
            File.Delete(Program.LogFile);
            RunLogger.LogInfo($"Will now run:\n{Program.Executable} {args}");
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Program.Executable,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var started = process.Start();
            RunLogger.LogInfo($"Process spawned with pid: {process.Id}");
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
            var timeoutStopwatch = new Stopwatch();
            timeoutStopwatch.Start();
            using (var stream = new StreamReader(fs))
            {
                var waitingForDeath = false;
                var failureMessagePrinted = false;
                while (true)
                {
                    var line = stream.ReadLine();

                    if (IsFailureMessage(line))
                    {
                        failureMessagePrinted = true;
                        RunLogger.LogError(line);
                    }
                        
                    if (IsExitMessage(line))
                    {
                        RunLogger.LogInfo("Found shutdown log print. Waiting 10 seconds for process to quit");
                        waitingForDeath = true;
                    }
                    else if (Program.ExecutionTimeout.HasValue && timeoutStopwatch.ElapsedMilliseconds > Program.ExecutionTimeout.Value * 1000)
                    {
                        if ((Program.Flags & Program.Flag.TimeoutIgnore) != Program.Flag.None)
                        {
                            RunLogger.LogResultInfo($"Execution timed out after {Program.ExecutionTimeout.Value * 1000} seconds");
                        }
                        else
                        {
                            RunLogger.LogResultError($"Execution timed out after {Program.ExecutionTimeout.Value * 1000} seconds. Failing run");
                        }

                        process.Kill();
                        return ProcessResult.IgnoreExitCode;
                    }

                    if (process.HasExited)
                    {
                        if (waitingForDeath)
                        {
                            RunLogger.LogInfo("Player has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }

                        while ((line = stream.ReadLine()) != null)
                        {
                            if (IsFailureMessage(line))
                                failureMessagePrinted = true;
                            if (!IsExitMessage(line))
                                continue;
                            RunLogger.LogInfo("Player has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }

                        if (failureMessagePrinted)
                        {
                            RunLogger.LogResultError("The process has exited, but a log failure message was detected, flagging run as failed.");
                            return ProcessResult.FailedRun;
                        }
                        
                        return ProcessResult.UseExitCode;
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
            return false;
        }

        private static bool IsFailureMessage(string line)
        {
            if (line == "A crash has been intercepted by the crash handler. For call stack and other details, see the latest crash report generated in:")
                return true;
            return false;
        }
    }
}
