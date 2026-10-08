using DDjourneys.Core.Models;
using Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Support;

/// <summary>
/// How one non-interactive mode pill looks. Colour is never the only cue:
/// every mode also has its own shape and fill/outline treatment, so pills
/// stay distinguishable with colour-vision deficiencies or low contrast
/// sensitivity.
/// </summary>
public sealed partial class ChipLook : System.ComponentModel.INotifyPropertyChanged
{
	private readonly Color _text;
	private readonly Color _stroke;
	private readonly string? _themeKey;

	public ChipLook(
		Color fill,
		Color text,
		Color stroke,
		double strokeThickness,
		CornerRadius corners,
		bool dashed,
		string? themeKey = null)
	{
		Fill = fill;
		_text = text;
		_stroke = stroke;
		StrokeThickness = strokeThickness;
		Corners = corners;
		Dashed = dashed;
		_themeKey = themeKey;

		if (themeKey is not null)
		{
			// Only a few long-lived instances are theme-aware, so the static subscription cannot leak.
			Theme.Changed += (_, _) =>
			{
				PropertyChanged?.Invoke(this, new(nameof(Text)));
				PropertyChanged?.Invoke(this, new(nameof(Stroke)));
			};
		}
	}

	public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

	public Color Fill { get; }

	/// <summary>Text colour; follows the theme for looks created with a theme key.</summary>
	public Color Text => _themeKey is null ? _text : ThemeColor(_themeKey, _text);

	public Color Stroke => _themeKey is null ? _stroke : ThemeColor(_themeKey, _stroke);

	public double StrokeThickness { get; }

	public CornerRadius Corners { get; }

	public bool Dashed { get; }

	/// <summary>A fresh shape per binding (shapes must not be shared between views).</summary>
	public IShape Shape => new RoundRectangle { CornerRadius = Corners };

	public DoubleCollection Dashes =>
		Dashed
			? [3, 2]
			: [];

	internal static Color ThemeColor(string key, Color fallback) =>
		Application.Current?.Resources.TryGetValue(key, out object? value) == true
		&& value is Color color
			? color
			: fallback;
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

	// One shared instance: its muted colours follow the theme through property-change notifications.
	private static readonly ChipLook WalkLook =
		new(Colors.Transparent, Colors.Gray, Colors.Gray, 1.5, Pill, true, "InkMuted");

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
				WalkLook,

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

	/// <summary>The same chip with the accent as its outline: the ride that is under way.</summary>
	public static ChipLook Marked(TransitMode mode)
	{
		ChipLook look = For(mode);

		return new ChipLook(
			look.Fill == Colors.Transparent ? Colors.Transparent : look.Fill,
			look.Text,
			Application.Current?.Resources.TryGetValue("Accent", out object? accent) == true && accent is Color accentColor ? accentColor : Colors.White,
			Math.Max(2.5, look.StrokeThickness),
			look.Corners,
			false);
	}

	private static ChipLook Solid(Color fill, CornerRadius corners) =>
		new(fill, Colors.White, Colors.Transparent, 0, corners, false);
}
