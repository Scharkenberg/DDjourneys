using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// Date and time, each with its caption. They share one row while both pickers fit their half of it; when the window
/// is too narrow (the Windows time picker has a wide built-in minimum that cannot be narrowed safely) the time moves
/// to a row of its own instead of being cut off. The journey planner and the departure board use this one control,
/// so both adapt in exactly the same way.
/// </summary>
public sealed class WhenPicker : ContentView
{
	public static readonly BindableProperty DateProperty =
		BindableProperty.Create(
			nameof(Date),
			typeof(DateTime),
			typeof(WhenPicker),
			DateTime.Today,
			BindingMode.TwoWay);

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
			new DateTime(1900, 1, 1));

	public static readonly BindableProperty MaximumDateProperty =
		BindableProperty.Create(
			nameof(MaximumDate),
			typeof(DateTime),
			typeof(WhenPicker),
			new DateTime(2100, 12, 31));

	private readonly Grid _grid;
	private readonly VerticalStackLayout _timeCell;
	private readonly DatePicker _date;
	private readonly TimePicker _time;
	private bool? _stacked;

	public WhenPicker()
	{
		_date =
			new DatePicker
			{
				Format = "ddd, d MMM",
				HorizontalOptions = LayoutOptions.Start
			};

		_date.SetBinding(DatePicker.DateProperty, new Binding(nameof(Date), BindingMode.TwoWay, source: this));
		_date.SetBinding(DatePicker.MinimumDateProperty, new Binding(nameof(MinimumDate), source: this));
		_date.SetBinding(DatePicker.MaximumDateProperty, new Binding(nameof(MaximumDate), source: this));

		_time =
			new TimePicker
			{
				Format = "HH:mm",
				HorizontalOptions = LayoutOptions.Start
			};

		_time.SetBinding(TimePicker.TimeProperty, new Binding(nameof(Time), BindingMode.TwoWay, source: this));

		var dateCell = Cell("CurrentStrings.Plan.Date", _date);
		_timeCell = Cell("CurrentStrings.Plan.Time", _time);

		_grid =
			new Grid
			{
				ColumnSpacing = 12,
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Star)
				],
				Children =
				{
					dateCell,
					_timeCell
				}
			};

		Grid.SetColumn(_timeCell, 1);

		Dense.SetPadding(_grid, new Thickness(12, 3));
		Dense.SetMinHeight(_grid, 52);

		_grid.SizeChanged += OnSizeChanged;

		Content = _grid;
	}

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

	private static VerticalStackLayout Cell(string captionPath, View picker)
	{
		// Bound to the localization service, so the caption follows a change of language.
		var caption1 = new Label { StyleClass = ["Caption"] };
		caption1.SetBinding(Label.TextProperty, new Binding(captionPath, source: LocalizationService.Current));

		return
			new VerticalStackLayout
			{
				Spacing = 0,
				VerticalOptions = LayoutOptions.Center,
				Children = { caption1, picker }
			};
	}

	private void OnSizeChanged(object? sender, EventArgs e)
	{
		if (_grid.Width <= 0)
		{
			return;
		}

		double required =
			Math.Max(
				_date.Measure(double.PositiveInfinity, double.PositiveInfinity).Width,
				_time.Measure(double.PositiveInfinity, double.PositiveInfinity).Width);

		if (required <= 0)
		{
			return;
		}

		double half = (_grid.Width - _grid.Padding.HorizontalThickness - _grid.ColumnSpacing) / 2;
		bool stacked = half < required;

		if (_stacked == stacked)
		{
			return;
		}

		_stacked = stacked;

		if (stacked)
		{
			_grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
			_grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
			_grid.RowSpacing = 4;
			Grid.SetRow(_timeCell, 1);
			Grid.SetColumn(_timeCell, 0);
		}
		else
		{
			_grid.RowDefinitions = [];
			_grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
			_grid.RowSpacing = 0;
			Grid.SetRow(_timeCell, 0);
			Grid.SetColumn(_timeCell, 1);
		}
	}
}
