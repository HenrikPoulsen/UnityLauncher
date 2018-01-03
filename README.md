# Unity Log Wrapper
This is a small tool with the goal to help running Unity for CI.
As of writing this when running Unity from the command line you do not get all the information that you may want for CI purposes.
Like for example:
* Build warnings
* Build errors
* Incorrect command line arguments
* Test failures

These things you would need to hunt down manually in the log file or testresults file.
But with this tool you can for example do:
```
dotnet run -unityexecutable pathToUnityBinary -projectpath pathtoproject -runtests -batchmode -testresults results.xml -logfile log.txt
```
And it would launch the project with said executable and keep track of the process and the log files and output any important information to the console, like the ones mentioned above.