using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using Microsoft.Maui.Graphics.Skia;
using Font = Microsoft.Maui.Graphics.Font;
using HorizontalAlignment = Microsoft.Maui.Graphics.HorizontalAlignment;
using IFont = Microsoft.Maui.Graphics.IFont;
using Point = Microsoft.Maui.Graphics.Point;
using VerticalAlignment = Microsoft.Maui.Graphics.VerticalAlignment;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// Renders a shared journey as a PNG in the visual language of the app: a header with route and
/// key figures on the accent gradient, and a white card with the timeline (coloured rail per ride,
/// line pills, times with real-time deviations, changes and walks), followed by notices.
/// </summary>
/// <remarks>
/// <para>
/// Drawn with .NET MAUI Graphics on SkiaSharp (<see cref="PlatformBitmapExportService"/>), so the
/// result is identical on every platform and needs no view on screen. Colours are fixed rather
/// than taken from the active theme: a shared picture should look the same for every recipient.
/// </para>
/// <para>
/// Layout runs twice: a measuring pass (on a tiny bitmap) computes the height, the drawing pass
/// renders into a bitmap of exactly that height. Both passes run the same code.
/// </para>
/// </remarks>
public static class JourneyShareImage
{
	private const int Width = 1080;
	private const float Margin = 64;
	private const float CardInset = 44;
	private const float TimeColumn = 132;
	private const float RailGap = 34;
	private const float RailWidth = 10;

	private static readonly Color GradientTop = Color.FromArgb("#0B6E8A");
	private static readonly Color GradientBottom = Color.FromArgb("#052C3A");
	private static readonly Color CardColor = Colors.White;
	private static readonly Color Ink = Color.FromArgb("#0F1A1F");
	private static readonly Color Muted = Color.FromArgb("#4A5960");
	private static readonly Color Hairline = Color.FromArgb("#DFE6E9");
	private static readonly Color Late = Color.FromArgb("#B45309");
	private static readonly Color OnTime = Color.FromArgb("#15803D");
	private static readonly Color Cancelled = Color.FromArgb("#B91C1C");
	private static readonly Color NoticeFill = Color.FromArgb("#FFF4E0");

	private static readonly IFont Regular = Font.Default;
	private static readonly IFont Bold = Font.DefaultBold;

	/// <summary>Renders the journey and writes it to a PNG in the cache directory; returns its path.</summary>
	public static async Task<string> RenderToFileAsync(JourneyShareModel model, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(strings);

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

		string path =
			Path.Combine(
				folder,
				$"journey-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");

		await Task.Run(
			() =>
			{
				using FileStream stream = File.Create(path);

				Render(model, strings, stream);
			});

		return path;
	}

	/// <summary>Renders the journey as PNG into <paramref name="output"/>.</summary>
	public static void Render(JourneyShareModel model, IUiStrings strings, Stream output)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(strings);
		ArgumentNullException.ThrowIfNull(output);

		var service = new PlatformBitmapExportService();

		Geometry geometry;

		using (BitmapExportContext measuring = service.CreateContext(Width, 64, 1f))
		{
			geometry = new Painter(measuring.Canvas, draw: false).Paint(model, strings, null);
		}

		using BitmapExportContext context = service.CreateContext(Width, (int)Math.Ceiling(geometry.Height), 1f);

		new Painter(context.Canvas, draw: true).Paint(model, strings, geometry);

