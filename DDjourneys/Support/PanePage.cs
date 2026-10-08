using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// A page that can show the next page of its flow beside itself. In a narrow window it is an ordinary Shell page.
/// In a wider one (<see cref="PaneRules.MinPaneWidth"/> per pane, up to <see cref="PaneRules.MaxPanes"/>) the current
/// Shell page is the host: a page opened from it that may stand beside it (<see cref="PaneRules"/>, <see cref="Panes"/>)
/// is not pushed but lent to it, and its content shows in the next column. The host shows the deepest pages of its chain
/// that fit; each further step moves the chain one column to the left. Back (Android back and gesture, the title bar's
/// back, the back arrow and close button of the panes) closes the deepest pane first. The host owns the chain. When the window width crosses the limit the pages move between Shell stack and
/// panes as new page instances on the same view models: native views are never moved from one page to another.
/// The frame (<see cref="PaneFrame"/>) is MAUI's <see cref="Microsoft.Maui.Controls.Foldable.TwoPaneView"/>: on a foldable
/// the panes sit on either side of the fold, and a vertical fold always shows at least two panes.
/// </summary>
public class PanePage : ContentPage
{
	private const uint MoveDuration = 300;

	private readonly List<PaneSlot> _slots = [];
	private readonly PaneFrame _frame;
	private View? _own;
	private List<(View View, double FromX, bool Fade)> _moving = [];
	private int _capacity = 1;
	private PanePage? _host;
	private BackButtonBehavior? _back;
	private bool? _navBarVisible;
	private bool _framing;
	private bool _wide;
	private bool _appeared;
	private bool _shownAsPane;
	private bool _gone;
	private bool _evaluationQueued;

	public PanePage()
	{
		PaneRoute = PaneRules.RouteOf(GetType());

		// The frame is the page's content from the start; the page's own content goes into it when XAML sets it,
		// so it never has to move later (moving a view that already has native views breaks its layout on Windows).
		_frame = new PaneFrame();

		// A fold appeared, moved or went: how many panes fit is decided again.
		_frame.FoldChanged += (_, _) => QueueEvaluation();

		SetFrame();
	}

	/// <summary>The route the page stands for; pairs are decided by route.</summary>
	public string? PaneRoute { get; internal set; }

	/// <summary>True while the page is shown as a pane of another page (it is then not in the window itself).</summary>
	public bool IsLent => _host is not null;

	/// <summary>The page that dialogs and popups go to: the host while lent.</summary>
	protected ContentPage DialogPage => _host ?? (ContentPage)this;

	/// <summary>
	/// State that is not in the view model, as a navigation query: a new instance of the page gets it when the page
	/// moves between Shell stack and panes. Pages whose state lives in their view model need nothing.
	/// </summary>
	protected internal virtual IDictionary<string, object>? RecreationQuery => null;

	internal bool IsWide => _wide;

	/// <summary>Panes lent to this page; Back closes the deepest.</summary>
	internal int PaneCount => _slots.Count;

	/// <summary>How many panes fit now.</summary>
	internal int Capacity => _capacity;

	// ---------------------------------------------------------------- dialogs (a lent page has no window)

	public new Task DisplayAlertAsync(string? title, string? message, string cancel) =>
		_host is { } host
			? host.DisplayAlertAsync(title, message, cancel)
			: base.DisplayAlertAsync(title!, message!, cancel);

	public new Task<bool> DisplayAlertAsync(string? title, string? message, string? accept, string cancel) =>
		_host is { } host
			? host.DisplayAlertAsync(title, message, accept, cancel)
			: base.DisplayAlertAsync(title!, message!, accept!, cancel);

	public new Task<string> DisplayActionSheetAsync(string? title, string? cancel, string? destruction, params string[] buttons) =>
		_host is { } host
			? host.DisplayActionSheetAsync(title, cancel, destruction, buttons)
			: base.DisplayActionSheetAsync(title!, cancel!, destruction!, buttons);

