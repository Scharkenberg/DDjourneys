using System.Globalization;
using System.Text;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using SkiaSharp;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// Renders a shared journey as a PNG in the visual language of the app: a header on the accent colour with
/// route and key figures, and a card with the timeline (coloured rail per ride, line pills, times with
/// real-time deviations, changes and walks), followed by notices.
/// </summary>
/// <remarks>
/// <para>
/// Drawn with SkiaSharp directly so the picture is identical on every platform and needs no view on screen.
/// It uses the font face and the colour palette currently chosen under Appearance, so it looks like the app.
/// </para>
/// <para>
/// Text is placed at an explicit baseline derived from the font's metrics (never fitted into a box, which
/// silently drops lines that are a pixel too tall), and every character the font lacks is drawn with a
/// fallback face. Symbols that are not text (warning, change) are drawn as vector icons.
/// </para>
/// <para>
/// Layout runs twice with the same code: a measuring pass computes the height, the drawing pass renders
/// into a surface of exactly that height.
/// </para>
/// </remarks>
public static class JourneyShareImage
{
	private const int Width = 1080;
	private const float Margin = 56;
	private const float CardInset = 40;
	// The picture is about 2.7 times a 400 dp phone width; corner radii follow the app's (6 dp cards).
	private const float Scale = 2.7f;
	private const float CardRadius = 6 * Scale;
	private const float RailGap = 30;
	private const float RailWidth = 8;
	private const float LineFactor = 1.32f;

	/// <summary>Renders the journey and writes it to a PNG in the cache directory; returns its path.</summary>
	/// <remarks>Call on the UI thread: the palette is captured here, the drawing runs in the background.</remarks>
	public static async Task<string> RenderToFileAsync(JourneyShareModel model, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(strings);

		SharePalette palette = SharePalette.Capture();
		ShareFonts fonts = await ShareFonts.LoadAsync(Theme.Font);

		string folder = Path.Combine(FileSystem.CacheDirectory, "share");
		Directory.CreateDirectory(folder);

		// Old pictures are useless once shared; keep the cache from growing.
		foreach (string old in Directory.EnumerateFiles(folder, "journey-*.png"))
		{
			try
			{
				File.Delete(old);
			}
			catch (IOException)
			{
			}
		}

		string path = Path.Combine(folder, $"journey-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");

		await Task.Run(
			() =>
			{
				using FileStream stream = File.Create(path);

				Render(model, strings, palette, fonts, stream);
			});

		return path;
	}

	private static void Render(JourneyShareModel model, IUiStrings strings, SharePalette palette, ShareFonts fonts, Stream output)
	{
		Geometry geometry = new Painter(null, palette, fonts).Paint(model, strings, null);

		var info = new SKImageInfo(Width, (int)Math.Ceiling(geometry.Height), SKColorType.Rgba8888, SKAlphaType.Premul);

		using SKSurface surface = SKSurface.Create(info);

		new Painter(surface.Canvas, palette, fonts).Paint(model, strings, geometry);

		using SKImage image = surface.Snapshot();
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

		data.SaveTo(output);
	}

	/// <summary>Result of the measuring pass: total height and the card's vertical extent.</summary>
	private sealed record Geometry(float Height, float CardTop, float CardBottom);

