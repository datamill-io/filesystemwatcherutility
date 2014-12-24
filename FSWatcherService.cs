namespace Commercehub.FileSystemWatcher {
	using System;
	using System.Collections;
	using System.ComponentModel;
	using System.Configuration;
	using System.Data.SqlClient;
	using System.Diagnostics;
	using System.IO;
	using System.ServiceProcess;
	using System.Threading;
	using log4net;
	using log4net.Config;

	class FSWatcherService : ServiceBase
	{
		private static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		protected FSWatcherUtility watcher;
		protected BackgroundWorker worker;
		protected ArrayList listOfArgs;
		protected string[] cmdLineArgs;
		protected int retries = 0;
		protected DateTime lastTime;

		private const string dbConnFileName = "dbConn.txt";
		private static TimeSpan MIN_RETRY_TIMESPAN = TimeSpan.Parse("00:00:01");
		private const int MAX_RETRIES = 5;
		private const int RETRY_DELAY_MILLISECONDS = 300;

		/// <summary>
		/// Public Constructor for FSWatcherService.
		/// - Put all of your Initialization code here.
		/// </summary>
        public FSWatcherService()
		{
			string svcPath = Directory.GetParent(this.GetType().Assembly.Location).FullName;
			string logConfig = Path.Combine(svcPath, "log4net-config.xml");
			if (File.Exists(logConfig))
			{
				XmlConfigurator.Configure(new System.IO.FileInfo(logConfig));
			}
			else
			{
				Console.Error.WriteLine("Log configuration '{0}' not found; defaulting to BasicConfigurator", logConfig);
				BasicConfigurator.Configure();
			}

			this.ServiceName = "File System Watcher";
			this.EventLog.Log = "Application";

			// These Flags set whether or not to handle that specific
			//  type of event. Set to true if you need it, false otherwise.
            this.CanHandlePowerEvent = false;
			this.CanHandleSessionChangeEvent = false;
			this.CanPauseAndContinue = true;
			this.CanShutdown = true;
			this.CanStop = true;

			// string dbConnFullPath = Path.Combine(svcPath, dbConnFileName);
			// if (!File.Exists(dbConnFullPath)) {
			// throw new System.IO.FileNotFoundException("DB Connection file missing.", dbConnFullPath);
			// }

			string dbConnectionString;
			// dbConnectionString = FSWatcherUtility.getDbConnInfo(dbConnFullPath);

			var appSettings = ConfigurationManager.AppSettings;
			dbConnectionString = appSettings.Get("DB_CONNECTION");
			if (!String.IsNullOrEmpty(dbConnectionString)) {
				// Try it out and see if it works
				using (SqlConnection myConnection = new SqlConnection(dbConnectionString)) {
					myConnection.Open();
					SqlCommand sqlCmd = myConnection.CreateCommand();
					log.DebugFormat("Database info: {1} on server {0}", myConnection.DataSource, myConnection.Database);
				}
			}

			// command-line arguments aren't passed in via OnStart(string[] args)
			// go figure
			cmdLineArgs = Environment.GetCommandLineArgs();
			watcher = new FSWatcherUtility(dbConnectionString);
		}

		// This event handler deals with the results of the 
		// background operation. 
		private void backgroundWorker1_RunWorkerCompleted(
			object sender, RunWorkerCompletedEventArgs e)
		{
		}

		/// <summary>
		/// The Main Thread: This is where your Service is Run.
		/// </summary>
        static void Main()
		{
			ServiceBase.Run(new FSWatcherService());
		}

		/// <summary>
		/// Dispose of objects that need it here.
		/// </summary>
		/// <param name="disposing">Whether
		///    or not disposing is going on.</param>
        protected override void Dispose(bool disposing)
		{
			log.Debug("Dispose called.");
			base.Dispose(disposing);
		}

		/// <summary>
		/// OnStart(): Put startup code here
		///  - Start threads, get inital data, etc.
		/// </summary>
		/// <param name="args"></param>
        protected override void OnStart(string[] args)
		{
			base.OnStart(args);
			log.Info("Starting File System Watcher");
			log.DebugFormat("Number of args: {0}", args.Length);
			startNewWorker();
		}

		protected void startNewWorker()
		{
			if (null != worker) {
				worker.CancelAsync(); // stop the existing worker
				while (worker.IsBusy) {
					Thread.Sleep(50);
				}
				worker.Dispose();
			}
			
			worker = new BackgroundWorker();
			worker.WorkerSupportsCancellation = true;
			worker.DoWork += new DoWorkEventHandler(watcher.backgroundWorker1_DoWork);
			worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(this.runWorkerCompletedHandler);
			listOfArgs = new ArrayList(cmdLineArgs);

			worker.RunWorkerAsync(listOfArgs);
		}

		public void runWorkerCompletedHandler(object sender, RunWorkerCompletedEventArgs e) {
			// check for AsyncCompletedEventArgs.Error and AsyncCompletedEventArgs.Cancelled
			if (null != e.Error) { // then we got an error
				//re-start the worker
				log.ErrorFormat("RunWorkerCompleted Error : {0}", e.Error);
				if (tryAgain()) {
					startNewWorker();
				}				else {
					log.Error("Too many retries; bailing.");
					this.Stop();
				}
			}
		}

		// circuit breaker to stop trying to restart
		private bool tryAgain() {
			TimeSpan timeSinceLastRetry = (DateTime.Now - lastTime);
			if (timeSinceLastRetry > MIN_RETRY_TIMESPAN) { // if it's been a while, reset retries
				retries = 0;
			}

			if (++retries > MAX_RETRIES) return false;

			System.Threading.Thread.Sleep(RETRY_DELAY_MILLISECONDS); // maybe a network glitch
			lastTime = DateTime.Now;
			return true;
		}

		/// <summary>
		/// OnStop(): Put your stop code here
		/// - Stop threads, set final data, etc.
		/// </summary>
        protected override void OnStop()
		{
			log.Info("Stop requested");
			worker.CancelAsync();
			base.OnStop();
		}

		/// <summary>
		/// OnPause: Put your pause code here
		/// - Pause working threads, etc.
		/// </summary>
        protected override void OnPause()
		{
			log.Debug("Pause requested");
			worker.CancelAsync();
			base.OnPause();
		}

		/// <summary>
		/// OnContinue(): Put your continue code here
		/// - Un-pause working threads, etc.
		/// </summary>
        protected override void OnContinue()
		{
			log.Debug("Continue requested");
			base.OnContinue();
			worker.RunWorkerAsync(listOfArgs);
		}

		/// <summary>
		/// OnShutdown(): Called when the System is shutting down
		/// - Put code here when you need special handling
		///   of code that deals with a system shutdown, such
		///   as saving special data before shutdown.
		/// </summary>
        protected override void OnShutdown()
		{
			log.Debug("Shutdown requested");
			worker.CancelAsync();
			base.OnShutdown();
		}

		/// <summary>
		/// OnCustomCommand(): If you need to send a command to your
		///   service without the need for Remoting or Sockets, use
		///   this method to do custom methods.
		/// </summary>
		/// <param name="command">Arbitrary Integer between 128 & 256</param>
        protected override void OnCustomCommand(int command)
		{
			log.DebugFormat("Custom command: {0}", command);
			//  A custom command can be sent to a service by using this method:
			//#  int command = 128; //Some Arbitrary number between 128 & 256
			//#  ServiceController sc = new ServiceController("NameOfService");
			//#  sc.ExecuteCommand(command);

			base.OnCustomCommand(command);
		}

		/// <summary>
		/// OnPowerEvent(): Useful for detecting power status changes,
		///   such as going into Suspend mode or Low Battery for laptops.
		/// </summary>
		/// <param name="powerStatus">The Power Broadcast Status
		/// (BatteryLow, Suspend, etc.)</param>
        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
		{
			log.DebugFormat("Power Event: {0}", powerStatus.ToString());

			return base.OnPowerEvent(powerStatus);
		}

		/// <summary>
		/// OnSessionChange(): To handle a change event
		///   from a Terminal Server session.
		///   Useful if you need to determine
		///   when a user logs in remotely or logs off,
		///   or when someone logs into the console.
		/// </summary>
		/// <param name="changeDescription">The Session Change
		/// Event that occured.</param>
        protected override void OnSessionChange(SessionChangeDescription changeDescription)
		{
			log.DebugFormat("Session Change: {0}", changeDescription.ToString());
			base.OnSessionChange(changeDescription);
		}
	}
}