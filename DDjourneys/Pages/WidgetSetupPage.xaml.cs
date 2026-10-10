using System.Globalization;
using DDjourneys.Controls;
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
/// The list shows the widgets of the board, one tap opens the editor under it; the editor saves by itself as
/// soon as the widget has what it needs to show anything (no Save button, like the system's settings).
/// </summary>
public partial class WidgetSetupPage : PanePage, IQueryAttributable
{
	private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(350);
	private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

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
	private bool _dirty;
	private bool _loading;
	private CancellationTokenSource? _search;
	private CancellationTokenSource? _save;

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

		// One widget that still needs its settings: no need to ask which one.
		if (_id is null
			&& PinnedIds() is { Length: 1 } only
			&& _store.LoadConfig(only[0]) is not { IsComplete: true })
		{
			Open(only[0]);
		}
	}

	protected override void OnDisappearing()
	{
		_search?.Cancel();

		// What was changed a moment ago is not lost by leaving.
		SaveNow();

		base.OnDisappearing();
	}

	// ---------- The widgets on the board ----------

	private void Reload()
	{
		Pinned.Children.Clear();

		string[] ids = PinnedIds();

		for (int index = 0; index < ids.Length; index++)
		{
			string id = ids[index];

			Pinned.Add(BuildRow(id, _store.LoadConfig(id), index + 1, string.Equals(id, _id, StringComparison.Ordinal)));

			if (index < ids.Length - 1)
			{
				Pinned.Add(new BoxView { StyleClass = ["Divider"] });
			}
		}

		if (ids.Length == 0)
		{
			var none =
				new Label
				{
					Text = Strings.SetupNone,
					StyleClass = ["Caption"],
					Margin = new Thickness(12, 14)
				};

			Pinned.Add(none);
		}
	}

	/// <summary>One widget of the board: its icon, what it shows in words a person knows (never the id), and its state.</summary>
	private View BuildRow(string id, WidgetConfig? config, int number, bool selected)
	{
		bool complete = config is { IsComplete: true };
		bool route = config?.Kind is WidgetKind.Route;

		var row =
			new Grid
			{
				ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
				ColumnSpacing = 12,
				Padding = new Thickness(12, 8)
			};

		Dense.SetMinHeight(row, Token("HitRow", 52));
		Motion.SetFeedback(row, true);
		row.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "AccentSoft" : "Clear");

		var tile =
			new Border
			{
				WidthRequest = 40,
				HeightRequest = 40,
				Padding = 0,
				StrokeThickness = 0,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
				VerticalOptions = LayoutOptions.Center
			};

		tile.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "Surface" : "AccentSoft");

		var glyph =
			new Icon
			{
				Glyph = route ? IconGlyph.Route : IconGlyph.Clock,
				Size = 22,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center
			};

		glyph.SetDynamicResource(Icon.ColorProperty, "Accent");

		tile.Content = glyph;
		row.Add(tile);

		var text = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };

		var title =
			new Label
			{
				Text = Describe(config, number),
				LineBreakMode = LineBreakMode.TailTruncation,
				MaxLines = 1
			};

		title.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");

		string kind = route ? Strings.SetupKindRoute : Strings.SetupKindDepartures;

		text.Add(title);
		text.Add(
			new Label
			{
				Text = complete
					? $"{kind} · {string.Format(CultureInfo.CurrentCulture, Strings.SetupRowsCount, config!.MaxRows > 0 ? config.MaxRows : 5)}"
					: kind,
				StyleClass = ["Caption"],
				LineBreakMode = LineBreakMode.TailTruncation,
				MaxLines = 1
			});

		row.Add(text, 1);

		if (complete)
		{
			var chevron = new Icon { Glyph = selected ? IconGlyph.ChevronDown : IconGlyph.ChevronRight, Size = 20, VerticalOptions = LayoutOptions.Center };

			chevron.SetDynamicResource(Icon.ColorProperty, "InkMuted");
			row.Add(chevron, 2);
		}
		else
		{
			// Not a control: the state of the widget, so it is a pill.
			var state =
				new Border
				{
					StrokeThickness = 0,
					Padding = new Thickness(10, 3),
					VerticalOptions = LayoutOptions.Center,
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(100) }
				};

			state.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "Surface" : "AccentSoft");

			var label = new Label { Text = Strings.SetupNotSetUp, FontSize = 12, LineBreakMode = LineBreakMode.NoWrap, MaxLines = 1 };

			label.SetDynamicResource(Label.TextColorProperty, "Accent");
			label.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");

			state.Content = label;
			row.Add(state, 2);
		}

		SemanticProperties.SetDescription(row, $"{title.Text}, {kind}{(complete ? string.Empty : ", " + Strings.SetupNotSetUp)}");

		var tap = new TapGestureRecognizer();

		tap.Tapped += (_, _) => Open(id);
		row.GestureRecognizers.Add(tap);

		return row;
	}

	/// <summary>A widget by its title, else its stop or route, else "Widget n".</summary>
	private static string Describe(WidgetConfig? config, int number)
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
				$"{StopLabel.NameFor(start.Name, start.Place)} → {StopLabel.NameFor(end.Name, end.Place)}",
			WidgetKind.Departures when config.Stop is { } stop => StopLabel.NameFor(stop.Name, stop.Place),
			_ => string.Format(CultureInfo.CurrentCulture, Strings.EditorFor, number)
		};
	}

	// ---------- The editor ----------

	private void Open(string id)
	{
		// What was changed on the widget that is closed now is kept.
		SaveNow();

		_id = id;

		// The editor starts from what the widget has, not from what the last one left in the fields.
		WidgetConfig? config = _store.LoadConfig(id);

		_loading = true;

		_kind = config?.Kind is WidgetKind.Route ? WidgetKind.Route : WidgetKind.Departures;
		_stop = config?.Stop;
		_from = config?.From?.Place;
		_to = config?.To?.Place;
		_rows = config is { MaxRows: > 0 } ? Math.Clamp(config.MaxRows, 1, WidgetConfig.MaxRowsLimit) : 5;
		_dirty = false;

		string[] ids = PinnedIds();
		int number = Math.Max(0, Array.IndexOf(ids, id)) + 1;

		EditorTitle.Text = Describe(config, number);

		bool wasHidden = !Editor.IsVisible;

		Editor.IsVisible = true;

		Rows.Value = _rows;
		Search.Text = string.Empty;

		_loading = false;

		ApplyKind(first: true);
		Reload();

		if (wasHidden)
		{
			_ = Motion.RevealAsync(Editor);
		}
	}

	private void ApplyKind(bool first = false)
	{
		SegDeparturesLabel.Text = Strings.SetupKindDepartures;
		SegRouteLabel.Text = Strings.SetupKindRoute;

		SetSegment(SegDepartures, SegDeparturesLabel, SegDeparturesIcon, _kind is WidgetKind.Departures);
		SetSegment(SegRoute, SegRouteLabel, SegRouteIcon, _kind is WidgetKind.Route);

		StopField.IsVisible = _kind is WidgetKind.Departures;
		FromField.IsVisible = _kind is WidgetKind.Route;
		ToField.IsVisible = _kind is WidgetKind.Route;

		// The search fills the first place that is still open.
		_field =
			_kind is WidgetKind.Route
				? (_from is null ? Field.From : _to is null ? Field.To : _field is Field.To ? Field.To : Field.From)
				: Field.Stop;

		ClearResults();
		RefreshFields();

		if (!first)
		{
			Changed();
		}
	}

	private static void SetSegment(Border segment, Label label, Icon icon, bool on)
	{
		Themed.SetIsOn(segment, on);
		Themed.SetIsOn(label, on);

		icon.SetDynamicResource(Icon.ColorProperty, on ? "OnAccent" : "InkMuted");
	}

	/// <summary>Each field says what it holds (or that it holds nothing yet); the one being filled wears the accent outline.</summary>
	private void RefreshFields()
	{
		SetField(StopField, StopCaption, StopValue, Strings.SetupStop, _stop, Field.Stop);
		SetField(FromField, FromCaption, FromValue, Strings.SetupFrom, _from, Field.From);
		SetField(ToField, ToCaption, ToValue, Strings.SetupTo, _to, Field.To);

		RowsValue.Text = string.Format(CultureInfo.CurrentCulture, Strings.SetupRowsCount, _rows);

		RefreshStatus(saved: false);
	}

	private void SetField(Border border, Label caption, Label value, string label, Location? place, Field field)
	{
		caption.Text = label;
		value.Text = place is null ? Strings.SetupNotChosen : StopLabel.NameFor(place.Name, place.Place);
		value.SetDynamicResource(Label.TextColorProperty, place is null ? "InkMuted" : "Ink");

		Themed.SetIsOn(border, _field == field);

		SemanticProperties.SetDescription(border, $"{label}: {value.Text}. {Strings.SetupChange}");
	}

	private void RefreshStatus(bool saved)
	{
		if (IsComplete)
		{
			StatusIcon.Glyph = saved ? IconGlyph.Check : IconGlyph.Info;
			StatusIcon.SetDynamicResource(Icon.ColorProperty, saved ? "OnTime" : "InkMuted");
			StatusLabel.Text = saved ? Strings.SetupSaved : string.Empty;
		}
		else
		{
			StatusIcon.Glyph = IconGlyph.Info;
			StatusIcon.SetDynamicResource(Icon.ColorProperty, "InkMuted");
			StatusLabel.Text = _kind is WidgetKind.Route ? Strings.SetupIncompleteRoute : Strings.SetupIncompleteStop;
		}

		StatusIcon.IsVisible = StatusLabel.Text.Length > 0;
	}

	private bool IsComplete =>
		_kind is WidgetKind.Route
			? _from is not null && _to is not null
			: _stop is not null;

	private void OnKindDepartures(object? sender, EventArgs e)
	{
		if (_kind is WidgetKind.Departures)
		{
			return;
		}

		_kind = WidgetKind.Departures;
		ApplyKind();
	}

	private void OnKindRoute(object? sender, EventArgs e)
	{
		if (_kind is WidgetKind.Route)
		{
			return;
		}

		_kind = WidgetKind.Route;
		ApplyKind();
	}

	private void OnStopField(object? sender, EventArgs e) => PickField(Field.Stop);

	private void OnFromField(object? sender, EventArgs e) => PickField(Field.From);

	private void OnToField(object? sender, EventArgs e) => PickField(Field.To);

	private void PickField(Field field)
	{
		_field = field;

		ClearResults();
		Search.Text = string.Empty;
		Search.Focus();

		RefreshFields();
	}

	private void ClearResults()
	{
		Results.Children.Clear();
		ResultsCard.IsVisible = false;
	}

	/// <summary>A search as the person types: after a short pause, newest question wins.</summary>
	private async void OnSearchChanged(object? sender, TextChangedEventArgs e)
	{
		_search?.Cancel();

		string query = e.NewTextValue?.Trim() ?? string.Empty;

		if (query.Length < 2)
		{
			ClearResults();
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

			Location[] shown = [.. found.Take(8)];

			for (int index = 0; index < shown.Length; index++)
			{
				Results.Add(BuildResult(shown[index]));

				if (index < shown.Length - 1)
				{
					Results.Add(new BoxView { StyleClass = ["Divider"] });
				}
			}

			if (shown.Length == 0)
			{
				Results.Add(new Label { Text = Strings.NoResults, StyleClass = ["Caption"], Margin = new Thickness(12, 14) });
			}

			ResultsCard.IsVisible = true;
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

	private View BuildResult(Location place)
	{
		(string name, string? city) = StopLabel.Split(place.Name, place.Place);

		var row =
			new Grid
			{
				ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
				ColumnSpacing = 12,
				Padding = new Thickness(12, 8)
			};

		Dense.SetMinHeight(row, Token("HitRowTight", 44));
		Motion.SetFeedback(row, true);

		var pin = new Icon { Glyph = IconGlyph.MapPin, Size = 20, VerticalOptions = LayoutOptions.Center };

		pin.SetDynamicResource(Icon.ColorProperty, "InkMuted");
		row.Add(pin);

		var text = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };

		text.Add(new Label { Text = name, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });

		if (!string.IsNullOrWhiteSpace(city))
		{
			text.Add(new Label { Text = city, StyleClass = ["Faint"], LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
		}

		row.Add(text, 1);

		SemanticProperties.SetDescription(row, StopLabel.NameFor(place.Name, place.Place));

		var tap = new TapGestureRecognizer();

		tap.Tapped += (_, _) => Choose(place);
		row.GestureRecognizers.Add(tap);

		return row;
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

		ClearResults();
		Search.Text = string.Empty;

		RefreshFields();
		Changed();
	}

	private void OnRowsChanged(object? sender, ValueChangedEventArgs e)
	{
		if (_loading)
		{
			return;
		}

		int rounded = Math.Clamp((int)Math.Round(e.NewValue), 1, WidgetConfig.MaxRowsLimit);

		// The slider is continuous: it settles on whole rows.
		if (Math.Abs(Rows.Value - rounded) > 0.001)
		{
			_loading = true;
			Rows.Value = rounded;
			_loading = false;
		}

		if (rounded == _rows)
		{
			return;
		}

		_rows = rounded;

		RefreshFields();
		Changed();
	}

	// ---------- Saving ----------

	/// <summary>Something was changed: it is saved a moment after the last change, once the widget is complete.</summary>
	private async void Changed()
	{
		if (_loading)
		{
			return;
		}

		_dirty = true;

		_save?.Cancel();

		var cancel = new CancellationTokenSource();

		_save = cancel;

		try
		{
			await Task.Delay(SaveDelay, cancel.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		SaveNow();
	}

	private void SaveNow()
	{
		_save?.Cancel();

		if (!_dirty
			|| _id is not { Length: > 0 } id
			|| !IsComplete)
		{
			return;
		}

		_dirty = false;

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

		RefreshStatus(saved: true);
		Reload();

#if WINDOWS
		_ = Platforms.Windows.Widgets.WidgetUpdaterWin.RefreshAsync(id);
#endif
	}

	// ---------- Troubleshooting ----------

	private void OnDiagToggle(object? sender, EventArgs e)
	{
		DiagBody.IsVisible = !DiagBody.IsVisible;
		DiagChevron.Glyph = DiagBody.IsVisible ? IconGlyph.ChevronUp : IconGlyph.ChevronDown;
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

	private static double Token(string key, double fallback) =>
		Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is double number
			? number
			: fallback;

	private static string[] PinnedIds()
	{
#if WINDOWS
		return Platforms.Windows.Widgets.WindowsWidgets.PinnedIds();
#else
		return [];
#endif
	}
}
