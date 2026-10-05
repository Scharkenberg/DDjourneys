using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// The WinUI text box draws its own frame (border, hover fill, accent underline on focus). The entries of the
/// app sit inside a card that already draws the contour, so the native one would double it. It is switched off
/// per instance through the control's own theme resources, for every visual state.
/// </summary>
internal static class NativeStyling
{
	private static readonly string[] BrushKeys =
	[
		"TextControlBackground",
		"TextControlBackgroundPointerOver",
		"TextControlBackgroundFocused",
		"TextControlBackgroundDisabled",
		"TextControlBorderBrush",
		"TextControlBorderBrushPointerOver",
		"TextControlBorderBrushFocused",
		"TextControlBorderBrushDisabled"
	];

	private static readonly string[] ThicknessKeys =
	[
		"TextControlBorderThemeThickness",
		"TextControlBorderThemeThicknessFocused"
	];

	public static void Install() =>
		EntryHandler.Mapper.AppendToMapping(
			"DDjourneysNoNativeFrame",
			(handler, _) => RemoveFrame(handler.PlatformView));

	private static void RemoveFrame(TextBox? box)
	{
		if (box is null)
		{
			return;
		}

		foreach (string key in BrushKeys)
		{
			box.Resources[key] = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
		}

		foreach (string key in ThicknessKeys)
		{
			box.Resources[key] = new Thickness(0);
		}

		box.BorderThickness = new Thickness(0);
		box.Background = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);

		// The template reads the resources when it is applied; re-apply if it already was.
		box.ApplyTemplate();
	}
}
