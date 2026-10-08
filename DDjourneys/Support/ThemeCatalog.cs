using Microsoft.Maui.Graphics;

namespace DDjourneys.Support;

/// <summary>
/// One selectable colour theme. A palette is exactly twelve colours; Theme.cs turns it
/// into a ResourceDictionary (plus the brush twins), so adding a theme means adding one line here.
/// </summary>
public sealed record ThemeDef(
	string Id,
	string Name,
	string Description,
	string DescriptionDe,
	bool IsDark,
	bool BuiltIn,
	IReadOnlyDictionary<string, Color> Palette)
{
	public Color Bg => Palette["Bg"];
	public Color Surface => Palette["Surface"];
	public Color Raised => Palette["Raised"];
	public Color Outline => Palette["Outline"];
	public Color Ink => Palette["Ink"];
	public Color Accent => Palette["Accent"];
}

/// <summary>
/// All themes. Every palette of the added themes was checked offline:
/// text contrast (Ink 7:1, muted ink, accent and status colours 4.5:1 on Bg and Surface,
/// outlines 3:1) and, for the colour-vision-safe sets, a minimum perceptual distance
/// (CIE76 dE 18) between the on-time, delay and cancelled colours as seen with
/// protanopia, deuteranopia and tritanopia (Machado 2009 simulation).
/// Status is also always spelled out as text, so colour is never the only cue.
/// AMOLED variants are derived: Bg and Surface become pure black and the former
/// Surface becomes Raised, so contrast can only go up.
/// </summary>
public static class ThemeCatalog
{
	public const string SystemId = "system";
	public const string LightId = "light";
	public const string AmoledId = "amoled";

	private static readonly string[] Keys =
	[
		"Bg", "Surface", "Raised", "Outline", "Ink", "InkMuted",
		"Accent", "OnAccent", "AccentSoft", "OnTime", "Delay", "Cancelled"
	];

	public static IReadOnlyList<ThemeDef> All { get; } = Build();

	public static ThemeDef Find(string? id) =>
		All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
		?? All.First(t => t.Id == LightId);

