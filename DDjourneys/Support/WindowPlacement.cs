using DDjourneys.Core.Diagnostics;
using System.Globalization;

namespace DDjourneys.Support;

/// <summary>
/// Desktop windows: a minimum size that fits the mostly vertical layout, and the last size and position
/// are restored on the next start. Does nothing on phones and tablets.
/// </summary>
public static class WindowPlacement
{
	public const double MinWidth = 460;
	public const double MinHeight = 460;

	private const double DefaultWidth = 500;
	private const double DefaultHeight = 800;
	private const string Key = "windowPlacement";

	public static void Attach(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		if (DeviceInfo.Idiom != DeviceIdiom.Desktop)
		{
			return;
		}

		window.MinimumWidth = MinWidth;
		window.MinimumHeight = MinHeight;

		Restore(window);

		IDispatcherTimer? timer = null;

		// Resizing fires continuously; write once it has settled.
		window.SizeChanged += (_, _) =>
		{
			timer ??= CreateTimer(window);
			timer?.Stop();
			timer?.Start();
		};

		window.Deactivated += (_, _) => Save(window);
		window.Destroying += (_, _) => Save(window);
	}

	private static IDispatcherTimer? CreateTimer(Window window)
	{
		IDispatcherTimer? timer = window.Dispatcher?.CreateTimer();

		if (timer is null)
		{
			return null;
		}

		timer.Interval = TimeSpan.FromMilliseconds(600);
		timer.IsRepeating = false;
		timer.Tick += (_, _) => Save(window);

		return timer;
	}

	/// <summary>Forgets the saved size and position (the next window opens at its default).</summary>
	internal static void Forget()
	{
		try
		{
			Preferences.Default.Remove(Key);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Window placement not forgotten: {ex.Message}");
		}
	}

	private static void Restore(Window window)
	{
		double width = DefaultWidth;
		double height = DefaultHeight;
		double? x = null;
		double? y = null;

		try
		{
			string saved = Preferences.Default.Get(Key, string.Empty);
			string[] parts = saved.Split(';');

			if (parts.Length == 4
				&& TryParse(parts[0], out double w)
				&& TryParse(parts[1], out double h)
				&& TryParse(parts[2], out double px)
				&& TryParse(parts[3], out double py))
			{
				width = w;
				height = h;
				x = px;
				y = py;
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Window placement not restored: {ex.Message}");
		}

		DisplayInfo display = DeviceDisplay.Current.MainDisplayInfo;
		double screenWidth = display.Width / display.Density;
		double screenHeight = display.Height / display.Density;

		width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, screenWidth));
		height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, screenHeight));

		window.Width = width;
		window.Height = height;

		// A position is only restored while the window would still be reachable on the main display
		// (a monitor that is gone must not strand the window off screen).
		if (x is { } left && y is { } top
			&& left > -width + 80 && left < screenWidth - 80
			&& top >= 0 && top < screenHeight - 80)
		{
			window.X = left;
			window.Y = top;
		}
	}

	private static void Save(Window window)
	{
		try
		{
			if (window.Width < MinWidth || window.Height < MinHeight
				|| double.IsNaN(window.X) || double.IsNaN(window.Y))
			{
				return;
			}

			Preferences.Default.Set(
				Key,
				string.Join(
					';',
					Format(window.Width),
					Format(window.Height),
					Format(window.X),
					Format(window.Y)));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Window placement not saved: {ex.Message}");
		}
	}

	private static string Format(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

	private static bool TryParse(string text, out double value) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
