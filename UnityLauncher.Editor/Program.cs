using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NDesk.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLauncher.Core;

namespace UnityLauncher.Editor
{
    class Program
    {
        [Flags]
        public enum Flag
        {
            None                          = 0,
            Batchmode                     = 1 << 0,
            Quit                          = 1 << 1,
            NoGraphics                    = 1 << 2,
            SilentCrashes                 = 1 << 3,
            WarningsAsErrors              = 1 << 4,
            RunTests                      = 1 << 5,
            Automated                     = 1 << 6,
            TimeoutIgnore                 = 1 << 7,
            IgnoreErrorsOnArtifactCreation= 1 << 8
        }

        public enum ScriptingBackend
        {
            current = -1,
            mono = 0,
            il2cpp = 1
        }
        public enum DisplayResolutionDialog
        {
            current = -1,
            disabled = 0,
            enabled = 1
        }
        public static Flag Flags = 0;
        public static ScriptingBackend ScriptingBackendOverride = ScriptingBackend.current;
        public static DisplayResolutionDialog DisplayResolutionDialogOverride = DisplayResolutionDialog.current;
        private static string buildLinuxUniversalPlayer;
        private static string buildOSXUniversalPlayer;
        private static string buildWindows64Player;
        public static int? ExecutionTimeout;
        public static string UnityExecutable { get; set; } = string.Empty;
        public static string LogFile { get; set; } = string.Empty;
        public static string CleanedLogFile { get; set; } = string.Empty;
        static string SceneOverride;
        public static string ExpectedBuildArtifact;
        private static List<string> ExtraArgs;
        private static List<string> AddPackages = new List<string>();
        private static string BuildTarget;
        private enum BuildTargets
        {
            standalone,
            Win,
            Win64,
            OSXUniversal,
            Linux,
            Linux64,
            LinuxUniversal,
            iOS,
            Android,
            Web,
            WebStreamed,
            WebGL,
            XboxOne,
            PS4,
            PSP2,
            WindowsStoreApps,
            Switch,
            N3DS,
            tvOS,
            PSM
        };
        public static int ExpectedExitCode = 0;
        public static string RegistryOverride = null;
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
                    "ignoreErrorsOnArtifactCreation",
                    "Workaround to a random compilation issue introduced in 2018.2+ on slower machines. In that you sometimes get random compilation errors but artifacts are still being produced. So this flag will ignore the errors if the testresults or standalone build artifacts has been generated.",
                    v => Flags |= Flag.IgnoreErrorsOnArtifactCreation
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
                    "expectedexitcode=",
                    "If you for some reason don't expect to get exit code 0 from the run and want to enforce it",
                    v => ExpectedExitCode = int.Parse(v)
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
                    "cleanedLogFile=",
                    "Logs file that should only contain important messages (warnings, errors, assertions). If this is set and the specified file is not empty after the run the run will be flagged as failed.",
                    v => CleanedLogFile = v
                },
                {
                    "testresults=",
                    "The path indicating where the result file should be saved. The result file is saved in Project’s root folder by default.",
                    v => TestResults = v
                },
                {
                    "scriptingBackend=",
                    "Will hack in the scripting backend for standalone in the ProjectSettings.asset so you can easily toggle it in CI. Valid values are: mono, il2cpp",
                    v => ScriptingBackendOverride = Enum.Parse<ScriptingBackend>(v)
                },
                {
                    "displayResolutionDialog=",
                    "Will hack in if the display resolution dialog should be enabled or not in built players into the ProjectSettings.asset. Valid values are: enabled, disabled",
                    v => DisplayResolutionDialogOverride = Enum.Parse<DisplayResolutionDialog>(v)
                },
                {
                    "buildLinuxUniversalPlayer=",
                    "Build a combined 32-bit and 64-bit standalone Linux player (for example, -buildLinuxUniversalPlayer path/to/your/build).",
                    v => buildLinuxUniversalPlayer = v
                },
                {
                    "buildOSXUniversalPlayer=",
                    "Build a combined 32-bit and 64-bit standalone Mac OSX player (for example, -buildOSXUniversalPlayer path/to/your/build.app).",
                    v => buildOSXUniversalPlayer = v
                },
                {
                    "registryoverride=",
                    "If you for some reason need to use a custom npm registry than the one in the manifest.json (maybe you have a fast one for your build system). Empty string removes the field",
                    v => RegistryOverride = v
                },
                {
                    "buildWindows64Player=",
                    "Build a 64-bit standalone Windows player (for example, -buildWindows64Player path/to/your/build.exe).",
                    v => buildWindows64Player = v
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
                    "scene=",
                    "Modifies the scene list to build with only the scene listed here. Needs to be the relative path to the file from the project path",
                    v => SceneOverride = v
                },
                {
                    "addPackage=",
                    "Modifies the Packages/manifest.json of the project to include the package specified. Use the format packagename@version. Ex: com.somepackage@1.0.0. Command can be repeated for multiple packages",
                    v => AddPackages.Add(v)
                },
                {
                    "buildTarget=",
                    $"Allows the selection of an active build target before loading a project. Possible options are: {string.Join(", ", Enum.GetNames(typeof(BuildTargets)).ToList())}.",
                    v => BuildTarget = v
                }
                
            };

            try
            {
                ExtraArgs = options.Parse(args);
            }
            catch (OptionException e)
            {
                RunLogger.LogError(e.Message);
                RunLogger.Dump();
                options.WriteOptionDescriptions(Console.Out);
                throw;
            }
            if (ExtraArgs.Any())
            {
                RunLogger.LogInfo($"Unknown commands passed. These will be passed a long to the process:\n {string.Join(" ", ExtraArgs)}");
            }

            if (!IsValidPath("unityexecutable", UnityExecutable))
            {
                RunLogger.Dump();
                options.WriteOptionDescriptions(Console.Out);
                return -1;
            }
            if (!IsValidPath("projectpath", ProjectPath))
            {
                RunLogger.Dump();
                options.WriteOptionDescriptions(Console.Out);
                return -1;
            }
                
            if (string.IsNullOrEmpty(LogFile))
            {
                RunLogger.LogError("logfile must be set");
                RunLogger.Dump();
                options.WriteOptionDescriptions(Console.Out);
                return -1;
            }

            var result = UpdateProjectSettings(ProjectPath);
            if (result != 0)
            {
                RunLogger.Dump();
                return -1;
            }
            
            result = UpdateManifest(ProjectPath, AddPackages);
            if (result != 0)
            {
                RunLogger.Dump();
                return -1;
            }


            if (!IsValidPath("logfile", new FileInfo(LogFile).Directory.FullName))
            {
                RunLogger.Dump();
                return -1;
            }
                

            var sb = new StringBuilder();

            sb.Append($"-logFile \"{Path.GetFullPath(LogFile)}\" ");
            sb.Append($"-projectPath \"{Path.GetFullPath(ProjectPath)}\" ");
            
            if (!string.IsNullOrEmpty(CleanedLogFile))
            {
                sb.Append($"-cleanedLogFile \"{Path.GetFullPath(CleanedLogFile)}\" ");
            }

            if (ExtraArgs.Any())
            {
                sb.Append(string.Join(" ", ExtraArgs));
                sb.Append(" ");
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
            
            if ((Flags & Flag.SilentCrashes) != Flag.None)
            {
                RunLogger.LogInfo("silentcrashes is set");
                sb.Append("-silent-crashes ");
            }
            
            if ((Flags & Flag.WarningsAsErrors) != Flag.None)
            {
                RunLogger.LogInfo("warningsaserrors is set");
                
            }
            
            if ((Flags & Flag.TimeoutIgnore) != Flag.None)
            {
                RunLogger.LogInfo("timeoutIgnore is set");
                
            }

            if ((Flags & Flag.IgnoreErrorsOnArtifactCreation) != Flag.None)
            {
                RunLogger.LogInfo("ignoreErrorsOnArtifactCreation is set");
            }

            if ((Flags & Flag.Automated) != Flag.None)
            {
                RunLogger.LogInfo("automated is set");
                sb.Append("-automated ");
            }

            if (!string.IsNullOrEmpty(BuildTarget))
            {
                if (!Enum.TryParse<BuildTargets>(BuildTarget, out var parsedEnum))
                {
                    RunLogger.LogError($"{BuildTarget} is not a valid buildtarget. It has to be one of:\n{string.Join(", ", Enum.GetNames(typeof(BuildTargets)))}");
                    return -1;
                }
                sb.Append($"-buildTarget {BuildTarget} ");
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

            if (!string.IsNullOrEmpty(buildWindows64Player))
            {
                RunLogger.LogInfo("buildWindows64Player is set");
                sb.Append($"-buildWindows64Player {buildWindows64Player} ");
                ExpectedBuildArtifact = $"{ProjectPath}/{buildWindows64Player}";
            }

            if (!string.IsNullOrEmpty(buildLinuxUniversalPlayer))
            {
                RunLogger.LogInfo("buildLinuxUniversalPlayer is set");
                sb.Append($"-buildLinuxUniversalPlayer {buildLinuxUniversalPlayer} ");
                ExpectedBuildArtifact = $"{ProjectPath}/{buildLinuxUniversalPlayer}";
            }

            if (!string.IsNullOrEmpty(buildOSXUniversalPlayer))
            {
                RunLogger.LogInfo("buildOSXUniversalPlayer is set");
                sb.Append($"-buildOSXUniversalPlayer {buildOSXUniversalPlayer} ");
                ExpectedBuildArtifact = $"{ProjectPath}/{buildOSXUniversalPlayer}";
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var runResult = UnityLauncher.Run(sb.ToString());
            RunLogger.LogResultInfo($"Command execution took: {stopwatch.Elapsed}");
            RestoreProjectSettings(ProjectPath);
            if (runResult == RunResult.FailedToStart)
            {
                RunLogger.Dump();
                return -1;
            }
                
            if (!LogParser.Parse())
                runResult = RunResult.Failure;

            if (runResult != RunResult.Success && ShouldOverrideOverrideBuildFailure())
            {
                runResult = RunResult.Success;
            }

            if ((Flags & Flag.RunTests) != Flag.None)
            {
                if (File.Exists(TestResults))
                {
                    RunLogger.LogInfo($"Parsing {TestResults}");
                    if(CheckTestResults.Parse(TestResults) != RunResult.Success)
                        runResult = RunResult.Failure;
                }
                else
                {
                    RunLogger.LogError($"Could not find {TestResults}");
                    runResult = RunResult.Failure;
                }
            }

     
             runResult = ParsedCleanedLogFileForErrors(runResult);

            if (runResult != RunResult.Success)
            {
                RunLogger.LogResultError("Run has failed");
                RunLogger.Dump();
                return -1;
            }

            if (!string.IsNullOrEmpty(ExpectedBuildArtifact) && !(File.Exists(ExpectedBuildArtifact) || Directory.Exists(ExpectedBuildArtifact)))
            {
                RunLogger.LogResultError($"Expected to find {ExpectedBuildArtifact} after the execution but it is missing. Check the log for what could have gone wrong");
                RunLogger.Dump();
                return -1;
            }
            
            RunLogger.LogResultInfo("Everything looks good. Run has passed");
            RunLogger.Dump();
            return 0;
        }

        private static bool ShouldOverrideOverrideBuildFailure()
        {
            
            if ((Flags & Flag.IgnoreErrorsOnArtifactCreation) == Flag.None)
                return false;
            
            var success = false;
            if (string.IsNullOrEmpty(TestResults) && string.IsNullOrEmpty(ExpectedBuildArtifact))
            {
                // No test or build artifacts expected then we allow failure
                RunLogger.LogInfo($"[{nameof(ShouldOverrideOverrideBuildFailure)}] No artifacts expected in command line");
            }
            else if (!string.IsNullOrEmpty(TestResults) && !File.Exists(TestResults))
            {
                // No test results have been generated so we allow failure
                RunLogger.LogInfo($"[{nameof(ShouldOverrideOverrideBuildFailure)}] No test results file has been generated");
            }
            else if (!string.IsNullOrEmpty(ExpectedBuildArtifact) &&
                     !Directory.Exists(ExpectedBuildArtifact) &&
                     !File.Exists(ExpectedBuildArtifact))
            {
                // Build artifact path has been set but nothing has been created then we also allow failure
                RunLogger.LogInfo($"[{nameof(ShouldOverrideOverrideBuildFailure)}] Build artifacts file/folder doesn't exist");
            }
            else
            {
                //TODO: Temporary fix for random compilation issue. If flag is set we ignore whatever the parselogfile reports
                RunLogger.LogResultInfo("Errors were found in the log file but these were ignored since the artifacts were still generated and -ignoreErrorsOnArtifactCreation has been set");
                success = true;
            }

            return success;
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

        private static int UpdateManifest(string projectPath, List<string> packages)
        {
            if (RegistryOverride == null && packages.FirstOrDefault() == null)
                return 0;
            var modified = false;
            
            var manifestPath = $"{projectPath}/Packages/manifest.json";
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if (RegistryOverride != null)
            {
                RunLogger.LogInfo($"Overriding registry in manifest to be {RegistryOverride}");
                
                if (!File.Exists(manifestPath))
                {
                    RunLogger.LogError($"Could not find {manifestPath}");
                    return -1;
                }
                
                if (RegistryOverride == "" && manifest.ContainsKey("registry"))
                    manifest.Remove("registry");
                else
                    manifest["registry"] = RegistryOverride;
                modified = true;                
            }

            if (packages.FirstOrDefault() != null)
            {
                foreach (var package in packages)
                {
                    if (!package.Contains("@"))
                    {
                        RunLogger.LogError($"-addPackage {0} is missing an @");
                        return -1;
                    }

                    var split = package.Trim().Split("@");
                    var packageName = split[0];
                    var packageVersion = split[1];

                    manifest["dependencies"][packageName] = packageVersion;
                    modified = true;
                }
            }
            
            if(modified)
                File.WriteAllText(manifestPath, manifest.ToString());
            
            return 0;
        }
        
        static List<string> ProjectSettingsOriginal;
        static List<string> EditorBuildSettingsOriginal;

        static int UpdateProjectSettings(string projectPath)
        {
            var projectSettingsAssetPath = $"{projectPath}/ProjectSettings/ProjectSettings.asset";
            var editorBuildSettingsPath = $"{projectPath}/ProjectSettings/EditorBuildSettings.asset";
            if (!File.Exists(projectSettingsAssetPath))
            {
                RunLogger.LogError($"Could not find {projectSettingsAssetPath}");
                return -1;
            }
            if (!File.Exists(editorBuildSettingsPath))
            {
                RunLogger.LogError($"Could not find {editorBuildSettingsPath}");
                return -1;
            }

            ProjectSettingsOriginal = File.ReadAllLines(projectSettingsAssetPath).ToList();
            EditorBuildSettingsOriginal = File.ReadAllLines(editorBuildSettingsPath).ToList();
                
            var projectSettingsAsset = File.ReadAllLines(projectSettingsAssetPath).ToList();
            var editorBuildSettings = File.ReadAllLines(editorBuildSettingsPath).ToList();

            if(!UpdateScriptingBackend(ref projectSettingsAsset))
                return -1;
            if (!UpdateResolutionDialog(ref projectSettingsAsset))
                return -1;
            if(!UpdateScene(ref editorBuildSettings))
                return -1;
            
            File.WriteAllLines(projectSettingsAssetPath, projectSettingsAsset);
            File.WriteAllLines(editorBuildSettingsPath, editorBuildSettings);
            return 0;
        }

        static void RestoreProjectSettings(string projectPath)
        {
            RunLogger.LogInfo($"Restoring settings in ProjectSettings.asset and EditorBuildSettings.asset to how they were");
            var projectSettingsAssetPath = $"{projectPath}/ProjectSettings/ProjectSettings.asset";
            var editorBuildSettingsPath = $"{projectPath}/ProjectSettings/EditorBuildSettings.asset";
            File.WriteAllLines(projectSettingsAssetPath, ProjectSettingsOriginal);
            File.WriteAllLines(editorBuildSettingsPath, EditorBuildSettingsOriginal);
        }

        static bool UpdateScene(ref List<string> file)
        {
            if (string.IsNullOrEmpty(SceneOverride))
                return true;

            if (!File.Exists($"{ProjectPath}/{SceneOverride}"))
            {
                RunLogger.LogResultError($"Failed to find {ProjectPath}/{SceneOverride}");
                return false;
            }
            
            RunLogger.LogInfo($"Setting m_Scenes in EditorBuildSettings.asset to {SceneOverride}");

            var sceneMetaFile = File.ReadAllLines($"{ProjectPath}/{SceneOverride}.meta");
            string sceneGuid = null;

            foreach (var line in sceneMetaFile)
            {
                if (!line.StartsWith("guid:"))
                    continue;
                sceneGuid = line.Substring(6);
                break;
            }

            var sectionFound = false;
            for(var i = 0; i < file.Count; i++)
            {
                var line = file[i];
                var trimmed = line.Trim();
                if(!sectionFound)
                {
                    if (trimmed.StartsWith("m_Scenes:"))
                    {
                        sectionFound = true;
                        file.Insert(++i, $"  - enabled: 1");
                        file.Insert(++i, $"    path: {SceneOverride}");
                        file.Insert(++i, $"    guid: {sceneGuid}");
                    }
                    continue;
                }

                if (trimmed.StartsWith("- enabled") || trimmed.StartsWith("guid:") || trimmed.StartsWith("path:"))
                {
                    file.RemoveAt(i);
                    i--;
                }
                else
                {
                    break;
                }
            }

            return true;
        }

        static bool UpdateResolutionDialog(ref List<string> file)
        {
            if (DisplayResolutionDialogOverride == DisplayResolutionDialog.current)
                return true;
            RunLogger.LogInfo($"Setting displayResolutionDialog in ProjectSettings.asset to {DisplayResolutionDialogOverride}");
            for(var i = 0; i < file.Count; i++)
            {
                var line = file[i];
                var trimmed = line.Trim();
                if(!trimmed.StartsWith("displayResolutionDialog:"))
                        continue;

                file[i] = $"  displayResolutionDialog: {(int)DisplayResolutionDialogOverride}";
                break;
            }

            return true;
        }
        
        

        static bool UpdateScriptingBackend(ref List<string> file)
        {
            if (ScriptingBackendOverride == ScriptingBackend.current)
                return true;
            
            RunLogger.LogInfo($"Setting scriptingBackend in ProjectSettings.asset to {ScriptingBackendOverride}");
            
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
                        file[i] = "  scriptingBackend: ";
                        file.Insert(i+1, $"    Standalone: {(int)ScriptingBackendOverride}");
                        break;
                    }
                }

                if (!trimmed.StartsWith("Standalone: "))
                    continue;

                file[i] = $"    Standalone: {(int)ScriptingBackendOverride}";
                break;
            }

            return true;
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