using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Localization;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// The Windows live notification: one app notification with a progress bar whose countdown and
/// progress are updated in place. Windows removes a notification when it is clicked (body or
/// button) and also when the user dismisses it; the two are told apart by the activation event.
/// </summary>
internal sealed partial class WindowsLiveJourneySurface : ILiveJourneySurface, IDisposable
{
	private const string LiveTag = "live";

	private static readonly TimeSpan ProgressTick = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(2500);
	private static readonly TimeSpan DismissGrace = TimeSpan.FromSeconds(2);

	private const int MaxAlerts = 8;

	private readonly TrackingCallbackBridge _bridge;
	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _wake = new(0);
	private readonly CancellationTokenSource _stop = new();

	private LiveJourneyContent? _content;
	private DateTimeOffset _basis;
	private string _layoutKey = string.Empty;
	private bool _live;
	private bool _dirty;
	private int _generation;
	private uint _sequence;
	private DateTimeOffset _nextTickAt;
	private DateTimeOffset? _restoreAt;
	private DateTimeOffset? _dismissAt;
	private string _lastSent = string.Empty;
	private DateTimeOffset? _retryAt;
	private readonly LinkedList<string> _alertTags = new();
	private Task _removal = Task.CompletedTask;
	private bool _shownBefore;
	private bool _loopStarted;
	private bool _disposed;

	public WindowsLiveJourneySurface(TrackingCallbackBridge bridge)
	{
		_bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
	}

	public bool IsSupported => true;

	public bool ShowsPaused => true;

	private static TrackingStrings Strings =>
		LocalizationService.Current.CurrentStrings.Tracking;

