JUST GET ME RUNNING!
--------------------
1. Get Read-Write access to a SQL Server instance & database
2. Create table `MN_File_System_Mgmt` in a database by running the SQL in file `fswatcher.sql`
3. Get the log4net dependency from http://logging.apache.org/log4net/download_log4net.cgi , and copy the appropriate DLL to the source directory
4. Compile the source code.  `compileit.cmd` should work.
5. Modify the configuration file `FSWatcherCmdLine.exe.config` to match the connection to your database
6. Run batch script `runFileSystemWatcher.cmd <yourTargetDirectory>`
7. Create or delete some files in the targetDirectory, and check your database table for events.
  

Operation
---------
The class FSWatcherUtility detects changes in a directory tree.  Whenever a file in the directory tree is created, renamed, changed or deleted, that event is captured and saved to a database.  FSWatcherUtility leverages the .NET class [System.IO.FileSystemWatcher](http://msdn.microsoft.com/en-us/library/system.io.filesystemwatcher(v=vs.110).aspx) to do most of the work.  The class is driven in a stand-alone executable, or in a service.

#### Command-line interface:
Run `FSWatcherCmdLine.exe` with no arguments to get a list of (required) and [optional] arguments.  Data collected can be sent to stdout by including the argument `stdout`.

Batch script `runFileSystemWatcher.cmd` detects basic requirements, and starts `FSWatcherCmdLine` to listen for `created` and `deleted` events.

To stop `FSWatcherCmdLine`, press 'q' in the running command prompt.  Alternatively, create a file named `stopWatching.txt`, and the utility will exit.

#### Windows Service:
`FSWatcherService.exe` must be registered as a service to run.  Batch script `installFSWService.cmd` will handle most of the details.  By default, the service is registered to run as you, and you are prompted for your password.

#### Dependencies:
Both executables store data in a database table named `MN_File_System_Mgmt`.  Configuration files for each executable have database connection details.  SQL for creating table `MN_File_System_Mgmt` is in file `fswatcher.sql`.

Both executables use the Apache library `log4net` to manage logging; a default configuration file `log4net-config.xml` is provided.  To run either executable, you must have file `log4net-config.xml` in the same directory with the executable.  By default, both executables log messages to the Windows Event log, and to a Console logger.  More information on configuring log4net can be found at the [Log4Net Configuration page](http://logging.apache.org/log4net/release/manual/configuration.html).

A custom view for EventViewer is included that selects all events from FileSystemWatcher executables.  Import the file `FSWatcherService_EventView.xml` into EventViewer to create the custom view selecting only those events.

Build Instructions
------------------
To compile, you need either:

* Mono: [http://www.mono-project.com/](http://www.mono-project.com/)
* Microsoft .NET v2.0 or later

Both executables require assembly `log4net.dll` for .NET v2.0.  Get log4net at: [Apache's Log4Net page](http://logging.apache.org/log4net/download_log4net.cgi)

The batch script `compileit.cmd` will compile using MS.NET compiler.  Set environment variable `nethome` to your .NET install location (defaulting to `%windir%\Microsoft.NET\Framework\v4.0.30319`), and you should be able to compile.

Similarly, the batch script `monocompileit.cmd` will compile using the Mono compiler.  It expects the environment variable `mono-home` will be set to the path of your Mono installation (defaulting `C:\Program Files (x86)\Mono-3.2.3`).

Architecture
------------
FSWatcherUtility uses two separate threads to handle two types of tasks:

1. Catching events from a `System.IO.FileSystemWatcher` instance
2. Saving the events to a database table

`FSWatcherService` and `FSWatcherCmdLine` create a `System.ComponentModel.BackgroundWorker` instance to manage the thread catching the events.  The original execution thread handles polling for events, saving any events to the database, and exiting.

While `System.IO.FileSystemWatcher` can be configured to trigger events for directories as well, FSWatcherUtility only listens for file events.

