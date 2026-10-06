using System.Globalization;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support.Widgets;

/// <summary>
/// Fetches what a widget shows. It uses the provider the widget was made for, not the one the app shows now
/// (<see cref="ProviderRegistry.Override"/>). The device position comes from the platform's last known one;
/// a widget cannot ask for a fresh fix in the background.
/// </summary>
public sealed class WidgetLoader(
	JourneyService journeys,
	DepartureService departures,
	LocationService locations,
	NetworkService network,
	ProviderRegistry providers)
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>Rows kept beyond what is shown: departures that have left by the time the widget is drawn again.</summary>
	private const int Spare = 2;

	/// <summary>
	/// How many rows to ask the providers for: the number the user chose, or (as many as fit) what the widget's size
	/// shows right now.
	/// </summary>
	public static int Wanted(WidgetConfig config, int fitting) =>
		Math.Clamp(config.MaxRows > 0 ? config.MaxRows : fitting, 1, WidgetLayout.MaxRows);

	/// <param name="config">The widget's settings.</param>
	/// <param name="fitting">The rows the widget's size shows (used when the settings say "as many as fit").</param>
	/// <param name="locate">The device position when one is known (nearby widgets and "my location").</param>
	public async Task<WidgetSnapshot> LoadAsync(
		WidgetConfig config,
		int fitting,
		Func<(double Latitude, double Longitude, DateTimeOffset At)?> locate,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(locate);

		WidgetStrings strings = LocalizationService.Current.CurrentStrings.Widgets;

		using IDisposable scope = providers.Override(config.ProviderId);

		int wanted = Wanted(config, fitting);

		WidgetSnapshot snapshot =
			config.Kind switch
			{
				WidgetKind.Route => await RouteAsync(config, wanted, locate, strings, cancellationToken).ConfigureAwait(false),
				WidgetKind.Departures => await BoardAsync(config, wanted, arrival: false, strings, cancellationToken).ConfigureAwait(false),
				WidgetKind.Arrivals => await BoardAsync(config, wanted, arrival: true, strings, cancellationToken).ConfigureAwait(false),
				WidgetKind.NearbyStops => await NearbyAsync(config, wanted, locate, strings, withDepartures: false, cancellationToken).ConfigureAwait(false),
				_ => await NearbyAsync(config, wanted, locate, strings, withDepartures: true, cancellationToken).ConfigureAwait(false)
			};

		return snapshot with { Requested = wanted };
	}

	// ---------- Route ----------

	private async Task<WidgetSnapshot> RouteAsync(
		WidgetConfig config,
		int wanted,
		Func<(double Latitude, double Longitude, DateTimeOffset At)?> locate,
		WidgetStrings strings,
		CancellationToken cancellationToken)
	{
		IUiStrings all = LocalizationService.Current.CurrentStrings;

		Location? from = await ResolveAsync(config.From, locate, cancellationToken).ConfigureAwait(false);
		Location? to = await ResolveAsync(config.To, locate, cancellationToken).ConfigureAwait(false);

		string title =
			$"{Name(config.From, strings)} → {Name(config.To, strings)}";

		if (from is null || to is null)
		{
			return new WidgetSnapshot
			{
				Title = title,
				Message = strings.NeedsLocation
			};
		}

		JourneyResult result =
			await journeys
				.SearchAsync(
					new JourneyQuery
					{
						From = from,
						To = to,
						DateTime = DateTimeOffset.UtcNow,
						SearchMode = JourneySearchMode.Departure,
						MaxResults = Math.Clamp(wanted + Spare, 1, 10),
						Routing = RoutingPreferences.Default,
						TimeoutSeconds = (int)Timeout.TotalSeconds
					},
					cancellationToken)
				.ConfigureAwait(false);

		if (!result.HasJourneys)
		{
			if (result.Outcome is JourneyOutcome.Empty)
			{
				return new WidgetSnapshot
				{
					Title = title,
					UpdatedAt = DateTimeOffset.UtcNow,
					Message = strings.NoJourneys
				};
			}

			throw new InvalidOperationException(result.ErrorMessage ?? "Journey search failed.");
		}

		return new WidgetSnapshot
		{
			Title = title,
			UpdatedAt = DateTimeOffset.UtcNow,
			Rows = (List<WidgetRow>)[.. result.Journeys.Select(journey => WidgetSnapshots.ForJourney(journey, all))]
		};
	}

	private static string Name(WidgetPlace? place, WidgetStrings strings) =>
		place is { IsHere: true }
			? strings.Here
			: place?.Place?.Name ?? string.Empty;

	/// <summary>A chosen place as it is; "my location" as the stop nearest to the last known position.</summary>
	private async Task<Location?> ResolveAsync(
		WidgetPlace? place,
		Func<(double Latitude, double Longitude, DateTimeOffset At)?> locate,
		CancellationToken cancellationToken)
	{
		if (place is null)
		{
			return null;
		}

		if (!place.IsHere)
		{
			return place.Place;
		}

		if (locate() is not { } here)
		{
			return null;
		}

		IReadOnlyList<Location> stops =
			await locations
				.SearchByCoordinatesAsync(here.Latitude, here.Longitude, Timeout, cancellationToken)
				.ConfigureAwait(false);

		return stops.Count > 0 ? stops[0] : null;
	}

	// ---------- Departures and arrivals of one stop ----------

	private async Task<WidgetSnapshot> BoardAsync(
		WidgetConfig config,
		int wanted,
		bool arrival,
		WidgetStrings strings,
		CancellationToken cancellationToken)
	{
		IUiStrings all = LocalizationService.Current.CurrentStrings;

		if (config.Stop is not { } stop)
		{
			return new WidgetSnapshot
			{
				Message = strings.SetUp
			};
		}

		IReadOnlyList<Departure> list =
			await DeparturesOfAsync(stop, arrival, config, wanted + Spare, cancellationToken).ConfigureAwait(false);

		return new WidgetSnapshot
		{
			Title = stop.Name,
			UpdatedAt = DateTimeOffset.UtcNow,
			Message = list.Count == 0 ? (arrival ? strings.NoArrivals : strings.NoDepartures) : string.Empty,
			Rows = (List<WidgetRow>)[.. list.Select(departure => WidgetSnapshots.ForDeparture(departure, all))]
		};
	}

	private async Task<IReadOnlyList<Departure>> DeparturesOfAsync(
		Location stop,
		bool arrival,
		WidgetConfig config,
		int limit,
		CancellationToken cancellationToken)
	{
		DepartureBoard board =
			await departures
				.GetDeparturesAsync(
					new DepartureQuery
					{
						Stop = stop,
						IsArrival = arrival,
						Limit = limit * (config.LineFilter.Count > 0 ? 3 : 1),
						Modes = config.Modes,
						TimeoutSeconds = (int)Timeout.TotalSeconds
					},
					cancellationToken)
				.ConfigureAwait(false);

		IReadOnlyList<string> lines = config.LineFilter;

		return
			(List<Departure>)
			[.. board.Departures
				.Where(
					departure => lines.Count == 0
						|| lines.Contains(departure.Line.Name, StringComparer.OrdinalIgnoreCase))
				.Take(limit)];
	}

	// ---------- Around the device ----------

	private async Task<WidgetSnapshot> NearbyAsync(
		WidgetConfig config,
		int wanted,
		Func<(double Latitude, double Longitude, DateTimeOffset At)?> locate,
		WidgetStrings strings,
		bool withDepartures,
		CancellationToken cancellationToken)
	{
		IUiStrings all = LocalizationService.Current.CurrentStrings;

		string title = withDepartures ? strings.NameNearbyDepartures : strings.NameNearby;

		if (locate() is not { } here)
		{
			return new WidgetSnapshot
			{
				Title = title,
				Message = strings.NeedsLocation
			};
		}

		IReadOnlyList<NearbyStop> stops =
			await StopsAroundAsync(here.Latitude, here.Longitude, config.RadiusMeters, cancellationToken).ConfigureAwait(false);

		if (stops.Count == 0)
		{
			return new WidgetSnapshot
			{
				Title = title,
				UpdatedAt = DateTimeOffset.UtcNow,
				Message = string.Format(CultureInfo.CurrentCulture, strings.NoStopsNearby, config.RadiusMeters)
			};
		}

		if (!withDepartures)
		{
			return new WidgetSnapshot
			{
				Title = title,
				UpdatedAt = DateTimeOffset.UtcNow,
				Rows = (List<WidgetRow>)[.. stops.Take(wanted).Select(stop => WidgetSnapshots.ForStop(stop, strings))]
			};
		}

		// The nearest stops, each with its next departures (a few at a time: the provider is polite to its own servers).
		NearbyStop[] chosen = [.. stops.Take(config.StopCount)];

		var boards = new IReadOnlyList<Departure>[chosen.Length];

		using var gate = new SemaphoreSlim(3);

		await Task.WhenAll(
			chosen.Select(
				async (stop, index) =>
				{
					await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

					try
					{
						boards[index] =
							await DeparturesOfAsync(stop.Stop, arrival: false, config, config.PerStop, cancellationToken)
								.ConfigureAwait(false);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						DiagnosticLog.Write($"Widget: departures of {stop.Stop.Name} failed: {ex.Message}");

						boards[index] = (List<Departure>)[];
					}
					finally
					{
						gate.Release();
					}
				})).ConfigureAwait(false);

		var rows = new List<WidgetRow>();

		for (int index = 0; index < chosen.Length; index++)
		{
			if (boards[index].Count == 0)
			{
				continue;
			}

			rows.Add(WidgetSnapshots.ForHeader(chosen[index], strings));
			rows.AddRange(boards[index].Select(departure => WidgetSnapshots.ForDeparture(departure, all)));
		}

		return new WidgetSnapshot
		{
			Title = title,
			UpdatedAt = DateTimeOffset.UtcNow,
			Message = rows.Count == 0 ? strings.NoDepartures : string.Empty,
			Rows = rows
		};
	}

	/// <summary>The provider's own nearby list where it has one, else the stop search around the position.</summary>
	private async Task<IReadOnlyList<NearbyStop>> StopsAroundAsync(
		double latitude,
		double longitude,
		int radius,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<NearbyStop> found =
			await network
				.GetNearbyStopsAsync(latitude, longitude, radius, cancellationToken)
				.ConfigureAwait(false);

		if (found.Count == 0)
		{
			IReadOnlyList<Location> stops =
				await locations
					.SearchByCoordinatesAsync(latitude, longitude, Timeout, cancellationToken)
					.ConfigureAwait(false);

			found =
				(List<NearbyStop>)
				[.. stops
					.Select(
						stop => new NearbyStop
						{
							Stop = stop,
							DistanceMeters =
								stop.Latitude is { } lat && stop.Longitude is { } lon
									? (int)Math.Round(GeoMath.DistanceMeters(latitude, longitude, lat, lon))
									: 0
						})
					.Where(stop => stop.DistanceMeters <= radius)];
		}

		return (List<NearbyStop>)[.. found.OrderBy(stop => stop.DistanceMeters)];
	}
}
