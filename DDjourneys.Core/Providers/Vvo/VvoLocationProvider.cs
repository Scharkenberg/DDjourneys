using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Provides location search using the VVO WebAPI.
/// </summary>
public sealed class VvoLocationProvider : ILocationProvider, IProviderDescriptor
{
	private readonly VvoApiClient _apiClient;


	/// <inheritdoc />
	public ProviderInfo Info =>
		VvoProviderInfo.Value;


	public VvoLocationProvider(
		VvoApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}


	/// <inheritdoc />
	public Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		SearchAsync(
			query,
			PlaceKinds.Stops,
			cancellationToken,
			timeout);


	/// <summary>
	/// PointFinder with <c>stopsOnly=false</c> also finds addresses and points of interest; the router
	/// takes their ids as origin and destination and plans the walk to the nearest stop itself.
	/// </summary>
	public async Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		PlaceKinds kinds,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		kinds |= PlaceKinds.Stops;

		bool places =
			kinds.HasFlag(PlaceKinds.Addresses)
			|| kinds.HasFlag(PlaceKinds.Pois);

		VvoPointResponse? response =
			await _apiClient.FindPointsAsync(
				query,
				new VvoPointFinderOptions
				{
					StopsOnly = !places
				},
				cancellationToken,
				timeout)
			.ConfigureAwait(false);

		if (response is null)
		{
			return Array.Empty<Location>();
		}

		cancellationToken.ThrowIfCancellationRequested();

		// Points parses its raw entries on every access.
		IReadOnlyList<VvoPoint> points =
			response.Points;

		return points
			.Where(point => !string.IsNullOrWhiteSpace(point.Id) && !string.IsNullOrWhiteSpace(point.Name))
			.Where(point => kinds.HasFlag(point.Kind.ToFlag()))
			.Select(Map)
			.ToArray();
	}


	/// <inheritdoc />
	public async Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		if (!VvoCoordinateConverter.TryToGk4(
			latitude,
			longitude,
			out (double Easting, double Northing) gk4))
		{
			return Array.Empty<Location>();
		}

		VvoPointResponse? response =
			await _apiClient.FindPointsByCoordinatesAsync(
				gk4.Easting,
				gk4.Northing,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		if (response is null)
		{
			return Array.Empty<Location>();
		}

		cancellationToken.ThrowIfCancellationRequested();

		// Stops only (the VVO router has no use for a bare coordinate), nearest first; a stop
		// without coordinates cannot be ranked and goes last.
		return response.Points
			.Where(point => point.IsStop && !string.IsNullOrWhiteSpace(point.Name))
			.Select(Map)
			.OrderBy(stop => DistanceSquared(stop, latitude, longitude))
			.ToArray();
	}


	/// <inheritdoc />
	public async Task<Location?> ResolveAddressAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		if (!VvoCoordinateConverter.TryToGk4(
			latitude,
			longitude,
			out (double Easting, double Northing) gk4))
		{
			return null;
		}

		VvoPointResponse? response =
			await _apiClient.FindPointsByCoordinatesAsync(
				gk4.Easting,
				gk4.Northing,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		cancellationToken.ThrowIfCancellationRequested();

		if (response is null)
		{
			DiagnosticLog.Write("[VVO position] no PointFinder response");

			return null;
		}

		IReadOnlyList<VvoPoint> points =
			response.Points;

		DiagnosticLog.Write(
			$"[VVO position] {points.Count} entries: "
			+ string.Join(" ; ", points.Take(5).Select(point => $"{point.Kind}:{point.Id}={point.Name}")));

		// The first entry of a coordinate query is the position itself, named after the street
		// address there ("coord:4621020:504065:NAV4:Nöthnitzer Straße 46", type "c"). Entries with an
		// address id come second; stops and points of interest are not "where the user is".
		VvoPoint? best =
			points.FirstOrDefault(
				point => point.Kind == PlaceKind.Coordinate && !string.IsNullOrWhiteSpace(point.Name))
			?? points
				.Where(point => point.Kind == PlaceKind.Address && !string.IsNullOrWhiteSpace(point.Name))
				.OrderBy(point => DistanceSquared(Map(point), latitude, longitude))
				.FirstOrDefault();

		return best is null
			? null
			: Map(best);
	}


	// Equirectangular approximation: only the order matters, and distances here are a few hundred metres.
	private static double DistanceSquared(
		Location stop,
		double latitude,
		double longitude)
	{
		if (stop.Latitude is not { } stopLatitude
			|| stop.Longitude is not { } stopLongitude)
		{
			return double.MaxValue;
		}

		double dy = stopLatitude - latitude;
		double dx = (stopLongitude - longitude) * Math.Cos(latitude * Math.PI / 180);

		return (dx * dx) + (dy * dy);
	}


	/// <summary>
	/// Maps one PointFinder entry. The geometry the API returned is kept (converted to WGS84); a point
	/// without usable coordinates (the API sends "0" for some entries) simply has none.
	/// </summary>
	public static Location Map(
		VvoPoint point)
	{
		ArgumentNullException.ThrowIfNull(point);

		bool hasCoordinates =
			VvoCoordinateConverter.TryFromPointFields(
				point.Coordinate1,
				point.Coordinate2,
				out (double Latitude, double Longitude) coordinates);

		return new Location
		{
			Id = point.Id,
			ProviderId = VvoProviderInfo.Id,
			Name = point.Name ?? string.Empty,
			Kind = point.Kind,
			Place = point.Place,
			Latitude = hasCoordinates ? coordinates.Latitude : null,
			Longitude = hasCoordinates ? coordinates.Longitude : null
		};
	}
}
