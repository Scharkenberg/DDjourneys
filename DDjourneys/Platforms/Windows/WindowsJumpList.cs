using DDjourneys.Localization;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support;
using JumpList = global::Windows.UI.StartScreen.JumpList;
using JumpListItem = global::Windows.UI.StartScreen.JumpListItem;
using JumpListSystemGroupKind = global::Windows.UI.StartScreen.JumpListSystemGroupKind;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// The jump list (right click on the taskbar icon): "take me home" and "departures from here". Each entry starts
/// the app with <c>shortcut=...</c>; a running instance receives it through the redirected activation
/// (see <see cref="WindowsContractActivation"/>).
/// </summary>
internal static class WindowsJumpList
{
	public static async Task UpdateAsync()
	{
		try
		{
			if (!JumpList.IsSupported())
			{
				return;
			}

			JumpList list = await JumpList.LoadCurrentAsync();

			list.SystemGroupKind = JumpListSystemGroupKind.None;
			list.Items.Clear();

			ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

			list.Items.Add(Entry(AppShortcut.Home, strings.ShortcutHome));
			list.Items.Add(Entry(AppShortcut.DeparturesHere, strings.ShortcutDepartures));

			await list.SaveAsync();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Updating the jump list failed", ex);
		}
	}

	private static JumpListItem Entry(AppShortcut shortcut, string label)
	{
		JumpListItem item =
			JumpListItem.CreateWithArguments(AppShortcuts.ArgumentsFor(shortcut), label);

		// An empty group name puts the entry under "Tasks".
		item.GroupName = string.Empty;

		return item;
	}
}
