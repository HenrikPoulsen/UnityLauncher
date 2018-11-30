using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using UnityLauncher.Core;
using UnityLauncher.Editor.UTP;

namespace UnityLauncher.Editor
{
    public static class UnityLauncher
    {
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
                if (UseV2Crawler())
                {
                    RunLogger.LogInfo("Using V2 log crawler");
                    processResult = UnityLauncherLogCrawlerV2.CheckForCleanupEntry(process);    
                }
                else
                {
                    RunLogger.LogInfo("Using V1 log crawler");
                    processResult = UnityLauncherLogCrawler.CheckForCleanupEntry(process);  
                }
                
                process.WaitForExit();
                retryCount++;
            } while (processResult == ProcessResult.Timeout && retryCount < retryLimit);

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

        private static bool UseV2Crawler()
        {
            var v2CutoffVersion = new UnityVersion();
            v2CutoffVersion.SetVersion("2019.1.0a12");
            var fs = new FileStream(Program.LogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            var regex = new Regex("Built from '.*' branch; Version is '(.*) \\(.*\\) revision .*'; Using compiler version '.*'");
            using (var stream = new StreamReader(fs))
            {         
                while (true)
                {
                    var line = stream.ReadLine();
                    if (line == null)
                    {
                        continue;
                    }

                    var match = regex.Match(line);

                    if (!match.Success)
                    {
                        continue;
                    }

                    var versionString = match.Groups[1].Value;
                    var version = new UnityVersion();
                    version.SetVersion(versionString);
                    
                    if(version >= v2CutoffVersion)
                        return true;
                    return false;
                }
            }

            return false;
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

        

        
    }
}
