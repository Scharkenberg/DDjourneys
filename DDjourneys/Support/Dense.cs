namespace DDjourneys.Support;

/// <summary>
/// Sizes that follow the UI density. Write the NORMAL value in XAML
/// (<c>support:Dense.MinHeight="52"</c>, <c>support:Dense.Padding="12,8"</c>); the element then gets the value
/// of the density in effect, now and whenever the density changes.
/// <para>
/// Not for geometry that has to stay exact (timeline lines and dots, icon sizes, swatches): those keep plain
/// Width/HeightRequest and Padding.
/// </para>
/// </summary>
public static class Dense
{
	public static readonly BindableProperty MinHeightProperty =
		BindableProperty.CreateAttached("MinHeight", typeof(double), typeof(Dense), double.NaN, propertyChanged: OnChanged);

	public static readonly BindableProperty MinWidthProperty =
		BindableProperty.CreateAttached("MinWidth", typeof(double), typeof(Dense), double.NaN, propertyChanged: OnChanged);

	public static readonly BindableProperty PaddingProperty =
		BindableProperty.CreateAttached("Padding", typeof(Thickness), typeof(Dense), new Thickness(double.NaN), propertyChanged: OnChanged);

	private static readonly BindableProperty RegisteredProperty =
		BindableProperty.CreateAttached("Registered", typeof(bool), typeof(Dense), false);

	private static readonly List<WeakReference<BindableObject>> Live = [];
	private static int _pruneAt = 256;

	public static double GetMinHeight(BindableObject b) => (double)b.GetValue(MinHeightProperty);
	public static void SetMinHeight(BindableObject b, double v) => b.SetValue(MinHeightProperty, v);
	public static double GetMinWidth(BindableObject b) => (double)b.GetValue(MinWidthProperty);
	public static void SetMinWidth(BindableObject b, double v) => b.SetValue(MinWidthProperty, v);
	public static Thickness GetPadding(BindableObject b) => (Thickness)b.GetValue(PaddingProperty);
	public static void SetPadding(BindableObject b, Thickness v) => b.SetValue(PaddingProperty, v);

	/// <summary>Re-applies every live element. Called by <see cref="Density"/> after a change.</summary>
	internal static void Refresh()
	{
		for (int i = Live.Count - 1; i >= 0; i--)
		{
			if (Live[i].TryGetTarget(out BindableObject? target))
			{
				Apply(target);
			}
			else
			{
				Live.RemoveAt(i);
			}
		}
	}

	private static void OnChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (!(bool)bindable.GetValue(RegisteredProperty))
		{
			bindable.SetValue(RegisteredProperty, true);
			Live.Add(new WeakReference<BindableObject>(bindable));

			// List items come and go; drop the dead entries now and then so the list stays small.
			if (Live.Count > _pruneAt)
			{
				Live.RemoveAll(r => !r.TryGetTarget(out _));
				_pruneAt = Math.Max(256, Live.Count * 2);
			}
		}

		Apply(bindable);
	}

	private static void Apply(BindableObject bindable)
	{
		var profile = Density.Profile;

		// Rows and touch targets grow with the OS text size, so bigger text never gets clipped or cramped.
		double textScale = Math.Max(1, SystemAccessibility.TextScale);

		if (bindable is VisualElement element)
		{
			double height = GetMinHeight(bindable);

			if (!double.IsNaN(height))
			{
				element.MinimumHeightRequest = profile.Hit(height) * textScale;
			}

			double width = GetMinWidth(bindable);

			if (!double.IsNaN(width))
			{
				element.MinimumWidthRequest = profile.Hit(width) * textScale;
			}
		}

		Thickness authored = GetPadding(bindable);

		if (double.IsNaN(authored.Left))
		{
			return;
		}

		var scaled = new Thickness(
			profile.PaddingHorizontal(authored.Left),
			profile.PaddingVertical(authored.Top),
			profile.PaddingHorizontal(authored.Right),
			profile.PaddingVertical(authored.Bottom));

		switch (bindable)
		{
			case Layout layout:
				layout.Padding = scaled;
				break;

			case Border border:
				border.Padding = scaled;
				break;

			case Button button:
				button.Padding = scaled;
				break;

			case ContentView view:
				view.Padding = scaled;
				break;
		}
	}
}
