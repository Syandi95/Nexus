Set WshShell = CreateObject("WScript.Shell")
WshShell.CurrentDirectory = "d:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows"
WshShell.Run """C:\Program Files\dotnet\dotnet.exe"" ""d:\NEXUS\Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows\Nexus.App.dll""", 1, False
