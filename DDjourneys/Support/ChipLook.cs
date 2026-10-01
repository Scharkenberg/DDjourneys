using DDjourneys.Core.Models;
using Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Support;

/// <summary>
/// How one non-interactive mode pill looks. Colour is never the only cue:
/// every mode also has its own shape and fill/outline treatment, so pills
/// stay distinguishable with colour-vision deficiencies or low contrast
/// sensitivity.
/// </summary>
public sealed record ChipLook(
	Color Fill,
	Color Text,
	Color Stroke,
	double StrokeThickness,
	CornerRadius Corners,
	bool Dashed)
{
	/// <summary>A fresh shape per binding (shapes must not be shared between views).</summary>
	public IShape Shape => new RoundRectangle { CornerRadius = Corners };

	public DoubleCollection Dashes =>
		Dashed
			? [3, 2]
			: [];
}

/// <summary>
/// Pill looks per transport mode (generic per mode, not official line colours).
/// Shape language: tram = full pill, bus = rounded square, suburban rail = leaf,
/// train = outlined near-square, ferry = mirrored leaf, cable car = outlined pill,
/// walking = dashed outline without fill.
/// </summary>
public static class ModeChips
{
	private static readonly CornerRadius Pill = new(100);
	private static readonly CornerRadius Square = new(5);
	// CornerRadius(topLeft, topRight, bottomLeft, bottomRight): a leaf rounds OPPOSITE corners
	private static readonly CornerRadius Leaf = new(13, 2, 2, 13);
	private static readonly CornerRadius LeafMirrored = new(2, 13, 13, 2);
	private static readonly CornerRadius Block = new(2);

	public static ChipLook For(TransitMode mode)
	{
		Color fill = ModeColors.For(mode);

		return mode switch
		{
			TransitMode.Tram =>
				Solid(fill, Pill),

			TransitMode.Bus =>
				Solid(fill, Square),

			TransitMode.SuburbanRail =>
				Solid(fill, Leaf),

			TransitMode.Ferry =>
				Solid(fill, LeafMirrored),

			TransitMode.CableCar =>
				new ChipLook(
					fill,
					Colors.White,
					Colors.White.WithAlpha(0.85f),
					2,
					Pill,
					false),

			TransitMode.Walk =>
				new ChipLook(
					Colors.Transparent,
					ThemeColor("InkMuted", Colors.Gray),
					ThemeColor("InkMuted", Colors.Gray),
					1.5,
					Pill,
					true),

			// Trains and everything else
			_ =>
				new ChipLook(
					fill,
					Colors.White,
					Colors.White.WithAlpha(0.85f),
					2,
					Block,
					false)
		};
	}

	private static ChipLook Solid(Color fill, CornerRadius corners) =>
		new(fill, Colors.White, Colors.Transparent, 0, corners, false);

	private static Color ThemeColor(string key, Color fallback) =>
		Application.Current?.Resources.TryGetValue(key, out object? value) == true
		&& value is Color color
			? color
			: fallback;
}