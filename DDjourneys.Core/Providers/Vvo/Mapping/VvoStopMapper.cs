using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Stops and places: VVO stops and searched locations to <see cref="Station"/>/<see cref="StopTime"/>, incl. coordinates.
/// </summary>
public static class VvoStopMapper
{
	public static Station? ToStation(
		Location? location) =>
		location is null
			? null
			: new Station
			{
				Id =
					location.Id
					?? string.Empty,

				ProviderId =
					string.IsNullOrWhiteSpace(location.ProviderId)
						? VvoProviderInfo.Id
						: location.ProviderId,

				Name =
					location.Name,

				Place =
					location.Place,

				Latitude =
					location.Latitude,

				Longitude =
					location.Longitude
			};

	/// <summary>
	/// A searched location (suburb, address, point of interest) often comes without a city.
	/// The stop it leads to is at most a short walk away, so that stop's city is used instead.
	/// </summary>
	public static Station? WithPlaceFrom(
		Station? searched,
		Station adjacentStop)
	{
		if (searched is null
			|| !string.IsNullOrWhiteSpace(searched.Place)
			|| string.IsNullOrWhiteSpace(adjacentStop.Place))
		{
			return searched;
		}

		return new Station
		{
			Id =
				searched.Id,

			ProviderId =
				searched.ProviderId,

			Name =
				searched.Name,

			Place =
				adjacentStop.Place,

			Latitude =
				searched.Latitude,

			Longitude =
				searched.Longitude
		};
	}

	public static Station? GetFirstStation(
		VvoPartialRoute route)
	{
		return route.RegularStops
			.Select(MapStop)
			.FirstOrDefault()
			?.Station;
	}

	public static StopTime MapStop(
		VvoStop stop)
	{
		(double? latitude, double? longitude) = MapCoordinates(stop);

		return new StopTime
		{
			Station =
	new Station
	{
		Id =
			stop.DataId
			?? string.Empty,

		ProviderId =
			VvoProviderInfo.Id,

		Name =
			stop.Name
			?? string.Empty,

		Place =
			VvoPlaces.Resolve(stop.Place),

		Latitude =
			latitude,

		Longitude =
			longitude,

		Platform =
			stop.Platform?.Name,

		PlatformKind =
			StopLabel.KindOf(
				stop.Platform?.Type)
	},

			ScheduledArrival =
				stop.ArrivalTime,

			RealtimeArrival =
				stop.ArrivalRealTime,

			ScheduledDeparture =
				stop.DepartureTime,

			RealtimeDeparture =
				stop.DepartureRealTime,

			Platform =
				stop.Platform?.Name,

			PlatformKind =
				StopLabel.KindOf(
					stop.Platform?.Type),

			ProviderData =
				stop,

			IsCancelled =
				VvoStopStates.IsCancelled(stop.ArrivalState)
				|| VvoStopStates.IsCancelled(stop.DepartureState),

			IsArrivalCancelled =
				VvoStopStates.IsCancelled(stop.ArrivalState),

			IsDepartureCancelled =
				VvoStopStates.IsCancelled(stop.DepartureState),

			Occupancy =
				VvoModeMapper.MapOccupancy(
					stop.Occupancy)
		};
	}

	public static (
	double? Latitude,
	double? Longitude)
MapCoordinates(
	VvoStop stop)
	{
		if (stop.Latitude <= 0
			|| stop.Longitude <= 0)
		{
			return (null, null);
		}

		(double latitude, double longitude) =
			VvoCoordinateConverter.FromGk4(
				stop.Longitude,
				stop.Latitude);

		return (
			Latitude: latitude,
			Longitude: longitude);
	}
}
