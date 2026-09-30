using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DDjourneys.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
	private static readonly Lazy<LocalizationService> _instance =
		new(() => new LocalizationService());

	public static LocalizationService Current => _instance.Value;

	private readonly Dictionary<string, LocalizationPack> _packs =
		new(StringComparer.OrdinalIgnoreCase);

	private LocalizationPack? _current;
	private string _languageCode = string.Empty;

	private LocalizationService()
	{
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public IReadOnlyList<LocalizationPack> AvailableLanguages =>
		_packs.Values
			.OrderBy(pack => pack.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();

	public string LanguageCode
	{
		get => _languageCode;
		private set
		{
			if (_languageCode == value)
			{
				return;
			}

			_languageCode = value;
			OnPropertyChanged();
		}
	}

	public LocalizationPack CurrentLanguagePack =>
		_current ?? throw new InvalidOperationException("No UI language has been selected.");

	public IUiStrings CurrentStrings => CurrentLanguagePack.Strings;

	public void Register(LocalizationPack pack)
	{
		ArgumentNullException.ThrowIfNull(pack);
		ArgumentException.ThrowIfNullOrWhiteSpace(pack.Code);
		ArgumentException.ThrowIfNullOrWhiteSpace(pack.DisplayName);
		ArgumentNullException.ThrowIfNull(pack.Strings);

		string code = Normalize(pack.Code);
		_packs[code] = pack;

		OnPropertyChanged(nameof(AvailableLanguages));
	}

	public bool Contains(string languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return false;
		}

		return _packs.ContainsKey(Normalize(languageCode));
	}

	public void SetLanguage(string languageCode)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

		LocalizationPack pack = ResolvePack(languageCode);

		_current = pack;
		LanguageCode = pack.Code;

		CultureInfo culture = CultureInfo.GetCultureInfo(pack.Code);

		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;

		OnPropertyChanged(nameof(CurrentLanguagePack));
		OnPropertyChanged(nameof(CurrentStrings));
		OnPropertyChanged(nameof(AvailableLanguages));
		OnPropertyChanged(string.Empty);
	}

	public bool TrySetLanguage(string languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return false;
		}

		try
		{
			SetLanguage(languageCode);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private LocalizationPack ResolvePack(string languageCode)
	{
		string normalized = Normalize(languageCode);

		if (_packs.TryGetValue(normalized, out LocalizationPack? exact))
		{
			return exact;
		}

		string neutral = CultureInfo.GetCultureInfo(normalized).TwoLetterISOLanguageName;

		if (_packs.TryGetValue(neutral, out LocalizationPack? neutralPack))
		{
			return neutralPack;
		}

		if (_packs.Count > 0)
		{
			return _packs.Values
				.OrderBy(pack => pack.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.First();
		}

		throw new InvalidOperationException("No UI languages have been registered.");
	}

	private static string Normalize(string languageCode) =>
		CultureInfo.GetCultureInfo(languageCode).Name;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}