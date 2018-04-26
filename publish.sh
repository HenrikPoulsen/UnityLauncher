set -e 

rm -rf UnityLauncher.Editor/bin/Release/

dotnet publish UnityLauncher.Editor -c Release -r osx.10.12-x64
mv UnityLauncher.Editor/bin/Release/netcoreapp2.0/osx.10.12-x64/UnityLauncher.Editor.Editor UnityLauncher.Editor/bin/Release/netcoreapp2.0/osx.10.12-x64/UnityLauncher.Editor
dotnet publish UnityLauncher.Editor -c Release -r linux-x64
mv UnityLauncher.Editor/bin/Release/netcoreapp2.0/linux-x64/UnityLauncher.Editor.Editor UnityLauncher.Editor/bin/Release/netcoreapp2.0/linux-x64/UnityLauncher.Editor
dotnet publish UnityLauncher.Editor -c Release -r win10-x64

(cd UnityLauncher.Editor/bin/Release/netcoreapp2.0 && zip -r -X UnityLauncher.Editor.zip .)

mv UnityLauncher.Editor/bin/Release/netcoreapp2.0/UnityLauncher.Editor.zip .

rm -rf UnityLauncher.Player/bin/Release/

dotnet publish UnityLauncher.Player -c Release -r osx.10.12-x64
mv UnityLauncher.Player/bin/Release/netcoreapp2.0/osx.10.12-x64/UnityLauncher.Player.Player UnityLauncher.Player/bin/Release/netcoreapp2.0/osx.10.12-x64/UnityLauncher.Player
dotnet publish UnityLauncher.Player -c Release -r linux-x64
mv UnityLauncher.Player/bin/Release/netcoreapp2.0/linux-x64/UnityLauncher.Player.Player UnityLauncher.Player/bin/Release/netcoreapp2.0/linux-x64/UnityLauncher.Player
dotnet publish UnityLauncher.Player -c Release -r win10-x64

(cd UnityLauncher.Player/bin/Release/netcoreapp2.0 && zip -r -X UnityLauncher.Player.zip .)

mv UnityLauncher.Player/bin/Release/netcoreapp2.0/UnityLauncher.Player.zip .
