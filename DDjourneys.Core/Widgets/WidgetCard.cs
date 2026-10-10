using System.Text.Json.Nodes;
using DDjourneys.Core.Serialization;

namespace DDjourneys.Core.Widgets;

/// <summary>The words a card needs, localized by the caller at render time (the board may render before the app could switch languages).</summary>
public sealed record WidgetCardStrings(
	string Updated,
	string Refresh,
	string Open,
	string SetUp,
	string CouldNotRefresh);

/// <summary>One card as the widget host takes it: the Adaptive Card itself, and the (empty) data document.</summary>
public sealed record WidgetCardPayload(string Template, string Data);

/// <summary>
/// Builds the Adaptive Card JSON of a Windows widget from a snapshot and the layout that fits its size -
/// a JsonObject tree via Wire, no DTO, no anonymous types. The card uses the host's own theming: the board
/// follows the device theme and the provider is not told which one is active (WidgetContext carries the id,
/// the definition and the size, nothing else), so the statuses ride on the host's semantic colours
/// (good / warning / attention) and everything else on its default and subtle styles. The rows are literal,
/// like the weather example in the docs: the values are baked in when the card is sent, not data-bound.
/// </summary>
public static class WidgetCard
{
	/// <summary>The card for a snapshot: the header (title, updated time, stale flag), the rows that fit, and the two actions.</summary>
	public static WidgetCardPayload For(
		WidgetSnapshot snapshot,
		WidgetLayout layout,
		WidgetCardStrings strings,
		string? titleOverride = null)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(layout);
		ArgumentNullException.ThrowIfNull(strings);

		JsonArray body = Wire.Array();
		JsonArray actions = Wire.Array();

		string title = titleOverride ?? snapshot.Title;

		if (title.Length > 0)
		{
			body.Add(Text(title, size: "medium", weight: "bolder"));
		}

		if (snapshot.IsStale)
		{
			body.Add(Text(strings.CouldNotRefresh, size: "small", isSubtle: true, spacing: "none"));
		}
		else if (layout.ShowUpdated && snapshot.UpdatedAt is { } updated)
		{
			body.Add(
				Text(
					string.Format(
						System.Globalization.CultureInfo.CurrentCulture,
						strings.Updated,
						updated.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture)),
					size: "small",
					isSubtle: true,
					spacing: "none"));
		}

		IReadOnlyList<WidgetRow> upcoming = snapshot.Upcoming(DateTimeOffset.UtcNow);
		IReadOnlyList<WidgetRow> shown = [.. upcoming.Take(layout.Rows)];

		if (shown.Count == 0)
		{
			body.Add(Text(snapshot.Message, isSubtle: snapshot.Message.Length > 0));
		}
		else
		{
			foreach (WidgetRow row in shown)
			{
				body.Add(Row(row));
			}
		}

		actions.Add(
			new JsonObject
			{
				["type"] = "Action.Execute",
				["title"] = strings.Refresh,
				["verb"] = "refresh"
			});

		actions.Add(
			new JsonObject
			{
				["type"] = "Action.Execute",
				["title"] = strings.Open,
				["verb"] = "open"
			});

		JsonObject card =
			new JsonObject
			{
				["type"] = "AdaptiveCard",
				["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
				["version"] = "1.5",
				["body"] = body,
				["actions"] = actions
			};

		return new WidgetCardPayload(card.ToJsonString(), "{}");
	}

	/// <summary>The card for a widget that has no settings yet: what it shows and the one thing to do.</summary>
	public static WidgetCardPayload SetUp(WidgetCardStrings strings)
	{
		ArgumentNullException.ThrowIfNull(strings);

		JsonObject card =
			new JsonObject
			{
				["type"] = "AdaptiveCard",
				["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
				["version"] = "1.5",
				["body"] = Wire.Array(Text(strings.SetUp)),
				["actions"] = Wire.Array(
					new JsonObject
					{
						["type"] = "Action.Execute",
						["title"] = strings.SetUp,
						["verb"] = "setup"
					})
			};

		return new WidgetCardPayload(card.ToJsonString(), "{}");
	}

	/// <summary>One row of the board: the line chip, where it goes, and when - with the delay in the host's words for it.</summary>
	private static JsonObject Row(WidgetRow row)
	{
		JsonArray main =
			Wire.Array(
				Text(row.Main, spacing: "none"),
				Text(row.Sub, size: "small", spacing: "none", isSubtle: row.Sub.Length > 0));

		JsonArray time =
			Wire.Array(
				Text(row.Time, spacing: "none"),
				Text(
					row.Delay,
					size: "small",
					spacing: "none",
					isSubtle: row.Delay.Length == 0,
					color: row.DelayLevel switch
					{
						WidgetDelay.Late => "warning",
						WidgetDelay.Cancelled => "attention",
						_ => "good"
					}));

		return new JsonObject
		{
			["type"] = "ColumnSet",
			["spacing"] = "small",
			["columns"] = Wire.Array(
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["items"] = Wire.Array(Text(row.Chip, weight: "bolder"))
				},
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "stretch",
					["items"] = main
				},
				new JsonObject
				{
					["type"] = "Column",
					["width"] = "auto",
					["items"] = time
				})
		};
	}

	private static JsonObject Text(
		string text,
		string size = "default",
		string weight = "default",
		bool wrap = true,
		string spacing = "default",
		bool isSubtle = false,
		string color = "default") =>
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
}
