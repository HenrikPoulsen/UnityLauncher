using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using NDesk.Options;

namespace UnityLogWrapper
{
    class Program
    {
        [Flags]
        public enum Flag
        {
            None = 0,
            Batchmode        = 1 << 0,
            Quit             = 1 << 1,
            NoGraphics       = 1 << 2,
            SilentCrashes    = 1 << 3,
            WarningsAsErrors = 1 << 4,
            RunTests         = 1 << 5,
            Automated        = 1 << 6,
        }
        public static Flag Flags = 0;
        public static string UnityExecutable { get; set; } = string.Empty;
        public static string LogFile { get; set; } = string.Empty;
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
                    "quit",
                    "Quit the Unity Editor after other commands have finished executing. Note that this can cause error messages to be hidden (however, they still appear in the Editor.log file).",
                    v => Flags |= Flag.Quit
                },
                {
                    "nographics",
                    "When running in batch mode, do not initialize the graphics device at all. This makes it possible to run your automated workflows on machines that don’t even have a GPU (automated workflows only work when you have a window in focus, otherwise you can’t send simulated input commands). Please note that -nographics does not allow you to bake GI, since Enlighten requires GPU acceleration.",
                    v => Flags |= Flag.NoGraphics
                },
                {
                    "automated",
                    "This flag enables some extra logging when running tests. Like when a test is started/finished/etc which is helpful for identifying which test is logging something or what the result of the test was if you don't want to parse the testresults file.",
                    v => Flags |= Flag.Automated
                },
                {
                    "silentcrashes",
                    "Don’t display a crash dialog.",
                    v => Flags |= Flag.SilentCrashes
                },
                {
                    "warningsaserrors",
                    "Treat warnings as errors.",
                    v => Flags |= Flag.WarningsAsErrors
                },
                {
                    "runtests",
                    "Executes tests in the project",
                    v => Flags |= Flag.RunTests
                },
                {
                    "unityexecutable=",
                    "Path to unity executable that should run this command",
                    v => UnityExecutable = v
                },
                {
                    "projectpath=",
                    "Open the project at the given path.",
                    v => ProjectPath = v
                },
                {
                    "logfile=",
                    "Specify where the Editor or Windows/Linux/OSX standalone log file are written.",
                    v => LogFile = v
                },
                {
                    "testresults=",
                    "The path indicating where the result file should be saved. The result file is saved in Project’s root folder by default.",
                    v => TestResults = v
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

            if (!IsValidPath("unityexecutable", UnityExecutable))
                return -1;
            if (!IsValidPath("projectpath", ProjectPath))
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
            sb.Append($"-projectPath \"{Path.GetFullPath(ProjectPath)}\" ");
            
            
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
            
            if ((Flags & Flag.SilentCrashes) != Flag.None)
            {
                RunLogger.LogInfo("silentcrashes is set");
                sb.Append("-silent-crashes ");
            }
            
            if ((Flags & Flag.WarningsAsErrors) != Flag.None)
            {
                RunLogger.LogInfo("warningsaserrors is set");
                
            }

            if ((Flags & Flag.Automated) != Flag.None)
            {
                RunLogger.LogInfo("automated is set");
                sb.Append("-automated ");
            }
            
            if ((Flags & Flag.RunTests) != Flag.None)
            {
                RunLogger.LogInfo("runtests is set");
                sb.Append("-runTests ");

                if (string.IsNullOrEmpty(TestResults))
                {
                    RunLogger.LogWarning("It is not recommended to set runtests but not testresults. It will not be able to parse the test results as part of the report");
                }
                else
                {
                    sb.Append($"-testResults \"{Path.GetFullPath(TestResults)}\" ");
                }
            }
            
            if ((Flags & Flag.Quit) != Flag.None)
            {
                RunLogger.LogInfo("Quit is set");
                if ((Flags & Flag.RunTests) != Flag.None)
                {
                    RunLogger.LogWarning("quit and runtests cannot be set at once. Ignoring quit command");
                }
                else
                {
                    sb.Append("-quit ");                    
                }
            }

            var runResult = UnityLauncher.Run(sb.ToString());
            if (runResult == UnityLauncher.RunResult.FailedToStart)
                return -1;
            if (!LogParser.Parse())
                runResult = UnityLauncher.RunResult.Failure;

            if ((Flags & Flag.RunTests) != Flag.None)
            {
                if (File.Exists(TestResults))
                {
                    RunLogger.LogInfo($"Parsing {TestResults}");
                    if(CheckTestResults.Parse(TestResults) != UnityLauncher.RunResult.Success)
                        runResult = UnityLauncher.RunResult.Failure;
                }
                else
                {
                    RunLogger.LogError($"Could not find {TestResults}");
                    runResult = UnityLauncher.RunResult.Failure;
                }
            }

            if (runResult != UnityLauncher.RunResult.Success)
            {
                RunLogger.LogResultError("Run has failed");
                RunLogger.Dump();
                return -1;
            }
            
            RunLogger.LogResultInfo("Everything looks good. Run has passed");
            RunLogger.Dump();
            return 0;
        }

        public static string TestResults { get; set; } = string.Empty;

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

        public static string ProjectPath { get; set; }
    }
}