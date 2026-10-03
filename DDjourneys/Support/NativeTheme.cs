namespace DDjourneys.Support;

/// <summary>
/// Keeps the colours of native-drawn controls (switch, slider, pickers, spinners) on the current theme.
/// <para>
/// These controls did not reliably pick up a new theme through DynamicResource in styles or visual states
/// (a switch track, a slider thumb stayed on the old colour until its page was rebuilt). Instead they are
/// registered here and given their colours directly, now and on every <see cref="Theme.Changed"/>.
/// Used from the implicit styles: <c>support:NativeTheme.Follow="True"</c>.
/// </para>
/// </summary>
public static class NativeTheme
{
	public static readonly BindableProperty FollowProperty =
		BindableProperty.CreateAttached("Follow", typeof(bool), typeof(NativeTheme), false, propertyChanged: OnFollowChanged);

	private static readonly List<WeakReference<VisualElement>> Live = [];
	private static int _pruneAt = 128;
	private static bool _subscribed;

	public static bool GetFollow(BindableObject b) => (bool)b.GetValue(FollowProperty);
	public static void SetFollow(BindableObject b, bool v) => b.SetValue(FollowProperty, v);

	private static void OnFollowChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not VisualElement element || newValue is not true)
		{
			return;
		}

		if (!_subscribed)
		{
			_subscribed = true;
			Theme.Changed += OnThemeChanged;
		}

		Live.Add(new WeakReference<VisualElement>(element));

		if (Live.Count > _pruneAt)
		{
			Live.RemoveAll(r => !r.TryGetTarget(out _));
			_pruneAt = Math.Max(128, Live.Count * 2);
		}

		// The thumb depends on the toggle state.
		if (element is Switch toggle)
		{
			toggle.Toggled += OnToggled;
		}

		Apply(element);
	}

	private static void OnToggled(object? sender, ToggledEventArgs e)
	{
		if (sender is Switch toggle)
		{
			Apply(toggle);
		}
	}

	private static void OnThemeChanged(object? sender, EventArgs e)
	{
		if (MainThread.IsMainThread)
		{
			ApplyAll();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(ApplyAll);
		}
	}

	private static void ApplyAll()
	{
		for (int i = Live.Count - 1; i >= 0; i--)
		{
			if (Live[i].TryGetTarget(out VisualElement? element))
			{
				Apply(element);
			}
			else
			{
				Live.RemoveAt(i);
			}
		}
	}

	private static Color C(string key) => Theme.ColorOf(key, Colors.Gray);

	private static void Apply(VisualElement element)
	{
		switch (element)
		{
			case Switch toggle:
				toggle.OnColor = C("Accent");
				toggle.ThumbColor = toggle.IsToggled ? C("OnAccent") : C("InkMuted");
				break;

			case Slider slider:
				slider.MinimumTrackColor = C("Accent");
				slider.MaximumTrackColor = C("Outline");
				slider.ThumbColor = C("Accent");
				break;

			case Stepper stepper:
				stepper.BackgroundColor = C("Raised");
				break;

			case ActivityIndicator indicator:
				indicator.Color = C("Accent");
				break;

			case RefreshView refresh:
				refresh.RefreshColor = C("Accent");
				break;

			case Picker picker:
				picker.TextColor = C("Ink");
				picker.TitleColor = C("InkMuted");
				break;

			case DatePicker date:
				date.TextColor = C("Ink");
				break;

			case TimePicker time:
				time.TextColor = C("Ink");
				break;

			case Entry entry:
				entry.TextColor = C("Ink");
				entry.PlaceholderColor = C("InkMuted");
				break;
		}
	}
}