	/// <summary>The colours of the picture, taken from the palette in effect when sharing.</summary>
	private sealed record SharePalette(
		SKColor HeaderTop,
		SKColor HeaderBottom,
		SKColor OnHeader,
		SKColor Card,
		SKColor Raised,
		SKColor Outline,
		SKColor Ink,
		SKColor Muted,
		SKColor Accent,
		SKColor OnTime,
		SKColor Delay,
		SKColor Cancelled,
		IReadOnlyDictionary<TransitMode, SKColor> Modes,
		IReadOnlyDictionary<TransitMode, CornerRadius> Shapes)
	{
		public static SharePalette Capture()
		{
			Color accent = Theme.ColorOf("Accent", Color.FromArgb("#0B6E8A"));
			Color onAccent = Theme.ColorOf("OnAccent", Colors.White);

			// The gradient moves AWAY from the text colour, so the header text only gains contrast.
			Color away = onAccent.GetLuminosity() > 0.5f ? Colors.Black : Colors.White;

			var modes = new Dictionary<TransitMode, SKColor>();
			var shapes = new Dictionary<TransitMode, CornerRadius>();

			foreach (TransitMode mode in Enum.GetValues<TransitMode>())
			{
				modes[mode] = Sk(ModeColors.For(mode));
				shapes[mode] = ModeChips.For(mode).Corners;
			}

			return new SharePalette(
				Sk(accent),
				Sk(Mix(accent, away, 0.45f)),
				Sk(onAccent),
				Sk(Theme.ColorOf("Surface", Colors.White)),
				Sk(Theme.ColorOf("Raised", Color.FromArgb("#DFE6E9"))),
				Sk(Theme.ColorOf("Outline", Color.FromArgb("#B4C1C7"))),
				Sk(Theme.ColorOf("Ink", Color.FromArgb("#0F1A1F"))),
				Sk(Theme.ColorOf("InkMuted", Color.FromArgb("#4A5960"))),
				Sk(accent),
				Sk(Theme.ColorOf("OnTime", Color.FromArgb("#15803D"))),
				Sk(Theme.ColorOf("Delay", Color.FromArgb("#9A5B00"))),
				Sk(Theme.ColorOf("Cancelled", Color.FromArgb("#B91C1C"))),
				modes,
				shapes);
		}

		private static Color Mix(Color a, Color b, float t) =>
			Color.FromRgb(
				a.Red + ((b.Red - a.Red) * t),
				a.Green + ((b.Green - a.Green) * t),
				a.Blue + ((b.Blue - a.Blue) * t));

		private static SKColor Sk(Color c) =>
			new(
				(byte)Math.Round(c.Red * 255),
				(byte)Math.Round(c.Green * 255),
				(byte)Math.Round(c.Blue * 255),
				(byte)Math.Round(c.Alpha * 255));
	}

	/// <summary>One layout pass. Without a canvas it only measures.</summary>
	private sealed class Painter(SKCanvas? canvas, SharePalette p, ShareFonts fonts)
	{
		private bool Draw => canvas is not null;

