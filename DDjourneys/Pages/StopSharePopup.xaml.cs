using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support;
using DDjourneys.Support.Sharing;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>
/// The share of one stop as a popup sheet (the JourneySharePopup pattern): the stop's name, a QR code
/// that opens it on a map for everyone without the app, the text that goes out, the board link to copy,
/// and the platform share sheet for the text. The one entry for all three stop surfaces (map, board,
/// zone plan), so they can never disagree.
/// </summary>
public partial class StopSharePopup : ContentView
{
	private readonly StopSharePayload _share;
	private readonly string _text;

	private bool _themeSubscribed;

	private StopSharePopup(Location stop)
	{
		ArgumentNullException.ThrowIfNull(stop);

		_share = StopShare.For(stop);
		_text = StopShareText.Build(_share, LocalizationService.Current.CurrentStrings);

		InitializeComponent();

		StopNameLabel.Text = _share.Name;
		ShareTextLabel.Text = _text;

		if (_share.QrPayload is { } payload)
		{
			// The code exists only when the stop has coordinates; the rest of the sheet still works without it.
			string path = StopShareImage.Render(QrCode.Encode(payload));

			QrImage.Source = ImageSource.FromFile(path);
		}
		else
		{
			QrBlock.IsVisible = false;
		}

		PaintBase();

		// Reparenting can fire Loaded more often than Unloaded: the flag keeps the subscription single.
		Loaded += OnPopupLoaded;
		Unloaded += OnPopupUnloaded;
	}

	/// <summary>Shows the share of the stop as a popup over the page (or the pane host that lends it).</summary>
	public static async Task ShowAsync(Page host, Location stop)
	{
		var popup = new StopSharePopup(stop);

		await host.ShowPopupAsync(
			popup,
			new PopupOptions
			{
				CanBeDismissedByTappingOutsideOfPopup = true,
				PageOverlayColor = Colors.Black.WithAlpha(0.45f),

				// The sheet draws its own shape, stroke and surface.
				Shape = null,
				Shadow = null
			},
			CancellationToken.None);
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

	private async void ShareClicked(object? sender, EventArgs e)
	{
		try
		{
			await Share.Default.RequestAsync(
				new ShareTextRequest
				{
					Title = LocalizationService.Current.CurrentStrings.Extras.ShareStop,
					Text = _text
				});
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Share stop failed:\n{ex}");
		}
	}

	private async void CopyTapped(object? sender, TappedEventArgs e)
	{
		try
		{
			// The board link alone: what a paste into a message or a note wants. OriginalString keeps
			// the encoded form, so the pasted link stays one link.
			await Clipboard.Default.SetTextAsync(_share.AppLink.OriginalString);

			CopiedLabel.IsVisible = true;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Copy stop link failed:\n{ex}");
		}
	}
}
