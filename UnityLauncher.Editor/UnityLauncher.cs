using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityLauncher.Core;
using UnityLauncher.Editor.UTP;

namespace UnityLauncher.Editor
{
    public static class UnityLauncher
    {
        private const int LinesToSave = 20;
        private static Queue<string> _lastLines = new Queue<string>(LinesToSave);
        static System.IO.StreamWriter OwnLog;

        
        public static RunResult Run(string args)
        {
            RunLogger.LogInfo($"Will now run:\n{Program.UnityExecutable} {args}");
            OwnLog = new StreamWriter(Program.LogFile + ".timings.log");
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
            
            OwnLog.Close();

            RunLogger.LogInfo($"Execution Done! Exit code: {process.ExitCode}");
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


            if (process.ExitCode != Program.ExpectedExitCode)
            {
                if (processResult == ProcessResult.IgnoreExitCode)
                {
                    RunLogger.LogInfo($"Exit code not {Program.ExpectedExitCode}, but this was expected in this case. Ignoring it");
                }
                else
                {
                    RunLogger.LogError($"Exit code not {Program.ExpectedExitCode}, run failed.");
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
                    CheckLastTestPrint();
                    var line = stream.ReadLine();
                    if (line != null)
                    {
                        StashLine(line);
                    }
                    else
                    {
                        // Let's chill if there is nothing new
                        Thread.Sleep(10);
                        continue;
                    }

                    if (HandleUtpMessage(line))
                        continue;
                    if (IsFailureMessage(line))
                    {
                        failureMessagePrinted = true;
                        RunLogger.LogError($"Failure message in the log: {line}");
                    }
                    
                    if (IsInstabilityMessage(line))
                    {
                        timeoutMessagePrinted = true;
                        RunLogger.LogError($"Instability message in the log: {line}");
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
                                ProcessWarningsAndErrors();
                                return ProcessResult.Timeout;
                            }
                            RunLogger.LogInfo("Editor did not quit after 10 seconds. Forcibly quitting and whitelisting the exit code");
                            process.Kill();
                            if (!ProcessWarningsAndErrors())
                                return ProcessResult.FailedRun;
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
                        ProcessWarningsAndErrors();
                        return ProcessResult.FailedRun;
                    }

                    if (process.HasExited)
                    {
                        while ((line = stream.ReadLine()) != null)
                        {
                            StashLine(line);
                            if (IsInstabilityMessage(line))
                            {
                                timeoutMessagePrinted = true;
                                RunLogger.LogError($"Instability message in the log: {line}");
                            }
                            if (IsFailureMessage(line))
                                failureMessagePrinted = true;
                            if (!IsExitMessage(line))
                                continue;

                            if (timeoutMessagePrinted)
                                continue;

                            if (failureMessagePrinted)
                                continue;
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            if (!ProcessWarningsAndErrors())
                                return ProcessResult.FailedRun;
                            return ProcessResult.UseExitCode;
                        }
                        
                        if (timeoutMessagePrinted)
                        {
                            // Hopefully temporary hack to work around potential packman timeout issues.
                            // So if we detect a timeout error in the log then we may want to just retry the entire run
                            RunLogger.LogError("The unity process has exited, but a timeout message was found, flagging run as timed out.");
                            ProcessWarningsAndErrors();
                            return ProcessResult.Timeout;
                        }
                        if (waitingForDeath)
                        {
                            RunLogger.LogInfo("Unity has exited cleanly.");
                            if (!ProcessWarningsAndErrors())
                                return ProcessResult.FailedRun;
                            return ProcessResult.UseExitCode;
                        }


                        if (failureMessagePrinted)
                        {
                            RunLogger.LogResultError("The unity process has exited, but a log failure message was detected, flagging run as failed.");
                            ProcessWarningsAndErrors();
                            return ProcessResult.FailedRun;
                        }
                        var writer = new StringWriter();
                        writer.WriteLine($"The unity process has exited, but did not print the proper cleanup, did it crash? Marking as failed. The last {LinesToSave} lines of the log was:");
                        foreach(var entry in _lastLines)
                        {
                            writer.WriteLine($"  {entry}");
                        }
                        RunLogger.LogResultError(writer.ToString());
                        ProcessWarningsAndErrors();
                        return ProcessResult.FailedRun;
                    }
                }
            }

        }

