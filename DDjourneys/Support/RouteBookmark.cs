using System.Windows.Input;
using DDjourneys.Controls;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>
/// The bookmark of a connection (start and destination), as every page that shows one offers it: one tap saves,
/// the next tap removes, and the icon says which of the two it is. Never disabled; without both places the tap
/// tells why instead. The planner, the results and the connection page share this.
/// </summary>
public sealed class RouteBookmark : ObservableObject
{
	private readonly PlaceStore _store;
	private readonly Func<(Location From, Location To)?> _route;
	private readonly Func<string, Task>? _tell;
	private readonly bool _hideWhenUnavailable;

	/// <param name="store">Where saved routes live.</param>
	/// <param name="route">The connection the page currently shows, or null while it has none.</param>
	/// <param name="tell">Shows a message (used when there is nothing to bookmark yet).</param>
	/// <param name="hideWhenUnavailable">Show no icon at all while there is no connection (pages that cannot get one).</param>
	public RouteBookmark(
		PlaceStore store,
		Func<(Location From, Location To)?> route,
		Func<string, Task>? tell = null,
		bool hideWhenUnavailable = false)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(route);

		_store = store;
		_route = route;
		_tell = tell;
		_hideWhenUnavailable = hideWhenUnavailable;

		ToggleCommand = new AsyncCommand(ToggleAsync);

		_store.Changed += OnStoreChanged;
		LocalizationService.Current.PropertyChanged += OnLocalizationChanged;
	}

	public ICommand ToggleCommand { get; }

	/// <summary>The current connection is saved.</summary>
	public bool IsSaved =>
		_route() is { } route
		&& _store.FindSavedRoute(route.From, route.To) is not null;

	public IconGlyph Glyph =>
		_hideWhenUnavailable && _route() is null
			? IconGlyph.None
			: IsSaved
				? IconGlyph.BookmarkFilled
				: IconGlyph.Bookmark;

	/// <summary>What a tap does, for screen readers and tooltips.</summary>
	public string Description =>
		IsSaved
			? LocalizationService.Current.CurrentStrings.Plan.ForgetSavedRoute
			: LocalizationService.Current.CurrentStrings.Plan.SaveRoute;

	/// <summary>Call when start or destination changed.</summary>
	public void Refresh()
	{
		OnPropertyChanged(nameof(IsSaved));
		OnPropertyChanged(nameof(Glyph));
		OnPropertyChanged(nameof(Description));
	}

	// Method handlers (not lambdas): both events hold their subscribers weakly.
	private void OnStoreChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(Refresh);

	private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(Refresh);

	private async Task ToggleAsync()
	{
		PlanStrings strings = LocalizationService.Current.CurrentStrings.Plan;

		if (_route() is not { } route)
		{
			if (_tell is not null)
			{
				await _tell(strings.StartAndDestinationRequired);
			}

			return;
		}

		string message;

		if (_store.FindSavedRoute(route.From, route.To) is { } saved)
		{
			_store.RemoveSavedRoute(saved);
			message = strings.RouteRemoved;
		}
		else
		{
			_store.AddSavedRoute(
				new SavedRoute(
					$"{route.From.Name} \u2192 {route.To.Name}",
					route.From,
					route.To));

			message = strings.RouteSaved;
		}

		Refresh();

		try
		{
			SemanticScreenReader.Announce(message);
		}
		catch (Exception)
		{
			// Announcements are a courtesy; the icon already shows the new state.
		}
	}
}
