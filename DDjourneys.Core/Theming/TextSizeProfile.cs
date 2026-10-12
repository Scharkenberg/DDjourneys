namespace DDjourneys.Core.Theming;

/// <summary>
/// The app's own text-size axis (B11): a multiplier on the seven type-scale tokens of Tokens.xaml. The tokens
/// themselves stay the designed values; the applier writes the scaled numbers into the resources, and the style
/// setters read them as DynamicResource so a change reaches every styled label at once. Min sizes and rows scale
/// with the same factor (through the density applier), so bigger text never gets clipped.
/// </summary>
public static class TextSizeProfile
{
	public const string NormalId = "normal";
	public const string LargeId = "large";
	public const string LargestId = "largest";

	public static IReadOnlyList<string> Ids { get; } = [NormalId, LargeId, LargestId];

	public static string Normalize(string? id) =>
		id switch
		{
			LargeId => LargeId,
			LargestId => LargestId,
			_ => NormalId
		};

	/// <summary>The factor of one step on the app's own tokens (and of the row scaling that goes with it).</summary>
	public static double FactorOf(string? id) =>
		Normalize(id) switch
		{
			LargeId => 1.17,
			LargestId => 1.34,
			_ => 1.0
		};

	/// <summary>The seven type-scale tokens (Tokens.xaml keys) with their designed values.</summary>
	public static IReadOnlyList<(string Key, double Size)> Tokens { get; } =
	[
		("FontMicro", 12),
		("FontCaption", 14),
		("FontBody", 16),
		("FontSubtitle", 18),
		("FontTitle", 24),
		("FontHeadline", 28),
		("FontTime", 28)
	];
}
