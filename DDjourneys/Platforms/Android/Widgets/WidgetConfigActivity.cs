using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using ProviderInfo = DDjourneys.Core.Providers.Abstractions.ProviderInfo;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using Uri = Android.Net.Uri;
using WidgetOrientation = Android.Widget.Orientation;
using Button = Android.Widget.Button;
using ScrollView = Android.Widget.ScrollView;
using Switch = Android.Widget.Switch;
using View = Android.Views.View;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>
/// The settings of a widget, shown when it is added (and when it is reconfigured). One screen for all five widgets;
/// what it asks depends on the widget's kind. Plain Android views: it runs before (and without) the app's own UI.
/// </summary>
[Activity(
	Name = WidgetNames.Config,
	Label = "@string/widget_config_label",
	Exported = true,
	ExcludeFromRecents = true,
	Theme = "@android:style/Theme.DeviceDefault.Light")]
[IntentFilter([AppWidgetManager.ActionAppwidgetConfigure])]
public sealed class WidgetConfigActivity : Activity
{
	private const int LocationRequest = 1;

	private int _widgetId = AppWidgetManager.InvalidAppwidgetId;
	private WidgetConfig _config = new();
	private readonly WidgetStrings _strings = LocalizationService.Current.CurrentStrings.Widgets;
	private LinearLayout _form = null!;
	private EditText? _lines;
	private EditText? _title;
	private bool _built;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		bool night = ((Resources?.Configuration?.UiMode ?? 0) & UiMode.NightMask) == UiMode.NightYes;

		SetTheme(night ? global::Android.Resource.Style.ThemeDeviceDefault : global::Android.Resource.Style.ThemeDeviceDefaultLight);

		base.OnCreate(savedInstanceState);

		_widgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId;

