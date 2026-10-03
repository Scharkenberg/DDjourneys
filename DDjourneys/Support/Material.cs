using DDjourneys.Core.Theming;

namespace DDjourneys.Support;

/// <summary>
/// The window material in effect (Windows 11 Mica, Mica Alt, desktop Acrylic) and how it tints the palette.
/// Everything platform specific lives in <c>WindowsMaterial</c>; this class only decides, so the theme code
/// stays the same on every platform: where no material exists, <see cref="Active"/> is false and the
/// palette stays opaque.
/// </summary>
public static class Material
{
	private static AppSettings? _settings;

	/// <summary>The material the user chose (<see cref="MaterialProfile.None"/> and the like).</summary>
	public static string Id { get; private set; } = MaterialProfile.None;

	/// <summary>How far the material reaches into the surfaces of the app.</summary>
	public static string Surfaces { get; private set; } = MaterialProfile.Layered;

	/// <summary>True while a material is actually shown: chosen, supported here, and not overridden by pure black.</summary>
	public static bool Active { get; private set; }

	/// <summary>The material that is actually on screen.</summary>
	public static string Effective => Active ? Id : MaterialProfile.None;

	/// <summary>The platform can show at least one material.</summary>
	public static bool Available => MaterialProfile.Materials.Any(id => id != MaterialProfile.None && IsSupported(id));

	/// <summary>Raised when the material, its reach or its activity changed.</summary>
	public static event EventHandler? Changed;

	public static void Initialize(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		Id = MaterialProfile.NormalizeMaterial(settings.WindowMaterial);
		Surfaces = MaterialProfile.NormalizeCoverage(settings.MaterialSurfaces);
	}

	public static bool IsSupported(string id) =>
		id == MaterialProfile.None
#if WINDOWS
		|| DDjourneys.Platforms.Windows.WindowsMaterial.IsSupported(id)
#endif
		;

	/// <returns>True when the choice changed.</returns>
	public static bool SetMaterial(string id)
	{
		string value = MaterialProfile.NormalizeMaterial(id);

		if (value == Id)
		{
			return false;
		}

		Id = value;
		_settings?.WindowMaterial = value;

		return true;
	}

	/// <returns>True when the choice changed.</returns>
	public static bool SetSurfaces(string id)
	{
		string value = MaterialProfile.NormalizeCoverage(id);

		if (value == Surfaces)
		{
			return false;
		}

		Surfaces = value;
		_settings?.MaterialSurfaces = value;

		return true;
	}

	/// <summary>
	/// Called by the theme with the palette about to be applied. Pure black surfaces and a material exclude each
	/// other (a material cannot be black); the explicit choice for black wins.
	/// </summary>
	internal static void Resolve(bool dark, bool pureBlack)
	{
		bool active = Id != MaterialProfile.None && IsSupported(Id) && !(dark && pureBlack);

		Active = active;
	}

	/// <summary>The palette colour with the opacity its role gets under the current material.</summary>
	internal static Color Tint(string role, Color color, bool dark)
	{
		if (!Active || role is not ("Bg" or "Surface" or "Raised"))
		{
			return color;
		}

		byte alpha = MaterialProfile.Alpha(Id, Surfaces, role, dark);

		return alpha == 255 ? color : color.WithAlpha(alpha / 255f);
	}

	internal static void Raise() =>
		Changed?.Invoke(null, EventArgs.Empty);
}
