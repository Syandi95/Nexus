Set WshShell = CreateObject("WScript.Shell")
WshShell.CurrentDirectory = "d:\NEXUS"
WshShell.Run "cmd /c ""%LOCALAPPDATA%\Temp\dotnet8\dotnet.exe"" build ""d:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\Nexus.App.csproj"" -c Debug && ""C:\Program Files\dotnet\dotnet.exe"" ""d:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows\Nexus.App.dll""", 1, False
