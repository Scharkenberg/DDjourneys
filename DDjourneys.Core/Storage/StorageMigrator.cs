namespace DDjourneys.Core.Storage;

/// <summary>The few operations stored data needs; MAUI Preferences on a device, a dictionary in tests.</summary>
public interface IKeyValueStore
{
	string? Get(string key);

	void Set(string key, string value);

	void Remove(string key);
}

/// <summary>One step from the schema version before it to <see cref="Version"/>.</summary>
public sealed record StorageMigration(int Version, string Name, Action<IKeyValueStore> Apply);

/// <summary>
/// Brings everything the app keeps on the device to the current schema, step by step, once per version.
/// A step that fails is skipped without raising the version, so it runs again on the next start and the data
/// it could not convert stays as it was; later steps still run. Data of a NEWER version (after a downgrade) is
/// never touched. The tolerant readers cope with it.
/// </summary>
public sealed class StorageMigrator(IKeyValueStore store, IReadOnlyList<StorageMigration> migrations)
{
	public const string VersionKey = "storage.version";

	private readonly IKeyValueStore _store = store ?? throw new ArgumentNullException(nameof(store));
	private readonly IReadOnlyList<StorageMigration> _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));

	/// <returns>The names of the steps that ran.</returns>
	public IReadOnlyList<string> Run()
	{
		var ran = new List<string>();
		int stored = ReadVersion();
		int newest = _migrations.Count == 0 ? 0 : _migrations.Max(item => item.Version);

		if (stored >= newest)
		{
			return ran;
		}

		int reached = stored;
		bool failed = false;

		foreach (StorageMigration step in _migrations.Where(item => item.Version > stored).OrderBy(item => item.Version))
		{
			try
			{
				step.Apply(_store);
				ran.Add(step.Name);

				if (!failed)
				{
					reached = step.Version;
				}
			}
			catch (Exception ex)
			{
				failed = true;
				System.Diagnostics.Debug.WriteLine($"Storage migration '{step.Name}' failed: {ex.Message}");
			}
		}

		if (reached > stored)
		{
			try
			{
				_store.Set(VersionKey, reached.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Storage version not saved: {ex.Message}");
			}
		}

		return ran;
	}

	private int ReadVersion()
	{
		try
		{
			return int.TryParse(_store.Get(VersionKey), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int version)
				&& version >= 0
					? version
					: 0;
		}
		catch
		{
			return 0;
		}
	}
}
