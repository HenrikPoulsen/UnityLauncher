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
            Batchmode =      1 << 0,
            Quit =          1 << 1,
            NoGraphics =    1 << 2,
            SilentCrashes = 1 << 3,
            WarningsAsErrors = 1 << 4,
            RunTests = 1 << 5,
        }
        public static Flag Flags = 0;
        public static string UnityExecutable { get; set; } = string.Empty;
        public static string LogFile { get; set; } = string.Empty;
        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            var options = new OptionSet
            {
                {
                    "batchmode", 
                    "Run Unity in batch mode. This should always be used in conjunction with the other command line arguments, because it ensures no pop-up windows appear and eliminates the need for any human intervention. When an exception occurs during execution of the script code, the Asset server updates fail, or other operations that fail, Unity immediately exits with return code 1. \nNote that in batch mode, Unity sends a minimal version of its log output to the console. However, the Log Files still contain the full log information. Opening a project in batch mode while the Editor has the same project open is not supported; only a single instance of Unity can run at a time.",
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
                Console.WriteLine(e);
                throw;
            }

            if (!IsValidPath("unityexecutable", UnityExecutable))
                return -1;
            if (!IsValidPath("projectpath", ProjectPath))
                return -1;
            if (string.IsNullOrEmpty(LogFile))
            {
                Console.WriteLine("logfile must be set");
                return -1;
            }
            
            if(!IsValidPath("logfile", new FileInfo(LogFile).Directory.FullName))
                return -1;

            var sb = new StringBuilder();

            sb.Append($"-logFile \"{Path.GetFullPath(LogFile)}\" ");
            sb.Append($"-projectPath \"{Path.GetFullPath(ProjectPath)}\" ");
            
            
            if ((Flags & Flag.Batchmode) != Flag.None)
            {
                Console.WriteLine("Batchmode is set");
                sb.Append("-batchmode ");
            }

            if ((Flags & Flag.NoGraphics) != Flag.None)
            {
                Console.WriteLine("Nographics is set");
                sb.Append("-nographics ");
            }
            
            if ((Flags & Flag.SilentCrashes) != Flag.None)
            {
                Console.WriteLine("silentcrashes is set");
                sb.Append("-silent-crashes ");
            }
            
            if ((Flags & Flag.WarningsAsErrors) != Flag.None)
            {
                Console.WriteLine("warningsaserrors is set");
                
            }
            
            if ((Flags & Flag.RunTests) != Flag.None)
            {
                Console.WriteLine("runtests is set");
                sb.Append("-runTests ");

                if (string.IsNullOrEmpty(TestResults))
                {
                    Console.WriteLine("It is not recommended to set runtests but not testresults. It will not be able to parse the test results as part of the report");
                }
                else
                {
                    sb.Append($"-testResults \"{Path.GetFullPath(TestResults)}\" ");
                }
            }
            
            if ((Flags & Flag.Quit) != Flag.None)
            {
                Console.WriteLine("Quit is set");
                if ((Flags & Flag.RunTests) != Flag.None)
                {
                    Console.WriteLine("quit and runtests cannot be set at once. Ignoring quit command");
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
                    Console.WriteLine($"Parsing {TestResults}");
                    if(CheckTestResults.Parse(TestResults) != UnityLauncher.RunResult.Success)
                        runResult = UnityLauncher.RunResult.Failure;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Could not find {TestResults}");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    runResult = UnityLauncher.RunResult.Failure;
                }
            }
            
            Console.WriteLine($"Found {LogParser.CompilerWarnings.Count} warnings and {LogParser.CompilerErrors.Count} errors");
            if ((Flags & Flag.RunTests) != Flag.None)
            {
                Console.WriteLine("Test results:");
                Console.WriteLine($"  Total: {CheckTestResults.Summary.Total}");
                Console.WriteLine($"  Passed: {CheckTestResults.Summary.Passed}");
                Console.WriteLine($"  Failed: {CheckTestResults.Summary.Failed}");
                Console.WriteLine($"  Ignored: {CheckTestResults.Summary.Ignored}");
                Console.WriteLine($"  Inconclusive: {CheckTestResults.Summary.Inconclusive}");
            }

            if (runResult != UnityLauncher.RunResult.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Run has failed");
                Console.ForegroundColor = ConsoleColor.Gray;
                
                return -1;
            }

            return 0;
        }

        public static string TestResults { get; set; } = string.Empty;

        private static bool IsValidPath(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Console.WriteLine($"{key} must be set");
                return false;
            }

            if (!Directory.Exists(value) && !File.Exists(value))
            {
                Console.WriteLine($"The path for '{key}' does not exist. '{value}'");
                return false;
            }
            return true;
        }

        public static string ProjectPath { get; set; }
    }
}