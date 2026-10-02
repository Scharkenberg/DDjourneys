using Android.Content.Res;
using Microsoft.Maui.Handlers;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Removes the native Android decorations that would otherwise show up in the framework's default
/// colour: the underline of entries and pickers. The text fields keep the app's own ink colours.
/// </summary>
internal static class NativeStyling
{
	public static void Install()
	{
		EntryHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearUnderline(handler.PlatformView));

		DatePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearUnderline(handler.PlatformView));

		TimePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearUnderline(handler.PlatformView));
	}

	private static void ClearUnderline(global::Android.Views.View? view)
	{
		if (view is null)
		{
			return;
		}

		view.BackgroundTintList =
			ColorStateList.ValueOf(
				global::Android.Graphics.Color.Transparent);
	}
}
