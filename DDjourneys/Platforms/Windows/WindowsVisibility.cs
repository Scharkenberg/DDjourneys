using DDjourneys.Support;
using Microsoft.UI.Windowing;

namespace DDjourneys.Platforms.Windows;

/// <summary>Tells <see cref="AppVisibility"/> when the window is minimised or hidden (no page lifecycle event does).</summary>
internal static class WindowsVisibility
{
	public static void Attach(Microsoft.UI.Xaml.Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		try
		{
			window.AppWindow.Changed += OnChanged;
			Update(window.AppWindow);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Watching the window visibility failed", ex);
		}
	}

	private static void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
	{
		if (args.DidVisibilityChange || args.DidPresenterChange)
		{
			Update(sender);
		}
	}

	private static void Update(AppWindow window)
	{
		bool minimised = window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

		AppVisibility.Set(window.IsVisible && !minimised);
	}
}
