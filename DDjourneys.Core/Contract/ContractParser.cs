using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Contract;

/// <summary>
/// Turns what a transport delivered (a link, or string extras) into a validated
/// <see cref="ContractRequest"/>, or into a <see cref="ContractFailure"/> that still carries the
/// reply address when that could be read. Pure and deterministic: no clock, no I/O.
/// Unknown keys are ignored with a warning, so newer callers keep working within one version.
/// </summary>
public static partial class ContractParser
{
	private static readonly FrozenSet<string> KnownKeys =
		new[]
		{
			"command", "v", "from", "from.stop", "from.lat", "from.lon", "to", "to.stop", "to.lat",
			"to.lon", "via", "via.stop", "via.lat", "via.lon", "at", "at.stop", "at.lat", "at.lon", "line",
			"time", "mode", "search", "plan", "ref", "x-success", "x-error"
		}.ToFrozenSet(StringComparer.Ordinal);

	[GeneratedRegex("^[A-Za-z0-9_-]{1,24}:[A-Za-z0-9_.:-]{1,64}$")]
	private static partial Regex StopKeyShape();

	[GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ,;/.+-]{0,63}$")]
	private static partial Regex LineShape();

	[GeneratedRegex(@"^\d{1,4}([ ,;]+\d{1,4}){0,9}$")]
	private static partial Regex NumbersShape();

	[GeneratedRegex("^[\\x21-\\x7E]{1,64}$")]
	private static partial Regex ReferenceShape();

	[GeneratedRegex("^v(\\d{1,3})$", RegexOptions.IgnoreCase)]
	private static partial Regex VersionSegment();

	private static readonly string[] WallFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm"];

	private static readonly string[] OffsetFormats =
	[
		"yyyy-MM-dd'T'HH:mm:sszzz",
		"yyyy-MM-dd'T'HH:mmzzz",
		"yyyy-MM-dd'T'HH:mm:ss'Z'",
		"yyyy-MM-dd'T'HH:mm'Z'"
	];

	// ----- Entry points -----

