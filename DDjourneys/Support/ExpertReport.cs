using System.Collections;
using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;
using DDjourneys.Core.Providers.Vvo.Serialization;
using System.Text;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>One labelled value of the expert view.</summary>
public sealed record ExpertEntry(string Key, string Value);

/// <summary>A titled block of the expert view.</summary>
public sealed record ExpertSection(string Title, IReadOnlyList<ExpertEntry> Entries);

/// <summary>
/// Everything known about a journey, as plain key/value text: our own model plus a field-by-field dump
/// of the provider objects it was mapped from (so raw codes such as occupancy strings, platform types,
/// Diva data, vehicle and trip ids are visible). Pure and UI-free.
/// </summary>
public static class ExpertReport
{
	private const int MaxValueLength = 600;

	public static IReadOnlyList<ExpertSection> Build(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		ExpertStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Expert;

		var sections =
			new List<ExpertSection>
			{
				Overview(journey, strings)
			};

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			sections.Add(
				Leg(
					journey.Legs[i],
					i,
					strings));
		}

		if (journey.Transfers.Count > 0)
		{
			sections.Add(
				Transfers(
					journey,
					strings));
		}

		return sections;
	}


	/// <summary>The whole report as copyable text.</summary>
	public static string ToText(
		IEnumerable<ExpertSection> sections)
	{
		var text =
			new StringBuilder();

		foreach (ExpertSection section in sections)
		{
			text.AppendLine(
				$"## {section.Title}");

			foreach (ExpertEntry entry in section.Entries)
			{
				text.AppendLine(
					$"{entry.Key}: {entry.Value}");
			}

			text.AppendLine();
		}

		return text.ToString().TrimEnd();
	}


	private static ExpertSection Overview(
		Journey journey,
		ExpertStrings strings)
	{
		var entries =
			new List<ExpertEntry>();

		Add(entries, "Id", journey.Id);
		Add(entries, "Context", journey.Context);
		Add(entries, "From", Describe(journey.From));
		Add(entries, "To", Describe(journey.To));
		Add(entries, "Departure", Stamp(journey.Departure));
		Add(entries, "Arrival", Stamp(journey.Arrival));
		Add(entries, "Duration", journey.Duration.ToString("c", CultureInfo.InvariantCulture));
		Add(entries, "PlannedDuration", journey.PlannedDuration?.ToString("c", CultureInfo.InvariantCulture));
		Add(entries, "Transfers", journey.TransferCount.ToString(CultureInfo.InvariantCulture));
		Add(entries, "HasDelay", journey.HasDelay.ToString());
		Add(entries, "IsCancelled", journey.IsCancelled.ToString());

		for (int i = 0; i < journey.Notices.Count; i++)
		{
			Add(entries, $"Notice {i + 1}", journey.Notices[i]);
		}

		AddProvider(entries, journey.ProviderData, "Provider");

		return new ExpertSection(
			strings.Journey,
			entries);
	}


	private static ExpertSection Leg(
		JourneyLeg leg,
		int index,
		ExpertStrings strings)
	{
		var entries =
			new List<ExpertEntry>();

		Add(entries, "Mode", leg.Mode.ToString());
		Add(entries, "Id", leg.Id);
		Add(entries, "From", Describe(leg.From));
		Add(entries, "To", Describe(leg.To));
		Add(entries, "Departure (planned)", Stamp(leg.ScheduledDeparture));
		Add(entries, "Departure (real-time)", Stamp(leg.RealtimeDeparture));
		Add(entries, "Arrival (planned)", Stamp(leg.ScheduledArrival));
		Add(entries, "Arrival (real-time)", Stamp(leg.RealtimeArrival));
		Add(entries, "Departure platform", Platform(leg.DeparturePlatform, leg.DeparturePlatformKind));
		Add(entries, "Arrival platform", Platform(leg.ArrivalPlatform, leg.ArrivalPlatformKind));
		Add(entries, "IsCancelled", leg.IsCancelled.ToString());
		Add(entries, "Path points", leg.Path.Count.ToString(CultureInfo.InvariantCulture));

		if (leg.Line is { } line)
		{
			Add(entries, "Line", line.Name);
			Add(entries, "Line operator", line.Operator);
			Add(entries, "Line destination", line.Destination);
			Add(entries, "Line direction id (Diva number)", line.DirectionId);
		}

		if (leg.Vehicle is { } vehicle)
		{
			Add(entries, "Vehicle id", vehicle.Id);
			Add(entries, "Vehicle name", vehicle.Name);
			Add(entries, "Vehicle operator", vehicle.Operator);
			Add(entries, "Vehicle operator code", vehicle.OperatorCode);
			Add(entries, "Vehicle product", vehicle.ProductName);
			Add(entries, "Vehicle DlId", vehicle.DlId);
			Add(entries, "Vehicle stateless id", vehicle.StatelessId);
			Add(entries, "Vehicle occupancy", vehicle.Occupancy.ToString());

			if (vehicle.Accessibility is { } access)
			{
				Add(entries, "Low floor", access.LowFloor?.ToString());
				Add(entries, "Wheelchair", access.WheelchairAccessible?.ToString());
				Add(entries, "Bicycle", access.BicycleAccessible?.ToString());
			}
		}

		for (int i = 0; i < leg.Notices.Count; i++)
		{
			Add(entries, $"Notice {i + 1}", leg.Notices[i]);
		}

		AddProvider(entries, leg.ProviderData, "Provider");

		for (int i = 0; i < leg.Stops.Count; i++)
		{
			StopTime stop = leg.Stops[i];

			var detail =
				new List<string>
				{
					$"id {stop.Station.Id}",
					$"arr {Stamp(stop.ScheduledArrival)} → {Stamp(stop.RealtimeArrival)}",
					$"dep {Stamp(stop.ScheduledDeparture)} → {Stamp(stop.RealtimeDeparture)}",
					$"platform {Platform(stop.Platform, stop.PlatformKind) ?? "-"}",
					$"occupancy {stop.Occupancy}",
					$"cancelled {stop.IsCancelled}"
				};

			if (stop.Station.Latitude is { } lat
				&& stop.Station.Longitude is { } lon)
			{
				detail.Add(
					string.Create(
						CultureInfo.InvariantCulture,
						$"wgs84 {lat:0.00000}, {lon:0.00000}"));
			}

			foreach (ExpertEntry raw in Dump(stop.ProviderData, string.Empty))
			{
				if (raw.Key is "ArrivalState" or "DepartureState" or "Type" or "Occupancy" or "Platform.Type")
				{
					detail.Add($"raw {raw.Key}={raw.Value}");
				}
			}

			entries.Add(
				new ExpertEntry(
					$"{strings.Stops} {i + 1}: {StopLabel.Compose(stop.Station)}",
					string.Join(
						Environment.NewLine,
						detail)));
		}

		return new ExpertSection(
			$"{strings.Leg} {index + 1}: {leg.Line?.Name ?? leg.Mode.ToString()}",
			entries);
	}


	private static ExpertSection Transfers(
		Journey journey,
		ExpertStrings strings)
	{
		var entries =
			new List<ExpertEntry>();

		for (int i = 0; i < journey.Transfers.Count; i++)
		{
			JourneyTransfer transfer = journey.Transfers[i];

			var detail =
				new List<string>
				{
					$"kind {transfer.Kind}",
					$"duration {transfer.Duration:c}",
					$"waiting {transfer.WaitingTime:c}",
					$"guaranteed {transfer.IsGuaranteed}",
					$"legs {transfer.PreviousLegIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"} → {transfer.NextLegIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
					$"arrival {Platform(transfer.ArrivalPlatform, transfer.ArrivalPlatformKind) ?? "-"}",
					$"departure {Platform(transfer.DeparturePlatform, transfer.DeparturePlatformKind) ?? "-"}",
					$"path points {transfer.Path.Count}"
				};

			foreach (ExpertEntry raw in Dump(transfer.ProviderData, string.Empty))
			{
				if (raw.Key is "PartialRouteId" or "Duration" or "ChangeoverEndangered" or "Mot.Type" or "Mot.Name")
				{
					detail.Add($"raw {raw.Key}={raw.Value}");
				}
			}

			entries.Add(
				new ExpertEntry(
					$"{strings.Transfers} {i + 1}",
					string.Join(
						Environment.NewLine,
						detail)));
		}

		return new ExpertSection(
			strings.Transfers,
			entries);
	}


	private static void AddProvider(
		List<ExpertEntry> entries,
		object? data,
		string prefix)
	{
		foreach (ExpertEntry entry in Dump(data, string.Empty))
		{
			entries.Add(
				new ExpertEntry(
					$"{prefix}.{entry.Key}",
					entry.Value));
		}
	}


	/// <summary>
	/// Public readable properties of a provider object, flattened: scalars as text, string lists joined,
	/// nested objects with a dotted prefix. Lists of objects (stops, partial routes) and the long map
	/// geometry are only counted.
	/// </summary>
	internal static IEnumerable<ExpertEntry> Dump(
		object? data,
		string prefix,
		int depth = 0)
	{
		if (data is null || depth > 3)
		{
			yield break;
		}

		if (data is XElement element)
		{
			foreach (ExpertEntry entry in DumpXml(element))
			{
				yield return entry;
			}

			yield break;
		}

		// Source-generated metadata of the provider DTOs: names and getters without reflection.
		if (VvoJson.TypeInfo(data.GetType()) is not { } typeInfo)
		{
			yield return new ExpertEntry(
				prefix.Length == 0 ? data.GetType().Name : prefix,
				Clip(data.ToString() ?? string.Empty));

			yield break;
		}

		foreach (JsonPropertyInfo property in typeInfo.Properties)
		{
			if (property.Get is null)
			{
				continue;
			}

			string name = property.Name;

			string key =
				prefix.Length == 0
					? name
					: $"{prefix}.{name}";

			object? value = property.Get(data);

			switch (value)
			{
				case null:
					break;

				case string text:
					if (text.Length > 0)
					{
						yield return new ExpertEntry(key, Clip(text));
					}

					break;

				case DateTimeOffset time:
					yield return new ExpertEntry(key, Stamp(time)!);
					break;

				case IEnumerable<string> strings:
					string[] items = [.. strings];

					if (items.Length > 0)
					{
						yield return new ExpertEntry(
							key,
							key.EndsWith("MapData", StringComparison.Ordinal)
								? $"[{items.Length}]"
								: Clip(string.Join(" | ", items)));
					}

					break;

				case IEnumerable list when value is not string:
					yield return new ExpertEntry(
						key,
						$"[{list.Cast<object?>().Count()}]");

					break;

				default:
					if (value.GetType().IsPrimitive
						|| value is Enum
						|| value is decimal
						|| value is TimeSpan)
					{
						yield return new ExpertEntry(
							key,
							Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
					}
					else
					{
						foreach (ExpertEntry nested in Dump(value, key, depth + 1))
						{
							yield return nested;
						}
					}

					break;
			}
		}
	}


	/// <summary>The leaf elements and attributes of a TRIAS element, as path = text.</summary>
	private static IEnumerable<ExpertEntry> DumpXml(
		XElement root)
	{
		foreach (XElement leaf in root.DescendantsAndSelf().Where(e => !e.HasElements))
		{
			string text = leaf.Value.Trim();

			if (text.Length == 0)
			{
				continue;
			}

			string path =
				string.Join(
					".",
					leaf.AncestorsAndSelf()
						.Reverse()
						.SkipWhile(e => e != root)
						.Select(e => e.Name.LocalName));

			yield return new ExpertEntry(path, Clip(text));
		}
	}


	private static void Add(
		List<ExpertEntry> entries,
		string key,
		string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			entries.Add(new ExpertEntry(key, value));
		}
	}


	private static string Describe(
		Station station) =>
		string.IsNullOrWhiteSpace(station.Id)
			? StopLabel.Compose(station)
			: $"{StopLabel.Compose(station)} [{station.Id}]";


	private static string? Platform(
		string? name,
		PlatformKind kind) =>
		string.IsNullOrWhiteSpace(name)
			? null
			: $"{name} ({kind})";


	private static string? Stamp(
		DateTimeOffset? time) =>
		time?.ToString(
			"yyyy-MM-dd HH:mm:ss zzz",
			CultureInfo.InvariantCulture);


	private static string Clip(
		string text) =>
		text.Length <= MaxValueLength
			? text
			: text[..MaxValueLength] + "…";
}
