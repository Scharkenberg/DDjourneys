using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Guardian;

public static class GuardianJourneyMapper
{
	public static GuardianJourney Map(
		Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		var episodes =
			new List<GuardianEpisode>();

		for (int i = 0;
			i < journey.Legs.Count;
			i++)
		{
			JourneyLeg leg =
				journey.Legs[i];

			episodes.Add(
				MapLeg(
					leg));

			// A transfer belongs between this leg and the next one.
			JourneyTransfer? transfer =
				journey.Transfers
					.FirstOrDefault(
						t => t.PreviousLegIndex == i);

			if (transfer is not null
				&& i < journey.Legs.Count - 1)
			{
				episodes.Add(
					MapTransfer(
						journey,
						leg,
						journey.Legs[i + 1],
						transfer));
			}
		}

		return new GuardianJourney
		{
			Episodes =
				episodes
		};
	}


	private static GuardianEpisode MapLeg(
		JourneyLeg leg)
	{
		bool individual =
			leg.Mode == TransitMode.Walk
			|| leg.Mode == TransitMode.Taxi;


		return new GuardianEpisode
		{
			Type =
				individual
					? "individual"
					: "public",

			Id =
				leg.Id
				?? string.Empty,

			Mot =
				new GuardianMot
				{
					Type =
						MapMotType(
							leg.Mode),

					Name =
						leg.Line?.Name
						?? string.Empty,

					Direction =
						leg.Line?.Destination
						?? string.Empty
				},

			From =
				MapStation(
					leg.From,
					leg.EffectiveDeparture,
					includePlatform: !individual),

			To =
				MapStation(
					leg.To,
					leg.EffectiveArrival,
					includePlatform: !individual),

			AllStations =
				leg.Stops
					.Select(
						stop =>
							MapStation(
								stop.Station,
								stop.EffectiveDeparture
									?? stop.EffectiveArrival,
								includePlatform: !individual))
					.ToArray(),

			Polyline =
				MapPath(
					leg.Path),

			DurationSeconds =
				individual
					? (long)
						Math.Max(
							0,
							(
								leg.EffectiveArrival
								- leg.EffectiveDeparture
							)?.TotalSeconds
							?? 0)
					: null
		};
	}


	private static GuardianEpisode MapTransfer(
		Journey journey,
		JourneyLeg previousLeg,
		JourneyLeg nextLeg,
		JourneyTransfer transfer)
	{
		// Use the actual surrounding journey locations here.
		// The Footpath itself is represented by transfer.Path.
		Station from =
			previousLeg.To;

		Station to =
			nextLeg.From;

		return new GuardianEpisode
		{
			Type =
				"individual",

			Id =
				string.Empty,

			Mot =
				new GuardianMot
				{
					Type =
						"Walking",

					Name =
						"Fussweg",

					Direction =
						string.Empty
				},

			From =
				MapStation(
					from,
					previousLeg.EffectiveArrival,
					includePlatform: false),

			To =
				MapStation(
					to,
					nextLeg.EffectiveDeparture,
					includePlatform: false),

			AllStations =
				new[]
				{
					MapStation(
						from,
						previousLeg.EffectiveArrival,
						includePlatform: false),

					MapStation(
						to,
						nextLeg.EffectiveDeparture,
						includePlatform: false)
				},

			Polyline =
				MapPath(
					transfer.Path),

			DurationSeconds =
				(long)
				Math.Max(
					0,
					transfer.Duration.TotalSeconds)
		};
	}


	private static GuardianStation MapStation(
		Station station,
		DateTimeOffset? time,
		bool includePlatform)
	{
		GuardianPlatform? platform = null;

		if (includePlatform
			&& !string.IsNullOrWhiteSpace(
				station.Platform))
		{
			platform =
				new GuardianPlatform
				{
					Type =
						"Gleis",

					Name =
						station.Platform
				};
		}

		return
			new GuardianStation
			{
				Name =
					station.Name,

				Id =
					station.Id,

				Coords =
					new GuardianCoordinates
					{
						Lat =
							station.Latitude ?? 0,

						Lon =
							station.Longitude ?? 0,

						Projection =
							"WGS84"
					},

				ScheduledTime =
					time?.ToUnixTimeMilliseconds()
					?? 0,

				Platform =
					platform
			};
	}

	private static GuardianCoordinates[] MapPath(
		IReadOnlyList<(double Latitude, double Longitude)> path)
	{
		return path
			.Select(
				point =>
					new GuardianCoordinates
					{
						Lat =
							point.Latitude,

						Lon =
							point.Longitude,

						Projection =
							"WGS84"
					})
			.ToArray();
	}


	private static string MapMotType(
		TransitMode mode)
	{
		return mode switch
		{
			TransitMode.Tram =>
				"Tram",

			TransitMode.Bus =>
				"Bus",

			TransitMode.SuburbanRail =>
				"SuburbanRailway",

			TransitMode.RegionalTrain =>
				"Train",

			TransitMode.Ferry =>
				"Ferry",

			TransitMode.Taxi =>
				"Taxi",

			_ =>
				"Any"
		};
	}
}