	/// <summary>Parses a <c>ddjourneys://plan?...</c> or <c>ddjourneys://v1/plan?...</c> link.</summary>
	public static ContractParseResult ParseUri(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| text.Length > ContractLimits.MaxUriLength
			|| !Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? uri))
		{
			return Fail(ContractErrorCode.Malformed, "The link is empty, too long or not a valid URI.");
		}

		return ParseUri(uri);
	}

	public static ContractParseResult ParseUri(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);

		if (uri.OriginalString.Length > ContractLimits.MaxUriLength)
		{
			return Fail(ContractErrorCode.Malformed, "The link is too long.");
		}

		if (!string.Equals(uri.Scheme, ContractVersion.Scheme, StringComparison.OrdinalIgnoreCase))
		{
			return Fail(ContractErrorCode.Malformed, $"The scheme must be '{ContractVersion.Scheme}'.");
		}

		var bag = new Dictionary<string, string?>(StringComparer.Ordinal);
		var warnings = new List<string>();

		// "ddjourneys://plan" (host is the command) or "ddjourneys://v1/plan" (host is the version).
		string host = uri.Host;
		string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (VersionSegment().Match(host) is { Success: true } version)
		{
			bag["v"] = version.Groups[1].Value;

			if (segments.Length > 0)
			{
				bag["command"] = segments[0];
			}
		}
		else
		{
			bag["command"] = host.Length > 0 ? host : segments.FirstOrDefault();
		}

		string query = uri.Query.TrimStart('?');

		if (query.Length > 0)
		{
			foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
			{
				int equals = pair.IndexOf('=');
				string key = Decode(equals < 0 ? pair : pair[..equals]).Trim().ToLowerInvariant();
				string value = equals < 0 ? string.Empty : Decode(pair[(equals + 1)..]);

				if (key.Length == 0)
				{
					continue;
				}

				if (bag.TryGetValue(key, out string? existing))
				{
					if (existing is null)
					{
						bag[key] = value;
					}
					else
					{
						warnings.Add(
							key is "command" or "v"
								? $"'{key}' in the query was ignored; the path decides it."
								: $"Duplicate '{key}' ignored.");
					}
				}
				else
				{
					bag[key] = value;
				}

				if (bag.Count > ContractLimits.MaxKeys)
				{
					return Fail(ContractErrorCode.Malformed, "Too many parameters.");
				}
			}
		}

		return Parse(bag, warnings);
	}

	/// <summary>Parses key/value pairs (Android string extras, or anything else that is not a link).</summary>
	public static ContractParseResult Parse(IReadOnlyDictionary<string, string?> parameters) =>
		Parse(parameters, []);

	// ----- Core -----

	private static ContractParseResult Parse(
		IReadOnlyDictionary<string, string?> parameters,
		List<string> warnings)
	{
		ArgumentNullException.ThrowIfNull(parameters);

		if (parameters.Count > ContractLimits.MaxKeys)
		{
			return Fail(ContractErrorCode.Malformed, "Too many parameters.");
		}

		var bag = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach ((string rawKey, string? rawValue) in parameters)
		{
			string key = rawKey.Trim().ToLowerInvariant();

			if (key.Length == 0 || rawValue is null)
			{
				continue;
			}

			if (!bag.TryAdd(key, rawValue.Trim()))
			{
				warnings.Add($"Duplicate '{key}' ignored.");
			}
		}

		// Reply address first: every later failure can still be answered.
		ContractCallbacks callbacks = ContractCallbacks.None;
		string? reference = null;
		ContractCommand? command = null;

		try
		{
			callbacks = ReadCallbacks(bag);
			reference = ReadReference(bag);

			int version = ReadVersion(bag);

			if (version > ContractVersion.Current || version < ContractVersion.Oldest)
			{
				throw new Refusal(
					ContractErrorCode.UnsupportedVersion,
					$"Version {version} is not supported; this app speaks {ContractVersion.Oldest}..{ContractVersion.Current}.",
					"v");
			}

			if (!bag.TryGetValue("command", out string? name) || name.Length == 0)
			{
				throw new Refusal(ContractErrorCode.MissingParameter, "No command given.", "command");
			}

			if (!ContractWire.TryParseCommand(name, out ContractCommand parsed))
			{
				throw new Refusal(ContractErrorCode.UnknownCommand, $"Unknown command '{Shorten(name)}'.", "command");
			}

			command = parsed;

			foreach (string key in bag.Keys)
			{
				if (!KnownKeys.Contains(key) && warnings.Count < 10)
				{
					warnings.Add($"Ignored unknown parameter '{Shorten(key)}'.");
				}
			}

			ContractRequest request = Build(parsed, version, bag, callbacks, reference, warnings);

			return ContractParseResult.Ok(request);
		}
		catch (Refusal refusal)
		{
			return ContractParseResult.Fail(
				new ContractFailure(
					refusal.Code,
					refusal.Message,
					refusal.Parameter,
					command,
					reference,
					callbacks));
		}
	}

	private static ContractRequest Build(
		ContractCommand command,
		int version,
		Dictionary<string, string> bag,
		ContractCallbacks callbacks,
		string? reference,
		List<string> warnings)
	{
		ContractPlace? from = null;
		ContractPlace? to = null;
		ContractPlace? via = null;
		ContractPlace? at = null;
		ContractTime? time = null;
		JourneySearchMode? mode = null;
		bool search = false;
		string? planId = null;
		string? line = null;

		switch (command)
		{
			case ContractCommand.Plan:
			case ContractCommand.Pick:
			case ContractCommand.Go:
				from = ReadPlace(bag, "from");
				to = ReadPlace(bag, "to");
				via = ReadPlace(bag, "via");
				time = ReadTime(bag);
				mode = ReadMode(bag);
				search = command != ContractCommand.Plan || ReadFlag(bag, "search");

				if (from is null && to is null)
				{
					throw new Refusal(
						ContractErrorCode.MissingParameter,
						"Give at least a start or a destination.",
						"to");
				}

				// Searching without a start is not an error: the start is where the user starts (the app's setting).
				// A pick still names both ends, since the caller asked for exactly that journey.
				if (search && from is null && command != ContractCommand.Pick)
				{
					from = new ContractPlace(ContractKeywords.Start, null, null, null);
				}

				if (command == ContractCommand.Go && to is null)
				{
					throw new Refusal(ContractErrorCode.MissingParameter, "Give a destination.", "to");
				}

				if (search && (from is null || to is null))
				{
					throw new Refusal(
						ContractErrorCode.MissingParameter,
						"Searching needs a destination (and, for a pick, a start).",
						from is null ? "from" : "to");
				}

				if (command == ContractCommand.Pick && callbacks.Success is null)
				{
					throw new Refusal(
						ContractErrorCode.MissingParameter,
						"A pick needs 'x-success' to hand the journey back to.",
						"x-success");
				}

				break;

			case ContractCommand.Tracked:
				planId = Text(bag, "plan", ContractLimits.MaxValueLength);
				break;

			case ContractCommand.Departures:
				// No place: where the device is.
				at = ReadPlace(bag, "at") ?? new ContractPlace(ContractKeywords.Here, null, null, null);
				time = ReadTime(bag);
				mode = ReadMode(bag);
				break;

			case ContractCommand.Map:
				at = ReadPlace(bag, "at");
				break;

			case ContractCommand.Disruptions:
				line = ReadLine(bag, numbersOnly: false);
				break;

			case ContractCommand.Live:
				line = ReadLine(bag, numbersOnly: true)
					?? throw new Refusal(ContractErrorCode.MissingParameter, "Give the line(s) to follow.", "line");
				break;
		}

		return new ContractRequest
		{
			Command = command,
			Version = version,
			From = from,
			To = to,
			Via = via,
			At = at,
			Line = line,
			Time = time,
			Mode = mode,
			Search = search,
			PlanId = planId,
			Callbacks = callbacks,
			Reference = reference,
			Warnings = warnings,
			Fingerprint = Fingerprint(command, bag)
		};
	}

	// ----- Fields -----

	private static string? ReadReference(Dictionary<string, string> bag)
	{
		string? value = Text(bag, "ref", ContractLimits.MaxReferenceLength);

		if (value is not null && !ReferenceShape().IsMatch(value))
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				"'ref' must be 1-64 printable ASCII characters without spaces.",
				"ref");
		}

		return value;
	}

	private static ContractCallbacks ReadCallbacks(Dictionary<string, string> bag)
	{
		Uri? success = Callback(bag, "x-success");
		Uri? error = Callback(bag, "x-error");

		return success is null && error is null
			? ContractCallbacks.None
			: new ContractCallbacks(success, error);
	}

	private static Uri? Callback(Dictionary<string, string> bag, string key)
	{
		if (!bag.TryGetValue(key, out string? text) || text.Length == 0)
		{
			return null;
		}

		if (!ContractCallbackPolicy.TryParse(text, out Uri? uri))
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{key}' must be an https or custom-scheme URI (at most {ContractLimits.MaxCallbackLength} characters).",
				key);
		}

		return uri;
	}

	private static int ReadVersion(Dictionary<string, string> bag)
	{
		if (!bag.TryGetValue("v", out string? text) || text.Length == 0)
		{
			return ContractVersion.Current;
		}

		if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
			|| version < 1)
		{
			throw new Refusal(ContractErrorCode.InvalidParameter, "'v' must be a positive integer.", "v");
		}

		return version;
	}

	private static ContractPlace? ReadPlace(Dictionary<string, string> bag, string prefix)
	{
		string? name = Text(bag, prefix, ContractLimits.MaxValueLength);
		string? stop = Text(bag, $"{prefix}.stop", ContractLimits.MaxValueLength);
		double? lat = Number(bag, $"{prefix}.lat", -90, 90);
		double? lon = Number(bag, $"{prefix}.lon", -180, 180);

		if (lat.HasValue != lon.HasValue)
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{prefix}.lat' and '{prefix}.lon' belong together.",
				lat.HasValue ? $"{prefix}.lon" : $"{prefix}.lat");
		}

		if (stop is not null)
		{
			if (!StopKeyShape().IsMatch(stop))
			{
				throw new Refusal(
					ContractErrorCode.InvalidParameter,
					$"'{prefix}.stop' must look like 'provider:id', for example 'vvo:33000028'.",
					$"{prefix}.stop");
			}

			int colon = stop.IndexOf(':');

			stop = stop[..colon].ToLowerInvariant() + stop[colon..];
		}

		if (ContractKeywords.IsKeyword(name)
			&& !ContractKeywords.IsKnown(name))
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{prefix}' may be a name or one of {ContractKeywords.Here}, {ContractKeywords.Home}, {ContractKeywords.Start}.",
				prefix);
		}

		ContractPlace place = new(ContractKeywords.IsKeyword(name) ? name!.ToLowerInvariant() : name, stop, lat, lon);

		return place.IsEmpty ? null : place;
	}

	private static ContractTime? ReadTime(Dictionary<string, string> bag)
	{
		string? text = Text(bag, "time", ContractLimits.MaxValueLength);

		if (text is null)
		{
			return null;
		}

		if (text.Equals("now", StringComparison.OrdinalIgnoreCase))
		{
			return new ContractTime(true, null, null);
		}

		if (DateTimeOffset.TryParseExact(
				text,
				OffsetFormats,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal,
				out DateTimeOffset absolute))
		{
			return new ContractTime(false, absolute, null);
		}

		if (DateTime.TryParseExact(
				text,
				WallFormats,
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out DateTime wall))
		{
			return new ContractTime(false, null, DateTime.SpecifyKind(wall, DateTimeKind.Unspecified));
		}

		throw new Refusal(
			ContractErrorCode.InvalidParameter,
			"'time' must be 'now', '2026-10-05T08:30' (provider time) or '2026-10-05T08:30+02:00'.",
			"time");
	}

	private static JourneySearchMode? ReadMode(Dictionary<string, string> bag)
	{
		string? text = Text(bag, "mode", ContractLimits.MaxValueLength);

		return text?.ToLowerInvariant() switch
		{
			null => null,
			"dep" or "departure" => JourneySearchMode.Departure,
			"arr" or "arrival" => JourneySearchMode.Arrival,
			_ => throw new Refusal(
				ContractErrorCode.InvalidParameter,
				"'mode' must be 'dep' or 'arr'.",
				"mode")
		};
	}

	private static bool ReadFlag(Dictionary<string, string> bag, string key)
	{
		string? text = Text(bag, key, 8);

		return text?.ToLowerInvariant() switch
		{
			null => false,
			"1" or "true" or "yes" => true,
			"0" or "false" or "no" => false,
			_ => throw new Refusal(ContractErrorCode.InvalidParameter, $"'{key}' must be 1 or 0.", key)
		};
	}

	private static double? Number(Dictionary<string, string> bag, string key, double min, double max)
	{
		string? text = Text(bag, key, 32);

		if (text is null)
		{
			return null;
		}

		if (!double.TryParse(
				text,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out double value)
			|| !double.IsFinite(value)
			|| value < min
			|| value > max)
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{key}' must be a number between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.",
				key);
		}

		return value;
	}

	/// <summary>A trimmed, control-character-free value; null when absent or empty.</summary>
	private static string? Text(Dictionary<string, string> bag, string key, int maxLength)
	{
		if (!bag.TryGetValue(key, out string? value) || value.Length == 0)
		{
			return null;
		}

		if (value.Length > maxLength)
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{key}' is longer than {maxLength} characters.",
				key);
		}

		if (value.Any(char.IsControl))
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				$"'{key}' contains control characters.",
				key);
		}

		return value;
	}

	/// <summary>Line names ("3", "S1", "3, 11"); with <paramref name="numbersOnly"/> only numbers (the live positions know no others).</summary>
	private static string? ReadLine(Dictionary<string, string> bag, bool numbersOnly)
	{
		string? text = Text(bag, "line", ContractLimits.MaxValueLength);

		if (text is null)
		{
			return null;
		}

		if (!LineShape().IsMatch(text)
			|| (numbersOnly && !NumbersShape().IsMatch(text)))
		{
			throw new Refusal(
				ContractErrorCode.InvalidParameter,
				numbersOnly
					? "'line' must be one or more line numbers, for example '3' or '3,11'."
					: "'line' must be one or more line names, for example '3', 'S1' or '3,11'.",
				"line");
		}

		return text;
	}

	private static string Fingerprint(ContractCommand command, Dictionary<string, string> bag)
	{
		var builder = new StringBuilder(command.Name());

		foreach (string key in bag.Keys.Where(KnownKeys.Contains).Where(k => k != "ref").Order(StringComparer.Ordinal))
		{
			builder.Append('|').Append(key).Append('=').Append(bag[key]);
		}

		return builder.ToString();
	}

	private static string Decode(string text) =>
		Uri.UnescapeDataString(text.Replace('+', ' '));

	private static string Shorten(string text) =>
		text.Length <= 32 ? text : text[..32] + "…";

	private static ContractParseResult Fail(ContractErrorCode code, string message) =>
		ContractParseResult.Fail(new ContractFailure(code, message));

	/// <summary>Control flow inside the parser only; never escapes <see cref="Parse"/>.</summary>
	private sealed class Refusal(ContractErrorCode code, string message, string? parameter)
		: Exception(message)
	{
		public ContractErrorCode Code { get; } = code;

		public string? Parameter { get; } = parameter;
	}
}
