using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLauncher.Core;

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

            var grabCallStack = false;
            var lastIssueWasError = false;
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
                        if (utpMessage.Type != "LogEntry")
                        {
                            continue;
                        }
                        
                        Console.WriteLine(line);
                        var entry = JsonConvert.DeserializeObject<UtpLogEntryMessage>(line.Substring(6));
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