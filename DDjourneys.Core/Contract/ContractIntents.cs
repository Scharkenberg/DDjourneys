using System.Globalization;
using System.Text.RegularExpressions;

namespace DDjourneys.Core.Contract;

/// <summary>
/// The things apps already know how to send, read as the short form <c>go</c> (to a destination, from where the user
/// starts): a <c>geo:</c> link (maps, browsers, messengers) and shared text (the share sheet). Pure, like the parser.
/// </summary>
public static class ContractIntents
{
	private static readonly Regex CoordinateQuery =
		new(@"^\s*(?<lat>-?\d{1,2}(?:\.\d+)?)\s*,\s*(?<lon>-?\d{1,3}(?:\.\d+)?)\s*(?:\((?<label>.*)\))?\s*$", RegexOptions.Compiled);

	/// <summary>
	/// A <c>geo:</c> link by its scheme-specific part: <c>51.05,13.73</c>, <c>0,0?q=Hellerau</c>,
	/// <c>51.05,13.73?q=Hellerau</c> or <c>0,0?q=51.05,13.73(Hellerau)</c>. Null when it names no place.
	/// </summary>
	public static ContractParseResult? FromGeo(string? schemeSpecificPart)
	{
		if (string.IsNullOrWhiteSpace(schemeSpecificPart)
			|| schemeSpecificPart.Length > ContractLimits.MaxUriLength)
		{
			return null;
		}

		string text = schemeSpecificPart.Trim();
		int question = text.IndexOf('?');
		string position = question < 0 ? text : text[..question];
		string query = question < 0 ? string.Empty : text[(question + 1)..];

		// "51.05,13.73;u=35": the uncertainty is of no use here.
		int semicolon = position.IndexOf(';');

		if (semicolon >= 0)
		{
			position = position[..semicolon];
		}

		(double Latitude, double Longitude)? point = Coordinates(position);

		// 0,0 means "no position, see q".
		if (point is { Latitude: 0, Longitude: 0 })
		{
			point = null;
		}

		string? name = null;

		foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			int equals = pair.IndexOf('=');

			if (equals <= 0
				|| !pair[..equals].Equals("q", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' ')).Trim();

			if (CoordinateQuery.Match(value) is { Success: true } match)
			{
				point ??= Coordinates($"{match.Groups["lat"].Value},{match.Groups["lon"].Value}");

				value = match.Groups["label"].Value.Trim();
			}

			name = value.Length == 0 ? null : value;
		}

		if (point is null && name is null)
		{
			return null;
		}

		var bag = new Dictionary<string, string?> { ["command"] = "go" };

		if (name is not null)
		{
			bag["to"] = name;
		}

		if (point is { } at)
		{
			bag["to.lat"] = at.Latitude.ToString(CultureInfo.InvariantCulture);
			bag["to.lon"] = at.Longitude.ToString(CultureInfo.InvariantCulture);
		}

		return ContractParser.Parse(bag);
	}

	/// <summary>Shared text: its first line is the destination. Null when there is nothing usable (a URL is not a place).</summary>
	public static ContractParseResult? FromSharedText(string? text)
	{
		string? line =
			text?
				.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.FirstOrDefault();

		if (string.IsNullOrWhiteSpace(line)
			|| line.Length > ContractLimits.MaxValueLength
			|| line.Contains("://", StringComparison.Ordinal)
			|| line.Any(char.IsControl))
		{
			return null;
		}

		return ContractParser.Parse(
			new Dictionary<string, string?>
			{
				["command"] = "go",
				["to"] = line
			});
	}

	private static (double Latitude, double Longitude)? Coordinates(string text)
	{
		string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

		return parts.Length == 2
			&& double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
			&& double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
			&& latitude is >= -90 and <= 90
			&& longitude is >= -180 and <= 180
				? (latitude, longitude)
				: null;
	}
}