		public Geometry Paint(JourneyShareModel model, IUiStrings strings, Geometry? known)
		{
			JourneyStrings text = strings.Journey;
			float inner = Width - (2 * Margin);
			float y = Margin;

			if (canvas is not null && known is not null)
			{
				Header(known.Height);

				using var card = new SKPaint { Color = p.Card, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(Margin, known.CardTop, Width - Margin, known.CardBottom), CardRadius, CardRadius, card);
			}

			// ----- Header -----

			SKColor soft = p.OnHeader.WithAlpha(190);

			y += Text("DDJOURNEYS", Margin, y, inner, 22, fonts.Semibold, soft, 1, spacing: 3) + 14;
			y += Text($"{model.Origin} → {model.Destination}", Margin, y, inner, 44, fonts.Semibold, p.OnHeader, 3) + 8;

			if (model.Day.Length > 0)
			{
				y += Text(model.Day, Margin, y, inner, 28, fonts.Regular, soft, 1) + 12;
			}

			y += Text(
				$"{Format.TimeOrDash(model.Departure)} – {Format.TimeOrDash(model.Arrival)}",
				Margin, y, inner, 64, fonts.Semibold, p.OnHeader, 1) + 18;

			float x = Margin;
			SKColor chip = p.OnHeader.WithAlpha(40);

			foreach (string label in new[] { model.DurationText, model.TransfersText })
			{
				if (label.Length > 0)
				{
					x += Pill(label, x, y, 26, chip, p.OnHeader, fonts.Semibold) + 12;
				}
			}

			if (model.IsCancelled)
			{
				Pill(text.NotPossible, x, y, 26, p.Cancelled, SKColors.White, fonts.Semibold);
			}

			y += PillHeight(26);

			// Why it cannot take place, right under the figures.
			if (model.IsCancelled && model.BlockReason.Length > 0)
			{
				y += 14 + Text(model.BlockReason, Margin, y + 14, inner, 28, fonts.Semibold, p.OnHeader, 2);
			}

			y += 36;

			float cardTop = y;

			// ----- Timeline card -----

			y += CardInset;

			float left = Margin + CardInset;
			float timeColumn = TimeColumn(model);
			float rail = left + timeColumn + RailGap;
			float content = rail + RailGap;
			float contentWidth = Width - Margin - CardInset - content;

			foreach (ShareStep step in model.Steps)
			{
				y = step.Kind switch
				{
					ShareStepKind.Ride => Ride(step, text, left, timeColumn, rail, content, contentWidth, y),
					ShareStepKind.Walk => Walk(step, text, rail, content, contentWidth, y),
					ShareStepKind.Change => Change(step, text, content, contentWidth, y),
					_ => Arrive(step, text, left, timeColumn, rail, content, contentWidth, y)
				};
			}

			// ----- Notices -----

			if (model.Notices.Count > 0)
			{
				y += 8;

				foreach (string notice in model.Notices.Take(3))
				{
					y = Notice(notice, left, Width - Margin - CardInset - left, y) + 12;
				}

				y -= 12;
			}

			y += CardInset;

			float cardBottom = y;

			// ----- Footer -----

			y += 24;

			string stamp =
				string.Format(
					CultureInfo.CurrentCulture,
					"DDjourneys · {0}",
					Format.ToWall(DateTimeOffset.UtcNow).ToString("g", CultureInfo.CurrentCulture));

			y += Text(stamp, Margin, y, inner, 22, fonts.Regular, soft, 1, align: SKTextAlign.Center);
			y += Margin - 12;

			return new Geometry(y, cardTop, cardBottom);
		}

		// ----- Blocks -----

		private float Ride(
			ShareStep step,
			JourneyStrings text,
			float left,
			float timeColumn,
			float rail,
			float content,
			float contentWidth,
			float y)
		{
			SKColor line = step.IsCancelled ? p.Cancelled : p.Modes.GetValueOrDefault(step.Mode, p.Accent);
			float top = y;
			float rowHeight = PillHeight(26);

			TimeCell(step.Time, step.LiveTime, left, timeColumn, y, rowHeight);

			float pillWidth = Pill(step.Line, content, y, 26, line, SKColors.White, fonts.Semibold, p.Shapes.GetValueOrDefault(step.Mode));

			if (step.Direction is { Length: > 0 } toward)
			{
				float dx = pillWidth + 14;
				float h = Text($"→ {toward}", content + dx, y + ((rowHeight - LineHeight(28)) / 2), contentWidth - dx, 28, fonts.Semibold, p.Ink, 2);
				rowHeight = Math.Max(rowHeight, h + ((rowHeight - LineHeight(28)) / 2));
			}

			y += rowHeight + 6;
			y += Text($"{step.From}{Suffix(step.FromPlatform)}", content, y, contentWidth, 26, fonts.Regular, p.Muted, 2);

			if (step.IsCancelled)
			{
				y += 4 + Text(text.Cancelled, content, y + 4, contentWidth, 26, fonts.Semibold, p.Cancelled, 1);
			}

			string stops =
				step.IntermediateStops switch
				{
					0 => string.Empty,
					1 => text.OneStop,
					int count => string.Format(CultureInfo.CurrentCulture, text.MultipleStops, count)
				};

			string ride = Format.Duration(step.LiveTime ?? step.Time, step.EndLiveTime ?? step.EndTime);
			string summary = stops.Length > 0 && ride.Length > 0 ? $"{stops} · {ride}" : stops + ride;

			if (summary.Length > 0)
			{
				y += 14;
				y += Text(summary, content, y, contentWidth, 24, fonts.Regular, p.Muted, 1);
			}

			y += 20;

			// Arrival row
			float arrival = y;
			float arrivalHeight = Text($"{step.To}{Suffix(step.ToPlatform)}", content, y, contentWidth, 28, fonts.Semibold, p.Ink, 2);

			TimeCell(step.EndTime, step.EndLiveTime, left, timeColumn, y, LineHeight(28));

			if (canvas is not null)
			{
				float from = top + (PillHeight(26) / 2);
				float to = arrival + (LineHeight(28) / 2);

				using var paint = new SKPaint { Color = line, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(rail - (RailWidth / 2), from, rail + (RailWidth / 2), to), RailWidth / 2, RailWidth / 2, paint);

				Node(rail, from, line);
				Node(rail, to, line);
			}

			return y + Math.Max(arrivalHeight, TimeCellHeight(step.EndLiveTime is not null && step.EndTime is not null)) + 28;
		}

