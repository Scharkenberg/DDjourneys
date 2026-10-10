using System.Collections.ObjectModel;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using DDjourneys.Support.Widgets;

namespace DDjourneys.Pages;

/// <summary>
/// Sets the Windows widgets of the app up: which stop a departures widget shows, which route a route
/// widget follows, how many rows. Widgets are pinned from the Widgets Board itself (there is no pin API);
/// this page configures what a pinned widget shows, and the set-up card of a fresh widget opens it.
/// </summary>
public partial class WidgetSetupPage : PanePage, IQueryAttributable
{
	private readonly IWidgetStore _store;
	private readonly LocationService _locations;
	private readonly ProviderRegistry _providers;
	private readonly AppSettings _settings;

	private string? _id;
	private WidgetKind _kind = WidgetKind.Departures;
	private Location? _stop;
	private Location? _from;
	private Location? _to;
	private int _rows = 5;

	public WidgetSetupPage(
		IWidgetStore store,
		LocationService locations,
		ProviderRegistry providers,
		AppSettings settings)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_store = store;
		_locations = locations;
		_providers = providers;
		_settings = settings;

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

	private void Reload()
	{
		Pinned.Children.Clear();

		foreach (string id in PinnedIds())
		{
			WidgetConfig? config = _store.LoadConfig(id);
			string kind = config?.Kind is WidgetKind.Route
					? Strings.SetupKindRoute
					: Strings.SetupKindDepartures;

			var row =
				new Grid
				{
					ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
					ColumnSpacing = 10,
				Padding = new(12, 8)
				};

			row.Add(new Label { Text = config?.Title?.Length > 0 ? config.Title : kind, VerticalOptions = LayoutOptions.Center, StyleClass = ["Title"] });
			row.Add(
				new Label
				{
					Text = kind,
					StyleClass = ["Caption"],
					VerticalOptions = LayoutOptions.Center
				},
				1);

			Button open = new() { Text = Strings.CardSetUp, StyleClass = ["ChipButton"] };
			open.Clicked += (_, _) => Open(id);
			row.Add(open, 2);

			Pinned.Add(row);
			Pinned.Add(new BoxView { StyleClass = ["Divider"] });
		}

		if (Pinned.Children.Count == 0)
		{
			Pinned.Add(new Label { Text = Strings.SetupNone, StyleClass = ["Caption"] });
		}
	}

	private void Open(string id)
	{
		_id = id;
		_kind = _store.LoadConfig(id)?.Kind ?? WidgetKind.Departures;

		EditorTitle.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.EditorFor, id);
		Editor.IsVisible = true;
		SavedNote.IsVisible = false;

		ApplyKind();
	}

	private void ApplyKind()
	{
		KindDepartures.Text = Strings.SetupKindDepartures;
		KindRoute.Text = Strings.SetupKindRoute;
		Save.Text = Strings.SetupSave;
		StopCaption.Text = _kind is WidgetKind.Route ? Strings.SetupFrom : Strings.SetupStop;

		KindDepartures.StyleClass = _kind is WidgetKind.Departures ? ["ChipButton"] : ["Chip"];
		KindRoute.StyleClass = _kind is WidgetKind.Route ? ["ChipButton"] : ["Chip"];

		Results.Children.Clear();
	}

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

	private async void OnSearchCompleted(object? sender, EventArgs e)
	{
		Results.Children.Clear();

		string query = Search.Text?.Trim() ?? string.Empty;

		if (query.Length == 0)
		{
			return;
		}

		try
		{
			IReadOnlyList<Location> found =
				await _locations.SearchAsync(query, kinds: PlaceKinds.Stops);

			foreach (Location place in found.Take(8))
			{
				Button row =
					new()
					{
						Text = place.Name,
						StyleClass = ["Chip"]
					};
				row.Clicked += (_, _) => Choose(place);
				Results.Add(row);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Widgets] stop search failed: {ex.Message}");
		}
	}

	private void Choose(Location place)
	{
		if (_kind is WidgetKind.Route)
		{
			if (_from is null)
			{
				_from = place;
				StopCaption.Text = Strings.SetupTo;
			}
			else
			{
				_to = place;
			}
		}
		else
		{
			_stop = place;
		}

		Search.Text = place.Name;
		Results.Children.Clear();
	}

	private void OnRowsChanged(object? sender, ValueChangedEventArgs e)
	{
		_rows = Math.Clamp((int)e.NewValue, 1, 10);
	}

	private async void OnSave(object? sender, EventArgs e)
	{
		if (_id is not { Length: > 0 } id)
		{
			return;
		}

		string provider = _providers.Selected?.Id ?? string.Empty;

		WidgetConfig existing =
			_store.LoadConfig(id) ?? new WidgetConfig { ProviderId = provider };

		// The first chosen stop of a route is the start, the second the destination; the kind decides which.
		WidgetConfig config =
			_kind is WidgetKind.Route
				? existing with
					{
						Kind = _kind,
						ProviderId = provider,
						MaxRows = _rows,
						Stop = null,
						From = _from is { } from ? new WidgetPlace(from) : existing.From,
						To = _to is { } to ? new WidgetPlace(to) : existing.To
					}
				: existing with
					{
						Kind = _kind,
						ProviderId = provider,
						MaxRows = _rows,
						Stop = _stop is { } stop ? stop : existing.Stop
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

	private static string[] PinnedIds()
	{
#if WINDOWS
		return Platforms.Windows.Widgets.WindowsWidgets.PinnedIds();
#else
		return [];
#endif
	}
}
