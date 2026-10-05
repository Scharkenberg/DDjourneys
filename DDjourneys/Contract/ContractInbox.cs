using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Models;
using DDjourneys.Pages;
using DDjourneys.Support;
using DDjourneys.Tracking;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Contract;

/// <summary>
/// Where every transport drops what it received (<see cref="ContractEntry"/>), and where the requests are
/// carried out once the UI is ready. Requests are handled one at a time in arrival order; an identical
/// request within <see cref="DuplicateWindow"/> is a double delivery and dropped.
/// Plan and tracked show UI and reply only on error; capabilities replies at once; pick replies when the
/// user chooses a journey (see <see cref="ContractSession"/>).
/// </summary>
public sealed class ContractInbox
{
	private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(15);
	private const int MaxAttempts = 40;
	private const int MaxQueued = 5;

	private readonly ContractPlaceResolver _resolver;
	private readonly ContractSession _session;
	private readonly ContractResponder _responder;
	private readonly TrackedJourneyNavigator _navigator;

	private readonly Lock _gate = new();
	private readonly Queue<ContractParseResult> _queue = new();
	private bool _delivering;
	private string? _lastFingerprint;
	private DateTimeOffset _lastAt;

	public ContractInbox(
		ContractPlaceResolver resolver,
		ContractSession session,
		ContractResponder responder,
		TrackedJourneyNavigator navigator)
	{
		_resolver = resolver;
		_session = session;
		_responder = responder;
		_navigator = navigator;

		ContractEntry.Attach(this);
	}

	/// <summary>A request is waiting or being carried out.</summary>
	public bool HasPending
	{
		get
		{
			lock (_gate)
			{
				return _queue.Count > 0 || _delivering;
			}
		}
	}

	/// <summary>Remembers a parsed request. A flood keeps only the newest few.</summary>
	public void Submit(ContractParseResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		lock (_gate)
		{
			_queue.Enqueue(result);

			while (_queue.Count > MaxQueued)
			{
				_queue.Dequeue();
			}
		}
	}