		private float Walk(ShareStep step, JourneyStrings text, float rail, float content, float contentWidth, float y)
		{
			string duration = step.Duration is { } walk && walk > TimeSpan.Zero ? Format.Duration(walk) : string.Empty;
			string label = string.Join(' ', new[] { text.Walk, duration, text.To, step.To }.Where(part => part.Length > 0));

			float height = Math.Max(40, Text(label, content, y + 4, contentWidth, 24, fonts.Regular, p.Muted, 2) + 8);

			if (canvas is not null)
			{
				// Dotted rail: walking is not a vehicle.
				using var dots = new SKPaint { Color = p.Muted.WithAlpha(150), IsAntialias = true };

				for (float dot = y + 6; dot < y + height - 2; dot += 13)
				{
					canvas.DrawCircle(rail, dot, 3.2f, dots);
				}
			}

			return y + height + 20;
		}

		private float Change(ShareStep step, JourneyStrings text, float content, float contentWidth, float y)
		{
			string wait =
				step.Duration is { } left && left > TimeSpan.Zero
					? $"{Format.Duration(left)} {text.ToChange}"
					: text.ImmediateChange;

			SKColor color = step.IsEndangered ? p.Cancelled : p.Muted;
			const float icon = 26;

			float height = Text($"{text.ChangeAt} {step.From} · {wait}", content + icon + 10, y, contentWidth - icon - 10, 24, fonts.Semibold, color, 2);

			if (canvas is not null)
			{
				ChangeIcon(content, y + ((LineHeight(24) - icon) / 2), icon, color);
			}

			if (step.IsEndangered)
			{
				height += 4 + Text(text.ConnectionMayBeMissed, content + icon + 10, y + height + 4, contentWidth - icon - 10, 22, fonts.Regular, p.Cancelled, 2);
			}

			if (canvas is not null)
			{
				using var hairline = new SKPaint { Color = p.Outline.WithAlpha(120), StrokeWidth = 2, IsAntialias = true };
				canvas.DrawLine(content, y + height + 16, content + contentWidth, y + height + 16, hairline);
			}

			return y + height + 36;
		}

		private float Arrive(
			ShareStep step,
			JourneyStrings text,
			float left,
			float timeColumn,
			float rail,
			float content,
			float contentWidth,
			float y)
		{
			float height = Text($"{text.Arrive} {step.To}", content, y, contentWidth, 30, fonts.Semibold, p.Ink, 2);

			TimeCell(step.Time, null, left, timeColumn, y, LineHeight(30));

			if (canvas is not null)
			{
				float cy = y + (LineHeight(30) / 2);

				using var ring = new SKPaint { Color = p.Accent, IsAntialias = true };
				using var hole = new SKPaint { Color = p.Card, IsAntialias = true };

				canvas.DrawCircle(rail, cy, 14, ring);
				canvas.DrawCircle(rail, cy, 5, hole);
			}

			return y + height + 8;
		}

