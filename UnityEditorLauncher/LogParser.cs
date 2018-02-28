using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityLogWrapper
{
    public static class LogParser
    {
        private static readonly List<string> CompilerWarnings = new List<string>();
        private static readonly List<string> CompilerErrors = new List<string>();
        public static bool Parse()
        {
            var success = true;

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
            using (var reader = new StreamReader(Program.LogFile))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(": error CS"))
                    {
                        CompilerErrors.Add(line);
                        success = false;
                        continue;
                    }
                    if (line.Contains(": warning CS"))
                    {
                        CompilerWarnings.Add(line);
                        continue;
                    }

                    if (line.Contains("Aborting batchmode due to failure:") ||
                        line.Contains("Scripts have compiler errors."))
                    {
                        success = false;
                        continue;
                    }
                }
            }

            if (CompilerWarnings.Any())
            {
                Console.Write("\n");
                RunLogger.LogInfo("Found compiler warnings:");
                foreach (var warning in CompilerWarnings)
                {
                    RunLogger.LogWarning(warning);
                }
                if ((Program.Flags & Program.Flag.WarningsAsErrors) != Program.Flag.None)
                {
                    success = false;
                    RunLogger.LogResultError("warningsasserrors has been set so marking the run as failed due to warnings");
                }
                Console.Write("\n");
            }
            if (CompilerErrors.Any())
            {
                Console.Write("\n");
                RunLogger.LogError("Found compiler errors:");
                foreach (var error in CompilerErrors)
                {
                    RunLogger.LogError(error);
                }
                success = false;
                Console.Write("\n");
            }
            
            if(!success)
                RunLogger.LogResultError($"[Compiler] Found {LogParser.CompilerWarnings.Count} warnings and {LogParser.CompilerErrors.Count} errors");
            else
                RunLogger.LogResultInfo($"[Compiler] Found {LogParser.CompilerWarnings.Count} warnings and {LogParser.CompilerErrors.Count} errors");
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