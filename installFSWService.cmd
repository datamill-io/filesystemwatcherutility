@echo off
setlocal
set svc=FileSystemWatcher
if '%1 EQU 'uninstall goto uninstall

:create
if '%1 EQU ' goto syntax
set monitorPath=%1
set /P mypwd=Enter your password: 
cls
sc \\%computername% create %svc% binPath= "%~dp0FSWatcherService.exe %monitorPath% created deleted" DisplayName= "File System Watcher" type= own start= auto error= normal obj= %userdomain%\%username% password= %mypwd%
sc description %svc% "Watches a remote file path for create, change, rename, and delete events.  Saves the events to a SQL Server table for later analysis."
sc qc %svc%
net start %svc%
start services.msc
start eventvwr
goto :xit

:uninstall
net stop %svc%
sc \\%computername% delete %svc% 
goto :xit

:syntax
echo syntax: %~nx0 uninstall 
echo         will uninstall the iexisting FileSystemWatcher service
echo syntax: %~nx0 Your\Target\Directory 
echo         will install FileSystemWatcher service to detect changes in directory Your\Target\Directory 
:xit
endlocal