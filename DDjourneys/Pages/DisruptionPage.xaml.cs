using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// One disruption or network notice in full. The text is HTML: it is shown by <see cref="Controls.NoticeView"/>;
/// a tapped link is looked at first (what is it: PDF, picture, web page?) and then opened in the right place.
/// </summary>
public partial class DisruptionPage : ContentPage, IQueryAttributable
{
	private CancellationTokenSource? _link;

	public DisruptionPage()
	{
		InitializeComponent();
		Motion.Prepare(this);

		Notice.LinkTapped += OnLinkTapped;
	}

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.DisruptionData, out object? value)
			&& value is DisruptionRow row)
		{
			BindingContext = row;
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		if (PageTeardown.IsLeavingForGood(args))
		{
			_link?.Cancel();
		}
	}

	private void OnLinkTapped(object? sender, Uri uri)
	{
		// Raised from the WebView's request thread: hop to the UI thread before touching any view.
		Dispatcher.Dispatch(async () => await HandleLinkAsync(uri));
	}

	private async Task HandleLinkAsync(Uri uri)
	{
		_link?.Cancel();

		var cts = new CancellationTokenSource();
		_link = cts;

		Status.IsVisible = false;
		Busy.IsVisible = true;
		Busy.IsRunning = true;

		try
		{
			string? problem =
				await NoticeLinks.OpenAsync(
					uri,
					Notice.ShowImageAsync,
					LocalizationService.Current.CurrentStrings.Extras.NoticeLinkFailed,
					cts.Token);

			if (problem is not null
				&& !cts.IsCancellationRequested)
			{
				Status.Text = problem;
				Status.IsVisible = true;
			}
		}
		finally
		{
			if (ReferenceEquals(_link, cts))
			{
				Busy.IsRunning = false;
				Busy.IsVisible = false;
			}
		}
	}
}
