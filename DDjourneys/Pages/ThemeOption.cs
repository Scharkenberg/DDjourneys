using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One row in the theme list: a swatch, a localized name and the selection mark.</summary>
public sealed class ThemeOption : ObservableObject
{
	private readonly ThemeDef _def;
	private string _name = string.Empty;
	private string _description = string.Empty;
	private bool _isSelected;

	public ThemeOption(string id, ThemeDef swatchSource)
	{
		Id = id;
		_def = swatchSource;
	}

	public string Id { get; }

	// Swatch colours (system shows the dark pair it resolves to at night)
	public Color Bg => _def.Bg;
	public Color Raised => _def.Raised;
	public Color Outline => _def.Outline;
	public Color Accent => _def.Accent;
	public Color Ink => _def.Ink;

	public string Name
	{
		get => _name;
		private set => SetProperty(ref _name, value);
	}

	public string Description
	{
		get => _description;
		private set => SetProperty(ref _description, value);
	}

	public bool IsSelected
	{
		get => _isSelected;
		private set => SetProperty(ref _isSelected, value);
	}

	/// <summary>Localized name of a theme (the built-in ones come from the string tables).</summary>
	public static string NameOf(ThemeDef def, SettingsStrings s) =>
		def.Id switch
		{
			"light" => s.ThemeLight,
			"dark" => s.ThemeDark,
			"amoled" => s.ThemeAmoled,
			_ => def.Name
		};

	/// <summary>Re-resolves the language-dependent texts and the selection mark.</summary>
	public void Update(string selectedId, SettingsStrings s, bool german)
	{
		IsSelected = string.Equals(Id, selectedId, StringComparison.OrdinalIgnoreCase);

		Name = NameOf(_def, s);

		Description = Id switch
		{
			"light" => s.ThemeLightDescription,
			"dark" => s.ThemeDarkDescription,
			"amoled" => s.ThemeAmoledDescription,
			_ => german ? _def.DescriptionDe : _def.Description
		};
	}
}