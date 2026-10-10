using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// One park &amp; ride site of the VVO open-data file (one observed sample, never re-verified from this
/// container: every field is optional and unknown keys are ignored). The per-space sensor list is deliberately
/// not part of this shape: it is most of the file's bytes and is only read, in a second pass, for the few
/// sites that carry no count of their own.
/// </summary>
public class VvoParkingSite
{
	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("ppid")]
	public string? Ppid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>WGS84 decimals, as delivered (no GK4 conversion here).</summary>
	[JsonPropertyName("lat")]
	public double? Latitude { get; init; }

	[JsonPropertyName("lon")]
	public double? Longitude { get; init; }

	[JsonPropertyName("info")]
	public string? Info { get; init; }

	[JsonPropertyName("number_total")]
	public int? Total { get; init; }

	[JsonPropertyName("number_free_now")]
	public int? FreeNow { get; init; }

	[JsonPropertyName("number_free_ago")]
	public int? FreeAgo { get; init; }

	/// <summary>When the counts were taken. The format is not documented (ISO, /Date(ms)/, something else), so it stays a string.</summary>
	[JsonPropertyName("dtg")]
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