		private float Notice(string notice, float x, float width, float y)
		{
			const float pad = 22;
			const float icon = 26;

			float textWidth = width - (2 * pad) - icon - 12;
			float height = Text(notice, x + pad + icon + 12, y + pad, textWidth, 24, fonts.Regular, p.Ink, 6, measureOnly: true);

			if (canvas is not null)
			{
				using var fill = new SKPaint { Color = p.Raised, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height + (2 * pad)), 4 * Scale, 4 * Scale, fill);

				WarningIcon(x + pad, y + pad + ((LineHeight(24) - icon) / 2), icon, p.Delay);

				Text(notice, x + pad + icon + 12, y + pad, textWidth, 24, fonts.Regular, p.Ink, 6);
			}

			return y + height + (2 * pad);
		}

		// ----- Primitives -----

		private void Header(float height)
		{
			using var shader =
				SKShader.CreateLinearGradient(
					new SKPoint(0, 0),
					new SKPoint(Width * 0.35f, height),
					[p.HeaderTop, p.HeaderBottom],
					SKShaderTileMode.Clamp);

			using var paint = new SKPaint { Shader = shader, IsAntialias = true };

			canvas!.DrawRect(new SKRect(0, 0, Width, height), paint);
		}

		/// <summary>The time column fits the widest time shown, so the rail sits close to the times.</summary>
		private float TimeColumn(JourneyShareModel model)
		{
			float widest = Measure("00:00", fonts.Semibold, 30);

			foreach (ShareStep step in model.Steps)
			{
				foreach (DateTimeOffset? time in new[] { step.Time, step.EndTime })
				{
					widest = Math.Max(widest, Measure(Format.TimeOrDash(time), fonts.Semibold, 30));
				}

				foreach (DateTimeOffset? time in new[] { step.LiveTime, step.EndLiveTime })
				{
					if (time is not null)
					{
						widest = Math.Max(widest, Measure(Format.TimeOrDash(time), fonts.Semibold, 24));
					}
				}
			}

			return widest + 4;
		}

		private void TimeCell(DateTimeOffset? planned, DateTimeOffset? live, float left, float width, float y, float rowHeight)
		{
			float top = y + ((rowHeight - LineHeight(30)) / 2);

			Text(Format.TimeOrDash(planned), left, top, width, 30, fonts.Semibold, p.Ink, 1, align: SKTextAlign.Right);

			if (live is { } actual && planned is { } plan)
			{
				SKColor color = actual > plan ? p.Delay : p.OnTime;

				Text(Format.TimeOrDash(actual), left, top + LineHeight(30), width, 24, fonts.Semibold, color, 1, align: SKTextAlign.Right);
			}
		}

		private static float TimeCellHeight(bool withLive) =>
			LineHeight(30) + (withLive ? LineHeight(24) : 0);

		private void Node(float x, float y, SKColor color)
		{
			using var fill = new SKPaint { Color = p.Card, IsAntialias = true };
			using var ring = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5 };