	private static List<ThemeDef> Build()
	{
		List<ThemeDef> list =
		[
			New("light", "Light", "Bright surfaces", "Helle Flächen", false, true, "#F2F4F5", "#FFFFFF", "#DFE6E9", "#B4C1C7", "#0F1A1F", "#4A5960", "#0B6E8A", "#FFFFFF", "#D3E9F0", "#15803D", "#9A5B00", "#B91C1C"),
			New("dark", "Dark", "Solarized-style dark", "Dunkles Solarized", true, true, "#0C1418", "#16232A", "#23343E", "#456070", "#E9EFF1", "#A2B3BC", "#5CC0DA", "#06222B", "#1B3E4A", "#4ADE80", "#FFC94D", "#F87171"),
			New("amoled", "Dark AMOLED", "Pure black surfaces", "Reines Schwarz", true, true, "#000000", "#000000", "#16232A", "#647985", "#F2F6F8", "#AFBFC7", "#5CC0DA", "#06222B", "#12323C", "#4ADE80", "#FFC94D", "#F87171"),
		];

		// Added themes: dark ones are followed by their AMOLED variant.
		list.Add(New("nord", "Nord", "Cool arctic blues", "Kühles Arktisblau", true, false, "#242933", "#2E3440", "#3B4252", "#74839E", "#ECEFF4", "#AEBBD0", "#88C0D0", "#1B2330", "#34495A", "#A3BE8C", "#EBCB8B", "#E5848C"));
		list.Add(Amoled(list[^1]));
		list.Add(New("dracula", "Dracula", "Violet on deep indigo", "Violett auf tiefem Indigo", true, false, "#1B1D29", "#282A3A", "#3A3D54", "#7A7FA6", "#F8F8F2", "#B6B9D6", "#BD93F9", "#1B1330", "#3E3560", "#5BE584", "#FFB86C", "#FF7B9C"));
		list.Add(Amoled(list[^1]));
		list.Add(New("forest", "Forest", "Mossy greens", "Moosgrün", true, false, "#0F1A14", "#17261D", "#223A2B", "#4F7A5E", "#E6F2E8", "#A5BDAA", "#7BD389", "#08210F", "#1F4A2E", "#C6F068", "#F2C14E", "#F28B82"));
		list.Add(Amoled(list[^1]));
		list.Add(New("ember", "Ember", "Warm copper glow", "Warmes Kupferglühen", true, false, "#1A1210", "#271A16", "#3A2822", "#8E6252", "#F6EAE4", "#C4A99E", "#FF9F5A", "#2B1204", "#4D2E1E", "#7FD6A4", "#FFD166", "#FF7A8A"));
		list.Add(Amoled(list[^1]));
		list.Add(New("midnight", "Midnight", "Deep indigo blue", "Tiefes Indigoblau", true, false, "#0B1026", "#131A38", "#1F2850", "#5668B0", "#E8ECFF", "#A9B4E0", "#7FA6FF", "#071033", "#1E2C66", "#6EE7B7", "#FFD166", "#FF8A9B"));
		list.Add(Amoled(list[^1]));
		list.Add(New("graphite", "Graphite", "Neutral greys, no tint", "Neutrale Grautöne", true, false, "#121212", "#1C1C1C", "#2A2A2A", "#6A6A6A", "#EDEDED", "#A8A8A8", "#D9D9D9", "#151515", "#383838", "#8FD19E", "#E8C468", "#F08A8A"));
		list.Add(Amoled(list[^1]));
		list.Add(New("okabe-dark", "Colour-safe Dark", "Blue/orange, for red-green colour blindness", "Blau/Orange, für Rot-Grün-Schwäche", true, false, "#0E141A", "#17212B", "#243342", "#5E7A94", "#EEF3F7", "#A9BAC8", "#56B4E9", "#06202E", "#17405A", "#56B4E9", "#F0E442", "#D9609A"));
		list.Add(Amoled(list[^1]));
		list.Add(New("tritan-dark", "Tritan-safe Dark", "Red/teal, for blue-yellow colour blindness", "Rot/Türkis, für Blau-Gelb-Schwäche", true, false, "#101416", "#1A2124", "#273136", "#5F7A80", "#F0F4F5", "#ADBDC2", "#3DD6D0", "#04201F", "#173F40", "#3DD6D0", "#FFA24D", "#FF6B8B"));
		list.Add(Amoled(list[^1]));
		list.Add(New("contrast-dark", "High Contrast Dark", "Maximum contrast, pure black", "Maximaler Kontrast, reines Schwarz", true, false, "#000000", "#000000", "#1C1C1C", "#FFFFFF", "#FFFFFF", "#E6E6E6", "#FFD400", "#000000", "#332B00", "#5CE1FF", "#FFB347", "#FF8080"));
		list.Add(New("rose", "Rosé", "Soft pink and cream", "Zartes Rosa auf Creme", false, false, "#FBF1F3", "#FFFFFF", "#F3DDE2", "#A86F7E", "#2A1419", "#6B4A53", "#B0295A", "#FFFFFF", "#F9D6E0", "#1F7A45", "#8F5200", "#B3261E"));
		list.Add(New("solarized-light", "Solarized Light", "Warm paper with teal ink", "Warmes Papier, petrolfarbene Tinte", false, false, "#FDF6E3", "#FFFBF0", "#EEE8D5", "#8A836D", "#1F3A44", "#52686F", "#1A6FB0", "#FFFFFF", "#DCEBF5", "#5C7A00", "#946000", "#C0392B"));
		list.Add(New("sand", "Sand", "Warm neutrals with forest green", "Warme Neutraltöne mit Waldgrün", false, false, "#F4EFE6", "#FFFFFF", "#E6DDCB", "#8F8060", "#241F14", "#5C523C", "#2F6B4F", "#FFFFFF", "#D6E8DD", "#1B6B3A", "#8A5A00", "#A82A2A"));
		list.Add(New("okabe-light", "Colour-safe Light", "Blue/orange, for red-green colour blindness", "Blau/Orange, für Rot-Grün-Schwäche", false, false, "#F3F6F9", "#FFFFFF", "#E1E8EE", "#6A7F92", "#0E1A24", "#43566A", "#0072B2", "#FFFFFF", "#D5E8F5", "#0072B2", "#8A5A00", "#B3125F"));
		list.Add(New("tritan-light", "Tritan-safe Light", "Red/teal, for blue-yellow colour blindness", "Rot/Türkis, für Blau-Gelb-Schwäche", false, false, "#F4F7F7", "#FFFFFF", "#E0E9EA", "#6A8486", "#101A1C", "#445B5E", "#00757A", "#FFFFFF", "#D2ECEC", "#00757A", "#9A4A00", "#B0123F"));
		list.Add(New("contrast-light", "High Contrast Light", "Maximum contrast, black on white", "Maximaler Kontrast, Schwarz auf Weiß", false, false, "#FFFFFF", "#FFFFFF", "#E8E8E8", "#000000", "#000000", "#262626", "#0033CC", "#FFFFFF", "#DDE6FF", "#0050B3", "#6B4E00", "#B00057"));
		list.Add(New("mint", "Mint", "Fresh green on cool white", "Frisches Grün auf kühlem Weiß", false, false, "#EEF8F3", "#FFFFFF", "#D7EDE2", "#648F7B", "#0E2219", "#3F5F50", "#0A7A52", "#FFFFFF", "#CDEBDD", "#1B6E2E", "#8A5200", "#B3261E"));
		list.Add(New("mint-solarized", "Mint Solarized", "Fresh green on pistachio paper", "Frisches Grün auf Pistazienpapier", false, false, "#E8F0D4", "#F6FAE8", "#D6E2B8", "#7A8A52", "#1E2A12", "#475A33", "#2E6B1F", "#FFFFFF", "#D3E4B8", "#1B6E2E", "#8A5200", "#B3261E"));
		list.Add(New("lavender", "Lavender", "Soft violet", "Sanftes Violett", false, false, "#F4F0FC", "#FFFFFF", "#E4DBF6", "#8872B8", "#1C1233", "#54476F", "#6A3AD0", "#FFFFFF", "#E3D8FA", "#1F7A45", "#8C5100", "#B3203C"));
		list.Add(New("lavender-solarized", "Lavender Solarized", "Indigo on grey-lilac paper", "Indigo auf graulila Papier", false, false, "#DFDCEE", "#EEECF7", "#CDC9E4", "#6F6A9E", "#17152E", "#45426B", "#3B36B5", "#FFFFFF", "#CFCBF0", "#176B34", "#7F4A00", "#A8203B"));
		list.Add(New("citrus", "Citrus", "Sunny amber", "Sonniges Bernstein", false, false, "#FFF8E6", "#FFFFFF", "#F6E7BC", "#A98A2E", "#2B2100", "#64561F", "#B35400", "#FFFFFF", "#FFE2B8", "#2F6F1E", "#7A5A00", "#B3261E"));
		list.Add(New("citrus-solarized", "Citrus Solarized", "Golden ochre on lemon paper", "Goldocker auf Zitronenpapier", false, false, "#F6EFC2", "#FCF9E0", "#E9E0A0", "#8F8030", "#2A2000", "#5A4B10", "#8A5E00", "#FFFFFF", "#EEDFA0", "#2F6F1E", "#7A4A00", "#A8261E"));
		list.Add(New("sky", "Sky", "Clear blue", "Klares Blau", false, false, "#EDF6FE", "#FFFFFF", "#D5E8F9", "#6589B0", "#0B1D33", "#3E5A78", "#0B62C4", "#FFFFFF", "#CFE3F8", "#137A4B", "#8F5600", "#B3261E"));
		list.Add(New("sky-solarized", "Sky Solarized", "Petrol blue on blue-grey paper", "Petrolblau auf blaugrauem Papier", false, false, "#DCE9E7", "#EEF6F4", "#C3D8D4", "#4F7C77", "#0C2528", "#355659", "#00667E", "#FFFFFF", "#BCE0E4", "#146B3D", "#7F4E00", "#A8201A"));
		list.Add(New("coral", "Coral", "Warm coral red", "Warmes Korallrot", false, false, "#FFF1EE", "#FFFFFF", "#FADAD3", "#B5766A", "#2D130E", "#6E463E", "#C23B22", "#FFFFFF", "#FFD9D1", "#1D7A4A", "#8A5200", "#8F1D2C"));
		list.Add(New("coral-solarized", "Coral Solarized", "Burnt orange on sand paper", "Gebranntes Orange auf Sandpapier", false, false, "#F2DFCC", "#FBF0E2", "#E6CAAD", "#96704A", "#2E1A0A", "#5F4126", "#9A4608", "#FFFFFF", "#F1D2B4", "#1A6B3F", "#7F4A00", "#8F1D2C"));
		list.Add(New("orchid", "Orchid", "Bold fuchsia", "Kräftiges Fuchsia", false, false, "#FBEFFB", "#FFFFFF", "#F1D8F2", "#A56AA8", "#2A1030", "#6B4470", "#A21CAF", "#FFFFFF", "#F5D0FA", "#1D7A4A", "#8A5200", "#B3261E"));
		list.Add(New("orchid-solarized", "Orchid Solarized", "Magenta on rosy paper", "Magenta auf rosigem Papier", false, false, "#F1DFE6", "#FAEFF3", "#E5C6D2", "#94596F", "#2E1220", "#603B4E", "#A3145A", "#FFFFFF", "#F0C9DA", "#1A6B3F", "#7F4A00", "#A8201A"));
		list.Add(New("cyber", "Cyber", "Acid lime on deep navy", "Säuregrün auf tiefem Marine", true, false, "#070B12", "#0E1622", "#182436", "#5A7FA3", "#EAF6FF", "#9FB5CC", "#B6FF3B", "#0A1200", "#2A3D0A", "#3DFFA2", "#FFC23D", "#FF5D8F"));
		list.Add(Amoled(list[^1]));
		list.Add(New("synthwave", "Synthwave", "Hot pink on night violet", "Pink auf Nachtviolett", true, false, "#140A24", "#1E1035", "#2E1A52", "#8A5BD0", "#FDF0FF", "#C9B3E6", "#FF4FD8", "#2A0020", "#5A1A5C", "#46F0C8", "#FFB347", "#FF6B6B"));
		list.Add(Amoled(list[^1]));

		return list;
	}

	private static ThemeDef Amoled(ThemeDef dark)
	{
		var palette = new Dictionary<string, Color>(dark.Palette)
		{
			["Bg"] = Colors.Black,
			["Surface"] = Colors.Black,
			["Raised"] = dark.Palette["Surface"]
		};

		return dark with
		{
			Id = dark.Id + "-amoled",
			Name = dark.Name + " AMOLED",
			Palette = palette
		};
	}

	private static ThemeDef New(
		string id, string name, string en, string de, bool dark, bool builtIn,
		params string[] colours)
	{
		var palette = new Dictionary<string, Color>(Keys.Length);

		for (int i = 0; i < Keys.Length; i++)
		{
			palette[Keys[i]] = Color.FromArgb(colours[i]);
		}

		return new ThemeDef(id, name, en, de, dark, builtIn, palette);
	}
}