        private static void CheckLastTestPrint()
        {
            if (testsLeft <= 0)
                return;
            if (lastTestPrint + TimeSpan.FromSeconds(10) > DateTime.UtcNow)
                return;
            RunLogger.LogInfo($"{testsLeft} tests remaining");
            lastTestPrint = DateTime.UtcNow;
        }

        private static bool ProcessWarningsAndErrors()
        {
            var success = true;
            if (Warnings.Any())
            {
                Console.Write("\n");
                RunLogger.LogInfo("Found warnings:");
                var warningsToPrint = Warnings.Count > 30 ? 30 : Warnings.Count;
                for (var i = 0; i < warningsToPrint; i++)
                {
                    RunLogger.LogWarning(Warnings[i]);
                }
            
                if (warningsToPrint < Warnings.Count)
                {
                    RunLogger.LogWarning($"Did not print all warnings. Stopped after {warningsToPrint}. There are {Warnings.Count - warningsToPrint} that have not been printed.");
                }
            
                if ((Program.Flags & Program.Flag.WarningsAsErrors) != Program.Flag.None)
                {
                    success = false;
                    RunLogger.LogResultError("warningsasserrors has been set so marking the run as failed due to warnings");
                }
                Console.Write("\n");
            }
            if (Errors.Any())
            {
                Console.Write("\n");
                RunLogger.LogError("Found errors:");
                var errorsToPrint = Errors.Count > 30 ? 30 : Errors.Count;
                for (var i = 0; i < errorsToPrint; i++)
                {
                    RunLogger.LogError(Errors[i]);
                }

                if (errorsToPrint < Errors.Count)
                {
                    RunLogger.LogError($"Did not print all errors. Stopped after {errorsToPrint}. There are {Errors.Count - errorsToPrint} that have not been printed.");
                }
                success = false;
                Console.Write("\n");
            }
        
            if(!success)
                RunLogger.LogResultError($"Found {Warnings.Count} warnings and {Errors.Count} errors");
            else
                RunLogger.LogResultInfo($"Found {Warnings.Count} warnings and {Errors.Count} errors");
            return success;
        }

