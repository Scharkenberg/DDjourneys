using System.Collections.Concurrent;
using DDjourneys.Core.Models;
using DDjourneys.Core.Widgets;
using SkiaSharp;

namespace DDjourneys.Platforms.Windows.Widgets;

/// <summary>
/// The line chips of the widget cards. An Adaptive Card cannot colour or shape a container, so a chip is drawn
/// once as a small PNG and embedded as a data URI (the Image element accepts one from card version 1.2): the
/// fill, the shape and the white lettering of the chips in the app (Tokens.xaml, <c>ModeChips</c>), at three
/// times the size so it stays sharp on every display scale. The colours are written here and not read from the
/// app's resources: a widget start never builds the UI, so there are no resources to read.
/// </summary>
internal static class WidgetChipImages
{
	/// <summary>Chip height in card pixels (the picture itself is drawn at <see cref="Scale"/> times this).</summary>
	private const int Height = 24;

	private const int MinWidth = 32;
	private const int MaxWidth = 72;
	private const float Scale = 3;
	private const float TextSize = 13;

	private static readonly ConcurrentDictionary<(string Text, TransitMode Mode), WidgetChipImage> Cache = new();

	/// <summary>The lettering face, looked up once.</summary>
	private static readonly Lazy<SKTypeface> Face =
		new(
			static () =>
				SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
				?? SKTypeface.Default);

	/// <summary>The chip of a row; null when the row has none (a stop header) or drawing failed.</summary>
	public static WidgetChipImage? For(WidgetRow row)
	{
		if (row.Kind != WidgetRowKind.Item
			|| string.IsNullOrWhiteSpace(row.Chip))
		{
			return null;
		}

		try
		{
			return Cache.GetOrAdd((row.Chip.Trim(), row.Mode), key => Draw(key.Text, key.Mode));
		}
		catch (Exception)
		{
			// The card falls back to its text chip.
			return null;
		}
	}

	private static WidgetChipImage Draw(string text, TransitMode mode)
	{
		using var font = new SKFont(Face.Value, TextSize * Scale) { Subpixel = true };

		float textWidth = font.MeasureText(text);
		int width = Math.Clamp((int)Math.Ceiling((textWidth / Scale) + 16), MinWidth, MaxWidth);

		var info = new SKImageInfo((int)(width * Scale), (int)(Height * Scale), SKColorType.Rgba8888, SKAlphaType.Premul);

		using SKSurface surface = SKSurface.Create(info);
		SKCanvas canvas = surface.Canvas;

		canvas.Clear(SKColors.Transparent);

		bool walk = mode == TransitMode.Walk;
		SKColor fill = Fill(mode);
		SKColor ink = walk ? SKColor.Parse("#8A98A1") : SKColors.White;

		float inset = walk ? 1.5f * Scale / 2 : 0;
		var rect = new SKRect(inset, inset, info.Width - inset, info.Height - inset);

		using var shape = new SKRoundRect();
		shape.SetRectRadii(rect, Radii(mode, rect.Height / 2));

		using var paint = new SKPaint { IsAntialias = true };

		if (walk)
		{
			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = 1.5f * Scale;
			paint.Color = ink;
		}
		else
		{
			paint.Color = fill;
		}

		canvas.DrawRoundRect(shape, paint);

		SKFontMetrics metrics = font.Metrics;
		float baseline = (info.Height / 2f) - ((metrics.Ascent + metrics.Descent) / 2f);

		using var letters = new SKPaint { Color = ink, IsAntialias = true };

		canvas.DrawText(text, (info.Width - textWidth) / 2f, baseline, font, letters);

		using SKImage image = surface.Snapshot();
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

		return new WidgetChipImage($"data:image/png;base64,{Convert.ToBase64String(data.ToArray())}", width, Height);
	}

	/// <summary>The mode colours of Tokens.xaml.</summary>
	private static SKColor Fill(TransitMode mode) =>
		SKColor.Parse(
			mode switch
			{
				TransitMode.Tram => "#B3261E",
				TransitMode.Bus => "#6B3FA0",
				TransitMode.SuburbanRail => "#1F7A4D",
				TransitMode.Ferry => "#1D5FA8",
				TransitMode.CableCar => "#8A5A00",
				TransitMode.Walk => "#5B6B74",
				_ => "#4B5B66"
			});

	/// <summary>The corners of <c>ModeChips</c>: tram pill, bus square, S-Bahn and ferry leaves, the rest a block.</summary>
	private static SKPoint[] Radii(TransitMode mode, float half)
	{
		float R(float value) => Math.Min(half, value * Scale);

		// Upper left, upper right, lower right, lower left.
		(float Tl, float Tr, float Br, float Bl) = mode switch
		{
			TransitMode.Tram or TransitMode.CableCar or TransitMode.Walk => (half, half, half, half),
			TransitMode.Bus => (R(5), R(5), R(5), R(5)),
			TransitMode.SuburbanRail => (R(13), R(2), R(13), R(2)),
			TransitMode.Ferry => (R(2), R(13), R(2), R(13)),
			_ => (R(2), R(2), R(2), R(2))
		};

		return [new SKPoint(Tl, Tl), new SKPoint(Tr, Tr), new SKPoint(Br, Br), new SKPoint(Bl, Bl)];
	}
}
