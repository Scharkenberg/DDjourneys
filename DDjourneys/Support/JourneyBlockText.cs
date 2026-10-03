using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>Human wording for <see cref="JourneyBlock"/>: "Not possible · Schweriner Straße is not served".</summary>
public static class JourneyBlockText
{
	/// <summary>The reason alone ("Schweriner Straße is not served"); empty without a block.</summary>
	public static string Reason(JourneyBlock? block, JourneyStrings strings)
	{
		ArgumentNullException.ThrowIfNull(strings);

		if (block is null)
		{
			return string.Empty;
		}

		CultureInfo culture = CultureInfo.CurrentCulture;
		string stop = block.Stop?.Name ?? string.Empty;

		return block.Kind switch
		{
			JourneyBlockKind.RideCancelled when block.Line is { Length: > 0 } line =>
				string.Format(culture, strings.BlockRideCancelled, line),
			JourneyBlockKind.RideCancelled => strings.Cancelled,
			JourneyBlockKind.BoardingNotServed or JourneyBlockKind.AlightingNotServed =>
				string.Format(culture, strings.BlockNotServed, stop),
			JourneyBlockKind.ConnectionBroken =>
				string.Format(culture, strings.BlockConnection, stop),
			_ => string.Empty
		};
	}

	/// <summary>Headline plus reason; empty without a block.</summary>
	public static string Describe(JourneyBlock? block, JourneyStrings strings)
	{
		string reason = Reason(block, strings);

		return block is null
			? string.Empty
			: reason.Length > 0
				? $"{strings.NotPossible} · {reason}"
				: strings.NotPossible;
	}
}
