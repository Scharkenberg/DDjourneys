using System.Net.Http;
using System.Security.Cryptography;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Support;

/// <summary>A stop to find on a plan, as another page knows it (any provider's id, name, place).</summary>
public sealed record ZonePlanStopRef(string? Id, string Name, string? Place);

/// <summary>What the zone plan opens with (<see cref="Routes.ZonePlanTarget"/>): a journey's two ends, marked and in view.</summary>
public sealed record ZonePlanTarget(ZonePlanStopRef? From, ZonePlanStopRef? To);

/// <summary>An opened VVO plan: its tiles, its stops, and whether it is the current edition (false: offline, the saved one).</summary>
public sealed record ZonePlan(VvoZonePlanKind Kind, RemotePlanTiles Tiles, IReadOnlyList<VvoPlanPin> Pins, bool Fresh);

/// <summary>
/// The VVO's interactive plans (fare zones of the region, Dresden network) on the device. Each opening asks the
/// server for the pin list and the few tiles that name the edition; together they are the edition's fingerprint and
/// its folder, so a new plan starts a new folder and an old one is dropped. Without a connection the last edition
/// opens as it was saved. Kept in the app data (not the cache the system may empty), because it is a plan for
/// offline use. All rights to the plans stay with the VVO and the DVB.
/// </summary>
public sealed class ZonePlanStore(ApiClient api)
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	private static string Root => Path.Combine(FileSystem.AppDataDirectory, "plans");

	public async Task<ZonePlan> OpenAsync(VvoZonePlanKind kind, CancellationToken cancellationToken)
	{
		string name = kind == VvoZonePlanKind.Region ? "vvo-region" : "vvo-dresden";
		string pointer = Path.Combine(Root, name + ".current");

		Directory.CreateDirectory(Root);

		(string key, IReadOnlyList<VvoPlanPin> pins, bool fresh) edition;

		try
		{
			edition = await FetchAsync(kind, name, pointer, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or InvalidDataException
			|| (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			DiagnosticLog.Write($"[Zone plan] {name}: current edition not loaded, opening the saved one: {ex.Message}");

			edition = await SavedAsync(pointer, cancellationToken).ConfigureAwait(false)
				?? throw new IOException($"No saved {name} plan.", ex);
		}

		(double width, double height, IReadOnlyList<double> scales, int minLevel) = VvoZonePlans.Geometry(kind);

		var spec = new PlanSpec(edition.key, width, height, scales, MinNativeLevel: minLevel, OverZoom: 0.6);
		var tiles = new RemotePlanTiles(api, spec, Path.Combine(Root, edition.key), (level, x, y) => VvoZonePlans.TileUrl(kind, level, x, y));

		return new ZonePlan(kind, tiles, edition.pins, edition.fresh);
	}

	private async Task<(string, IReadOnlyList<VvoPlanPin>, bool)> FetchAsync(
		VvoZonePlanKind kind,
		string name,
		string pointer,
		CancellationToken cancellationToken)
	{
		(byte[] pinsJson, _) = await api
			.GetBytesAsync(VvoZonePlans.PinsAddress(kind), "application/json", Timeout, cancellationToken)
			.ConfigureAwait(false);

		IReadOnlyList<VvoPlanPin> pins = VvoZonePlans.ParsePins(pinsJson);

		if (pins.Count == 0)
		{
			throw new InvalidDataException("The VVO pin list is empty or unreadable.");
		}

		var tiles = new List<(int Level, int X, int Y, byte[] Png)>();

		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		hash.AppendData(pinsJson);

		foreach ((int level, int x, int y) in VvoZonePlans.EditionTiles(kind))
		{
			(byte[] png, _) = await api
				.GetBytesAsync(VvoZonePlans.TileUrl(kind, level, x, y), "image/png", Timeout, cancellationToken)
				.ConfigureAwait(false);

			hash.AppendData(png);
			tiles.Add((level, x, y, png));
		}

		string key = $"{name}-{Convert.ToHexStringLower(hash.GetHashAndReset().AsSpan(0, 8))}";
		string directory = Path.Combine(Root, key);

		Directory.CreateDirectory(directory);

		await WriteAsync(Path.Combine(directory, "pins.json"), pinsJson, cancellationToken).ConfigureAwait(false);

		// The probes are tiles like any other: kept, not fetched again.
		foreach ((int level, int x, int y, byte[] png) in tiles)
		{
			await WriteAsync(Path.Combine(directory, $"{level}_{x}_{y}.png"), png, cancellationToken).ConfigureAwait(false);
		}

		await File.WriteAllTextAsync(pointer, key, cancellationToken).ConfigureAwait(false);

		_ = Task.Run(() => Tidy(name, key), cancellationToken);

		return (key, pins, true);
	}

	private static async Task<(string, IReadOnlyList<VvoPlanPin>, bool)?> SavedAsync(string pointer, CancellationToken cancellationToken)
	{
		try
		{
			if (!File.Exists(pointer))
			{
				return null;
			}

			string key = (await File.ReadAllTextAsync(pointer, cancellationToken).ConfigureAwait(false)).Trim();
			string pinsFile = Path.Combine(Root, key, "pins.json");

			if (key.Length == 0 || !File.Exists(pinsFile))
			{
				return null;
			}

			IReadOnlyList<VvoPlanPin> pins = VvoZonePlans.ParsePins(await File.ReadAllBytesAsync(pinsFile, cancellationToken).ConfigureAwait(false));

			return pins.Count > 0 ? (key, pins, false) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
	{
		string part = path + ".part";

		await File.WriteAllBytesAsync(part, content, cancellationToken).ConfigureAwait(false);
		File.Move(part, path, overwrite: true);
	}

	/// <summary>Drops the folders of older editions of this plan.</summary>
	private static void Tidy(string name, string key)
	{
		try
		{
			foreach (string directory in Directory.EnumerateDirectories(Root, $"{name}-*"))
			{
				if (!string.Equals(Path.GetFileName(directory), key, StringComparison.Ordinal))
				{
					Directory.Delete(directory, recursive: true);
				}
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[Zone plan] old editions not removed: {ex.Message}");
		}
	}
}
