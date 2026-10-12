using System.Text.Json;
using System.Text.Json.Nodes;

namespace DDjourneys.Core.Storage;

/// <summary>
/// The last-seen disruption fingerprint of one watched line (see <see cref="Services.LineDisruptions"/>):
/// what the last check answered for the line, so the next check can tell a change from the same state.
/// The line is stored in the normalized form of <see cref="Services.BoardFilter.LineKey"/> ("S 1" → "S1"),
/// so a name that only differs in spaces or case finds its own state again.
/// </summary>
public sealed record WatchedLineState(
	string ProviderId,
	string Line,
	string Fingerprint);

/// <summary>
/// The fingerprint states of the watched lines as a stored list (versioned envelope). This is bookkeeping,
/// not passenger data: dropping it costs at most one suppressed "changed" comparison on the next check,
/// never a notification the passenger missed. Reading is entry by entry like every stored list.
/// </summary>
public static class WatchedLineStateList
{
	public static IReadOnlyList<WatchedLineState> Parse(string? json)
	{
		StoredRead<WatchedLineState> read = StoredJson.Read(json, ReadEntry);

		return read.Items;
	}

	public static string Write(IReadOnlyList<WatchedLineState> states) =>
		StoredJson.Write(states, ToNode);

	private static JsonNode? ToNode(WatchedLineState state) =>
		new JsonObject
		{
			["ProviderId"] = state.ProviderId,
			["Line"] = state.Line,
			["Fingerprint"] = state.Fingerprint
		};

	private static WatchedLineState? ReadEntry(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "ProviderId") is not { Length: > 0 } providerId
			|| StoredJson.String(element, "Line") is not { Length: > 0 } line)
		{
			return null;
		}

		return new WatchedLineState(providerId, line, StoredJson.String(element, "Fingerprint") ?? string.Empty);
	}
}
