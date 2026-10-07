using DDjourneys.Core.Theming;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Application = Microsoft.UI.Xaml.Application;
using ResourceDictionary = Microsoft.UI.Xaml.ResourceDictionary;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Thickness = Microsoft.UI.Xaml.Thickness;
using Window = Microsoft.UI.Xaml.Window;
using WinColor = global::Windows.UI.Color;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// Windows 11 materials for the app window, following the Fluent layering guidance
/// (https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops):
/// <list type="bullet">
/// <item>The window gets ONE backdrop (<see cref="Window.SystemBackdrop"/>): Mica, Mica Alt or desktop Acrylic. It sits
/// behind everything and only shows where every layer above it is transparent, which is what the theme does
/// with <see cref="Material"/> (page background transparent, surfaces thin layers of the palette).</item>
/// <item>The title bar belongs to the same surface: caption buttons are transparent and wear the palette's ink, so
/// the material continues under them, in light, dark and every colour set, independent of the OS theme.</item>
/// <item>WinUI's own layers that would hide the backdrop (page and navigation view backgrounds) are transparent.</item>
/// <item>Transient surfaces (menus, drop-downs, date pickers) keep the OS's own acrylic.</item>
/// </list>
/// The OS falls back to a solid colour by itself (transparency off, battery saver, remote sessions, inactive window).
/// </summary>
internal static class WindowsMaterial
{
	private static readonly List<WeakReference<Window>> Tracked = [];
	private static readonly Lock Gate = new();

	private static int _wired;

	public static bool IsSupported(string id)
	{
		try
		{
			return id switch
			{
				MaterialProfile.Mica or MaterialProfile.MicaAlt => MicaController.IsSupported(),
				MaterialProfile.Acrylic => DesktopAcrylicController.IsSupported(),
				_ => true
			};
		}
		catch (Exception ex)
		{
			WindowsTrace.Write($"Material support check failed for {id}", ex);

			return false;
		}
	}

	/// <summary>
	/// Once, early: WinUI's default page and navigation backgrounds are opaque theme brushes; they would hide the
	/// backdrop behind them. MAUI pages paint their own (palette) background, so nothing is lost without a material.
	/// </summary>
	public static void Prepare()
	{
		try
		{
			ResourceDictionary resources = Application.Current.Resources;
			var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

			foreach (string key in new[]
			{
				"ApplicationPageBackgroundThemeBrush",
				"NavigationViewContentBackground",
				"NavigationViewContentGridBorderBrush",
				"NavigationViewDefaultPaneBackground",
				"NavigationViewExpandedPaneBackground",
				"NavigationViewTopPaneBackground"
			})
			{
				resources[key] = clear;
			}

			resources["NavigationViewContentGridBorderThickness"] = new Thickness(0);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Preparing the window material failed", ex);
		}
	}

	/// <summary>Tracks a MAUI window and gives it the material (and again whenever theme or material change).</summary>
	public static void Attach(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		lock (Gate)
		{
			if (Tracked.Any(item => item.TryGetTarget(out Window? known) && ReferenceEquals(known, window)))
			{
				return;
			}

			Tracked.Add(new WeakReference<Window>(window));
		}

		if (Interlocked.Exchange(ref _wired, 1) == 0)
		{
			Theme.Changed += (_, _) => Refresh();
			Support.Material.Changed += (_, _) => Refresh();
		}

		window.Closed += (_, _) =>
		{
			lock (Gate)
			{
				Tracked.RemoveAll(item => !item.TryGetTarget(out Window? known) || ReferenceEquals(known, window));
			}
		};

		// The content may not exist yet when MAUI attaches the handler.
		window.Activated += OnFirstActivated;

		Apply(window);
	}

