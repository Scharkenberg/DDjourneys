namespace DDjourneys.Support;

/// <summary>
/// State-dependent theme colours and fonts that keep following theme changes.
/// <para>
/// Why this exists: MAUI keeps ONE DynamicResource registration per property and element. When a VisualState or
/// Trigger setter that uses a DynamicResource is unapplied, <c>RemoveDynamicResource</c> drops that single slot
/// regardless of specificity, so the style's own registration is gone as well and the property keeps its last
/// resolved colour forever (e.g. a button that was disabled once, or a Leave/Arrive toggle that was switched).
/// </para>
/// <para>
/// This helper instead picks the resource key from the element's state (enabled, <see cref="IsOnProperty"/>) and
/// registers it as a LOCAL dynamic resource. Local registrations are never unapplied by styles, states or triggers,
/// so every theme change reaches them. Rule: never combine these properties with VisualState/Trigger setters
/// for the same visual property.
/// </para>
/// </summary>
public static class Themed
{
	public static readonly BindableProperty BackgroundProperty = Key("Background");
	public static readonly BindableProperty TextProperty = Key("Text");
	public static readonly BindableProperty StrokeProperty = Key("Stroke");
	public static readonly BindableProperty FontProperty = Key("Font");

	public static readonly BindableProperty OnBackgroundProperty = Key("OnBackground");
	public static readonly BindableProperty OnTextProperty = Key("OnText");
	public static readonly BindableProperty OnStrokeProperty = Key("OnStroke");
	public static readonly BindableProperty OnFontProperty = Key("OnFont");

	public static readonly BindableProperty DisabledBackgroundProperty = Key("DisabledBackground");
	public static readonly BindableProperty DisabledTextProperty = Key("DisabledText");

	/// <summary>Switches every channel to its "On…" key (where one is given).</summary>
	public static readonly BindableProperty IsOnProperty =
		BindableProperty.CreateAttached(
			"IsOn",
			typeof(bool),
			typeof(Themed),
			false,
			propertyChanged: (bindable, _, _) => Update(bindable));

	private static readonly BindableProperty HookedProperty =
		BindableProperty.CreateAttached("Hooked", typeof(bool), typeof(Themed), false);

	public static string? GetBackground(BindableObject b) => (string?)b.GetValue(BackgroundProperty);
	public static void SetBackground(BindableObject b, string? v) => b.SetValue(BackgroundProperty, v);
	public static string? GetText(BindableObject b) => (string?)b.GetValue(TextProperty);
	public static void SetText(BindableObject b, string? v) => b.SetValue(TextProperty, v);
	public static string? GetStroke(BindableObject b) => (string?)b.GetValue(StrokeProperty);
	public static void SetStroke(BindableObject b, string? v) => b.SetValue(StrokeProperty, v);
	public static string? GetFont(BindableObject b) => (string?)b.GetValue(FontProperty);
	public static void SetFont(BindableObject b, string? v) => b.SetValue(FontProperty, v);

	public static string? GetOnBackground(BindableObject b) => (string?)b.GetValue(OnBackgroundProperty);
	public static void SetOnBackground(BindableObject b, string? v) => b.SetValue(OnBackgroundProperty, v);
	public static string? GetOnText(BindableObject b) => (string?)b.GetValue(OnTextProperty);
	public static void SetOnText(BindableObject b, string? v) => b.SetValue(OnTextProperty, v);
	public static string? GetOnStroke(BindableObject b) => (string?)b.GetValue(OnStrokeProperty);
	public static void SetOnStroke(BindableObject b, string? v) => b.SetValue(OnStrokeProperty, v);
	public static string? GetOnFont(BindableObject b) => (string?)b.GetValue(OnFontProperty);
	public static void SetOnFont(BindableObject b, string? v) => b.SetValue(OnFontProperty, v);

	public static string? GetDisabledBackground(BindableObject b) => (string?)b.GetValue(DisabledBackgroundProperty);
	public static void SetDisabledBackground(BindableObject b, string? v) => b.SetValue(DisabledBackgroundProperty, v);
	public static string? GetDisabledText(BindableObject b) => (string?)b.GetValue(DisabledTextProperty);
	public static void SetDisabledText(BindableObject b, string? v) => b.SetValue(DisabledTextProperty, v);

	public static bool GetIsOn(BindableObject b) => (bool)b.GetValue(IsOnProperty);
	public static void SetIsOn(BindableObject b, bool v) => b.SetValue(IsOnProperty, v);

	private static BindableProperty Key(string name) =>
		BindableProperty.CreateAttached(
			name,
			typeof(string),
			typeof(Themed),
			null,
			propertyChanged: (bindable, _, _) => Update(bindable));

	private static void Update(BindableObject bindable)
	{
		if (bindable is not VisualElement element)
		{
			return;
		}

		Hook(element);

		bool enabled = element.IsEnabled;
		bool on = GetIsOn(element);

		Apply(element, BackgroundTarget(), Pick(element, enabled, on, BackgroundProperty, OnBackgroundProperty, DisabledBackgroundProperty));
		Apply(element, TextTarget(element), Pick(element, enabled, on, TextProperty, OnTextProperty, DisabledTextProperty));
		Apply(element, StrokeTarget(element), Pick(element, enabled, on, StrokeProperty, OnStrokeProperty, null));
		Apply(element, FontTarget(element), Pick(element, enabled, on, FontProperty, OnFontProperty, null));
	}

	private static string? Pick(
		BindableObject element,
		bool enabled,
		bool on,
		BindableProperty normal,
		BindableProperty onKey,
		BindableProperty? disabled)
	{
		if (!enabled && disabled is not null && element.GetValue(disabled) is string { Length: > 0 } off)
		{
			return off;
		}

		if (on && element.GetValue(onKey) is string { Length: > 0 } active)
		{
			return active;
		}

		return element.GetValue(normal) as string is { Length: > 0 } key ? key : null;
	}

	private static void Apply(Element element, BindableProperty? target, string? key)
	{
		// No key for this channel: leave the property to its style (never remove a registration here,
		// that would hit the same single-slot problem this class works around).
		if (target is null || key is null)
		{
			return;
		}

		element.SetDynamicResource(target, key);
	}

	private static void Hook(VisualElement element)
	{
		if ((bool)element.GetValue(HookedProperty))
		{
			return;
		}

		element.SetValue(HookedProperty, true);
		element.PropertyChanged += OnElementPropertyChanged;
	}

	private static void OnElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (sender is BindableObject bindable && e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
		{
			Update(bindable);
		}
	}

	private static BindableProperty BackgroundTarget() =>
		VisualElement.BackgroundColorProperty;

	private static BindableProperty? TextTarget(VisualElement element) =>
		element switch
		{
			Button => Button.TextColorProperty,
			Label => Label.TextColorProperty,
			Entry => Entry.TextColorProperty,
			_ => null
		};

	private static BindableProperty? StrokeTarget(VisualElement element) =>
		element switch
		{
			Border => Border.StrokeProperty,
			Microsoft.Maui.Controls.Shapes.Shape => Microsoft.Maui.Controls.Shapes.Shape.StrokeProperty,
			_ => null
		};

	private static BindableProperty? FontTarget(VisualElement element) =>
		element switch
		{
			Button => Button.FontFamilyProperty,
			Label => Label.FontFamilyProperty,
			Entry => Entry.FontFamilyProperty,
			_ => null
		};
}
