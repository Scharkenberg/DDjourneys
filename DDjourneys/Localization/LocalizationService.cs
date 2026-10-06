using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Maui;

namespace DDjourneys.Localization;

public sealed partial class LocalizationService : INotifyPropertyChanged
{
	private static readonly Lazy<LocalizationService> _instance =
		new(() => new LocalizationService());

	public static LocalizationService Current => _instance.Value;

	private readonly Dictionary<string, LocalizationPack> _packs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly WeakEventManager _weakEventManager = new();

	private LocalizationPack? _current;
	private string _languageCode = string.Empty;

	private LocalizationService()
	{
	}

	public event PropertyChangedEventHandler? PropertyChanged
	{
		add => _weakEventManager.AddEventHandler(value, nameof(PropertyChanged));
		remove => _weakEventManager.RemoveEventHandler(value, nameof(PropertyChanged));
	}

	public LocalizationPack[] AvailableLanguages =>
		[.. _packs.Values
			.OrderBy(
				pack => pack.DisplayName,
				StringComparer.CurrentCultureIgnoreCase)];

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
		_current ?? throw new InvalidOperationException(
			"No UI language has been selected.");

	public IUiStrings CurrentStrings =>
		CurrentLanguagePack.Strings;

	public void Register(LocalizationPack pack)
	{
		ArgumentNullException.ThrowIfNull(pack);
		ArgumentException.ThrowIfNullOrWhiteSpace(pack.Code);
		ArgumentException.ThrowIfNullOrWhiteSpace(pack.DisplayName);
		ArgumentNullException.ThrowIfNull(pack.Strings);

		string code = Normalize(pack.Code);

		_packs[code] = pack;

		if (_current is not null)
		{
			pack.IsSelected =
				string.Equals(
					pack.Code,
					_current.Code,
					StringComparison.OrdinalIgnoreCase);
		}

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

		if (_current is not null &&
			!ReferenceEquals(_current, pack))
		{
			_current.IsSelected = false;
		}

		_current = pack;
		_current.IsSelected = true;

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

		string neutral =
			CultureInfo.GetCultureInfo(normalized)
				.TwoLetterISOLanguageName;

		LocalizationPack? neutralPack = _packs.Values.FirstOrDefault(
			pack => string.Equals(
				CultureInfo.GetCultureInfo(pack.Code).TwoLetterISOLanguageName,
				neutral,
				StringComparison.OrdinalIgnoreCase));

		if (neutralPack is not null)
		{
			return neutralPack;
		}

		if (_packs.Count > 0)
		{
			return _packs.Values
				.OrderBy(
					pack => pack.DisplayName,
					StringComparer.CurrentCultureIgnoreCase)
				.First();
		}

		throw new InvalidOperationException(
			"No UI languages have been registered.");
	}

	private static string Normalize(string languageCode) =>
		CultureInfo.GetCultureInfo(languageCode).Name;

	private void OnPropertyChanged(
		[CallerMemberName] string? propertyName = null)
	{
		_weakEventManager.HandleEvent(
			this,
			new PropertyChangedEventArgs(propertyName),
			nameof(PropertyChanged));
	}
}
