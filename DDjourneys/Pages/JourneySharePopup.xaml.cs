using CommunityToolkit.Maui.Extensions;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The choice of how to share a journey (text or image), shown as a popup sheet. The answer is the option the caller
/// passed in for the tapped row; cancelling answers null.
/// </summary>
public partial class JourneySharePopup : ContentView
{
	private readonly Page _hostPage;
	private readonly string _textChoice;
	private readonly string _imageChoice;
	private readonly string _calendarChoice;

	private bool _themeSubscribed;

	public JourneySharePopup(
		Page hostPage,
		string textChoice,
		string imageChoice,
		string calendarChoice)
	{
		ArgumentNullException.ThrowIfNull(hostPage);

		_hostPage = hostPage;
		_textChoice = textChoice;
		_imageChoice = imageChoice;
		_calendarChoice = calendarChoice;

		InitializeComponent();

		PaintBase();

		// Reparenting can fire Loaded more often than Unloaded: the flag keeps the subscription single.
		Loaded += OnPopupLoaded;
		Unloaded += OnPopupUnloaded;
	}

	private void OnPopupLoaded(object? sender, EventArgs e)
	{
		if (!_themeSubscribed)
		{
			Theme.Changed += OnThemeChanged;
			_themeSubscribed = true;
		}
	}

	private void OnPopupUnloaded(object? sender, EventArgs e)
	{
		if (_themeSubscribed)
		{
			Theme.Changed -= OnThemeChanged;
			_themeSubscribed = false;
		}
	}

	private void OnThemeChanged(object? sender, EventArgs e) =>
		Dispatcher.Dispatch(PaintBase);

	/// <summary>The layer under the sheet: the page colour, opaque (the palette itself may be translucent under a window material).</summary>
	private void PaintBase() =>
		SheetBase.BackgroundColor = Theme.ColorOf("Bg", Colors.Transparent);

	private async void TextTapped(object? sender, TappedEventArgs e)
	{
		await _hostPage.ClosePopupAsync(_textChoice, CancellationToken.None);
	}

	private async void ImageTapped(object? sender, TappedEventArgs e)
	{
		await _hostPage.ClosePopupAsync(_imageChoice, CancellationToken.None);
	}

	private async void CalendarTapped(object? sender, TappedEventArgs e)
	{
		await _hostPage.ClosePopupAsync(_calendarChoice, CancellationToken.None);
	}

	private async void CancelClicked(object? sender, EventArgs e)
	{
		await _hostPage.ClosePopupAsync<string?>(null, CancellationToken.None);
	}
}
