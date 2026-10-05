using DDjourneys.Core.Theming;

namespace DDjourneys.Support;

/// <summary>
/// One selectable colour set, independent of light/dark mode. Sets that exist as a hand-tuned
/// light and/or dark palette in <see cref="ThemeCatalog"/> use those; a missing variant (and the
/// two system-accent sets) is derived with <see cref="PaletteBuilder"/>, which guarantees legible contrast.
/// </summary>
public sealed record ColorOption(
	string Id,
	string Name,
	string NameDe,
	string Description,
	string DescriptionDe,
	ThemeDef? Light,
	ThemeDef? Dark,
	Color Seed,
	bool Solarized,
	bool FollowsSystemAccent);

public static class ColorCatalog
{
	public const string SystemId = "system";
	public const string SystemSolarizedId = "system-solarized";
	public const string DefaultId = "default";

	private static readonly Color DefaultSeed = Color.FromArgb("#0B6E8A");

	private static readonly Dictionary<string, string> GermanNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["Default"] = "Standard",
		["Forest"] = "Wald",
		["Ember"] = "Glut",
		["Colour-safe"] = "Farbsicher",
		["Tritan-safe"] = "Tritan-sicher",
		["High Contrast"] = "Hoher Kontrast",
		["Mint"] = "Minze",
		["Lavender"] = "Lavendel",
		["Citrus"] = "Zitrus",
		["Sky"] = "Himmel",
		["Coral"] = "Koralle",
		["Orchid"] = "Orchidee"
	};

	public static IReadOnlyList<ColorOption> All { get; } = Build();

	public static ColorOption Find(string? id) =>
		All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))
		?? All.First(o => o.Id == DefaultId);

	/// <summary>The twelve palette colours for a colour set in the given mode.</summary>
	public static IReadOnlyDictionary<string, Color> Resolve(string? id, bool dark, bool pureBlack)
	{
		ColorOption option = Find(id);

		if (option.FollowsSystemAccent)
		{
			Rgb seed = ToRgb(SystemAccent.TryGet(dark) ?? DefaultSeed);

			return ToMap(PaletteBuilder.Build(seed, dark, option.Solarized, pureBlack));
		}

		ThemeDef? def = dark ? option.Dark : option.Light;

		if (def is null)
		{
			return ToMap(PaletteBuilder.Build(ToRgb(option.Seed), dark, option.Solarized, pureBlack, tint: true));
		}

		var palette = new Dictionary<string, Color>(def.Palette);

		if (dark && pureBlack && palette["Bg"] != Colors.Black)
		{
			palette["Raised"] = palette["Surface"];
			palette["Bg"] = Colors.Black;
			palette["Surface"] = Colors.Black;
		}

		return palette;
	}

	/// <summary>Maps the pre-split single theme id to mode, colour set and pure-black choice.</summary>
	public static (string Mode, string Color, bool PureBlack) FromLegacy(string? legacyId)
	{
		string id = (legacyId ?? string.Empty).Trim().ToLowerInvariant();

		switch (id)
		{
			case "":
			case "system":
				return (Theme.ModeSystem, DefaultId, true);
			case "light":
				return (Theme.ModeLight, DefaultId, false);
			case "dark":
				return (Theme.ModeDark, DefaultId, false);
			case "amoled":
				return (Theme.ModeDark, DefaultId, true);
		}

		bool black = id.EndsWith("-amoled", StringComparison.Ordinal);
		string baseId = black ? id[..^"-amoled".Length] : id;
		ThemeDef? def = ThemeCatalog.All.FirstOrDefault(t => t.Id == baseId);
		bool dark = black || (def?.IsDark ?? false);

		return (dark ? Theme.ModeDark : Theme.ModeLight, FamilyOf(baseId), black);
	}

	private static List<ColorOption> Build()
	{
		var list = new List<ColorOption>
		{
			new(SystemId, "System accent colour", "Systemakzentfarbe", string.Empty, string.Empty, null, null, DefaultSeed, false, true),
			new(SystemSolarizedId, "System accent colour, solarized", "Systemakzentfarbe, Solarized", string.Empty, string.Empty, null, null, DefaultSeed, true, true)
		};

		var order = new List<string>();
		var light = new Dictionary<string, ThemeDef>();
		var dark = new Dictionary<string, ThemeDef>();

		foreach (ThemeDef t in ThemeCatalog.All)
		{
			if (t.Id == ThemeCatalog.AmoledId || t.Id.EndsWith("-amoled", StringComparison.Ordinal))
			{
				continue;
			}

			string family = FamilyOf(t.Id);

			if (!order.Contains(family))
			{
				order.Add(family);
			}

			(t.IsDark ? dark : light)[family] = t;
		}

		foreach (string family in order)
		{
			light.TryGetValue(family, out ThemeDef? l);
			dark.TryGetValue(family, out ThemeDef? d);
			ThemeDef first = (l ?? d)!;

			string name = family == DefaultId ? "Default" : DisplayName(first.Name);
			bool solarized = family == "solarized" || family.EndsWith("-solarized", StringComparison.Ordinal);

			(string en, string de) = family switch
			{
				DefaultId => ("Teal accent", "Petrolfarbener Akzent"),
				"contrast" => ("Maximum contrast", "Maximaler Kontrast"),
				_ => (first.Description, first.DescriptionDe)
			};

			list.Add(new ColorOption(
				family, name, GermanName(name), en, de, l, d,
				(l ?? d)!.Accent, solarized, false));
		}

		return list;
	}

	/// <summary>Colour-set id of a <see cref="ThemeCatalog"/> theme id.</summary>
	private static string FamilyOf(string themeId) =>
		themeId switch
		{
			"light" or "dark" or "amoled" => DefaultId,
			"solarized-light" => "solarized",
			_ when themeId.EndsWith("-dark", StringComparison.Ordinal) => themeId[..^"-dark".Length],
			_ when themeId.EndsWith("-light", StringComparison.Ordinal) => themeId[..^"-light".Length],
			_ => themeId
		};

	private static string DisplayName(string themeName)
	{
		foreach (string suffix in (string[])[" Dark", " Light"])
		{
			if (themeName.EndsWith(suffix, StringComparison.Ordinal))
			{
				return themeName[..^suffix.Length];
			}
		}

		return themeName;
	}

	private static string GermanName(string name)
	{
		const string solarized = " Solarized";

		if (name.EndsWith(solarized, StringComparison.Ordinal))
		{
			string head = name[..^solarized.Length];

			return GermanNames.TryGetValue(head, out string? de) ? de + solarized : name;
		}

		return GermanNames.TryGetValue(name, out string? mapped) ? mapped : name;
	}

	private static Rgb ToRgb(Color c) =>
		new(
			(byte)Math.Round(c.Red * 255),
			(byte)Math.Round(c.Green * 255),
			(byte)Math.Round(c.Blue * 255));

	private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

	private static Dictionary<string, Color> ToMap(PaletteColors p) =>
		new()
		{
			["Bg"] = ToColor(p.Bg),
			["Surface"] = ToColor(p.Surface),
			["Raised"] = ToColor(p.Raised),
			["Outline"] = ToColor(p.Outline),
			["Ink"] = ToColor(p.Ink),
			["InkMuted"] = ToColor(p.InkMuted),
			["Accent"] = ToColor(p.Accent),
			["OnAccent"] = ToColor(p.OnAccent),
			["AccentSoft"] = ToColor(p.AccentSoft),
			["OnTime"] = ToColor(p.OnTime),
			["Delay"] = ToColor(p.Delay),
			["Cancelled"] = ToColor(p.Cancelled)
		};
}
