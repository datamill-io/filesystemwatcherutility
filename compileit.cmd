@echo off
setlocal
if "%nethome%"=="" set nethome=%windir%\Microsoft.NET\Framework\v4.0.30319
path %nethome%;%path%
pushd %~dp0
del FSWatcher*.dll FSWatcher*.exe

@echo on
csc /t:library /r:System.Data.dll,log4net.dll /out:FSWatcherUtility.dll FSWatcherUtility.cs  
if not exist FSWatcherUtility.dll goto :EOF

csc /r:System.ServiceProcess.dll,FSWatcherUtility.dll,log4net.dll FSWatcherService.cs 
if not exist FSWatcherService.exe goto :EOF

csc /r:FSWatcherUtility.dll,System.Data.dll,log4net.dll FSWatcherCmdLine.cs 
if not exist FSWatcherCmdLine.exe goto :EOF

@echo off
copy /y FSWatcherUtility.xml FSWatcherService.exe.config
copy /y FSWatcherUtility.xml FSWatcherCmdLine.exe.config

popd
endlocal