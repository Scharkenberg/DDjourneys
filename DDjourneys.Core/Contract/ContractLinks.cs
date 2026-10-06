using System.Globalization;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Contract;

/// <summary>
/// Builds contract links, so apps (and this one) never hand-assemble query strings. The same keys work
/// as Android string extras. Every link is parsed back by <see cref="ContractParser"/> in the tests.
/// </summary>
public static class ContractLinks
{
	/// <summary>A <c>plan</c> link: fills the planner and, with <paramref name="search"/>, searches.</summary>
	public static Uri Plan(
		ContractPlace? from,
		ContractPlace? to,
		DateTimeOffset? at = null,
		JourneySearchMode? mode = null,
		bool search = false,
		string? reference = null,
		ContractPlace? via = null) =>
		Build(ContractCommand.Plan, from, to, at, mode, search, reference, null, null, via);

	/// <summary>A <c>pick</c> link: the user chooses a journey, which comes back to <paramref name="success"/>.</summary>
	public static Uri Pick(
		ContractPlace from,
		ContractPlace to,
		Uri success,
		Uri? error = null,
		DateTimeOffset? at = null,
		JourneySearchMode? mode = null,
		string? reference = null)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		ArgumentNullException.ThrowIfNull(success);

		return Build(ContractCommand.Pick, from, to, at, mode, true, reference, success, error);
	}

	/// <summary>
	/// The short form: a <c>go</c> link takes the user to <paramref name="to"/> from where they start (the app's start
	/// setting, else the device), now, with the search done. <paramref name="to"/> may be a name, a stop or
	/// coordinates; <see cref="ContractKeywords"/> work as names (<c>@home</c>).
	/// </summary>
	public static Uri Go(
		ContractPlace to,
		ContractPlace? from = null,
		DateTimeOffset? at = null,
		JourneySearchMode? mode = null,
		string? reference = null)
	{
		ArgumentNullException.ThrowIfNull(to);

		return Build(ContractCommand.Go, from, to, at, mode, true, reference, null, null);
	}

	/// <summary>
	/// A <c>departures</c> link: the board of a place (the stop nearest to the device when none is given); with
	/// <paramref name="arrivals"/> the arrivals.
	/// </summary>
	public static Uri Departures(
		ContractPlace? at = null,
		bool arrivals = false,
		DateTimeOffset? time = null)
	{
		var pairs = new List<KeyValuePair<string, string>>();

		AddPlace(pairs, "at", at);

		if (time is { } when)
		{
			pairs.Add(new("time", JourneyPayload.Time(when)));
		}

		if (arrivals)
		{
			pairs.Add(new("mode", "arr"));
		}

		return Create(ContractCommand.Departures, pairs);
	}

	/// <summary>A <c>home</c> link: from where the device is to the user's home, now.</summary>
	public static Uri Home() =>
		Create(ContractCommand.Home, []);

	/// <summary>A <c>map</c> link: the map, centred on <paramref name="at"/> (else on the user).</summary>
	public static Uri Map(ContractPlace? at = null)
	{
		var pairs = new List<KeyValuePair<string, string>>();

		AddPlace(pairs, "at", at);

		return Create(ContractCommand.Map, pairs);
	}

	/// <summary>A <c>disruptions</c> link, limited to a line when given.</summary>
	public static Uri Disruptions(string? line = null) =>
		Create(
			ContractCommand.Disruptions,
			string.IsNullOrWhiteSpace(line) ? [] : [new("line", line)]);

	/// <summary>A <c>live</c> link: the live vehicles of the line number(s), "3" or "3,11".</summary>
	public static Uri Live(string line)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(line);

		return Create(ContractCommand.Live, [new("line", line)]);
	}

	/// <summary>A <c>tracked</c> link: the followed journeys, focused on a plan when given.</summary>
	public static Uri Tracked(string? planId = null)
	{
		var pairs = new List<KeyValuePair<string, string>>();

		if (!string.IsNullOrWhiteSpace(planId))
		{
			pairs.Add(new("plan", planId));
		}

		return Create(ContractCommand.Tracked, pairs);
	}

	/// <summary>A <c>capabilities</c> link; the answer goes to <paramref name="success"/>.</summary>
	public static Uri Capabilities(Uri success, string? reference = null)
	{
		ArgumentNullException.ThrowIfNull(success);

		var pairs = new List<KeyValuePair<string, string>> { new("x-success", success.OriginalString) };

		if (reference is not null)
		{
			pairs.Add(new("ref", reference));
		}

		return Create(ContractCommand.Capabilities, pairs);
	}

	private static Uri Build(
		ContractCommand command,
		ContractPlace? from,
		ContractPlace? to,
		DateTimeOffset? at,
		JourneySearchMode? mode,
		bool search,
		string? reference,
		Uri? success,
		Uri? error,
		ContractPlace? via = null)
	{
		var pairs = new List<KeyValuePair<string, string>>();

		AddPlace(pairs, "from", from);
		AddPlace(pairs, "to", to);
		AddPlace(pairs, "via", via);

		if (at is { } time)
		{
			pairs.Add(new("time", JourneyPayload.Time(time)));
		}

		if (mode is { } searchMode)
		{
			pairs.Add(new("mode", searchMode == JourneySearchMode.Arrival ? "arr" : "dep"));
		}

		if (search && command == ContractCommand.Plan)
		{
			pairs.Add(new("search", "1"));
		}

		if (reference is not null)
		{
			pairs.Add(new("ref", reference));
		}

		if (success is not null)
		{
			pairs.Add(new("x-success", success.OriginalString));
		}

		if (error is not null)
		{
			pairs.Add(new("x-error", error.OriginalString));
		}

		return Create(command, pairs);
	}

	private static void AddPlace(List<KeyValuePair<string, string>> pairs, string prefix, ContractPlace? place)
	{
		if (place is null)
		{
			return;
		}

		if (place.Name is not null)
		{
			pairs.Add(new(prefix, place.Name));
		}

		if (place.StopKey is not null)
		{
			pairs.Add(new($"{prefix}.stop", place.StopKey));
		}

		if (place is { Latitude: { } lat, Longitude: { } lon })
		{
			pairs.Add(new($"{prefix}.lat", lat.ToString("0.######", CultureInfo.InvariantCulture)));
			pairs.Add(new($"{prefix}.lon", lon.ToString("0.######", CultureInfo.InvariantCulture)));
		}
	}

	private static Uri Create(ContractCommand command, List<KeyValuePair<string, string>> pairs) =>
		new(
			ContractReply.Append(
				$"{ContractVersion.Scheme}://v{ContractVersion.Current.ToString(CultureInfo.InvariantCulture)}/{command.Name()}",
				pairs),
			UriKind.Absolute);
}
