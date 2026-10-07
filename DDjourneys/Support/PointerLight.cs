using DDjourneys.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>
/// A soft light under the mouse pointer on desktop: the surface under the pointer brightens around it and falls off
/// towards the edges, as the "reveal" highlight of Fluent did. The light is the element's own background colour with
/// a radial gradient laid in (so it works on a card as well as on a transparent row, and under a window material, where
/// the colour is translucent). Nothing happens on touch devices, and not while animations are switched off.
/// <para>
/// <c>support:PointerLight.Enabled="True"</c> on any view. Cards get it from their style. Elements with
/// <c>support:Motion.Feedback</c> get it with their hover lift.
/// </para>
/// </summary>
public static class PointerLight
{
	/// <summary>Light on a surface (a card) is softer than on something that can be pressed.</summary>
	private const double SurfaceStrength = 0.08;

	private const double ControlStrength = 0.16;

	private const long MinimumStepMilliseconds = 24;

	private static readonly ConditionalWeakTable<View, LightState> States = [];

	public static readonly BindableProperty EnabledProperty =
		BindableProperty.CreateAttached("Enabled", typeof(bool), typeof(PointerLight), false, propertyChanged: OnEnabledChanged);

	public static bool GetEnabled(BindableObject view) => (bool)view.GetValue(EnabledProperty);

	public static void SetEnabled(BindableObject view, bool value) => view.SetValue(EnabledProperty, value);

	/// <summary>Where a pointer exists at all (Windows, Mac Catalyst); touch-only devices skip the whole thing.</summary>
	private static bool HasPointer =>
		DeviceInfo.Idiom == DeviceIdiom.Desktop;

	private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not View view
			|| newValue is not true
			|| oldValue is true
			|| !HasPointer
			|| States.TryGetValue(view, out _))
		{
			return;
		}

		double strength = view is Border ? SurfaceStrength : ControlStrength;
		var state = new LightState(strength);
		var pointer = new PointerGestureRecognizer();

		pointer.PointerEntered += (_, args) => Shine(view, state, args);
		pointer.PointerMoved += (_, args) => Shine(view, state, args);
		pointer.PointerExited += (_, _) => Dim(view, state);

		States.Add(view, state);
		view.GestureRecognizers.Add(pointer);
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
			// Whatever brush the element has on its own is left alone: only the colour-driven background is lit.
			if (!state.Lit)
			{
				if (view.Background is { } own && !Brush.IsNullOrEmpty(own))
				{
					return;
				}

				state.Lit = true;
			}

			state.LastTicks = now;

			Color baseColor = view.BackgroundColor ?? Colors.Transparent;
			Color lightColor = Theme.IsDark ? Colors.White : Theme.ColorOf("Accent", Colors.White);

			double x = Math.Clamp(position.X / view.Width, 0, 1);
			double y = Math.Clamp(position.Y / view.Height, 0, 1);

			// The centre is the base colour with the light laid over it; the rim is the base colour itself.
			float alpha = baseColor.Alpha + (float)(state.Strength * (1 - baseColor.Alpha));
			float share = alpha <= 0 ? 0 : (float)state.Strength / alpha;

			Color centre =
				new Color(
					baseColor.Red + ((lightColor.Red - baseColor.Red) * share),
					baseColor.Green + ((lightColor.Green - baseColor.Green) * share),
					baseColor.Blue + ((lightColor.Blue - baseColor.Blue) * share),
					alpha);

			// A transparent base has no colour of its own: fade the light out to itself, not through grey.
			Color rim = baseColor.Alpha <= 0 ? lightColor.WithAlpha(0) : baseColor;

			view.Background =
				new RadialGradientBrush(
					[new GradientStop(centre, 0f), new GradientStop(rim, 1f)],
					new Point(x, y),
					0.7);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Pointer light skipped: {ex.Message}");
		}
	}

	private static void Dim(View view, LightState state)
	{
		if (!state.Lit)
		{
			return;
		}

		state.Lit = false;

		try
		{
			// The colour set as BackgroundColor (a dynamic resource) paints again.
			view.ClearValue(VisualElement.BackgroundProperty);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Pointer light not cleared: {ex.Message}");
		}
	}

	private sealed class LightState(double strength)
	{
		public double Strength { get; } = strength;

		public bool Lit { get; set; }

		public long LastTicks { get; set; }
	}
}
