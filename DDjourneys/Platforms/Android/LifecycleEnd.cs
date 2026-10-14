namespace DDjourneys.Platforms.Android;

/// <summary>Runs once when the activity is destroyed (it can be recreated): static subscriptions end with it.</summary>
internal sealed class LifecycleEnd(Action end) : Java.Lang.Object, AndroidX.Lifecycle.IDefaultLifecycleObserver
{
	public void OnDestroy(AndroidX.Lifecycle.ILifecycleOwner owner) =>
		end();
}
