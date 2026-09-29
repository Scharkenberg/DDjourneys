using CommunityToolkit.Maui;
using DDjourneys.Core.Providers;
using DDjourneys.Pages;
using Microsoft.Extensions.Logging;

namespace DDjourneys;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Core services (providers, journey and location services).
		builder.Services.AddDDjourneysProviders();

		// Pages are transient; view models are added with their pages.
		builder.Services.AddTransient<PlanPage>();
		builder.Services.AddTransient<PlanViewModel>();

		return builder.Build();
	}
}
