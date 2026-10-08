using System.Globalization;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// Date and time. A strip of day chips (today, tomorrow and the five days after; swipe for more) picks the usual
/// days with one tap; below it the native date picker (any day) and the native time picker, each with its icon.
/// The two pickers share a row while they fit and wrap otherwise (FlexLayout measures; no code reacts to sizes,
/// so nothing is rebuilt while the card is laid out and nothing can end up outside the card).
/// </summary>
public sealed partial class WhenPicker : ContentView
{
	private const int StripDays = 7;

	public static readonly BindableProperty DateProperty =
		BindableProperty.Create(
			nameof(Date),
			typeof(DateTime),
			typeof(WhenPicker),
			DateTime.Today,
			BindingMode.TwoWay,
			propertyChanged: (bindable, _, _) => ((WhenPicker)bindable).MarkSelection());

	public static readonly BindableProperty TimeProperty =
		BindableProperty.Create(
			nameof(Time),
			typeof(TimeSpan),
			typeof(WhenPicker),
			TimeSpan.Zero,
			BindingMode.TwoWay);

	public static readonly BindableProperty MinimumDateProperty =
		BindableProperty.Create(
			nameof(MinimumDate),
			typeof(DateTime),
			typeof(WhenPicker),
			new DateTime(1900, 1, 1),
			propertyChanged: (bindable, _, _) => ((WhenPicker)bindable).BuildStrip());

	public static readonly BindableProperty MaximumDateProperty =
		BindableProperty.Create(
			nameof(MaximumDate),
			typeof(DateTime),
			typeof(WhenPicker),
			new DateTime(2100, 12, 31),
			propertyChanged: (bindable, _, _) => ((WhenPicker)bindable).BuildStrip());

	private readonly HorizontalStackLayout _strip;
	private readonly List<(DateTime Day, Border Chip, Label Text)> _chips = [];
	private DateTime _stripStart;

	public WhenPicker()
	{
		var date =
			new DatePicker
			{
				Format = "ddd, d MMM",
				VerticalOptions = LayoutOptions.Center
			};
		date.SetBinding(DatePicker.DateProperty, static (WhenPicker w) => w.Date, BindingMode.TwoWay, source: this);
		date.SetBinding(DatePicker.MinimumDateProperty, static (WhenPicker w) => w.MinimumDate, source: this);
		date.SetBinding(DatePicker.MaximumDateProperty, static (WhenPicker w) => w.MaximumDate, source: this);
		date.SetBinding(SemanticProperties.DescriptionProperty, static (LocalizationService l) => l.CurrentStrings.Plan.Date, source: LocalizationService.Current);

		var time =
			new TimePicker
			{
				Format = "HH:mm",
				VerticalOptions = LayoutOptions.Center
			};
		time.SetBinding(TimePicker.TimeProperty, static (WhenPicker w) => w.Time, BindingMode.TwoWay, source: this);
		time.SetBinding(SemanticProperties.DescriptionProperty, static (LocalizationService l) => l.CurrentStrings.Plan.Time, source: LocalizationService.Current);

		_strip = new HorizontalStackLayout { Spacing = 6 };

		var strip =
			new ScrollView
			{
				Orientation = ScrollOrientation.Horizontal,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
				Padding = new Thickness(12, 8, 12, 2),
				Content = _strip
			};

		var pickers =
			new FlexLayout
			{
				Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
				JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceBetween,
				AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
				AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start,
				Padding = new Thickness(12, 2, 8, 4),
				Children =
				{
					Labelled(IconGlyph.Calendar, date),
					Labelled(IconGlyph.Clock, time)
				}
			};

		Content =
			new VerticalStackLayout
			{
				Spacing = 0,
				Children = { strip, pickers }
			};

		BuildStrip();

		// Past midnight "today" moves on; the chips follow the language while the control is on screen.
		Loaded += (_, _) =>
		{
			LocalizationService.Current.PropertyChanged -= OnLanguageChanged;
			LocalizationService.Current.PropertyChanged += OnLanguageChanged;

			if (_stripStart != DateTime.Today)
			{
				BuildStrip();
			}
		};

		Unloaded += (_, _) => LocalizationService.Current.PropertyChanged -= OnLanguageChanged;
	}

	private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
		BuildStrip();

	public DateTime Date
	{
		get => (DateTime)GetValue(DateProperty);
		set => SetValue(DateProperty, value);
	}

	public TimeSpan Time
	{
		get => (TimeSpan)GetValue(TimeProperty);
		set => SetValue(TimeProperty, value);
	}

	public DateTime MinimumDate
	{
		get => (DateTime)GetValue(MinimumDateProperty);
		set => SetValue(MinimumDateProperty, value);
	}

	public DateTime MaximumDate
	{
		get => (DateTime)GetValue(MaximumDateProperty);
		set => SetValue(MaximumDateProperty, value);
	}

	/// <summary>A picker with its (decorative) icon; the picker carries the description.</summary>
	private static HorizontalStackLayout Labelled(IconGlyph glyph, View picker)
	{
		var icon =
			new Icon
			{
				Glyph = glyph,
				Size = 20,
				VerticalOptions = LayoutOptions.Center
			};
		icon.SetDynamicResource(Icon.ColorProperty, "InkMuted");

		return
			new HorizontalStackLayout
			{
				Spacing = 4,
				Margin = new Thickness(0, 2, 4, 2),
				Children = { icon, picker }
			};
	}

	/// <summary>Rectangular chips (they are tappable, so never pills): today, tomorrow, then weekday and day.</summary>
	private void BuildStrip()
	{
		if (_strip is null)
		{
			return;
		}

		_strip.Clear();
		_chips.Clear();
		_stripStart = DateTime.Today;

		CommonStrings strings = LocalizationService.Current.CurrentStrings.Common;

		for (int i = 0; i < StripDays; i++)
		{
			DateTime day = _stripStart.AddDays(i);

			if (day < MinimumDate.Date || day > MaximumDate.Date)
			{
				continue;
			}

			string text =
				i switch
				{
					0 => strings.Today,
					1 => strings.Tomorrow,
					_ => day.ToString("ddd d", CultureInfo.CurrentCulture)
				};

			var label =
				new Label
				{
					Text = text,
					LineBreakMode = LineBreakMode.NoWrap,
					VerticalTextAlignment = TextAlignment.Center
				};
			label.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");
			label.SetDynamicResource(Label.FontSizeProperty, "FontCaption");

			var chip =
				new Border
				{
					Content = label,
					StrokeThickness = 0,
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
					Padding = new Thickness(12, 0)
				};
			chip.SetDynamicResource(Dense.MinHeightProperty, "HitButton");
			SemanticProperties.SetDescription(chip, day.ToString("D", CultureInfo.CurrentCulture));

			var tap = new TapGestureRecognizer();
			tap.Tapped += (_, _) => Date = day + (Date - Date.Date);
			chip.GestureRecognizers.Add(tap);

			_chips.Add((day, chip, label));
			_strip.Add(chip);
		}

		MarkSelection();
	}

	private void MarkSelection()
	{
		foreach ((DateTime day, Border chip, Label label) in _chips)
		{
			bool on = day == Date.Date;

			chip.SetDynamicResource(BackgroundColorProperty, on ? "Accent" : "Raised");
			label.SetDynamicResource(Label.TextColorProperty, on ? "OnAccent" : "Ink");
		}
	}
}
