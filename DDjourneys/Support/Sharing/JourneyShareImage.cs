using System.Globalization;
using System.Text;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using SkiaSharp;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// Renders a shared journey as a PNG in the visual language of the app: a slim route line with the key figures,
/// then a card with the timeline (start, coloured rail per ride, line pills, times with real-time deviations,
/// changes and walks, destination), followed by notices. The surroundings are kept small and quiet.
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
	private const float Margin = 36;
	private const float CardInset = 34;

	// Type scale: the content (times, stops, lines) is large; the surroundings are small and quiet.
	private const float SzTitle = 38;
	private const float SzBody = 36;
	private const float SzSub = 28;
	private const float SzSmall = 25;
	private const float SzLive = 27;
	private const float SzPill = 30;
	// The picture is about 2.7 times a 400 dp phone width; corner radii follow the app's (6 dp cards).
	private const float Scale = 2.7f;
	private const float CardRadius = 6 * Scale;
	private const float RailGap = 28;
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
		SKColor Page,
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

			var modes = new Dictionary<TransitMode, SKColor>();
			var shapes = new Dictionary<TransitMode, CornerRadius>();

			foreach (TransitMode mode in Enum.GetValues<TransitMode>())
			{
				modes[mode] = Sk(ModeColors.For(mode));
				shapes[mode] = ModeChips.For(mode).Corners;
			}

			return new SharePalette(
				Sk(Theme.ColorOf("Bg", Color.FromArgb("#F2F4F5"))),
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
				canvas.Clear(p.Page);

				using var card = new SKPaint { Color = p.Card, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(Margin, known.CardTop, Width - Margin, known.CardBottom), CardRadius, CardRadius, card);
			}

			// ----- Route and figures: one slim block, no banner -----

			y += Text($"{model.Origin} → {model.Destination}", Margin, y, inner, SzTitle, fonts.Semibold, p.Ink, 2) + 6;

			var figures = new List<string>(4);

			if (model.Day.Length > 0)
			{
				figures.Add(model.Day);
			}

			figures.Add($"{Format.TimeOrDash(model.Departure)}–{Format.TimeOrDash(model.Arrival)}");
			figures.Add(model.DurationText);
			figures.Add(model.TransfersText);

			if (model.PriceText is { Length: > 0 } price)
			{
				figures.Add(price);
			}

			y += Text(string.Join(" · ", figures.Where(figure => figure.Length > 0)), Margin, y, inner, SzSub, fonts.Regular, p.Muted, 2);

			if (model.IsCancelled)
			{
				y += 12;
				float width = Pill(text.NotPossible, Margin, y, SzSmall, p.Cancelled, SKColors.White, fonts.Semibold);
				float height = PillHeight(SzSmall);

				if (model.BlockReason.Length > 0)
				{
					float dx = width + 14;
					Text(model.BlockReason, Margin + dx, y + ((height - LineHeight(SzSub)) / 2), inner - dx, SzSub, fonts.Semibold, p.Cancelled, 2);
				}

				y += height;
			}

			y += 24;

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
					ShareStepKind.Depart => Endpoint(step, text.Depart, left, timeColumn, rail, content, contentWidth, y),
					ShareStepKind.Ride => Ride(step, text, left, timeColumn, rail, content, contentWidth, y),
					ShareStepKind.Walk => Walk(step, text, rail, content, contentWidth, y),
					ShareStepKind.Change => Change(step, text, content, contentWidth, y),
					_ => Endpoint(step, text.Arrive, left, timeColumn, rail, content, contentWidth, y)
				};
			}

			y += CardInset - 6;

			float cardBottom = y;

			// ----- Signature: small and quiet -----

			y += 14;
			y += Text("DDjourneys", Margin, y, inner, 20, fonts.Regular, p.Muted.WithAlpha(150), 1, align: SKTextAlign.Right);
			y += 22;

			return new Geometry(y, cardTop, cardBottom);
		}

		// ----- Blocks -----

		/// <summary>A stop name in the content colour, its platform small underneath; returns the height.</summary>
		private float StopBlock(string name, string? platform, float x, float y, float width)
		{
			float height = Text(name, x, y, width, SzBody, fonts.Semibold, p.Ink, 2);

			if (!string.IsNullOrWhiteSpace(platform))
			{
				height += Text(platform, x, y + height, width, SzSmall, fonts.Regular, p.Muted, 1);
			}

			return height;
		}

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

			// Boarding: time, node and stop on one line.
			float boardHeight = StopBlock(step.From, step.FromPlatform, content, y, contentWidth);

			TimeCell(step.Time, step.LiveTime, left, timeColumn, y);

			y += Math.Max(boardHeight, TimeCellHeight(step.LiveTime is not null && step.Time is not null)) + 12;

			// The ride itself: line pill, direction, then stops and duration.
			float pillHeight = PillHeight(SzPill);
			float pillWidth = Pill(step.Line, content, y, SzPill, line, SKColors.White, fonts.Semibold, p.Shapes.GetValueOrDefault(step.Mode));
			float rowHeight = pillHeight;

			if (step.Direction is { Length: > 0 } toward)
			{
				float dx = pillWidth + 14;
				float inset = (pillHeight - LineHeight(SzSub)) / 2;
				float h = Text($"→ {toward}", content + dx, y + inset, contentWidth - dx, SzSub, fonts.Regular, p.Ink, 2);

				rowHeight = Math.Max(rowHeight, h + inset);
			}

			y += rowHeight;

			if (step.IsCancelled)
			{
				y += 4 + Text(text.Cancelled, content, y + 4, contentWidth, SzSmall, fonts.Semibold, p.Cancelled, 1);
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
				y += 6;
				y += Text(summary, content, y, contentWidth, SzSmall, fonts.Regular, p.Muted, 1);
			}

			y += 14;

			// Alighting: time, node and stop on one line.
			float arrival = y;
			float arrivalHeight = StopBlock(step.To, step.ToPlatform, content, y, contentWidth);

			TimeCell(step.EndTime, step.EndLiveTime, left, timeColumn, y);

			if (canvas is not null)
			{
				float from = top + (LineHeight(SzBody) / 2);
				float to = arrival + (LineHeight(SzBody) / 2);

				using var paint = new SKPaint { Color = line, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(rail - (RailWidth / 2), from, rail + (RailWidth / 2), to), RailWidth / 2, RailWidth / 2, paint);

				Node(rail, from, line);
				Node(rail, to, line);
			}

			return y + Math.Max(arrivalHeight, TimeCellHeight(step.EndLiveTime is not null && step.EndTime is not null)) + 26;
		}

		private float Walk(ShareStep step, JourneyStrings text, float rail, float content, float contentWidth, float y)
		{
			string duration = step.Duration is { } walk && walk > TimeSpan.Zero ? Format.Duration(walk) : string.Empty;
			string label = string.Join(' ', new[] { text.Walk, duration, text.To, step.To }.Where(part => part.Length > 0));

			if (step.WaitTime is { } left && left >= TimeSpan.FromMinutes(1))
			{
				label += $" · {Format.Duration(left)} {text.ToChange}";
			}

			SKColor color = step.IsEndangered ? p.Cancelled : p.Muted;
			float height = Text(label, content, y + 4, contentWidth, SzSub, fonts.Regular, color, 3) + 8;

			if (step.IsEndangered)
			{
				height += Text(text.ConnectionMayBeMissed, content, y + height, contentWidth, SzSmall, fonts.Semibold, p.Cancelled, 2);
			}
			else if (step.IsGuaranteed)
			{
				height += Text(text.ConnectionGuaranteed, content, y + height, contentWidth, SzSmall, fonts.Semibold, p.OnTime, 2);
			}

			height = Math.Max(44, height);

			if (canvas is not null)
			{
				// Dotted rail: walking is not a vehicle.
				using var dots = new SKPaint { Color = p.Muted.WithAlpha(150), IsAntialias = true };

				for (float dot = y + 6; dot < y + height - 2; dot += 13)
				{
					canvas.DrawCircle(rail, dot, 3.2f, dots);
				}
			}

			return y + height + 16;
		}

		private float Change(ShareStep step, JourneyStrings text, float content, float contentWidth, float y)
		{
			string wait =
				step.Duration is { } left && left > TimeSpan.Zero
					? $"{Format.Duration(left)} {text.ToChange}"
					: text.ImmediateChange;

			SKColor color = step.IsEndangered ? p.Cancelled : p.Muted;
			const float icon = 28;

			float height = Text($"{text.ChangeAt} {step.From} · {wait}", content + icon + 10, y, contentWidth - icon - 10, SzSub, fonts.Semibold, color, 2);

			if (canvas is not null)
			{
				ChangeIcon(content, y + ((LineHeight(SzSub) - icon) / 2), icon, color);
			}

			if (step.IsEndangered)
			{
				height += 4 + Text(text.ConnectionMayBeMissed, content + icon + 10, y + height + 4, contentWidth - icon - 10, SzSmall, fonts.Regular, p.Cancelled, 2);
			}
			else if (step.IsGuaranteed)
			{
				height += 4 + Text(text.ConnectionGuaranteed, content + icon + 10, y + height + 4, contentWidth - icon - 10, SzSmall, fonts.Semibold, p.OnTime, 2);
			}

			if (canvas is not null)
			{
				using var hairline = new SKPaint { Color = p.Outline.WithAlpha(120), StrokeWidth = 2, IsAntialias = true };
				canvas.DrawLine(content, y + height + 14, content + contentWidth, y + height + 14, hairline);
			}

			return y + height + 32;
		}

		/// <summary>The starting point or the destination: a ring on the rail, the place in the content colour.</summary>
		private float Endpoint(
			ShareStep step,
			string caption,
			float left,
			float timeColumn,
			float rail,
			float content,
			float contentWidth,
			float y)
		{
			float height = Text($"{caption} {step.To}", content, y, contentWidth, SzBody, fonts.Semibold, p.Ink, 3);

			TimeCell(step.Time, null, left, timeColumn, y);

			if (canvas is not null)
			{
				float cy = y + (LineHeight(SzBody) / 2);

				using var ring = new SKPaint { Color = p.Accent, IsAntialias = true };
				using var hole = new SKPaint { Color = p.Card, IsAntialias = true };

				canvas.DrawCircle(rail, cy, 15, ring);
				canvas.DrawCircle(rail, cy, 5.5f, hole);
			}

			return y + Math.Max(height, LineHeight(SzBody)) + 14;
		}

		// ----- Primitives -----

		/// <summary>The time column fits the widest time shown, so the rail sits close to the times.</summary>
		private float TimeColumn(JourneyShareModel model)
		{
			float widest = Measure("00:00", fonts.Semibold, SzBody);

			foreach (ShareStep step in model.Steps)
			{
				foreach (DateTimeOffset? time in new[] { step.Time, step.EndTime })
				{
					widest = Math.Max(widest, Measure(Format.TimeOrDash(time), fonts.Semibold, SzBody));
				}

				foreach (DateTimeOffset? time in new[] { step.LiveTime, step.EndLiveTime })
				{
					if (time is not null)
					{
						widest = Math.Max(widest, Measure(Format.TimeOrDash(time), fonts.Semibold, SzLive));
					}
				}
			}

			return widest + 4;
		}

		/// <summary>The time on the same line as its stop; a real-time deviation sits right under it.</summary>
		private void TimeCell(DateTimeOffset? planned, DateTimeOffset? live, float left, float width, float y)
		{
			Text(Format.TimeOrDash(planned), left, y, width, SzBody, fonts.Semibold, p.Ink, 1, align: SKTextAlign.Right);

			if (live is { } actual && planned is { } plan)
			{
				SKColor color = actual > plan ? p.Delay : p.OnTime;

				Text(Format.TimeOrDash(actual), left, y + LineHeight(SzBody), width, SzLive, fonts.Semibold, color, 1, align: SKTextAlign.Right);
			}
		}

		private static float TimeCellHeight(bool withLive) =>
			LineHeight(SzBody) + (withLive ? LineHeight(SzLive) : 0);

		private void Node(float x, float y, SKColor color)
		{
			using var fill = new SKPaint { Color = p.Card, IsAntialias = true };
			using var ring = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5 };

			canvas!.DrawCircle(x, y, 12, fill);
			canvas.DrawCircle(x, y, 10.5f, ring);
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