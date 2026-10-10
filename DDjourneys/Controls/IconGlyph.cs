namespace DDjourneys.Controls;

/// <summary>
/// The app's icon set. One line-drawn family, all built on the same 24x24 grid with a 2-unit
/// stroke and round ends, so every icon reads at the same weight whatever its size or the theme.
/// </summary>
public enum IconGlyph
{
	None = 0,
	Check,
	ChevronRight,
	ChevronDown,
	ChevronUp,
	ArrowRight,
	ArrowLeft,
	SwapVertical,
	Star,
	StarFilled,
	Refresh,
	Share,
	Info,
	Warning,
	Tune,
	Bell,
	BellFilled,
	Search,
	Close,
	Clock,
	Calendar,
	MapPin,
	Trash,
	Pause,
	Play,
	Plus,
	Minus,
	Pulse,
	Palette,
	Globe,
	Code,
	Walk,
	History,
	Locate,
	Home,
	Bookmark,
	BookmarkFilled,
	Ticket,
	Document,
	Image,
	Map,
	Route,
	Fit,
	Eye,
	EyeOff,
	Flag,
	Download,
	External,
	Accessible,
	Bus,
	Edit,
	Sun,
	Detour
}

/// <summary>One icon: path markup on the 24x24 grid, and whether it is drawn solid.</summary>
internal sealed record IconDef(
	string Data,
	bool Filled = false);

/// <summary>
/// The geometry of every icon. Hand-drawn on a 24x24 grid: strokes sit on half-unit coordinates so
/// they stay crisp, circles are built from two arcs, and dots are zero-length segments that the
/// round cap turns into a disc.
/// </summary>
internal static class IconPaths
{
	public static IconDef? For(IconGlyph glyph) =>
		Map.TryGetValue(glyph, out IconDef? def)
			? def
			: null;

