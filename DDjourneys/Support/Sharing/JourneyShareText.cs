using System.Globalization;
using System.Text;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// The journey as plain text for messengers: a headline, the key figures, then one block per
/// step with times, lines, directions, platforms, real-time deviations and changes, and the
/// notices at the end. Mode symbols make the steps scannable; everything also reads without them.
/// </summary>
public static class JourneyShareText
{
	private const string Indent = "        ";

	public static string Build(JourneyShareModel model, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(strings);

		JourneyStrings text = strings.Journey;
		var builder = new StringBuilder();

		builder.AppendLine($"{model.Origin} → {model.Destination}");

		var figures = new List<string>(4);

		if (model.Day.Length > 0)
		{
			figures.Add(model.Day);
		}

		figures.Add($"{Format.TimeOrDash(model.Departure)}–{Format.TimeOrDash(model.Arrival)}");
		figures.Add(model.DurationText);
		figures.Add(model.TransfersText);

		builder.AppendLine(string.Join(" · ", figures));

		if (model.IsCancelled)
		{
			builder.AppendLine($"❌ {(model.BlockText.Length > 0 ? model.BlockText : text.Cancelled)}");
		}

		builder.AppendLine();

		foreach (ShareStep step in model.Steps)
		{
			switch (step.Kind)
			{
				case ShareStepKind.Depart:
					builder.AppendLine($"\U0001F4CD {Time(step.Time)}  {text.Depart} {step.To}");
					break;

				case ShareStepKind.Walk:
					{
						string walk = $"{text.Walk} {Duration(step.Duration)} {text.To} {step.To}".Replace("  ", " ");

						string wait =
							step.WaitTime is { } left && left >= TimeSpan.FromMinutes(1)
								? $" · {Format.Duration(left)} {text.ToChange}"
								: string.Empty;

						string risk = step.IsEndangered ? $" ⚠ {text.ConnectionMayBeMissed}" : string.Empty;

						builder.AppendLine($"{Symbol(TransitMode.Walk)} {Time(step.Time)}  {walk}{wait}{risk}");
						break;
					}

				case ShareStepKind.Ride:
					AppendRide(builder, step, text);
					break;

				case ShareStepKind.Change:
					{
						string wait =
							step.Duration is { } left && left > TimeSpan.Zero
								? $" · {Format.Duration(left)} {text.ToChange}"
								: $" · {text.ImmediateChange}";

						string risk = step.IsEndangered ? $" ⚠ {text.ConnectionMayBeMissed}" : string.Empty;

						builder.AppendLine($"⇄       {text.ChangeAt} {step.From}{wait}{risk}");
						break;
					}

				case ShareStepKind.Arrive:
					builder.AppendLine($"\U0001F3C1 {Time(step.Time)}  {text.Arrive} {step.To}");
					break;
			}
		}

		if (model.Notices.Count > 0)
		{
			builder.AppendLine();

			foreach (string notice in model.Notices)
			{
				builder.AppendLine($"⚠ {notice}");
			}
		}

		builder.AppendLine();
		builder.Append("— DDjourneys");

		return builder.ToString();
	}

	private static void AppendRide(StringBuilder builder, ShareStep step, JourneyStrings text)
	{
		string direction = step.Direction is { Length: > 0 } toward ? $" → {toward}" : string.Empty;
		string mode = Format.TransportMode(step.Mode);
		string line = string.Equals(step.Line, mode, StringComparison.CurrentCulture) ? mode : $"{mode} {step.Line}";

		builder.AppendLine(
			$"{Symbol(step.Mode)} {Time(step.Time)}{Live(step.Time, step.LiveTime)}  {line}{direction}" +
			(step.IsCancelled ? $" ❌ {text.Cancelled}" : string.Empty));

		builder.AppendLine(
			$"{Indent}{text.Depart} {step.From}{Bracketed(step.FromPlatform)}");

		string stops =
			step.IntermediateStops switch
			{
				0 => string.Empty,
				1 => $" · {text.OneStop}",
				int count => $" · {string.Format(CultureInfo.CurrentCulture, text.MultipleStops, count)}"
			};

		builder.AppendLine(
			$"{Indent}{Time(step.EndTime)}{Live(step.EndTime, step.EndLiveTime)} " +
			$"{text.Arrive} {step.To}{Bracketed(step.ToPlatform)}{stops}");
	}

	private static string Time(DateTimeOffset? value) =>
		Format.TimeOrDash(value);

	/// <summary>" (08:14, +2 min)" when the real-time value differs; empty otherwise.</summary>
	private static string Live(DateTimeOffset? planned, DateTimeOffset? live) =>
		live is { } actual
			? $" ({Format.TimeOrDash(actual)}, {Format.Delay(actual - planned)})"
			: string.Empty;

	private static string Duration(TimeSpan? value) =>
		value is { } duration && duration > TimeSpan.Zero
			? Format.Duration(duration)
			: string.Empty;

	private static string Bracketed(string? value) =>
		string.IsNullOrWhiteSpace(value) ? string.Empty : $" ({value})";

	private static string Symbol(TransitMode mode) =>
		mode switch
		{
			TransitMode.Walk => "\U0001F6B6",
			TransitMode.Tram => "\U0001F68B",
			TransitMode.Bus => "\U0001F68C",
			TransitMode.Subway => "\U0001F687",
			TransitMode.SuburbanRail => "\U0001F688",
			TransitMode.RegionalTrain or TransitMode.LongDistanceTrain => "\U0001F686",
			TransitMode.Ferry => "⛴️",
			TransitMode.CableCar => "\U0001F6A1",
			TransitMode.Taxi or TransitMode.OnDemand => "\U0001F695",
			_ => "\U0001F68D"
		};
}