using Microsoft.Extensions.Logging;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys
{
	public static class MauiProgram
	{
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				});

#if DEBUG
			builder.Logging.AddDebug();
#endif

			builder.Services.AddDDjourneysProviders();
			builder.Services.AddSingleton<MainPage>();
			return builder.Build();
		}
	}
}
