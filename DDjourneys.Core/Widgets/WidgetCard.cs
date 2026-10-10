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
	string SetUpHint = "");

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

	/// <summary>How many rows a widget of this size has room for, within the user's own cap (0: no cap).</summary>
	public static int RowsFor(WidgetCardSize size, int maxRows)
	{
		int fit =
			size switch
			{
				WidgetCardSize.Small => 2,
				WidgetCardSize.Medium => 4,
				_ => 8
			};

		return maxRows > 0 ? Math.Min(fit, maxRows) : fit;
	}

	/// <summary>The card for a snapshot: the header, the rows that fit the size, the time of the last update, the actions.</summary>
	public static WidgetCardPayload For(
		WidgetSnapshot snapshot,
		WidgetCardSize size,
		int maxRows,
		WidgetCardStrings strings,
		string? titleOverride = null,
		Func<WidgetRow, WidgetChipImage?>? chips = null,
		DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(strings);

		JsonArray body = Wire.Array();

		string title = string.IsNullOrWhiteSpace(titleOverride) ? snapshot.Title : titleOverride;

		IReadOnlyList<WidgetRow> upcoming = snapshot.Upcoming(now ?? DateTimeOffset.UtcNow);
		IReadOnlyList<WidgetRow> shown = [.. upcoming.Take(RowsFor(size, maxRows))];

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
						: Row(row, size, chips?.Invoke(row), first));

				first = false;
			}
		}

		string stamp = Stamp(snapshot, strings);

		if (size != WidgetCardSize.Small
			&& stamp.Length > 0)
		{
			body.Add(Text(stamp, size: "small", isSubtle: snapshot.IsStale is false, color: snapshot.IsStale ? "warning" : "default", spacing: "medium"));
		}
		else if (size == WidgetCardSize.Small
			&& snapshot.IsStale)
		{
			body.Add(Text(strings.CouldNotRefresh, size: "small", color: "warning", spacing: "small"));
		}

		JsonArray actions = Wire.Array();

		if (size != WidgetCardSize.Small)
		{
			actions.Add(Execute(strings.Refresh, "refresh"));
			actions.Add(Execute(strings.Open, "open"));
		}

		return Payload(body, actions, title, Execute(strings.Open, "open"));
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

		return Payload(
			body,
			Wire.Array(Execute(strings.SetUp, "setup")),
			string.Empty,
			Execute(strings.SetUp, "setup"));
	}

	private static WidgetCardPayload Payload(JsonArray body, JsonArray actions, string header, JsonObject select)
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
		card["actions"] = actions;
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

	/// <summary>One row: the line chip, where it goes (and the second line), when - with the delay in the host's words for it.</summary>
	private static JsonObject Row(WidgetRow row, WidgetCardSize size, WidgetChipImage? chip, bool first)
	{
		bool small = size == WidgetCardSize.Small;
		bool journey = row.Arrival.Length > 0;

		JsonArray main;
		JsonArray tail = Wire.Array();

		if (journey)
		{
			// "23:09 → 23:17": when to leave and when to arrive; the facts of the journey below.
			main = Wire.Array(
				Text($"{row.Time} → {row.Arrival}", weight: "bolder", wrap: false, spacing: "none"));

			string facts = string.Join(" · ", new[] { row.Lead, row.Duration, row.Transfers, row.Lines }.Where(part => part.Length > 0));

			if (!small && facts.Length > 0)
			{
				main.Add(Text(facts, size: "small", isSubtle: true, wrap: false, spacing: "none"));
			}

			if (row.Delay.Length > 0)
			{
				tail.Add(Text(row.Delay, size: small ? "small" : "default", weight: "bolder", color: Level(row), wrap: false, spacing: "none", align: "right"));
			}
		}
		else
		{
			main = Wire.Array(Text(row.Main, weight: "bolder", wrap: false, spacing: "none"));

			if (!small && row.Sub.Length > 0)
			{
				main.Add(Text(row.Sub, size: "small", isSubtle: true, wrap: false, spacing: "none"));
			}

			// Small: no second line, so a delay colours the time itself.
			tail.Add(
				Text(
					row.Time,
					weight: "bolder",
					wrap: false,
					spacing: "none",
					align: "right",
					color: small && row.Delay.Length > 0 ? Level(row) : "default"));

			if (!small && row.Delay.Length > 0)
			{
				tail.Add(Text(row.Delay, size: "small", color: Level(row), wrap: false, spacing: "none", align: "right"));
			}
		}

		JsonArray columns =
			Wire.Array(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["verticalContentAlignment"] = "center",
					["items"] = Wire.Array(Chip(row, chip, small))
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

	private static string Level(WidgetRow row) =>
		row.DelayLevel switch
		{
			WidgetDelay.Late => "warning",
			WidgetDelay.Cancelled => "attention",
			_ => "good"
		};

	/// <summary>The line chip: the drawn image, else a rounded emphasis container with the line in it.</summary>
	private static JsonObject Chip(WidgetRow row, WidgetChipImage? image, bool small)
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
				Text(row.Chip, size: small ? "small" : "default", weight: "bolder", wrap: false, spacing: "none", align: "center"))
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
