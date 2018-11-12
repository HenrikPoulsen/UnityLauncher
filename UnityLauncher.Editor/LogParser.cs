using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLauncher.Core;
using UnityLauncher.Editor.UTP;

namespace UnityLauncher.Editor
{
    public static class LogParser
    {
        private static readonly List<string> Warnings = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        public static bool Parse()
        {
            bool success;

            success = ParseLogFile();
            
            if (!ParseTestResults())
                success = false;
            return success;
        }

        private static bool ParseTestResults()
        {
            if (string.IsNullOrEmpty(Program.TestResults))
                return true;
            if (!File.Exists(Program.TestResults))
            {
                RunLogger.LogResultError($"Failed to find the test results file '{Program.TestResults}', failing run");
                return false;
            }

            return true;
        }

        private static bool ParseLogFile()
        {
            var success = true;
            RunLogger.LogInfo($"Parsing log located at {Path.GetFileName(Program.LogFile)}");
            while (LogIsLocked())
            {
                Thread.Sleep(1000);
            }

            var isInTest = false;
            var logsInTest = new List<UtpLogEntryMessage>();
            using (var reader = new StreamReader(Program.LogFile))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (line.StartsWith("##utp:"))
                    {
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
                                    continue;
                                }
                                if (entry.Severity == "Info")
                                    continue;
                                if (entry.Severity == "Warning")
                                {
                                    Warnings.Add($"{entry.Message}\n{entry.Stacktrace}");
                                }
                                else
                                {
                                    Errors.Add($"{entry.Severity}: {entry.Message}\n{entry.Stacktrace}");
                                    success = false;
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
                                                    $"  {entryInTest.Severity}: {entryInTest.Message}\n  {entryInTest.File}:{entryInTest.Line}";
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
                                break;
                            }

                            case "AssemblyCompilationErrors":
                            {
                                var entry = JsonConvert.DeserializeObject<UtpAssemblyCompilationErrorsMessage>(line.Substring(6));
                                var errorString = $"AssemblyCompilationErrors found {entry.Errors.Count} errors:\n";
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
                                    RunLogger.LogInfo($"Action {entry.Name}: Started");
                                }

                                if (entry.Phase == UtpPhase.End)
                                {
                                    RunLogger.LogInfo($"Action {entry.Name}: Ended");
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
                                RunLogger.LogError($"Unknown UTP message found of type: {utpMessage.Type}");
                                continue;
                            }
                        }
                    }

                    if (line.Contains("Aborting batchmode due to failure:") ||
                        line.Contains("Scripts have compiler errors."))
                    {
                        success = false;
                        continue;
                    }
                }
            }

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
                RunLogger.LogResultError($"Found {LogParser.Warnings.Count} warnings and {LogParser.Errors.Count} errors");
            else
                RunLogger.LogResultInfo($"Found {LogParser.Warnings.Count} warnings and {LogParser.Errors.Count} errors");
            return success;
        }

        private static bool LogIsLocked()
        {
            FileStream stream = null;

            try
            {
                stream = new FileInfo(Program.LogFile).Open(FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                RunLogger.LogWarning($"{Program.LogFile} is in use by another process. Waiting before parsing");
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            //file is not locked
            return false;
        }
    }
}