	private static readonly Dictionary<IconGlyph, IconDef> Map =
		new()
		{
			[IconGlyph.Check] =
				new("M4 12.5 L9.5 18 L20 6.5"),

			[IconGlyph.ChevronRight] =
				new("M9 5 L16 12 L9 19"),

			[IconGlyph.ChevronDown] =
				new("M5 9 L12 16 L19 9"),

			[IconGlyph.ChevronUp] =
				new("M5 15 L12 8 L19 15"),

			[IconGlyph.ArrowRight] =
				new("M3 12 H20 M14 6 L20 12 L14 18"),

			[IconGlyph.ArrowLeft] =
				new("M21 12 H4 M10 6 L4 12 L10 18"),

			// Two shafts passing each other: the swap of start and destination.
			[IconGlyph.SwapVertical] =
				new("M8 21 V4 M4 8 L8 4 L12 8 M16 3 V20 M12 16 L16 20 L20 16"),

			[IconGlyph.Star] =
				new("M12 3.2 L14.9 9.3 L21.4 10.3 L16.7 15 L17.8 21.6 L12 18.5 L6.2 21.6 L7.3 15 L2.6 10.3 L9.1 9.3 Z"),

			[IconGlyph.StarFilled] =
				new("M12 3.2 L14.9 9.3 L21.4 10.3 L16.7 15 L17.8 21.6 L12 18.5 L6.2 21.6 L7.3 15 L2.6 10.3 L9.1 9.3 Z", true),

			// Almost closed circle with a corner arrowhead at the opening.
			[IconGlyph.Refresh] =
				new("M19.6 8.6 A8 8 0 1 0 20 12 M19.6 8.6 H14.6 M19.6 8.6 V3.6"),

			// Three nodes joined by two links: the platform-neutral share mark.
			[IconGlyph.Share] =
				new("M5.5 9.5 A2.5 2.5 0 1 0 5.5 14.5 A2.5 2.5 0 1 0 5.5 9.5 Z "
					+ "M18.5 3 A2.5 2.5 0 1 0 18.5 8 A2.5 2.5 0 1 0 18.5 3 Z "
					+ "M18.5 16 A2.5 2.5 0 1 0 18.5 21 A2.5 2.5 0 1 0 18.5 16 Z "
					+ "M7.8 10.6 L16.2 6.4 M7.8 13.4 L16.2 17.6"),

			[IconGlyph.Info] =
				new("M12 2.8 A9.2 9.2 0 1 0 12 21.2 A9.2 9.2 0 1 0 12 2.8 Z M12 11 V16.6 M12 7.4 V7.5"),

			[IconGlyph.Warning] =
				new("M12 3.6 L22 20.4 H2 Z M12 9.8 V14.6 M12 17.6 V17.7"),

			// Sliders: the lines break around each knob, so nothing overlaps.
			[IconGlyph.Tune] =
				new("M3 7 H12.2 M17.8 7 H21 M15 4.5 A2.5 2.5 0 1 0 15 9.5 A2.5 2.5 0 1 0 15 4.5 Z "
					+ "M3 17 H6.2 M11.8 17 H21 M9 14.5 A2.5 2.5 0 1 0 9 19.5 A2.5 2.5 0 1 0 9 14.5 Z"),

			[IconGlyph.Bell] =
				new("M12 3 A6 6 0 0 1 18 9 C18 14.6 20 16.6 20 16.6 H4 C4 16.6 6 14.6 6 9 A6 6 0 0 1 12 3 Z "
					+ "M9.8 19.6 A2.4 2.4 0 0 0 14.2 19.6"),

			// The same bell, solid: a followed journey.
			[IconGlyph.BellFilled] =
				new("M12 3 A6 6 0 0 1 18 9 C18 14.6 20 16.6 20 16.6 H4 C4 16.6 6 14.6 6 9 A6 6 0 0 1 12 3 Z "
					+ "M9.8 19.6 A2.4 2.4 0 0 0 14.2 19.6", true),

			[IconGlyph.Search] =
				new("M10.8 3.8 A7 7 0 1 0 10.8 17.8 A7 7 0 1 0 10.8 3.8 Z M15.9 15.9 L20.8 20.8"),

			[IconGlyph.Close] =
				new("M6 6 L18 18 M18 6 L6 18"),

			[IconGlyph.Clock] =
				new("M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M12 7 V12.4 L15.8 14.7"),

			[IconGlyph.Calendar] =
				new("M4.5 6 H19.5 V20.5 H4.5 Z M4.5 10.5 H19.5 M8.5 3.5 V7 M15.5 3.5 V7"),

			[IconGlyph.MapPin] =
				new("M12 21.6 C12 21.6 19 15.3 19 10.6 A7 7 0 1 0 5 10.6 C5 15.3 12 21.6 12 21.6 Z "
					+ "M9.4 10.5 A2.6 2.6 0 1 0 14.6 10.5 A2.6 2.6 0 1 0 9.4 10.5 Z"),

			[IconGlyph.Trash] =
				new("M4.5 7 H19.5 M9.5 7 V4.5 H14.5 V7 M6.6 7 L7.7 20.5 H16.3 L17.4 7 "
					+ "M10.3 10.8 V16.8 M13.7 10.8 V16.8"),

			[IconGlyph.Pause] =
				new("M9 5 V19 M15 5 V19"),

			[IconGlyph.Play] =
				new("M7.5 4.6 L19 12 L7.5 19.4 Z"),

			[IconGlyph.Plus] =
				new("M12 5 V19 M5 12 H19"),

			[IconGlyph.Minus] =
				new("M5 12 H19"),

			// Live signal: the journey in progress.
			[IconGlyph.Pulse] =
				new("M2.5 12 H7 L10 5 L14 19 L17 12 H21.5"),

			[IconGlyph.Palette] =
				new("M12 3 A9 9 0 1 0 12 21 C13.4 21 14 20.2 14 19.3 C14 17.8 12.8 17.3 13.6 16.3 "
					+ "C14.2 15.6 15.2 15.7 16.6 15.7 C19 15.7 21 14.2 21 11.6 C21 6.9 17 3 12 3 Z "
					+ "M7.6 12.4 V12.5 M8.4 8.4 V8.5 M11.4 6.6 V6.7 M15.4 7.4 V7.5"),

			[IconGlyph.Globe] =
				new("M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M3.2 12 H20.8 "
					+ "M12 3 C14.6 6.1 15.6 9 15.6 12 C15.6 15 14.6 17.9 12 21 "
					+ "C9.4 17.9 8.4 15 8.4 12 C8.4 9 9.4 6.1 12 3 Z"),

			[IconGlyph.Code] =
				new("M9 6.5 L3.5 12 L9 17.5 M15 6.5 L20.5 12 L15 17.5"),

			[IconGlyph.Walk] =
				new("M13.4 4.6 A1.8 1.8 0 1 0 13.4 8.2 A1.8 1.8 0 1 0 13.4 4.6 Z "
					+ "M11 21 L13 15.5 L9.8 12.8 L10.6 9.2 L14.2 10.6 L16.6 13.4 "
					+ "M10.6 9.2 L7.4 11 L6 14 M13 15.5 L16.2 21"),

			[IconGlyph.History] =
				new("M4.4 8.6 A8 8 0 1 0 12 4 M4.4 8.6 H9.4 M4.4 8.6 V3.6 M12 7.6 V12.4 L15.6 14.6"),

			// Crosshair: "where I am".
			[IconGlyph.Locate] =
				new("M5.5 12 A6.5 6.5 0 1 0 18.5 12 A6.5 6.5 0 1 0 5.5 12 Z "
					+ "M12 2.5 V5.5 M12 18.5 V21.5 M2.5 12 H5.5 M18.5 12 H21.5 M12 12 L12 12"),

			[IconGlyph.Home] =
				new("M3.5 11.5 L12 4 L20.5 11.5 M5.5 10 V20.5 H18.5 V10 M10 20.5 V14.5 H14 V20.5"),

			[IconGlyph.Bookmark] =
				new("M7 3.5 H17 V20.5 L12 16.5 L7 20.5 Z"),

			// A ticket with the two notches of a perforation: tickets and prices.
			[IconGlyph.Ticket] =
				new("M3.5 7.5 H20.5 V10.5 A1.5 1.5 0 0 0 20.5 13.5 V16.5 H3.5 V13.5 A1.5 1.5 0 0 0 3.5 10.5 Z M14.5 7.5 V9 M14.5 11.25 V12.75 M14.5 15 V16.5"),

			[IconGlyph.BookmarkFilled] =
				new("M7 3.5 H17 V20.5 L12 16.5 L7 20.5 Z", true),

			// A sheet with a folded corner and two text lines: an attachment or document.
			[IconGlyph.Document] =
				new("M6 3.5 H14 L18.5 8 V20.5 H6 Z M14 3.5 V8 H18.5 M9 13 H15.5 M9 16.5 H15.5"),

			[IconGlyph.Image] =
				new("M4 5 H20 V19 H4 Z M4 15.5 L9 10.5 L13 14.5 L16 11.5 L20 15.5 M14.6 8.6 V8.7"),

			// A folded map.
			[IconGlyph.Map] =
				new("M3.5 6.2 L9 4 L15 6.2 L20.5 4 V17.8 L15 20 L9 17.8 L3.5 20 Z M9 4 V17.8 M15 6.2 V20"),

			// Four corners: fit everything into view.
			[IconGlyph.Fit] =
				new("M4 9 V4 H9 M20 9 V4 H15 M4 15 V20 H9 M20 15 V20 H15"),

			[IconGlyph.Eye] =
				new("M2.5 12 C5 7.4 8.5 5.6 12 5.6 C15.5 5.6 19 7.4 21.5 12 C19 16.6 15.5 18.4 12 18.4 C8.5 18.4 5 16.6 2.5 12 Z "
					+ "M9.4 12 A2.6 2.6 0 1 0 14.6 12 A2.6 2.6 0 1 0 9.4 12 Z"),

			[IconGlyph.EyeOff] =
				new("M2.5 12 C5 7.4 8.5 5.6 12 5.6 C15.5 5.6 19 7.4 21.5 12 C19 16.6 15.5 18.4 12 18.4 C8.5 18.4 5 16.6 2.5 12 Z "
					+ "M9.4 12 A2.6 2.6 0 1 0 14.6 12 A2.6 2.6 0 1 0 9.4 12 Z M4 4 L20 20"),

			[IconGlyph.Flag] =
				new("M5.5 21 V3.5 M5.5 4.5 H18 L15 8.5 L18 12.5 H5.5"),

			[IconGlyph.Download] =
				new("M12 4 V15 M7 10.5 L12 15.5 L17 10.5 M5 20 H19"),

			[IconGlyph.External] =
				new("M13.5 4 H20 V10.5 M20 4 L11 13 M17.5 14 V20 H4 V6.5 H10"),

			// A person with open arms: accessibility.
			// A route with two endpoints: the course of a whole line.
			[IconGlyph.Route] =
				new("M5.5 18.6 C6.5 13.5 11 14.6 11 10 C11 6.4 14 5.4 18.6 5.4 M5.5 18.6 V18.7 M18.6 5.4 V5.5"),

			[IconGlyph.Bus] =
				new("M5.5 3.5 H18.5 V17 H5.5 Z M5.5 11 H18.5 M8 17 V20.5 M16 17 V20.5 M8.6 14 V14.1 M15.4 14 V14.1"),

			[IconGlyph.Edit] =
				new("M4 20 H8.2 L19.4 8.8 L15.2 4.6 L4 15.8 Z M13 6.8 L17.2 11"),

			// A circle with rays: the sun-following theme.
			[IconGlyph.Sun] =
				new("M12 7.6 A4.4 4.4 0 1 0 12 16.4 A4.4 4.4 0 1 0 12 7.6 Z M12 2.2 V4.4 M12 19.6 V21.8 M2.2 12 H4.4 M19.6 12 H21.8 M5.2 5.2 L6.7 6.7 M17.3 17.3 L18.8 18.8 M18.8 5.2 L17.3 6.7 M6.7 17.3 L5.2 18.8"),

			[IconGlyph.Accessible] =
				new("M12 3.4 A1.9 1.9 0 1 0 12 7.2 A1.9 1.9 0 1 0 12 3.4 Z M5 9.4 H19 M12 9.4 V14.4 M12 14.4 L8.6 21 M12 14.4 L15.4 21"),

			// A way around: the straight path dips aside and continues - alternatives for a broken connection.
			[IconGlyph.Detour] =
				new("M3.5 12 V12.1 M3.5 12 H8.5 C12 12 10.5 18.6 14.5 18.6 H20.5 M16 15.6 L20.5 18.6 L16 21.6")
		};
}
