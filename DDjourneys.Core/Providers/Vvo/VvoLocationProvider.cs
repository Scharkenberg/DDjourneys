using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
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


		if (response?.Points is null)
		{
			return Array.Empty<Location>();
		}
		cancellationToken.ThrowIfCancellationRequested();


		return response.Points
			.Where(point => !string.IsNullOrWhiteSpace(point.Id) && !string.IsNullOrWhiteSpace(point.Name))
			.Select(Map)
			.ToArray();
	}


	private static Location Map(
		VvoPoint point)
	{
		return new Location
		{
			Id = point.Id,
			Name = point.Name ?? string.Empty,
			Place = point.Place
		};
	}
}
