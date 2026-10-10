using System.Globalization;
using System.Text.Json;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Core.Providers.Shared;

/// <summary>
/// Reads GBFS 2.x feeds (discovery first, then the feeds it names). One feed failing never fails the
/// operator: what loaded is what the join sees. The service above this client owns the per-feed caches,
/// so the client stays stateless; every method degrades to null on a bad answer.
/// </summary>
public sealed class GbfsClient(ApiClient apiClient)
{
	public async Task<GbfsDiscovery?> GetDiscoveryAsync(
		Uri discoveryUrl,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string json =
				await apiClient.GetAsync(
					discoveryUrl.ToString(),
					timeout,
					cancellationToken)
					.ConfigureAwait(false);

			return JsonSerializer.Deserialize(
					json,
					VvoJson.TypeInfo(typeof(GbfsDiscovery))) as GbfsDiscovery;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Shared] gbfs discovery {discoveryUrl.Host} failed: {ex.Message}");

			return null;
		}
	}

	public async Task<GbfsStationInformation?> GetInformationAsync(
		Uri feedUrl,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string json =
				await apiClient.GetAsync(
					feedUrl.ToString(),
					timeout,
					cancellationToken)
					.ConfigureAwait(false);

			return JsonSerializer.Deserialize(
					json,
					VvoJson.TypeInfo(typeof(GbfsStationInformation))) as GbfsStationInformation;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Shared] gbfs station_information {feedUrl.Host} failed: {ex.Message}");

			return null;
		}
	}

	public async Task<GbfsStationStatus?> GetStatusAsync(
		Uri feedUrl,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string json =
				await apiClient.GetAsync(
					feedUrl.ToString(),
					timeout,
					cancellationToken)
					.ConfigureAwait(false);

			return JsonSerializer.Deserialize(
					json,
					VvoJson.TypeInfo(typeof(GbfsStationStatus))) as GbfsStationStatus;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Shared] gbfs station_status {feedUrl.Host} failed: {ex.Message}");

			return null;
		}
	}

	/// <summary>The absolute URL of one feed, whatever language bucket carries it; null when the discovery names none.</summary>
	internal static Uri? FeedUrl(GbfsDiscovery? discovery, string name)
	{
		if (discovery?.Data is not { Count: > 0 } buckets)
		{
			return null;
		}

		foreach (GbfsLanguage bucket in buckets.Values)
		{
			foreach (GbfsFeed feed in bucket.Feeds)
			{
				if (string.Equals(feed.Name, name, StringComparison.OrdinalIgnoreCase)
					&& Uri.TryCreate(feed.Url, UriKind.Absolute, out Uri? url)
					&& url is { Scheme: "http" or "https" })
				{
					return url;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// The stations of one operator: the skeleton from station_information, the counts from station_status
	/// (a missing or failed status leaves the stations standing, grey, without numbers that could lie).
	/// </summary>
	internal static IReadOnlyList<SharedStation> Join(
		GbfsStationInformation? information,
		GbfsStationStatus? status,
		string operatorName,
		Uri? website = null)
	{
		if (information?.Data?.Stations is not { Count: > 0 } skeleton)
		{
			return [];
		}

		Dictionary<string, GbfsStationStatusRow>? rows = null;

		if (status?.Data?.Stations is { Count: > 0 } reported)
		{
			rows = new Dictionary<string, GbfsStationStatusRow>(StringComparer.Ordinal);

			foreach (GbfsStationStatusRow row in reported)
			{
				if (row.StationId is { Length: > 0 } id)
				{
					rows[id] = row;
				}
			}
		}

		List<SharedStation> joined = new(skeleton.Count);

		foreach (GbfsStation station in skeleton)
		{
			if (station.StationId is not { Length: > 0 } stationId
				|| station.Latitude is not { } latitude
				|| station.Longitude is not { } longitude)
			{
				continue;
			}

			GbfsStationStatusRow? row = rows?.TryGetValue(stationId, out GbfsStationStatusRow? found) == true ? found : null;

			joined.Add(
				new SharedStation(
					operatorName,
					stationId,
					station.Name ?? station.ShortName ?? stationId,
					latitude,
					longitude,
					row?.NumBikesAvailable ?? 0,
					row?.NumDocksAvailable,
					row?.IsRenting is int renting ? renting == 1 : null,
					AbsoluteLink(station.RentalUris?.Android),
					AbsoluteWeb(station.RentalUris?.Web),
					website,
					row?.LastReported is { } reportedAt ? FromPosix(reportedAt) : null));
		}

		return joined;
	}

	/// <summary>GBFS 2.x counts POSIX seconds, newer feeds milliseconds; both are told apart by their size.</summary>
	internal static DateTimeOffset FromPosix(long value) =>
		DateTimeOffset.FromUnixTimeMilliseconds(
			value > 100_000_000_000L ? value : checked(value * 1000));

	/// <summary>An app deep link: any absolute scheme goes (nextbike:// is exactly what the check for an installed app needs).</summary>
	private static Uri? AbsoluteLink(string? text) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme.Length > 0
			? uri
		: null;

	/// <summary>A web page: only http and https count.</summary>
	private static Uri? AbsoluteWeb(string? text) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri is { Scheme: "http" or "https" }
			? uri
		: null;
}
