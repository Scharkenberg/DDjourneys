using DDjourneys.Core.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;

namespace DDjourneys.Support;

/// <summary>
/// Soft pointer glow for desktop surfaces.
/// <para>
/// This stays in MAUI so it compiles on the current app stack. The glow is softer, less saturated,
/// and it applies to any enabled view, not only clickable ones.
/// </para>
/// <para>
/// Apply with <c>support:PointerLight.Enabled="True"</c> on any view.
/// </para>
/// </summary>
public static class PointerLight
{
	private const double SurfaceStrength = 0.045;
	private const double ControlStrength = 0.075;
	private const long MinimumStepMilliseconds = 16;

	private static readonly ConditionalWeakTable<View, LightState> States = new();

	public static readonly BindableProperty EnabledProperty =
		BindableProperty.CreateAttached(
			"Enabled",
			typeof(bool),
			typeof(PointerLight),
			false,
			propertyChanged: OnEnabledChanged);

	public static bool GetEnabled(BindableObject view) => (bool)view.GetValue(EnabledProperty);

	public static void SetEnabled(BindableObject view, bool value) => view.SetValue(EnabledProperty, value);

	private static bool HasPointer => DeviceInfo.Idiom == DeviceIdiom.Desktop;

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

		LightState state = new(view is Border ? SurfaceStrength : ControlStrength);
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
			state.LastTicks = now;

			if (!state.Lit)
			{
				state.OriginalBackground ??= view.Background;
				state.Lit = true;
			}

			Color baseColor = view.BackgroundColor;
			Color glowColor = Theme.IsDark ? Colors.White : Theme.ColorOf("Accent", Colors.White);

			double x = Math.Clamp(position.X / view.Width, 0, 1);
			double y = Math.Clamp(position.Y / view.Height, 0, 1);

			float strength = (float)Math.Clamp(state.Strength, 0.03, 0.10);

			Color centre =
				Blend(baseColor, glowColor, strength * 0.65f)
					.WithAlpha(MathF.Min(1f, MathF.Max(baseColor.Alpha, 0.14f + strength)));

			Color mid =
				Blend(baseColor, glowColor, strength * 0.35f)
					.WithAlpha(MathF.Max(baseColor.Alpha, 0.06f + (strength * 0.30f)));

			Color rim = baseColor.Alpha <= 0 ? baseColor.WithAlpha(0) : baseColor;

			view.Background =
				new RadialGradientBrush(
					[
						new GradientStop(centre, 0f),
						new GradientStop(mid, 0.55f),
						new GradientStop(rim, 1f)
					],
					new Point(x, y),
					0.95);
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
			view.Background = state.OriginalBackground;
			state.OriginalBackground = null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Pointer light not cleared: {ex.Message}");
		}
	}

	private static Color Blend(Color from, Color to, float t)
	{
		t = Math.Clamp(t, 0f, 1f);

		return new Color(
			from.Red + ((to.Red - from.Red) * t),
			from.Green + ((to.Green - from.Green) * t),
			from.Blue + ((to.Blue - from.Blue) * t),
			from.Alpha + ((to.Alpha - from.Alpha) * t));
	}

	private sealed class LightState(double strength)
	{
		public double Strength { get; } = strength;
		public Brush? OriginalBackground { get; set; }
		public bool Lit { get; set; }
		public long LastTicks { get; set; }
	}
}