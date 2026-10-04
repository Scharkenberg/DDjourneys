using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
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
	public async Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);


		VvoPointResponse? response =
			await _apiClient.FindPointsAsync(
				query,
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
			.Select(Map)
			.ToArray();
	}


	/// <summary>
	/// Searches for locations near the given coordinates using the VVO WebAPI.
	/// </summary>
	public async Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		VvoPointResponse? response =
			await _apiClient.FindPointsByCoordinatesAsync(
				latitude,
				longitude,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		if (response is null)
		{
			return Array.Empty<Location>();
		}

		cancellationToken.ThrowIfCancellationRequested();

		IReadOnlyList<VvoPoint> points = response.Points;

		return points
			.Where(point => !string.IsNullOrWhiteSpace(point.Name))
			.Select(Map)
			.ToArray();
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
			Place = point.Place,
			Latitude = hasCoordinates ? coordinates.Latitude : null,
			Longitude = hasCoordinates ? coordinates.Longitude : null
		};
	}
}