        private static DateTime MsToDateTime(long ms)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(ms);
            DateTime startdate = new DateTime(1970, 1, 1) + time;
            return startdate;
        }

        private static void StashLine(string line)
        {
            OwnLog.WriteLine($"{RunLogger.GetTime()}: {line}");
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
            if (string.IsNullOrEmpty(line))
                return false;
            switch (line)
            {
                case "Cleanup mono":
                    return true;
                case "Exiting batchmode successfully now!":
                    return true;
                case "Aborting batchmode due to failure:":
                    return true;
                
            }

            if (line.StartsWith("Exiting without the bug reporter. Application will terminate with return code"))
                return true;
            return false;
        }
        
        static bool isInTest = false;
        static List<UtpLogEntryMessage> logsInTest = new List<UtpLogEntryMessage>();
        private static readonly List<string> Warnings = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static int testsLeft = 0;
        private static DateTime lastTestPrint = DateTime.UtcNow;

        private static bool HandleUtpMessage(string line)
        {
            if (!line.StartsWith("##utp:"))
                return false;
            var utpMessage = JsonConvert.DeserializeObject<UtpMessageBase>(line.Substring(6));
            switch (utpMessage.Type)
            {
                case "LogEntry":
                {
                    var entry = JsonConvert.DeserializeObject<UtpLogEntryMessage>(line.Substring(6));
                    if (isInTest)
                    {
                        // There might be errors logs in a test which would be expected. So we save them
                        logsInTest.Add(entry);
                        return true;
                    }

                    if (entry.Severity == "Info")
                        return true;
                    if (entry.Severity == "Warning")
                    {
                        Warnings.Add($"{MsToDateTime(entry.Time):HH:mm:ss.fff}: {entry.Message}\n{entry.Stacktrace}");
                    }
                    else
                    {
                        Errors.Add($"{MsToDateTime(entry.Time):HH:mm:ss.fff}: {entry.Severity}: {entry.Message}\n{entry.Stacktrace}");
                    }

                    break;
                }
                    
                case "TestStatus":
                {
                    var entry = JsonConvert.DeserializeObject<UtpTestStatusMessage>(line.Substring(6));
                    if (entry.Phase == UtpPhase.Begin)
                    {
                        isInTest = true;
                        logsInTest.Clear();
                    }
                    else if(entry.Phase == UtpPhase.End)
                    {
                        isInTest = false;
                        testsLeft--;
                        if (entry.State == TestStateEnum.Error || entry.State == TestStateEnum.Failure)
                        {
                            var errorString =
                                $"Test '{entry.Name}' reported state '{entry.State}' with message:\n{entry.Message}";
                            if (logsInTest.Count > 0)
                            {
                                errorString += $"\n  Logging in failed test was:\n";
                                foreach(var entryInTest in logsInTest)
                                {
                                    errorString +=
                                        $"  {MsToDateTime(entryInTest.Time):HH:mm:ss.fff}: {entryInTest.Severity}: {entryInTest.Message}\n  {entryInTest.File}:{entryInTest.Line}";
                                }

                                errorString += "\n";
                            }
                            RunLogger.LogError(errorString);
                        }
                    }
                    break;
                }

                case "TestPlan":
                {
                    var entry = JsonConvert.DeserializeObject<UtpTestPlanMessage>(line.Substring(6));
                    RunLogger.LogInfo($"Starting test run. {entry.Tests.Count} tests will be run");
                    testsLeft = entry.Tests.Count;
                    lastTestPrint = DateTime.UtcNow;
                    break;
                }

                case "AssemblyCompilationErrors":
                {
                    var entry = JsonConvert.DeserializeObject<UtpAssemblyCompilationErrorsMessage>(line.Substring(6));
                    var errorString = $"{MsToDateTime(entry.Time):HH:mm:ss.fff}: AssemblyCompilationErrors found {entry.Errors.Count} errors:\n";
                    foreach (var error in entry.Errors)
                    {
                        errorString += $"{error}\n";
                    }
                    RunLogger.LogError(errorString);
                    break;
                }

                case "Action":
                {
                    var entry = JsonConvert.DeserializeObject<UtpActionMessage>(line.Substring(6));
                    if (entry.Phase == UtpPhase.Begin)
                    {
                        RunLogger.LogInfo($"{MsToDateTime(entry.Time):HH:mm:ss.fff}: Action {entry.Name}: Started");
                    }

                    if (entry.Phase == UtpPhase.End)
                    {
                        RunLogger.LogInfo($"{MsToDateTime(entry.Time):HH:mm:ss.fff}: Action {entry.Name}: Ended");
                        if (entry.Errors?.Count > 0)
                        {
                            var errorString = $"Action ended with {entry.Errors.Count} errors:\n";
                            foreach (var error in entry.Errors)
                            {
                                errorString += $"{error}\n";
                            }
                            RunLogger.LogError(errorString);
                        }
                    }
                    break;
                }
                default:
                {
                    RunLogger.LogError($"{MsToDateTime(utpMessage.Time):HH:mm:ss.fff}: Unknown UTP message found of type: {utpMessage.Type}");
                    break;
                }
            }

            return true;
        }

        private static bool IsInstabilityMessage(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            
            if (line.Contains("connect ETIMEDOUT"))
                return true;
            if (line.Contains("Cannot connect to registry"))
                return true;
            if (line.Contains("failed to fetch from registry:"))
                return true;
            if (line.Contains("Cannot connect to Unity Package Manager local server"))
                return true;
            if (line.Contains(": 404 Not Found: artifactory")) // Another packman related thing
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
                case "[Package Manager] An error occurred while resolving packages:":
                    return true;
            }

            if (line.StartsWith("DirectoryNotFoundException: Could not find a part of the path"))
                return true;
            if (line.StartsWith("UnityException: "))
                return true;

            return false;
        }
    }
}