		context.WriteToStream(output);
	}

	/// <summary>Result of the measuring pass: total height and the card's vertical extent.</summary>
	private sealed record Geometry(float Height, float CardTop, float CardBottom);

	/// <summary>One layout and drawing pass. With <c>draw</c> false it only measures.</summary>
	private sealed class Painter(ICanvas canvas, bool draw)
	{
		private const float CardRadius = 40;

		/// <param name="known">The measured geometry; required for drawing (backgrounds come first).</param>
		public Geometry Paint(JourneyShareModel model, IUiStrings strings, Geometry? known)
		{
			JourneyStrings text = strings.Journey;
			float y = Margin;

			if (draw && known is not null)
			{
				Background(0, known.Height);

				canvas.SaveState();
				canvas.FillColor = CardColor;
				canvas.FillRoundedRectangle(Margin, known.CardTop, Width - 2 * Margin, known.CardBottom - known.CardTop, CardRadius);
				canvas.RestoreState();
			}

			// ----- Header on the gradient -----

			y += Text("DDJOURNEYS", Margin, y, Width - 2 * Margin, 26, Bold, Colors.White.WithAlpha(0.7f), 1) + 18;

			y += Text($"{model.Origin} → {model.Destination}", Margin, y, Width - 2 * Margin, 52, Bold, Colors.White, 3) + 14;

			if (model.Day.Length > 0)
			{
				y += Text(model.Day, Margin, y, Width - 2 * Margin, 32, Regular, Colors.White.WithAlpha(0.85f), 1) + 22;
			}

			y += Text(
				$"{Format.TimeOrDash(model.Departure)}  →  {Format.TimeOrDash(model.Arrival)}",
				Margin, y, Width - 2 * Margin, 76, Bold, Colors.White, 1) + 22;

			float x = Margin;

			foreach (string chip in new[] { model.DurationText, model.TransfersText })
			{
				x += Pill(chip, x, y, 30, Colors.White.WithAlpha(0.16f), Colors.White, Bold) + 14;
			}

			if (model.IsCancelled)
			{
				Pill(text.Cancelled, x, y, 30, Cancelled, Colors.White, Bold);
			}

			y += PillHeight(30) + 40;

			float cardTop = y;

			// ----- Timeline card -----

			y += CardInset;

			float left = Margin + CardInset;
			float rail = left + TimeColumn + RailGap;
			float content = rail + RailGap;
			float contentWidth = Width - Margin - CardInset - content;

			foreach (ShareStep step in model.Steps)
			{
				y = step.Kind switch
				{
					ShareStepKind.Ride => Ride(step, text, left, rail, content, contentWidth, y),
					ShareStepKind.Walk => Walk(step, text, rail, content, contentWidth, y),
					ShareStepKind.Change => Change(step, text, content, contentWidth, y),
					_ => Arrive(step, text, left, rail, content, contentWidth, y)
				};
			}

			// ----- Notices -----

			if (model.Notices.Count > 0)
			{
				y += 16;

				foreach (string notice in model.Notices.Take(3))
				{
					y = Notice(notice, left, Width - Margin - CardInset - left, y) + 14;
				}
			}

			y += CardInset - 14;

			float cardBottom = y;

			// ----- Footer -----

			y += 28;

			string stamp =
				string.Format(
					CultureInfo.CurrentCulture,
					"DDjourneys · {0}",
					Format.ToWall(DateTimeOffset.UtcNow).ToString("g", CultureInfo.CurrentCulture));

			y += Text(stamp, Margin, y, Width - 2 * Margin, 24, Regular, Colors.White.WithAlpha(0.7f), 1, HorizontalAlignment.Center);

			y += Margin - 16;

			return new Geometry(y, cardTop, cardBottom);
		}

		// ----- Blocks -----

		private float Ride(
			ShareStep step,
			JourneyStrings text,
			float left,
			float rail,
			float content,
			float contentWidth,
			float y)
		{
			Color lineColor = step.IsCancelled ? Cancelled : ModeColors.For(step.Mode);
			float top = y;

			// Departure row: time | node | pill + direction
			TimeCell(step.Time, step.LiveTime, left, y);

			float pillWidth = Pill(step.Line, content, y, 30, lineColor, Colors.White, Bold);

			string direction = step.Direction is { Length: > 0 } toward ? $"→ {toward}" : string.Empty;

			float rowHeight = PillHeight(30);

			if (direction.Length > 0)
			{
				rowHeight =
					Math.Max(
						rowHeight,
						Text(direction, content + pillWidth + 16, y + 6, contentWidth - pillWidth - 16, 30, Bold, Ink, 2));
			}

			y += rowHeight + 8;

			y += Text($"{step.From}{Suffix(step.FromPlatform)}", content, y, contentWidth, 28, Regular, Muted, 2) + 6;

			if (step.IsCancelled)
			{
				y += Text(text.Cancelled, content, y, contentWidth, 28, Bold, Cancelled, 1) + 6;
			}

			string stops =
				step.IntermediateStops switch
				{
					0 => string.Empty,
					1 => text.OneStop,
					int count => string.Format(CultureInfo.CurrentCulture, text.MultipleStops, count)
				};

			string ride = Format.Duration(step.LiveTime ?? step.Time, step.EndLiveTime ?? step.EndTime);

			y += 18;
			y += Text(stops.Length > 0 ? $"{stops} · {ride}" : ride, content, y, contentWidth, 24, Regular, Muted, 1) + 22;

			// Arrival row
			float arrival = y;

			TimeCell(step.EndTime, step.EndLiveTime, left, y);

			y += Math.Max(
				46,
				Text($"{step.To}{Suffix(step.ToPlatform)}", content, y, contentWidth, 30, Bold, Ink, 2)) + 26;

			// Rail with a node at both ends (drawn last, over nothing in the card's text column).
			if (draw)
			{
				canvas.SaveState();
				canvas.FillColor = lineColor;
				canvas.FillRoundedRectangle(rail - RailWidth / 2, top + 20, RailWidth, arrival - top, RailWidth / 2);
				Node(rail, top + 20, lineColor);
				Node(rail, arrival + 20, lineColor);
				canvas.RestoreState();
			}

			return y;
		}

		private float Walk(ShareStep step, JourneyStrings text, float rail, float content, float contentWidth, float y)
		{
			string duration = step.Duration is { } walk && walk > TimeSpan.Zero ? Format.Duration(walk) : string.Empty;
			string label = $"{text.Walk} {duration} {text.To} {step.To}".Replace("  ", " ", StringComparison.Ordinal);

			float height = Math.Max(44, Text(label, content, y + 4, contentWidth, 26, Regular, Muted, 2) + 8);

			if (draw)
			{
				// Dotted rail: walking is not a vehicle.
				canvas.SaveState();
				canvas.FillColor = Muted.WithAlpha(0.55f);

				for (float dot = y + 6; dot < y + height - 4; dot += 14)
				{
					canvas.FillCircle(rail, dot, 3.5f);
				}

				canvas.RestoreState();
			}

			return y + height + 18;
		}

		private float Change(ShareStep step, JourneyStrings text, float content, float contentWidth, float y)
		{
			string wait =
				step.Duration is { } left && left > TimeSpan.Zero
					? $"{Format.Duration(left)} {text.ToChange}"
					: text.ImmediateChange;

			Color color = step.IsEndangered ? Cancelled : Muted;

			float height = Text($"⇄ {text.ChangeAt} {step.From} · {wait}", content, y, contentWidth, 26, Bold, color, 2);

			if (step.IsEndangered)
			{
				height += 4 + Text(text.ConnectionMayBeMissed, content, y + height + 4, contentWidth, 24, Regular, Cancelled, 1);
			}

			if (draw)
			{
				canvas.SaveState();
				canvas.StrokeColor = Hairline;
				canvas.StrokeSize = 2;
				canvas.DrawLine(content, y + height + 14, content + contentWidth, y + height + 14);
				canvas.RestoreState();
			}

			return y + height + 32;
		}

		private float Arrive(
			ShareStep step,
			JourneyStrings text,
			float left,
			float rail,
			float content,
			float contentWidth,
			float y)
		{
			TimeCell(step.Time, null, left, y);

			if (draw)
			{
				canvas.SaveState();
				canvas.FillColor = GradientTop;
				canvas.FillCircle(rail, y + 20, 14);
				canvas.FillColor = Colors.White;
				canvas.FillCircle(rail, y + 20, 5);
				canvas.RestoreState();
			}

			return y + Math.Max(46, Text($"{text.Arrive} {step.To}", content, y, contentWidth, 32, Bold, Ink, 2)) + 10;
		}

		private float Notice(string notice, float x, float width, float y)
		{
			const float pad = 24;

			float inner = Text($"⚠ {notice}", x + pad, y + pad, width - 2 * pad, 24, Regular, Ink, 5, measureOnly: true);

			if (draw)
			{
				canvas.SaveState();
				canvas.FillColor = NoticeFill;
				canvas.FillRoundedRectangle(x, y, width, inner + 2 * pad, 18);
				canvas.RestoreState();

				Text($"⚠ {notice}", x + pad, y + pad, width - 2 * pad, 24, Regular, Ink, 5);
			}

			return y + inner + 2 * pad;
		}

		// ----- Primitives -----

		private void Background(float top, float height)
		{
			canvas.SaveState();

			var paint =
				new LinearGradientPaint
				{
					StartColor = GradientTop,
					EndColor = GradientBottom,
					StartPoint = new Point(0, 0),
					EndPoint = new Point(0.35, 1)
				};

			var area = new RectF(0, top, Width, height);

			canvas.SetFillPaint(paint, area);
			canvas.FillRectangle(area);
			canvas.RestoreState();
		}

		private void TimeCell(DateTimeOffset? planned, DateTimeOffset? live, float left, float y)
		{
			Text(Format.TimeOrDash(planned), left, y, TimeColumn, 34, Bold, Ink, 1, HorizontalAlignment.Right);

			if (live is { } actual && planned is { } plan)
			{
				Color color = actual > plan ? Late : OnTime;

				Text(Format.TimeOrDash(actual), left, y + 42, TimeColumn, 26, Bold, color, 1, HorizontalAlignment.Right);
			}
		}

		private void Node(float x, float y, Color color)
		{
			canvas.FillColor = Colors.White;
			canvas.FillCircle(x, y, 13);
			canvas.StrokeColor = color;
			canvas.StrokeSize = 6;
			canvas.DrawCircle(x, y, 11);
		}

		/// <summary>A rounded label; returns its width.</summary>
		private float Pill(string label, float x, float y, float size, Color fill, Color ink, IFont font)
		{
			SizeF measured = canvas.GetStringSize(label, font, size);
			float width = measured.Width + 2 * (size * 0.6f);
			float height = PillHeight(size);

			if (draw)
			{
				canvas.SaveState();
				canvas.FillColor = fill;
				canvas.FillRoundedRectangle(x, y, width, height, height / 2);
				canvas.Font = font;
				canvas.FontSize = size;
				canvas.FontColor = ink;
				canvas.DrawString(label, x, y, width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
				canvas.RestoreState();
			}

			return width;
		}

		private static float PillHeight(float size) =>
			size * 1.8f;

		/// <summary>Wrapped text with at most <paramref name="maxLines"/> lines; returns its height.</summary>
		private float Text(
			string value,
			float x,
			float y,
			float width,
			float size,
			IFont font,
			Color color,
			int maxLines,
			HorizontalAlignment alignment = HorizontalAlignment.Left,
			bool measureOnly = false)
		{
			List<string> lines = Wrap(value, font, size, width, maxLines);
			float lineHeight = size * 1.3f;

			if (draw && !measureOnly)
			{
				canvas.SaveState();
				canvas.Font = font;
				canvas.FontSize = size;
				canvas.FontColor = color;

				for (int i = 0; i < lines.Count; i++)
				{
					canvas.DrawString(
						lines[i],
						x,
						y + i * lineHeight,
						width,
						lineHeight,
						alignment,
						VerticalAlignment.Center);
				}

				canvas.RestoreState();
			}

			return lines.Count * lineHeight;
		}

		/// <summary>Greedy word wrap with an ellipsis on the last line when the text is longer.</summary>
		private List<string> Wrap(string value, IFont font, float size, float width, int maxLines)
		{
			var lines = new List<string>();
			string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string current = string.Empty;

			for (int i = 0; i < words.Length; i++)
			{
				string candidate = current.Length == 0 ? words[i] : $"{current} {words[i]}";

				if (current.Length > 0 && canvas.GetStringSize(candidate, font, size).Width > width)
				{
					lines.Add(current);
					current = words[i];

					if (lines.Count == maxLines)
					{
						// No room left: shorten the last line and mark the cut.
						lines[^1] = Ellipsize($"{lines[^1]} {string.Join(' ', words.Skip(i))}", font, size, width);
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
					canvas.GetStringSize(current, font, size).Width > width
						? Ellipsize(current, font, size, width)
						: current);
			}

			return lines.Count == 0 ? [string.Empty] : lines;
		}

		private string Ellipsize(string value, IFont font, float size, float width)
		{
			string text = value;

			while (text.Length > 1 && canvas.GetStringSize($"{text}…", font, size).Width > width)
			{
				text = text[..^1];
			}

			return $"{text.TrimEnd()}…";
		}

		private static string Suffix(string? platform) =>
			string.IsNullOrWhiteSpace(platform) ? string.Empty : $" · {platform}";
	}
}