			canvas!.DrawCircle(x, y, 11, fill);
			canvas.DrawCircle(x, y, 9.5f, ring);
		}

		private void ChangeIcon(float x, float y, float size, SKColor color)
		{
			using var paint =
				new SKPaint
				{
					Color = color,
					IsAntialias = true,
					Style = SKPaintStyle.Stroke,
					StrokeWidth = size * 0.11f,
					StrokeCap = SKStrokeCap.Round,
					StrokeJoin = SKStrokeJoin.Round
				};

			float s = size;
			using var path = new SKPath();

			// Upper arrow to the right, lower arrow to the left.
			path.MoveTo(x + (s * 0.12f), y + (s * 0.32f));
			path.LineTo(x + (s * 0.86f), y + (s * 0.32f));
			path.MoveTo(x + (s * 0.66f), y + (s * 0.12f));
			path.LineTo(x + (s * 0.88f), y + (s * 0.32f));
			path.LineTo(x + (s * 0.66f), y + (s * 0.52f));

			path.MoveTo(x + (s * 0.88f), y + (s * 0.70f));
			path.LineTo(x + (s * 0.14f), y + (s * 0.70f));
			path.MoveTo(x + (s * 0.34f), y + (s * 0.50f));
			path.LineTo(x + (s * 0.12f), y + (s * 0.70f));
			path.LineTo(x + (s * 0.34f), y + (s * 0.90f));

			canvas!.DrawPath(path, paint);
		}

		private void WarningIcon(float x, float y, float size, SKColor color)
		{
			using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
			using var mark = new SKPaint { Color = p.Raised, IsAntialias = true, StrokeWidth = size * 0.11f, StrokeCap = SKStrokeCap.Round };
			using var triangle = new SKPath();

			float s = size;

			triangle.MoveTo(x + (s * 0.5f), y + (s * 0.06f));
			triangle.LineTo(x + (s * 0.97f), y + (s * 0.90f));
			triangle.LineTo(x + (s * 0.03f), y + (s * 0.90f));
			triangle.Close();

			canvas!.DrawPath(triangle, fill);
			canvas.DrawLine(x + (s * 0.5f), y + (s * 0.36f), x + (s * 0.5f), y + (s * 0.60f), mark);
			using var dot = new SKPaint { Color = p.Raised, IsAntialias = true };
			canvas.DrawCircle(x + (s * 0.5f), y + (s * 0.75f), s * 0.06f, dot);
		}

		/// <summary>
		/// A label on a filled shape; returns its width. Without <paramref name="corners"/> it is a full pill,
		/// otherwise it takes the mode's shape from the app (pill, rounded square, leaf, ...).
		/// </summary>
		private float Pill(string label, float x, float y, float size, SKColor fill, SKColor ink, SKTypeface face, CornerRadius? corners = null)
		{
			float padding = size * 0.65f;
			float height = PillHeight(size);
			float width = Math.Max(height, Measure(label, face, size) + (2 * padding));

			if (canvas is not null)
			{
				using var paint = new SKPaint { Color = fill, IsAntialias = true };

				var rect = new SKRect(x, y, x + width, y + height);
				float half = height / 2;

				if (corners is { } c)
				{
					float R(double value) => Math.Min(half, (float)value * Scale);

					using var shape = new SKRoundRect();
					shape.SetRectRadii(
						rect,
						[
							new SKPoint(R(c.TopLeft), R(c.TopLeft)),
							new SKPoint(R(c.TopRight), R(c.TopRight)),
							new SKPoint(R(c.BottomRight), R(c.BottomRight)),
							new SKPoint(R(c.BottomLeft), R(c.BottomLeft))
						]);
					canvas.DrawRoundRect(shape, paint);
				}
				else
				{
					canvas.DrawRoundRect(rect, half, half, paint);
				}

				float labelWidth = Measure(label, face, size);
				x += (width - labelWidth) / 2 - padding;

				DrawLine(label, x + padding, y + ((height - LineHeight(size)) / 2), size, face, ink, 0);
			}

			return width;
		}

		private static float PillHeight(float size) => size * 1.75f;

		private static float LineHeight(float size) => size * LineFactor;

		/// <summary>Wrapped text with at most <paramref name="maxLines"/> lines; returns its height.</summary>
		private float Text(
			string value,
			float x,
			float y,
			float width,
			float size,
			SKTypeface face,
			SKColor color,
			int maxLines,
			SKTextAlign align = SKTextAlign.Left,
			float spacing = 0,
			bool measureOnly = false)
		{
			List<string> lines = Wrap(value, face, size, width, maxLines, spacing);

			if (Draw && !measureOnly)
			{
				for (int i = 0; i < lines.Count; i++)
				{
					float lineWidth = Measure(lines[i], face, size, spacing);

					float start =
						align switch
						{
							SKTextAlign.Right => x + width - lineWidth,
							SKTextAlign.Center => x + ((width - lineWidth) / 2),
							_ => x
						};

					DrawLine(lines[i], start, y + (i * LineHeight(size)), size, face, color, spacing);
				}
			}

			return lines.Count * LineHeight(size);
		}

		/// <summary>One line at an explicit baseline, split into runs of faces that have the glyphs.</summary>
		private void DrawLine(string line, float x, float top, float size, SKTypeface face, SKColor color, float spacing)
		{
			using var probe = new SKFont(face, size);
			SKFontMetrics metrics = probe.Metrics;

			// Centre the font's own ascent+descent inside the line box.
			float glyphHeight = metrics.Descent - metrics.Ascent;
			float baseline = top + ((LineHeight(size) - glyphHeight) / 2) - metrics.Ascent;

			using var paint = new SKPaint { Color = color, IsAntialias = true };

			foreach ((string run, SKTypeface runFace) in Runs(line, face))
			{
				using var font = new SKFont(runFace, size) { Subpixel = true };

				if (spacing <= 0)
				{
					canvas!.DrawText(run, x, baseline, font, paint);
					x += font.MeasureText(run);
				}
				else
				{
					foreach (Rune rune in run.EnumerateRunes())
					{
						string glyph = rune.ToString();
						canvas!.DrawText(glyph, x, baseline, font, paint);
						x += font.MeasureText(glyph) + spacing;
					}
				}
			}
		}

		private float Measure(string value, SKTypeface face, float size, float spacing = 0)
		{
			float width = 0;

			foreach ((string run, SKTypeface runFace) in Runs(value, face))
			{
				using var font = new SKFont(runFace, size);
				width += font.MeasureText(run);
			}

			return spacing > 0 ? width + (spacing * value.EnumerateRunes().Count()) : width;
		}

		private IEnumerable<(string Run, SKTypeface Face)> Runs(string value, SKTypeface preferred)
		{
			var builder = new StringBuilder();
			SKTypeface? current = null;

			foreach (Rune rune in value.EnumerateRunes())
			{
				SKTypeface face = fonts.For(preferred, rune.Value);

				if (current is not null && !ReferenceEquals(face, current))
				{
					yield return (builder.ToString(), current);
					builder.Clear();
				}

				current = face;
				builder.Append(rune.ToString());
			}

			if (current is not null && builder.Length > 0)
			{
				yield return (builder.ToString(), current);
			}
		}

		/// <summary>Greedy word wrap with an ellipsis on the last line when the text is longer.</summary>
		private List<string> Wrap(string value, SKTypeface face, float size, float width, int maxLines, float spacing)
		{
			var lines = new List<string>();
			string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string current = string.Empty;

			for (int i = 0; i < words.Length; i++)
			{
				string candidate = current.Length == 0 ? words[i] : $"{current} {words[i]}";

				if (current.Length > 0 && Measure(candidate, face, size, spacing) > width)
				{
					lines.Add(current);
					current = words[i];

					if (lines.Count == maxLines)
					{
						// No room left: shorten the last line and mark the cut.
						lines[^1] = Ellipsize($"{lines[^1]} {string.Join(' ', words.Skip(i))}", face, size, width, spacing);
						return lines;
					}
				}
				else
				{
					current = candidate;
				}
			}

			if (current.Length > 0)
			{
				lines.Add(
					Measure(current, face, size, spacing) > width
						? Ellipsize(current, face, size, width, spacing)
						: current);
			}

			return lines.Count == 0 ? [string.Empty] : lines;
		}

		private string Ellipsize(string value, SKTypeface face, float size, float width, float spacing)
		{
			string text = value;

			while (text.Length > 1 && Measure($"{text}…", face, size, spacing) > width)
			{
				text = text[..^1];
			}

			return $"{text.TrimEnd()}…";
		}

		private static string Suffix(string? platform) =>
			string.IsNullOrWhiteSpace(platform) ? string.Empty : $" · {platform}";
	}
}
