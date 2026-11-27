@echo off
setlocal
set "project=%~dp0..\iRacingSDK.Net.Tests"

msbuild "%project%\iRacingSDK.Net.Tests.csproj" -restore -v:minimal -p:Configuration=Debug -p:Platform=AnyCPU
if errorlevel 1 exit /b %errorlevel%

"%VSINSTALLDIR%Common7\IDE\Extensions\TestPlatform\vstest.console.exe" ^
    "%project%\bin\Debug\iRacingSDK.Net.Tests.exe" /Platform:x86 ^
    "/ResultsDirectory:%project%\bin\Debug\TestResults"
