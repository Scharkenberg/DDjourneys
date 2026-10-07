using CommunityToolkit.Maui.Extensions;

namespace DDjourneys.Pages;

public partial class JourneySharePopup : ContentView
{
	private readonly Page _hostPage;
	private bool? _stacked;
	private string _textChoice;
	private string _imageChoice;

	public JourneySharePopup(
	Page hostPage,
	string textChoice,
	string imageChoice)
	{
		ArgumentNullException.ThrowIfNull(hostPage);

		_hostPage = hostPage;
		_textChoice = textChoice;
		_imageChoice = imageChoice;

		InitializeComponent();

		Loaded += OnLoaded;
		SizeChanged += OnSizeChanged;
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		// The popup can receive Loaded before its final bounds have propagated.
		// Defer one layout pass so the initial presentation is evaluated with real dimensions.
		Dispatcher.Dispatch(UpdateLayout);
	}

	private void OnSizeChanged(object? sender, EventArgs e)
	{
		UpdateLayout();
	}

	private void UpdateLayout()
	{
		double width = Root.Width;

		if (width <= 0)
		{
			return;
		}

		double available =
			width
			- Root.Padding.HorizontalThickness;

		double textWidth =
			TextCard.Measure(
				double.PositiveInfinity,
				double.PositiveInfinity).Width;

		double imageWidth =
			ImageCard.Measure(
				double.PositiveInfinity,
				double.PositiveInfinity).Width;

		double required =
			textWidth
			+ imageWidth
			+ OptionsGrid.ColumnSpacing;

		bool stacked = required > available;

		if (_stacked == stacked)
		{
			return;
		}

		_stacked = stacked;

		if (stacked)
		{
			OptionsGrid.ColumnDefinitions =
			[
				new ColumnDefinition(GridLength.Star)
			];

			OptionsGrid.RowDefinitions =
			[
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto)
			];

			Grid.SetRow(TextCard, 0);
			Grid.SetColumn(TextCard, 0);

			Grid.SetRow(ImageCard, 1);
			Grid.SetColumn(ImageCard, 0);
		}
		else
		{
			OptionsGrid.ColumnDefinitions =
			[
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star)
			];

			OptionsGrid.RowDefinitions =
			[
				new RowDefinition(GridLength.Auto)
			];

			Grid.SetRow(TextCard, 0);
			Grid.SetColumn(TextCard, 0);

			Grid.SetRow(ImageCard, 0);
			Grid.SetColumn(ImageCard, 1);
		}

		OptionsGrid.InvalidateMeasure();
	}

	private async void TextTapped(object? sender, TappedEventArgs e)
	{
		await _hostPage.ClosePopupAsync(_textChoice, CancellationToken.None);
	}

	private async void ImageTapped(object? sender, TappedEventArgs e)
	{
		await _hostPage.ClosePopupAsync(_imageChoice, CancellationToken.None);
	}

	private async void CancelClicked(object? sender, EventArgs e)
	{
		await _hostPage.ClosePopupAsync<string?>(null, CancellationToken.None);
	}
}