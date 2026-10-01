using CommunityToolkit.Maui;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Pages;
using DDjourneys.Support;
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

#if WINDOWS && DEBUG
		InstallLabelContainerDiagnostics();
#endif

		var settings = new AppSettings();

		LocalizationInitializer.Initialize(settings);

		builder.Services.AddSingleton(settings);

		// Core services.
		builder.Services.AddSingleton<ApiClient>();
		builder.Services.AddSingleton<VvoApiClient>();

		builder.Services.AddSingleton<IJourneyProvider, VvoJourneyProvider>();

		builder.Services.AddSingleton<JourneyProviderDiagnostics>();
		builder.Services.AddSingleton<JourneyService>();

		builder.Services.AddSingleton<ILocationProvider, VvoLocationProvider>();
		builder.Services.AddSingleton<LocationService>();

		builder.Services.AddSingleton<PlaceStore>();

		// Pages are transient; view models are added with their pages.
		builder.Services.AddTransient<PlanPage>();
		builder.Services.AddTransient<PlanViewModel>();

		builder.Services.AddTransient<PlaceSearchPage>();
		builder.Services.AddTransient<PlaceSearchViewModel>();

		builder.Services.AddTransient<ResultsPage>();
		builder.Services.AddTransient<ResultsViewModel>();

		builder.Services.AddTransient<JourneyPage>();
		builder.Services.AddTransient<JourneyViewModel>();

		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<ThemesPage>();
		builder.Services.AddTransient<ThemesViewModel>();

		return builder.Build();
	}

#if WINDOWS && DEBUG
	private static void InstallLabelContainerDiagnostics()
	{
		foreach (string key in new[]
		{
			nameof(IView.Background),
			nameof(IView.Clip),
			nameof(IView.Shadow)
		})
		{
			Microsoft.Maui.Handlers.LabelHandler.Mapper.PrependToMapping(
				key,
				(handler, view) =>
				{
					if (handler.PlatformView is Microsoft.UI.Xaml.FrameworkElement
						{
							Parent: { } parent
						}
						&& parent is not Microsoft.UI.Xaml.Controls.Panel)
					{
						System.Diagnostics.Debug.WriteLine(
							$"[DIAG] Label '{(view as Label)?.Text}': " +
							$"{key} mapped while its TextBlock is parented by " +
							$"{parent.GetType().Name}");
					}
				});
		}
	}
#endif
}