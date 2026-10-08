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

		EditorHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearFrame(handler.PlatformView));

		DatePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearFrame(handler.PlatformView));

		TimePickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearFrame(handler.PlatformView));

		PickerHandler.Mapper.AppendToMapping(
			"DDjourneysNoUnderline",
			(handler, _) => ClearFrame(handler.PlatformView));
	}

	/// <summary>
	/// Every layout pass of the activity's window looks for text layouts that have a frame again and clears them. The
	/// handler mappers above can be bypassed (Material 3 builds its fields in its own way and applies its outline after
	/// them), a pass over the window cannot: nothing the app shows keeps a native frame, whoever created it.
	/// </summary>
	public static void Watch(global::Android.App.Activity activity)
	{
		global::Android.Views.View? decor = activity.Window?.DecorView;

		if (decor?.ViewTreeObserver is not { IsAlive: true } observer)
		{
			return;
		}

		_sweeper ??= new FrameSweeper();
		observer.RemoveOnGlobalLayoutListener(_sweeper);
		observer.AddOnGlobalLayoutListener(_sweeper);
		_sweeper.Root = decor;

		FrameSweeper.Sweep(decor);
	}

	private static FrameSweeper? _sweeper;

	private sealed class FrameSweeper : Java.Lang.Object, global::Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
	{
		public global::Android.Views.View? Root { get; set; }

		public void OnGlobalLayout()
		{
			if (Root is { } root)
			{
				Sweep(root);
			}
		}

		public static void Sweep(global::Android.Views.View view)
		{
			if (view is TextInputLayout layout)
			{
				Strip(layout);
			}

			if (view is global::Android.Views.ViewGroup group)
			{
				for (int i = 0; i < group.ChildCount; i++)
				{
					if (group.GetChildAt(i) is { } child)
					{
						Sweep(child);
					}
				}
			}
		}
	}

	/// <summary>
	/// An entry has no frame of its own here: the card around it draws the only contour. Besides the underline this clears
	/// the outlined box that Material 3 puts around an entry (a <see cref="TextInputLayout"/>). With Material 3 the native
	/// view can be the layout itself, sit inside it, or hold it, and the layout may attach after the mapping ran, so every
	/// case is covered, again when the view attaches to the window and once more after the first layout.
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

		if (!Hooked.TryGetValue(view, out _))
		{
			Hooked.Add(view, new object());
			view.ViewAttachedToWindow += (_, _) => StripBox(view);
			view.LayoutChange += (_, _) => StripBox(view);
		}
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<global::Android.Views.View, object> Hooked = [];

	private static void StripBox(global::Android.Views.View view)
	{
		if (view is TextInputLayout self)
		{
			Strip(self);
		}

		for (global::Android.Views.IViewParent? parent = view.Parent; parent is not null; parent = parent.Parent)
		{
			if (parent is TextInputLayout layout)
			{
				Strip(layout);
			}

			if (parent is not global::Android.Views.View)
			{
				break;
			}
		}

		if (view is global::Android.Views.ViewGroup group)
		{
			for (int i = 0; i < group.ChildCount; i++)
			{
				if (group.GetChildAt(i) is TextInputLayout inner)
				{
					Strip(inner);
				}
			}
		}
	}

	private static void Strip(TextInputLayout layout)
	{
		// The text layout draws the box by giving its EditText the box drawable as background (TextInputLayout
		// assigns it when the box mode is set, and switching the mode to none does not take it away again), so the
		// frame survives unless the EditText itself is cleared as well.
		global::Android.Widget.EditText? edit = layout.EditText;

		// Only when something is left to remove: LayoutChange fires often, and changing a view there asks for a new layout.
		if (layout.BoxBackgroundMode == TextInputLayout.BoxBackgroundNone
			&& layout.BoxStrokeWidth == 0
			&& layout.BoxStrokeWidthFocused == 0
			&& layout.Background is null
			&& edit?.Background is null)
		{
			return;
		}

		global::DDjourneys.Core.Diagnostics.DiagnosticLog.Write($"[UI] entry frame removed (box mode {layout.BoxBackgroundMode}, edit text background {edit?.Background?.GetType().Name ?? "none"})");

		layout.BoxBackgroundMode = TextInputLayout.BoxBackgroundNone;
		layout.BoxStrokeWidth = 0;
		layout.BoxStrokeWidthFocused = 0;
		layout.SetBoxStrokeColorStateList(ColorStateList.ValueOf(global::Android.Graphics.Color.Transparent));
		layout.Background = null;

		if (edit is not null)
		{
			edit.Background = null;
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
