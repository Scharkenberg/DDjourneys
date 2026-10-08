using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Widgets;

/// <summary>What a home screen widget shows.</summary>
public enum WidgetKind
{
	/// <summary>Journeys from any place A to any place B.</summary>
	Route = 0,

	/// <summary>Departures from one stop.</summary>
	Departures,

	/// <summary>Arrivals at one stop.</summary>
	Arrivals,

	/// <summary>The stops around the device, with their distance in meters.</summary>
	NearbyStops,

	/// <summary>The next departures from the stops around the device, in one view.</summary>
	NearbyDepartures
}

/// <summary>A point a widget works with: a chosen place, or wherever the device is when the widget refreshes.</summary>
public sealed record WidgetPlace(Location? Place, bool IsHere = false)
{
	public static WidgetPlace Here { get; } = new(null, true);

	public bool IsSet =>
		IsHere || Place is not null;
}

/// <summary>
/// The settings of one widget instance, kept as JSON per widget id. Every option has a default, so a stored
/// configuration from an older build still reads.
/// </summary>
public sealed record WidgetConfig
{
	public static readonly IReadOnlyList<int> Radii = [200, 300, 500, 750, 1000, 1500, 2000];

	/// <summary>Minutes between automatic refreshes. Android decides when a widget may update at all (30 minutes at best).</summary>
	public static readonly IReadOnlyList<int> Intervals = [30, 60, 120, 240];

	public const int MaxRowsLimit = 10;

	public WidgetKind Kind { get; init; }

	/// <summary>The title the widget shows; empty keeps the default (the stop, the route, the kind).</summary>
	public string Title { get; init; } = string.Empty;

	/// <summary>The provider the widget was made for; it keeps using it whichever provider the app shows.</summary>
	public string ProviderId { get; init; } = string.Empty;

	public WidgetPlace? From { get; init; }

	public WidgetPlace? To { get; init; }

	/// <summary>The stop of a departures or arrivals widget.</summary>
	public Location? Stop { get; init; }

	/// <summary>How far around the device stops are looked for.</summary>
	public int RadiusMeters { get; init; } = 500;

	/// <summary>Most rows to show; 0 shows as many as fit the widget.</summary>
	public int MaxRows { get; init; }

	/// <summary>Nearby departures: how many of the nearest stops are listed.</summary>
	public int StopCount { get; init; } = 3;

	/// <summary>Nearby departures: how many departures per stop.</summary>
	public int PerStop { get; init; } = 2;

	/// <summary>Only these lines (comma separated, "3, 11"); empty shows every line.</summary>
	public string Lines { get; init; } = string.Empty;

	public ModeFilter Modes { get; init; } = ModeFilter.All;

	public bool AutoRefresh { get; init; } = true;

	public int IntervalMinutes { get; init; } = 30;

	/// <summary>Enough is chosen for the widget to show something.</summary>
	public bool IsComplete =>
		Kind switch
		{
			WidgetKind.Route => From is { IsSet: true } && To is { IsSet: true },
			WidgetKind.Departures or WidgetKind.Arrivals => Stop is not null,
			_ => true
		};

	/// <summary>The line names of <see cref="Lines"/>, trimmed and without repeats.</summary>
	public IReadOnlyList<string> LineFilter =>
		[.. Lines
			.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)];

	public string ToJson() =>
		new JsonObject
		{
			[nameof(Kind)] = (int)Kind,
			[nameof(Title)] = Title,
			[nameof(ProviderId)] = ProviderId,
			[nameof(From)] = PlaceNode(From),
			[nameof(To)] = PlaceNode(To),
			[nameof(Stop)] = LocationJson.ToNode(Stop),
			[nameof(RadiusMeters)] = RadiusMeters,
			[nameof(MaxRows)] = MaxRows,
			[nameof(StopCount)] = StopCount,
			[nameof(PerStop)] = PerStop,
			[nameof(Lines)] = Lines,
			[nameof(Modes)] = (int)Modes,
			[nameof(AutoRefresh)] = AutoRefresh,
			[nameof(IntervalMinutes)] = IntervalMinutes
		}.ToJsonString();

	/// <summary>Reads a stored configuration; null when the text is not one.</summary>
	public static WidgetConfig? FromJson(string? json)
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

			var fallback = new WidgetConfig();

			ModeFilter modes = (ModeFilter)Int(root, "Modes", (int)ModeFilter.All) & ModeFilter.All;

			return new WidgetConfig
			{
				Kind = Enum.IsDefined((WidgetKind)Int(root, "Kind", 0)) ? (WidgetKind)Int(root, "Kind", 0) : WidgetKind.Route,
				Title = StoredJson.String(root, "Title")?.Trim() ?? string.Empty,
				ProviderId = StoredJson.String(root, "ProviderId") ?? string.Empty,
				From = ReadPlace(root, "From"),
				To = ReadPlace(root, "To"),
				Stop = StoredJson.TryGet(root, "Stop", out JsonElement stop) ? LocationJson.FromElement(stop) : null,
				RadiusMeters = Math.Clamp(Int(root, "RadiusMeters", fallback.RadiusMeters), 100, 5000),
				MaxRows = Math.Clamp(Int(root, "MaxRows", 0), 0, MaxRowsLimit),
				StopCount = Math.Clamp(Int(root, "StopCount", fallback.StopCount), 1, 6),
				PerStop = Math.Clamp(Int(root, "PerStop", fallback.PerStop), 1, 6),
				Lines = StoredJson.String(root, "Lines") ?? string.Empty,
				Modes = modes == ModeFilter.None ? ModeFilter.All : modes,
				AutoRefresh = Bool(root, "AutoRefresh", true),
				IntervalMinutes = Math.Clamp(Int(root, "IntervalMinutes", fallback.IntervalMinutes), 30, 24 * 60)
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static int Int(JsonElement root, string name, int fallback) =>
		StoredJson.Number(root, name) is { } value
			? (int)Math.Round(value)
			: fallback;

	private static bool Bool(JsonElement root, string name, bool fallback) =>
		StoredJson.TryGet(root, name, out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: fallback;

	private static JsonObject? PlaceNode(WidgetPlace? place) =>
		place is null
			? null
			: new JsonObject
			{
				["Here"] = place.IsHere,
				["Place"] = LocationJson.ToNode(place.Place)
			};

	private static WidgetPlace? ReadPlace(JsonElement root, string name)
	{
		if (!StoredJson.TryGet(root, name, out JsonElement element)
			|| element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		bool here = Bool(element, "Here", false);

		Location? place =
			StoredJson.TryGet(element, "Place", out JsonElement location)
				? LocationJson.FromElement(location)
				: null;

		return here || place is not null
			? new WidgetPlace(place, here)
			: null;
	}
}
