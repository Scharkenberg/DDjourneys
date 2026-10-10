using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The DVB line network map: downloaded on demand (see <see cref="NetworkMapStore"/>), cached for a week,
/// pinch-zoomed and panned. The PDF opens in the system viewer. A document, not a pane page: it is pushed
/// with a plain Shell navigation and popped by back.
/// </summary>
public sealed partial class NetworkMapPage : ContentPage
{
	private readonly NetworkMapStore _maps;
	private CancellationTokenSource? _load;
	private double _aspect;

	public NetworkMapPage(NetworkMapStore maps)
	{
		InitializeComponent();

		_maps = maps;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (Zoom.Source is null)
		{
			Load(refresh: false);
		}
	}

	protected override void OnDisappearing()
	{
		_load?.Cancel();

		base.OnDisappearing();
	}

	private void RefreshClicked(object? sender, EventArgs e) =>
				Load(refresh: true);

	private void RetryClicked(object? sender, EventArgs e) =>
				Load(refresh: false);

	private async void OpenPdfClicked(object? sender, EventArgs e)
	{
		try
		{
			NetworkMapFile pdf = await _maps.GetPdfAsync();

			await Launcher.Default.OpenAsync(
				new OpenFileRequest("Liniennetzplan", new ReadOnlyFile(pdf.Path, "application/pdf")))
				;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] opening the PDF failed: {ex.Message}");
		}
	}

	private async void Load(bool refresh)
	{
		_load?.Cancel();

		var cancel = new CancellationTokenSource();

		_load = cancel;

		Message.IsVisible = false;
		Retry.IsVisible = false;
		Stale.IsVisible = false;

		try
		{
			NetworkMapFile map = refresh
				? await _maps.RefreshAsync(cancel.Token)
				: await _maps.GetJpgAsync(cancel.Token);

			if (cancel.IsCancellationRequested)
			{
				return;
			}

			// The aspect lets the zoom clamp to the paper's edge, not the letterboxed frame.
			if (_aspect <= 0)
			{
				using SkiaSharp.SKBitmap? bounds = SkiaSharp.SKBitmap.Decode(map.Path);

				if (bounds is not null && bounds.Height > 0)
				{
					_aspect = (double)bounds.Width / bounds.Height;
				}
			}

			Zoom.ContentAspect = _aspect;
			Zoom.Reset();
			Zoom.Source = ImageSource.FromFile(map.Path);
			Stale.IsVisible = !map.Fresh;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception)
		{
			// A friendly message with a way out, never a blank page.
			Message.IsVisible = true;
			Retry.IsVisible = true;
		}
	}
}
