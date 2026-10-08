using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// Navigation between the pages of a flow (planner, results, journey, map; departures, run, map). In a wide window a
/// pair from <see cref="PaneRules"/> opens beside the page it was opened from instead of on top of it (see
/// <see cref="PanePage"/>); everything else, and every navigation in a narrow window, is ordinary Shell navigation.
/// Also turns the Shell stack into panes and back when the window width crosses the limit.
/// </summary>
public static class Panes
{
	/// <summary>True while pages move between the Shell stack and panes; the page transition stays out of it.</summary>
	public static bool IsRearranging { get; private set; }

	/// <summary>The current page shows panes: Back closes the deepest one (instead of leaving the page).</summary>
	public static bool CanGoBack =>
		Shell.Current?.CurrentPage is PanePage { PaneCount: > 0 };

	/// <summary><see cref="CanGoBack"/> may have changed (panes opened or closed, another page came to the front).</summary>
	public static event EventHandler? BackChanged;

	/// <summary>Closes the deepest pane of the current page; false when there is none (Back then does what it always does).</summary>
	public static bool GoBack() =>
		Shell.Current?.CurrentPage is PanePage page && page.ClosePane();

	internal static void NotifyBackChanged() =>
		BackChanged?.Invoke(null, EventArgs.Empty);

	/// <summary>
	/// Opens <paramref name="route"/>. <paramref name="from"/> is the page or view model asking: it decides which pane
	/// the new page stands beside. Without it the navigation is always a Shell push.
	/// </summary>
	public static async Task GoToAsync(
		string route,
		ShellNavigationQueryParameters parameters,
		object? from = null)
	{
		ArgumentNullException.ThrowIfNull(parameters);

		if (Shell.Current is not { } shell)
		{
			return;
		}

		if (from is not null
			&& !IsRearranging
			&& shell.CurrentPage is PanePage { IsWide: true } host
			&& !parameters.ContainsKey(Routes.MapMode))
		{
			int index = host.IndexOf(from);

			if (index >= 0
				&& PaneRules.CanPair(host.RouteAt(index), route)
				&& Create(route) is { } page)
			{
				Deliver(page, parameters);
				host.Open(page, index);

				return;
			}
		}

		await shell.GoToAsync(route, parameters);
	}

	/// <summary>Back to the start page: every page on the stack drops its panes first.</summary>
	public static Task ToStartAsync(Shell shell)
	{
		ArgumentNullException.ThrowIfNull(shell);

		foreach (Page? page in StackOf(shell))
		{
			(page as PanePage)?.CloseAllPanes();
		}

		return shell.GoToAsync($"//{Routes.Plan}");
	}

	/// <summary>
	/// Narrow to wide: the top of the Shell stack and the pages under it that pair become panes of the lowest one.
	/// The popped pages are replaced by new instances on the same view models (their native views stay behind).
	/// </summary>
	internal static async Task GatherAsync(PanePage top)
	{
		if (IsRearranging || Shell.Current is not { } shell)
		{
			return;
		}

		List<Page?> stack = StackOf(shell);
		int last = stack.Count - 1;

		if (last < 1 || !ReferenceEquals(stack[last], top))
		{
			return;
		}

		int first = last;

		while (first > 0
			&& stack[first - 1] is PanePage left
			&& stack[first] is PanePage right
			&& PaneRules.CanPair(left.PaneRoute, right.PaneRoute))
		{
			first--;
		}

		if (first == last || stack[first] is not PanePage host)
		{
			return;
		}

		List<PanePage> moving = [.. stack.Skip(first + 1).OfType<PanePage>()];
		List<PanePage> panes = [];

		IsRearranging = true;

		try
		{
			for (int i = 0; i < moving.Count; i++)
			{
				await shell.Navigation.PopAsync(false);
			}

			foreach (PanePage old in moving)
			{
				if (Recreate(old) is { } page)
				{
					old.Retire();
					panes.Add(page);
				}
				else
				{
					old.LeaveForGood();
				}
			}

			host.Gather(panes, top.Capacity);
		}
		finally
		{
			IsRearranging = false;
		}

		DiagnosticLog.Write($"[Panes] wide window: {panes.Count} page(s) beside {host.PaneRoute}");

		host.QueueEvaluation();
	}

	/// <summary>Wide to narrow: the panes become pages on the Shell stack again (new instances, same view models), in order.</summary>
	internal static async Task SpreadAsync(PanePage host)
	{
		if (IsRearranging || Shell.Current is not { } shell)
		{
			return;
		}

		List<PanePage> moving = host.Spread();

		if (moving.Count == 0)
		{
			return;
		}

		int pushed = 0;

		IsRearranging = true;

		try
		{
			foreach (PanePage old in moving)
			{
				if (Recreate(old) is not { } page)
				{
					old.LeaveForGood();

					continue;
				}

				old.Retire();
				await shell.Navigation.PushAsync(page, false);
				pushed++;
			}
		}
		finally
		{
			IsRearranging = false;
		}

		DiagnosticLog.Write($"[Panes] narrow window: {pushed} pane(s) back on the stack above {host.PaneRoute}");

		(shell.CurrentPage as PanePage)?.QueueEvaluation();
	}

	/// <summary>A new instance of a page on the same view model, with the state the page keeps itself.</summary>
	private static PanePage? Recreate(PanePage old)
	{
		if (IPlatformApplication.Current?.Services is not { } services
			|| PaneRules.Recreate(old, services) is not { } page)
		{
			return null;
		}

		page.PaneRoute = old.PaneRoute;

		if (old.RecreationQuery is { } query)
		{
			Deliver(page, query);
		}

		return page;
	}

	/// <summary>The current Shell stack; Shell keeps its root as null, so it is filled in.</summary>
	private static List<Page?> StackOf(Shell shell)
	{
		if (shell.CurrentItem?.CurrentItem is not ShellSection section)
		{
			return [];
		}

		List<Page?> stack = [.. section.Stack];

		if (stack.Count > 0
			&& stack[0] is null
			&& section.CurrentItem is IShellContentController content)
		{
			stack[0] = content.Page;
		}

		return stack;
	}

	private static PanePage? Create(string route)
	{
		if (PaneRules.PageFor(route) is not { } type
			|| IPlatformApplication.Current?.Services.GetService(type) is not PanePage page)
		{
			return null;
		}

		page.PaneRoute = route;

		return page;
	}

	/// <summary>The query goes where Shell would put it: the page and its view model.</summary>
	private static void Deliver(PanePage page, IDictionary<string, object> query)
	{
		if (page is IQueryAttributable target)
		{
			target.ApplyQueryAttributes(query);
		}

		if (page.BindingContext is IQueryAttributable model
			&& !ReferenceEquals(model, page))
		{
			model.ApplyQueryAttributes(query);
		}
	}
}
