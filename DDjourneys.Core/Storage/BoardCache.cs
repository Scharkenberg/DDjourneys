using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Models;
using DDjourneys.Core.Serialization;

namespace DDjourneys.Core.Storage;

/// <summary>
/// The last departures of one stop, as they were fetched: what the departures page still answers when the fetch
/// fails (offline), stamped with the moment it was fetched. Only boards asked for "now" are ever stored — a board
/// for a chosen other time is not what the page should fall back to.
/// </summary>
public sealed record CachedBoard(
	string ProviderId,
	string StopId,
	bool IsArrival,
	DateTimeOffset Fetched,
	IReadOnlyList<Departure> Departures);

/// <summary>
/// The last boards as a stored list (versioned envelope, one entry per provider, stop and board mode), so the
/// departures of a stop survive a failed fetch. The list holds at most <see cref="Capacity"/> boards: writing one
/// puts it at the front, and the stop whose board was fetched longest ago is dropped — fetching a stop again makes
/// it the newest. Reading is entry by entry like every stored list; one entry that does not fit costs that entry,
/// never the whole list. A departure is stored in the shape the board view shows (line, direction, scheduled and
/// live time, state, platform, occupancy, route changes); <c>ProviderData</c> (what a provider keeps for a run) is
/// not stored, it is asked for again when the run is opened.
/// </summary>
public static class BoardCacheList
{
	/// <summary>How many boards are kept: about a place list's length, the stops a person really looks at.</summary>
	public const int Capacity = 30;

	public static IReadOnlyList<CachedBoard> Parse(string? json) =>
		StoredJson.Read(json, ReadEntry).Items;

	public static string Write(IReadOnlyList<CachedBoard> boards) =>
		StoredJson.Write(boards, ToNode);

	/// <summary>
	/// The list with this board at the front and every older board of the same provider, stop and mode dropped, cut
	/// to <paramref name="capacity"/> from the end (the boards fetched longest ago go first).
	/// </summary>
	public static IReadOnlyList<CachedBoard> Touch(
		IReadOnlyList<CachedBoard> boards,
		CachedBoard board,
		int capacity = Capacity)
	{
		ArgumentNullException.ThrowIfNull(boards);
		ArgumentNullException.ThrowIfNull(board);

		if (capacity < 1)
		{
			capacity = 1;
		}

		var touched = new List<CachedBoard>(boards.Count + 1) { board };

		foreach (CachedBoard existing in boards)
		{
			if (touched.Count >= capacity)
			{
				break;
			}

			if (Matches(existing, board.ProviderId, board.StopId, board.IsArrival))
			{
				continue;
			}

			touched.Add(existing);
		}

		return touched;
	}

	/// <summary>The board of this stop for this provider and board mode; reading does not change the order.</summary>
	public static CachedBoard? Find(
		IReadOnlyList<CachedBoard> boards,
		string providerId,
		string stopId,
		bool isArrival) =>
		boards.FirstOrDefault(board => Matches(board, providerId, stopId, isArrival));

	private static bool Matches(CachedBoard board, string providerId, string stopId, bool isArrival) =>
		board.IsArrival == isArrival
		&& string.Equals(board.ProviderId, providerId, StringComparison.Ordinal)
		&& string.Equals(board.StopId, stopId, StringComparison.Ordinal);

	private static JsonNode? ToNode(CachedBoard board)
	{
		var departures = new JsonArray();

		foreach (Departure departure in board.Departures)
		{
			departures.Add(DepartureNode(departure));
		}

		return
			new JsonObject
			{
				["ProviderId"] = board.ProviderId,
				["StopId"] = board.StopId,
				["IsArrival"] = board.IsArrival,
				["Fetched"] = board.Fetched.ToString("O", CultureInfo.InvariantCulture),
				["Departures"] = departures
			};
	}

	private static CachedBoard? ReadEntry(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "ProviderId") is not { Length: > 0 } providerId
			|| StoredJson.String(element, "StopId") is not { Length: > 0 } stopId)
		{
			return null;
		}

		var departures = new List<Departure>();

		if (StoredJson.TryGet(element, "Departures", out JsonElement array)
			&& array.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in array.EnumerateArray())
			{
				if (ReadDeparture(item) is { } departure)
				{
					departures.Add(departure);
				}
			}
		}

		return
			new CachedBoard(
				providerId,
				stopId,
				StoredJson.TryGet(element, "IsArrival", out JsonElement arrival) && arrival.ValueKind == JsonValueKind.True,
				ReadTime(StoredJson.String(element, "Fetched")) ?? default,
				departures);
	}

	private static JsonNode DepartureNode(Departure departure) =>
		new JsonObject
		{
			["Id"] = departure.Id,
			["StopId"] = departure.StopId,
			["Line"] =
				new JsonObject
				{
					["Name"] = departure.Line.Name,
					["Mode"] = departure.Line.Mode.ToString(),
					["Destination"] = departure.Line.Destination
				},
			["Scheduled"] = departure.Scheduled.ToString("O", CultureInfo.InvariantCulture),
			["Realtime"] = departure.Realtime?.ToString("O", CultureInfo.InvariantCulture),
			["IsArrival"] = departure.IsArrival,
			["Platform"] = departure.Platform,
			["PlatformKind"] = (int)departure.PlatformKind,
			["State"] = (int)departure.State,
			["Occupancy"] = (int)departure.Occupancy,
			["RouteChanges"] = Wire.Array(departure.RouteChangeIds.Select(id => (JsonNode?)id))
		};

	private static Departure? ReadDeparture(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "Id") is not { Length: > 0 } id)
		{
			return null;
		}

		// Without a scheduled time a departure says nothing worth showing offline.
		if (ReadTime(StoredJson.String(element, "Scheduled")) is not { } scheduled)
		{
			return null;
		}

		TransitLine line =
			StoredJson.TryGet(element, "Line", out JsonElement lineNode) && lineNode.ValueKind == JsonValueKind.Object
				? new TransitLine
				{
					Name = StoredJson.String(lineNode, "Name") ?? string.Empty,
					Mode = Enum.TryParse(StoredJson.String(lineNode, "Mode"), out TransitMode mode) ? mode : TransitMode.Unknown,
					Destination = StoredJson.String(lineNode, "Destination")
				}
				: new TransitLine { Name = string.Empty, Mode = TransitMode.Unknown };

		return
			new Departure
			{
				Id = id,
				StopId = StoredJson.String(element, "StopId") ?? string.Empty,
				Line = line,
				Scheduled = scheduled,
				Realtime = ReadTime(StoredJson.String(element, "Realtime")),
				IsArrival = StoredJson.TryGet(element, "IsArrival", out JsonElement arrival) && arrival.ValueKind == JsonValueKind.True,
				Platform = StoredJson.String(element, "Platform"),
				PlatformKind = (PlatformKind)(int)(StoredJson.Number(element, "PlatformKind") ?? 0),
				State = (DepartureState)(int)(StoredJson.Number(element, "State") ?? 0),
				Occupancy = (OccupancyLevel)(int)(StoredJson.Number(element, "Occupancy") ?? 0),
				RouteChangeIds = ReadChanges(element)
			};
	}

	private static IReadOnlyList<string> ReadChanges(JsonElement element)
	{
		if (!StoredJson.TryGet(element, "RouteChanges", out JsonElement array)
			|| array.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return
			[.. array.EnumerateArray()
				.Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
				.OfType<string>()];
	}

	private static DateTimeOffset? ReadTime(string? text) =>
		DateTimeOffset.TryParse(
			text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out DateTimeOffset at)
			? at
			: null;
}
