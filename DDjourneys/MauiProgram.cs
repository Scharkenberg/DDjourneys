using CommunityToolkit.Maui;
using DDjourneys.Core.Providers;
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

		builder.Services.AddSingleton<AppSettings>();

		// Core services (providers, journey and location services).
		builder.Services.AddDDjourneysProviders();

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

		return builder.Build();
	}

#if WINDOWS && DEBUG
	/// <summary>
	/// Debug aid for WinUI's "No installed components were detected" (0x800F1000).
	/// MAUI wraps a Label's TextBlock in a container when it gets a Background, Clip or Shadow.
	/// If the TextBlock already sits inside something that is not a Panel, WinUI refuses.
	/// This logs exactly that situation, with the label text, before the crash happens.
	/// Remove once the crash is confirmed gone.
	/// </summary>
	private static void InstallLabelContainerDiagnostics()
	{
		foreach (string key in new[] { nameof(IView.Background), nameof(IView.Clip), nameof(IView.Shadow) })
		{
			Microsoft.Maui.Handlers.LabelHandler.Mapper.PrependToMapping(key, (handler, view) =>
			{
				if (handler.PlatformView is Microsoft.UI.Xaml.FrameworkElement { Parent: { } parent }
					&& parent is not Microsoft.UI.Xaml.Controls.Panel)
				{
					System.Diagnostics.Debug.WriteLine(
						$"[DIAG] Label '{(view as Label)?.Text}': {key} mapped while its TextBlock is parented by {parent.GetType().Name}");
				}
			});
		}
	}
#endif
}