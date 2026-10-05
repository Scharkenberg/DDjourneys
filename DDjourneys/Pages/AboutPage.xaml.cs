using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The README of the project, shown as it is. The file is part of the app package (see the MauiAsset entry in the
/// project file), so it is there without a network and always matches the version of the app that shows it.
/// </summary>
public partial class AboutPage : ContentPage
{
	/// <summary>Name of the document inside the app package.</summary>
	public const string AssetName = "README.md";

	private bool _loaded;

	public AboutPage()
	{
		InitializeComponent();
		Document.LinkTapped += OnLinkTapped;
		Motion.Prepare(this);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		Motion.EnterPage(this);

		if (_loaded)
		{
			return;
		}

		_loaded = true;

		try
		{
			// Reading and shaping the text is off the UI thread; only the views are built here.
			string text =
				await Task.Run(
					async () =>
					{
						await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(AssetName);
						using var reader = new StreamReader(stream);

						return await reader.ReadToEndAsync();
					});

			Document.Markdown = text;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"About page: {AssetName} not read: {ex.Message}");

			Message.Text = LocalizationService.Current.CurrentStrings.Common.SomethingWentWrong;
			Message.IsVisible = true;
		}
	}

	private static async void OnLinkTapped(object? sender, string address)
	{
		try
		{
			// Only web addresses and mail leave the app; a relative path in the text has nowhere to go.
			if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
				&& uri.Scheme is "https" or "http" or "mailto")
			{
				await Launcher.Default.OpenAsync(uri);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"About page: link not opened: {ex.Message}");
		}
	}
}
