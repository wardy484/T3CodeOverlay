using Microsoft.Data.Sqlite;

namespace Browsingway;

internal sealed record T3ThreadStatus(int Working, int DirectAttention, int Completed, int Failed)
{
	public int NeedsAttention => DirectAttention + Completed + Failed;
	public static readonly T3ThreadStatus Empty = new(0, 0, 0, 0);
}

internal sealed class T3StatusMonitor : IDisposable
{
	private readonly string _pluginConfigDir;
	private readonly Timer _timer;
	private string _configuredDataDirectory;
	private string? _databasePath;
	private DateTime _acknowledgedAt;
	private T3ThreadStatus _status = T3ThreadStatus.Empty;
	private int _refreshing;
	private bool _loggedFailure;

	public T3StatusMonitor(string pluginConfigDir, string configuredDataDirectory, DateTime acknowledgedAt)
	{
		_pluginConfigDir = pluginConfigDir;
		_configuredDataDirectory = configuredDataDirectory;
		_databasePath = ResolveDatabasePath(pluginConfigDir, configuredDataDirectory);
		_acknowledgedAt = acknowledgedAt;
		_timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
	}

	public T3ThreadStatus Status => Volatile.Read(ref _status);
	public string? DatabasePath => Volatile.Read(ref _databasePath);

	public void SetDataDirectory(string dataDirectory)
	{
		_configuredDataDirectory = dataDirectory;
		Volatile.Write(ref _databasePath, ResolveDatabasePath(_pluginConfigDir, dataDirectory));
		Refresh();
	}

	public void Acknowledge(DateTime acknowledgedAt)
	{
		_acknowledgedAt = acknowledgedAt;
		Refresh();
	}

	public void Dispose() => _timer.Dispose();

	private void Refresh()
	{
		if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;

		try
		{
			string? databasePath = _databasePath;
			if (databasePath is null || !File.Exists(databasePath))
			{
				databasePath = ResolveDatabasePath(_pluginConfigDir, _configuredDataDirectory);
				Volatile.Write(ref _databasePath, databasePath);
			}
			if (databasePath is null)
			{
				Volatile.Write(ref _status, T3ThreadStatus.Empty);
				return;
			}

			SqliteConnectionStringBuilder builder = new()
			{
				DataSource = databasePath,
				Mode = SqliteOpenMode.ReadOnly,
				Pooling = false,
				DefaultTimeout = 1,
			};
			using SqliteConnection connection = new(builder.ConnectionString);
			connection.Open();
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				SELECT
					COALESCE(SUM(CASE WHEN s.status IN ('running', 'starting') THEN 1 ELSE 0 END), 0),
					COALESCE(SUM(CASE WHEN t.pending_approval_count > 0
						OR t.pending_user_input_count > 0
						OR t.has_actionable_proposed_plan > 0 THEN 1 ELSE 0 END), 0),
					COALESCE(SUM(CASE WHEN v.state = 'completed'
						AND v.completed_at IS NOT NULL
						AND v.completed_at > $acknowledgedAt THEN 1 ELSE 0 END), 0),
					COALESCE(SUM(CASE WHEN (s.status = 'error' OR v.state IN ('error', 'failed'))
						AND COALESCE(v.completed_at, t.updated_at) > $acknowledgedAt THEN 1 ELSE 0 END), 0)
				FROM projection_threads t
				LEFT JOIN projection_thread_sessions s ON s.thread_id = t.thread_id
				LEFT JOIN projection_turns v ON v.thread_id = t.thread_id AND v.turn_id = t.latest_turn_id
				WHERE t.deleted_at IS NULL AND t.archived_at IS NULL
				""";
			command.Parameters.AddWithValue("$acknowledgedAt", _acknowledgedAt.ToUniversalTime().ToString("O"));
			using SqliteDataReader reader = command.ExecuteReader();
			if (reader.Read())
			{
				Volatile.Write(ref _status, new T3ThreadStatus(
					reader.GetInt32(0),
					reader.GetInt32(1),
					reader.GetInt32(2),
					reader.GetInt32(3)));
			}
			_loggedFailure = false;
		}
		catch (Exception exception)
		{
			if (!_loggedFailure)
			{
				Services.PluginLog.Warning(exception, "Could not read T3 thread status");
				_loggedFailure = true;
			}
		}
		finally
		{
			Interlocked.Exchange(ref _refreshing, 0);
		}
	}

	internal static string? ResolveDatabasePath(string pluginConfigDir, string? configuredDataDirectory)
	{
		foreach (string candidate in GetCandidates(pluginConfigDir, configuredDataDirectory))
		{
			try
			{
				string fullPath = Path.GetFullPath(candidate);
				if (File.Exists(fullPath)) return fullPath;
			}
			catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
			{
				// Ignore malformed optional paths and continue with automatic discovery.
			}
		}

		return null;
	}

	private static IEnumerable<string> GetCandidates(string pluginConfigDir, string? configuredDataDirectory)
	{
		foreach (string path in ExpandDataDirectory(configuredDataDirectory)) yield return path;
		foreach (string path in ExpandDataDirectory(Environment.GetEnvironmentVariable("T3_DATA_DIR"))) yield return path;

		string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!String.IsNullOrWhiteSpace(userProfile))
		{
			yield return Path.Combine(userProfile, ".t3", "userdata", "state.sqlite");
		}

		DirectoryInfo? directory = new(pluginConfigDir);
		while (directory is not null)
		{
			yield return Path.Combine(directory.FullName, ".t3", "userdata", "state.sqlite");
			directory = directory.Parent;
		}
	}

	private static IEnumerable<string> ExpandDataDirectory(string? value)
	{
		if (String.IsNullOrWhiteSpace(value)) yield break;

		string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
		if (expanded == "~" || expanded.StartsWith($"~{Path.DirectorySeparatorChar}") || expanded.StartsWith($"~{Path.AltDirectorySeparatorChar}"))
		{
			expanded = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				expanded.Length == 1 ? "" : expanded[2..]);
		}

		if (String.Equals(Path.GetFileName(expanded), "state.sqlite", StringComparison.OrdinalIgnoreCase))
		{
			yield return expanded;
			yield break;
		}

		yield return Path.Combine(expanded, "state.sqlite");
		yield return Path.Combine(expanded, "userdata", "state.sqlite");
	}
}