	public void Show(LiveJourneyContent content)
	{
		ArgumentNullException.ThrowIfNull(content);

		try
		{
			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				DateTimeOffset now = DateTimeOffset.UtcNow;
				string key = LayoutKeyOf(content);

				// A process that was started by a click must not greet the user with another banner.
				bool loud =
					(_content is null
						|| _content.PlanId != content.PlanId
						|| _content.Phase != content.Phase)
					&& !(WindowsNotificationHost.Embedded && !_shownBefore);

				_content = content;
				_basis = content.PositionAt ?? now;
				_shownBefore = true;

				if (!_live || key != _layoutKey)
				{
					TryReplaceLocked(content, key, now, loud);
				}
				else
				{
					_dirty = true;
				}

				EnsureLoopLocked();
			}

			WindowsBackground.SetKeepAlive(LiveBackgroundRules.KeepsProcessAlive(content));

			_wake.Release();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Windows live notification failed", ex);
		}
	}

	public void Dismiss()
	{
		try
		{
			lock (_gate)
			{
				_content = null;
				_live = false;
				_dirty = false;
				_generation++;
				_restoreAt = null;
				_dismissAt = null;
				_layoutKey = string.Empty;
				_lastSent = string.Empty;
				_retryAt = null;

				_removal = RemoveAsync(WindowsNotificationHost.LiveGroup, LiveTag);
			}

			WindowsBackground.SetKeepAlive(false);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Windows live notification removal failed", ex);
		}
	}

	public void ShowAlert(JourneyAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		try
		{
			var builder = new AppNotificationBuilder()
				.AddArgument(WindowsNotificationHost.SourceKey, WindowsNotificationHost.SourceAlert)
				.AddArgument(WindowsNotificationHost.ActionKey, TrackingActions.Open)
				.AddArgument(TrackingActions.PlanIdKey, alert.PlanId)
				.AddText(alert.Title)
				.AddText(alert.Text);

			AppNotification notification = builder.BuildNotification();

			notification.Tag = AlertTag(alert.PlanId, alert.Kind);
			notification.Group = WindowsNotificationHost.AlertGroup;
			notification.Expiration = DateTimeOffset.Now.AddHours(6);
			notification.SuppressDisplay = alert.Kind == JourneyAlertKind.Arrived;

			AppNotificationManager.Default.Show(notification);

			TrimAlerts(notification.Tag);

			// A new or replaced alert lands on top of the notification center; the live notification
			// goes back above it, so it is never pushed behind "see more" (replacing moves to the top).
			RaiseLive();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Windows alert failed", ex);
		}
	}

	/// <summary>An app may have 20 notifications, first in first out: alerts stay few so the live one is never evicted.</summary>
	private void TrimAlerts(string tag)
	{
		List<string> stale = [];

		lock (_alertTags)
		{
			_alertTags.Remove(tag);
			_alertTags.AddLast(tag);

			while (_alertTags.Count > MaxAlerts)
			{
				stale.Add(_alertTags.First!.Value);
				_alertTags.RemoveFirst();
			}
		}

		foreach (string old in stale)
		{
			_ = RemoveAsync(WindowsNotificationHost.AlertGroup, old);
		}
	}

	private void RaiseLive()
	{
		try
		{
			lock (_gate)
			{
				if (_content is { } content && _live && !_disposed)
				{
					TryReplaceLocked(content, LayoutKeyOf(content), DateTimeOffset.UtcNow, loud: false);
				}
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Raising the live notification failed", ex);
		}
	}

	public void ClearAlerts(string planId)
	{
		foreach (JourneyAlertKind kind in Enum.GetValues<JourneyAlertKind>())
		{
			_ = RemoveAsync(WindowsNotificationHost.AlertGroup, AlertTag(planId, kind));
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		try
		{
			AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
		}
		catch (Exception ex)
		{
			WindowsTrace.Write($"Windows notification detach failed: {ex.Message}");
		}

		_stop.Cancel();
	}

	// ----- Building -----

	private static string LayoutKeyOf(LiveJourneyContent content) =>
		LivePresentationRules.LayoutKey(content) + "|" + HasProgress(content);

	private static bool HasProgress(LiveJourneyContent content) =>
		content.When is not null || content.Segments.Sum() > 0;

	/// <summary>Shows the notification; a failure is logged and retried shortly instead of lost.</summary>
	private void TryReplaceLocked(LiveJourneyContent content, string key, DateTimeOffset now, bool loud)
	{
		try
		{
			ReplaceLocked(content, key, now, loud);
		}
		catch (Exception ex)
		{
			_live = false;
			_retryAt = now + TimeSpan.FromSeconds(15);

			WindowsTrace.Write("Showing the live notification failed", ex);
		}
	}

	private void ReplaceLocked(LiveJourneyContent content, string key, DateTimeOffset now, bool loud)
	{
		// A removal that is still under way must not take the new notification with it.
		_removal.Wait(TimeSpan.FromSeconds(1));

		var builder = new AppNotificationBuilder()
			.AddArgument(WindowsNotificationHost.SourceKey, WindowsNotificationHost.SourceLive)
			.AddArgument(WindowsNotificationHost.ActionKey, TrackingActions.Open)
			.AddArgument(TrackingActions.PlanIdKey, content.PlanId)
			.MuteAudio()
			.AddText(content.Title)
			.AddText(content.Text);

		bool progress = HasProgress(content);

		if (progress)
		{
			builder.AddProgressBar(
				new AppNotificationProgressBar()
					.BindTitle()
					.BindValue()
					.BindValueStringOverride()
					.BindStatus());
		}
		else if (!string.IsNullOrEmpty(content.SubText))
		{
			builder.AddText(content.SubText);
		}

		if (content.PlanId.Length > 0 && (content.Ongoing || content.Phase == TrackingPhase.Paused))
		{
			bool paused = content.Phase == TrackingPhase.Paused;

			builder.AddButton(
				new AppNotificationButton(paused ? Strings.Resume : Strings.NotifActionPause)
					.AddArgument(WindowsNotificationHost.SourceKey, WindowsNotificationHost.SourceLive)
					.AddArgument(WindowsNotificationHost.ActionKey, paused ? TrackingActions.Resume : TrackingActions.Pause)
					.AddArgument(TrackingActions.PlanIdKey, content.PlanId));

			builder.AddButton(
				new AppNotificationButton(Strings.NotifActionStop)
					.AddArgument(WindowsNotificationHost.SourceKey, WindowsNotificationHost.SourceLive)
					.AddArgument(WindowsNotificationHost.ActionKey, TrackingActions.Stop)
					.AddArgument(TrackingActions.PlanIdKey, content.PlanId));
		}

		AppNotification notification = builder.BuildNotification();

		notification.Tag = LiveTag;
		notification.Group = WindowsNotificationHost.LiveGroup;
		notification.SuppressDisplay = !loud;
		notification.Expiration =
			content.Phase == TrackingPhase.Paused
				? now.AddHours(12).ToLocalTime()
				: (content.When is { } when && when > now ? when : now.AddHours(2)).AddMinutes(30).ToLocalTime();

		if (progress)
		{
			notification.Progress = BuildProgress(content, now);
		}

		AppNotificationManager.Default.Show(notification);

		_live = true;
		_dirty = false;
		_layoutKey = key;
		_generation++;
		_restoreAt = null;
		_dismissAt = null;
		_retryAt = null;
		_lastSent = SignatureOf(content, now);
		_nextTickAt = now + TickInterval(content, now);
	}

	private AppNotificationProgressData BuildProgress(LiveJourneyContent content, DateTimeOffset now)
	{
		TimeSpan? remaining = LivePresentationRules.Remaining(content, now);

		string countdown =
			remaining is not { } left
				? string.Empty
				: left <= TimeSpan.Zero
					? Strings.NotifNow
					: string.Format(CultureInfo.CurrentCulture, Strings.NotifMinutes, LivePresentationRules.Minutes(left));

		// The headline and text above the bar already say it; the bar only adds what is new.
		string title =
			content.ShortText is { Length: > 0 } shortText
				&& shortText != content.Title
				&& shortText != content.Text
				? shortText
				: string.Empty;

		string status = content.SubText ?? string.Empty;

		return new AppNotificationProgressData(NextSequence())
		{
			Title = title,
			Value = ProgressOf(content, now),
			ValueStringOverride = countdown,
			Status = status
		};
	}

	private uint NextSequence() =>
		++_sequence;

	/// <summary>Position counts seconds; while the journey runs it advances with the clock.</summary>
	private double ProgressOf(LiveJourneyContent content, DateTimeOffset now)
	{
		int total = content.Segments.Sum();

		if (total <= 0)
		{
			return 0;
		}

		double position = content.Position;

		if (content.Phase is TrackingPhase.InProgress or TrackingPhase.AtInterchange or TrackingPhase.AtRisk)
		{
			position += Math.Max(0, (now - _basis).TotalSeconds);
		}

		return Math.Clamp(position / total, 0, 1);
	}

	private string SignatureOf(LiveJourneyContent content, DateTimeOffset now)
	{
		TimeSpan? remaining = LivePresentationRules.Remaining(content, now);

		return string.Concat(
			remaining is { } left ? LivePresentationRules.Minutes(left).ToString(CultureInfo.InvariantCulture) : "-",
			"|",
			((int)(ProgressOf(content, now) * 400)).ToString(CultureInfo.InvariantCulture));
	}

	private static TimeSpan TickInterval(LiveJourneyContent content, DateTimeOffset now)
	{
		TimeSpan interval =
			LivePresentationRules.Remaining(content, now) is { } remaining
				? LivePresentationRules.NextTickDelay(remaining)
				: ProgressTick;

		return interval < ProgressTick ? interval : ProgressTick;
	}

	private static string AlertTag(string planId, JourneyAlertKind kind) =>
		kind + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planId)))[..16];

	// ----- Refresh loop and activation -----

	private void EnsureLoopLocked()
	{
		if (_loopStarted)
		{
			return;
		}

		_loopStarted = true;

		try
		{
			AppNotificationManager.Default.NotificationInvoked += OnInvoked;
		}
		catch (Exception ex)
		{
			WindowsTrace.Write($"Windows notification attach failed: {ex.Message}");
		}

		_ = Task.Run(() => RunAsync(_stop.Token));
	}

	/// <summary>
	/// A click removed the notification, it was not dismissed: put it back (silently, into the
	/// action center) unless the action ended the live presentation in the meantime.
	/// </summary>
	private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
	{
		// Only a click on the live notification (or its buttons) removed it; alerts are separate.
		if (!args.Arguments.TryGetValue(WindowsNotificationHost.SourceKey, out string? source)
			|| source != WindowsNotificationHost.SourceLive)
		{
			return;
		}

		lock (_gate)
		{
			if (_content is null)
			{
				return;
			}

			_live = false;
			_dismissAt = null;
			_restoreAt = DateTimeOffset.UtcNow + RestoreDelay;
		}

		_wake.Release();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			TimeSpan wait;

			try
			{
				wait = await StepAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				WindowsTrace.Write($"Windows live notification refresh failed: {ex.Message}");

				wait = TimeSpan.FromSeconds(10);
			}

			try
			{
				await _wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task<TimeSpan> StepAsync()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		TimeSpan wait = TimeSpan.FromMinutes(1);
		AppNotificationProgressData? data = null;
		int generation;
		string? forwardPlan = null;

		lock (_gate)
		{
			if (_content is not { } content)
			{
				return Timeout.InfiniteTimeSpan;
			}

			generation = _generation;

			if (_restoreAt is { } restoreAt)
			{
				if (now >= restoreAt)
				{
					_restoreAt = null;

					TryReplaceLocked(content, LayoutKeyOf(content), now, loud: false);
				}
				else
				{
					wait = Min(wait, restoreAt - now);
				}

				return Max(wait, TimeSpan.FromMilliseconds(100));
			}

			if (_retryAt is { } retryAt)
			{
				if (now >= retryAt)
				{
					_retryAt = null;

					TryReplaceLocked(content, LayoutKeyOf(content), now, loud: true);
				}
				else
				{
					wait = Min(wait, retryAt - now);
				}

				return Max(wait, TimeSpan.FromMilliseconds(100));
			}

			if (_dismissAt is { } dismissAt)
			{
				if (now >= dismissAt)
				{
					_dismissAt = null;
					forwardPlan = content.PlanId;
				}
				else
				{
					wait = Min(wait, dismissAt - now);
				}
			}

			if (_live)
			{
				if (_dirty || now >= _nextTickAt)
				{
					string signature = SignatureOf(content, now);

					_dirty = false;
					_nextTickAt = now + TickInterval(content, now);

					if (HasProgress(content) && signature != _lastSent)
					{
						_lastSent = signature;
						data = BuildProgress(content, now);
					}
				}

				wait = Min(wait, _nextTickAt - now);
			}
		}

		if (data is not null)
		{
			AppNotificationProgressResult result =
				await AppNotificationManager.Default.UpdateAsync(
					data,
					LiveTag,
					WindowsNotificationHost.LiveGroup);

			if (result == AppNotificationProgressResult.AppNotificationNotFound)
			{
				bool present = await LiveStillPresentAsync();

				lock (_gate)
				{
					if (generation == _generation && _live)
					{
						_live = false;

						if (present)
						{
							// It is there, only the update did not find it: put a fresh one in its place.
							WindowsTrace.Write("Update did not find the live notification although it exists; replacing");

							_restoreAt = DateTimeOffset.UtcNow;
							wait = TimeSpan.FromMilliseconds(100);
						}
						else
						{
							// Gone without an activation event: wait briefly for one, then treat as dismissed.
							WindowsTrace.Write("Live notification is gone; waiting for an activation");

							_dismissAt = DateTimeOffset.UtcNow + DismissGrace;
							wait = Min(wait, DismissGrace);
						}
					}
				}
			}
		}

		if (forwardPlan is not null)
		{
			try
			{
				WindowsTrace.Write("Live notification dismissed by the user; telling the tracker");

				await _bridge.HandleActionAsync(TrackingActions.Dismissed, forwardPlan).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				WindowsTrace.Write($"Dismissed hand-over failed: {ex.Message}");
			}
		}

		return Max(wait, TimeSpan.FromMilliseconds(100));
	}

	private static async Task<bool> LiveStillPresentAsync()
	{
		try
		{
			IList<AppNotification> all = await AppNotificationManager.Default.GetAllAsync();

			return all.Any(item => item.Tag == LiveTag && item.Group == WindowsNotificationHost.LiveGroup);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Listing notifications failed", ex);

			return false;
		}
	}

	private static TimeSpan Min(TimeSpan a, TimeSpan b) =>
		a < b ? a : b;

	private static TimeSpan Max(TimeSpan a, TimeSpan b) =>
		a > b ? a : b;

	private static async Task RemoveAsync(string group, string tag)
	{
		try
		{
			await AppNotificationManager.Default.RemoveByTagAndGroupAsync(tag, group).AsTask().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write($"Windows notification removal failed: {ex.Message}");
		}
	}
}
