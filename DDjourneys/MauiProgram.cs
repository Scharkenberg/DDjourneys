using CommunityToolkit.Maui;
using DDjourneys.Contract;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Services;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Localization;
using DDjourneys.Pages;
using DDjourneys.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

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
				// Inter Tight as two static instances (MAUI cannot select weights from a variable font).
				fonts.AddFont("InterTight-Regular.ttf", "InterTightRegular");
				fonts.AddFont("InterTight-SemiBold.ttf", "InterTightSemiBold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

#if WINDOWS && DEBUG
		InstallLabelContainerDiagnostics();
#endif

#if ANDROID
		Platforms.Android.NativeStyling.Install();
#endif

		// Versioned on-device storage: bring it up to date before anything reads it.
		AppStorage.Upgrade();

		var settings = new AppSettings();

		LocalizationInitializer.Initialize(settings);

		builder.Services.AddSingleton(settings);

		// Core services.
		builder.Services.AddSingleton<ApiClient>();
		builder.Services.AddSingleton<VvoApiClient>();

		// Providers: every provider registers its description; the registry holds the user's choice.
		builder.Services.AddSingleton(VvoProviderInfo.Value);
		builder.Services.AddSingleton(
			services => new ProviderRegistry(
				services.GetServices<ProviderInfo>(),
				() => settings.ProviderId,
				id => settings.ProviderId = id));

		builder.Services.AddSingleton<VvoJourneyProvider>();
		builder.Services.AddSingleton<IJourneyProvider>(
			services => services.GetRequiredService<VvoJourneyProvider>());

		builder.Services.AddSingleton<JourneyProviderDiagnostics>();
		builder.Services.AddSingleton<JourneyService>();

		builder.Services.AddSingleton<ILocationProvider, VvoLocationProvider>();
		builder.Services.AddSingleton<IGeolocation, CommunityToolkit.Maui.Geolocation.Geolocation>();

		// Journey tracking is platform-agnostic; a platform only contributes how live state is shown,
		// how polling is kept alive and how notification permission works. Platforms without their
		// own implementations track in-app (the overview page shows everything).
#if ANDROID
		builder.Services.AddSingleton<ILiveJourneySurface, Platforms.Android.LiveJourney.AndroidLiveJourneySurface>();
		builder.Services.AddSingleton<ITrackingRuntime, Platforms.Android.LiveJourney.AndroidTrackingRuntime>();
		builder.Services.AddSingleton<INotificationAccess, Platforms.Android.LiveJourney.AndroidNotificationAccess>();
#elif WINDOWS
		builder.Services.AddSingleton<ILiveJourneySurface, Platforms.Windows.LiveJourney.WindowsLiveJourneySurface>();
		builder.Services.AddSingleton<ITrackingRuntime, InProcessTrackingRuntime>();
		builder.Services.AddSingleton<INotificationAccess, Platforms.Windows.LiveJourney.WindowsNotificationAccess>();
#else
		builder.Services.AddSingleton<ILiveJourneySurface, NoLiveJourneySurface>();
		builder.Services.AddSingleton<ITrackingRuntime, InProcessTrackingRuntime>();
		builder.Services.AddSingleton<INotificationAccess, UnrestrictedNotificationAccess>();
#endif
		builder.Services.AddSingleton<TrackingCallbackBridge>();
		builder.Services.AddSingleton<Tracking.TrackedJourneyNavigator>();

		// Schutzengel is the DVB/VVO service, so it always requeries through the VVO provider.
		// (Platforms.Windows.NoOpJourneyTracker remains available to switch tracking off.)
		builder.Services.AddSingleton<IJourneyTracker>(
			services => new Tracking.Schutzengel.SchutzengelJourneyTracker(
				services.GetRequiredService<VvoJourneyProvider>(),
				services.GetRequiredService<TrackingCallbackBridge>(),
				services.GetRequiredService<ILiveJourneySurface>(),
				services.GetRequiredService<ITrackingRuntime>(),
				services.GetRequiredService<INotificationAccess>(),
				defaultLeadMinutes: () => services.GetRequiredService<AppSettings>().DefaultLeadMinutes));

		builder.Services.AddSingleton(
			services => new Lazy<IJourneyTracker>(() => services.GetRequiredService<IJourneyTracker>()));

		// External contract (links, intents, protocol launches): see docs/EXTERNAL_CONTRACT.md.
		builder.Services.AddSingleton<ICallbackLauncher, PlatformCallbackLauncher>();
		builder.Services.AddSingleton<ContractResponder>();
		builder.Services.AddSingleton<ContractSession>();
		builder.Services.AddSingleton<ContractPlaceResolver>();
		builder.Services.AddSingleton<ContractInbox>();

#if WINDOWS
		builder.ConfigureLifecycleEvents(
			events => events.AddWindows(
				windows => windows.OnAppInstanceActivated(
					Platforms.Windows.WindowsContractActivation.Handle)));
#endif

		builder.Services.AddSingleton<PlaceStore>();

		// Location service needs IGeolocation for GPS functionality
		builder.Services.AddSingleton<ILocationService>(
			services => new LocationService(
				[services.GetRequiredService<ILocationProvider>()],
				services.GetService<ProviderRegistry>(),
				services.GetRequiredService<IGeolocation>()));

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
		builder.Services.AddTransient<AppearancePage>();
		builder.Services.AddTransient<AppearanceViewModel>();

		builder.Services.AddTransient<TrackedJourneysPage>();
		builder.Services.AddTransient<TrackedJourneysViewModel>();

		builder.Services.AddTransient<ExpertPage>();
		builder.Services.AddTransient<ExpertViewModel>();

		builder.Services.AddTransient<ProvidersPage>();
		builder.Services.AddTransient<ProvidersViewModel>();

		builder.Services.AddTransient<RoutingSettingsPage>();
		builder.Services.AddTransient<RoutingSettingsViewModel>();

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