		// Backing out must leave the widget unadded: the result is "cancelled" until Done is pressed.
		SetResult(Result.Canceled, new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, _widgetId));

		if (_widgetId == AppWidgetManager.InvalidAppwidgetId)
		{
			Finish();

			return;
		}

		AppWidgetManager manager = AppWidgetManager.GetInstance(this)!;

		WidgetKind kind =
			WidgetStore.LoadConfig(_widgetId)?.Kind
			?? WidgetNames.KindOf(manager.GetAppWidgetInfo(_widgetId)?.Provider?.ClassName)
			?? WidgetKind.Route;

		ProviderRegistry? registry = IPlatformApplication.Current?.Services.GetService<ProviderRegistry>();

		_config =
			WidgetStore.LoadConfig(_widgetId)
			?? new WidgetConfig
			{
				Kind = kind,
				ProviderId = registry?.SelectedId ?? string.Empty
			};

		BuildForm(registry);
	}

	// ---------- Layout ----------

	private void BuildForm(ProviderRegistry? registry)
	{
		WidgetStrings s = _strings;
		IUiStrings all = LocalizationService.Current.CurrentStrings;

		if (!_built)
		{
			var scroll = new ScrollView(this);

			scroll.SetFitsSystemWindows(true);

			_form = new LinearLayout(this) { Orientation = WidgetOrientation.Vertical };
			_form.SetPadding(Dp(20), Dp(16), Dp(20), Dp(24));

			scroll.AddView(_form);
			SetContentView(scroll);

			_built = true;
		}
		else
		{
			_form.RemoveAllViews();
		}

		_lines = null;

		AddTitle(KindName(_config.Kind));

		AddProvider(registry);

		_title = AddEdit(s.ConfigLabelTitle, KindName(_config.Kind), _config.Title);
		AddHint(s.ConfigTitleHint);

		switch (_config.Kind)
		{
			case WidgetKind.Route:
				AddPlace(s.ConfigFrom, () => _config.From, place => _config = _config with { From = place }, allowHere: true, wide: true);
				AddPlace(s.ConfigTo, () => _config.To, place => _config = _config with { To = place }, allowHere: true, wide: true);
				break;

			case WidgetKind.Departures:
			case WidgetKind.Arrivals:
				AddPlace(
					s.ConfigStop,
					() => _config.Stop is { } stop ? new WidgetPlace(stop) : null,
					place => _config = _config with { Stop = place.Place },
					allowHere: false,
					wide: false);
				break;

			case WidgetKind.NearbyStops:
				AddRadius();
				break;

			case WidgetKind.NearbyDepartures:
				AddRadius();

				AddChoice(
					s.ConfigStops,
					[1, 2, 3, 4, 5, 6],
					count => count.ToString(),
					_config.StopCount,
					count => _config = _config with { StopCount = count });

				AddChoice(
					s.ConfigPerStop,
					[1, 2, 3, 4, 5, 6],
					count => count.ToString(),
					_config.PerStop,
					count => _config = _config with { PerStop = count });
				break;
		}

		if (_config.Kind is WidgetKind.Departures or WidgetKind.Arrivals or WidgetKind.NearbyDepartures)
		{
			_lines = AddEdit(s.ConfigLines, s.LinesHint, _config.Lines);

			AddModes(all.Routing);
		}

		AddChoice(
			s.ConfigRows,
			[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
			rows => rows == 0 ? s.RowsAuto : rows.ToString(),
			_config.MaxRows,
			rows => _config = _config with { MaxRows = rows });

		AddRefresh();

		if (_config.Kind is WidgetKind.NearbyStops or WidgetKind.NearbyDepartures or WidgetKind.Route)
		{
			AddLocation();
		}

		AddButtons(all);
	}

	/// <summary>
	/// Any provider the app has, whichever it shows itself. Places belong to one provider (their ids mean nothing to
	/// another), so changing it clears them and the form is built again.
	/// </summary>
	private void AddProvider(ProviderRegistry? registry)
	{
		IReadOnlyList<ProviderInfo> providers = registry?.Providers ?? [];

		if (providers.Count < 2)
		{
			string name = registry?.Find(_config.ProviderId)?.Name ?? _config.ProviderId;

			if (name.Length > 0)
			{
				AddHint($"{_strings.ConfigProvider}: {name}");
			}

			return;
		}

		AddLabel(_strings.ConfigProvider);

		var spinner = new Spinner(this);

		var adapter =
			new ArrayAdapter<string>(
				this,
				global::Android.Resource.Layout.SimpleSpinnerItem,
				new List<string>(providers.Select(provider => provider.Name)));

		adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);

		spinner.Adapter = adapter;

		int index = providers.ToList().FindIndex(provider => string.Equals(provider.Id, _config.ProviderId, StringComparison.OrdinalIgnoreCase));

		spinner.SetSelection(Math.Max(index, 0));

		spinner.ItemSelected +=
			(_, e) =>
			{
				if (e.Position < 0 || e.Position >= providers.Count)
				{
					return;
				}

				string id = providers[e.Position].Id;

				if (string.Equals(id, _config.ProviderId, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}

				// Keep what was typed, drop what belongs to the old provider, and ask again.
				_config =
					_config with
					{
						ProviderId = id,
						From = null,
						To = null,
						Stop = null,
						Lines = _lines?.Text?.Trim() ?? _config.Lines,
						Title = _title?.Text?.Trim() ?? _config.Title
					};

				BuildForm(registry);

				Toast.MakeText(this, _strings.ConfigProviderChanged, ToastLength.Short)?.Show();
			};

		_form.AddView(spinner);
	}

	private string KindName(WidgetKind kind) =>
		kind switch
		{
			WidgetKind.Route => _strings.NameRoute,
			WidgetKind.Departures => _strings.NameDepartures,
			WidgetKind.Arrivals => _strings.NameArrivals,
			WidgetKind.NearbyStops => _strings.NameNearby,
			_ => _strings.NameNearbyDepartures
		};

	private void AddRadius() =>
		AddChoice(
			_strings.ConfigRadius,
			WidgetConfig.Radii,
			meters => string.Format(System.Globalization.CultureInfo.CurrentCulture, _strings.Meters, meters),
			_config.RadiusMeters,
			meters => _config = _config with { RadiusMeters = meters });

	private void AddRefresh()
	{
		AddSection();

		var auto =
			new Switch(this)
			{
				Text = _strings.ConfigAuto,
				Checked = _config.AutoRefresh
			};

		_form.AddView(auto);

		Spinner interval =
			AddChoice(
				_strings.ConfigInterval,
				WidgetConfig.Intervals,
				minutes => minutes % 60 == 0
					? string.Format(System.Globalization.CultureInfo.CurrentCulture, _strings.IntervalHours, minutes / 60)
					: string.Format(System.Globalization.CultureInfo.CurrentCulture, _strings.IntervalMinutes, minutes),
				_config.IntervalMinutes,
				minutes => _config = _config with { IntervalMinutes = minutes });

		interval.Enabled = _config.AutoRefresh;

		auto.CheckedChange +=
			(_, e) =>
			{
				_config = _config with { AutoRefresh = e.IsChecked };
				interval.Enabled = e.IsChecked;
			};

		AddHint(_strings.ConfigAutoHint);
	}

	private void AddModes(RoutingStrings routing)
	{
		(ModeFilter Mode, string Name)[] modes =
		[
			(ModeFilter.Tram, routing.Tram),
			(ModeFilter.CityBus, routing.CityBus),
			(ModeFilter.IntercityBus, routing.IntercityBus),
			(ModeFilter.SuburbanRailway, routing.SuburbanRailway),
			(ModeFilter.Train, routing.Train),
			(ModeFilter.Cableway, routing.Cableway),
			(ModeFilter.Ferry, routing.Ferry),
			(ModeFilter.HailedSharedTaxi, routing.HailedSharedTaxi)
		];

		AddLabel(_strings.ConfigModes);

		var button = new Button(this);

		void Refresh() =>
			button.Text =
				_config.Modes == ModeFilter.All
					? _strings.ModesAll
					: string.Join(", ", modes.Where(item => _config.Modes.HasFlag(item.Mode)).Select(item => item.Name));

		Refresh();

		button.Click +=
			(_, _) =>
			{
				bool[] selected = [.. modes.Select(item => _config.Modes.HasFlag(item.Mode))];

				new AlertDialog.Builder(this)
					.SetTitle(_strings.ConfigModes)!
					.SetMultiChoiceItems(
						modes.Select(item => item.Name).ToArray(),
						selected,
						(_, e) => selected[e.Which] = e.IsChecked)!
					.SetPositiveButton(
						LocalizationService.Current.CurrentStrings.Common.Ok,
						(_, _) =>
						{
							ModeFilter chosen =
								modes
									.Where((_, index) => selected[index])
									.Aggregate(ModeFilter.None, (all, item) => all | item.Mode);

							// Nothing chosen means everything: an empty list could never show a departure.
							_config = _config with { Modes = chosen == ModeFilter.None ? ModeFilter.All : chosen };

							Refresh();
						})!
					.SetNegativeButton(LocalizationService.Current.CurrentStrings.Common.Cancel, (_, _) => { })!
					.Show();
			};

		_form.AddView(button);
	}

	private void AddLocation()
	{
		AddSection();
		AddHint(_strings.LocationHint);

		if (AndroidWidgetLocation.IsAllowed(this))
		{
			return;
		}

		var allow = new Button(this) { Text = LocalizationService.Current.CurrentStrings.Departures.UseMyLocation };
		allow.Click +=
			(_, _) =>
			{
				RequestPermissions(
					[global::Android.Manifest.Permission.AccessFineLocation, global::Android.Manifest.Permission.AccessCoarseLocation],
					LocationRequest);
			};

		_form.AddView(allow);

		var settings = new Button(this) { Text = _strings.OpenSettings };

		settings.Click +=
			(_, _) =>
			{
				var intent = new Intent(Settings.ActionApplicationDetailsSettings, Uri.FromParts("package", PackageName, null));

				StartActivity(intent);
			};

		_form.AddView(settings);
	}

	private void AddButtons(IUiStrings all)
	{
		AddSection();

		var row = new LinearLayout(this) { Orientation = WidgetOrientation.Horizontal };

		var cancel = new Button(this) { Text = all.Common.Cancel };

		cancel.Click += (_, _) => Finish();

		var done = new Button(this) { Text = _strings.Done };

		done.Click += (_, _) => Save();

		row.AddView(cancel, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
		row.AddView(done, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));

		_form.AddView(row);
	}

	// ---------- Save ----------

	private void Save()
	{
		_config = _config with { Lines = _lines?.Text?.Trim() ?? _config.Lines, Title = _title?.Text?.Trim() ?? _config.Title };

		if (!_config.IsComplete)
		{
			Toast.MakeText(this, _strings.ChoosePlace, ToastLength.Short)?.Show();

			return;
		}

		WidgetStore.SaveConfig(_widgetId, _config);
		WidgetStore.ClearSnapshot(_widgetId);

		SetResult(Result.Ok, new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, _widgetId));

		// The first draw is ours to make: a widget with a settings screen gets no first update from the system.
		Context context = ApplicationContext ?? this;
		int id = _widgetId;

		_ = Task.Run(() => WidgetUpdater.UpdateAsync(context, [id], WidgetUpdateReason.Configured));

		Finish();
	}

	// ---------- View helpers ----------

	private int Dp(int value) =>
		(int)TypedValue.ApplyDimension(ComplexUnitType.Dip, value, Resources?.DisplayMetrics);

	private void AddTitle(string text)
	{
		var title = new TextView(this) { Text = text };

		title.SetTextSize(ComplexUnitType.Sp, 22);
		title.SetTypeface(title.Typeface, global::Android.Graphics.TypefaceStyle.Bold);

		_form.AddView(title);
	}

	private void AddLabel(string text)
	{
		var label = new TextView(this) { Text = text };

		label.SetTextSize(ComplexUnitType.Sp, 13);
		label.SetPadding(0, Dp(14), 0, 0);

		_form.AddView(label);
	}

	private void AddHint(string text)
	{
		var hint = new TextView(this) { Text = text };

		hint.SetTextSize(ComplexUnitType.Sp, 12);
		hint.SetPadding(0, Dp(6), 0, 0);
		hint.Alpha = 0.7f;

		_form.AddView(hint);
	}

	private void AddSection()
	{
		var gap = new View(this);

		_form.AddView(gap, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(12)));
	}

	private EditText AddEdit(string label, string hint, string value)
	{
		AddLabel(label);

		var edit =
			new EditText(this)
			{
				Hint = hint,
				InputType = InputTypes.ClassText
			};

		edit.SetSingleLine(true);
		edit.Text = value;

		_form.AddView(edit);

		return edit;
	}

	private void AddPlace(string label, Func<WidgetPlace?> get, Action<WidgetPlace> set, bool allowHere, bool wide)
	{
		AddLabel(label);

		var button = new Button(this);

		void Refresh() =>
			button.Text =
				get() is { } place
					? place.IsHere ? _strings.Here : place.Place?.Name ?? _strings.ChoosePlace
					: _strings.ChoosePlace;

		Refresh();

		button.Click +=
			(_, _) =>
				PlacePickerDialog.Show(
					this,
					_config.ProviderId,
					allowHere,
					wide,
					place =>
					{
						set(place);
						Refresh();
					});

		_form.AddView(button);
	}

	private Spinner AddChoice(string label, IReadOnlyList<int> values, Func<int, string> text, int current, Action<int> chosen)
	{
		AddLabel(label);

		var spinner = new Spinner(this);

		var adapter =
			new ArrayAdapter<string>(
				this,
				global::Android.Resource.Layout.SimpleSpinnerItem,
				new List<string>(values.Select(text)));

		adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);

		spinner.Adapter = adapter;

		int index = values.ToList().IndexOf(current);

		spinner.SetSelection(index >= 0 ? index : 0);

		spinner.ItemSelected +=
			(_, e) =>
			{
				if (e.Position >= 0 && e.Position < values.Count)
				{
					chosen(values[e.Position]);
				}
			};

		_form.AddView(spinner);

		return spinner;
	}
}
