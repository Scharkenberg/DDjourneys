using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using static DDjourneys.Tracking.Schutzengel.SchutzengelWireCodes;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Price and fare-zone fields of the raw data.
/// </summary>
internal static partial class SchutzengelTariffMapper
{
	internal static int ParsePrice(
		string? price)
	{
		if (string.IsNullOrWhiteSpace(
			price))
		{
			return 0;
		}


		return int.TryParse(
			price.Replace(
				",",
				string.Empty,
				StringComparison.Ordinal),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int value)
			? value
			: 0;
	}


	internal static string[] ZoneStrings(
		string? value)
	{
		if (string.IsNullOrWhiteSpace(
			value))
		{
			return
			[
				string.Empty,
				string.Empty
			];
		}


		string[] parts =
			value.Split(
				',',
				StringSplitOptions.None);


		string First(int index) =>
			index < parts.Length
				? ZoneNoiseRx().Replace(parts[index], string.Empty).Trim()
				: string.Empty;


		return
		[
			First(0),
			First(1)
		];
	}

	[GeneratedRegex(@"TZ\s|\(\d+\)")]
	private static partial Regex ZoneNoiseRx();
}
