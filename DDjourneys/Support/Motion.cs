namespace DDjourneys.Support;

/// <summary>
/// Central switch and helpers for animations. Every animation in the app goes through here,
/// so the "Animations" setting, cancellation and failures are handled in exactly one place.
/// </summary>
public static class Motion
{
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
}
