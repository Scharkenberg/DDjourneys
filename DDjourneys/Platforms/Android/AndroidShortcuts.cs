using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// The quick actions of the app icon's long-press menu ("take me home", "departures from here"). They are
/// dynamic shortcuts, so their labels follow the language chosen in the app; publishing again is cheap.
/// </summary>
internal static class AndroidShortcuts
{
	private const string SignatureKey = "shortcuts.signature";

	public static void Publish(Activity activity)
	{
		try
		{
			if (!OperatingSystem.IsAndroidVersionAtLeast(25)
				|| activity.GetSystemService(Java.Lang.Class.FromType(typeof(ShortcutManager))) is not ShortcutManager manager)
			{
				return;
			}

			ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

			// Publishing is a call into the system every time: only when the labels or the app changed.
			string signature = $"{AppInfo.Current.BuildString}|{strings.ShortcutHome}|{strings.ShortcutDepartures}";

			if (Preferences.Default.Get(SignatureKey, string.Empty) == signature)
			{
				return;
			}

			manager.SetDynamicShortcuts(
			[
				Build(activity, AppShortcut.Home, strings.ShortcutHome, "ic_shortcut_home", 0),
				Build(activity, AppShortcut.DeparturesHere, strings.ShortcutDepartures, "ic_shortcut_departures", 1)
			]);

			Preferences.Default.Set(SignatureKey, signature);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Publishing the quick actions failed: {ex.Message}");
		}
	}

	[System.Runtime.Versioning.SupportedOSPlatform("android25.0")]
	private static ShortcutInfo Build(Activity activity, AppShortcut shortcut, string label, string icon, int rank)
	{
		var intent =
			new Intent(activity, typeof(MainActivity));

		intent.SetAction(AppShortcuts.AndroidAction);
		intent.PutExtra(AppShortcuts.ExtraKey, AppShortcuts.IdOf(shortcut));

		int resource =
			activity.Resources?.GetIdentifier(icon, "drawable", activity.PackageName) ?? 0;

		ShortcutInfo.Builder builder =
			new ShortcutInfo.Builder(activity, "dd." + AppShortcuts.IdOf(shortcut))
				.SetShortLabel(label)!
				.SetLongLabel(label)!
				.SetRank(rank)!
				.SetIntent(intent)!;

		if (resource != 0)
		{
			builder.SetIcon(Icon.CreateWithResource(activity, resource));
		}

		return builder.Build()!;
	}
}
