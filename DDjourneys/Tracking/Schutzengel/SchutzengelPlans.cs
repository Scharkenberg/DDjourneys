using System.Globalization;
using System.Text.Json;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// The plan options object of the service (<c>planOptions</c> / <c>newOptions</c>).
/// Periodic plans need a logged-in account, so only the fields that matter for static plans
/// are editable here; <see cref="Type"/> and <see cref="Weekdays"/> are carried through unchanged.
/// </summary>
internal sealed record SchutzengelOptions(
	string Type,
	IReadOnlyList<string>? Weekdays,
	bool StartActive,
	int StartLeadSeconds,
	bool Change,
	bool Problem)
{
	public static SchutzengelOptions Default { get; } = new("static", null, true, 300, true, true);

	public bool IsPeriodic =>
		string.Equals(Type, "periodic", StringComparison.OrdinalIgnoreCase);

	/// <summary>How long before the first departure monitoring counts as "active".</summary>
	public TimeSpan StartLead =>
		StartActive
			? TimeSpan.FromSeconds(Math.Max(0, StartLeadSeconds))
			: TimeSpan.Zero;

	public object ToPayload() =>
		new
		{
			attentions = new
			{
				start = new
				{
					timeBeforeSeconds = StartLeadSeconds,
					active = StartActive
				},
				change = Change,
				problem = Problem
			},
			type = Type,
			weekdays = Weekdays
		};

	public WatchOptions ToWatchOptions() =>
		new(StartActive, Math.Max(1, StartLeadSeconds / 60), Change, Problem);

	public SchutzengelOptions With(WatchOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return this with
		{
			StartActive = options.StartAlert,
			StartLeadSeconds = Math.Max(1, options.StartLeadMinutes) * 60,
			Change = options.ChangeAlert,
			Problem = options.ProblemAlert
		};
	}

	public static SchutzengelOptions Parse(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		bool startActive = Default.StartActive;
		int lead = Default.StartLeadSeconds;
		bool change = Default.Change;
		bool problem = Default.Problem;

		if (element.TryGetProperty("attentions", out JsonElement attentions)
			&& attentions.ValueKind == JsonValueKind.Object)
		{
			change = ReadBool(attentions, "change", change);
			problem = ReadBool(attentions, "problem", problem);

			if (attentions.TryGetProperty("start", out JsonElement start)
				&& start.ValueKind == JsonValueKind.Object)
			{
				startActive = ReadBool(start, "active", startActive);

				if (start.TryGetProperty("timeBeforeSeconds", out JsonElement seconds)
					&& seconds.ValueKind == JsonValueKind.Number
					&& seconds.TryGetInt32(out int value))
				{
					lead = value;
				}
			}
		}

		string type =
			SchutzengelPlanList.ReadString(element, "type") is { Length: > 0 } text
				? text
				: Default.Type;

		List<string>? weekdays = null;

		if (element.TryGetProperty("weekdays", out JsonElement days)
			&& days.ValueKind == JsonValueKind.Array)
		{
			weekdays = [];

			foreach (JsonElement day in days.EnumerateArray())
			{
				if (day.ValueKind == JsonValueKind.String && day.GetString() is { } name)
				{
					weekdays.Add(name);
				}
			}
		}

		return new SchutzengelOptions(type, weekdays, startActive, lead, change, problem);
	}

	private static bool ReadBool(JsonElement element, string name, bool fallback) =>
		element.TryGetProperty(name, out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: fallback;
}

/// <summary>One entry of <c>plansMinimal</c>.</summary>
internal sealed record SchutzengelPlanInfo(
	string PlanId,
	string? ActiveTripId,
	string? TripReference,
	bool Deactivated,
	SchutzengelOptions Options);

internal static class SchutzengelPlanList
{
	/// <summary>
	/// Accepts the bare array of the service as well as an object wrapping it in <c>plans</c>.
	/// Plan ids are matched exactly, never by substring.
	/// </summary>
	public static IReadOnlyList<SchutzengelPlanInfo> Parse(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Object
			&& root.TryGetProperty("plans", out JsonElement nested))
		{
			root = nested;
		}

		if (root.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<SchutzengelPlanInfo>();

		foreach (JsonElement item in root.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object
				|| ReadString(item, "plan_id") is not { Length: > 0 } planId)
			{
				continue;
			}

			result.Add(
				new SchutzengelPlanInfo(
					planId,
					ReadString(item, "active_trip_id"),
					ReadString(item, "trip_reference"),
					item.TryGetProperty("deactivated", out JsonElement deactivated)
						&& deactivated.ValueKind == JsonValueKind.True,
					item.TryGetProperty("planOptions", out JsonElement options)
						? SchutzengelOptions.Parse(options)
						: SchutzengelOptions.Default));
		}

		return result;
	}

	/// <summary>The creation response carries <c>planId</c>; older shapes are tolerated.</summary>
	public static string? ReadCreatedPlanId(JsonElement created)
	{
		foreach (string name in new[] { "planId", "plan_id", "id" })
		{
			if (ReadString(created, name) is { Length: > 0 } direct)
			{
				return direct;
			}

			if (created.ValueKind == JsonValueKind.Object
				&& created.TryGetProperty("data", out JsonElement data)
				&& ReadString(data, name) is { Length: > 0 } nested)
			{
				return nested;
			}
		}

		return null;
	}

	internal static string? ReadString(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(name, out JsonElement value)
		&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}

internal enum SchutzengelNoticeSeverity
{
	Information,
	ConnectionRisk,
	Cancellation
}

internal sealed record SchutzengelNotice(
	string Title,
	string Message,
	DateTimeOffset? Time)
{
	public string Text =>
		string.IsNullOrWhiteSpace(Message)
			? Title
			: string.IsNullOrWhiteSpace(Title)
				? Message
				: $"{Title}: {Message}";

	/// <summary>
	/// The wording is defined by the service, so this is a fallback only; the timeline's own
	/// connection check does not depend on it. German and English phrases are recognised.
	/// </summary>
	public SchutzengelNoticeSeverity Severity
	{
		get
		{
			string text = Text;

			if (ContainsAny(text, "fällt aus", "fallen aus", "ausfall", "entfällt", "entfallen", "cancel"))
			{
				return SchutzengelNoticeSeverity.Cancellation;
			}

			bool mentionsConnection =
				ContainsAny(text, "anschluss", "umstieg", "connection", "transfer");

			if (mentionsConnection
				&& ContainsAny(text, "gefährdet", "verpasst", "endanger", "missed", "at risk"))
			{
				return SchutzengelNoticeSeverity.ConnectionRisk;
			}

			return SchutzengelNoticeSeverity.Information;
		}
	}

	private static bool ContainsAny(string text, params string[] phrases) =>
		phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}

internal static class SchutzengelNotices
{
	/// <summary>Chronological; entries without a usable shape are skipped.</summary>
	public static IReadOnlyList<SchutzengelNotice> Parse(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<SchutzengelNotice>();

		foreach (JsonElement item in root.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string title = SchutzengelPlanList.ReadString(item, "title") ?? string.Empty;
			string message = SchutzengelPlanList.ReadString(item, "message") ?? string.Empty;

			if (title.Length == 0 && message.Length == 0)
			{
				continue;
			}

			item.TryGetProperty("notificationTime", out JsonElement time);

			result.Add(new SchutzengelNotice(title, message, SchutzengelTime.Read(time)));
		}

		return result
			.OrderBy(notice => notice.Time ?? DateTimeOffset.MinValue)
			.ToList();
	}
}

/// <summary>Timestamps arrive as epoch milliseconds, occasionally as seconds, ISO or Microsoft JSON dates.</summary>
internal static class SchutzengelTime
{
	private const long MillisecondsThreshold = 100_000_000_000;

	public static DateTimeOffset? Read(JsonElement value)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Number:
				if (value.TryGetInt64(out long epoch))
				{
					return FromEpoch(epoch);
				}

				return value.TryGetDouble(out double fractional)
					? FromEpoch((long)fractional)
					: null;

			case JsonValueKind.String:
				string? text = value.GetString();

				if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedEpoch))
				{
					return FromEpoch(parsedEpoch);
				}

				// Microsoft JSON date as the VVO WebAPI writes it: /Date(1512770460000+0100)/
				if (text is { Length: > 8 }
					&& text.StartsWith("/Date(", StringComparison.Ordinal)
					&& text.EndsWith(")/", StringComparison.Ordinal))
				{
					string inner = text[6..^2];
					int zone = inner.IndexOfAny(['+', '-'], 1);

					if (long.TryParse(
						zone > 0 ? inner[..zone] : inner,
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out long wcfEpoch))
					{
						return FromEpoch(wcfEpoch);
					}
				}

				return DateTimeOffset.TryParse(
					text,
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal,
					out DateTimeOffset parsed)
					? parsed
					: null;

			default:
				return null;
		}
	}

	public static DateTimeOffset? FromEpoch(long epoch)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(
				Math.Abs(epoch) > MillisecondsThreshold
					? epoch
					: epoch * 1000);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	/// <summary>Offset to add to the local clock to get server time, or null if unreadable.</summary>
	public static TimeSpan? ReadServerOffset(JsonElement root, DateTimeOffset localNow)
	{
		if (root.ValueKind == JsonValueKind.Object)
		{
			foreach (string name in new[] { "serverTime", "server_time", "timestamp", "time" })
			{
				if (root.TryGetProperty(name, out JsonElement nested)
					&& Read(nested) is { } found)
				{
					return found - localNow;
				}
			}

			return null;
		}

		return Read(root) is { } time
			? time - localNow
			: null;
	}
}

