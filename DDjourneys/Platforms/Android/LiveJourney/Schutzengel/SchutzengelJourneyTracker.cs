using System.Text.Json;
using System.Threading.Channels;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelJourneyTracker :
	IJourneyTracker
{
	internal static Func<Task>? DeactivateHandler { get; set; }
	internal static Func<Task>? DeleteHandler { get; set; }


	private static readonly TimeSpan NetworkSyncInterval =
		TimeSpan.FromSeconds(60);

	private static readonly TimeSpan ProgressUpdateInterval =
		TimeSpan.FromSeconds(1);


	private readonly SchutzengelApi _api;
	private readonly IJourneyProvider _journeyProvider;
	private readonly SchutzengelTokenStore _tokens = new();

	private readonly Channel<JourneyTrackingEvent> _events =
		Channel.CreateUnbounded<JourneyTrackingEvent>();


	private CancellationTokenSource? _loopCancellation;

	private Task? _networkLoopTask;
	private Task? _progressLoopTask;


	private Journey? _journey;
	private string? _planId;
	private string? _tripId;

	private LiveJourneyState? _state;

	private SchutzengelTripTimeline? _tripTimeline;

	private string? _realtimeDataVersion;
	private int? _notificationCount;

	private TrackingPhase? _remotePhase;
	private string? _remoteMessage;

	private TimeSpan _serverTimeOffset;


	public SchutzengelJourneyTracker(
		IJourneyProvider journeyProvider,
		HttpClient? http = null)
	{
		_journeyProvider =
			journeyProvider
			?? throw new ArgumentNullException(
				nameof(journeyProvider));


		_api =
			new SchutzengelApi(
				http ?? new HttpClient(),
				_tokens.GetAsync,
				_tokens.SetAsync,
				_tokens.RemoveToken);


		DeactivateHandler =
			() => StopAsync();


		DeleteHandler =
			EndAsync;
	}


	public bool IsAvailable =>
		true;


	public IAsyncEnumerable<JourneyTrackingEvent> Events =>
		_events.Reader.ReadAllAsync();


	internal async Task ResumeStoredAsync(
		CancellationToken cancellationToken = default)
	{
		if (!Preferences.Default.Get(
			"schutzengel_tracking_active",
			false))
		{
			return;
		}


		Journey? journey =
			await _tokens.GetJourneyAsync(
				cancellationToken);


		if (journey is not null)
		{
			await StartAsync(
				journey,
				cancellationToken);
		}
	}


	public async Task StartAsync(
		Journey journey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			journey);


		System.Diagnostics.Debug.WriteLine(
			$"[SCHUTZENGEL] Journey.Id = '{journey.Id}'");


		await StopLoopAsync();


		_journey =
			journey;

		_planId = null;
		_tripId = null;

		_tripTimeline =
			SchutzengelTripTimeline.FromJourney(
				journey);

		_realtimeDataVersion = null;
		_notificationCount = null;

		_remotePhase = null;
		_remoteMessage = null;


		if (OperatingSystem.IsAndroidVersionAtLeast(
			33))
		{
			PermissionStatus permission =
				await Permissions.CheckStatusAsync<
					Permissions.PostNotifications>();


			if (permission !=
				PermissionStatus.Granted)
			{
				permission =
					await Permissions.RequestAsync<
						Permissions.PostNotifications>();
			}


			if (permission !=
				PermissionStatus.Granted)
			{
				throw new InvalidOperationException(
					"Notification permission is required to follow a journey.");
			}
		}


		await _tokens.SaveJourneyAsync(
			journey,
			cancellationToken);


		Preferences.Default.Set(
			"schutzengel_tracking_active",
			true);


		JourneyTrackingForegroundService.Start();


		_loopCancellation =
			CancellationTokenSource
				.CreateLinkedTokenSource(
					cancellationToken);


		CancellationToken ct =
			_loopCancellation.Token;


		try
		{
			await _api.EnsureAuthenticatedAsync(
				ct);


			using (
				JsonDocument server =
					await _api.GetServerTimeAsync(
						ct))
			{
				SynchronizeServerTime(
					server.RootElement);
			}


			string storedPlan =
				Preferences.Default.Get(
					"schutzengel_active_plan",
					string.Empty);


			string storedTrip =
				Preferences.Default.Get(
					"schutzengel_active_trip",
					string.Empty);


			string storedJourney =
				Preferences.Default.Get(
					"schutzengel_active_journey",
					string.Empty);


			if (!string.IsNullOrWhiteSpace(
				storedPlan)
				&& !string.IsNullOrWhiteSpace(
					storedJourney)
				&& storedJourney !=
					JourneyIdentity(journey))
			{
				try
				{
					await _api.DeactivateAsync(
						storedPlan,
						ct);
				}
				catch
				{
				}


				try
				{
					await _api.DeletePlanAsync(
						storedPlan,
						ct);
				}
				catch
				{
				}


				ClearSavedPlan();


				storedPlan = string.Empty;
				storedTrip = string.Empty;
			}


			if (!string.IsNullOrWhiteSpace(
				storedPlan)
				&& !string.IsNullOrWhiteSpace(
					storedTrip))
			{
				using JsonDocument plans =
					await _api.GetAllPlansAsync(
						ct);


				if (SchutzengelPlanRecovery.ContainsPlan(
					plans.RootElement,
					storedPlan))
				{
					_planId =
						storedPlan;

					_tripId =
						storedTrip;


					System.Diagnostics.Debug.WriteLine(
						$"[SCHUTZENGEL] Recovered plan {_planId} / trip {_tripId}");
				}
			}


			if (_planId is null)
			{
				using JsonDocument cleanup =
					await _api.DeleteAllPlansAsync(
						ct);
			}


			if (_planId is null)
			{
				System.Diagnostics.Debug.WriteLine(
					"[SCHUTZENGEL] Creating new plan...");


				if (_journeyProvider
					is not VvoJourneyProvider vvoProvider)
				{
					throw new InvalidOperationException(
						"Schutzengel tracking requires the VVO journey provider.");
				}


				(
					VvoRoute Route,
					string? SessionId,
					VvoStatus? Status
				)? trackingConnection =
					await vvoProvider.GetSchutzengelConnectionAsync(
						journey,
						ct);


				if (trackingConnection is null)
				{
					throw new InvalidOperationException(
						"Could not rehydrate the selected VVO connection for Schutzengel.");
				}


				object rawData =
					SchutzengelRawDataTranslator.Translate(
						trackingConnection.Value.Route,
						journey,
						trackingConnection.Value.SessionId,
						trackingConnection.Value.Status);


				string serializedPlan =
					SchutzengelPlanTranslator.Serialize(
						journey,
						rawData);


				System.Diagnostics.Debug.WriteLine(
					$"""
					[SCHUTZENGEL] Matched VVO RouteId =
					{trackingConnection.Value.Route.RouteId}
					""");


				System.Diagnostics.Debug.WriteLine(
					$"[SCHUTZENGEL] Plan JSON length = {serializedPlan.Length}");


				using JsonDocument created =
					await _api.CreatePlanAsync(
						serializedPlan,
						ct);


				System.Diagnostics.Debug.WriteLine(
					$"[SCHUTZENGEL] CreatePlan response = {created.RootElement}");


				_planId =
					ReadId(
						created.RootElement,
						"plan_id",
						"id",
						"planId");


				if (_planId is null)
				{
					throw new InvalidOperationException(
						"Schutzengel plan response did not include a plan ID.");
				}


				System.Diagnostics.Debug.WriteLine(
					"[SCHUTZENGEL] Fetching plansMinimal...");


				using JsonDocument plans =
					await _api.GetAllPlansAsync(
						ct);


				System.Diagnostics.Debug.WriteLine(
					$"[SCHUTZENGEL] plansMinimal = {plans.RootElement}");


				_tripId =
					ReadActiveTripId(
						plans.RootElement,
						_planId);


				if (_tripId is null)
				{
					throw new InvalidOperationException(
						"Schutzengel plansMinimal response did not include an active trip ID for the created plan.");
				}


				Preferences.Default.Set(
					"schutzengel_active_plan",
					_planId);


				Preferences.Default.Set(
					"schutzengel_active_trip",
					_tripId);


				Preferences.Default.Set(
					"schutzengel_active_journey",
					JourneyIdentity(journey));
			}


			LiveJourneyState initialState =
				BuildState();


			_state =
				initialState;


			Publish(
				JourneyTrackingEventKind.Started,
				initialState);


			_networkLoopTask =
				_tripId is null
					? null
					: NetworkLoopAsync(
						ct);


			_progressLoopTask =
				ProgressLoopAsync(
					ct);
		}
		catch (
			OperationCanceledException)
			when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"[SCHUTZENGEL] StartAsync failed: {ex}");


			string savedJourney =
				Preferences.Default.Get(
					"schutzengel_active_journey",
					string.Empty);


			bool mayResumeSavedPlan =
				string.IsNullOrWhiteSpace(
					savedJourney)
				|| savedJourney ==
					JourneyIdentity(journey);


			_planId =
				mayResumeSavedPlan
					? Preferences.Default.Get(
						"schutzengel_active_plan",
						string.Empty)
					: string.Empty;


			_tripId =
				mayResumeSavedPlan
					? Preferences.Default.Get(
						"schutzengel_active_trip",
						string.Empty)
					: string.Empty;


			if (string.IsNullOrWhiteSpace(
				_planId))
			{
				_planId = null;
			}


			if (string.IsNullOrWhiteSpace(
				_tripId))
			{
				_tripId = null;
			}


			_tripTimeline =
				SchutzengelTripTimeline.FromJourney(
					journey);


			_remotePhase = null;
			_remoteMessage = null;


			LiveJourneyState fallbackState =
				BuildState(
					$"Local tracking: Schutzengel unavailable ({ex.Message})");


			_state =
				fallbackState;


			Publish(
				JourneyTrackingEventKind.Started,
				fallbackState);


			_progressLoopTask =
				ProgressLoopAsync(
					ct);


			_networkLoopTask =
				null;
		}
	}


	private async Task NetworkLoopAsync(
		CancellationToken ct)
	{
		try
		{
			await SynchronizeSchutzengelAsync(
				ct);
		}
		catch (
			OperationCanceledException)
		when (ct.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"[SCHUTZENGEL] Initial realtime sync failed: {ex}");
		}


		using var timer =
			new PeriodicTimer(
				NetworkSyncInterval);


		while (
			!ct.IsCancellationRequested
			&& _tripId is not null
			&& _journey is not null)
		{
			try
			{
				if (!await timer.WaitForNextTickAsync(
					ct))
				{
					return;
				}


				await SynchronizeSchutzengelAsync(
					ct);
			}
			catch (
				OperationCanceledException)
			when (ct.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(
					$"[SCHUTZENGEL] Realtime sync failed: {ex.Message}");
			}
		}
	}


	private async Task SynchronizeSchutzengelAsync(
		CancellationToken ct)
	{
		Journey? journey =
			_journey;


		string? tripId =
			_tripId;


		if (journey is null
			|| string.IsNullOrWhiteSpace(tripId))
		{
			return;
		}


		string? requestDataVersion =
			_realtimeDataVersion;


		int? requestNotificationCount =
			_notificationCount;


		using JsonDocument server =
			await _api.GetServerTimeAsync(
				ct);


		SynchronizeServerTime(
			server.RootElement);


		using JsonDocument plans =
			await _api.GetAllPlansAsync(
				ct);


		if (TryReadPlanInfo(
			plans.RootElement,
			_planId,
			out string? activeTripId,
			out bool deactivated))
		{
			if (!string.IsNullOrWhiteSpace(
				activeTripId)
				&& !string.Equals(
					activeTripId,
					_tripId,
					StringComparison.Ordinal))
			{
				System.Diagnostics.Debug.WriteLine(
					$"[SCHUTZENGEL] Active trip changed: {_tripId} -> {activeTripId}");


				_tripId =
					activeTripId;


				Preferences.Default.Set(
					"schutzengel_active_trip",
					activeTripId);


				_realtimeDataVersion = null;
				_notificationCount = null;


				_tripTimeline =
					SchutzengelTripTimeline.FromJourney(
						journey);


				requestDataVersion = null;
				requestNotificationCount = null;


				tripId =
					activeTripId;
			}


			if (deactivated)
			{
				_remotePhase =
					TrackingPhase.Paused;

				_remoteMessage =
					"Schutzengel monitoring is paused.";


				RenderProgress();


				return;
			}


			if (_remotePhase ==
				TrackingPhase.Paused)
			{
				_remotePhase = null;
				_remoteMessage = null;
			}
		}
		else
		{
			System.Diagnostics.Debug.WriteLine(
				"[SCHUTZENGEL] Stored plan was not returned by plansMinimal; retaining cached trip data.");

			return;
		}


		using JsonDocument realtime =
			await _api.GetRealtimeAsync(
				tripId,
				requestDataVersion,
				requestNotificationCount,
				ct);


		using JsonDocument notifications =
			await _api.GetNotificationsAsync(
				tripId,
				requestDataVersion,
				requestNotificationCount,
				ct);


		if (SchutzengelTripTimeline.TryParseRealtime(
			realtime.RootElement,
			journey,
			out SchutzengelTripTimeline? updatedTimeline))
		{
			_tripTimeline =
				updatedTimeline;


			if (updatedTimeline.DataVersion is { } version)
			{
				_realtimeDataVersion =
					version.ToString(
						System.Globalization.CultureInfo.InvariantCulture);
			}


			System.Diagnostics.Debug.WriteLine(
				$"[SCHUTZENGEL] Realtime snapshot updated: " +
				$"episodes={updatedTimeline.Episodes.Count}, " +
				$"data_version={updatedTimeline.DataVersion?.ToString() ?? "none"}");
		}


		if (notifications.RootElement.ValueKind ==
			JsonValueKind.Array)
		{
			_notificationCount =
				notifications.RootElement.GetArrayLength();
		}


		string realtimeText =
			realtime.RootElement.GetRawText();


		string notificationText =
			notifications.RootElement.GetRawText();


		string combinedText =
			realtimeText
			+ "\n"
			+ notificationText;


		TrackingPhase phase =
			SchutzengelRealtimeTranslator.Translate(
				combinedText,
				out JourneyTrackingEventKind kind,
				out string? message);


		if (kind is
			JourneyTrackingEventKind.Cancelled
			or JourneyTrackingEventKind.Arrived
			or JourneyTrackingEventKind.RiskChanged)
		{
			_remotePhase =
				phase;

			_remoteMessage =
				message;
		}


		RenderProgress();


		if (kind is
			JourneyTrackingEventKind.Cancelled
			or JourneyTrackingEventKind.Arrived)
		{
			LiveJourneyState state =
				BuildState();


			Publish(
				kind,
				state);


			await CleanupPlanAsync(
				ct);
		}
	}


	private async Task ProgressLoopAsync(
		CancellationToken ct)
	{
		RenderProgress();


		using var timer =
			new PeriodicTimer(
				ProgressUpdateInterval);


		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (!await timer.WaitForNextTickAsync(
					ct))
				{
					return;
				}


				if (_journey is null
					|| _tripTimeline is null)
				{
					return;
				}


				RenderProgress();
			}
			catch (
				OperationCanceledException)
			when (ct.IsCancellationRequested)
			{
				return;
			}
		}
	}


	private void RenderProgress(
		string? overrideMessage = null)
	{
		Journey? journey =
			_journey;


		SchutzengelTripTimeline? timeline =
			_tripTimeline;


		if (journey is null
			|| timeline is null)
		{
			return;
		}


		SchutzengelProgressSnapshot progress =
			timeline.Calculate(
				CurrentTime);


		LiveJourneyState newState =
			BuildState(
				progress,
				overrideMessage);


		LiveJourneyState? previousState =
			_state;


		_state =
			newState;


		LiveJourneyNotification.Show(
			newState,
			progress);


		bool meaningfulChange =
			previousState is null
			|| previousState.Phase != newState.Phase
			|| previousState.CurrentLegIndex !=
				newState.CurrentLegIndex
			|| previousState.EstimatedArrival !=
				newState.EstimatedArrival
			|| previousState.Message !=
				newState.Message;


		if (!meaningfulChange)
		{
			return;
		}


		System.Diagnostics.Debug.WriteLine(
			$"[SCHUTZENGEL PROGRESS] " +
			$"phase={newState.Phase}, " +
			$"leg={newState.CurrentLegIndex + 1}/{newState.LegCount}, " +
			$"progress={progress.TimeProgress:P0}, " +
			$"current={progress.CurrentStopName ?? "-"}, " +
			$"next={progress.NextStopName ?? "-"}");


		if (previousState is null)
		{
			return;
		}


		JourneyTrackingEventKind kind =
			EventKindFor(
				newState.Phase);


		_events.Writer.TryWrite(
			new JourneyTrackingEvent(
				kind,
				newState));
	}


	private LiveJourneyState BuildState(
		string? overrideMessage = null)
	{
		SchutzengelProgressSnapshot progress =
			_tripTimeline?.Calculate(
				CurrentTime)
			?? new SchutzengelProgressSnapshot(
				-1,
				0,
				Math.Max(
					1,
					_journey?.Legs.Count ?? 0),
				0,
				-1,
				TrackingPhase.Planned,
				null,
				null,
				null,
				_journey?.Arrival,
				"Preparing journey monitoring",
				null);


		return BuildState(
			progress,
			overrideMessage);
	}


	private LiveJourneyState BuildState(
		SchutzengelProgressSnapshot progress,
		string? overrideMessage = null)
	{
		TrackingPhase phase =
			_remotePhase
			?? progress.Phase;


		string? message =
			overrideMessage
			?? _remoteMessage
			?? progress.Message;


		return new LiveJourneyState(
			JourneyIdentity(
				_journey!),

			phase,

			Math.Clamp(
				progress.CurrentLegIndex,
				0,
				Math.Max(
					0,
					progress.LegCount - 1)),

			Math.Max(
				1,
				_journey!.Legs.Count),

			_journey.Arrival,

			progress.EstimatedArrival,

			message,

			CurrentTime);
	}


	private static string? ReadActiveTripId(
		JsonElement plans,
		string planId)
	{
		if (plans.ValueKind ==
			JsonValueKind.Object
			&& plans.TryGetProperty(
				"plans",
				out JsonElement nestedPlans))
		{
			return ReadActiveTripId(
				nestedPlans,
				planId);
		}


		if (plans.ValueKind !=
			JsonValueKind.Array)
		{
			return null;
		}


		foreach (JsonElement plan in
			plans.EnumerateArray())
		{
			if (!plan.TryGetProperty(
				"plan_id",
				out JsonElement planIdElement)
				|| planIdElement.ValueKind !=
					JsonValueKind.String
				|| !string.Equals(
					planIdElement.GetString(),
					planId,
					StringComparison.Ordinal))
			{
				continue;
			}


			if (plan.TryGetProperty(
				"active_trip_id",
				out JsonElement tripIdElement)
				&& tripIdElement.ValueKind ==
					JsonValueKind.String)
			{
				return tripIdElement.GetString();
			}
		}


		return null;
	}


	private static bool TryReadPlanInfo(
		JsonElement plans,
		string? planId,
		out string? activeTripId,
		out bool deactivated)
	{
		activeTripId = null;
		deactivated = false;


		if (string.IsNullOrWhiteSpace(
			planId))
		{
			return false;
		}


		if (plans.ValueKind ==
			JsonValueKind.Object
			&& plans.TryGetProperty(
				"plans",
				out JsonElement nestedPlans))
		{
			return TryReadPlanInfo(
				nestedPlans,
				planId,
				out activeTripId,
				out deactivated);
		}


		if (plans.ValueKind !=
			JsonValueKind.Array)
		{
			return false;
		}


		foreach (JsonElement plan in
			plans.EnumerateArray())
		{
			if (!plan.TryGetProperty(
				"plan_id",
				out JsonElement idElement)
				|| idElement.ValueKind !=
					JsonValueKind.String
				|| !string.Equals(
					idElement.GetString(),
					planId,
					StringComparison.Ordinal))
			{
				continue;
			}


			if (plan.TryGetProperty(
				"active_trip_id",
				out JsonElement tripElement)
				&& tripElement.ValueKind ==
					JsonValueKind.String)
			{
				activeTripId =
					tripElement.GetString();
			}


			if (plan.TryGetProperty(
				"deactivated",
				out JsonElement deactivatedElement)
				&& deactivatedElement.ValueKind ==
					JsonValueKind.True)
			{
				deactivated = true;
			}


			return true;
		}


		return false;
	}


	private async Task LocalLoopAsync(
		CancellationToken ct)
	{
		using var timer =
			new PeriodicTimer(
				NetworkSyncInterval);


		while (!ct.IsCancellationRequested
			&& _journey is { } journey)
		{
			try
			{
				if (!await timer.WaitForNextTickAsync(
					ct))
				{
					return;
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}


			Journey current;


			try
			{
				current =
					await RefreshFromPlannerAsync(
						journey,
						ct);
			}
			catch (
				OperationCanceledException)
			when (ct.IsCancellationRequested)
			{
				return;
			}
			catch
			{
				current =
					journey;
			}


			_journey =
				current;


			_tripTimeline =
				SchutzengelTripTimeline.FromJourney(
					current);


			TrackingPhase phase =
				InferLocalPhase(
					current);


			JourneyTrackingEventKind kind =
				EventKindFor(
					phase);


			LiveJourneyState state =
				BuildState();


			_state =
				state;


			Publish(
				kind,
				state);


			if (kind is
				JourneyTrackingEventKind.Cancelled
				or JourneyTrackingEventKind.Arrived)
			{
				await CleanupPlanAsync(
					ct);


				LiveJourneyNotification.Dismiss();


				return;
			}
		}
	}


	public async Task StopAsync(
		CancellationToken cancellationToken = default)
	{
		await StopLoopAsync();


		if (_planId is not null)
		{
			await _api.DeactivateAsync(
				_planId,
				cancellationToken);
		}


		_journey = null;
		_tripTimeline = null;

		_planId = null;
		_tripId = null;

		_remotePhase = null;
		_remoteMessage = null;


		Preferences.Default.Set(
			"schutzengel_tracking_active",
			false);


		LiveJourneyNotification.Dismiss();
		JourneyTrackingForegroundService.Stop();
	}


	private async Task EndAsync()
	{
		await StopLoopAsync();


		if (_planId is not null)
		{
			try
			{
				await _api.DeactivateAsync(
					_planId,
					CancellationToken.None);
			}
			catch
			{
			}


			try
			{
				await _api.DeletePlanAsync(
					_planId,
					CancellationToken.None);
			}
			catch
			{
			}
		}


		ClearSavedPlan();


		_planId = null;
		_tripId = null;
		_journey = null;
		_tripTimeline = null;

		_remotePhase = null;
		_remoteMessage = null;


		Preferences.Default.Set(
			"schutzengel_tracking_active",
			false);


		_tokens.RemoveJourney();

		LiveJourneyNotification.Dismiss();
		JourneyTrackingForegroundService.Stop();
	}


	private async Task CleanupPlanAsync(
		CancellationToken ct)
	{
		if (_planId is not null)
		{
			bool stopped = false;


			try
			{
				await _api.DeactivateAsync(
					_planId,
					ct);

				stopped = true;
			}
			catch
			{
			}


			try
			{
				await _api.DeletePlanAsync(
					_planId,
					ct);

				stopped = true;
			}
			catch
			{
				try
				{
					using JsonDocument plan =
						await _api.GetPlanAsync(
							_planId,
							ct);
				}
				catch
				{
				}
			}


			if (!stopped)
			{
				return;
			}
		}


		ClearSavedPlan();


		Preferences.Default.Set(
			"schutzengel_tracking_active",
			false);


		_tokens.RemoveJourney();


		_planId = null;
		_tripId = null;
		_journey = null;
		_tripTimeline = null;

		_remotePhase = null;
		_remoteMessage = null;


		LiveJourneyNotification.Dismiss();
		JourneyTrackingForegroundService.Stop();
	}


	private async Task StopLoopAsync()
	{
		if (_loopCancellation is null)
		{
			return;
		}


		CancellationTokenSource cancellation =
			_loopCancellation;


		_loopCancellation = null;


		await cancellation.CancelAsync();


		List<Task> tasks =
			[];


		if (_networkLoopTask is not null)
		{
			tasks.Add(
				_networkLoopTask);
		}


		if (_progressLoopTask is not null)
		{
			tasks.Add(
				_progressLoopTask);
		}


		_networkLoopTask = null;
		_progressLoopTask = null;


		if (tasks.Count > 0)
		{
			try
			{
				await Task.WhenAll(
					tasks);
			}
			catch (OperationCanceledException)
			{
			}
		}


		cancellation.Dispose();
	}


	private void Publish(
		JourneyTrackingEventKind kind,
		LiveJourneyState state)
	{
		_state =
			state;


		_events.Writer.TryWrite(
			new JourneyTrackingEvent(
				kind,
				state));


		LiveJourneyNotification.Show(
			state,
			_tripTimeline?.Calculate(
				CurrentTime));
	}


	private async Task<Journey> RefreshFromPlannerAsync(
		Journey original,
		CancellationToken ct)
	{
		JourneyResult result =
			await _journeyProvider.SearchAsync(
				new JourneyQuery
				{
					From =
						new DDjourneys.Core.Models.Location
						{
							Id = original.From.Id,
							Name = original.From.Name,
							Place = original.From.Place,
							Latitude = original.From.Latitude,
							Longitude = original.From.Longitude
						},

					To =
						new DDjourneys.Core.Models.Location
						{
							Id = original.To.Id,
							Name = original.To.Name,
							Place = original.To.Place,
							Latitude = original.To.Latitude,
							Longitude = original.To.Longitude
						},

					DateTime =
						original.Departure
						?? CurrentTime,

					MaxResults = 10,

					TimeoutSeconds = 15
				},
				ct);


		if (!result.IsSuccessful
			|| !result.HasJourneys)
		{
			return original;
		}


		return result.Journeys
			.Select(
				candidate =>
					(
						Journey: candidate,
						Score:
							MatchScore(
								original,
								candidate)))
			.Where(
				item =>
					item.Score > 0)
			.OrderByDescending(
				item =>
					item.Score)
			.ThenBy(
				item =>
					Math.Abs(
						(
							(item.Journey.Departure
								?? CurrentTime)
							- (
								original.Departure
								?? CurrentTime)
						)
						.TotalSeconds))
			.Select(
				item =>
					item.Journey)
			.FirstOrDefault()
			?? original;
	}


	private static int MatchScore(
		Journey original,
		Journey candidate)
	{
		if (original.From.Id != candidate.From.Id
			|| original.To.Id != candidate.To.Id
			|| original.Legs.Count != candidate.Legs.Count)
		{
			return 0;
		}


		int score =
			1;


		for (int i = 0;
			i < original.Legs.Count;
			i++)
		{
			JourneyLeg expected =
				original.Legs[i];


			JourneyLeg actual =
				candidate.Legs[i];


			if (expected.Mode != actual.Mode
				|| expected.Line?.Name !=
					actual.Line?.Name
				|| expected.From.Id !=
					actual.From.Id
				|| expected.To.Id !=
					actual.To.Id)
			{
				return 0;
			}


			score++;
		}


		return score;
	}


	private DateTimeOffset CurrentTime =>
		DateTimeOffset.UtcNow
		+ _serverTimeOffset;


	private TrackingPhase InferLocalPhase(
		Journey journey) =>
		journey.IsCancelled
			? TrackingPhase.Cancelled
			: journey.Arrival <= CurrentTime
				? TrackingPhase.Arrived
				: journey.Transfers.Any(
					transfer =>
						!transfer.IsGuaranteed)
					? TrackingPhase.AtRisk
					: journey.Departure <= CurrentTime
						? TrackingPhase.InProgress
						: TrackingPhase.Planned;


	private static TrackingPhase DetectTerminalOrRisk(
		string text,
		out JourneyTrackingEventKind kind,
		out string? message) =>
		SchutzengelRealtimeTranslator.Translate(
			text,
			out kind,
			out message);


	private JourneyTrackingEventKind EventKindFor(
		TrackingPhase phase) =>
		phase switch
		{
			TrackingPhase.Cancelled =>
				JourneyTrackingEventKind.Cancelled,

			TrackingPhase.Arrived =>
				JourneyTrackingEventKind.Arrived,

			TrackingPhase.AtRisk
				when _state?.Phase !=
					TrackingPhase.AtRisk =>
				JourneyTrackingEventKind.RiskChanged,

			_ =>
				JourneyTrackingEventKind.Updated
		};


	private static string? ReadId(
		JsonElement json,
		params string[] names)
	{
		foreach (string name in names)
		{
			if (json.ValueKind ==
				JsonValueKind.Object
				&& json.TryGetProperty(
					name,
					out JsonElement value)
				&& value.ValueKind ==
					JsonValueKind.String)
			{
				return value.GetString();
			}


			if (json.ValueKind ==
				JsonValueKind.Object
				&& json.TryGetProperty(
					"data",
					out JsonElement data)
				&& data.ValueKind ==
					JsonValueKind.Object
				&& data.TryGetProperty(
					name,
					out value)
				&& value.ValueKind ==
					JsonValueKind.String)
			{
				return value.GetString();
			}
		}


		return null;
	}


	private void SynchronizeServerTime(
		JsonElement root)
	{
		DateTimeOffset? serverTime =
			root.ValueKind switch
			{
				JsonValueKind.String
					when DateTimeOffset.TryParse(
						root.GetString(),
						out DateTimeOffset parsed)
					=>
						parsed,

				JsonValueKind.Number
					when root.TryGetInt64(
						out long epoch)
					=>
						DateTimeOffset.FromUnixTimeMilliseconds(
							Math.Abs(epoch)
								> 100_000_000_000
								? epoch
								: epoch * 1000),

				JsonValueKind.Object =>
					ReadServerTimeProperty(
						root),

				_ =>
					null
			};


		if (serverTime is { } time)
		{
			_serverTimeOffset =
				time - DateTimeOffset.UtcNow;
		}
	}


	private static DateTimeOffset? ReadServerTimeProperty(
		JsonElement root)
	{
		foreach (string propertyName in
			new[]
			{
				"serverTime",
				"server_time",
				"timestamp",
				"time"
			})
		{
			if (!root.TryGetProperty(
				propertyName,
				out JsonElement value))
			{
				continue;
			}


			return value.ValueKind switch
			{
				JsonValueKind.String
					when DateTimeOffset.TryParse(
						value.GetString(),
						out DateTimeOffset parsed)
					=>
						parsed,

				JsonValueKind.Number
					when value.TryGetInt64(
						out long epoch)
					=>
						DateTimeOffset.FromUnixTimeMilliseconds(
							Math.Abs(epoch)
								> 100_000_000_000
								? epoch
								: epoch * 1000),

				_ =>
					null
			};
		}


		return null;
	}


	private static string JourneyIdentity(
		Journey journey) =>
		$"{journey.Id}:{journey.From.Id}:{journey.To.Id}:{journey.Departure:O}:{journey.Arrival:O}";


	private static void ClearSavedPlan()
	{
		Preferences.Default.Remove(
			"schutzengel_active_plan");

		Preferences.Default.Remove(
			"schutzengel_active_trip");

		Preferences.Default.Remove(
			"schutzengel_active_journey");
	}
}