	/// <summary>Applies material, title bar and theme to every window (any thread).</summary>
	public static void Refresh()
	{
		List<Window> windows = [];

		lock (Gate)
		{
			foreach (WeakReference<Window> item in Tracked)
			{
				if (item.TryGetTarget(out Window? window))
				{
					windows.Add(window);
				}
			}
		}

		foreach (Window window in windows)
		{
			Window target = window;

			if (target.DispatcherQueue is { } queue && !queue.HasThreadAccess)
			{
				queue.TryEnqueue(() => Apply(target));
			}
			else
			{
				Apply(target);
			}
		}
	}

	private static void OnFirstActivated(object sender, WindowActivatedEventArgs args)
	{
		if (sender is not Window window)
		{
			return;
		}

		window.Activated -= OnFirstActivated;
		Apply(window);
	}

	private static void Apply(Window window)
	{
		try
		{
			string material = Support.Material.Effective;

			// Same kind again: the running backdrop is styled again with the new palette and reach (no flicker, no new
			// controller on every change); another kind replaces it.
			if (material == MaterialProfile.None)
			{
				if (window.SystemBackdrop is not null)
				{
					window.SystemBackdrop = null;
				}
			}
			else if (window.SystemBackdrop is PaletteBackdrop running
				&& string.Equals(running.Material, material, StringComparison.Ordinal))
			{
				running.Restyle();
			}
			else
			{
				window.SystemBackdrop = new PaletteBackdrop(material);
			}

			// The backdrop reads the light/dark theme of the content: the app's mode, not the OS's.
			if (window.Content is FrameworkElement root)
			{
				ElementTheme wanted = Theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;

				if (root.RequestedTheme != wanted)
				{
					root.RequestedTheme = wanted;
				}
			}

			StyleTitleBar(window, material != MaterialProfile.None);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Applying the window material failed", ex);
		}
	}

	private static void StyleTitleBar(Window window, bool materialShown)
	{
		if (!AppWindowTitleBar.IsCustomizationSupported())
		{
			return;
		}

		AppWindowTitleBar bar = window.AppWindow.TitleBar;

		Microsoft.Maui.Graphics.Color ink = Theme.ColorOf("Ink", Microsoft.Maui.Graphics.Colors.Black);
		Microsoft.Maui.Graphics.Color muted = Theme.ColorOf("InkMuted", ink);
		Microsoft.Maui.Graphics.Color background = Theme.BarColor;
		Microsoft.Maui.Graphics.Color accent = Theme.ColorOf("Accent", background);

		// With a material the title bar shows it; without, it wears the palette (not the OS theme).
		WinColor? fill = materialShown ? null : ToWin(background);

		bar.BackgroundColor = fill;
		bar.InactiveBackgroundColor = fill;

		WinColor clear = WinColor.FromArgb(0, 0, 0, 0);

		bar.ButtonBackgroundColor = clear;
		bar.ButtonInactiveBackgroundColor = clear;
		bar.ButtonForegroundColor = ToWin(ink);
		bar.ButtonInactiveForegroundColor = ToWin(muted);
		bar.ButtonHoverBackgroundColor = ToWin(materialShown ? accent : background, materialShown ? 0.22f : 0.16f);
		bar.ButtonHoverForegroundColor = ToWin(ink);
		bar.ButtonPressedBackgroundColor = ToWin(materialShown ? accent : background, materialShown ? 0.34f : 0.24f);
		bar.ButtonPressedForegroundColor = ToWin(ink);

		if (!materialShown)
		{
			bar.ForegroundColor = ToWin(ink);
			bar.InactiveForegroundColor = ToWin(muted);
		}
		else
		{
			bar.ForegroundColor = null;
			bar.InactiveForegroundColor = null;
		}
	}

	private static WinColor ToWin(Microsoft.Maui.Graphics.Color color, float? alpha = null) =>
		WinColor.FromArgb(
			(byte)Math.Round((alpha ?? color.Alpha) * 255),
			(byte)Math.Round(color.Red * 255),
			(byte)Math.Round(color.Green * 255),
			(byte)Math.Round(color.Blue * 255));
}
