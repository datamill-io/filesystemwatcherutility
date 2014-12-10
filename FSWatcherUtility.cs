namespace Commercehub.FileSystemWatcher {
	using System;
	using System.IO;
	using System.Security.Permissions;
	using System.Collections;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Configuration;
	using System.Data;
	using System.Data.SqlClient;
	using System.Text;
	using System.Text.RegularExpressions;
	using System.Threading;
	using log4net;

	public class FSWatcherUtility
	{
		private static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		private const int BUFFER_BLOCK = 4096; //minimum block of memory when creating a FileSystemWatcher object
		private const string eventsList = "created deleted changed renamed";
		private const string FORMAT_ISO8601 = "yyyy-MM-ddTHH:mm:ss.fffZ";
		private const string FORMAT_DATESTAMP = "yyyyMMdd-HHmmss";

		private static string[] ALL_EVENTS = eventsList.Split(new char[] { ' ' });
		private static Regex SqlEncoder = new Regex("(['?#])");

		private int MAXRETRIES = 3; // number of times to try committing a set of events
		private int COMMIT_SLEEP_MILLISECONDS; //after a commit failure, wait this long before trying again
		private int FSW_RESTART_INTERVAL_MINUTES; // interval between new instances of FileSystemWatcher
		private int COMMIT_EVENT_LIMIT; // max number of events to attempt to commit
		private int COMMIT_EVENT_MIN; // MINIMUM number of events before to attempt to commit
		private bool enableStdOut = false;
		private int maxQLen = 0;
		private string dbConnString;
		private FileSystemWatcher fsWatcher;
		private BackgroundWorker worker;
		private DoWorkEventArgs eventArgs;
		private ConcurrentQueue<FSEvent> eventQ = new ConcurrentQueue<FSEvent>();
		private string svcPath;
		private bool sqlTypeSelector = false;

		/// Constructor
		public FSWatcherUtility(String dbConnectionString) {
			this.dbConnString = dbConnectionString;
			if (String.IsNullOrEmpty(dbConnString)) {
				log.Warn("STDOUT enabled: Missing DBConnection string.");
				enableStdOut = true;
			}

			svcPath = Directory.GetParent(this.GetType().Assembly.Location).FullName;
			var appSettings = ConfigurationManager.AppSettings;

			COMMIT_SLEEP_MILLISECONDS = Int32.Parse(appSettings.Get("COMMIT_SLEEP_MILLISECONDS"));
			FSW_RESTART_INTERVAL_MINUTES = Int32.Parse(appSettings.Get("FSW_RESTART_INTERVAL_MINUTES"));
			COMMIT_EVENT_LIMIT = Int32.Parse(appSettings.Get("COMMIT_EVENT_LIMIT"));
			COMMIT_EVENT_MIN = Int32.Parse(appSettings.Get("COMMIT_EVENT_MIN"));
			MAXRETRIES = Int32.Parse(appSettings.Get("MAXRETRIES"));
		}

		public static string getDbConnInfo(String dbConnFileName) {
			//get the db connection string from a file
			string dbConnInfo = "";
			log.DebugFormat("Looking for DBConnection file: \'{0}\'", dbConnFileName);
			if (File.Exists(dbConnFileName))
			{
				StreamReader sr = File.OpenText(dbConnFileName);
				dbConnInfo = sr.ReadLine();
				sr.Close();
				string[] connStrings = dbConnInfo.Split(';');
				foreach (string setting in connStrings)
				{
					log.InfoFormat("DBConnection setting: \'{0}\'", setting);
				}
			}

			return dbConnInfo;
		}

		public void SetEvent(string eventName)
		{
			InfoMsg("Watching for event '{0}'", eventName);
			string eventNameLC = eventName.ToLower();
			switch (eventNameLC)
			{
				case "changed":
					fsWatcher.Changed += new FileSystemEventHandler(OnChanged);
				fsWatcher.NotifyFilter |= (NotifyFilters.LastAccess | NotifyFilters.LastWrite);
				break;
				case "created":
					fsWatcher.Created += new FileSystemEventHandler(OnChanged);
				fsWatcher.NotifyFilter |= (NotifyFilters.CreationTime);
				break;
				case "deleted":
					fsWatcher.Deleted += new FileSystemEventHandler(OnChanged);
				fsWatcher.NotifyFilter |= NotifyFilters.LastAccess;
				break;
				case "renamed":
					fsWatcher.Renamed += new RenamedEventHandler(OnRenamed);
				break;

				default:
					ErrMsg("Hmm... dunno what to do with event '{0}'", eventName);
				break;
			}
		}

		// Entry point for a BackgroundWorker
		public void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
		{
			// Get the BackgroundWorker that raised this event.
			worker = sender as BackgroundWorker;
			// ... and the DoWorkEventArgs.
			eventArgs = e;
			ArrayList args = (ArrayList) eventArgs.Argument;
			log.DebugFormat("backgroundWorker1_DoWork args length: {0}", args.Count);
			foreach (string arg in args) {
				log.DebugFormat("backgroundWorker1_DoWork arg: '{0}'", arg);
			}

			run(args);
		}

		private void run(ArrayList args)
		{
			string root = (string)args[1];

			if (! Directory.Exists(root))
			{
				ErrMsg("Hey! I can't find directory '{0}'", root);
				return;
			}

			log.InfoFormat("Watching for file events in: '{0}'", root);

			// process arguments passed in
			ArrayList handlers = new ArrayList();
			foreach (string arg in args) {
				log.DebugFormat("Processing argument: '{0}'", arg);
				if (arg.Length>0 && eventsList.Contains(arg)) handlers.Add(arg);
				if ("stdout".Equals(arg.ToLower())) enableStdOut = true;
			}

			while (!worker.CancellationPending)
			{
				DateTime timer = DateTime.Now.AddMinutes(FSW_RESTART_INTERVAL_MINUTES);
				log.InfoFormat("Using this FileSystemWatcher until {0:o}", timer);
				WatchForFiles(root, handlers);

				bool force = true;
				while (!(worker.CancellationPending || 0>= timer.CompareTo(DateTime.Now))) {
					Thread.Sleep(COMMIT_SLEEP_MILLISECONDS);
					// empty the Q
					saveData(!force);
					//log.DebugFormat("BackgroundWorker.IsBusy: {0}", worker.IsBusy);
				}

				if (worker.CancellationPending) {
					log.Info("Stop requested.");
				}

				// stop listening
				fsWatcher.EnableRaisingEvents = false;
				saveData(force); // flush the queue
			}

			eventArgs.Cancel = true;
		}

		private void WatchForFiles(string root, ArrayList eventTypes)
		{
			// Create a new FileSystemWatcher and set its properties.
			fsWatcher = new FileSystemWatcher(root);
			log.DebugFormat("Created new FileSystemWatcher on '{0}'", root);
			fsWatcher.Error += new ErrorEventHandler(OnFSError);

			// Detect changes in subdirectories
			fsWatcher.IncludeSubdirectories = true;

			// Only watch text files.
			//fsWatcher.Filter = "*.txt";

			// Only watch file events, not directories
			fsWatcher.NotifyFilter = NotifyFilters.FileName;

			// Add events.
			if (eventTypes.Count>1)
			{
				foreach (string eventType in eventTypes)
				{
					if (eventsList.Contains(eventType)) SetEvent(eventType);
					log.DebugFormat("Added event '{0}'", eventType);
				}
			}
			else
			{
				log.Debug("Adding ALL events");
				foreach (string eventType in ALL_EVENTS)
				{
					SetEvent(eventType);
				}
			}

			// Minimum = 1 block; maximum = 16 blocks
			fsWatcher.InternalBufferSize = BUFFER_BLOCK * 16;
			log.DebugFormat("fsWatcher.InternalBufferSize:\t{0}", fsWatcher.InternalBufferSize);

			// Begin watching.
			fsWatcher.EnableRaisingEvents = true;

			log.Info("Started FileSystemWatcher.");
		}

		// Define the event handlers. 
		private void OnChanged(object source, FileSystemEventArgs e)
		{
			//log.DebugFormat("Change: {0}", e.ToString());
			// Specify what is done when a file is changed, created, or deleted.

			eventQ.Enqueue(new FSEvent(e));
		}

		private void OnRenamed(object source, RenamedEventArgs e)
		{
			// Specify what is done when a file is renamed.
			//log.DebugFormat("File: {0} renamed to {1}", e.OldFullPath, e.FullPath);

			eventQ.Enqueue(new FSEvent(e));
		}

		private void OnFSError(object source, ErrorEventArgs e)
		{
			// Show what the error was.
			log.Error("FileSystemWatcher Error Event", e.GetException());
		}

		/* Table schema
				[directory] [varchar](255) NULL,

				[file_name] [varchar](255) NULL,

				[file_extension] [varchar](32) NULL,

				[change_type] [varchar](32) NULL,

				[create_date] [datetime2](3) NULL,

			*/

		private const string SQL_INSERT = "INSERT MN_File_System_Mgmt ([create_date],[directory],[file_name],[file_extension],[change_type]) VALUES ";
		private const string SQL_VALUES = " (@cdate, @dir, @fname, @extn, @chgType)";
		/*
		Create a SqlCommand, prepared and ready to accept values
		*/
		private static SqlCommand createTemplateSqlCommand(SqlConnection connection) {
			SqlCommand command = new SqlCommand(SQL_INSERT + SQL_VALUES, connection);
			SqlParameter create_date_Param = new SqlParameter("@cdate", SqlDbType.DateTime2, 3);
			create_date_Param.Value = "2014-10-10T12:00:00Z";
			command.Parameters.Add(create_date_Param);
			SqlParameter dir_Param = new SqlParameter("@dir", SqlDbType.VarChar, 255);
			dir_Param.Value = "foo";
			command.Parameters.Add(dir_Param);
			SqlParameter fname_Param = new SqlParameter("@fname", SqlDbType.VarChar, 255);
			fname_Param.Value = "bar";
			command.Parameters.Add(fname_Param);
			SqlParameter extn_Param = new SqlParameter("@extn", SqlDbType.VarChar, 32);
			extn_Param.Value = "txt";
			command.Parameters.Add(extn_Param);
			SqlParameter chgTyp_Param = new SqlParameter("@chgType", SqlDbType.VarChar, 32);
			chgTyp_Param.Value = "changed";
			command.Parameters.Add(chgTyp_Param);
			command.Prepare();

			return command;
		}

		private static SqlCommand createOneRowSqlCommand(SqlCommand command, FSEvent fse) {
			//command.Parameters.Clear

			command.Parameters["@cdate"].Value = fse.getTimeStamp();

			command.Parameters["@dir"].Value = fse.getDirectory();

			command.Parameters["@fname"].Value = fse.getFileName();

			command.Parameters["@extn"].Value = fse.getExtension();

			command.Parameters["@chgType"].Value = fse.getChangeType();

			return command;
		}

		private string buildSingleSQL(IEnumerable<FSEvent> events) {
			StringBuilder sb = new StringBuilder(SQL_INSERT);
			foreach (FSEvent evt in events) {
				String fileName = SqlEncoder.Replace(evt.getFileName(), "'$1");
				sb.AppendFormat("('{0:o}','{1}','{2}','{3}','{4}'),", evt.getTimeStamp(), evt.getDirectory(), fileName, evt.getExtension(), evt.getChangeType());
			}

			int len = sb.Length;
			sb.Remove(len-1, 1); // trim the last comma
			return sb.ToString();
		}

		private void saveData(bool forceACommit)
		{
			if (!forceACommit && COMMIT_EVENT_MIN>eventQ.Count) {
				//log.Debug("No events found to emit.");
				return;
			}
			else {
				// After some testing, it appears that a single Insert statement with multiple values has
				// better performance than multiple Insert statements.
				sqlTypeSelector = false; // always use the Single Statement
				//sqlTypeSelector = sqlTypeSelector ^ true; // flip the type of SQL each time
					bool allDone = false;
					while (!allDone) { //Try until finished
						log.DebugFormat("Emitting {0} events", eventQ.Count);
						if (maxQLen < eventQ.Count) {
							log.WarnFormat("┌┴┬┘  Longest queue increased from {0} to {1}", maxQLen, eventQ.Count);
							maxQLen = eventQ.Count;
						}

						FSEvent fse = null;
						bool gotOne = false;
						List<FSEvent> sqlInsertList = new List<FSEvent>(COMMIT_EVENT_LIMIT);
						int totalProcessed = 0; // yes, this serves two purposes: to limit the number of rows
						// AND to break out of here if the TryDequeue keeps failing for some reason
						while (!eventQ.IsEmpty && COMMIT_EVENT_LIMIT > totalProcessed++) {
							gotOne = eventQ.TryDequeue(out fse);

							if (!gotOne) {
								log.Debug("eventQ.TryDequeue() failed.  whatever.");
							}
							else {
								sqlInsertList.Add(fse); // save it for the SQL insert
								if (enableStdOut || log.IsDebugEnabled) {
									string visibleRecord = String.Format("{0:o}\t{1}\t{2}\t{3}\t{4}", fse.getTimeStamp(), fse.getDirectory(), fse.getFileName(), fse.getExtension(), fse.getChangeType());
									if (enableStdOut) Console.Out.WriteLine("{0}", visibleRecord);
									log.DebugFormat("Event: {0}", visibleRecord);
								}
							}
						}

						using (SqlConnection myConnection = new SqlConnection(dbConnString)) {
						myConnection.Open();
						if (0<sqlInsertList.Count) {
							int tries = 0;
							bool doneTrying = false;
							SqlCommand sqlCmd = createTemplateSqlCommand(myConnection);
							while (!doneTrying) {
								++tries;
								SqlTransaction transaction = null;
								try {
									DateTime startTime = DateTime.Now;
									transaction = myConnection.BeginTransaction("FSEventInserts");
									sqlCmd.Transaction = transaction;

									String sqlType = (sqlTypeSelector) ? "Multi-statement" : "Single statement";
									int rows = 0;
									if (null == transaction) { // probably not necessary
										log.WarnFormat("Error getting a SqlTransaction");
									} else {
										log.DebugFormat("transaction.IsolationLevel: {0}", transaction.IsolationLevel);
										//# # # # # # # # # # # # # # # # # # # # # # # # # # # # #
										if (sqlType.Equals("Multi-statement")) { //Multiple statement execution
											foreach (FSEvent fsEvent in sqlInsertList) {
												sqlCmd = createOneRowSqlCommand(sqlCmd, fsEvent);
												rows += sqlCmd.ExecuteNonQuery();
											}
										}
										//# # # # # # # # # # # # # # # # # # # # # # # # # # # # # 
										else { //Single statement execution
											sqlCmd.CommandText = buildSingleSQL(sqlInsertList);
											log.DebugFormat("Try #{0}", tries);
											rows = sqlCmd.ExecuteNonQuery();
										}
										transaction.Commit();

										// if we got here, it's good
										sqlInsertList.Clear();
										doneTrying = true;
										log.DebugFormat("Finished on try #{0}", tries);
									}

									DateTime endTime = DateTime.Now;
									TimeSpan elapsedTime = endTime-startTime;
									log.DebugFormat("{2}: {1} rows saved in time: {0:G}", elapsedTime, rows, sqlType);
								}
								catch (SqlException t) {
									log.ErrorFormat("Error during commit try #{3}: Code: {4}\n{0}\n{1}\n{2}", t.Message, t.StackTrace, DictToString(t.Data, ""), tries, t.ErrorCode);
									doneTrying = (t.Message.Contains("Incorrect syntax near") || MAXRETRIES<tries);
									if (!doneTrying) {
										Thread.Sleep(500); // wait a bit to see if the connection restores
									}
									else {
										//save the SQL to a file
										string dtStamp = "\\FSWatcher-SQL-ERROR-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
										string fName = svcPath + dtStamp + ".txt";
										StreamWriter writer = new StreamWriter(fName);
										writer.WriteLine(sqlCmd.CommandText);
										writer.Flush();
										writer.Close();
										log.ErrorFormat("Wrote error file '{0}'", fName);
									}
								}

								finally {
									log.DebugFormat("transaction.Connection: {0}", transaction.Connection);
									if (null!= transaction && null!= transaction.Connection) {
										transaction.Rollback();
									}
								}
							}

							if (0<sqlInsertList.Count) {
								log.ErrorFormat("Rows lost: {0}", sqlInsertList.Count);
							}
						}

						//if (!String.IsNullOrEmpty(dbConnString))

							log.DebugFormat("Remaining events: {0}", eventQ.Count);
						allDone = (forceACommit && eventQ.IsEmpty) || (COMMIT_EVENT_MIN>eventQ.Count);
					}// using (myConnection)
				} // while (!allDone)
			}
		}

		private static void InfoMsg(string formatMessage, params object[] args)
		{
			//Console.Out.WriteLine(formatMessage, args);
			log.InfoFormat(formatMessage, args);
		}

		private static void ErrMsg(string formatMessage, params object[] args)
		{
			//Console.Error.WriteLine(formatMessage, args);
			log.ErrorFormat(formatMessage, args);
		}

		private string DictToString(IDictionary items, string format)
		{
			format = String.IsNullOrEmpty(format) ? "{0}='{1}' " : format;

			StringBuilder itemString = new StringBuilder();
			foreach(DictionaryEntry item in items)
				itemString.AppendFormat(format, item.Key, item.Value);

			return itemString.ToString();
		}
	}

	class FSEvent {
		private DateTime timeStamp = DateTime.Now;
		private FileSystemEventArgs fseArgs;

		public FSEvent(FileSystemEventArgs args) {
			this.fseArgs = args;
		}

		public string getDirectory() {
			return Path.GetDirectoryName(this.fseArgs.FullPath);
		}

		public DateTime getTimeStamp() {
			return this.timeStamp;
		}

		public string getFileName() {
			return Path.GetFileNameWithoutExtension(this.fseArgs.FullPath);
		}

		public string getExtension() {
			return Path.GetExtension(this.fseArgs.FullPath);
		}

		public string getChangeType() {
			return this.fseArgs.ChangeType.ToString();
		}
	}
}