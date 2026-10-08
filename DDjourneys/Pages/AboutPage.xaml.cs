using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The README of the project, shown as it is, in the language of the app (German or else English). Both files are part
/// of the app package (EmbeddedResource entries in the project file), so they are there without a network and always match
/// the version of the app that shows them. The two files say the same; a test keeps their structure in step.
/// </summary>
public partial class AboutPage : ContentPage
{
	/// <summary>Name of the English document inside the app package.</summary>
	public const string ResourceName = "DDjourneys.README.md";

	/// <summary>Name of the German document inside the app package.</summary>
	public const string GermanResourceName = "DDjourneys.LIESMICH.md";

	private string? _loadedName;

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

		string preferredName = ResourceFor(LocalizationService.Current.LanguageCode);

		if (string.Equals(_loadedName, preferredName, StringComparison.Ordinal))
		{
			return;
		}

		try
		{
			await LoadAsync(preferredName);
			_loadedName = preferredName;
			Message.IsVisible = false;
			return;
		}
		catch (Exception ex) when (preferredName == GermanResourceName)
		{
			DiagnosticLog.Write($"About page: {preferredName} not read, trying English fallback: {ex.Message}");
		}

		try
		{
			await LoadAsync(ResourceName);
			_loadedName = ResourceName;
			Message.IsVisible = false;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"About page: {preferredName} and fallback could not be read: {ex.Message}");
			_loadedName = null;
			Message.Text = LocalizationService.Current.CurrentStrings.Common.SomethingWentWrong;
			Message.IsVisible = true;
		}
	}

	private async Task LoadAsync(string name)
	{
		// Reading and shaping the text is off the UI thread; only the views are built here.
		string text =
			await Task.Run(
				async () =>
				{
					await using Stream stream =
						typeof(AboutPage).Assembly.GetManifestResourceStream(name)
						?? throw new FileNotFoundException($"{name} is not embedded in the app.");

					using var reader = new StreamReader(stream);

					return await reader.ReadToEndAsync();
				});

		Document.Markdown = text;
	}

	/// <summary>The document for a UI language code ("de", "de-DE": German; anything else: English).</summary>
	public static string ResourceFor(string? languageCode) =>
		languageCode is { } code
		&& code.StartsWith("de", StringComparison.OrdinalIgnoreCase)
			? GermanResourceName
			: ResourceName;

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
