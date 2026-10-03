using DDjourneys.Pages;
using DDjourneys.Support;

namespace DDjourneys.Tracking;

/// <summary>
/// Brings the overview of followed journeys into view, optionally focused on one plan. Platform
/// entry points (a tapped notification, a link from another app) only <see cref="Request"/>; the
/// request is delivered once the UI is ready, which on a cold start can take a moment.
/// </summary>
public sealed class TrackedJourneyNavigator
{
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
	private const int MaxAttempts = 40;

	private readonly Lock _gate = new();
	private string? _pending;
	private bool _delivering;

	/// <summary>Remembers the wish; a later request replaces an undelivered one.</summary>
	/// <param name="planId">The plan to focus; null or empty opens the overview without focus.</param>
	public void Request(string? planId)
	{
		lock (_gate)
		{
			_pending = planId ?? string.Empty;
		}
	}

	/// <summary>Delivers a pending request, waiting for the shell if necessary. Never throws.</summary>
	public async Task DeliverAsync()
	{
		lock (_gate)
		{
			if (_delivering || _pending is null)
			{
				return;
			}

			_delivering = true;
		}

		try
		{
			while (true)
			{
				string? planId;

				lock (_gate)
				{
					planId = _pending;
				}

				if (planId is null)
				{
					return;
				}

				bool delivered =
					await MainThread.InvokeOnMainThreadAsync(() => TryNavigateAsync(planId));

				if (delivered)
				{
					lock (_gate)
					{
						if (_pending == planId)
						{
							_pending = null;
						}
					}

					return;
				}

				await Task.Delay(RetryDelay);
			}
		}
		finally
		{
			lock (_gate)
			{
				_delivering = false;
			}
		}
	}
	private static async Task<bool> TryNavigateAsync(string planId)
	{
		if (Shell.Current is not { CurrentPage: { } current } shell)
		{
			return false;
		}

		string? focus = planId.Length == 0 ? null : planId;

		if (current is TrackedJourneysPage page)
		{
			page.Focus(focus);

			return true;
		}

		await shell.GoToAsync(
			Routes.Tracked,
			new ShellNavigationQueryParameters
			{
				[Routes.FocusPlan] = focus ?? string.Empty
			});

		return true;
	}
}
