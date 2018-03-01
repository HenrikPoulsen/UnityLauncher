using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityLauncher.Core;

namespace UnityLauncher.Editor
{
    public static class UnityLauncher
    {
        private const int LinesToSave = 20;
        private static Queue<string> _lastLines = new Queue<string>(LinesToSave);

        
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
            var timeoutStopwatch = new Stopwatch();
            timeoutStopwatch.Start();
            using (var stream = new StreamReader(fs))
            {
                var waitingForDeath = false;
                var waitingForDeathCounter = 10;
                var failureMessagePrinted = false;
                while (true)
                {
                    var line = stream.ReadLine();
                    if (line != null)
                    {
                        StashLine(line);
                    }
                    if (IsFailureMessage(line))
                    {
                        failureMessagePrinted = true;
                        RunLogger.LogError(line);
                    }
                        
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
                    else if (Program.ExecutionTimeout.HasValue && timeoutStopwatch.ElapsedMilliseconds > Program.ExecutionTimeout.Value * 1000)
                    {
                        if ((Program.Flags & Program.Flag.TimeoutIgnore) != Program.Flag.None)
                        {
                            RunLogger.LogResultInfo($"Execution timed out after {Program.ExecutionTimeout.Value} seconds");
                        }
                        else
                        {
                            RunLogger.LogResultError($"Execution timed out after {Program.ExecutionTimeout.Value} seconds. Failing run");
                        }

                        process.Kill();
                        return ProcessResult.UseExitCode;
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
                            StashLine(line);
                            if (IsFailureMessage(line))
                                failureMessagePrinted = true;
                            if (!IsExitMessage(line))
                                continue;

                            if (failureMessagePrinted)
                                continue;
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }

                        if (failureMessagePrinted)
                        {
                            RunLogger.LogResultError("The unity process has exited, but a log failure message was detected, flagging run as failed.");
                            return ProcessResult.FailedRun;
                        }
                        var writer = new StringWriter();
                        writer.WriteLine($"The unity process has exited, but did not print the proper cleanup, did it crash? Marking as failed. The last {LinesToSave} lines of the log was:");
                        foreach(var entry in _lastLines)
                        {
                            writer.WriteLine($"  {entry}");
                        }
                        RunLogger.LogResultError(writer.ToString());
                        return ProcessResult.FailedRun;
                    }
                }
            }

        }

        private static void StashLine(string line)
        {
            if (_lastLines.Count >= LinesToSave)
                _lastLines.Dequeue();
            _lastLines.Enqueue(line);
        }

        private static bool IsExitMessage(IEnumerable<string> readLines)
        {
            return readLines.Any(IsExitMessage);
        }

        private static bool IsExitMessage(string line)
        {
            switch (line)
            {
                case "Cleanup mono":
                    return true;
                case "Exiting batchmode successfully now!":
                    return true;
            }

            return false;
        }

        private static bool IsFailureMessage(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            switch (line)
            {
                case "Error building Player because scripts had compiler errors":
                    return true;
                case "Failed to build player.":
                    return true;
                case "Aborting batchmode due to failure:":
                    return true;
                case "No tests were executed":
                    return true;
                case "Unhandled Exception: System.InvalidOperationException: C++ code builder is unable to build C++ code. In order to build C++ code for Windows Desktop, you must have one of these installed:":
                    return true;
            }

            if (line.StartsWith("DirectoryNotFoundException: Could not find a part of the path"))
                return true;
            return false;
        }
    }
}