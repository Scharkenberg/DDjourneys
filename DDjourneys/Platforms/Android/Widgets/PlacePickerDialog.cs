using Android.App;
using Android.Content;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Support;
using ListView = Android.Widget.ListView;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>
/// A search box with a list under it: the places the passenger has used (home, favourites, recents) until something
/// is typed, then what the provider finds. The widget's own provider answers, not the one the app shows.
/// </summary>
internal sealed class PlacePickerDialog
{
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

	private readonly Activity _activity;
	private readonly string _providerId;
	private readonly bool _allowHere;
	private readonly PlaceKinds _kinds;
	private readonly Action<WidgetPlace> _picked;
	private readonly WidgetStrings _strings;
	private readonly List<(string Text, WidgetPlace Place)> _entries = [];
	private readonly ArrayAdapter<string> _adapter;
	private readonly LocationService? _locations;
	private readonly ProviderRegistry? _registry;
	private readonly PlaceStore? _store;

	private CancellationTokenSource? _search;
	private AlertDialog? _dialog;

	private PlacePickerDialog(Activity activity, string providerId, bool allowHere, bool wide, Action<WidgetPlace> picked)
	{
		_activity = activity;
		_providerId = providerId;
		_allowHere = allowHere;
		_picked = picked;
		_kinds = wide ? PlaceKinds.Stops | PlaceKinds.Addresses | PlaceKinds.Pois : PlaceKinds.Stops;
		_strings = LocalizationService.Current.CurrentStrings.Widgets;

		IServiceProvider? services = IPlatformApplication.Current?.Services;

		_locations = services?.GetService<LocationService>();
		_registry = services?.GetService<ProviderRegistry>();
		_store = services?.GetService<PlaceStore>();

		_adapter = new ArrayAdapter<string>(activity, global::Android.Resource.Layout.SimpleListItem1, new List<string>());
	}

	public static void Show(Activity activity, string providerId, bool allowHere, bool wide, Action<WidgetPlace> picked) =>
		new PlacePickerDialog(activity, providerId, allowHere, wide, picked).Open();

	private void Open()
	{
		int pad = (int)(16 * _activity.Resources!.DisplayMetrics!.Density);

		var layout = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
		layout.SetPadding(pad, pad, pad, 0);

		var input =
			new EditText(_activity)
			{
				Hint = _strings.SearchHint,
				InputType = InputTypes.ClassText,
				ImeOptions = ImeAction.Search
			};

		input.SetSingleLine(true);

		var list = new ListView(_activity) { Adapter = _adapter };

		layout.AddView(input);
		layout.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(320 * _activity.Resources.DisplayMetrics.Density)));

		list.ItemClick +=
			(_, e) =>
			{
				if (e.Position >= 0 && e.Position < _entries.Count)
				{
					WidgetPlace place = _entries[e.Position].Place;

					_dialog?.Dismiss();
					_picked(place);
				}
			};

		input.TextChanged += (_, _) => OnTyped(input.Text ?? string.Empty);

		_dialog =
			new AlertDialog.Builder(_activity)
				.SetView(layout)!
				.SetNegativeButton(LocalizationService.Current.CurrentStrings.Common.Cancel, (_, _) => { })!
				.Create()!;

		_dialog.DismissEvent += (_, _) => _search?.Cancel();

		ShowDefaults();

		_dialog.Show();

		input.RequestFocus();
	}

	/// <summary>Before anything is typed: my location, home, favourites, recents of the provider the app shows.</summary>
	private void ShowDefaults()
	{
		var entries = new List<(string Text, WidgetPlace Place)>();

		if (_allowHere)
		{
			entries.Add((_strings.Here, WidgetPlace.Here));
		}

		// The passenger's places belong to the provider the app shows; for another provider they would be wrong ids.
		if (_store is not null
			&& _registry is not null
			&& string.Equals(_registry.SelectedId, _providerId, StringComparison.OrdinalIgnoreCase))
		{
			IEnumerable<Location> own =
				(_store.Home is { } home ? new[] { home } : [])
					.Concat(_store.Favourites)
					.Concat(_store.Recents)
					.Where(place => place.IsRoutable)
					.DistinctBy(place => place.StopKey ?? place.Id ?? place.Name);

			foreach (Location place in own.Take(12))
			{
				entries.Add((Text(place), new WidgetPlace(place)));
			}
		}

		Fill(entries);
	}

	private void OnTyped(string text)
	{
		_search?.Cancel();

		string query = text.Trim();

		if (query.Length < 2)
		{
			ShowDefaults();

			return;
		}

		var cts = new CancellationTokenSource();

		_search = cts;

		_ = SearchAsync(query, cts.Token);
	}

	private async Task SearchAsync(string query, CancellationToken token)
	{
		try
		{
			await Task.Delay(Debounce, token).ConfigureAwait(false);

			if (_locations is null)
			{
				return;
			}

			IReadOnlyList<Location> found;

			using (_registry?.Override(_providerId))
			{
				found =
					await _locations
						.SearchAsync(query, TimeSpan.FromSeconds(8), _kinds, token)
						.ConfigureAwait(false);
			}

			var entries =
				found
					.Where(place => place.IsRoutable)
					.Take(30)
					.Select(place => (Text(place), new WidgetPlace(place)))
					.ToList();

			if (!token.IsCancellationRequested)
			{
				_activity.RunOnUiThread(() => Fill(entries));
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Core.Diagnostics.DiagnosticLog.Write($"Widget place search failed: {ex.Message}");
		}
	}

	private void Fill(List<(string Text, WidgetPlace Place)> entries)
	{
		_entries.Clear();
		_entries.AddRange(entries);

		_adapter.Clear();

		foreach ((string text, _) in _entries)
		{
			_adapter.Add(text);
		}

		if (_entries.Count == 0)
		{
			_adapter.Add(_strings.NoResults);
		}

		_adapter.NotifyDataSetChanged();
	}

	private static string Text(Location place) =>
		string.IsNullOrWhiteSpace(place.Place)
			? place.Name
			: $"{place.Name}, {place.Place}";
}
