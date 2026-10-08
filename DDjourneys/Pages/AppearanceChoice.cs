using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One row of the appearance page: a localized title, a description, an optional colour swatch and the selection mark.</summary>
public sealed partial class AppearanceChoice(string id, bool hasSwatch) : ObservableObject
{
	private string _title = string.Empty;
	private string _description = string.Empty;
	private bool _isSelected;
	private Color _bg = Colors.Transparent;
	private Color _raised = Colors.Transparent;
	private Color _outline = Colors.Transparent;
	private Color _accent = Colors.Transparent;
	private Color _ink = Colors.Transparent;

	public string Id { get; } = id;

	public bool HasSwatch { get; } = hasSwatch;

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);
	}

	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value);
	}

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}

	public Color Bg
	{
		get => _bg;
		private set => SetProperty(ref _bg, value);
	}

	public Color Raised
	{
		get => _raised;
		private set => SetProperty(ref _raised, value);
	}

	public Color Outline
	{
		get => _outline;
		private set => SetProperty(ref _outline, value);
	}

	public Color Accent
	{
		get => _accent;
		private set => SetProperty(ref _accent, value);
	}

	public Color Ink
	{
		get => _ink;
		private set => SetProperty(ref _ink, value);
	}

	public void SetSwatch(IReadOnlyDictionary<string, Color> palette)
	{
		Bg = palette["Bg"];
		Raised = palette["Raised"];
		Outline = palette["Outline"];
		Accent = palette["Accent"];
		Ink = palette["Ink"];
	}
}
