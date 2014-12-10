::@echo off
setlocal
if "%mono-home%"=="" set mono-home=C:\Program Files (x86)\Mono-3.2.3
path %mono-home%\bin;%path%
pushd %~dp0
del FS*.dll FS*.exe
set utl=-reference:System.Configuration.dll,System.Data.dll,FSWatcherUtility.dll,log4net.dll

@echo on
call mcs -target:library -reference:System.Configuration.dll,System.Data.dll,log4net.dll -out:FSWatcherUtility.dll FSWatcherUtility.cs 
if not exist FSWatcherUtility.dll goto :EOF
call mcs %utl%,System.ServiceProcess.dll FSWatcherService.cs 
call mcs %utl% FSWatcherCmdLine.cs 
@echo off
popd
endlocal