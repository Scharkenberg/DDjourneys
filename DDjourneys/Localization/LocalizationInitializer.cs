using DDjourneys.Support;

namespace DDjourneys.Localization;

public static class LocalizationInitializer
{
	public static void Initialize(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		LocalizationService localization = LocalizationService.Current;

		localization.Register(
			new LocalizationPack(
				"en",
				"English",
				new EnglishUiStrings()));

		localization.Register(
			new LocalizationPack(
				"de",
				"Deutsch",
				new GermanUiStrings()));

		if (!localization.TrySetLanguage(settings.LanguageCode))
		{
			localization.SetLanguage("en");
		}
	}
}