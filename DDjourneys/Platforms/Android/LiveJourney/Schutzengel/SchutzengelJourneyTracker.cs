using System.Text.Json;
using System.Threading.Channels;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelJourneyTracker : IJourneyTracker
{
	internal static Func<Task>? DeactivateHandler { get; set; }
	internal static Func<Task>? DeleteHandler { get; set; }
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
	private readonly SchutzengelApi _api;
	private readonly IJourneyProvider _journeyProvider;
	private readonly SchutzengelTokenStore _tokens = new();
	private readonly Channel<JourneyTrackingEvent> _events = Channel.CreateUnbounded<JourneyTrackingEvent>();
	private CancellationTokenSource? _loopCancellation;
	private Task? _loopTask;
	private Journey? _journey;
	private string? _planId;
	private string? _tripId;
	private LiveJourneyState? _state;
	private TimeSpan _serverTimeOffset;

	public SchutzengelJourneyTracker(IJourneyProvider journeyProvider, HttpClient? http = null)
	{
		_journeyProvider = journeyProvider ?? throw new ArgumentNullException(nameof(journeyProvider));
		_api = new SchutzengelApi(http ?? new HttpClient());
		DeactivateHandler = () => StopAsync();
		DeleteHandler = EndAsync;
	}
	public bool IsAvailable => true;
	public IAsyncEnumerable<JourneyTrackingEvent> Events => _events.Reader.ReadAllAsync();

	internal async Task ResumeStoredAsync(CancellationToken cancellationToken = default)
	{
		if (!Preferences.Default.Get("schutzengel_tracking_active", false)) return;
		var journey = await _tokens.GetJourneyAsync(cancellationToken);
		if (journey is not null) await StartAsync(journey, cancellationToken);
	}

	public async Task StartAsync(Journey journey, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(journey);
		await StopLoopAsync();
		_journey = journey;
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			var permission = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
			if (permission != PermissionStatus.Granted)
				permission = await Permissions.RequestAsync<Permissions.PostNotifications>();
			if (permission != PermissionStatus.Granted)
				throw new InvalidOperationException("Notification permission is required to follow a journey.");
		}
		await _tokens.SaveJourneyAsync(journey, cancellationToken);
		Preferences.Default.Set("schutzengel_tracking_active", true);
		JourneyTrackingForegroundService.Start();
		_loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var ct = _loopCancellation.Token;
		try
		{
			var token = await _tokens.GetAsync(ct);
			if (string.IsNullOrWhiteSpace(token))
			{
				token = await _api.CreateAccountAsync(ct);
				if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Schutzengel returned an empty account token.");
				await _tokens.SetAsync(token, ct);
			}
			_api.Authenticate(token);
			using (var server = await _api.GetServerTimeAsync(ct)) SynchronizeServerTime(server.RootElement);

			_planId = null;
			_tripId = null;
			var storedPlan = Preferences.Default.Get("schutzengel_active_plan", string.Empty);
			var storedTrip = Preferences.Default.Get("schutzengel_active_trip", string.Empty);
			var storedJourney = Preferences.Default.Get("schutzengel_active_journey", string.Empty);
			if (!string.IsNullOrWhiteSpace(storedPlan)
				&& !string.IsNullOrWhiteSpace(storedJourney)
				&& storedJourney != JourneyIdentity(journey))
			{
				try { await _api.DeactivateAsync(storedPlan, ct); } catch { }
				await _api.DeletePlanAsync(storedPlan, ct);
				ClearSavedPlan();
				storedPlan = string.Empty;
				storedTrip = string.Empty;
			}
			if (!string.IsNullOrWhiteSpace(storedPlan) && !string.IsNullOrWhiteSpace(storedTrip))
			{
				try
				{
					using var plans = await _api.GetAllPlansAsync(ct);
					if (SchutzengelPlanRecovery.ContainsPlan(plans.RootElement, storedPlan))
					{
						_planId = storedPlan;
						_tripId = storedTrip;
						await _api.ActivateAsync(_planId, ct);
					}
				}
				catch { throw; }
			}

			if (_planId is null)
			{
				using var cleanup = await _api.DeleteAllPlansAsync(ct);
			}
			if (_planId is null)
			{
				using var created = await _api.CreatePlanAsync(SchutzengelPlanTranslator.Serialize(journey), ct);
				_planId = ReadId(created.RootElement, "plan_id", "id", "planId");
				_tripId = ReadId(created.RootElement, "trip_id", "tripId");
				if (_planId is null || _tripId is null) throw new InvalidOperationException("Schutzengel plan response did not include plan and trip IDs.");
				Preferences.Default.Set("schutzengel_active_plan", _planId);
				Preferences.Default.Set("schutzengel_active_trip", _tripId);
				Preferences.Default.Set("schutzengel_active_journey", JourneyIdentity(journey));
				await _api.SetOptionsAsync(_planId, ct);
				await _api.ActivateAsync(_planId, ct);
			}
			_state = LocalState(journey, TrackingPhase.Planned, "Monitoring with Schutzengel");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
		catch (Exception ex)
		{
			var savedJourney = Preferences.Default.Get("schutzengel_active_journey", string.Empty);
			var mayResumeSavedPlan = string.IsNullOrWhiteSpace(savedJourney) || savedJourney == JourneyIdentity(journey);
			_planId = mayResumeSavedPlan ? Preferences.Default.Get("schutzengel_active_plan", string.Empty) : string.Empty;
			_tripId = mayResumeSavedPlan ? Preferences.Default.Get("schutzengel_active_trip", string.Empty) : string.Empty;
			if (string.IsNullOrWhiteSpace(_planId)) _planId = null;
			if (string.IsNullOrWhiteSpace(_tripId)) _tripId = null;
			_state = LocalState(journey, InferLocalPhase(journey), $"Local tracking: Schutzengel unavailable ({ex.Message})");
		}
		Publish(JourneyTrackingEventKind.Started, _state!);
		_loopTask = _tripId is null ? LocalLoopAsync(ct) : PollLoopAsync(ct);
	}

	private async Task LocalLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _journey is { } journey)
		{
			try { await Task.Delay(PollInterval, ct); }
			catch (OperationCanceledException) { return; }
			Journey current;
			try { current = await RefreshFromPlannerAsync(journey, ct); }
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
			catch { current = journey; }
			var phase = InferLocalPhase(current);
			var kind = EventKindFor(phase);
			Publish(kind, LocalState(current, phase, "Using DDjourneys realtime monitoring"));
			if (kind is JourneyTrackingEventKind.Cancelled or JourneyTrackingEventKind.Arrived)
			{
				await CleanupPlanAsync(ct);
				LiveJourneyNotification.Dismiss();
				return;
			}
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		await StopLoopAsync();
		if (_planId is not null)
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			await _api.DeactivateAsync(_planId, cts.Token);
		}
		_journey = null;
		Preferences.Default.Set("schutzengel_tracking_active", false);
		LiveJourneyNotification.Dismiss();
		JourneyTrackingForegroundService.Stop();
	}

	private async Task EndAsync()
	{
		await StopLoopAsync();
		if (_planId is not null)
		{
			try { await _api.DeactivateAsync(_planId, CancellationToken.None); } catch { }
			await _api.DeletePlanAsync(_planId, CancellationToken.None);
		}
		ClearSavedPlan();
		_planId = null;
		_tripId = null;
		_journey = null;
		Preferences.Default.Set("schutzengel_tracking_active", false);
		_tokens.RemoveJourney();
		LiveJourneyNotification.Dismiss();
		JourneyTrackingForegroundService.Stop();
	}

	private async Task PollLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _tripId is not null && _journey is not null)
		{
			try
			{
				await Task.Delay(PollInterval, ct);
				using var server = await _api.GetServerTimeAsync(ct);
				SynchronizeServerTime(server.RootElement);
				using var realtime = await _api.GetRealtimeAsync(_tripId, ct);
				using var notices = await _api.GetNotificationsAsync(_tripId, ct);
				var raw = realtime.RootElement.ToString() + notices.RootElement;
				var phase = DetectTerminalOrRisk(raw, out var kind, out var message);
				if (kind == JourneyTrackingEventKind.RiskChanged && _state?.Phase == TrackingPhase.AtRisk) kind = JourneyTrackingEventKind.Updated;
				var state = LocalState(_journey, phase, message);
				Publish(kind, state);
				if (kind is JourneyTrackingEventKind.Cancelled or JourneyTrackingEventKind.Arrived)
				{
					await CleanupPlanAsync(ct);
					LiveJourneyNotification.Dismiss();
					return;
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
			catch (Exception ex)
			{
				Journey current;
				try { current = await RefreshFromPlannerAsync(_journey!, ct); }
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
				catch { current = _journey!; }
				var phase = InferLocalPhase(current);
				var kind = EventKindFor(phase);
				var state = LocalState(current, phase, $"Using DDjourneys realtime monitoring: {ex.Message}");
				Publish(kind, state);
				if (kind is JourneyTrackingEventKind.Cancelled or JourneyTrackingEventKind.Arrived)
				{
					await CleanupPlanAsync(ct);
					LiveJourneyNotification.Dismiss();
					return;
				}
			}
		}
	}

	private async Task CleanupPlanAsync(CancellationToken ct)
	{
		if (_planId is not null)
		{
			var stopped = false;
			try { await _api.DeactivateAsync(_planId, ct); stopped = true; } catch { }
			try { await _api.DeletePlanAsync(_planId, ct); stopped = true; }
			catch
			{
				try { using var plan = await _api.GetPlanAsync(_planId, ct); }
				catch { }
			}
			if (!stopped) return;
		}
		ClearSavedPlan();
		Preferences.Default.Set("schutzengel_tracking_active", false);
		_tokens.RemoveJourney();
		_planId = null;
		_tripId = null;
		_journey = null;
		JourneyTrackingForegroundService.Stop();
	}

	private async Task StopLoopAsync()
	{
		if (_loopCancellation is null) return;
		var cancellation = _loopCancellation;
		_loopCancellation = null;
		await cancellation.CancelAsync();
		if (_loopTask is not null)
		{
			try { await _loopTask; } catch (OperationCanceledException) { }
			_loopTask = null;
		}
		cancellation.Dispose();
	}

	private void Publish(JourneyTrackingEventKind kind, LiveJourneyState state)
	{
		_state = state;
		_events.Writer.TryWrite(new JourneyTrackingEvent(kind, state));
		LiveJourneyNotification.Show(state);
	}

	private async Task<Journey> RefreshFromPlannerAsync(Journey original, CancellationToken ct)
	{
		var result = await _journeyProvider.SearchAsync(new JourneyQuery
		{
			From = new DDjourneys.Core.Models.Location { Id = original.From.Id, Name = original.From.Name, Place = original.From.Place, Latitude = original.From.Latitude, Longitude = original.From.Longitude },
			To = new DDjourneys.Core.Models.Location { Id = original.To.Id, Name = original.To.Name, Place = original.To.Place, Latitude = original.To.Latitude, Longitude = original.To.Longitude },
			DateTime = original.Departure ?? CurrentTime,
			MaxResults = 10,
			TimeoutSeconds = 15
		}, ct);
		if (!result.IsSuccessful || !result.HasJourneys) return original;

		return result.Journeys
			.Select(candidate => (Journey: candidate, Score: MatchScore(original, candidate)))
			.Where(item => item.Score > 0)
			.OrderByDescending(item => item.Score)
			.ThenBy(item => Math.Abs(((item.Journey.Departure ?? CurrentTime) - (original.Departure ?? CurrentTime)).TotalSeconds))
			.Select(item => item.Journey)
			.FirstOrDefault() ?? original;
	}

	private static int MatchScore(Journey original, Journey candidate)
	{
		if (original.From.Id != candidate.From.Id || original.To.Id != candidate.To.Id || original.Legs.Count != candidate.Legs.Count) return 0;
		var score = 1;
		for (var i = 0; i < original.Legs.Count; i++)
		{
			var expected = original.Legs[i];
			var actual = candidate.Legs[i];
			if (expected.Mode != actual.Mode || expected.Line?.Name != actual.Line?.Name || expected.From.Id != actual.From.Id || expected.To.Id != actual.To.Id) return 0;
			score += 1;
		}
		return score;
	}

	private LiveJourneyState LocalState(Journey j, TrackingPhase phase, string? message) => new(
		JourneyIdentity(j), phase,
		Math.Clamp(j.Legs.ToList().FindIndex(l => l.EffectiveArrival is { } arrival && arrival >= CurrentTime), 0, Math.Max(0, j.Legs.Count - 1)),
		j.Legs.Count, j.Arrival, j.Legs.LastOrDefault()?.EffectiveArrival, message, CurrentTime);
	private DateTimeOffset CurrentTime => DateTimeOffset.UtcNow + _serverTimeOffset;
	private TrackingPhase InferLocalPhase(Journey j) => j.IsCancelled ? TrackingPhase.Cancelled : j.Arrival <= CurrentTime ? TrackingPhase.Arrived : j.Transfers.Any(t => !t.IsGuaranteed) ? TrackingPhase.AtRisk : j.Departure <= CurrentTime ? TrackingPhase.InProgress : TrackingPhase.Planned;
	private static TrackingPhase DetectTerminalOrRisk(string text, out JourneyTrackingEventKind kind, out string? message)
		=> SchutzengelRealtimeTranslator.Translate(text, out kind, out message);
	private JourneyTrackingEventKind EventKindFor(TrackingPhase phase) => phase switch
	{
		TrackingPhase.Cancelled => JourneyTrackingEventKind.Cancelled,
		TrackingPhase.Arrived => JourneyTrackingEventKind.Arrived,
		TrackingPhase.AtRisk when _state?.Phase != TrackingPhase.AtRisk => JourneyTrackingEventKind.RiskChanged,
		_ => JourneyTrackingEventKind.Updated
	};
	private static string? ReadId(JsonElement json, params string[] names)
	{
		foreach (var name in names)
		{
			if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
			if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String) return v.GetString();
		}
		return null;
	}
	private void SynchronizeServerTime(JsonElement root)
	{
		foreach (var propertyName in new[] { "serverTime", "server_time", "timestamp", "time" })
		{
			if (!root.TryGetProperty(propertyName, out var value)) continue;
			DateTimeOffset? serverTime = value.ValueKind switch
			{
				JsonValueKind.String when DateTimeOffset.TryParse(value.GetString(), out var parsed) => parsed,
				JsonValueKind.Number when value.TryGetInt64(out var epoch) => DateTimeOffset.FromUnixTimeMilliseconds(Math.Abs(epoch) > 100_000_000_000 ? epoch : epoch * 1000),
				_ => null
			};
			if (serverTime is { } time) _serverTimeOffset = time - DateTimeOffset.UtcNow;
			return;
		}
	}
	private static string JourneyIdentity(Journey journey) => $"{journey.Id}:{journey.From.Id}:{journey.To.Id}:{journey.Departure:O}:{journey.Arrival:O}";
	private static void ClearSavedPlan() { Preferences.Default.Remove("schutzengel_active_plan"); Preferences.Default.Remove("schutzengel_active_trip"); Preferences.Default.Remove("schutzengel_active_journey"); }
}


