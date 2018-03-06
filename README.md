# Unity Launcher
This is a small tool with the goal to help running Unity Editor & Players for CI.
## Editor
As of writing this when running the Unity Editor from the command line you do not get all the information that you may want for CI purposes.
Like for example:
* Build warnings
* Build errors
* Incorrect command line arguments
* Test failures

These things you would need to hunt down manually in the log file or testresults file.
But with this tool you can for example do:
```
dotnet UnityLauncher.Editor/bin/Debug/netcoreapp2.0/UnityLauncher.Editor.dll -unityexecutable pathToUnityBinary -projectpath pathtoproject -runtests -batchmode -testresults results.xml -logfile log.txt
```
And it would launch the project with said executable and keep track of the process and the log files and output any important information to the console, like the ones mentioned above.

## Player
Sometimes it might be useful to not only build a standalone player but also execute it to see if it crashes or logs errors.

To do this you can for example do the following:
```
dotnet UnityLauncher.Player/bin/Debug/netcoreapp2.0/UnityLauncher.Player.dll -executable pathToStandaloneBinary -logfile log.txt -cleanedLogFile log-cleaned.txt -timeout 120 -timeoutIgnore
```
Setting the `-timeout` flag means it will kill the process after that amount of time. If `-timeoutIgnore` is set it will not indicate that the run failed if it timed out.
After the execution is done it will check the exit code of the player and as well as the log output to see if there were any errors or execeptions. If something unexpected happens it will indicate that the run failed.
