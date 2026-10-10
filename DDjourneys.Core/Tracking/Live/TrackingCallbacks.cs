namespace DDjourneys.Core.Tracking.Live;

/// <summary>Stable action names of the live presentation (notification buttons, swipe, tap).</summary>
/// <remarks>
/// The values are part of pending intents that outlive app updates; never change them.
/// </remarks>
public static class TrackingActions
{
	public const string Pause = "dd.journey.pause";
	public const string Resume = "dd.journey.resume";
	public const string Stop = "dd.journey.stop";
	public const string Dismissed = "dd.journey.dismissed";
	public const string Open = "dd.journey.open";

	/// <summary>Find alternatives for a missed or endangered connection (opens the planner, searching now).</summary>
	public const string Replan = "dd.journey.replan";

	/// <summary>Name of the plan id in platform payloads (intent extras, URIs).</summary>
	public const string PlanIdKey = "plan_id";
}

/// <summary>What platform components outside DI (services, receivers, activities) may ask of the tracker.</summary>
public interface ITrackingCallbacks
{
	/// <summary>The platform ended the keep-alive (system timeout or destroy).</summary>
	void ServiceStopped();

	/// <summary>An action of the live presentation (<see cref="TrackingActions"/>) was used.</summary>
	Task HandleActionAsync(string action, string planId);

	/// <summary>The app came to the foreground.</summary>
	Task ResumeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The injected link between platform components that DI cannot create and the tracker. It is a
/// normal singleton, so no static tracker reference exists: the tracker attaches itself on
/// construction and detaches when it is disposed. Calls with no tracker attached are no-ops.
/// </summary>
public sealed class TrackingCallbackBridge
{
	private volatile ITrackingCallbacks? _target;

	public bool IsAttached => _target is not null;

	public void Attach(ITrackingCallbacks target)
	{
		ArgumentNullException.ThrowIfNull(target);

		_target = target;
	}

	/// <summary>Detaches only if <paramref name="target"/> is still the attached one.</summary>
	public void Detach(ITrackingCallbacks target) =>
		Interlocked.CompareExchange(ref _target, null, target);

	public void ServiceStopped() =>
		_target?.ServiceStopped();

	public Task HandleActionAsync(string action, string planId) =>
		_target?.HandleActionAsync(action, planId) ?? Task.CompletedTask;

	public Task ResumeAsync(CancellationToken cancellationToken = default) =>
		_target?.ResumeAsync(cancellationToken) ?? Task.CompletedTask;
}
