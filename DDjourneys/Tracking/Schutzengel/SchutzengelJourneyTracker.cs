using DDjourneys.Core.Diagnostics;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Localization;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Keeps the watchlist of followed journeys in sync with the Schutzengel service and turns it into
/// a live presentation and alerts.
///
/// The class is platform-agnostic. What a platform contributes comes in through three seams:
/// <see cref="ILiveJourneySurface"/> (how live state and alerts are shown),
/// <see cref="ITrackingRuntime"/> (keeping polling alive, e.g. an Android foreground service) and
/// <see cref="INotificationAccess"/> (notification permission). Platform components that DI does
/// not create reach it through <see cref="TrackingCallbackBridge"/>.
///
/// The service owns the plans (<c>plansMinimal</c>, <c>planRawData</c>); this class only caches
/// what it needs, so the overview survives process death without any local persistence besides the
/// anonymous account token. Like the reference client it polls <c>planRealtime</c> and
/// <c>notifications</c> with the last <c>data_version</c> / <c>notification_count</c>, keeps a server
/// clock offset and derives progress from the clock between polls.
///
/// All state is guarded by <see cref="_gate"/>. Events, notifications and the foreground service are
/// only touched after the gate has been released.
/// </summary>
internal sealed class SchutzengelJourneyTracker : IJourneyTracker, ITrackingCallbacks, IDisposable
{
	private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan PlanListInterval = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan ClockInterval = TimeSpan.FromMinutes(10);

	/// <summary>
	/// A journey is watched in the background from this long before its alert lead time on.
	/// Without push messages the app has to be running (as foreground service) to notice a start.
	/// </summary>
	private static readonly TimeSpan MonitorWindow = TimeSpan.FromHours(2);

	/// <summary>A "connection endangered" notice only marks the journey while it is recent.</summary>

	private readonly IJourneyProvider _provider;
	private readonly SchutzengelApi _api;
	private readonly TrackingCallbackBridge _bridge;
	private readonly ILiveJourneySurface _surface;
	private readonly ITrackingRuntime _runtime;
	private readonly INotificationAccess _access;
	private readonly HttpClient? _ownedHttp;
	private readonly SchutzengelTokenStore _tokens = new();

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, WatchEntry> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _dismissed = new(StringComparer.Ordinal);

	/// <summary>Per plan: the notice (time and text) the user swiped away.</summary>
	private readonly Dictionary<string, string> _dismissedNotices = new(StringComparer.Ordinal);
	private readonly TrackingEventBroadcaster<JourneyTrackingEvent> _events = new();

	private readonly List<string> _pendingForgotten = [];

	private readonly object _runtimeGate = new();

	private volatile IReadOnlyList<WatchedJourney> _watched = [];

	private TimeSpan _serverOffset = TimeSpan.Zero;
	private DateTimeOffset _clockSyncedAt = DateTimeOffset.MinValue;
	private DateTimeOffset _planListSyncedAt = DateTimeOffset.MinValue;
	private string? _liveKey;

	// The user's choice for the live presentation (null: automatic) and what it shows right now.
	private volatile string? _preferredLive = LoadPreferredLive();
	private volatile string? _livePlanId;

	// Immutable copy of the cached trips for readers outside the gate (GetTrip).
	private volatile IReadOnlyDictionary<string, TripTimeline> _timelines =
		new Dictionary<string, TripTimeline>(StringComparer.Ordinal);

	private readonly Func<int>? _defaultLeadMinutes;

	private CancellationTokenSource? _loopCancellation;
	private Task? _loop;
	private bool _runWanted;
	private bool _serviceRunning;
	private bool _disposed;

	public SchutzengelJourneyTracker(
		IJourneyProvider journeyProvider,
		TrackingCallbackBridge bridge,
		ILiveJourneySurface surface,
		ITrackingRuntime runtime,
		INotificationAccess access,
		HttpClient? http = null,
		Func<int>? defaultLeadMinutes = null)
	{
		_defaultLeadMinutes = defaultLeadMinutes;
		_provider = journeyProvider ?? throw new ArgumentNullException(nameof(journeyProvider));
		_bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
		_surface = surface ?? throw new ArgumentNullException(nameof(surface));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_access = access ?? throw new ArgumentNullException(nameof(access));

		// A client this class created is also this class's to dispose.
		_ownedHttp = http is null ? new HttpClient() : null;

		_api =
			new SchutzengelApi(
				http ?? _ownedHttp!,
				_tokens.GetAsync,
				_tokens.SetAsync,
				_tokens.RemoveToken);

		// Platform components that DI does not create reach the tracker through the bridge.
		_bridge.Attach(this);
	}

	public bool IsAvailable => true;

	public bool HasLiveSurface => _surface.IsSupported;

	public string? LivePlanId => _livePlanId;

	public string? PreferredLivePlanId => _preferredLive;

	public IAsyncEnumerable<JourneyTrackingEvent> Events => _events.SubscribeAsync();

	public IReadOnlyList<WatchedJourney> Watched => _watched;

	public event EventHandler? WatchedChanged;

	private DateTimeOffset Now => DateTimeOffset.UtcNow + _serverOffset;

	// ----- Queries -----

	public WatchedJourney? Find(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		string? key = JourneyFingerprint.Of(journey);

		return key is null
			? null
			: _watched.FirstOrDefault(item => string.Equals(item.JourneyKey, key, StringComparison.Ordinal));
	}

	public async Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			return await _access.CanNotifyAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Log($"Notification state unreadable: {ex.Message}");

