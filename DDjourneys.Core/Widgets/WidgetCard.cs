using System.Globalization;
using System.Text.Json.Nodes;
using DDjourneys.Core.Serialization;

namespace DDjourneys.Core.Widgets;

/// <summary>The words a card needs, localized by the caller at render time (the board may render before the app could switch languages).</summary>
public sealed record WidgetCardStrings(
	string Updated,
	string Refresh,
	string Open,
	string SetUp,
	string CouldNotRefresh,
	string SetUpHint = "",
	string InMinutes = "in {0} min",
	string Now = "now");

/// <summary>The three sizes of the Widgets Board; the card is built for the one the board says.</summary>
public enum WidgetCardSize
{
	Small = 0,
	Medium,
	Large
}

/// <summary>A drawn line chip as an image the card embeds (a data URI of a PNG, <see cref="Width"/> x <see cref="Height"/> in card pixels).</summary>
public sealed record WidgetChipImage(string DataUri, int Width, int Height);

/// <summary>One card as the widget host takes it: the Adaptive Card itself, and the (empty) data document.</summary>
public sealed record WidgetCardPayload(string Template, string Data);

/// <summary>
/// Builds the Adaptive Card JSON of a Windows widget from a snapshot and the size the board shows it in - a
/// JsonObject tree via Wire, no DTO, no anonymous types. The design follows "Widget design fundamentals": the
/// type ramp (caption = small + subtle, body = default, body strong = default + bolder), the host's default
/// and subtle colours for text (the provider is not told the theme), the host's semantic colours for the
/// statuses (good / warning / attention), and the board's own header, whose text the card overrides with
/// the stop or the route. The line chips are drawn images in the colours and shapes of the app's chips (the
/// card cannot colour a container); without an image renderer a chip is a rounded emphasis container.
/// The rows are literal, like the weather example in the docs: the values are baked in when the card is sent.
/// </summary>
public static class WidgetCard
{
	/// <summary>The card version the Widgets Board supports (it is needed for the header override).</summary>
	private const string Version = "1.6";

	private const string Schema = "http://adaptivecards.io/schemas/adaptive-card.json";

	/// <summary>A departure further away than this shows no countdown.</summary>
	private static readonly TimeSpan CountdownLimit = TimeSpan.FromMinutes(60);

	/// <summary>How many rows a widget of this size has: always as many as fit (2, 6 and 10), never fewer by choice.</summary>
	public static int RowsFor(WidgetCardSize size) =>
		size switch
		{
			WidgetCardSize.Small => 2,
			WidgetCardSize.Medium => 6,
			_ => WidgetConfig.MaxRowsLimit
		};

	/// <summary>
	/// The card for a snapshot: the board's header, the rows that fit the size (every row with all it has to say, on
	/// every size), and on every size one footer line - the time of the last update and the link into the app.
	/// A tap on the card refreshes it; there are no buttons.
	/// </summary>
	public static WidgetCardPayload For(
		WidgetSnapshot snapshot,
		WidgetCardSize size,
		WidgetCardStrings strings,
		string? titleOverride = null,
		Func<WidgetRow, WidgetChipImage?>? chips = null,
		DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(strings);

		DateTimeOffset at = now ?? DateTimeOffset.UtcNow;

		JsonArray body = Wire.Array();

		string title = string.IsNullOrWhiteSpace(titleOverride) ? snapshot.Title : titleOverride;

		IReadOnlyList<WidgetRow> shown = [.. snapshot.Upcoming(at).Take(RowsFor(size))];

		if (shown.Count == 0)
		{
			body.Add(Text(snapshot.Message, isSubtle: snapshot.Message.Length > 0, spacing: "none"));
		}
		else
		{
			bool first = true;

			foreach (WidgetRow row in shown)
			{
				body.Add(
					row.Kind == WidgetRowKind.Header
						? Header(row, first)
						: Row(row, chips?.Invoke(row), first, at, strings));

				first = false;
			}
		}

		body.Add(Footer(Stamp(snapshot, strings), strings, snapshot.IsStale));

		return Payload(body, title, Execute(strings.Refresh, "refresh"));
	}

