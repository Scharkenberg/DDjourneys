using DDjourneys.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>How an element enters when it first appears (<see cref="Motion.EnterProperty"/>).</summary>
public enum EntranceStyle
{
	None,

	/// <summary>Fades in.</summary>
	Fade,

	/// <summary>Rises from below while it fades in and grows slightly.</summary>
	Rise,

	/// <summary>Pops in from a smaller size, with a small overshoot.</summary>
	Zoom,

	/// <summary>Slides in from the right.</summary>
	Slide,

	/// <summary>Draws itself from the left (lines, dividers).</summary>
	Grow
}

/// <summary>
/// Central switch and helpers for animations. Every animation in the app goes through here,
/// so the "Animations" setting, cancellation and failures are handled in exactly one place.
/// Rules: never block interaction, never leave a view invisible, never throw.
/// <para>
/// Most motion is declared, not coded: the global styles (Controls.xaml) set <c>Motion.Enter</c>,
/// <c>Motion.OnShow</c> and <c>Motion.Pulse</c> on cards, buttons, labels and dividers, so everything that appears,
/// is shown later or changes its value moves. Curves are the Material 3 easing tokens (<see cref="Curves"/>).
/// </para>
/// </summary>
public static class Motion
{
	private const int CascadeCap = 8;

	private static readonly object Marker = new();
	private static readonly ConditionalWeakTable<Page, object> Entered = [];

	/// <summary>Views that are being animated in right now: a second entrance on the same view is skipped.</summary>
	private static readonly ConditionalWeakTable<VisualElement, object> Playing = [];

	/// <summary>Views whose entrance has played (once per instance, so recycled list cells do not replay it).</summary>
	private static readonly ConditionalWeakTable<VisualElement, object> Seen = [];

	private static readonly ConditionalWeakTable<VisualElement, object> Breathing = [];
	private static readonly ConditionalWeakTable<VisualElement, Quiet> Quiets = [];

	/// <summary>The app setting, and the OS: "remove animations" always wins.</summary>
	public static bool Enabled
	{
		get => _enabled;
		private set
		{
			if (_enabled == value)
			{
				return;
			}

			_enabled = value;

			// Whoever waits for animations to come back is woken now instead of polling.
			Interlocked.Exchange(ref _enabledSignal, NewSignal()).TrySetResult();
		}
	}

	private static bool _enabled = true;
	private static TaskCompletionSource _enabledSignal = NewSignal();

	private static TaskCompletionSource NewSignal() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// Waits until a looping animation has a reason to run again: animations are switched on, or a long pause has
	/// passed (so a view that was removed meanwhile is not held alive for ever).
	/// </summary>
	internal static async Task WaitUntilWorthAnimatingAsync(VisualElement view)
	{
		if (!Enabled)
		{
			TaskCompletionSource signal = _enabledSignal;

			if (!Enabled)
			{
				await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(30)));
			}

