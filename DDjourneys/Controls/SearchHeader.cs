using System.Windows.Input;

namespace DDjourneys.Controls;

/// <summary>
/// The searched connection as a page header: "start → destination" (each with its city below),
/// a caption line (date or search mode) and one action button on the right. Results and the single
/// connection use it, so both pages start with exactly the same block.
/// </summary>
public sealed partial class SearchHeader : ContentView
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

	public static readonly BindableProperty SecondaryGlyphProperty =
		BindableProperty.Create(
			nameof(SecondaryGlyph),
			typeof(IconGlyph),
			typeof(SearchHeader),
			IconGlyph.None,
			propertyChanged: OnChanged);

	public static readonly BindableProperty SecondaryCommandProperty =
		BindableProperty.Create(
			nameof(SecondaryCommand),
			typeof(ICommand),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty SecondaryDescriptionProperty =
		BindableProperty.Create(
			nameof(SecondaryDescription),
			typeof(string),
			typeof(SearchHeader),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty TertiaryGlyphProperty =
		BindableProperty.Create(nameof(TertiaryGlyph), typeof(IconGlyph), typeof(SearchHeader), IconGlyph.None, propertyChanged: OnChanged);

	public static readonly BindableProperty TertiaryCommandProperty =
		BindableProperty.Create(nameof(TertiaryCommand), typeof(ICommand), typeof(SearchHeader), null, propertyChanged: OnChanged);

	public static readonly BindableProperty TertiaryDescriptionProperty =
		BindableProperty.Create(nameof(TertiaryDescription), typeof(string), typeof(SearchHeader), null, propertyChanged: OnChanged);

	private readonly RouteView _route = new();

	private readonly IconButton _tertiary =
		new()
		{
			VerticalOptions = LayoutOptions.Start
		};

	private readonly IconButton _secondary =
		new()
		{
			VerticalOptions = LayoutOptions.Start
		};

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
		grid.Add(
			new HorizontalStackLayout
			{
				Spacing = 0,
				VerticalOptions = LayoutOptions.Start,
				Children = { _tertiary, _secondary, _action }
			},
			1,
			0);

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

	/// <summary>An optional second button left of the action (the bookmark of the connection).</summary>
	public IconGlyph SecondaryGlyph
	{
		get => (IconGlyph)GetValue(SecondaryGlyphProperty);
		set => SetValue(SecondaryGlyphProperty, value);
	}

	public ICommand? SecondaryCommand
	{
		get => (ICommand?)GetValue(SecondaryCommandProperty);
		set => SetValue(SecondaryCommandProperty, value);
	}

	public string? SecondaryDescription
	{
		get => (string?)GetValue(SecondaryDescriptionProperty);
		set => SetValue(SecondaryDescriptionProperty, value);
	}

	/// <summary>An optional third button, left of the second (the technical details, when they are enabled).</summary>
	public IconGlyph TertiaryGlyph
	{
		get => (IconGlyph)GetValue(TertiaryGlyphProperty);
		set => SetValue(TertiaryGlyphProperty, value);
	}

	public ICommand? TertiaryCommand
	{
		get => (ICommand?)GetValue(TertiaryCommandProperty);
		set => SetValue(TertiaryCommandProperty, value);
	}

	public string? TertiaryDescription
	{
		get => (string?)GetValue(TertiaryDescriptionProperty);
		set => SetValue(TertiaryDescriptionProperty, value);
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

		_secondary.Glyph = SecondaryGlyph;
		_secondary.Command = SecondaryCommand;
		_secondary.IsVisible = SecondaryGlyph != IconGlyph.None;

		SemanticProperties.SetDescription(_secondary, SecondaryDescription);

		_tertiary.Glyph = TertiaryGlyph;
		_tertiary.Command = TertiaryCommand;
		_tertiary.IsVisible = TertiaryGlyph != IconGlyph.None;

		SemanticProperties.SetDescription(_tertiary, TertiaryDescription);
	}
}
