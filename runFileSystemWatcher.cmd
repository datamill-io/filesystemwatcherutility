@echo off
setlocal

if '%1==' goto noArgs
if not exist %1 goto noArgs

if exist %~dp0stopWatching.txt del %~dp0stopWatching.txt

if '%2=='stdout set sendToStdOut=stdout

call :watchIt %1
goto :xit

:watchIt
set target=%1
if NOT EXIST %target% goto :EOF

::rem listen for CREATE & DELETE, and send table rows to stdout
::rem restricting the output in an attempt to avoid the System.IO.InternalBufferOverflowException
start "%1" /D "%~dp0" cmd /k "%~dp0FSWatcherCmdLine.exe %target% created deleted %sendToStdOut%"
goto :EOF

:noArgs
echo usage: %~nx0 targetVolume
echo where targetVolume is the fully-qualified UNC path to start watching
goto :EOF

:xit
endlocal