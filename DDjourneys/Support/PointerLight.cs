using DDjourneys.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>
/// A soft light under the mouse pointer on desktop. The light is an overlay that lies ABOVE the content of the element
/// (an input-transparent box with a radial gradient), so it shows on cards whose rows and fields cover their own
/// background, which a background gradient never did. The overlay is added once the element is loaded: to a
/// <see cref="Grid"/> as one more child spanning all cells, to a <see cref="Border"/> by putting its content and the overlay
/// into one grid. Other layouts are left alone. Nothing happens on touch devices or while animations are off.
/// <para><c>support:PointerLight.Enabled="True"</c>; Cards get it from their style, <c>Motion.Feedback</c> elements with it.</para>
/// </summary>
public static class PointerLight
{
	/// <summary>Opacity of the light at its centre: a card is lit softly, something pressable more.</summary>
	private const double SurfaceGlow = 0.10;

	private const double ControlGlow = 0.18;

	/// <summary>Radius of the light relative to the element (the gradient is relative to its bounds).</summary>
	private const double Radius = 0.75;

	private const long MinimumStepMilliseconds = 16;

	private static readonly ConditionalWeakTable<View, LightState> States = [];

	public static readonly BindableProperty EnabledProperty =
		BindableProperty.CreateAttached("Enabled", typeof(bool), typeof(PointerLight), false, propertyChanged: OnEnabledChanged);

	public static bool GetEnabled(BindableObject view) => (bool)view.GetValue(EnabledProperty);

	public static void SetEnabled(BindableObject view, bool value) => view.SetValue(EnabledProperty, value);

	private static bool HasPointer => DeviceInfo.Idiom == DeviceIdiom.Desktop;

	private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not View view
			|| newValue is not true
			|| oldValue is true
			|| !HasPointer
			|| view is not (Border or Grid)
			|| States.TryGetValue(view, out _))
		{
			return;
		}

		var state = new LightState(view is Border ? SurfaceGlow : ControlGlow);

		States.Add(view, state);

		var pointer = new PointerGestureRecognizer();

		pointer.PointerEntered += (_, args) => Shine(view, state, args);
		pointer.PointerMoved += (_, args) => Shine(view, state, args);
		pointer.PointerExited += (_, _) => Dim(state);

		view.GestureRecognizers.Add(pointer);
	}

	private static void Install(View view, LightState state)
	{
		if (state.Overlay is not null)
		{
			return;
		}

		var overlay =
			new BoxView
			{
				InputTransparent = true,
				IsVisible = false,
				Color = Colors.Transparent,
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		switch (view)
		{
			case Grid grid:
				Grid.SetRowSpan(overlay, Math.Max(1, grid.RowDefinitions.Count));
				Grid.SetColumnSpan(overlay, Math.Max(1, grid.ColumnDefinitions.Count));
				grid.Children.Add(overlay);
				break;

			case Border border:
				{
					var host = new Grid();
					View? content = border.Content;

					border.Content = null;

					if (content is not null)
					{
						host.Children.Add(content);
					}

					host.Children.Add(overlay);
					border.Content = host;
				}

				break;
		}

		state.Overlay = overlay;
	}

	private static void Shine(View view, LightState state, PointerEventArgs args)
	{
		if (!Motion.Enabled)
		{
			return;
		}

		long now = Environment.TickCount64;

		if (now - state.LastTicks < MinimumStepMilliseconds
			|| view.Width <= 0
			|| view.Height <= 0
			|| args.GetPosition(view) is not { } position)
		{
			return;
		}

		try
		{
			state.LastTicks = now;

			Install(view, state);

			if (state.Overlay is not { } overlay)
			{
				return;
			}

			Color glow = Theme.IsDark ? Colors.White : Theme.ColorOf("Accent", Colors.White);

			double x = Math.Clamp(position.X / view.Width, 0, 1);
			double y = Math.Clamp(position.Y / view.Height, 0, 1);

			overlay.Background =
				new RadialGradientBrush(
					[
						new GradientStop(glow.WithAlpha((float)state.Glow), 0f),
						new GradientStop(glow.WithAlpha((float)(state.Glow * 0.35)), 0.5f),
						new GradientStop(glow.WithAlpha(0f), 1f)
					],
					new Point(x, y),
					Radius);

			overlay.IsVisible = true;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Pointer light skipped: {ex.Message}");
		}
	}

	private static void Dim(LightState state)
	{
		if (state.Overlay is { } overlay)
		{
			overlay.IsVisible = false;
		}
	}

	private sealed class LightState(double glow)
	{
		public double Glow { get; } = glow;

		public BoxView? Overlay { get; set; }

		public long LastTicks { get; set; }
	}
}
