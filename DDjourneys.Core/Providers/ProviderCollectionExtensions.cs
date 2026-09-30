using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Services;
using DDjourneys.Core.Storage;

namespace DDjourneys.Core.Providers;

/// <summary>
/// Extension methods for registering journey providers.
/// </summary>
public static class ProviderCollectionExtensions
{
	public static IServiceCollection AddDDjourneysProviders(
		this IServiceCollection services)
	{
		services.AddSingleton<VvoApiClient>();

		services.AddSingleton<IJourneyProvider,	VvoJourneyProvider>();

		services.AddSingleton<JourneyProviderDiagnostics>();
		services.AddSingleton<JourneyService>();
		services.AddSingleton<ILocationProvider, VvoLocationProvider>();
		services.AddSingleton<LocationService>();
		services.AddSingleton<PlaceStore>();
		services.AddSingleton<ApiClient>();

		return services;
	}
}
