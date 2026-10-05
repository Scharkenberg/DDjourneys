using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Models;
using DDjourneys.Core.Serialization;
using DDjourneys.Core.Storage;

namespace DDjourneys.Core.Widgets;

public enum WidgetRowKind
{
	/// <summary>A departure, a journey or a stop.</summary>
	Item = 0,

	/// <summary>A stop's name above its departures (the "next departures from the nearest stops" widget).</summary>
	Header
}

public enum WidgetDelay
{
	None = 0,
	Late,
	Cancelled
}

/// <summary>One row of a widget, as text: the widget draws it without knowing where it came from.</summary>
public sealed record WidgetRow
{
	public WidgetRowKind Kind { get; init; }

	/// <summary>The line ("11"), or the distance ("230 m") of a stop.</summary>
	public string Chip { get; init; } = string.Empty;

	/// <summary>Colours the chip.</summary>
	public TransitMode Mode { get; init; } = TransitMode.Unknown;

	/// <summary>Direction, journey times or stop name.</summary>
	public string Main { get; init; } = string.Empty;

	/// <summary>Second line: platform, lines of the journey, place.</summary>
	public string Sub { get; init; } = string.Empty;

	/// <summary>Right side: departure time, or the distance of a stop header.</summary>
	public string Time { get; init; } = string.Empty;

	/// <summary>"+2 min" and the like.</summary>
	public string Delay { get; init; } = string.Empty;

	public WidgetDelay DelayLevel { get; init; }

	/// <summary>When the departure or journey leaves. A widget drawn later drops rows that are over.</summary>
	public DateTimeOffset? At { get; init; }
}

/// <summary>What a widget shows after its last refresh, kept so it can be drawn again (resize, restart) without the network.</summary>
public sealed record WidgetSnapshot
{
	public string Title { get; init; } = string.Empty;

	public DateTimeOffset? UpdatedAt { get; init; }

	/// <summary>Shown instead of rows when there are none ("No departures", "Location not available").</summary>
	public string Message { get; init; } = string.Empty;

	/// <summary>The last refresh failed: the rows are the older ones.</summary>
	public bool IsStale { get; init; }

	public IReadOnlyList<WidgetRow> Rows { get; init; } = [];

	/// <summary>How many rows the refresh asked the provider for; a widget made taller than that fetches again.</summary>
	public int Requested { get; init; }

	/// <summary>
	/// The rows still worth showing at <paramref name="now"/>: a departure that left more than a minute ago is dropped,
	/// and so is a stop header whose departures are all gone. Lets a snapshot age gracefully between refreshes.
	/// </summary>
	public IReadOnlyList<WidgetRow> Upcoming(DateTimeOffset now)
	{
		var kept = new List<WidgetRow>(Rows.Count);

		WidgetRow? header = null;

		foreach (WidgetRow row in Rows)
		{
			if (row.Kind == WidgetRowKind.Header)
			{
				header = row;

				continue;
			}

			if (row.At is { } at
				&& at < now - TimeSpan.FromMinutes(1))
			{
				continue;
			}

			if (header is not null)
			{
				kept.Add(header);
				header = null;
			}

			kept.Add(row);
		}

		return kept;
	}

	public string ToJson() =>
		new JsonObject
		{
			[nameof(Title)] = Title,
			[nameof(UpdatedAt)] = UpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
			[nameof(Message)] = Message,
			["Stale"] = IsStale,
			[nameof(Requested)] = Requested,
			[nameof(Rows)] =
				Wire.Array(
					Rows.Select(
						row => (JsonNode?)new JsonObject
						{
							["Kind"] = (int)row.Kind,
							["Chip"] = row.Chip,
							["Mode"] = row.Mode.ToString(),
							["Main"] = row.Main,
							["Sub"] = row.Sub,
							["Time"] = row.Time,
							["Delay"] = row.Delay,
							["Level"] = (int)row.DelayLevel,
							["At"] = row.At?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
						}))
		}.ToJsonString();

	public static WidgetSnapshot? FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);

			JsonElement root = document.RootElement;

			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			var rows = new List<WidgetRow>();

			if (StoredJson.TryGet(root, "Rows", out JsonElement array)
				&& array.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in array.EnumerateArray())
				{
					rows.Add(
						new WidgetRow
						{
							Kind = StoredJson.Number(item, "Kind") == 1 ? WidgetRowKind.Header : WidgetRowKind.Item,
							Chip = StoredJson.String(item, "Chip") ?? string.Empty,
							Mode = Enum.TryParse(StoredJson.String(item, "Mode"), out TransitMode mode) ? mode : TransitMode.Unknown,
							Main = StoredJson.String(item, "Main") ?? string.Empty,
							Sub = StoredJson.String(item, "Sub") ?? string.Empty,
							Time = StoredJson.String(item, "Time") ?? string.Empty,
							Delay = StoredJson.String(item, "Delay") ?? string.Empty,
							At =
								DateTimeOffset.TryParse(
									StoredJson.String(item, "At"),
									System.Globalization.CultureInfo.InvariantCulture,
									System.Globalization.DateTimeStyles.RoundtripKind,
									out DateTimeOffset at)
									? at
									: null,
							DelayLevel = StoredJson.Number(item, "Level") switch
							{
								1 => WidgetDelay.Late,
								2 => WidgetDelay.Cancelled,
								_ => WidgetDelay.None
							}
						});
				}
			}

			return new WidgetSnapshot
			{
				Title = StoredJson.String(root, "Title") ?? string.Empty,
				UpdatedAt =
					DateTimeOffset.TryParse(
						StoredJson.String(root, "UpdatedAt"),
						System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.RoundtripKind,
						out DateTimeOffset updated)
						? updated
						: null,
				Message = StoredJson.String(root, "Message") ?? string.Empty,
				IsStale =
					StoredJson.TryGet(root, "Stale", out JsonElement stale)
					&& stale.ValueKind == JsonValueKind.True,
				Requested = (int)(StoredJson.Number(root, "Requested") ?? 0),
				Rows = rows
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
