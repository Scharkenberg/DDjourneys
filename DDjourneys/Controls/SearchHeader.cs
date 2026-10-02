using System.Windows.Input;

namespace DDjourneys.Controls;

/// <summary>
/// The searched connection as a page header: "start → destination" (each with its city below),
/// a caption line (date or search mode) and one action button on the right. Results and the single
/// connection use it, so both pages start with exactly the same block.
/// </summary>
public sealed class SearchHeader : ContentView
{
	/// <summary>Size of the two stop names; shared by every page that shows the searched connection.</summary>
	public const string NameFontSizeKey = "FontSubtitle";

	public static readonly BindableProperty FromProperty =
		BindableProperty.Create(
			nameof(From),
			typeof(string),
			typeof(SearchHeader),
			string.Empty,
			propertyChanged: OnChanged);

	public static readonly BindableProperty FromPlaceProperty =
		BindableProperty.Create(
			nameof(FromPlace),
			typeof(string),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ToProperty =
		BindableProperty.Create(
			nameof(To),
			typeof(string),
			typeof(SearchHeader),
			string.Empty,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ToPlaceProperty =
		BindableProperty.Create(
			nameof(ToPlace),
			typeof(string),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty SubtitleProperty =
		BindableProperty.Create(
			nameof(Subtitle),
			typeof(string),
			typeof(SearchHeader),
			string.Empty,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ActionGlyphProperty =
		BindableProperty.Create(
			nameof(ActionGlyph),
			typeof(IconGlyph),
			typeof(SearchHeader),
			IconGlyph.None,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ActionCommandProperty =
		BindableProperty.Create(
			nameof(ActionCommand),
			typeof(ICommand),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ActionDescriptionProperty =
		BindableProperty.Create(
			nameof(ActionDescription),
			typeof(string),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	private readonly RouteView _route = new();

	private readonly Label _subtitle =
		new()
		{
			StyleClass = ["Caption"]
		};

	private readonly IconButton _action =
		new()
		{
			VerticalOptions = LayoutOptions.Start
		};

	public SearchHeader()
	{
		var texts =
			new VerticalStackLayout
			{
				Spacing = 4,
				VerticalOptions = LayoutOptions.Center,
				Children = { _route, _subtitle }
			};

		var grid =
			new Microsoft.Maui.Controls.Grid
			{
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Auto)
				],
				ColumnSpacing = 8
			};

		grid.Add(texts, 0, 0);
		grid.Add(_action, 1, 0);

		Content = grid;

		_route.SetDynamicResource(
			RouteView.NameFontSizeProperty,
			NameFontSizeKey);

		Apply();
	}

	public string From
	{
		get => (string)GetValue(FromProperty);
		set => SetValue(FromProperty, value);
	}

	public string? FromPlace
	{
		get => (string?)GetValue(FromPlaceProperty);
		set => SetValue(FromPlaceProperty, value);
	}

	public string To
	{
		get => (string)GetValue(ToProperty);
		set => SetValue(ToProperty, value);
	}

	public string? ToPlace
	{
		get => (string?)GetValue(ToPlaceProperty);
		set => SetValue(ToPlaceProperty, value);
	}

	/// <summary>Caption under the route: the day, or the requested time.</summary>
	public string Subtitle
	{
		get => (string)GetValue(SubtitleProperty);
		set => SetValue(SubtitleProperty, value);
	}

	public IconGlyph ActionGlyph
	{
		get => (IconGlyph)GetValue(ActionGlyphProperty);
		set => SetValue(ActionGlyphProperty, value);
	}

	public ICommand? ActionCommand
	{
		get => (ICommand?)GetValue(ActionCommandProperty);
		set => SetValue(ActionCommandProperty, value);
	}

	public string? ActionDescription
	{
		get => (string?)GetValue(ActionDescriptionProperty);
		set => SetValue(ActionDescriptionProperty, value);
	}

	private static void OnChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((SearchHeader)bindable).Apply();

	private void Apply()
	{
		_route.From = From;
		_route.FromPlace = FromPlace;
		_route.To = To;
		_route.ToPlace = ToPlace;

		_subtitle.Text = Subtitle;
		_subtitle.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);

		_action.Glyph = ActionGlyph;
		_action.Command = ActionCommand;
		_action.IsVisible = ActionGlyph != IconGlyph.None;

		SemanticProperties.SetDescription(_action, ActionDescription);
	}
}
