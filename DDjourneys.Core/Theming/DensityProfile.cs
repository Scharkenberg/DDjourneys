namespace DDjourneys.Core.Theming;

/// <summary>
/// One UI density: how much room controls, rows and pages take. Normal is the authored design; the other
/// profiles derive from it, so every number in the XAML stays the one source of truth.
/// <para>
/// Two kinds of numbers: factors (applied to values written in XAML through <c>Dense.*</c>) and a token table
/// (the shared spacing and padding resources). Font sizes are deliberately not part of a density; text size
/// is its own axis.
/// </para>
/// </summary>
public sealed record DensityProfile
{
	public const string CompactId = "compact";
	public const string NormalId = "normal";
	public const string TouchId = "touch";

	/// <summary>Compact never shrinks a control below this (or below its authored size, if that is smaller): a fingertip is ~9 mm.</summary>
	public const double CompactHitFloor = 36;

	public static readonly DensityProfile Compact = new()
	{
		Id = CompactId,
		HeightFactor = 0.85,
		VerticalPaddingFactor = 0.5,
		HorizontalPaddingFactor = 0.85,
		SpaceXS = 3,
		SpaceS = 6,
		SpaceM = 8,
		SpaceL = 12,
		SpaceXL = 16,
		CardPadding = 10,
		PagePaddingHorizontal = 12,
		PagePaddingVertical = 8,
		FieldPaddingHorizontal = 12,
		FieldPaddingVertical = 6,
		SectionGap = 4
	};

	public static readonly DensityProfile Normal = new()
	{
		Id = NormalId,
		HeightFactor = 1,
		VerticalPaddingFactor = 1,
		HorizontalPaddingFactor = 1,
		SpaceXS = 4,
		SpaceS = 8,
		SpaceM = 12,
		SpaceL = 16,
		SpaceXL = 24,
		CardPadding = 14,
		PagePaddingHorizontal = 16,
		PagePaddingVertical = 12,
		FieldPaddingHorizontal = 14,
		FieldPaddingVertical = 8,
		SectionGap = 8
	};

	public static readonly DensityProfile Touch = new()
	{
		Id = TouchId,
		HeightFactor = 1.2,
		VerticalPaddingFactor = 1.5,
		HorizontalPaddingFactor = 1.1,
		SpaceXS = 5,
		SpaceS = 10,
		SpaceM = 16,
		SpaceL = 20,
		SpaceXL = 28,
		CardPadding = 18,
		PagePaddingHorizontal = 16,
		PagePaddingVertical = 16,
		FieldPaddingHorizontal = 16,
		FieldPaddingVertical = 12,
		SectionGap = 12
	};

	public static IReadOnlyList<DensityProfile> All { get; } = [Compact, Normal, Touch];

	public required string Id { get; init; }

	public required double HeightFactor { get; init; }

	public required double VerticalPaddingFactor { get; init; }

	public required double HorizontalPaddingFactor { get; init; }

	public required double SpaceXS { get; init; }

	public required double SpaceS { get; init; }

	public required double SpaceM { get; init; }

	public required double SpaceL { get; init; }

	public required double SpaceXL { get; init; }

	public required double CardPadding { get; init; }

	public required double PagePaddingHorizontal { get; init; }

	public required double PagePaddingVertical { get; init; }

	public required double FieldPaddingHorizontal { get; init; }

	public required double FieldPaddingVertical { get; init; }

	public required double SectionGap { get; init; }

	public static DensityProfile Find(string? id) =>
		All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Normal;

	/// <summary>Minimum height or width of a control. Shrinking stops at <see cref="CompactHitFloor"/>.</summary>
	public double Hit(double authored)
	{
		double scaled = Round(authored * HeightFactor);

		if (HeightFactor < 1)
		{
			scaled = Math.Max(scaled, Math.Min(authored, CompactHitFloor));
		}

		return scaled;
	}

	public double PaddingVertical(double authored) => Round(authored * VerticalPaddingFactor);

	public double PaddingHorizontal(double authored) => Round(authored * HorizontalPaddingFactor);

	private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
