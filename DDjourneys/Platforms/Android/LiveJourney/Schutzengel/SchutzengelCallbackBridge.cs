namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>What the Android components (service, receiver, activity) may ask of the tracker.</summary>
internal interface ISchutzengelPlatformCallbacks
{
	/// <summary>The foreground service ended (system timeout or destroy).</summary>
	void ServiceStopped();

	/// <summary>A button or the swipe of the live notification was used.</summary>
	Task HandleNotificationActionAsync(string action, string planId);

	/// <summary>The app came to the foreground.</summary>
	Task ResumeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The injected link between Android components that DI cannot create (service, receiver,
/// activity) and the tracker. It is a normal singleton, so no static tracker reference exists:
/// the tracker attaches itself on construction and detaches when it is disposed. Calls with
/// no tracker attached are harmless no-ops.
/// </summary>
internal sealed class SchutzengelCallbackBridge
{
	private volatile ISchutzengelPlatformCallbacks? _target;

	public bool IsAttached => _target is not null;

	public void Attach(ISchutzengelPlatformCallbacks target)
	{
		ArgumentNullException.ThrowIfNull(target);

		_target = target;
	}

	/// <summary>Detaches only if <paramref name="target"/> is still the attached one.</summary>
	public void Detach(ISchutzengelPlatformCallbacks target) =>
		Interlocked.CompareExchange(ref _target, null, target);

	public void ServiceStopped() =>
		_target?.ServiceStopped();

	public Task HandleNotificationActionAsync(string action, string planId) =>
		_target?.HandleNotificationActionAsync(action, planId) ?? Task.CompletedTask;

	public Task ResumeAsync(CancellationToken cancellationToken = default) =>
		_target?.ResumeAsync(cancellationToken) ?? Task.CompletedTask;
}
