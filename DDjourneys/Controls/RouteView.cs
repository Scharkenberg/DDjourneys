namespace DDjourneys.Controls;

/// <summary>
/// "Start → destination" as a page header: both stops with their city on a second, fainter line.
/// Wraps instead of truncating, so long names stay readable on a phone.
/// </summary>
public sealed partial class RouteView : ContentView
{
	public static readonly BindableProperty FromProperty =
		BindableProperty.Create(
			nameof(From),
			typeof(string),
			typeof(RouteView),
			string.Empty,
			propertyChanged: OnChanged);

	public static readonly BindableProperty FromPlaceProperty =
		BindableProperty.Create(
			nameof(FromPlace),
			typeof(string),
			typeof(RouteView),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ToProperty =
		BindableProperty.Create(
			nameof(To),
			typeof(string),
			typeof(RouteView),
			string.Empty,
			propertyChanged: OnChanged);

	public static readonly BindableProperty ToPlaceProperty =
		BindableProperty.Create(
			nameof(ToPlace),
			typeof(string),
			typeof(RouteView),
			null,
			propertyChanged: OnChanged);

	public static readonly BindableProperty NameFontSizeProperty =
		BindableProperty.Create(
			nameof(NameFontSize),
			typeof(double),
			typeof(RouteView),
			20d,
			propertyChanged: OnChanged);

	private readonly StopNameView _from = new();
	private readonly StopNameView _to = new();
	private readonly Icon _arrow =
		new()
		{
			Glyph = IconGlyph.ArrowRight,
			Size = 18,
			Margin = new Thickness(8, 4, 8, 0),
			VerticalOptions = LayoutOptions.Start
		};

	public RouteView()
	{
		var flex =
			new FlexLayout
			{
				Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
				AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
				Children = { _from, _arrow, _to }
			};

		_arrow.SetDynamicResource(Icon.ColorProperty, "InkMuted");

		// The arrow keeps its size; only the two names give way when the row gets narrow.
		FlexLayout.SetShrink(_arrow, 0);

		Content = flex;
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

	/// <summary>Size of the two name lines; the cities stay small.</summary>
	public double NameFontSize
	{
		get => (double)GetValue(NameFontSizeProperty);
		set => SetValue(NameFontSizeProperty, value);
	}

	private static void OnChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((RouteView)bindable).Apply();

	private void Apply()
	{
		_from.Stop = From;
		_from.Place = FromPlace;
		_from.NameFontSize = NameFontSize;

		_to.Stop = To;
		_to.Place = ToPlace;
		_to.NameFontSize = NameFontSize;

		_arrow.Size = Math.Max(14, NameFontSize - 2);

		SemanticProperties.SetDescription(
			this,
			$"{From} → {To}");
	}
}