	// ---------------------------------------------------------------- content

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (propertyName == ContentProperty.PropertyName && !_framing && !ReferenceEquals(Content, _frame))
		{
			SetFrame();
		}
	}

	/// <summary>Whatever was set as content becomes the page's own content inside the frame.</summary>
	private void SetFrame()
	{
		_framing = true;

		try
		{
			View? content = Content;

			if (_own is not null)
			{
				_frame.Strip.Remove(_own);
			}

			Content = _frame;
			_own = content;

			if (content is not null)
			{
				_frame.Strip.Insert(0, content);
			}

		}
		finally
		{
			_framing = false;
		}
	}

	// ---------------------------------------------------------------- lifecycle

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (_host is null)
		{
			_appeared = true;
			SyncPanes();
			QueueEvaluation();
			Panes.NotifyBackChanged();
		}
	}

	protected override void OnDisappearing()
	{
		if (_host is null)
		{
			_appeared = false;
			SyncPanes();
		}

		base.OnDisappearing();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);
		QueueEvaluation();
	}

	/// <summary>Android back and the Shell back button close the right pane first.</summary>
	protected override bool OnBackButtonPressed() =>
		ClosePane() || base.OnBackButtonPressed();

	/// <summary>Call from <c>OnNavigatedFrom</c> instead of <see cref="PageTeardown"/>: a page replaced by its new instance keeps its view model.</summary>
	protected void LeaveIfGone(NavigatedFromEventArgs args)
	{
		if (_host is null && !_gone && !Panes.IsRearranging && PageTeardown.IsLeavingForGood(args))
		{
			LeaveForGood();
		}
	}

	/// <summary>The page is gone for good: by default its view model is disposed.</summary>
	protected virtual void OnLeftForGood()
	{
		if (BindingContext is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}

	/// <summary>A new instance of the page took over its view model: let go of the view model without disposing it.</summary>
	protected virtual void OnRetired()
	{
	}

	internal void LeaveForGood()
	{
		if (_gone)
		{
			return;
		}

		_gone = true;

		while (_slots.Count > 0)
		{
			Drop(_slots[^1]);
		}

		OnLeftForGood();
	}

	internal void Retire()
	{
		if (_gone)
		{
			return;
		}

		_gone = true;
		OnRetired();
	}

	/// <summary>A lent page is told when it becomes visible or hidden, as if it were a page of its own.</summary>
	private void SetShownAsPane(bool shown)
	{
		if (_shownAsPane == shown)
		{
			return;
		}

		_shownAsPane = shown;

		if (shown)
		{
			OnAppearing();
		}
		else
		{
			OnDisappearing();
		}
	}

	private void SyncPanes()
	{
		foreach (PaneSlot slot in _slots)
		{
			slot.Page.SetShownAsPane(_appeared && slot.IsVisible);
		}
	}

	// ---------------------------------------------------------------- width

	internal void QueueEvaluation()
	{
		if (_host is not null || _gone || _evaluationQueued)
		{
			return;
		}

		_evaluationQueued = true;

		// Never rearrange inside a layout pass.
		Dispatcher.Dispatch(
			() =>
			{
				_evaluationQueued = false;
				_ = EvaluateAsync();
			});
	}

	private async Task EvaluateAsync()
	{
		if (_host is not null
			|| _gone
			|| Panes.IsRearranging
			|| Width <= 0
			|| !ReferenceEquals(Shell.Current?.CurrentPage, this))
		{
			return;
		}

		// Width, and a vertical fold (book posture, a dual screen: always one pane on each side of it).
		int previous = _capacity;
		_capacity = _frame.Capacity(previous);
		_wide = _capacity >= 2;

		try
		{
			if (_wide && _slots.Count == 0)
			{
				await Panes.GatherAsync(this);
			}
			else if (!_wide && _slots.Count > 0)
			{
				await Panes.SpreadAsync(this);
			}
			else if (_slots.Count > 0 && _capacity != previous)
			{
				// More or fewer panes fit: the same chain, more or fewer of it shown (no motion for a resize).
				Arrange(animate: false);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Panes] rearranging failed: {ex}");
		}
	}

	// ---------------------------------------------------------------- chain

	/// <summary>Position of <paramref name="source"/> (a page or its view model) in the chain: 0 is this page, -1 not found.</summary>
	internal int IndexOf(object source)
	{
		if (ReferenceEquals(source, this) || ReferenceEquals(source, BindingContext))
		{
			return 0;
		}

		for (int i = 0; i < _slots.Count; i++)
		{
			PanePage page = _slots[i].Page;

			if (ReferenceEquals(source, page) || ReferenceEquals(source, page.BindingContext))
			{
				return i + 1;
			}
		}

		return -1;
	}

	internal string? RouteAt(int index) =>
		index == 0
			? PaneRoute
			: _slots[index - 1].Page.PaneRoute;

	/// <summary>Shows <paramref name="page"/> right of the chain position <paramref name="index"/>; panes further right are closed.</summary>
	internal void Open(PanePage page, int index)
	{
		while (_slots.Count > index)
		{
			Drop(_slots[^1]);
		}

		Adopt(page);
		Arrange();
	}

	/// <summary>Closes the right-most pane (animated). False when there is none.</summary>
	internal bool ClosePane()
	{
		if (_slots.Count == 0)
		{
			return false;
		}

		PaneSlot slot = _slots[^1];
		_slots.RemoveAt(_slots.Count - 1);
		slot.InputTransparent = true;

		Arrange();
		_ = RetireSlotAsync(slot);

		return true;
	}

	/// <summary>Closes every pane at once (the app goes back to the start).</summary>
	internal void CloseAllPanes()
	{
		if (_slots.Count == 0)
		{
			return;
		}

		while (_slots.Count > 0)
		{
			Drop(_slots[^1]);
		}

		Arrange();
	}

	/// <summary>Narrow to wide: new instances of the pages taken off the Shell stack become panes.</summary>
	internal void Gather(IReadOnlyList<PanePage> pages, int capacity)
	{
		_wide = true;
		_capacity = Math.Max(2, capacity);

		foreach (PanePage page in pages)
		{
			Adopt(page);
		}

		Arrange();
	}

	/// <summary>Wide to narrow: the panes are taken down; their pages are returned (in order) to be replaced on the Shell stack.</summary>
	internal List<PanePage> Spread()
	{
		_wide = false;

		List<PanePage> pages = [];

		while (_slots.Count > 0)
		{
			PaneSlot slot = _slots[0];
			_slots.RemoveAt(0);

			slot.Page.SetShownAsPane(false);
			slot.Detach();
			(slot.Parent as Grid)?.Remove(slot);
			slot.Page._host = null;

			pages.Add(slot.Page);
		}

		Arrange();

		return pages;
	}

	/// <summary>The page's own content leaves its (never shown) frame for a pane.</summary>
	private View? TakeContent()
	{
		View? content = _own;
		_own = null;

		if (content is not null)
		{
			_frame.Strip.Remove(content);
		}

		return content;
	}

	private void Adopt(PanePage page)
	{
		View? content = page.TakeContent();
		page._host = this;

		if (content is not null)
		{
			// Motion.Prepare hid the content for a page entrance; a pane has its own.
			content.Opacity = 1;
			content.TranslationX = 0;
			content.Scale = 1;
		}

		var slot = new PaneSlot(page, content, () => ClosePane());
		_slots.Add(slot);
		_frame.Strip.Add(slot);
	}

	/// <summary>Removes a pane without motion and lets its page go.</summary>
	private void Drop(PaneSlot slot)
	{
		_slots.Remove(slot);
		slot.Page.SetShownAsPane(false);
		slot.Detach();
		(slot.Parent as Grid)?.Remove(slot);
		slot.Page.LeaveForGood();
	}

	private async Task RetireSlotAsync(PaneSlot slot)
	{
		slot.Page.SetShownAsPane(false);

		if (Motion.Enabled && PaneWidth(_capacity) > 0)
		{
			try
			{
				await Task.WhenAll(
					slot.TranslateToAsync(PaneWidth(_capacity), 0, 220, Curves.Accelerate),
					slot.FadeToAsync(0, 180, Curves.Accelerate));
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Pane exit skipped: {ex.Message}");
			}
		}

		slot.Detach();
		(slot.Parent as Grid)?.Remove(slot);
		slot.Page.LeaveForGood();
	}

	// ---------------------------------------------------------------- layout and motion

	/// <summary>
	/// Shows the deepest pages of the chain that fit (or the page alone) and keeps back, title bar, pane headers and
	/// lifecycle in step.
	/// </summary>
	private void Arrange(bool animate = true)
	{
		List<View> chain = [];

		if (_own is not null)
		{
			chain.Add(_own);
		}

		chain.AddRange(_slots);

		if (chain.Count == 0)
		{
			return;
		}

		int shown = _slots.Count == 0 ? 1 : Math.Clamp(_capacity, 1, chain.Count);
		List<View> visible = chain.GetRange(chain.Count - shown, shown);

		// Where each pane is on screen now (a running motion included), before the cells change.
		Dictionary<View, double> before = [];

		foreach (View view in chain)
		{
			if (view.IsVisible && view.Width > 0)
			{
				before[view] = view.X + view.TranslationX;
			}
		}

		foreach (View view in chain)
		{
			view.IsVisible = visible.Contains(view);
		}

		_frame.Layout(visible);

		// The page's own title bar offers back only when it shows (its content is visible) and is not the start page.
		_navBarVisible ??= Shell.GetNavBarIsVisible(this);
		bool ownVisible = _own?.IsVisible ?? false;
		bool titleBarBack = ownVisible && _navBarVisible.Value && !IsStartPage;

		Shell.SetNavBarIsVisible(this, _navBarVisible.Value && ownVisible);

		PaneSlot? firstShown = _slots.FirstOrDefault(slot => slot.IsVisible);

		for (int i = 0; i < _slots.Count; i++)
		{
			PaneSlot slot = _slots[i];

			slot.SetNavigation(
				back: ReferenceEquals(slot, firstShown) && !titleBarBack,
				close: i == _slots.Count - 1);
		}

		if (_slots.Count > 0)
		{
			Shell.SetBackButtonBehavior(this, _back ??= new BackButtonBehavior { Command = new Command(() => ClosePane()) });
		}
		else
		{
			ClearValue(Shell.BackButtonBehaviorProperty);
		}

		Animate(visible, before, animate);

		SyncPanes();
		Panes.NotifyBackChanged();
	}

	/// <summary>The root of the Shell stack has no back of its own.</summary>
	private static bool IsStartPage =>
		Shell.Current?.Navigation.NavigationStack.Count is not > 1;

	/// <summary>
	/// One motion for all panes (FLIP): each pane that stays starts where it was on screen and glides into its new cell; a
	/// new deepest pane slides in from the right, a pane coming back into view on the left fades in. The new cells are
	/// known exactly (<see cref="PaneFrame.LeftOf"/>), so the start offset is set in the same pass as the cell and no frame
	/// shows a pane in the wrong place. A single animation drives every pane, so they move in step; a new arrangement
	/// takes over from a running one where it stands. Entrances inside the panes stay quiet meanwhile.
	/// </summary>
	private void Animate(List<View> visible, Dictionary<View, double> before, bool animate)
	{
		if (!animate || !Motion.Enabled || before.Count == 0)
		{
			FinishMotion();
			return;
		}

		List<(View View, double FromX, bool Fade)> items = [];

		for (int i = 0; i < visible.Count; i++)
		{
			View view = visible[i];

			if (before.TryGetValue(view, out double was))
			{
				double offset = was - _frame.LeftOf(i);

				if (Math.Abs(offset) > 0.5)
				{
					items.Add((view, offset, false));
				}
			}
			else
			{
				items.Add((view, i == visible.Count - 1 ? 48 : 0, true));
			}
		}

		RunMotion(items);
	}

	private void RunMotion(List<(View View, double FromX, bool Fade)> items)
	{
		FinishMotion();

		if (items.Count == 0)
		{
			return;
		}

		Motion.QuietEntrances(MoveDuration + 200);

		foreach ((View view, double from, bool fade) in items)
		{
			view.CancelAnimations();
			view.TranslationX = from;

			if (fade)
			{
				view.Opacity = 0;
			}
		}

		_moving = items;

		var motion =
			new Animation(
				progress =>
				{
					foreach ((View view, double from, bool fade) in items)
					{
						view.TranslationX = from * (1 - progress);

						if (fade)
						{
							view.Opacity = Math.Min(1, progress * 1.6);
						}
					}
				},
				0,
				1,
				Curves.Decelerate);

		motion.Commit(_frame, PaneMotion, 16, MoveDuration, finished: (_, _) => Settle(items));
	}

	private const string PaneMotion = "DDjourneysPanes";

	/// <summary>Ends a running pane motion with every pane in place.</summary>
	private void FinishMotion()
	{
		List<(View View, double FromX, bool Fade)> running = _moving;
		_moving = [];

		if (running.Count > 0)
		{
			_frame.AbortAnimation(PaneMotion);
			Settle(running);
		}
	}

	private static void Settle(List<(View View, double FromX, bool Fade)> items)
	{
		foreach ((View view, _, _) in items)
		{
			view.TranslationX = 0;
			view.Opacity = 1;
		}
	}

	/// <summary>One column (the page's width before the frame has been laid out).</summary>
	private double PaneWidth(int columns) =>
		(_frame.Width > 0 ? _frame.Width : Width) / Math.Max(1, columns);
}
