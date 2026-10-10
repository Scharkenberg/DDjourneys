using System.Globalization;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Support;
using DDjourneys.Support.Widgets;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>
/// Sets the Windows widgets of the app up: which stop a departures widget shows, which route a route
/// widget follows, how many rows. Widgets are pinned from the Widgets Board itself (there is no pin API);
/// this page configures what a pinned widget shows, and the set-up card of a fresh widget opens it.
/// The editor shows what the widget has now, one tap on a field chooses which place the search fills, and
/// Save waits until the widget has what it needs to show anything.
/// </summary>
public partial class WidgetSetupPage : PanePage, IQueryAttributable
{
	private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(350);

	private enum Field
	{
		Stop,
		From,
		To
	}

	private readonly IWidgetStore _store;
	private readonly LocationService _locations;
	private readonly ProviderRegistry _providers;

	private string? _id;
	private WidgetKind _kind = WidgetKind.Departures;
	private Field _field = Field.Stop;
	private Location? _stop;
	private Location? _from;
	private Location? _to;
	private int _rows = 5;
	private CancellationTokenSource? _search;

	public WidgetSetupPage(
		IWidgetStore store,
		LocationService locations,
		ProviderRegistry providers)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_store = store;
		_locations = locations;
		_providers = providers;

		Rows.Value = _rows;
	}

	private static WidgetStrings Strings =>
		LocalizationService.Current.CurrentStrings.Widgets;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.WidgetId, out object? value)
			&& value is string id
			&& id.Length > 0)
		{
			Open(id);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		Reload();
	}

	protected override void OnDisappearing()
	{
		_search?.Cancel();

		base.OnDisappearing();
	}

	private void Reload()
	{
		Pinned.Children.Clear();

		string[] ids = PinnedIds();

		for (int index = 0; index < ids.Length; index++)
		{
			string id = ids[index];
			WidgetConfig? config = _store.LoadConfig(id);
			string kind = config?.Kind is WidgetKind.Route
				? Strings.SetupKindRoute
				: Strings.SetupKindDepartures;

			var row =
				new Grid
				{
					ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
					ColumnSpacing = 10,
					Padding = new Thickness(12, 8)
				};

			// What the widget shows, in words a person knows: its title or its place, never the id.
			var text = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };

			text.Add(new Label { Text = Describe(config, kind, index + 1), LineBreakMode = LineBreakMode.TailTruncation, StyleClass = ["Title"] });
			text.Add(new Label { Text = config is { IsComplete: true } ? kind : Strings.CardSetUp, StyleClass = ["Caption"] });

			row.Add(text);

			Button open = new() { Text = Strings.CardSetUp, StyleClass = ["ChipButton"] };
			open.Clicked += (_, _) => Open(id);
			row.Add(open, 1);

			Pinned.Add(row);
			Pinned.Add(new BoxView { StyleClass = ["Divider"] });
		}

		if (Pinned.Children.Count == 0)
		{
			Pinned.Add(new Label { Text = Strings.SetupNone, StyleClass = ["Caption"] });
		}
	}

	/// <summary>A widget by its title, else its stop or route, else its kind and number.</summary>
	private static string Describe(WidgetConfig? config, string kind, int number)
	{
		if (config is null)
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.EditorFor, number);
		}

		if (config.Title.Length > 0)
		{
			return config.Title;
		}

		return config.Kind switch
		{
			WidgetKind.Route when config.From?.Place is { } start && config.To?.Place is { } end =>
				$"{start.Name} \u2192 {end.Name}",
			WidgetKind.Departures when config.Stop is { } stop => stop.Name,
			_ => string.Format(CultureInfo.CurrentCulture, "{0} \u00b7 {1}", kind, number)
		};
	}

	private void Open(string id)
	{
		_id = id;

		// The editor starts from what the widget has, not from what the last one left in the fields.
		WidgetConfig? config = _store.LoadConfig(id);

		_kind = config?.Kind is WidgetKind.Route ? WidgetKind.Route : WidgetKind.Departures;
		_stop = config?.Stop;
		_from = config?.From?.Place;
		_to = config?.To?.Place;
		_rows = config is { MaxRows: > 0 } ? Math.Clamp(config.MaxRows, 1, WidgetConfig.MaxRowsLimit) : 5;

		string[] ids = PinnedIds();
		int number = Math.Max(0, Array.IndexOf(ids, id)) + 1;

		EditorTitle.Text = string.Format(CultureInfo.CurrentCulture, Strings.EditorFor, number);

		bool wasHidden = !Editor.IsVisible;

		Editor.IsVisible = true;
		SavedNote.IsVisible = false;

		Rows.Value = _rows;
		Search.Text = string.Empty;

		ApplyKind();

		if (wasHidden)
		{
			_ = Motion.RevealAsync(Editor);
		}
	}

	private void ApplyKind()
	{
		KindDepartures.Text = Strings.SetupKindDepartures;
		KindRoute.Text = Strings.SetupKindRoute;
		Save.Text = Strings.SetupSave;

		KindDepartures.StyleClass = _kind is WidgetKind.Departures ? ["ChipButton"] : ["Chip"];
		KindRoute.StyleClass = _kind is WidgetKind.Route ? ["ChipButton"] : ["Chip"];

		StopField.IsVisible = _kind is WidgetKind.Departures;
		FromField.IsVisible = _kind is WidgetKind.Route;
		ToField.IsVisible = _kind is WidgetKind.Route;

		// The search fills the first place that is still open.
		_field =
			_kind is WidgetKind.Route
				? (_from is null ? Field.From : _to is null ? Field.To : _field is Field.To ? Field.To : Field.From)
				: Field.Stop;

		Results.Children.Clear();

		RefreshFields();
	}

	/// <summary>Each field says what it holds (or that it holds nothing yet); the one being filled is the filled-in chip.</summary>
	private void RefreshFields()
	{
		SetField(StopField, Strings.SetupStop, _stop, Field.Stop);
		SetField(FromField, Strings.SetupFrom, _from, Field.From);
		SetField(ToField, Strings.SetupTo, _to, Field.To);

		RowsCaption.Text = string.Format(CultureInfo.CurrentCulture, Strings.SetupRowsCount, _rows);

		Save.IsEnabled = IsComplete;
	}

	private void SetField(Button button, string label, Location? place, Field field)
	{
		button.Text = $"{label}: {place?.Name ?? Strings.SetupNotChosen}";
		button.StyleClass = _field == field ? ["ChipButton"] : ["Chip"];

		SemanticProperties.SetDescription(button, button.Text);
	}

	private bool IsComplete =>
		_kind is WidgetKind.Route
			? _from is not null && _to is not null
			: _stop is not null;

	private void OnKindDepartures(object? sender, EventArgs e)
	{
		_kind = WidgetKind.Departures;
		ApplyKind();
	}

	private void OnKindRoute(object? sender, EventArgs e)
	{
		_kind = WidgetKind.Route;
		ApplyKind();
	}

	private void OnStopField(object? sender, EventArgs e) => PickField(Field.Stop);

	private void OnFromField(object? sender, EventArgs e) => PickField(Field.From);

	private void OnToField(object? sender, EventArgs e) => PickField(Field.To);

	private void PickField(Field field)
	{
		_field = field;

		Results.Children.Clear();
		Search.Text = string.Empty;
		Search.Focus();

		RefreshFields();
	}

	/// <summary>A search as the person types: after a short pause, newest question wins.</summary>
	private async void OnSearchChanged(object? sender, TextChangedEventArgs e)
	{
		_search?.Cancel();

		string query = e.NewTextValue?.Trim() ?? string.Empty;

		if (query.Length < 2)
		{
			Results.Children.Clear();
			Searching.IsVisible = false;
			Searching.IsRunning = false;

			return;
		}

		var cancel = new CancellationTokenSource();

		_search = cancel;

		try
		{
			await Task.Delay(SearchDelay, cancel.Token);

			Searching.IsVisible = true;
			Searching.IsRunning = true;

			IReadOnlyList<Location> found =
				await _locations.SearchAsync(query, kinds: PlaceKinds.Stops, cancellationToken: cancel.Token);

			if (cancel.IsCancellationRequested)
			{
				return;
			}

			Results.Children.Clear();

			foreach (Location place in found.Take(8))
			{
				Button row =
					new()
					{
						Text = place.Name,
						HorizontalOptions = LayoutOptions.Fill,
						StyleClass = ["Chip"]
					};
				row.Clicked += (_, _) => Choose(place);
				Results.Add(row);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Widgets] stop search failed: {ex.Message}");
		}
		finally
		{
			if (ReferenceEquals(_search, cancel))
			{
				Searching.IsVisible = false;
				Searching.IsRunning = false;
			}
		}
	}

	private void Choose(Location place)
	{
		switch (_field)
		{
			case Field.Stop:
				_stop = place;
				break;

			case Field.From:
				_from = place;

				// On to the destination when it is still open.
				_field = _to is null ? Field.To : Field.From;
				break;

			case Field.To:
				_to = place;
				break;
		}

		Results.Children.Clear();
		Search.Text = string.Empty;

		RefreshFields();
	}

	private void OnRowsChanged(object? sender, ValueChangedEventArgs e)
	{
		_rows = Math.Clamp((int)e.NewValue, 1, WidgetConfig.MaxRowsLimit);

		RefreshFields();
	}

	private async void OnSave(object? sender, EventArgs e)
	{
		if (_id is not { Length: > 0 } id
			|| !IsComplete)
		{
			return;
		}

		string provider = _providers.Selected?.Id ?? string.Empty;

		WidgetConfig existing =
			_store.LoadConfig(id) ?? new WidgetConfig { ProviderId = provider };

		WidgetConfig config =
			_kind is WidgetKind.Route
				? existing with
					{
						Kind = _kind,
						ProviderId = provider,
						MaxRows = _rows,
						Stop = null,
						From = new WidgetPlace(_from),
						To = new WidgetPlace(_to)
					}
				: existing with
					{
						Kind = _kind,
						ProviderId = provider,
						MaxRows = _rows,
						Stop = _stop,
						From = null,
						To = null
					};

		_store.SaveConfig(id, config);
		_store.ClearSnapshot(id);

		SavedNote.Text = Strings.SetupSaved;
		SavedNote.IsVisible = true;

#if WINDOWS
		await Platforms.Windows.Widgets.WidgetUpdaterWin.RefreshAsync(id);
#else
		await Task.CompletedTask;
#endif

		Reload();
	}

	private async void OnDiagnostics(object? sender, EventArgs e)
	{
		DiagButton.IsEnabled = false;

		try
		{
#if WINDOWS
			string report = await Platforms.Windows.Widgets.WindowsWidgets.DiagnosticsAsync();
#else
			string report = "Windows widgets exist on Windows only.";

			await Task.CompletedTask;
#endif
			DiagnosticLog.Write($"[Widgets] diagnostics:{Environment.NewLine}{report}");

			await Clipboard.Default.SetTextAsync(report);

			DiagText.Text = report + Environment.NewLine + Strings.DiagCopied;
			DiagText.IsVisible = true;
		}
		catch (Exception ex)
		{
			DiagText.Text = ex.Message;
			DiagText.IsVisible = true;
		}
		finally
		{
			DiagButton.IsEnabled = true;
		}
	}

	private static string[] PinnedIds()
	{
#if WINDOWS
		return Platforms.Windows.Widgets.WindowsWidgets.PinnedIds();
#else
		return [];
#endif
	}
}
