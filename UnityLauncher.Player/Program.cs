using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using NDesk.Options;
using UnityLauncher.Core;

namespace UnityLauncher.Player
{
    class Program
    {
        [Flags]
        public enum Flag
        {
            None                       = 0,
            Batchmode                  = 1 << 0,
            NoGraphics                 = 1 << 2,
            TimeoutIgnore              = 1 << 7,
        }

        public enum ScriptingBackend
        {
            current = -1,
            mono = 0,
            il2cpp = 1
        }
        public static Flag Flags = 0;

        public static int? ExecutionTimeout;
        public static string Executable { get; set; } = string.Empty;
        public static string LogFile { get; set; } = string.Empty;
        public static string CleanedLogFile { get; set; } = string.Empty;
        public static int? ScreenWidth;
        public static int? ScreenHeight;
        public static string ScreenQuality;
        static int Main(string[] args)
        {
            var options = new OptionSet
            {
                {
                    "batchmode", 
                    "Run Unity in batch mode. This should always be used in conjunction with the other command line arguments, because it ensures no pop-up windows appear and eliminates the need for any human intervention. When an exception occurs during execution of the script code, the Asset server updates fail, or other operations that fail, Unity immediately exits with return code 1. \nNote that in batch mode, Unity sends a minimal version of its log output to the RunLogger However, the Log Files still contain the full log information. Opening a project in batch mode while the Editor has the same project open is not supported; only a single instance of Unity can run at a time.",
                    v => Flags |= Flag.Batchmode
                },
                {
                    "nographics",
                    "When running in batch mode, do not initialize the graphics device at all. This makes it possible to run your automated workflows on machines that don’t even have a GPU (automated workflows only work when you have a window in focus, otherwise you can’t send simulated input commands). Please note that -nographics does not allow you to bake GI, since Enlighten requires GPU acceleration.",
                    v => Flags |= Flag.NoGraphics
                },
                {
                    "executable=",
                    "Path to unity executable that should run this command",
                    v => Executable = v
                },
                {
                    "logfile=",
                    "Specify where the Editor or Windows/Linux/OSX standalone log file are written.",
                    v => LogFile = v
                },
                {
                    "cleanedLogFile=",
                    "Logs file that should only contain important messages (warnings, errors, assertions). If this is set and the specified file is not empty after the run the run will be flagged as failed.",
                    v => CleanedLogFile = v
                },
                {
                    "timeout=",
                    "Timeout the execution after the supplied seconds. Will fail run if -timeoutIgnore is not set and it times out",
                    v => ExecutionTimeout = int.Parse(v)
                },
                {
                    "timeoutIgnore",
                    "Indicates that if the exeuction times out it should not flag it as a failure if everything else is ok",
                    v => Flags |= Flag.TimeoutIgnore
                },
                {
                    "screenheight=",
                    "Sets the height of the window",
                    v => ScreenHeight = int.Parse(v)
                },
                {
                    "screenwidth=",
                    "Sets the width of the window",
                    v => ScreenWidth = int.Parse(v)
                },
                {
                    "screenquality=",
                    "Sets the render quality. Should be the name of the quality level",
                    v => ScreenQuality = v
                },  
                
            };

            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                RunLogger.LogError(e.Message);
                throw;
            }

            if (!IsValidPath("executable", Executable))
                return -1;
            if (string.IsNullOrEmpty(LogFile))
            {
                RunLogger.LogError("logfile must be set");
                return -1;
            }
            
            if(!IsValidPath("logfile", new FileInfo(LogFile).Directory.FullName))
                return -1;

            var sb = new StringBuilder();

            sb.Append($"-logFile \"{Path.GetFullPath(LogFile)}\" ");

            if (!string.IsNullOrEmpty(CleanedLogFile))
            {
                sb.Append($"-cleanedLogFile \"{Path.GetFullPath(CleanedLogFile)}\" ");
            }
            
            if ((Flags & Flag.Batchmode) != Flag.None)
            {
                RunLogger.LogInfo("Batchmode is set");
                sb.Append("-batchmode ");
            }

