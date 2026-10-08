using DDjourneys.Core.Theming;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ICompositionSupportsSystemBackdrop = Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop;
using MauiColor = Microsoft.Maui.Graphics.Color;
using WinColor = global::Windows.UI.Color;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// Mica, Mica Alt or desktop Acrylic with the app's own settings. The built-in <c>MicaBackdrop</c> and
/// <c>DesktopAcrylicBackdrop</c> follow the system defaults, which are tuned to be hardly noticeable and know nothing of
/// the colour set; a custom <see cref="SystemBackdrop"/> over the same controllers can set the tint (here: the palette's
/// background with a share of its accent), how much of it is laid over the wallpaper (<c>TintOpacity</c>), how much the
/// luminosity layer (<c>LuminosityOpacity</c>) and the colour shown where the system cannot draw the material
/// (<c>FallbackColor</c>: transparency off, battery saver, inactive window). The strength comes from
/// <see cref="MaterialProfile.Look"/>, so the three levels of reach also change the backdrop and not only the cards above it.
/// <para>
/// Customised properties switch the controllers' automatic light/dark handling off; the palette is the theme here, and
/// <see cref="Restyle"/> is called whenever it changes (https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop).
/// </para>
/// </summary>
internal sealed partial class PaletteBackdrop(string material) : SystemBackdrop
{
	private MicaController? _mica;
	private DesktopAcrylicController? _acrylic;

	/// <summary>One of <see cref="MaterialProfile.Mica"/>, <see cref="MaterialProfile.MicaAlt"/>, <see cref="MaterialProfile.Acrylic"/>.</summary>
	public string Material { get; } = material;

	protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
	{
		base.OnTargetConnected(connectedTarget, xamlRoot);

		SystemBackdropConfiguration configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);

		if (Material == MaterialProfile.Acrylic)
		{
			_acrylic = new DesktopAcrylicController();
			Style();
			_acrylic.SetSystemBackdropConfiguration(configuration);
			_acrylic.AddSystemBackdropTarget(connectedTarget);
		}
		else
		{
			_mica = new MicaController();
			Style();
			_mica.SetSystemBackdropConfiguration(configuration);
			_mica.AddSystemBackdropTarget(connectedTarget);
		}
	}

	protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
	{
		base.OnTargetDisconnected(disconnectedTarget);

		if (_mica is { } mica)
		{
			mica.RemoveSystemBackdropTarget(disconnectedTarget);
			mica.Dispose();
			_mica = null;
		}

		if (_acrylic is { } acrylic)
		{
			acrylic.RemoveSystemBackdropTarget(disconnectedTarget);
			acrylic.Dispose();
			_acrylic = null;
		}
	}

	/// <summary>Reads palette and reach again (a theme, colour set or level changed).</summary>
	public void Restyle()
	{
		try
		{
			Style();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Styling the window material failed", ex);
		}
	}

	private void Style()
	{
		bool dark = Theme.IsDark;
		BackdropLook look = MaterialProfile.Look(Material, Support.Material.Surfaces, dark);

		MauiColor background = Theme.BarColor;
		MauiColor accent = Theme.ColorOf("Accent", background);

		WinColor tint = ToWin(Mix(background, accent, look.AccentMix));
		WinColor fallback = ToWin(Mix(background, accent, look.AccentMix * 0.5));

		if (_mica is { } mica)
		{
			mica.Kind = Material == MaterialProfile.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base;
			mica.TintColor = tint;
			mica.TintOpacity = (float)look.TintOpacity;
			mica.LuminosityOpacity = (float)look.LuminosityOpacity;
			mica.FallbackColor = fallback;
		}

		if (_acrylic is { } acrylic)
		{
			// The thin variant at the levels that are meant to show a lot.
			acrylic.Kind = Support.Material.Surfaces == MaterialProfile.Backdrop ? DesktopAcrylicKind.Base : DesktopAcrylicKind.Thin;
			acrylic.TintColor = tint;
			acrylic.TintOpacity = (float)look.TintOpacity;
			acrylic.LuminosityOpacity = (float)look.LuminosityOpacity;
			acrylic.FallbackColor = fallback;
		}
	}

	private static MauiColor Mix(MauiColor from, MauiColor to, double amount) =>
		new(
			(float)(from.Red + ((to.Red - from.Red) * amount)),
			(float)(from.Green + ((to.Green - from.Green) * amount)),
			(float)(from.Blue + ((to.Blue - from.Blue) * amount)));

	private static WinColor ToWin(MauiColor color) =>
		WinColor.FromArgb(
			255,
			(byte)Math.Round(color.Red * 255),
			(byte)Math.Round(color.Green * 255),
			(byte)Math.Round(color.Blue * 255));
}