			return;
		}

		// Off screen, or on a page that is under another one: nothing to animate, check again later.
		await Task.Delay(1500);
	}

	/// <summary>False for a view that is hidden, not loaded, or on a page that is not the one the user sees.</summary>
	internal static bool IsShowing(VisualElement view)
	{
		if (!view.IsVisible || !view.IsLoaded)
		{
			return false;
		}

		Element? parent = view;

		while (parent is not null and not Page)
		{
			parent = parent.Parent;
		}

		return parent is not Page page
			|| Shell.Current is not { } shell
			|| ReferenceEquals(shell.CurrentPage, page);
	}

	public static void Bind(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		Enabled = settings.Animations && !SystemAccessibility.ReduceMotion;

		settings.Changed += (_, name) =>
		{
			if (name == nameof(AppSettings.Animations))
			{
				Enabled = settings.Animations && !SystemAccessibility.ReduceMotion;
			}
		};

		SystemAccessibility.Changed += (_, _) =>
			Enabled = settings.Animations && !SystemAccessibility.ReduceMotion;
	}

	// ---------------------------------------------------------------- attached properties

	/// <summary>
	/// Press and hover feedback for anything tappable: <c>support:Motion.Feedback="True"</c>. A tap gives a short
	/// press-in and a springy release; the mouse pointer (Windows) lifts the element a little while it is over it.
	/// Never replaces the element's own recognizers.
	/// </summary>
	public static readonly BindableProperty FeedbackProperty =
		BindableProperty.CreateAttached("Feedback", typeof(bool), typeof(Motion), false, propertyChanged: OnFeedbackChanged);

	/// <summary>How the element enters when it first loads. Set by the global styles for cards, buttons and dividers.</summary>
	public static readonly BindableProperty EnterProperty =
		BindableProperty.CreateAttached("Enter", typeof(EntranceStyle), typeof(Motion), EntranceStyle.None, propertyChanged: OnEnterChanged);

	/// <summary>
	/// An element that becomes visible later (a notice, a risk line, an expanded section) rises in instead of
	/// appearing. The first moments after loading and after a list cell was given new data are exempt.
	/// </summary>
	public static readonly BindableProperty OnShowProperty =
		BindableProperty.CreateAttached("OnShow", typeof(bool), typeof(Motion), false, propertyChanged: OnOnShowChanged);

	/// <summary>A label whose text changes while it is on screen (a delay, a time) ticks over: dips and rises in.</summary>
	public static readonly BindableProperty PulseProperty =
		BindableProperty.CreateAttached("Pulse", typeof(bool), typeof(Motion), false, propertyChanged: OnPulseChanged);

	/// <summary>A slow fade in and out, for things that are live ("in progress").</summary>
	public static readonly BindableProperty BreatheProperty =
		BindableProperty.CreateAttached("Breathe", typeof(bool), typeof(Motion), false, propertyChanged: OnBreatheChanged);

	/// <summary>Rotation that is animated to instead of jumped to (an expand chevron). Bind it instead of Rotation.</summary>
	public static readonly BindableProperty TurnProperty =
		BindableProperty.CreateAttached("Turn", typeof(double), typeof(Motion), 0.0, propertyChanged: OnTurnChanged);

	public static bool GetFeedback(BindableObject b) => (bool)b.GetValue(FeedbackProperty);

	public static void SetFeedback(BindableObject b, bool v) => b.SetValue(FeedbackProperty, v);

	public static EntranceStyle GetEnter(BindableObject b) => (EntranceStyle)b.GetValue(EnterProperty);

	public static void SetEnter(BindableObject b, EntranceStyle v) => b.SetValue(EnterProperty, v);

	public static bool GetOnShow(BindableObject b) => (bool)b.GetValue(OnShowProperty);

	public static void SetOnShow(BindableObject b, bool v) => b.SetValue(OnShowProperty, v);

	public static bool GetPulse(BindableObject b) => (bool)b.GetValue(PulseProperty);

	public static void SetPulse(BindableObject b, bool v) => b.SetValue(PulseProperty, v);

	public static bool GetBreathe(BindableObject b) => (bool)b.GetValue(BreatheProperty);

	public static void SetBreathe(BindableObject b, bool v) => b.SetValue(BreatheProperty, v);

	public static double GetTurn(BindableObject b) => (double)b.GetValue(TurnProperty);

	public static void SetTurn(BindableObject b, double v) => b.SetValue(TurnProperty, v);

	private static void OnFeedbackChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not View view || newValue is not true || oldValue is true)
		{
			return;
		}

		var tap = new TapGestureRecognizer();

		tap.Tapped += (_, _) => _ = TapAsync(view);
		view.GestureRecognizers.Add(tap);

		var pointer = new PointerGestureRecognizer();

		pointer.PointerEntered += (_, _) => _ = HoverAsync(view, true);
		pointer.PointerExited += (_, _) => _ = HoverAsync(view, false);
		view.GestureRecognizers.Add(pointer);
	}

	private static void OnEnterChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not VisualElement view)
		{
			return;
		}

		view.Loaded -= EnterLoaded;

		if ((EntranceStyle)newValue != EntranceStyle.None)
		{
			view.Loaded += EnterLoaded;
		}
	}

	private static void EnterLoaded(object? sender, EventArgs e)
	{
		if (!Enabled || sender is not VisualElement view || !Seen.TryAdd(view, Marker))
		{
			return;
		}

		// Siblings come one after another; the base delay lets the page's own transition get going first.
		int delay = 50 + (Math.Min(VisibleIndex(view), CascadeCap + 1) * 45);

		switch (GetEnter(view))
		{
			case EntranceStyle.Fade:
				_ = PlayAsync(view, 0, 0, 1, delay, 280, Curves.Emphasized);
				break;

			case EntranceStyle.Rise:
				_ = PlayAsync(view, 0, 22, 0.97, delay, 420, Curves.Decelerate);
				break;

			case EntranceStyle.Zoom:
				_ = PlayAsync(view, 0, 0, 0.78, delay, 420, Curves.Settle);
				break;

			case EntranceStyle.Slide:
				_ = PlayAsync(view, 36, 0, 0.98, delay, 380, Curves.Decelerate);
				break;

			case EntranceStyle.Grow:
				_ = GrowAsync(view, delay);
				break;
		}
	}

	private static void OnOnShowChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not VisualElement view || newValue is not true || oldValue is true)
		{
			return;
		}

		Quiet quiet = QuietOf(view);

		view.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName != VisualElement.IsVisibleProperty.PropertyName
				|| !view.IsVisible
				|| !Enabled
				|| !view.IsLoaded
				|| Environment.TickCount64 < quiet.Until)
			{
				return;
			}

			_ = PlayAsync(view, 0, 12, 0.98, 0, 320, Curves.Decelerate);
		};
	}

	private static void OnPulseChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not Label label || newValue is not true || oldValue is true)
		{
			return;
		}

		Quiet quiet = QuietOf(label);

		quiet.Text = label.Text;

		label.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName != Label.TextProperty.PropertyName)
			{
				return;
			}

			string? previous = quiet.Text;

			quiet.Text = label.Text;

			if (!Enabled
				|| !label.IsLoaded
				|| string.IsNullOrEmpty(previous)
				|| previous == label.Text
				|| Environment.TickCount64 < quiet.Until)
			{
				return;
			}

			_ = TickAsync(label);
		};
	}

	private static void OnBreatheChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not VisualElement view)
		{
			return;
		}

		view.Loaded -= BreatheLoaded;

		if (newValue is true)
		{
			view.Loaded += BreatheLoaded;
		}
	}

	private static void BreatheLoaded(object? sender, EventArgs e)
	{
		if (sender is VisualElement view && Breathing.TryAdd(view, Marker))
		{
			_ = BreatheAsync(view);
		}
	}

	private static void OnTurnChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not VisualElement view)
		{
			return;
		}

		double to = (double)newValue;

		if (!Enabled || !view.IsLoaded || view.Handler is null)
		{
			view.Rotation = to;
			return;
		}

		_ = TurnAsync(view, to);
	}

	/// <summary>When an element last got data or loaded, so that initial bindings are not animated as changes.</summary>
	private sealed class Quiet
	{
		public long Until;

		public string? Text;
	}

	private static Quiet QuietOf(VisualElement view)
	{
		if (Quiets.TryGetValue(view, out Quiet? existing))
		{
			return existing;
		}

		var quiet = new Quiet { Until = Environment.TickCount64 + 800 };

		Quiets.Add(view, quiet);

		view.BindingContextChanged += (_, _) => quiet.Until = Environment.TickCount64 + 400;
		view.Loaded += (_, _) => quiet.Until = Environment.TickCount64 + 700;

		return quiet;
	}

	/// <summary>Position among the visible siblings of the same layout (0 for anything not in a layout).</summary>
	private static int VisibleIndex(VisualElement view)
	{
		if (view.Parent is not Layout layout)
		{
			return 0;
		}

		int index = 0;

		foreach (IView sibling in layout.Children)
		{
			if (ReferenceEquals(sibling, view))
			{
				return index;
			}

			if (sibling is VisualElement { IsVisible: true })
			{
				index++;
			}
		}

		return 0;
	}

	// ---------------------------------------------------------------- building blocks

	/// <summary>
	/// Animates a view in from an offset and a scale: opacity, translation and scale together, the move on
	/// <paramref name="curve"/>. Always leaves the view where it belongs, whatever happens, and never runs twice on
	/// the same view at once.
	/// </summary>
	private static async Task PlayAsync(
		VisualElement view,
		double fromX,
		double fromY,
		double fromScale,
		int delayMs,
		uint duration,
		Easing curve)
	{
		if (!Enabled || !Playing.TryAdd(view, Marker))
		{
			return;
		}

		double rest = view.Opacity > 0 ? view.Opacity : 1;

		try
		{
			view.Opacity = 0;
			view.TranslationX = fromX;
			view.TranslationY = fromY;
			view.Scale = fromScale;

			if (delayMs > 0)
			{
				await Task.Delay(delayMs);
			}

			if (view.Handler is null)
			{
				return; // left the screen meanwhile; the finally block restores it
			}

			await Task.WhenAll(
				view.FadeToAsync(rest, (uint)(duration * 0.7), Easing.CubicOut),
				view.TranslateToAsync(0, 0, duration, curve),
				view.ScaleToAsync(1, duration, curve));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Entrance skipped: {ex.Message}");
		}
		finally
		{
			view.Opacity = rest;
			view.TranslationX = 0;
			view.TranslationY = 0;
			view.Scale = 1;
			Playing.Remove(view);
		}
	}

	/// <summary>Fades and lifts a view into place. Always leaves the view fully visible, whatever happens.</summary>
	public static Task RevealAsync(VisualElement view, int delayMs = 0, uint duration = 240, double rise = 10)
	{
		ArgumentNullException.ThrowIfNull(view);

		return PlayAsync(view, 0, rise, 0.985, delayMs, duration, Curves.Decelerate);
	}

	/// <summary>Reveals views one after another (a short wave instead of one big pop).</summary>
	public static void Cascade(IEnumerable<IView> views, int stepMs = 45, uint duration = 260, double rise = 12)
	{
		if (!Enabled)
		{
			return;
		}

		try
		{
			int index = 0;

			foreach (IView view in views)
			{
				if (view is VisualElement element && element.IsVisible)
				{
					_ = RevealAsync(element, Math.Min(index, CascadeCap) * stepMs, duration, rise);
					index++;
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Cascade skipped: {ex.Message}");
		}
	}

	/// <summary>Runs a value from one number to another on a curve, calling <paramref name="apply"/> every frame.</summary>
	public static Task TweenAsync(VisualElement owner, string name, double from, double to, uint duration, Easing curve, Action<double> apply)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(apply);

		var done = new TaskCompletionSource();

		new Animation(apply, from, to, curve)
			.Commit(owner, name, 16, duration, Easing.Linear, (_, _) => done.TrySetResult());

		return done.Task;
	}

	private static async Task GrowAsync(VisualElement view, int delayMs)
	{
		if (!Enabled || !Playing.TryAdd(view, Marker))
		{
			return;
		}

		try
		{
			view.AnchorX = 0;
			view.ScaleX = 0;

			if (delayMs > 0)
			{
				await Task.Delay(delayMs);
			}

			if (view.Handler is null)
			{
				return;
			}

			await TweenAsync(view, "Grow", 0, 1, 560, Curves.Emphasized, value => view.ScaleX = value);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Grow skipped: {ex.Message}");
		}
		finally
		{
			view.ScaleX = 1;
			Playing.Remove(view);
		}
	}

	/// <summary>A value that changed: the old one dips out, the new one rises in.</summary>
	private static async Task TickAsync(VisualElement view)
	{
		if (!Enabled || !Playing.TryAdd(view, Marker))
		{
			return;
		}

		double rest = view.Opacity > 0 ? view.Opacity : 1;

		try
		{
			await view.FadeToAsync(rest * 0.2, 70, Easing.CubicIn);

			view.TranslationY = 8;

			await Task.WhenAll(
				view.FadeToAsync(rest, 260, Easing.CubicOut),
				view.TranslateToAsync(0, 0, 320, Curves.Decelerate));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Tick skipped: {ex.Message}");
		}
		finally
		{
			view.Opacity = rest;
			view.TranslationY = 0;
			Playing.Remove(view);
		}
	}

	private static async Task BreatheAsync(VisualElement view)
	{
		try
		{
			while (view.Handler is not null)
			{
				if (!Enabled || !IsShowing(view))
				{
					await WaitUntilWorthAnimatingAsync(view);
					continue;
				}

				long started = Environment.TickCount64;

				await view.FadeToAsync(0.45, 1000, Curves.Emphasized);
				await view.FadeToAsync(1, 1000, Curves.Emphasized);

				// With system animations off (accessibility, battery saver) MAUI finishes them at once: do not spin.
				if (Environment.TickCount64 - started < 1500)
				{
					await Task.Delay(2000);
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Breathe stopped: {ex.Message}");
		}
		finally
		{
			view.Opacity = 1;
			Breathing.Remove(view);
		}
	}

	private static async Task TurnAsync(VisualElement view, double to)
	{
		try
		{
			await view.RotateToAsync(to, 300, Curves.Emphasized);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Turn skipped: {ex.Message}");
		}
		finally
		{
			view.Rotation = to;
		}
	}

	private static async Task HoverAsync(VisualElement view, bool over)
	{
		if (!Enabled || Playing.TryGetValue(view, out _))
		{
			return;
		}

		try
		{
			await view.ScaleToAsync(over ? 1.012 : 1, 160, Curves.Emphasized);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Hover skipped: {ex.Message}");
		}
	}

	/// <summary>One full turn (a refresh icon after it was pressed).</summary>
	public static async Task SpinAsync(VisualElement view)
	{
		ArgumentNullException.ThrowIfNull(view);

		if (!Enabled)
		{
			return;
		}

		try
		{
			view.Rotation = 0;

			await view.RotateToAsync(360, 640, Curves.Emphasized);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Spin skipped: {ex.Message}");
		}
		finally
		{
			view.Rotation = 0;
		}
	}

	/// <summary>Pops a view in (small overshoot): for something that just appeared and deserves a glance.</summary>
	public static Task PopAsync(VisualElement view)
	{
		ArgumentNullException.ThrowIfNull(view);

		return PlayAsync(view, 0, 0, 0.8, 0, 380, Curves.Settle);
	}

	/// <summary>Quick press feedback (press in, springy release). Fire and forget.</summary>
	public static async Task TapAsync(VisualElement view)
	{
		ArgumentNullException.ThrowIfNull(view);

		if (!Enabled)
		{
			return;
		}

		try
		{
			await view.ScaleToAsync(0.96, 70, Easing.CubicOut);
			await view.ScaleToAsync(1, 280, Curves.Settle);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Tap feedback skipped: {ex.Message}");
		}
		finally
		{
			view.Scale = 1;
		}
	}

	// ---------------------------------------------------------------- pages

	/// <summary>True between a navigation starting and the target page appearing.</summary>
	private static bool _navigating;

	private static bool _anyPageShown;

	/// <summary>Where the current navigation goes: forward (push) or back (pop).</summary>
	public static bool NavigatingBack { get; private set; }

	/// <summary>
	/// Hides the page content before its first frame, so the entrance starts from nothing
	/// instead of flashing the finished page first. Call from the page constructor, after
	/// InitializeComponent. A failsafe timer shows the page even if it never appears.
	/// </summary>
	public static void Prepare(ContentPage page)
	{
		ArgumentNullException.ThrowIfNull(page);

		if (!Enabled || page.Content is not VisualElement content)
		{
			return;
		}

		content.Opacity = 0;

		page.Dispatcher.DispatchDelayed(
			TimeSpan.FromMilliseconds(1500),
			() => content.Opacity = 1);
	}

	/// <summary>Called by the shell when a navigation starts, so entrance and exit move in the right direction.</summary>
	public static void NavigationStarting(bool back)
	{
		NavigatingBack = back;
		_navigating = true;
	}

	/// <summary>
	/// Page exit, a Material shared-axis exit: the content drifts away (to the left going forward, to the right going
	/// back), shrinks a little and fades, on the accelerate curve. Runs alongside the shell's own switch;
	/// <see cref="Restore"/> undoes it if the navigation does not happen.
	/// </summary>
	public static async Task ExitPageAsync(ContentPage page, bool back)
	{
		ArgumentNullException.ThrowIfNull(page);

		if (!Enabled || page.Content is not VisualElement content || page.Handler is null)
		{
			return;
		}

		try
		{
			await Task.WhenAll(
				content.FadeToAsync(0, 140, Curves.Accelerate),
				content.TranslateToAsync(back ? 48 : -36, 0, 150, Curves.Accelerate),
				content.ScaleToAsync(0.95, 150, Curves.Accelerate));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Page exit skipped: {ex.Message}");
		}
	}

	/// <summary>Shows a page's content again (a navigation that was cancelled or never reached the page).</summary>
	public static void Restore(Page page)
	{
		if (page is ContentPage { Content: VisualElement content })
		{
			content.Opacity = 1;
			content.TranslationX = 0;
			content.Scale = 1;
		}
	}

	/// <summary>
	/// Page entrance, a Material shared-axis entrance: a pushed page glides in from the right, a page that is returned
	/// to from the left, growing from 94 % to full size while it fades in on the decelerate curve; the first page of
	/// the app only grows and fades. The sections of the page rise in one after another the first time
	/// (<paramref name="cascade"/>), on top of what the global styles do for cards and buttons. An appearance
	/// without a navigation (the app coming back from the background) shows the page immediately.
	/// Call from OnAppearing.
	/// </summary>
	public static void EnterPage(ContentPage page, bool cascade = true)
	{
		ArgumentNullException.ThrowIfNull(page);

		Theme.Revalidate(page);
		Density.Revalidate(page);

		if (page.Content is not VisualElement content)
		{
			return;
		}

		bool first = !Entered.TryGetValue(page, out _);
		bool navigated = _navigating;
		_navigating = false;

		if (first)
		{
			Entered.Add(page, new object());
		}

		if (!Enabled || !(first || navigated))
		{
			Restore(page);
			return;
		}

		bool initial = !_anyPageShown;
		_anyPageShown = true;

		double from = initial ? 0 : NavigatingBack ? -44 : 64;

		_ = SlideInAsync(content, from, first ? 420u : 340u);

		if (cascade && first)
		{
			Cascade(SectionsOf(content), 48, 400, 24);
		}
	}

	private static async Task SlideInAsync(VisualElement content, double from, uint duration)
	{
		try
		{
			content.Opacity = 0;
			content.TranslationX = from;
			content.Scale = 0.94;

			if (content.Handler is null)
			{
				return;
			}

			await Task.WhenAll(
				content.FadeToAsync(1, (uint)(duration * 0.65), Easing.CubicOut),
				content.TranslateToAsync(0, 0, duration, Curves.Decelerate),
				content.ScaleToAsync(1, duration, Curves.Decelerate));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Page entrance skipped: {ex.Message}");
		}
		finally
		{
			content.Opacity = 1;
			content.TranslationX = 0;
			content.Scale = 1;
		}
	}

	/// <summary>The top-level sections of a scrolling page (the children of its single layout), if it has that shape.</summary>
	private static IView[] SectionsOf(VisualElement content)
	{
		Layout? layout =
			content switch
			{
				ScrollView { Content: Layout inner } => inner,
				Layout direct => direct,
				_ => null
			};

		return layout is null ? [] : [.. layout.Children];
	}
}
