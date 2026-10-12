using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Services;

namespace DDjourneys.Core.Storage;

/// <summary>
/// A line the passenger watches for disruptions: the provider it belongs to (the same line name means
/// different things elsewhere) and the line's name as the stop's lines named it.
/// </summary>
public sealed record WatchedLine(
	string ProviderId,
	string Name);

/// <summary>
/// The watched lines as a stored list (versioned envelope, one entry per provider and line). Two lines are
/// the same line when their names differ only in spaces or case ("S 1" and "s1" — the rule
/// <see cref="LineCourseService.SameLine"/> uses, like the board filter). Reading is entry by entry like
/// every stored list, so one entry that does not fit costs that entry, never the whole list.
/// </summary>
public static class WatchedLineList
{
	public static IReadOnlyList<WatchedLine> Parse(string? json)
	{
		StoredRead<WatchedLine> read = StoredJson.Read(json, ReadEntry);

		return read.Items;
	}

	public static string Write(IReadOnlyList<WatchedLine> lines) =>
		StoredJson.Write(lines, ToNode);

	/// <summary>Whether <paramref name="providerId"/>'s list knows this line (the same-line rule applies).</summary>
	public static bool Contains(
		IReadOnlyList<WatchedLine> lines,
		string providerId,
		string? name) =>
		name is { Length: > 0 }
			&& lines.Any(line =>
				string.Equals(line.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
					&& LineCourseService.SameLine(line.Name, name));

	/// <summary>The list with this line watched or forgotten (the same-line rule applies); never null.</summary>
	public static IReadOnlyList<WatchedLine> With(
		IReadOnlyList<WatchedLine> lines,
		string providerId,
		string name,
		bool watched)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		List<WatchedLine> kept =
			[.. lines.Where(line =>
				!(string.Equals(line.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
					&& LineCourseService.SameLine(line.Name, name)))];

		if (watched)
		{
			kept.Add(new WatchedLine(providerId, name.Trim()));
		}

		return kept;
	}

	private static JsonNode? ToNode(WatchedLine line) =>
		new JsonObject
		{
			["ProviderId"] = line.ProviderId,
			["Name"] = line.Name
		};

	private static WatchedLine? ReadEntry(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "ProviderId") is not { Length: > 0 } providerId
			|| StoredJson.String(element, "Name") is not { Length: > 0 } name)
		{
			return null;
		}

		return new WatchedLine(providerId, name);
	}
}
