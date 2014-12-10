namespace Commercehub.FileSystemWatcher {
	using System;
	using System.Data.SqlClient;
	using System.IO;
	using System.Collections;
	using System.ComponentModel;
	using System.Configuration;
	using log4net;
	using log4net.Config;

	class FSWatcherCmdLine
	{
		private static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		// private const string dbConnFileName = "dbConn.txt";
		private static int THREE_SECONDS_MILLIS = 3000;
		private static string dbConnectionString;

		public static void Main()
		{
			string logPath = Directory.GetParent(log.GetType().Assembly.Location).FullName;
			string logConfig = Path.Combine(logPath, "log4net-config.xml");
			if (File.Exists(logConfig))
			{
				Console.Out.WriteLine("logConfig: {0}", logConfig);
				XmlConfigurator.Configure(new System.IO.FileInfo(logConfig));
			}
			else
			{
				Console.Error.WriteLine("Log configuration '{0}' not found; defaulting to BasicConfigurator", logConfig);
				BasicConfigurator.Configure();
			}

			string[] args = System.Environment.GetCommandLineArgs();
			log.DebugFormat("args; {0}", args);
			// If a directory is not specified, exit program. 
			if (args != null && args.Length < 2)
			{
				// Display the proper way to call the program.
				Console.Error.WriteLine(@"Usage: FSWatcherUtility (D:\mydirectory) [stdout] [created] [deleted] [changed] [renamed]");
				Console.Error.WriteLine(@"where  (D:\mydirectory) is the directory to watch for files.");
				Console.Error.WriteLine("include 'stdout' to mirror the data to stdout.");
				Console.Error.WriteLine("select from 'created' 'deleted' 'changed' 'renamed' to indicate which events to log.  Log all events by including no event types.");
				return;
			}
			else
			{
				string root = (args[1]);

				if (! Directory.Exists(root))
				{
					Console.Error.WriteLine("Hey! I can't find directory '{0}'", root);
					return;
				}
			}

			string signalFile = Directory.GetCurrentDirectory() + @"\stopWatching.txt";
			Console.Out.WriteLine("\nPress \'q\' to quit the program, or create a file named '"+signalFile+"'.");
			System.Threading.Thread.Sleep(5000); //5 seconds

			// dbConnectionString = FSWatcherUtility.getDbConnInfo(dbConnFullPath);
			dbConnectionString = ConfigurationManager.AppSettings.Get("DB_CONNECTION");
			log.DebugFormat("DB_CONNECTION: '{0}'", dbConnectionString);

			if (!String.IsNullOrEmpty(dbConnectionString)) {
				// Try it out and see if it works
				SqlConnection myConnection = new SqlConnection(dbConnectionString);
				try {
					myConnection.Open();
					SqlCommand sqlCmd = myConnection.CreateCommand();
					log.DebugFormat("Database info: {1} on server {0}", myConnection.DataSource, myConnection.Database);
				}

				finally {
					myConnection.Close();
				}
			}

			BackgroundWorker worker = null;
			bool isKnownIssue = true;
			while (isKnownIssue) {
				try {
					log.Info("Starting new BackgoundWorker");
					isKnownIssue = false;
					worker = new BackgroundWorker();
					worker.WorkerSupportsCancellation = true;
					worker.DoWork += new DoWorkEventHandler(new FSWatcherUtility(dbConnectionString).backgroundWorker1_DoWork);
					ArrayList listOfArgs = new ArrayList(args);
					worker.RunWorkerAsync(listOfArgs);
					bool isFinished = false;
					while (worker.IsBusy) {
						System.Threading.Thread.Sleep(THREE_SECONDS_MILLIS); // 3 seconds
						// Wait for the user to quit the program.
						if (Console.KeyAvailable) {
							char keypress = Convert.ToChar(Console.Read());
							isFinished = (keypress == 'q' || keypress == 'Q');
						}

						isFinished |= (File.Exists(signalFile));
						if (isFinished) worker.CancelAsync();
					}
				}
				catch (System.IO.InternalBufferOverflowException e) {
					isKnownIssue = true;
					log.ErrorFormat("Buffer Overflow: \n{0}\n{1}\n{2}" , e.Message, e.Source, e.StackTrace);
				}
				catch (Exception e) {
					log.ErrorFormat("Unknown Exception: \n{0}\n{1}\n{2}" , e.Message, e.Source, e.StackTrace);
				}

				finally {
					if (worker != null) {
						worker.CancelAsync();
					}
				}
			}
		}
	}
}