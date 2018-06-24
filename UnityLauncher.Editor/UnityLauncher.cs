using System;
using System.Collections.Generic;
using System.Data;
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
            RunLogger.LogInfo($"Will now run:\n{Program.UnityExecutable} {args}");
            ProcessResult processResult;
            const int retryLimit = 50;
            var retryCount = 0;
            Process process;
            do
            {
                DeletedLogFile();
                
                if (retryCount != 0)
                {
                    RunLogger.LogWarning($"Previous run timed out. Trying again in 30 seconds. This is attempt {retryCount}/{retryLimit}");
                    Thread.Sleep(30000);
                }
                process = new Process()
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
                processResult = CheckForCleanupEntry(process);
                process.WaitForExit();
                retryCount++;
            } while (processResult == ProcessResult.Timeout && retryCount < retryLimit);

            if (processResult == ProcessResult.Timeout)
            {
                RunLogger.LogResultError("The run has timed out and exhausted the allowed retry count. Failing run");
                return RunResult.Failure;
            }
            
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

        private static void DeletedLogFile()
        {
            var retryLimit = 50;
            var retryCount = 0;
            do
            {
                retryCount++;
                try
                {
                    File.Delete(Program.LogFile);
                }
                catch (IOException)
                {
                    RunLogger.LogWarning($"Failed to delete logfile. Retrying in 10 seconds. Attempt {retryCount}/{retryLimit}");
                    Thread.Sleep(10000);
                }
                
            } while (retryCount < retryLimit && File.Exists(Program.LogFile));
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
                var timeoutMessagePrinted = false;
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
                        RunLogger.LogError($"Failure message in the log: {line}");
                    }
                    
                    if (IsTimeoutMessage(line))
                    {
                        timeoutMessagePrinted = true;
                        RunLogger.LogError($"Timeout message in the log: {line}");
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
                            if (timeoutMessagePrinted)
                            {
                                // Hopefully temporary hack to work around potential packman timeout issues.
                                // So if we detect a timeout error in the log then we may want to just retry the entire run
                                RunLogger.LogError("Editor did not quit after 10 seconds, but was also timed out. Forcibly quitting and retrying");
                                process.Kill();
                                return ProcessResult.Timeout;
                            }
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
                            process.Kill();
                            return ProcessResult.IgnoreExitCode;
                        }
                        
                        RunLogger.LogResultError($"Execution timed out after {Program.ExecutionTimeout.Value} seconds. Failing run");

                        process.Kill();
                        return ProcessResult.FailedRun;
                    }

                    if (process.HasExited)
                    {
                        while ((line = stream.ReadLine()) != null)
                        {
                            StashLine(line);
                            if (IsTimeoutMessage(line))
                            {
                                timeoutMessagePrinted = true;
                                RunLogger.LogError($"Timeout message in the log: {line}");
                            }
                            if (IsFailureMessage(line))
                                failureMessagePrinted = true;
                            if (!IsExitMessage(line))
                                continue;

                            if (failureMessagePrinted)
                                continue;
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            return ProcessResult.UseExitCode;
                        }
                        
                        if (timeoutMessagePrinted)
                        {
                            // Hopefully temporary hack to work around potential packman timeout issues.
                            // So if we detect a timeout error in the log then we may want to just retry the entire run
                            RunLogger.LogError("The unity process has exited, but a timeout message was found, flagging run as timed out.");
                            return ProcessResult.Timeout;
                        }
                        if (waitingForDeath)
                        {
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

        private static bool IsTimeoutMessage(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            
            if (line.Contains("connect ETIMEDOUT"))
                return true;
            if (line.Contains("Cannot connect to registry"))
                return true;
            if (line.Contains("failed to fetch from registry:"))
                return true;
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
                case "Fatal Error! It looks like another Unity instance is running with this project open.":
                    return true;
                case "Multiple Unity instances cannot open the same project.":
                    return true;
            }

            if (line.StartsWith("DirectoryNotFoundException: Could not find a part of the path"))
                return true;
            return false;
        }
    }
}