	/// <summary>Carries out everything queued; safe to call from anywhere, any number of times. Never throws.</summary>
	public async Task DeliverAsync()
	{
		lock (_gate)
		{
			if (_delivering || _queue.Count == 0)
			{
				return;
			}

			_delivering = true;
		}

		try
		{
			while (true)
			{
				ContractParseResult next;

				lock (_gate)
				{
					if (!_queue.TryDequeue(out ContractParseResult? item))
					{
						return;
					}

					next = item;
				}

				await ProcessAsync(next).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Contract delivery failed: {ex}");
		}
		finally
		{
			lock (_gate)
			{
				_delivering = false;
			}
		}
	}

	private async Task ProcessAsync(ContractParseResult parsed)
	{
		if (parsed.Failure is { } failure)
		{
			await _responder
				.SendAsync(ContractReply.Failure(failure), failure.Callbacks ?? ContractCallbacks.None)
				.ConfigureAwait(false);

			return;
		}

		ContractRequest request = parsed.Request!;

		if (IsDoubleDelivery(request))
		{
			return;
		}

		try
		{
			switch (request.Command)
			{
				case ContractCommand.Capabilities:
					await _responder
						.SendAsync(
							ContractReply.Success(request, ContractCapabilities.Values(AppInfo.Current.VersionString)),
							request.Callbacks)
						.ConfigureAwait(false);
					break;

				case ContractCommand.Tracked:
					_session.End();
					_navigator.Request(request.PlanId);
					await _navigator.DeliverAsync().ConfigureAwait(false);
					break;

				case ContractCommand.Plan:
				case ContractCommand.Pick:
				case ContractCommand.Go:
					await OpenPlannerAsync(request).ConfigureAwait(false);
					break;

				case ContractCommand.Home:
					// The quick action "take me home": the same code path, the same messages.
					_session.End();
					AppShortcuts.Submit(AppShortcut.Home);
					break;

				case ContractCommand.Departures:
					await OpenDeparturesAsync(request).ConfigureAwait(false);
					break;

				case ContractCommand.Map:
					_session.End();

					await ShowAsync(
						Routes.Map,
						await _resolver.ResolveAtAsync(request, CancellationToken.None).ConfigureAwait(false) is { } centre
							? new ShellNavigationQueryParameters { [Routes.MapAt] = centre }
							: null)
						.ConfigureAwait(false);
					break;

				case ContractCommand.Disruptions:
					_session.End();

					await ShowAsync(
						Routes.Disruptions,
						request.Line is { } filter
							? new ShellNavigationQueryParameters { [Routes.LineName] = filter }
							: null)
						.ConfigureAwait(false);
					break;

				case ContractCommand.Live:
					_session.End();

					await ShowAsync(
						Routes.Vehicles,
						new ShellNavigationQueryParameters { [Routes.Line] = request.Line! })
						.ConfigureAwait(false);
					break;
			}
		}
		catch (ContractRefusal refusal)
		{
			await _responder
				.SendAsync(ContractReply.Failure(request, refusal.Code, refusal.Message), request.Callbacks)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			await _responder
				.SendAsync(
					ContractReply.Failure(request, ContractErrorCode.Internal, "The request took too long."),
					request.Callbacks)
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Contract request failed: {ex}");

			await _responder
				.SendAsync(
					ContractReply.Failure(request, ContractErrorCode.Internal, "The request could not be carried out."),
					request.Callbacks)
				.ConfigureAwait(false);
		}
	}

	private async Task OpenPlannerAsync(ContractRequest request)
	{
		using var timeout = new CancellationTokenSource(ResolveTimeout);

		ResolvedPlan plan =
		await _resolver.ResolveAsync(request, CancellationToken.None).ConfigureAwait(false);

		if (request.Command == ContractCommand.Pick)
		{
			_session.Begin(request);
		}
		else
		{
			_session.End();
		}

		await RetryUntilReadyAsync(() => TryApplyAsync(plan)).ConfigureAwait(false);
	}

	/// <summary>The departures of a place (the stop nearest to the device when none is named), arrivals and time as asked.</summary>
	private async Task OpenDeparturesAsync(ContractRequest request)
	{
		_session.End();

		Location? stop =
			await _resolver.ResolveAtAsync(request, CancellationToken.None).ConfigureAwait(false);

		// A place that is not a stop has no departures; the nearest stop of the device is the next best thing.
		if (stop is null || !stop.IsStation)
		{
			AppShortcuts.Submit(AppShortcut.DeparturesHere);

			return;
		}

		DateTime? boardTime = null;

		if (request.Time is { IsNow: false } time)
		{
			boardTime = time.Absolute is { } absolute ? Format.ToWall(absolute) : time.Wall;
		}

		var parameters =
			new ShellNavigationQueryParameters
			{
				[Routes.Stop] = stop,
				[Routes.BoardArrivals] = request.Mode == JourneySearchMode.Arrival
			};

		if (boardTime is { } wall)
		{
			parameters[Routes.BoardTime] = wall;
		}

		await ShowAsync(Routes.Departures, parameters).ConfigureAwait(false);
	}

	/// <summary>Shows a page over the planner (unwinding whatever is pushed).</summary>
	private static Task ShowAsync(string route, ShellNavigationQueryParameters? parameters) =>
		RetryUntilReadyAsync(
			async () =>
			{
				if (Shell.Current is not { CurrentPage: not null } shell)
				{
					return false;
				}

				await shell.GoToAsync($"//{Routes.Plan}");
				await shell.GoToAsync(route, parameters ?? []);

				return true;
			});

	/// <summary>A cold start delivers before the shell exists: tries on the UI thread until it does (or gives up).</summary>
	private static async Task RetryUntilReadyAsync(Func<Task<bool>> attempt)
	{
		for (int tries = 0; tries < MaxAttempts; tries++)
		{
			try
			{
				if (await MainThread.InvokeOnMainThreadAsync(attempt).ConfigureAwait(false))
				{
					return;
				}
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Contract navigation not ready yet: {ex.Message}");
			}

			await Task.Delay(RetryDelay).ConfigureAwait(false);
		}
	}

	/// <summary>Shows the planner (unwinding any pushed pages) and fills it; false while the shell is not ready.</summary>
	private static async Task<bool> TryApplyAsync(ResolvedPlan plan)
	{
		if (Shell.Current is not { CurrentPage: not null } shell)
		{
			return false;
		}

		await shell.GoToAsync($"//{Routes.Plan}");

		if (shell.CurrentPage is not PlanPage page)
		{
			return false;
		}

		page.ApplyContract(plan);

		return true;
	}

	private bool IsDoubleDelivery(ContractRequest request)
	{
		DateTimeOffset now = Format.Now();

		lock (_gate)
		{
			bool duplicate =
				_lastFingerprint == request.Fingerprint
				&& now - _lastAt < DuplicateWindow;

			_lastFingerprint = request.Fingerprint;
			_lastAt = now;

			return duplicate;
		}
	}
}

/// <summary>
/// The static doorway for platform code that runs before (or without) dependency injection: the
/// activation hook may fire before the service provider exists. Requests wait here until the inbox attaches.
/// </summary>
public static class ContractEntry
{
	private static readonly Lock Gate = new();
	private static readonly List<ContractParseResult> Early = [];
	private static ContractInbox? _inbox;

	/// <summary>A request from outside has arrived and is not done yet.</summary>
	public static bool HasPending
	{
		get
		{
			lock (Gate)
			{
				return Early.Count > 0 || (_inbox?.HasPending ?? false);
			}
		}
	}

	/// <summary>Parses a link and hands it over. Never throws.</summary>
	public static void SubmitUri(string? link) =>
		Submit(ContractParser.ParseUri(link));

	public static void Submit(ContractParseResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		Ensure();

		ContractInbox? inbox;

		lock (Gate)
		{
			inbox = _inbox;

			if (inbox is null)
			{
				if (Early.Count < 5)
				{
					Early.Add(result);
				}

				return;
			}
		}

		inbox.Submit(result);
		_ = inbox.DeliverAsync();
	}

	internal static void Attach(ContractInbox inbox)
	{
		List<ContractParseResult> waiting;

		lock (Gate)
		{
			_inbox = inbox;
			waiting = [.. Early];
			Early.Clear();
		}

		foreach (ContractParseResult result in waiting)
		{
			inbox.Submit(result);
		}
	}

	/// <summary>Creates the inbox when the services exist (creating it attaches it); harmless otherwise.</summary>
	private static void Ensure()
	{
		lock (Gate)
		{
			if (_inbox is not null)
			{
				return;
			}
		}

		try
		{
			_ = IPlatformApplication.Current?.Services.GetService<ContractInbox>();
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Contract inbox unavailable: {ex.Message}");
		}
	}

	/// <summary>Delivers whatever is queued (called when the UI becomes active).</summary>
	public static void Deliver()
	{
		Ensure();

		ContractInbox? inbox;

		lock (Gate)
		{
			inbox = _inbox;
		}

		if (inbox is not null)
		{
			_ = inbox.DeliverAsync();
		}
	}
}
