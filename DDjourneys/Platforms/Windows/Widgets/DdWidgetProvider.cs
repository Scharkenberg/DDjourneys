using System.Collections.Concurrent;
using DDjourneys.Core.Widgets;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DDjourneys.Platforms.Windows.Widgets;

/// <summary>
/// The widget provider the Windows Widgets Board talks to (verified against the Windows App SDK docs:
/// "Implement a widget provider in a C# Windows app"). The host CoCreates the class behind
/// <see cref="WindowsWidgets"/>, then calls the six methods of <see cref="IWidgetProvider"/>: a widget that
/// became visible (Activate) is served its cached snapshot and refreshed in the background; a widget
/// without settings gets the set-up card; a tap on a widget refreshes it, its link "Show in the app" opens the app.
/// </summary>
public sealed partial class DdWidgetProvider : IWidgetProvider
{
	// The board calls from its own threads, all on this one instance.
	private readonly ConcurrentDictionary<string, bool> _active = new(StringComparer.Ordinal);

	public DdWidgetProvider()
	{
		// Recover the widgets the host knows about after a restart or a crash of the provider.
		try
		{
			foreach (WidgetInfo info in WidgetManager.GetDefault().GetWidgetInfos())
			{
				if (info.WidgetContext?.Id is { Length: > 0 } id)
				{
					_active[id] = false;

					WindowsWidgets.RememberSize(info.WidgetContext);
				}
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Reading the pinned widgets failed", ex);
		}
	}

	public void CreateWidget(WidgetContext widgetContext)
	{
		if (widgetContext?.Id is not { Length: > 0 } id)
		{
			return;
		}

		_active[id] = false;

		WindowsWidgets.RememberSize(widgetContext);

		WindowsTrace.Write($"[Widgets] CreateWidget {id} ({widgetContext.DefinitionId}, size {widgetContext.Size})");

		try
		{
			// The app may still be building when the board creates a widget: the kind waits for the store.
			if (WindowsWidgets.Store is null)
			{
				WindowsWidgets.RememberEarly(id, widgetContext.DefinitionId ?? string.Empty);

				return;
			}

			// The picker entry decides the kind: a route widget starts as a route widget in the set-up page.
			if (WindowsWidgets.Store is { } store
				&& store.LoadConfig(id) is null)
			{
				store.SaveConfig(
					id,
					new WidgetConfig
					{
						Kind = string.Equals(widgetContext.DefinitionId, WindowsWidgets.RouteDefinition, StringComparison.Ordinal)
							? WidgetKind.Route
							: WidgetKind.Departures
					});
			}

			// The board expects a card right after a widget was created, not only when it first becomes visible.
			WidgetUpdaterWin.Serve(id);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Creating the widget failed", ex);
		}
	}

	public void DeleteWidget(string widgetId, string customState)
	{
		WindowsTrace.Write($"[Widgets] DeleteWidget {widgetId}");

		_active.TryRemove(widgetId, out _);
		WidgetUpdaterWin.Forget(widgetId);
		WindowsWidgets.Store?.Remove(widgetId);

		if (_active.Count == 0)
		{
			WindowsBackground.SetKeepAlive(false);
		}
	}

	public void Activate(WidgetContext widgetContext)
	{
		if (widgetContext?.Id is not { Length: > 0 } id)
		{
			return;
		}

		WindowsTrace.Write($"[Widgets] Activate {id}");

		_active[id] = true;

		WindowsWidgets.RememberSize(widgetContext);
		WidgetUpdaterWin.SetVisible(id, true);
		WindowsBackground.SetKeepAlive(true);

		// Serve what is cached at once, then look for newer rows in the background.
		WidgetUpdaterWin.Serve(id);
	}

	public void Deactivate(string widgetId)
	{
		WindowsTrace.Write($"[Widgets] Deactivate {widgetId}");

		WidgetUpdaterWin.SetVisible(widgetId, false);

		if (_active.ContainsKey(widgetId))
		{
			_active[widgetId] = false;
		}

		if (_active.Count == 0 || _active.Values.All(static active => !active))
		{
			// Nobody is looking: the process may go when the rest of the app has nothing left either
			// (the board relaunches it by COM when it wants it again).
			WindowsBackground.SetKeepAlive(false);
		}
	}

	public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
	{
		string? id = actionInvokedArgs?.WidgetContext?.Id;

		if (id is null)
		{
			return;
		}

		WindowsTrace.Write($"[Widgets] action '{actionInvokedArgs!.Verb}' on {id}");

		// The size can have changed since the last look: the card that answers is built for the size the board has now.
		WindowsWidgets.RememberSize(actionInvokedArgs.WidgetContext);

		switch (actionInvokedArgs!.Verb)
		{
			case "refresh":
				_ = WidgetUpdaterWin.RefreshAsync(id);
				break;

			case "open":
				WindowsWidgets.OpenApp(id);
				break;

			case "setup":
				WindowsWidgets.OpenSetup(id);
				break;
		}
	}

	public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
	{
		// A size change is the only case today: the card is built for the size, so it is built again (and the
		// rows a bigger size has room for are asked for).
		string? id = contextChangedArgs?.WidgetContext?.Id;

		if (id is { Length: > 0 })
		{
			WindowsTrace.Write($"[Widgets] context changed {id}: {contextChangedArgs!.WidgetContext!.Size}");

			WindowsWidgets.RememberSize(contextChangedArgs.WidgetContext);
			WidgetUpdaterWin.Serve(id);
		}
	}
}
