using DDjourneys.Core.Models;

namespace DDjourneys.Controls;

/// <summary>
/// A stop name with its city/region on a second, smaller and fainter line — the one presentation
/// used for every stop in the app (planner, place search, timeline, headers).
///
/// The second line disappears when there is no city, or when the name already ends with it
/// ("Hauptbahnhof, Dresden"), so rows never keep an empty gap.
/// </summary>
public sealed partial class StopNameView : ContentView
{
	public static readonly BindableProperty StopProperty =
		BindableProperty.Create(
			nameof(Stop),
			typeof(string),
			typeof(StopNameView),
			string.Empty,
			propertyChanged: OnContentChanged);

	public static readonly BindableProperty PlaceProperty =
		BindableProperty.Create(
			nameof(Place),
			typeof(string),
			typeof(StopNameView),
			null,
			propertyChanged: OnContentChanged);

	public static readonly BindableProperty PrefixProperty =
		BindableProperty.Create(
			nameof(Prefix),
			typeof(string),
			typeof(StopNameView),
			null,
			propertyChanged: OnContentChanged);

	public static readonly BindableProperty CompactProperty =
		BindableProperty.Create(
			nameof(Compact),
			typeof(bool),
			typeof(StopNameView),
			false,
			propertyChanged: OnLookChanged);

	public static readonly BindableProperty MutedProperty =
		BindableProperty.Create(
			nameof(Muted),
			typeof(bool),
			typeof(StopNameView),
			false,
			propertyChanged: OnLookChanged);

	public static readonly BindableProperty NameFontSizeProperty =
		BindableProperty.Create(
			nameof(NameFontSize),
			typeof(double),
			typeof(StopNameView),
			0d,
			propertyChanged: OnLookChanged);

	/// <summary>
	/// One line each, never wrapped or cut: a long name scrolls sideways (like the chip strips). For start, stop-over
	/// and destination wherever they are a heading or a card endpoint.
	/// </summary>
	public static readonly BindableProperty SingleLineProperty =
		BindableProperty.Create(
			nameof(SingleLine),
			typeof(bool),
			typeof(StopNameView),
			false,
			propertyChanged: (bindable, _, _) => ((StopNameView)bindable).ApplyLayout());

	public static readonly BindableProperty AlignmentProperty =
		BindableProperty.Create(
			nameof(Alignment),
			typeof(TextAlignment),
			typeof(StopNameView),
			TextAlignment.Start,
			propertyChanged: OnLookChanged);

	private readonly Label _name =
		new()
		{
			LineBreakMode = LineBreakMode.WordWrap
		};

	private readonly Label _place =
		new()
		{
			LineBreakMode = LineBreakMode.TailTruncation,
			StyleClass = ["Faint"]
		};

	private readonly VerticalStackLayout _lines;

	private bool _tapForwarded;

	/// <summary>
	/// A tap on the scrolling names (Android only: its scroller keeps the touch from the views around it; elsewhere the
	/// tap reaches them by itself). Whoever makes the surroundings tappable forwards it.
	/// </summary>
	public event EventHandler? Tapped;

	public StopNameView()
	{
		_lines =
			new VerticalStackLayout
			{
				Spacing = 1,
				Children = { _name, _place }
			};

		Content = _lines;

		ApplyLook();
		Refresh();
	}

	public bool SingleLine
	{
		get => (bool)GetValue(SingleLineProperty);
		set => SetValue(SingleLineProperty, value);
	}

	/// <summary>One line each without a scroller of its own: for a row that scrolls as a whole (<see cref="RouteView"/>).</summary>
	internal void UseOneLine()
	{
		_name.LineBreakMode = LineBreakMode.NoWrap;
		_place.LineBreakMode = LineBreakMode.NoWrap;
	}

