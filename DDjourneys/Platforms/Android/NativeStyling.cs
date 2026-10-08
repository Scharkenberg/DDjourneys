using Android.Content.Res;
using Google.Android.Material.TextField;
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
			(handler, _) => ClearFrame(handler.PlatformView));

		DatePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearUnderline(handler.PlatformView));

		TimePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearUnderline(handler.PlatformView));
	}

	/// <summary>
	/// An entry has no frame of its own here: the card around it draws the only contour. Besides the underline this clears
	/// the outlined box that Material 3 puts around an entry (a <see cref="TextInputLayout"/>), which may attach after the mapping.
	/// </summary>
	private static void ClearFrame(global::Android.Views.View? view)
	{
		if (view is null)
		{
			return;
		}

		ClearUnderline(view);
		view.Background = null;
		StripBox(view);

		view.Post(() => StripBox(view));
	}

	private static void StripBox(global::Android.Views.View view)
	{
		for (global::Android.Views.IViewParent? parent = view.Parent; parent is not null; parent = parent.Parent)
		{
			if (parent is TextInputLayout layout)
			{
				layout.BoxBackgroundMode = TextInputLayout.BoxBackgroundNone;
				layout.BoxStrokeWidth = 0;
				layout.BoxStrokeWidthFocused = 0;
				layout.SetBoxStrokeColorStateList(ColorStateList.ValueOf(global::Android.Graphics.Color.Transparent));
				layout.Background = null;

				return;
			}

			if (parent is not global::Android.Views.View)
			{
				return;
			}
		}
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
