using System.Text.Json.Serialization;
using DDjourneys.Core.Api;

namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// One park &amp; ride site of the VVO open-data file (checked against the live file, October 2026: 56 sites,
/// "ppid" a NUMBER, "dtg" German local time "11.10.2026-02:37:19" or empty, "number_free_now" -1 when the site
/// counts nothing). Every field is optional, unknown keys are ignored, and the text fields read any scalar (a
/// wrong type made the whole file unreadable before). The per-space sensor list is deliberately not part of this
/// shape: it is most of the file's bytes and is only read, in a second pass, for the sites without a count.
/// </summary>
public class VvoParkingSite
{
	[JsonPropertyName("id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? Id { get; init; }

	[JsonPropertyName("ppid")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? Ppid { get; init; }

	[JsonPropertyName("name")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? Name { get; init; }

	/// <summary>WGS84 decimals, as delivered (no GK4 conversion here).</summary>
	[JsonPropertyName("lat")]
	public double? Latitude { get; init; }

	[JsonPropertyName("lon")]
	public double? Longitude { get; init; }

	[JsonPropertyName("info")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? Info { get; init; }

	[JsonPropertyName("number_total")]
	public int? Total { get; init; }

	[JsonPropertyName("number_free_now")]
	public int? FreeNow { get; init; }

	[JsonPropertyName("number_free_ago")]
	public int? FreeAgo { get; init; }

	/// <summary>When the counts were taken. The format is not documented (ISO, /Date(ms)/, something else), so it stays a string.</summary>
	[JsonPropertyName("dtg")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? Dtg { get; init; }
}

/// <summary>The same file read again, this time with the per-space sensors: only asked for when a site has no count.</summary>
public sealed class VvoParkingSiteLive : VvoParkingSite
{
	[JsonPropertyName("LiveData")]
	public IReadOnlyList<VvoParkingSensor> LiveData { get; init; } = [];
}

/// <summary>One parking-space sensor: status 0 is free, 1 occupied, -1 defect; a "disabled" tag is out of service.</summary>
public sealed class VvoParkingSensor
{
	[JsonPropertyName("status")]
	public int? Status { get; init; }

	[JsonPropertyName("lat")]
	public double? Latitude { get; init; }

	[JsonPropertyName("lon")]
	public double? Longitude { get; init; }

	[JsonPropertyName("tag")]
	public string? Tag { get; init; }
}