	/// <summary>The card for a widget that has no settings yet: what it is for and the one thing to do.</summary>
	public static WidgetCardPayload SetUp(WidgetCardStrings strings)
	{
		ArgumentNullException.ThrowIfNull(strings);

		JsonArray body = Wire.Array(Text(strings.SetUp, size: "medium", weight: "bolder", spacing: "none"));

		if (strings.SetUpHint.Length > 0)
		{
			body.Add(Text(strings.SetUpHint, isSubtle: true, spacing: "small"));
		}

		return Payload(body, string.Empty, Execute(strings.SetUp, "setup"));
	}

	private static WidgetCardPayload Payload(JsonArray body, string header, JsonObject select)
	{
		var card =
			new JsonObject
			{
				["type"] = "AdaptiveCard",
				["$schema"] = Schema,
				["version"] = Version
			};

		// The board's own header names the definition ("Departures"); the stop or the route says more.
		if (header.Length > 0)
		{
			card["header"] = header;
		}

		card["body"] = body;
		card["selectAction"] = select;

		return new WidgetCardPayload(card.ToJsonString(), "{}");
	}

	private static string Stamp(WidgetSnapshot snapshot, WidgetCardStrings strings)
	{
		if (snapshot.IsStale)
		{
			return strings.CouldNotRefresh;
		}

		return snapshot.UpdatedAt is { } updated
			? string.Format(
				CultureInfo.CurrentCulture,
				strings.Updated,
				updated.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture))
			: string.Empty;
	}

	/// <summary>One line at the very bottom: when the widget was updated, and the link into the app (like the weather widget's).</summary>
	private static JsonObject Footer(string stamp, WidgetCardStrings strings, bool stale) =>
		new()
		{
			["type"] = "ColumnSet",
			["spacing"] = "small",
			["columns"] = Wire.Array(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "stretch",
					["verticalContentAlignment"] = "center",
					["items"] = Wire.Array(
						Text(stamp, size: "small", isSubtle: !stale, color: stale ? "warning" : "default", wrap: false, spacing: "none"))
				},
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["verticalContentAlignment"] = "center",
					["selectAction"] = Execute(strings.Open, "open"),
					["items"] = Wire.Array(
						Text(strings.Open, size: "small", weight: "bolder", color: "accent", wrap: false, spacing: "none", align: "right"))
				})
		};

	/// <summary>A stop's name above its departures (the nearby widgets): the distance on the right.</summary>
	private static JsonObject Header(WidgetRow row, bool first) =>
		new()
		{
			["type"] = "ColumnSet",
			["spacing"] = first ? "none" : "medium",
			["columns"] = Wire.Array(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "stretch",
					["items"] = Wire.Array(
						Text(row.Main, size: "small", weight: "bolder", isSubtle: true, wrap: false, spacing: "none"))
				},
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["items"] = Wire.Array(
						Text(row.Time, size: "small", isSubtle: true, wrap: false, spacing: "none", align: "right"))
				})
		};

	/// <summary>One row: the line chip, where it goes (and the second line), when - with the delay and the countdown on the right.</summary>
	private static JsonObject Row(WidgetRow row, WidgetChipImage? chip, bool first, DateTimeOffset now, WidgetCardStrings strings)
	{
		string? countdown = Countdown(row, now, strings);

		JsonArray main;
		JsonArray tail;

		if (row.Arrival.Length > 0)
		{
			// A journey: "23:09 → 23:17" and the facts below; the countdown to the start and the delay on the right.
			main = Wire.Array(
				Text($"{row.Time} \u2192 {row.Arrival}", weight: "bolder", wrap: false, spacing: "none"));

			string facts = string.Join(" \u00b7 ", new[] { row.Lead, row.Facts }.Where(part => part.Length > 0));

			if (facts.Length > 0)
			{
				main.Add(Text(facts, size: "small", isSubtle: true, wrap: false, spacing: "none"));
			}

			tail = Wire.Array();

			if (countdown is not null)
			{
				tail.Add(Text(countdown, weight: "bolder", wrap: false, spacing: "none", align: "right"));
			}

			if (row.Delay.Length > 0)
			{
				tail.Add(Text(row.Delay, size: "small", weight: "bolder", color: Level(row), wrap: false, spacing: "none", align: "right"));
			}
		}
		else
		{
			main = Wire.Array(Text(row.Main, weight: "bolder", wrap: false, spacing: "none"));

			if (row.Sub.Length > 0)
			{
				main.Add(Text(row.Sub, size: "small", isSubtle: true, wrap: false, spacing: "none"));
			}

			tail = Wire.Array(Text(row.Time, weight: "bolder", wrap: false, spacing: "none", align: "right"));

			if (Detail(row, countdown) is { } detail)
			{
				tail.Add(detail);
			}
		}

		JsonArray columns =
			Wire.Array(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["verticalContentAlignment"] = "center",
					["items"] = Wire.Array(Chip(row, chip))
				},
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "stretch",
					["verticalContentAlignment"] = "center",
					["items"] = main
				});

		if (tail.Count > 0)
		{
			columns.Add(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["verticalContentAlignment"] = "center",
					["items"] = tail
				});
		}

		return new JsonObject
		{
			["type"] = "ColumnSet",
			["spacing"] = first ? "none" : "small",
			["columns"] = columns
		};
	}

	/// <summary>The second line on the right of a departure: the delay in the host's colour, then the countdown, subtle.</summary>
	private static JsonObject? Detail(WidgetRow row, string? countdown)
	{
		if (row.Delay.Length == 0
			&& countdown is null)
		{
			return null;
		}

		if (row.Delay.Length == 0)
		{
			return Text(countdown!, size: "small", isSubtle: true, wrap: false, spacing: "none", align: "right");
		}

		if (countdown is null)
		{
			return Text(row.Delay, size: "small", weight: "bolder", color: Level(row), wrap: false, spacing: "none", align: "right");
		}

		return new JsonObject
		{
			["type"] = "RichTextBlock",
			["spacing"] = "none",
			["horizontalAlignment"] = "right",
			["inlines"] = Wire.Array(
				new JsonObject
				{
					["type"] = "TextRun",
					["text"] = row.Delay,
					["size"] = "small",
					["weight"] = "bolder",
					["color"] = Level(row)
				},
				new JsonObject
				{
					["type"] = "TextRun",
					["text"] = $" \u00b7 {countdown}",
					["size"] = "small",
					["isSubtle"] = true
				})
		};
	}

	/// <summary>"in 4 min", "now" - from the moment the card is drawn (the app draws it again every minute while it is seen); null when it is further away than an hour or has no time.</summary>
	private static string? Countdown(WidgetRow row, DateTimeOffset now, WidgetCardStrings strings)
	{
		if (row.At is not { } at
			|| row.DelayLevel == WidgetDelay.Cancelled)
		{
			return null;
		}

		TimeSpan until = at - now;

		if (until > CountdownLimit)
		{
			return null;
		}

		int minutes = (int)Math.Ceiling(until.TotalMinutes);

		return minutes <= 0
			? strings.Now
			: string.Format(CultureInfo.CurrentCulture, strings.InMinutes, minutes);
	}

	private static string Level(WidgetRow row) =>
		row.DelayLevel switch
		{
			WidgetDelay.Late => "warning",
			WidgetDelay.Cancelled => "attention",
			_ => "good"
		};

	/// <summary>The line chip: the drawn image, else a rounded emphasis container with the line in it.</summary>
	private static JsonObject Chip(WidgetRow row, WidgetChipImage? image)
	{
		if (image is not null)
		{
			return new JsonObject
			{
				["type"] = "Image",
				["url"] = image.DataUri,
				["altText"] = row.Chip,
				["width"] = $"{image.Width}px",
				["height"] = $"{image.Height}px"
			};
		}

		return new JsonObject
		{
			["type"] = "Container",
			["style"] = "emphasis",
			["roundedCorners"] = true,
			["items"] = Wire.Array(
				Text(row.Chip, weight: "bolder", wrap: false, spacing: "none", align: "center"))
		};
	}

	private static JsonObject Execute(string title, string verb) =>
		new()
		{
			["type"] = "Action.Execute",
			["title"] = title,
			["verb"] = verb
		};

	private static JsonObject Text(
		string text,
		string size = "default",
		string weight = "default",
		bool wrap = true,
		string spacing = "default",
		bool isSubtle = false,
		string color = "default",
		string align = "left")
	{
		var block =
			new JsonObject
			{
				["type"] = "TextBlock",
				["text"] = text,
				["size"] = size,
				["weight"] = weight,
				["wrap"] = wrap,
				["spacing"] = spacing,
				["isSubtle"] = isSubtle,
				["color"] = color
			};

		if (align != "left")
		{
			block["horizontalAlignment"] = align;
		}

		if (!wrap)
		{
			block["maxLines"] = 1;
		}

		return block;
	}
}