			return false;
		}
	}

	public TrackedTrip? GetTrip(string planId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(planId);

		// No gate: the snapshot is replaced as a whole after every recomputation, and a trip is
		// immutable, so the course moves on with the clock between polls.
		return _timelines.TryGetValue(planId, out TripTimeline? timeline)
			? timeline.Describe(planId, Now)
			: null;
	}

	public Task SetPreferredLivePlanAsync(string? planId, CancellationToken cancellationToken = default) =>
		RunAsync(
			() =>
			{
				string? choice = string.IsNullOrWhiteSpace(planId) ? null : planId;

				if (choice is not null && !_entries.ContainsKey(choice))
				{
					choice = null;
				}

				_preferredLive = choice;
				SavePreferredLive(choice);

				PendingEffects effects = Recompute();

				// The choice itself is part of what the overview shows.
				effects.WatchlistChanged = true;

				return Task.FromResult(effects);
			},
			cancellationToken);

	// ----- Commands -----

	public async Task<WatchedJourney> FollowAsync(
		Journey journey,
		CancellationToken cancellationToken = default,
		string? replacesPlanId = null)
	{
		ArgumentNullException.ThrowIfNull(journey);

		await RequestNotificationPermissionAsync().ConfigureAwait(false);

		string? key = JourneyFingerprint.Of(journey);
		string planId;

		var effects = new List<PendingEffects>(2);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await _api.EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

			effects.Add(await SyncAsync(force: true, cancellationToken).ConfigureAwait(false));

			WatchEntry? existing =
				key is null
					? null
					: _entries.Values.FirstOrDefault(
						entry => string.Equals(entry.Summary?.Fingerprint, key, StringComparison.Ordinal));

			if (existing is not null)
			{
				planId = existing.Info.PlanId;

				if (existing.Info.Deactivated)
				{
					using JsonDocument activated =
						await _api.ActivateAsync(planId, cancellationToken).ConfigureAwait(false);
				}
			}
			else
			{
				// An alternative to a followed plan links to that plan's trip (the original is then no longer listed).
				string? tripReference =
					replacesPlanId is not null
					&& _entries.TryGetValue(replacesPlanId, out WatchEntry? replaced)
						? replaced.Info.ActiveTripId
						: null;

				planId = await CreatePlanAsync(journey, tripReference, cancellationToken).ConfigureAwait(false);
			}

			effects.Add(await SyncAsync(force: true, cancellationToken).ConfigureAwait(false));
		}
		finally
		{
			_gate.Release();
		}

		foreach (PendingEffects pending in effects)
		{
			Dispatch(pending);
		}

		return _watched.FirstOrDefault(item => item.PlanId == planId)
			?? throw new InvalidOperationException("The service did not list the followed journey.");
	}

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		RunAsync(
			async () =>
			{
				if (!await _api.HasAccountAsync(cancellationToken).ConfigureAwait(false))
				{
					return Recompute();
				}

				return await SyncAsync(force: true, cancellationToken).ConfigureAwait(false);
			},
			cancellationToken);

	public Task SetActiveAsync(string planId, bool active, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(planId);

		return RunAsync(
			async () =>
			{
				// A notification action can arrive in a fresh process whose cache is still empty.
				using JsonDocument response =
					active
						? await _api.ActivateAsync(planId, cancellationToken).ConfigureAwait(false)
						: await _api.DeactivateAsync(planId, cancellationToken).ConfigureAwait(false);

				if (_entries.TryGetValue(planId, out WatchEntry? entry))
				{
					entry.Info = entry.Info with { Deactivated = !active };
				}

				return await SyncAsync(force: true, cancellationToken).ConfigureAwait(false);
			},
			cancellationToken);
	}

	public Task SetOptionsAsync(string planId, WatchOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(planId);
		ArgumentNullException.ThrowIfNull(options);

		return RunAsync(
			async () =>
			{
				if (!_entries.TryGetValue(planId, out WatchEntry? entry))
				{
					return Recompute();
				}

				SchutzengelOptions updated = entry.Info.Options.With(options);

				using JsonDocument response =
					await _api.SetOptionsAsync(planId, updated, cancellationToken).ConfigureAwait(false);

				entry.Info = entry.Info with { Options = updated };

				return await SyncAsync(force: true, cancellationToken).ConfigureAwait(false);
			},
			cancellationToken);
	}

	public Task DeleteAsync(string planId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(planId);

		return RunAsync(
			async () =>
			{
				using JsonDocument response =
					await _api.DeletePlanAsync(planId, cancellationToken).ConfigureAwait(false);

				Forget(planId);

				return Recompute();
			},
			cancellationToken);
	}

	public Task DeleteAllAsync(CancellationToken cancellationToken = default) =>
		RunAsync(
			async () =>
			{
				if (await _api.HasAccountAsync(cancellationToken).ConfigureAwait(false))
				{
					using JsonDocument response =
						await _api.DeleteAllPlansAsync(cancellationToken).ConfigureAwait(false);
				}

				foreach (string planId in _entries.Keys.ToList())
				{
					Forget(planId);
				}

				return Recompute();
			},
			cancellationToken);

	/// <summary>Entry point for the actions of the live presentation (buttons, swipe).</summary>
	public Task HandleActionAsync(string action, string planId)
	{
		switch (action)
		{
			case TrackingActions.Pause:
				return SetActiveAsync(planId, false);

			case TrackingActions.Resume:
				return SetActiveAsync(planId, true);

			case TrackingActions.Stop:
				return DeleteAsync(planId);

			case TrackingActions.Dismissed:
				return RunAsync(
					() =>
					{
						if (_entries.TryGetValue(planId, out WatchEntry? entry))
						{
							_dismissed[planId] = StateKey(entry);
						}

						// A full recomputation, not empty effects: empty effects say "nothing to
						// monitor" and would stop the keep-alive while the journey is still under way.
						return Task.FromResult(Recompute());
					},
					CancellationToken.None);

			default:
				return Task.CompletedTask;
		}
	}

	/// <summary>Called when the app comes to the foreground: refreshes and resumes monitoring.</summary>
	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await RefreshAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Log($"Resume refresh failed: {ex.Message}");

			// Offline is not fatal: the cached watchlist keeps counting on the local clock.
			await RunAsync(() => Task.FromResult(Recompute()), CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <summary>The platform ended the keep-alive; monitoring resumes with the app.</summary>
	public void ServiceStopped()
	{
		lock (_runtimeGate)
		{
			_serviceRunning = false;
		}
	}

	// ----- Plan creation -----

	private async Task<string> CreatePlanAsync(Journey journey, string? tripReference, CancellationToken cancellationToken)
	{
		if (_provider is not VvoJourneyProvider vvo)
		{
			throw new InvalidOperationException("Journey tracking requires the VVO journey provider.");
		}

		(VvoRoute Route, string? SessionId, VvoStatus? Status)? connection =
			await vvo.GetSchutzengelConnectionAsync(journey, cancellationToken).ConfigureAwait(false);

		if (connection is null)
		{
			throw new InvalidOperationException(
				"The selected connection is no longer offered by the timetable service.");
		}

		System.Text.Json.Nodes.JsonObject rawData =
			SchutzengelRawDataTranslator.Translate(
				connection.Value.Route,
				journey,
				connection.Value.SessionId,
				connection.Value.Status);

		string plan =
			SchutzengelPlanTranslator.Serialize(
				journey,
				rawData,
				SchutzengelOptions.Default with
				{
					StartLeadSeconds = Math.Clamp(_defaultLeadMinutes?.Invoke() ?? 5, 1, 60) * 60
				},
				tripReference);

		using JsonDocument created =
			await _api.CreatePlanAsync(plan, cancellationToken).ConfigureAwait(false);

		return SchutzengelPlanList.ReadCreatedPlanId(created.RootElement)
			?? throw new InvalidOperationException("The service did not return a plan id.");
	}

	private async Task RequestNotificationPermissionAsync()
	{
		try
		{
			await _access.RequestAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// Tracking works without notifications; the overview page still shows everything.
			Log($"Notification permission request failed: {ex.Message}");
		}
	}

	// ----- Live preference (kept across restarts) -----

	private const string PreferredLiveKey = "tracking.live_plan";

	private static string? LoadPreferredLive()
	{
		try
		{
			string value = Preferences.Default.Get(PreferredLiveKey, string.Empty);

			return value.Length == 0 ? null : value;
		}
		catch (Exception ex)
		{
			Log($"Live preference unreadable: {ex.Message}");

			return null;
		}
	}

	private static void SavePreferredLive(string? planId)
	{
		try
		{
			if (planId is null)
			{
				Preferences.Default.Remove(PreferredLiveKey);
			}
			else
			{
				Preferences.Default.Set(PreferredLiveKey, planId);
			}
		}
		catch (Exception ex)
		{
			Log($"Live preference not saved: {ex.Message}");
		}
	}

	// ----- Synchronisation (always called with the gate held) -----

	private async Task RunAsync(Func<Task<PendingEffects>> work, CancellationToken cancellationToken)
	{
		PendingEffects effects;

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			effects = await work().ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}

		Dispatch(effects);
	}

	private async Task<PendingEffects> SyncAsync(bool force, CancellationToken cancellationToken)
	{
		if (!await _api.HasAccountAsync(cancellationToken).ConfigureAwait(false))
		{
			_entries.Clear();

			return Recompute();
		}

		DateTimeOffset localNow = DateTimeOffset.UtcNow;

		if (force || localNow - _clockSyncedAt >= ClockInterval)
		{
			await SyncClockAsync(localNow, cancellationToken).ConfigureAwait(false);
		}

		if (force || localNow - _planListSyncedAt >= PlanListInterval || _entries.Count == 0)
		{
			await SyncPlanListAsync(localNow, cancellationToken).ConfigureAwait(false);
		}

		foreach (WatchEntry entry in _entries.Values.ToList())
		{
			cancellationToken.ThrowIfCancellationRequested();

			await LoadSummaryAsync(entry, cancellationToken).ConfigureAwait(false);
			await LoadTripAsync(entry, cancellationToken).ConfigureAwait(false);
		}

		return Recompute();
	}

	private async Task SyncClockAsync(DateTimeOffset localNow, CancellationToken cancellationToken)
	{
		try
		{
			using JsonDocument time = await _api.GetServerTimeAsync(cancellationToken).ConfigureAwait(false);

			if (SchutzengelTime.ReadServerOffset(time.RootElement, localNow) is { } offset)
			{
				_serverOffset = offset;
				_clockSyncedAt = localNow;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The device clock is usually right; keep the last known offset.
			Log($"Server time unavailable: {ex.Message}");
		}
	}

	private async Task SyncPlanListAsync(DateTimeOffset localNow, CancellationToken cancellationToken)
	{
		using JsonDocument document = await _api.GetAllPlansAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<SchutzengelPlanInfo> plans = SchutzengelPlanList.Parse(document.RootElement);

		_planListSyncedAt = localNow;

		HashSet<string> known = plans.Select(plan => plan.PlanId).ToHashSet(StringComparer.Ordinal);

		foreach (string vanished in _entries.Keys.Where(id => !known.Contains(id)).ToList())
		{
			Forget(vanished);
		}

		foreach (SchutzengelPlanInfo plan in plans)
		{
			if (_entries.TryGetValue(plan.PlanId, out WatchEntry? entry))
			{
				entry.Info = plan;
			}
			else
			{
				_entries[plan.PlanId] = new WatchEntry(plan);
			}
		}
	}

	private async Task LoadSummaryAsync(WatchEntry entry, CancellationToken cancellationToken)
	{
		if (entry.SummaryLoaded)
		{
			return;
		}

		try
		{
			using JsonDocument document =
				await _api.GetPlanAsync(entry.Info.PlanId, cancellationToken).ConfigureAwait(false);

			entry.Summary = SchutzengelRawSummaryParser.ParsePlanRawData(document.RootElement);
			entry.Timeline?.SetEnsured(entry.Summary?.EnsuredChanges);
			entry.SummaryLoaded = true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Log($"Plan {entry.Info.PlanId}: raw data unavailable: {ex.Message}");
		}
	}

	/// <summary>The realtime and notification poll of the reference client for one plan.</summary>
	private async Task LoadTripAsync(WatchEntry entry, CancellationToken cancellationToken)
	{
		SchutzengelPlanInfo info = entry.Info;

		if (info.Deactivated
			|| entry.Status is WatchStatus.Recent
			|| info.ActiveTripId is not { Length: > 0 } tripId)
		{
			return;
		}

		if (!string.Equals(entry.TripId, tripId, StringComparison.Ordinal))
		{
			// A periodic plan moved on to its next trip.
			entry.TripId = tripId;
			entry.Timeline = null;
			entry.Notices = [];
			entry.NoticeCount = 0;
			entry.NoticesLoaded = false;
			entry.NoticesSeeded = false;
			entry.AlertedNotices = 0;
		}

		// Like the reference client: only plans of today, once their start is known.
		if (entry.Timeline?.Start is { } start
			&& Support.Format.ToWall(start).Date != Support.Format.ToWall(Now).Date)
		{
			return;
		}

		try
		{
			string? version = entry.Timeline?.DataVersion?.ToString(CultureInfo.InvariantCulture);
			int? count = version is null ? null : entry.NoticeCount;

			using (SchutzengelResponse realtime =
				await _api.FetchRealtimeAsync(tripId, version, count, cancellationToken).ConfigureAwait(false))
			{
				if (realtime.StatusCode != HttpStatusCode.Created
					&& TripTimeline.TryParse(realtime.Root, entry.Timeline, out TripTimeline timeline))
				{
					timeline.SetEnsured(entry.Summary?.EnsuredChanges);
					entry.Timeline = timeline;
				}
			}

			version = entry.Timeline?.DataVersion?.ToString(CultureInfo.InvariantCulture);
			count = version is null ? null : entry.NoticeCount;

			using SchutzengelResponse notices =
				await _api.FetchNotificationsAsync(tripId, version, count, cancellationToken).ConfigureAwait(false);

			if (notices.StatusCode != HttpStatusCode.NoContent
				&& notices.Root.ValueKind == JsonValueKind.Array)
			{
				entry.Notices = [.. SchutzengelNotices.Parse(notices.Root)];
				entry.NoticeCount = notices.Root.GetArrayLength();
				entry.NoticesLoaded = true;
			}
			else if (notices.StatusCode == HttpStatusCode.NoContent)
			{
				entry.NoticesLoaded = true;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// One broken trip must not stall the others; its cached data keeps being displayed.
			Log($"Plan {info.PlanId}: trip update failed: {ex.Message}");
		}
	}

	private void Forget(string planId)
	{
		_entries.Remove(planId);
		_dismissed.Remove(planId);
		_dismissedNotices.Remove(planId);

		try
		{
			Preferences.Default.Remove(DismissedNoticePrefix + planId);
		}
		catch (Exception ex)
		{
			Log($"Dismissed notice not cleared: {ex.Message}");
		}

		if (_preferredLive == planId)
		{
			_preferredLive = null;
			SavePreferredLive(null);
		}

		// Notifications are posted by Dispatch; clearing is cheap and idempotent.
		_pendingForgotten.Add(planId);
	}

	// ----- Derived state -----

	/// <summary>Recomputes every entry from the cache and the clock and collects what has to happen.</summary>
	private PendingEffects Recompute()
	{
		DateTimeOffset now = Now;
		TrackingStrings strings = LocalizationService.Current.CurrentStrings.Tracking;

		var effects = new PendingEffects();
		var views = new List<WatchedJourney>(_entries.Count);

		// Journeys that may own the live presentation: under way first, then those about to start,
		// each by departure. The first one is the automatic choice.
		var underway = new List<(WatchEntry Entry, WatchedJourney View)>();
		var upcoming = new List<WatchedJourney>();
		bool run = false;

		// The reference client lists a plan only while no other plan replaces it (trip_reference); finished
		// plans always stay in the history.
		var replaced =
			_entries.Values
				.Select(item => item.Info.TripReference)
				.OfType<string>()
				.ToHashSet(StringComparer.Ordinal);

		foreach (WatchEntry entry in _entries.Values)
		{
			if (entry.Status != WatchStatus.Recent
				&& entry.Info.ActiveTripId is { } tripId
				&& replaced.Contains(tripId))
			{
				continue;
			}

			WatchedJourney view = Evaluate(entry, now, strings, effects);

			views.Add(view);

			if (view.Status == WatchStatus.Active && view.Phase != TrackingPhase.Paused)
			{
				run = true;
				underway.Add((entry, view));
			}
			else if (view.Status == WatchStatus.Planned
				&& view.Departure is { } departure
				&& departure - entry.Info.Options.StartLead - MonitorWindow <= now)
			{
				run = true;
				upcoming.Add(view);
			}
		}

		underway.Sort((left, right) => Before(left.View.Departure, right.View.Departure) ? -1 : Before(right.View.Departure, left.View.Departure) ? 1 : 0);
		upcoming.Sort((left, right) => Before(left.Departure, right.Departure) ? -1 : Before(right.Departure, left.Departure) ? 1 : 0);

		string? chosen =
			LivePlanSelector.Choose(
				_preferredLive,
				[.. underway.Select(item => item.View.PlanId), .. upcoming.Select(item => item.PlanId)]);

		WatchEntry? focus = null;
		WatchedJourney? focusView = null;
		WatchedJourney? waiting = null;

		foreach ((WatchEntry entry, WatchedJourney view) in underway)
		{
			if (view.PlanId == chosen)
			{
				focus = entry;
				focusView = view;
			}
		}

		if (focus is null)
		{
			waiting = upcoming.FirstOrDefault(view => view.PlanId == chosen);
		}

		views.Sort(CompareViews);

		_timelines =
			_entries
				.Where(item => item.Value.Timeline is not null)
				.ToDictionary(item => item.Key, item => item.Value.Timeline!, StringComparer.Ordinal);

		effects.ShouldRun = run;
		effects.Forgotten = [.. _pendingForgotten];
		_pendingForgotten.Clear();

		if (!SameWatchlist(_watched, views))
		{
			_watched = views;
			effects.WatchlistChanged = true;
		}

		string? previousLive = _livePlanId;

		ScheduleLiveNotification(focus, focusView, waiting, ChoosePaused(views, focus, waiting, run, now), run, now, strings, effects);

		if (!string.Equals(previousLive, _livePlanId, StringComparison.Ordinal))
		{
			effects.WatchlistChanged = true;
		}

		return effects;
	}

	private const int UnconfirmedArrivalGraceMinutes = 30;

	/// <summary>
	/// A connection-risk notice about a change the provider ensures. The guarantee is authoritative (the next vehicle
	/// waits); the service's notice only sees the clock. Such a notice is neither shown nor alerted.
	/// </summary>
	private static bool IsOutrankedByGuarantee(WatchEntry entry, SchutzengelNotice notice)
	{
		if (notice.Severity != SchutzengelNoticeSeverity.ConnectionRisk)
		{
			return false;
		}

		if (entry.Timeline?.CoversEnsuredChange(notice.Text) == true)
		{
			return true;
		}

		// Before the timeline is loaded, the summary alone decides: every change of the trip is ensured.
		return entry.Summary?.EnsuredChanges is { Count: > 0 } changes && changes.All(flag => flag);
	}

	/// <summary>
	/// A boarding instruction is obsolete once the ride it is about is under way: it was issued before that ride's
	/// planned departure and the passenger has since boarded (the current ride started after the notice).
	/// </summary>
	private static bool IsBoardingDone(WatchEntry entry, SchutzengelNotice notice, TripSnapshot snapshot)
	{
		if (!notice.IsBoardingInstruction
			|| snapshot.Stage != TripStage.Riding
			|| notice.Time is not { } issued
			|| entry.Timeline is not { } timeline
			|| snapshot.EpisodeIndex < 0
			|| snapshot.EpisodeIndex >= timeline.Episodes.Count)
		{
			return false;
		}

		return timeline.Episodes[snapshot.EpisodeIndex].From.Scheduled is { } rideStart
			&& issued <= rideStart;
	}

	private static string NoticeKey(SchutzengelNotice notice) =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"{notice.Time?.ToUnixTimeMilliseconds()}|{notice.Text}");

	private const string DismissedNoticePrefix = "tracking.dismissed_notice.";

	private string? DismissedNoticeKey(string planId)
	{
		if (_dismissedNotices.TryGetValue(planId, out string? key))
		{
			return key;
		}

		try
		{
			string stored = Preferences.Default.Get(DismissedNoticePrefix + planId, string.Empty);

			if (stored.Length > 0)
			{
				_dismissedNotices[planId] = stored;

				return stored;
			}
		}
		catch (Exception ex)
		{
			Log($"Dismissed notice unreadable: {ex.Message}");
		}

		return null;
	}

	public Task DismissNoticeAsync(string planId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(planId);

		return RunAsync(
			() =>
			{
				if (_entries.TryGetValue(planId, out WatchEntry? entry)
					&& entry.Notices.Count > 0)
				{
					string key = NoticeKey(entry.Notices[^1]);

					_dismissedNotices[planId] = key;

					try
					{
						Preferences.Default.Set(DismissedNoticePrefix + planId, key);
					}
					catch (Exception ex)
					{
						Log($"Dismissed notice not saved: {ex.Message}");
					}
				}

				return Task.FromResult(Recompute());
			},
			cancellationToken);
	}

	private WatchedJourney Evaluate(
		WatchEntry entry,
		DateTimeOffset now,
		TrackingStrings strings,
		PendingEffects effects)
	{
		SchutzengelPlanInfo info = entry.Info;
		TripSnapshot snapshot = entry.Timeline?.Calculate(now) ?? TripSnapshot.Empty;
		SchutzengelNotice? newest = entry.Notices.Count > 0 ? entry.Notices[^1] : null;

		// An ensured change cannot be at risk: the provider's guarantee outranks the service's clock-based notice.
		if (newest is not null && IsOutrankedByGuarantee(entry, newest))
		{
			newest = null;
		}

		// Swiped away by the user: gone until a newer notice arrives.
		if (newest is not null && DismissedNoticeKey(info.PlanId) == NoticeKey(newest))
		{
			newest = null;
		}

		DateTimeOffset? start = entry.Timeline?.Start ?? entry.Summary?.Departure;
		DateTimeOffset? end = entry.Timeline?.End ?? entry.Summary?.Arrival;

		bool periodic = info.Options.IsPeriodic;

		WatchStatus status;

		if (info.Deactivated)
		{
			status = WatchStatus.Deactivated;
		}
		else if (end is { } finish && finish < now && !periodic
			&& (entry.Timeline is not null || finish.AddMinutes(UnconfirmedArrivalGraceMinutes) < now))
		{
			// Without live data the planned arrival is only a guess: a delayed journey is not over yet.
			status = WatchStatus.Recent;
		}
		else if (end is { } over && over < now && periodic)
		{
			// A periodic plan waits for its next trip instead of completing.
			status = WatchStatus.Planned;
		}
		else if (start is { } begin && begin - info.Options.StartLead <= now)
		{
			status = WatchStatus.Active;
		}
		else
		{
			status = WatchStatus.Planned;
		}

		// Only the newest notice counts, and only while it is still relevant (see NoticePolicy);
		// this runs on every render pass, so an outdated notice disappears within seconds.
		bool journeyOver = status is WatchStatus.Recent or WatchStatus.Deactivated;

		SchutzengelNotice? latest =
			newest is not null
			&& NoticePolicy.Evaluate(
				newest.Severity switch
				{
					SchutzengelNoticeSeverity.Cancellation => NoticeKind.Cancellation,
					SchutzengelNoticeSeverity.ConnectionRisk => NoticeKind.ConnectionRisk,
					_ => newest.IsBoardingInstruction ? NoticeKind.Instruction : NoticeKind.Information
				},
				newest.Time,
				journeyOver,
				snapshot.Risk is not null,
				now).IsVisible
				&& !IsBoardingDone(entry, newest, snapshot)
				? newest
				: null;

		bool freshRisk = latest is { Severity: SchutzengelNoticeSeverity.ConnectionRisk };

		TrackingPhase phase;

		if (status == WatchStatus.Deactivated)
		{
			phase = TrackingPhase.Paused;
		}
		else if (newest?.Severity == SchutzengelNoticeSeverity.Cancellation)
		{
			phase = TrackingPhase.Cancelled;
		}
		else if (status == WatchStatus.Recent)
		{
			phase = TrackingPhase.Arrived;
		}
		else if (snapshot.Risk is not null || freshRisk)
		{
			phase = TrackingPhase.AtRisk;
		}
		else
		{
			phase = status == WatchStatus.Active ? snapshot.Phase : TrackingPhase.Planned;
		}

		entry.Status = status;
		entry.Phase = phase;
		entry.Snapshot = snapshot;

		RaiseTransitions(entry, status, phase, snapshot, latest, start, now, strings, effects);

		entry.LastStatus = status;
		entry.LastPhase = phase;
		entry.LastEpisode = snapshot.EpisodeIndex;
		entry.Seen = true;

		bool riding = status == WatchStatus.Active && snapshot.Stage != TripStage.NotStarted;

		return new WatchedJourney(
			info.PlanId,
			entry.Summary?.Fingerprint,
			entry.Summary?.Origin ?? snapshot.Origin ?? string.Empty,
			entry.Summary?.Destination ?? snapshot.Destination ?? string.Empty,
			start,
			end,
			entry.Summary?.Lines ?? [],
			status,
			phase,
			riding || status == WatchStatus.Recent ? snapshot.Progress : 0,
			snapshot.Stage == TripStage.Riding && status == WatchStatus.Active ? snapshot.MotName : null,
			riding ? snapshot.NextStop : null,
			riding ? snapshot.NextStopTime : null,
			latest?.Text,
			info.Options.ToWatchOptions(),
			periodic,
			entry.Summary?.EnsuredChanges,
			entry.Notices
				.OrderByDescending(item => item.Time ?? DateTimeOffset.MinValue)
				.Select(item => new WatchedNotice(item.Time, item.Text, item.Severity != SchutzengelNoticeSeverity.Information))
				.ToList());
	}

	private void RaiseTransitions(
		WatchEntry entry,
		WatchStatus status,
		TrackingPhase phase,
		TripSnapshot snapshot,
		SchutzengelNotice? latest,
		DateTimeOffset? start,
		DateTimeOffset now,
		TrackingStrings strings,
		PendingEffects effects)
	{
		string planId = entry.Info.PlanId;
		SchutzengelOptions options = entry.Info.Options;
		bool alertable = status is WatchStatus.Planned or WatchStatus.Active;
		bool problemAlerted = false;

		LiveJourneyState State(string? message) =>
			new(
				planId,
				phase,
				snapshot.LegIndex,
				snapshot.LegCount,
				snapshot.PlannedArrival,
				snapshot.Arrival,
				message,
				now);

		// New notices since the last look. The first load only seeds the counter.
		if (entry.NoticesLoaded)
		{
			if (!entry.NoticesSeeded)
			{
				entry.AlertedNotices = entry.Notices.Count;
				entry.NoticesSeeded = true;
			}
			else
			{
				foreach (SchutzengelNotice notice in entry.Notices.Skip(entry.AlertedNotices))
				{
					if (IsOutrankedByGuarantee(entry, notice))
					{
						continue;
					}

					bool problem = notice.Severity != SchutzengelNoticeSeverity.Information;

					if (!alertable || !(problem ? options.Problem : options.Change))
					{
						continue;
					}

					problemAlerted |= problem;

					effects.Actions.Add(
						() => _surface.ShowAlert(
							new JourneyAlert(
							planId,
							problem ? JourneyAlertKind.Problem : JourneyAlertKind.Change,
							problem ? strings.NotifProblemAlertTitle : strings.NotifChangeAlertTitle,
							notice.Text)));
				}

				entry.AlertedNotices = entry.Notices.Count;
			}
		}

		if (!entry.Seen)
		{
			return;
		}

		if (entry.LastStatus == WatchStatus.Planned && status == WatchStatus.Active)
		{
			if (options.StartActive)
			{
				string text =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifStartAlertText,
						entry.Summary?.Origin ?? snapshot.Origin,
						entry.Summary?.Destination ?? snapshot.Destination,
						DDjourneys.Support.Format.TimeOrDash(start));

				effects.Actions.Add(
					() => _surface.ShowAlert(
						new JourneyAlert(
						planId,
						JourneyAlertKind.Start,
						strings.NotifStartAlertTitle,
						text)));
			}

			effects.Events.Add(new JourneyTrackingEvent(JourneyTrackingEventKind.Started, State(null)));
		}
		else if (status == WatchStatus.Active
			&& phase == entry.LastPhase
			&& snapshot.EpisodeIndex != entry.LastEpisode)
		{
			effects.Events.Add(new JourneyTrackingEvent(JourneyTrackingEventKind.Updated, State(null)));
		}

		if (phase == entry.LastPhase)
		{
			return;
		}

		switch (phase)
		{
			case TrackingPhase.AtRisk when alertable:
				{
					string text =
						snapshot.Risk is { } risk
							? string.Format(
								CultureInfo.CurrentCulture,
								risk.Missed ? strings.NotifMissedText : strings.NotifTightText,
								risk.Station)
							: latest?.Text ?? string.Empty;

					if (options.Problem && !problemAlerted)
					{
						effects.Actions.Add(
							() => _surface.ShowAlert(
								new JourneyAlert(
								planId,
								JourneyAlertKind.Problem,
								strings.NotifRiskTitle,
								text)));
					}

					effects.Events.Add(new JourneyTrackingEvent(JourneyTrackingEventKind.RiskChanged, State(text)));
					break;
				}

			case TrackingPhase.Cancelled:
				effects.Events.Add(
					new JourneyTrackingEvent(JourneyTrackingEventKind.Cancelled, State(latest?.Text)));
				break;

			case TrackingPhase.Arrived when entry.LastStatus == WatchStatus.Active:
				{
					string destination = entry.Summary?.Destination ?? snapshot.Destination ?? string.Empty;

					effects.Actions.Add(
						() => _surface.ShowAlert(
							new JourneyAlert(
							planId,
							JourneyAlertKind.Arrived,
							strings.NotifArrivedTitle,
							destination)));

					effects.Events.Add(new JourneyTrackingEvent(JourneyTrackingEventKind.Arrived, State(destination)));
					break;
				}

			case TrackingPhase.InProgress or TrackingPhase.AtInterchange
				when entry.LastPhase is TrackingPhase.AtRisk:
				effects.Events.Add(new JourneyTrackingEvent(JourneyTrackingEventKind.RiskChanged, State(null)));
				break;
		}
	}

	/// <summary>Decides what the single live presentation shows (or that it should go away).</summary>
	private void ScheduleLiveNotification(
		WatchEntry? focus,
		WatchedJourney? focusView,
		WatchedJourney? waiting,
		WatchedJourney? paused,
		bool run,
		DateTimeOffset now,
		TrackingStrings strings,
		PendingEffects effects)
	{
		LiveJourneyContent? content = null;

		if (focus is not null && focusView is not null)
		{
			string planId = focus.Info.PlanId;

			if (_dismissed.TryGetValue(planId, out string? dismissedState))
			{
				if (dismissedState == StateKey(focus))
				{
					// The user swiped it away; it returns when the situation changes.
					if (_liveKey is not null)
					{
						_liveKey = null;
						effects.Actions.Add(_surface.Dismiss);
					}

					_livePlanId = null;

					return;
				}

				_dismissed.Remove(planId);
			}

			content =
				SchutzengelLiveContent.Create(
					planId,
					focus.Snapshot,
					focus.Phase,
					focusView.LatestNotice,
					now,
					strings);
		}
		else if (run)
		{
			content =
				waiting is not null
					? SchutzengelLiveContent.Waiting(waiting, strings)
					: SchutzengelLiveContent.Monitoring(strings);
		}
		else if (paused is not null && _entries.TryGetValue(paused.PlanId, out WatchEntry? pausedEntry))
		{
			// Nothing is monitored, but a surface that can resume from outside the app keeps the
			// paused journey in view (with its Resume button).
			if (_dismissed.TryGetValue(paused.PlanId, out string? pausedDismissed)
				&& pausedDismissed == StateKey(pausedEntry))
			{
				if (_liveKey is not null)
				{
					_liveKey = null;
					effects.Actions.Add(_surface.Dismiss);
				}

				_livePlanId = null;

				return;
			}

			_dismissed.Remove(paused.PlanId);

			content = SchutzengelLiveContent.Paused(paused, strings);
		}

		// A paused journey is shown, but it is not "the live journey" of the overview.
		_livePlanId =
			content is { PlanId.Length: > 0, Phase: not TrackingPhase.Paused }
				? content.PlanId
				: null;

		if (content is null)
		{
			if (_liveKey is not null)
			{
				_liveKey = null;
				effects.Actions.Add(_surface.Dismiss);
			}

			return;
		}

		string key = content.Key;

		if (key == _liveKey)
		{
			return;
		}

		_liveKey = key;

		effects.Actions.Add(() => _surface.Show(content));
	}

	/// <summary>
	/// The paused journey a surface keeps in view when nothing else is shown: the preferred one, else the
	/// one departing first. Long-finished journeys are not worth a notification.
	/// </summary>
	private WatchedJourney? ChoosePaused(
		IReadOnlyList<WatchedJourney> views,
		WatchEntry? focus,
		WatchedJourney? waiting,
		bool run,
		DateTimeOffset now)
	{
		if (focus is not null || waiting is not null || run || !_surface.ShowsPaused)
		{
			return null;
		}

		List<WatchedJourney> candidates =
			[.. views.Where(
				view => view.Status == WatchStatus.Deactivated
					&& (view.Arrival is not { } arrival || arrival > now - TimeSpan.FromHours(6)))];

		return candidates.FirstOrDefault(view => view.PlanId == _preferredLive)
			?? candidates.OrderBy(view => view.Departure ?? DateTimeOffset.MaxValue).FirstOrDefault();
	}

	private static string StateKey(WatchEntry entry) =>
		$"{entry.Phase}|{entry.Snapshot.EpisodeIndex}";

	private static bool Before(DateTimeOffset? left, DateTimeOffset? right) =>
		(left ?? DateTimeOffset.MaxValue) < (right ?? DateTimeOffset.MaxValue);

	private static int CompareViews(WatchedJourney left, WatchedJourney right)
	{
		int byStatus = Rank(left.Status).CompareTo(Rank(right.Status));

		if (byStatus != 0)
		{
			return byStatus;
		}

		int byTime =
			(left.Departure ?? DateTimeOffset.MaxValue).CompareTo(right.Departure ?? DateTimeOffset.MaxValue);

		// Finished journeys: most recent first.
		return left.Status == WatchStatus.Recent ? -byTime : byTime;
	}

	private static int Rank(WatchStatus status) =>
		status switch
		{
			WatchStatus.Active => 0,
			WatchStatus.Planned => 1,
			WatchStatus.Deactivated => 2,
			_ => 3
		};

	private static bool SameWatchlist(IReadOnlyList<WatchedJourney> left, IReadOnlyList<WatchedJourney> right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			WatchedJourney a = left[i];
			WatchedJourney b = right[i];

			bool same =
				a.PlanId == b.PlanId
				&& a.JourneyKey == b.JourneyKey
				&& a.Origin == b.Origin
				&& a.Destination == b.Destination
				&& a.Departure == b.Departure
				&& a.Arrival == b.Arrival
				&& a.Status == b.Status
				&& a.Phase == b.Phase
				&& Math.Abs(a.Progress - b.Progress) < 0.005
				&& a.CurrentLine == b.CurrentLine
				&& a.NextStop == b.NextStop
				&& a.NextStopTime == b.NextStopTime
				&& a.LatestNotice == b.LatestNotice
				&& (a.Notices?.Count ?? 0) == (b.Notices?.Count ?? 0)
				&& a.Options == b.Options
				&& a.IsPeriodic == b.IsPeriodic
				&& a.Lines.SequenceEqual(b.Lines)
				&& (a.EnsuredChanges ?? []).SequenceEqual(b.EnsuredChanges ?? []);

			if (!same)
			{
				return false;
			}
		}

		return true;
	}

	// ----- Effects (always called without the gate) -----

	private void Dispatch(PendingEffects effects)
	{
		foreach (string planId in effects.Forgotten)
		{
			Guarded(() => _surface.ClearAlerts(planId));
		}

		foreach (Action action in effects.Actions)
		{
			Guarded(action);
		}

		foreach (JourneyTrackingEvent trackingEvent in effects.Events)
		{
			_events.Publish(trackingEvent);
		}

		UpdateRuntime(effects.ShouldRun);

		if (effects.WatchlistChanged)
		{
			Guarded(() => WatchedChanged?.Invoke(this, EventArgs.Empty));
		}
	}

	private static void Guarded(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Log($"Effect failed: {ex}");
		}
	}

	// ----- Runtime: polling loop and foreground service -----

	private void UpdateRuntime(bool run)
	{
		lock (_runtimeGate)
		{
			if (_disposed)
			{
				return;
			}

			_runWanted = run;

			if (run)
			{
				if (!_serviceRunning)
				{
					_serviceRunning = _runtime.TryKeepAlive();
				}

				if (_loop is null)
				{
					var cancellation = new CancellationTokenSource();
					CancellationToken token = cancellation.Token;

					_loopCancellation = cancellation;
					_loop = Task.Run(() => LoopAsync(token));
				}

				return;
			}

			if (_serviceRunning)
			{
				_serviceRunning = false;
				_runtime.Release();
			}
		}
	}

	private async Task LoopAsync(CancellationToken cancellationToken)
	{
		DateTimeOffset nextSync = DateTimeOffset.MinValue;

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				PendingEffects effects;

				await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

				try
				{
					DateTimeOffset local = DateTimeOffset.UtcNow;

					if (local >= nextSync)
					{
						nextSync = local + SyncInterval;

						try
						{
							effects = await SyncAsync(force: false, cancellationToken).ConfigureAwait(false);
						}
						catch (Exception ex) when (ex is not OperationCanceledException)
						{
							// Offline or service trouble: keep counting on the local clock.
							Log($"Sync failed: {ex.Message}");

							effects = Recompute();
						}
					}
					else
					{
						effects = Recompute();
					}
				}
				finally
				{
					_gate.Release();
				}

				Dispatch(effects);

				lock (_runtimeGate)
				{
					if (!_runWanted)
					{
						return;
					}
				}

				await Task.Delay(RenderInterval, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Log($"Polling loop ended: {ex}");
		}
		finally
		{
			lock (_runtimeGate)
			{
				_loop = null;
				_loopCancellation?.Dispose();
				_loopCancellation = null;
			}
		}
	}

	/// <summary>
	/// Ends monitoring: detaches from the bridge, stops the polling loop and the foreground
	/// service, completes every event subscription and releases an owned HTTP client. Idempotent.
	/// The loop observes the cancellation on its own; the gate is deliberately not disposed so it
	/// can finish an iteration that is already running.
	/// </summary>
	public void Dispose()
	{
		CancellationTokenSource? cancellation;
		bool stopService;

		lock (_runtimeGate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_runWanted = false;
			cancellation = _loopCancellation;
			stopService = _serviceRunning;
			_serviceRunning = false;
		}

		_bridge.Detach(this);

		try
		{
			// The loop's finally block disposes the source afterwards.
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		if (stopService)
		{
			Guarded(_runtime.Release);
		}

		_events.Dispose();
		_ownedHttp?.Dispose();
	}

	[Conditional("DEBUG")]
	private static void Log(string message) =>
		DiagnosticLog.Write($"[SCHUTZENGEL] {message}");

	// ----- Types -----

	/// <summary>Cached knowledge about one plan of the account.</summary>
	private sealed class WatchEntry(SchutzengelPlanInfo info)
	{
		public SchutzengelPlanInfo Info { get; set; } = info;

		public SchutzengelRawSummary? Summary { get; set; }

		public bool SummaryLoaded { get; set; }

		public string? TripId { get; set; }

		public TripTimeline? Timeline { get; set; }

		public IReadOnlyList<SchutzengelNotice> Notices { get; set; } = [];

		public int NoticeCount { get; set; }

		public bool NoticesLoaded { get; set; }

		public bool NoticesSeeded { get; set; }

		public int AlertedNotices { get; set; }

		public TripSnapshot Snapshot { get; set; } = TripSnapshot.Empty;

		public WatchStatus Status { get; set; } = WatchStatus.Planned;

		public TrackingPhase Phase { get; set; } = TrackingPhase.Planned;

		public bool Seen { get; set; }

		public WatchStatus LastStatus { get; set; }

		public TrackingPhase LastPhase { get; set; }

		public int LastEpisode { get; set; } = -1;
	}

	private sealed class PendingEffects
	{
		public List<Action> Actions { get; } = [];

		public List<JourneyTrackingEvent> Events { get; } = [];

		public IReadOnlyList<string> Forgotten { get; set; } = [];

		public bool WatchlistChanged { get; set; }

		public bool ShouldRun { get; set; }
	}
}
