using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// Date and time, each with its caption. They share one row as long as both controls fit naturally; the layout does
/// not reserve half the available width for each picker. Instead it uses a left cell, a flexible spacer, and a right
/// cell. When the window becomes too narrow, the time moves to a row of its own instead of being cut off.
/// </summary>
public sealed partial class WhenPicker : ContentView
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
	private readonly VerticalStackLayout _dateCell;
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
				HorizontalOptions = LayoutOptions.End
			};

		_time.SetBinding(TimePicker.TimeProperty, new Binding(nameof(Time), BindingMode.TwoWay, source: this));

		_dateCell = Cell("CurrentStrings.Plan.Date", _date);
		_timeCell = Cell("CurrentStrings.Plan.Time", _time);

		_grid =
			new Grid
			{
				ColumnSpacing = 12,
				ColumnDefinitions =
				[
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Auto)
				],
				Children =
				{
					_dateCell,
					_timeCell
				}
			};

		Grid.SetColumn(_dateCell, 0);
		Grid.SetColumn(_timeCell, 2);

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
		var caption = new Label { StyleClass = ["Caption"] };
		caption.SetBinding(Label.TextProperty, new Binding(captionPath, source: LocalizationService.Current));

		return
			new VerticalStackLayout
			{
				Spacing = 0,
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Fill,
				Children = { caption, picker }
			};
	}

	private void OnSizeChanged(object? sender, EventArgs e)
	{
		if (_grid.Width <= 0)
		{
			return;
		}

		double dateWidth = _dateCell.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
		double timeWidth = _timeCell.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;

		if (dateWidth <= 0 || timeWidth <= 0)
		{
			return;
		}

		double available =
			_grid.Width
			- _grid.Padding.HorizontalThickness
			- (_grid.ColumnSpacing * 2);

		double required = dateWidth + timeWidth;

		bool stacked = required > available;

		if (_stacked == stacked)
		{
			return;
		}

		_stacked = stacked;

		if (stacked)
		{
			_grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
			_grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
			_grid.RowSpacing = 4;

			Grid.SetRow(_dateCell, 0);
			Grid.SetColumn(_dateCell, 0);

			Grid.SetRow(_timeCell, 1);
			Grid.SetColumn(_timeCell, 0);
		}
		else
		{
			_grid.RowDefinitions = [];
			_grid.ColumnDefinitions =
			[
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			];
			_grid.RowSpacing = 0;

			Grid.SetRow(_dateCell, 0);
			Grid.SetColumn(_dateCell, 0);

			Grid.SetRow(_timeCell, 0);
			Grid.SetColumn(_timeCell, 2);
		}
	}
}