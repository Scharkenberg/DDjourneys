using DDjourneys.Core.Theming;

namespace DDjourneys.Support;

/// <summary>
/// Writes the line colours of one colour-vision set into the app's Mode* resources (Tokens.xaml carries the
/// standard values; this overwrites them at start and whenever the set changes). Everything that reads a mode
/// colour through <see cref="ModeColors"/> - the chips, the timeline rails, the map lines, the widget chips -
/// follows at once where it reads them live; cards and timelines already on screen rebuild on
/// <see cref="Changed"/> (the view models subscribe). Widget images and the web views pick the colours up on
/// their next build.
/// </summary>
public static class ModeColorSet
{
	private static ColourVisionSet _current = ColourVisionSet.Standard;

	/// <summary>Raised after the Mode* resources were rewritten (the set itself or its colours changed).</summary>
	public static event EventHandler? Changed;

	public static ColourVisionSet Current => _current;

	public static void Initialize(Application app, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		_current = ModeColorSets.Of(settings.ModeColourSet);

		Write(app);
	}

	/// <summary>Switches the set, stores the choice and rewrites the resources; a no-op when nothing changes.</summary>
	public static void Set(Application app, AppSettings settings, string id)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		ColourVisionSet next = ModeColorSets.Of(id);

		if (next == _current)
		{
			return;
		}

		_current = next;
		settings.ModeColourSet = ModeColorSets.IdOf(next);

		Write(app);

		Changed?.Invoke(null, EventArgs.Empty);
	}

	private static void Write(Application app)
	{
		IReadOnlyList<Rgb> colors = ModeColorSets.ColorsOf(_current);

		for (int i = 0; i < colors.Count && i < ModeColorSets.Modes.Count; i++)
		{
			app.Resources["Mode" + ModeColorSets.Modes[i]] = Color.FromRgb(colors[i].R, colors[i].G, colors[i].B);
		}
	}
}