	/// <summary>Set from XAML before the control is on screen, so the lines move into the scroller before any native view exists.</summary>
	private void ApplyLayout()
	{
		Content = null;

		if (SingleLine)
		{
			_name.LineBreakMode = LineBreakMode.NoWrap;
			_place.LineBreakMode = LineBreakMode.NoWrap;

			Content =
				new ScrollView
				{
					Orientation = ScrollOrientation.Horizontal,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
					Content = _lines
				};


			if (!_tapForwarded)
			{
				_tapForwarded = true;
				var tap = new TapGestureRecognizer();
				tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
				_lines.GestureRecognizers.Add(tap);
			}
		}
		else
		{
			_name.LineBreakMode = LineBreakMode.WordWrap;
			_place.LineBreakMode = LineBreakMode.TailTruncation;

			if (_lines.Parent is ScrollView scroller)
			{
				scroller.Content = null;
			}

			Content = _lines;
		}
	}

	/// <summary>The stop's name.</summary>
	public string Stop
	{
		get => (string)GetValue(StopProperty);
		set => SetValue(StopProperty, value);
	}

	/// <summary>City, village or region; may be empty.</summary>
	public string? Place
	{
		get => (string?)GetValue(PlaceProperty);
		set => SetValue(PlaceProperty, value);
	}

	/// <summary>Muted lead-in before the name, e.g. "to" in a walking caption.</summary>
	public string? Prefix
	{
		get => (string?)GetValue(PrefixProperty);
		set => SetValue(PrefixProperty, value);
	}

	/// <summary>Caption-sized, regular weight name (intermediate stops).</summary>
	public bool Compact
	{
		get => (bool)GetValue(CompactProperty);
		set => SetValue(CompactProperty, value);
	}

	/// <summary>Placeholder look: the name is drawn in the muted ink (nothing chosen yet).</summary>
	public bool Muted
	{
		get => (bool)GetValue(MutedProperty);
		set => SetValue(MutedProperty, value);
	}

	/// <summary>Horizontal alignment of both lines.</summary>
	public TextAlignment Alignment
	{
		get => (TextAlignment)GetValue(AlignmentProperty);
		set => SetValue(AlignmentProperty, value);
	}

	/// <summary>Explicit size for the name line; 0 keeps the style's size.</summary>
	public double NameFontSize
	{
		get => (double)GetValue(NameFontSizeProperty);
		set => SetValue(NameFontSizeProperty, value);
	}

	private static void OnContentChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((StopNameView)bindable).Refresh();

	private static void OnLookChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((StopNameView)bindable).ApplyLook();

	private void ApplyLook()
	{
		_name.HorizontalTextAlignment = Alignment;
		_place.HorizontalTextAlignment = Alignment;

		if (Compact)
		{
			_name.StyleClass = ["Caption"];
			// Never RemoveDynamicResource here: MAUI keeps one registration per property, so removing ours
			// would also drop the implicit style's and freeze the font on theme changes.
			_name.SetDynamicResource(Label.FontFamilyProperty, "FontRegular");
		}
		else
		{
			_name.StyleClass = [];
			_name.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");
		}

		if (NameFontSize > 0)
		{
			_name.FontSize = NameFontSize;
		}
		else
		{
			_name.ClearValue(Label.FontSizeProperty);
		}

		_name.RemoveDynamicResource(Label.TextColorProperty);

		// Compact rows take their colour from the Caption style; everything else is explicit,
		// so a placeholder can be muted without losing the theme.
		_name.SetDynamicResource(
			Label.TextColorProperty,
			Muted || Compact
				? "InkMuted"
				: "Ink");

		// The prefix span carries its own colour, so rebuild the text whenever the look changes.
		Refresh();
	}

	private void Refresh()
	{
		// One presentation everywhere: the name without its city, the city below it.
		(string name, string? city) = StopLabel.Split(Stop, Place);

		if (string.IsNullOrEmpty(Prefix))
		{
			_name.FormattedText = null;
			_name.Text = name;
		}
		else
		{
			var text = new FormattedString();

			var prefix =
				new Span
				{
					Text = name.Length == 0
						? Prefix
						: $"{Prefix} "
				};

			prefix.SetDynamicResource(Span.TextColorProperty, "InkMuted");
			text.Spans.Add(prefix);

			if (name.Length > 0)
			{
				text.Spans.Add(
					new Span
					{
						Text = name
					});
			}

			_name.FormattedText = text;
		}

		_place.Text = city;
		_place.IsVisible = city is not null;
	}
}
