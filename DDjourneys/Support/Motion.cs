using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>
/// Central switch and helpers for animations. Every animation in the app goes through here,
/// so the "Animations" setting, cancellation and failures are handled in exactly one place.
/// Rules: never block interaction, never leave a view invisible, never throw.
/// </summary>
public static class Motion
{
	private const int CascadeCap = 8;

	private static readonly ConditionalWeakTable<Page, object> Entered = new();

	public static bool Enabled { get; private set; } = true;

	public static void Bind(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		Enabled = settings.Animations;
		settings.Changed += (_, name) =>
		{
			if (name == nameof(AppSettings.Animations))
			{
				Enabled = settings.Animations;
			}
		};
	}

	/// <summary>Fades and lifts a view into place. Always leaves the view fully visible, whatever happens.</summary>
	public static async Task RevealAsync(VisualElement view, int delayMs = 0, uint duration = 240, double rise = 10)
	{
		ArgumentNullException.ThrowIfNull(view);

		if (!Enabled)
		{
			return;
		}

		try
		{
			view.Opacity = 0;
			view.TranslationY = rise;

			if (delayMs > 0)
			{
				await Task.Delay(delayMs);
			}

			if (view.Handler is null)
			{
				return; // left the screen meanwhile; the finally block restores it
			}

			await Task.WhenAll(
				view.FadeToAsync(1, duration, Easing.CubicOut),
				view.TranslateToAsync(0, 0, duration, Easing.CubicOut));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Reveal skipped: {ex.Message}");
		}
		finally
		{
			view.Opacity = 1;
			view.TranslationY = 0;
		}
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
			System.Diagnostics.Debug.WriteLine($"Cascade skipped: {ex.Message}");
		}
	}

	/// <summary>
	/// Hides the page content before its first frame, so the entrance fade starts from nothing
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

	/// <summary>
	/// Page entrance: the whole page fades in once per page instance (no per-element movement, so
	/// nothing jumps). Coming back to a page (Pop) shows it immediately.
	/// Call from OnAppearing.
	/// </summary>
	public static void EnterPage(ContentPage page)
	{
		ArgumentNullException.ThrowIfNull(page);

		Theme.Revalidate(page);

		if (page.Content is not VisualElement content)
		{
			return;
		}

		if (!Enabled || Entered.TryGetValue(page, out _))
		{
			content.Opacity = 1;
			return;
		}

		Entered.Add(page, new object());

		_ = RevealAsync(content, 0, 180, 0);
	}

	/// <summary>Quick press feedback (scale down, spring back). Fire and forget.</summary>
	public static async Task TapAsync(VisualElement view)
	{
		ArgumentNullException.ThrowIfNull(view);

		if (!Enabled)
		{
			return;
		}

		try
		{
			await view.ScaleToAsync(0.97, 70, Easing.CubicOut);
			await view.ScaleToAsync(1, 120, Easing.CubicIn);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Tap feedback skipped: {ex.Message}");
		}
		finally
		{
			view.Scale = 1;
		}
	}
}