/// <summary>What the overview page shows about a plan, taken from its stored raw connection.</summary>
internal sealed record SchutzengelRawSummary(
	string Origin,
	string Destination,
	DateTimeOffset? Departure,
	DateTimeOffset? Arrival,
	IReadOnlyList<string> Lines,
	string? Fingerprint);

internal static class SchutzengelRawSummaryParser
{
	private const int TransportationCategoryMot = 0;

	// MOT types that are not rides (hailed shared taxi, taxi, walking).
	private static readonly HashSet<int> IndividualMotTypes = [16, 17, 19];

	/// <summary>Reads the <c>planRawData</c> response (<c>{ raw_data, polyline }</c>).</summary>
	public static SchutzengelRawSummary? ParsePlanRawData(JsonElement response)
	{
		if (response.ValueKind != JsonValueKind.Object
			|| !response.TryGetProperty("raw_data", out JsonElement raw))
		{
			return null;
		}

		if (raw.ValueKind == JsonValueKind.String && raw.GetString() is { Length: > 0 } text)
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(text);
				return ParseConnection(document.RootElement);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		return ParseConnection(raw);
	}

	/// <summary>Reads a VVO connection object (the <c>rawData</c> of a plan).</summary>
	public static SchutzengelRawSummary? ParseConnection(JsonElement connection)
	{
		if (connection.ValueKind != JsonValueKind.Object
			|| !connection.TryGetProperty("partialConnections", out JsonElement partials)
			|| partials.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		var movements = new List<(JsonElement Partial, JsonElement Nodes, bool Ride)>();

		foreach (JsonElement partial in partials.EnumerateArray())
		{
			if (partial.ValueKind != JsonValueKind.Object
				|| !partial.TryGetProperty("nodes", out JsonElement nodes)
				|| nodes.ValueKind != JsonValueKind.Array
				|| nodes.GetArrayLength() == 0)
			{
				continue;
			}

			int category = TransportationCategoryMot;
			int type = 0;

			if (partial.TryGetProperty("mot", out JsonElement mot) && mot.ValueKind == JsonValueKind.Object)
			{
				if (mot.TryGetProperty("category", out JsonElement categoryElement)
					&& categoryElement.TryGetInt32(out int parsedCategory))
				{
					category = parsedCategory;
				}

				if (mot.TryGetProperty("type", out JsonElement typeElement)
					&& typeElement.TryGetInt32(out int parsedType))
				{
					type = parsedType;
				}
			}

			if (category != TransportationCategoryMot)
			{
				continue;
			}

			movements.Add((partial, nodes, !IndividualMotTypes.Contains(type)));
		}

		if (movements.Count == 0)
		{
			return null;
		}

		JsonElement firstNode = movements[0].Nodes[0];
		JsonElement lastNodes = movements[^1].Nodes;
		JsonElement lastNode = lastNodes[lastNodes.GetArrayLength() - 1];

		var lines = new List<string>();

		foreach ((JsonElement partial, _, bool ride) in movements)
		{
			if (ride
				&& partial.TryGetProperty("line", out JsonElement line)
				&& SchutzengelPlanList.ReadString(line, "name") is { Length: > 0 } name
				&& !lines.Contains(name))
			{
				lines.Add(name);
			}
		}

		var rides = movements.Where(movement => movement.Ride).ToList();

		string? fingerprint = null;

		if (rides.Count > 0)
		{
			JsonElement rideFirst = rides[0].Nodes[0];
			JsonElement rideNodes = rides[^1].Nodes;
			JsonElement rideLast = rideNodes[rideNodes.GetArrayLength() - 1];

			fingerprint =
				JourneyFingerprint.Compose(
					ReadTime(rideFirst, "departureDateTime")?.ToUnixTimeMilliseconds(),
					ReadTime(rideLast, "arrivalDateTime")?.ToUnixTimeMilliseconds(),
					rides.Select(
						ride =>
							ride.Partial.TryGetProperty("line", out JsonElement rideLine)
								? SchutzengelPlanList.ReadString(rideLine, "name")
								: null));
		}

		return new SchutzengelRawSummary(
			DDjourneys.Core.Models.StopLabel.Compose(
				SchutzengelPlanList.ReadString(firstNode, "name"),
				SchutzengelPlanList.ReadString(firstNode, "city")),
			DDjourneys.Core.Models.StopLabel.Compose(
				SchutzengelPlanList.ReadString(lastNode, "name"),
				SchutzengelPlanList.ReadString(lastNode, "city")),
			ReadTime(firstNode, "departureDateTime") ?? ReadTime(firstNode, "arrivalDateTime"),
			ReadTime(lastNode, "arrivalDateTime") ?? ReadTime(lastNode, "departureDateTime"),
			lines,
			fingerprint);
	}

	private static DateTimeOffset? ReadTime(JsonElement node, string name) =>
		node.ValueKind == JsonValueKind.Object
		&& node.TryGetProperty(name, out JsonElement value)
			? SchutzengelTime.Read(value)
			: null;
}
