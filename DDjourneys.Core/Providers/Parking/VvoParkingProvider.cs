using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Core.Providers.Parking;

/// <summary>
/// The park &amp; ride occupancy of the VVO, as its open-data file publishes it. The list is small (about fifty
/// sites), so it is fetched whole. Sites that carry no count of their own are counted from their per-space
/// sensors instead (a second, usually unnecessary pass over the same bytes); a site whose sensors are all
/// defect or out of service shows no numbers rather than wrong ones.
/// </summary>
public sealed partial class VvoParkingProvider(ApiClient apiClient) : IParkingProvider
{
	public async Task<IReadOnlyList<ParkingSite>> GetSitesAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		string json =
			await apiClient.GetAsync(
				InterfaceSchemas.VvoPurUrl,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		VvoParkingSite[]? sites =
			JsonSerializer.Deserialize(
				json,
				VvoJsonContext.Default.VvoParkingSiteArray);

		if (sites is null || sites.Length == 0)
		{
			return [];
		}

		// Only when a site has no count of its own does the sensor list have to be read.
		VvoParkingSiteLive[]? live = null;

		if (Array.Exists(sites, static site => site.FreeNow is null))
		{
			live =
				JsonSerializer.Deserialize(
					json,
					VvoJsonContext.Default.VvoParkingSiteLiveArray);
		}

		return MapSites(sites, live);
	}

	/// <summary>The observed sites: numbers where the file has them, sensor counts where it has sensors, nothing where neither can be trusted.</summary>
	internal static IReadOnlyList<ParkingSite> MapSites(
		VvoParkingSite[] sites,
		VvoParkingSiteLive[]? live)
	{
		List<ParkingSite> mapped = new(sites.Length);

		for (int index = 0; index < sites.Length; index++)
		{
			if (MapSite(
					index,
					sites[index],
					live is { Length: > 0 } list && index < list.Length ? list[index] : null) is { } site)
			{
				mapped.Add(site);
			}
		}

		return mapped;
	}

	/// <summary>One site: skipped when it cannot be placed or named at all.</summary>
	internal static ParkingSite? MapSite(
		int index,
		VvoParkingSite site,
		VvoParkingSiteLive? live)
	{
		string id = First(site.Id, site.Ppid) ?? string.Empty;

		if (id.Length == 0
			|| site.Latitude is not { } latitude
			|| site.Longitude is not { } longitude)
		{
			return null;
		}

		string name = First(site.Name, site.Info) ?? id;

		DateTimeOffset? at = ParseTimestamp(site.Dtg);
		int total = site.Total ?? 0;

		if (site.FreeNow is { } free)
		{
			return new ParkingSite(id, name, latitude, longitude, total, free, at);
		}

		// No count of its own: usable sensors only. A site whose sensors are all defect or out of
		// service has no live data; wrong numbers are worse than none.
		if (live?.LiveData is not { Count: > 0 } sensors)
		{
			return new ParkingSite(id, name, latitude, longitude, 0, 0, null);
		}

		List<VvoParkingSensor> usable =
			[.. sensors.Where(sensor =>
				!string.Equals(sensor.Tag, "disabled", StringComparison.OrdinalIgnoreCase)
				&& sensor.Status != -1)];

		if (usable.Count == 0)
		{
			return new ParkingSite(id, name, latitude, longitude, 0, 0, null);
		}

		return new ParkingSite(
			id,
			name,
			latitude,
			longitude,
			site.Total ?? usable.Count,
			usable.Count(sensor => sensor.Status == 0),
			at);
	}

	/// <summary>When the counts were taken: ISO first, then the VVO /Date(ms)/ house style, else unknown.</summary>
	internal static DateTimeOffset? ParseTimestamp(string? dtg)
	{
		if (string.IsNullOrWhiteSpace(dtg))
		{
			return null;
		}

		if (DateTimeOffset.TryParse(
				dtg,
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out DateTimeOffset iso))
		{
			return iso;
		}

		Match ajax = AjaxDate().Match(dtg);

		return ajax.Success
			&& long.TryParse(ajax.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds)
				? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
				: null;
	}

	private static string? First(params string?[] values)
	{
		foreach (string? value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}

		return null;
	}

	/// <summary>The VVO house style: /Date(1512770460000+0100)/.</summary>
	[GeneratedRegex(@"^/Date\((\d+)")]
	private static partial Regex AjaxDate();
}