            if ((Flags & Flag.NoGraphics) != Flag.None)
            {
                RunLogger.LogInfo("Nographics is set");
                sb.Append("-nographics ");
            }
            
            if ((Flags & Flag.TimeoutIgnore) != Flag.None)
            {
                RunLogger.LogInfo("timeoutIgnore is set");
                
            }

            if (ScreenHeight.HasValue)
            {
                sb.Append($"-screen-height {ScreenHeight} ");
            }
            
            if (ScreenWidth.HasValue)
            {
                sb.Append($"-screen-width {ScreenWidth} ");
            }
            
            if (!string.IsNullOrEmpty(ScreenQuality))
            {
                sb.Append($"-screen-quality {ScreenQuality} ");
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var runResult = PlayerLauncher.Run(sb.ToString());
            RunLogger.LogResultInfo($"Command execution took: {stopwatch.Elapsed}");
            if (runResult == RunResult.FailedToStart)
                return -1;
            //if (!LogParser.Parse())
            //    runResult = RunResult.Failure;

            runResult = ParsedCleanedLogFileForErrors(runResult);

            if (runResult != RunResult.Success)
            {
                RunLogger.LogResultError("Run has failed");
                RunLogger.Dump();
                return -1;
            }
            
            RunLogger.LogResultInfo("Everything looks good. Run has passed");
            RunLogger.Dump();
            return 0;
        }

        static RunResult ParsedCleanedLogFileForErrors(RunResult runResult)
        {
            if (string.IsNullOrEmpty(CleanedLogFile))
                return runResult;
            if (!File.Exists(CleanedLogFile))
                return runResult;

            var content = File.ReadAllLines(CleanedLogFile);
            if (content.Length == 0)
                return runResult;
            var printedIndex = 0;
            
            var errors = new List<string>();
            foreach (var line in content)
            {
                if (!line.StartsWith("Assertion Failed:") && !line.Contains("(Error: "))
                    continue;
                
                errors.Add(line);
                if (errors.Count >= 10)
                    break;
            }

            if (!errors.Any())
                return runResult;
            
            RunLogger.LogResultError($"{CleanedLogFile} contains errors and/or assertions. Failing run");
            RunLogger.LogError("Cleaned output file is not empty. Here are the first 10 errors and assertions:");

            foreach (var error in errors)
            {
                    RunLogger.LogError(error);
            }

            return RunResult.Failure;
        }

        static int SetScriptingBackend(ScriptingBackend scriptingBackend, string projectPath)
        {
            var filePath = $"{projectPath}/ProjectSettings/ProjectSettings.asset";
            if (!File.Exists(filePath))
            {
                RunLogger.LogError($"Could not find {filePath} to set scriptingBackend in");
                return -1;
            }
                
            var file = File.ReadAllLines(filePath).ToList();

            var sectionEmptyIndex = -1;
            var foundSection = false;
            for(var i = 0; i < file.Count; i++)
            {
                var line = file[i];
                var trimmed = line.Trim();
                if (!foundSection)
                {
                     if(!trimmed.StartsWith("scriptingBackend"))
                        continue;
                    
                    foundSection = true;
                    if (trimmed.EndsWith("}"))
                    {
                        sectionEmptyIndex = i;
                        file[i] = "  scriptingBackend: ";
                        file.Insert(i+1, $"    Standalone: {(int)scriptingBackend}");
                        break;
                    }
                }

                if (!trimmed.StartsWith("Standalone: "))
                    continue;

                file[i] = $"    Standalone: {(int)scriptingBackend}";
                break;
            }
            
            File.WriteAllLines(filePath, file);
            return 0;
        }

        private static bool IsValidPath(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                RunLogger.LogError($"{key} must be set");
                return false;
            }

            if (!Directory.Exists(value) && !File.Exists(value))
            {
                RunLogger.LogError($"The path for '{key}' does not exist. '{value}'");
                return false;
            }
            return true;
        }
    }
}