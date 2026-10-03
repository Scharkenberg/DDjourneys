using System.ComponentModel;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>
/// Base for view models that listen to long-lived event sources (localization, the place store,
/// the tracker). Everything a view model subscribes to is registered with <see cref="Own"/> or one
/// of the helpers, and released by <see cref="Dispose"/>, which the page calls when it is
/// torn down (see <see cref="PageTeardown"/>). Work posted to the UI thread should check
/// <see cref="IsDisposed"/> first.
/// </summary>
public abstract class DisposableViewModel : ObservableObject, IDisposable
{
	private readonly List<Action> _releases = [];
	private bool _disposed;

	public bool IsDisposed => _disposed;

	/// <summary>Registers an action that undoes a subscription; it runs once, in reverse order.</summary>
	protected void Own(Action release)
	{
		ArgumentNullException.ThrowIfNull(release);

		if (_disposed)
		{
			release();

			return;
		}

		_releases.Add(release);
	}

	/// <summary>Attaches now and detaches on dispose.</summary>
	protected void Subscribe(Action attach, Action detach)
	{
		ArgumentNullException.ThrowIfNull(attach);
		ArgumentNullException.ThrowIfNull(detach);

		if (_disposed)
		{
			return;
		}

		attach();
		Own(detach);
	}

	/// <summary>Listens to language changes until disposed.</summary>
	protected void ListenToLocalization(
		LocalizationService localization,
		PropertyChangedEventHandler handler) =>
		Subscribe(
			() => localization.PropertyChanged += handler,
			() => localization.PropertyChanged -= handler);

	/// <summary>Releases subscriptions first, then lets the view model stop its own work. Idempotent.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			OnDisposing();
		}
		finally
		{
			for (int i = _releases.Count - 1; i >= 0; i--)
			{
				try
				{
					_releases[i]();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Releasing a subscription failed: {ex.Message}");
				}
			}

			_releases.Clear();
		}
	}

	/// <summary>Cancel pending work here (searches, polling); subscriptions are released afterwards.</summary>
	protected virtual void OnDisposing()
	{
	}
}
