using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityLogWrapper
{
    public static class UnityLauncher
    {
        public static bool Run(string args)
        {
            if (AlreadyRunning())
            {
                Console.WriteLine($"Error: Unity is already running. Wait until the process is closed and try again");
                return false;
            }
            File.Delete(Program.LogFile);
            Console.WriteLine($"Will now run:\n{Program.UnityExecutable} {args}");
            var process = new Process()
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
            Console.WriteLine($"Unity process spawned with Id: {process.Id}");
            StreamReader fs;
            var ignoreExitCode = CheckForCleanupEntry(process);
            Console.WriteLine($"Exeuction Done! Exit code: {process.ExitCode}");


            if (process.ExitCode != 0)
            {
                if (ignoreExitCode)
                {
                    Console.WriteLine("Exit code not 0, but this was expected in this case. Ignoring it");
                }
                else
                {
                    Console.WriteLine("Exit code not 0, run failed.");
                    return false;    
                }                  
            
            }
            return true;
        }

        private static bool CheckForCleanupEntry(Process process)
        {
            var fs = new FileStream(Program.LogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
            using (var stream = new StreamReader(fs))
            {
                var waitingForDeath = false;
                var waitingForDeathCounter = 10;
                while (!process.HasExited)
                {
                    var line = stream.ReadLine();
                    if (line == "Cleanup mono")
                    {
                        Console.WriteLine("Found editor shutdown log print. Waiting 10 seconds for process to quit");
                        waitingForDeath = true;
                    }
                    if (waitingForDeath)
                    {
                        Thread.Sleep(1000);
                        if (waitingForDeathCounter-- <= 0)
                        {
                            Console.WriteLine("Editor did not quit after 10 seconds. Forcibly quitting and whitelisting the exit code");
                            process.Kill();
                            return true;
                        }
                    }
                //if(line.Contains())
                }
            }
            return false;

        }

        private static bool AlreadyRunning()
        {
            if (Process.GetProcessesByName("Unity").Any())
            {
                return true;
            }
            return false;
        }
    }
}