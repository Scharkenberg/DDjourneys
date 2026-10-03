namespace DDjourneys.Core.Tracking.Live;

/// <summary>
/// Where a platform shows the live state of a followed journey and its alerts. The tracker only
/// decides what to show; a surface decides how. Platforms without a native presentation use
/// <see cref="NoLiveJourneySurface"/>: tracking keeps working, the in-app pages show the state.
/// </summary>
/// <remarks>Calls arrive on arbitrary threads and must never throw.</remarks>
public interface ILiveJourneySurface
{
	/// <summary>True when the platform can present live state outside the app.</summary>
	bool IsSupported { get; }

	/// <summary>
	/// True when the surface presents a paused journey (with a way to resume it) instead of removing the
	/// presentation. Surfaces that cannot resume from outside the app keep the default.
	/// </summary>
	bool ShowsPaused => false;

	/// <summary>Shows or replaces the single live presentation.</summary>
	void Show(LiveJourneyContent content);

	/// <summary>Removes the live presentation.</summary>
	void Dismiss();

	/// <summary>Raises one alert; a later alert of the same kind for the same plan replaces it.</summary>
	void ShowAlert(JourneyAlert alert);

	/// <summary>Removes every alert of one plan (it was deleted).</summary>
	void ClearAlerts(string planId);
}

/// <summary>For platforms without a live presentation: everything is a no-op.</summary>
public sealed class NoLiveJourneySurface : ILiveJourneySurface
{
	public bool IsSupported => false;

	public void Show(LiveJourneyContent content)
	{
	}

	public void Dismiss()
	{
	}

	public void ShowAlert(JourneyAlert alert)
	{
	}

	public void ClearAlerts(string planId)
	{
	}
}
