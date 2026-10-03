using System.Runtime.CompilerServices;
using DDjourneys.Core.Theming;

namespace DDjourneys.Support;

/// <summary>
/// The UI density in effect (compact, normal, larger touch targets) and the shared spacing resources.
/// <para>
/// Same approach as <see cref="Theme"/>: the shared resources (SpaceS, PadCard, PadPage, …) are rewritten in
/// place in the application dictionary, so every DynamicResource consumer follows at once; values written
/// straight into XAML go through <see cref="Dense"/>, which re-applies them from <see cref="Changed"/>.
/// </para>
/// </summary>
public static class Density
{
	private static readonly ConditionalWeakTable<Page, StampBox> Stamps = new();
	private static readonly Dictionary<string, object> Current = new();

	private static Application? _app;
	private static AppSettings? _settings;
	private static int _version;

	public static event EventHandler? Changed;

	public static DensityProfile Profile { get; private set; } = DensityProfile.Normal;

	public static void Initialize(Application app, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		_app = app;
		_settings = settings;

		Profile = DensityProfile.Find(settings.UiDensity);
		WriteTokens();
	}

	/// <summary>Switches the density and updates everything that is already on screen.</summary>
	public static void Set(string id)
	{
		DensityProfile profile = DensityProfile.Find(id);

		if (profile == Profile)
		{
			return;
		}

		Profile = profile;
		_settings?.UiDensity = profile.Id;

		try
		{
			WriteTokens();
			_version++;
			Dense.Refresh();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Density apply failed: {ex}");
		}

		Changed?.Invoke(null, EventArgs.Empty);
	}

	/// <summary>
	/// Makes sure a page that is about to be shown carries the current spacing resources, even if it was not
	/// reached when the density changed (same reason and trick as <see cref="Theme.Revalidate"/>).
	/// </summary>
	public static void Revalidate(Page page)
	{
		ArgumentNullException.ThrowIfNull(page);

		if (Stamps.TryGetValue(page, out StampBox? box) && box.Version == _version)
		{
			return;
		}

		Stamps.Remove(page);
		Stamps.Add(page, new StampBox(_version));

		if (_version == 0 || Current.Count == 0)
		{
			return;
		}

		try
		{
			ResourceDictionary resources = page.Resources;

			foreach ((string key, object value) in Current)
			{
				resources[key] = value;
			}

			foreach (string key in Current.Keys)
			{
				resources.Remove(key);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Density revalidate failed: {ex.Message}");
		}
	}

	private static void WriteTokens()
	{
		if (_app is null)
		{
			return;
		}

		DensityProfile p = Profile;
		ResourceDictionary resources = _app.Resources;

		Write(resources, "SpaceXS", p.SpaceXS);
		Write(resources, "SpaceS", p.SpaceS);
		Write(resources, "SpaceM", p.SpaceM);
		Write(resources, "SpaceL", p.SpaceL);
		Write(resources, "SpaceXL", p.SpaceXL);
		Write(resources, "PadCard", new Thickness(p.CardPadding));
		Write(resources, "PadPage", new Thickness(p.PagePaddingHorizontal, p.PagePaddingVertical));
		Write(resources, "PadField", new Thickness(p.FieldPaddingHorizontal, p.FieldPaddingVertical));
		Write(resources, "SectionGap", new Thickness(0, p.SectionGap, 0, 0));
	}

	private static void Write(ResourceDictionary resources, string key, object value)
	{
		Current[key] = value;
		resources[key] = value;
	}

	private sealed class StampBox(int version)
	{
		public int Version { get; } = version;
	}
}
