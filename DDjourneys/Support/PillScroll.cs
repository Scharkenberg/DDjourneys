namespace DDjourneys.Support;

/// <summary>
/// The scrolling text of a pill (a Border of class Chip holding one Label). The pill is exactly as wide as its text,
/// up to <see cref="Fraction"/> of the width its parent layout has; a longer text scrolls horizontally inside it.
/// <para>
/// The width is set explicitly (WidthRequest) from the laid-out width of the label. A horizontal ScrollView does
/// not report "content or less" when measured: on Android it takes the full width it is offered, which made every
/// pill wide. The label is pinned to <c>Start</c> so its width is always its own text width and never the
/// viewport's, which keeps the feedback loop (viewport from label, label from viewport) from existing.
/// </para>
/// <para>
/// Mouse users get what touch users have: wheel and click-drag scroll the text (Windows).
/// A pill pops in once its width is known. One that overflows scrolls to its end and back once after it appears, as a hint that it can be scrolled.
/// </para>
/// </summary>
public sealed class PillScroll : ScrollView
{
	public static readonly BindableProperty FractionProperty =
		BindableProperty.Create(
			nameof(Fraction),
			typeof(double),
			typeof(PillScroll),
			1.0,
			propertyChanged: (bindable, _, _) => ((PillScroll)bindable).Resize());

	private Label? _label;
	private VisualElement? _host;
	private bool _shown;
	private bool _hinted;

	public PillScroll()
	{
		Orientation = ScrollOrientation.Horizontal;
		HorizontalScrollBarVisibility = ScrollBarVisibility.Never;

		// Until the text has been measured the pill is a sliver, hidden (see OnParentSet): never wide.
		WidthRequest = 1;

		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		HandlerChanged += OnHandlerChanged;
	}

	/// <summary>Largest share of the parent layout's width the pill may take. 0 or less: no limit.</summary>
	public double Fraction
	{
		get => (double)GetValue(FractionProperty);
		set => SetValue(FractionProperty, value);
	}

	/// <summary>The pill itself (the Border around this view).</summary>
	private VisualElement? Pill => Parent as VisualElement;

	protected override void OnParentSet()
	{
		base.OnParentSet();

		// Hidden until the width is known, so there is no flash of a wrong-sized pill.
		if (Parent is VisualElement pill && !_shown)
		{
			pill.Opacity = 0;
			pill.Scale = 0.85;
		}
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (propertyName == nameof(Content))
		{
			Adopt();
		}
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		Release();
		Adopt();

		_host = Pill?.Parent as VisualElement;

		if (_host is not null)
		{
			_host.SizeChanged += OnSizeChanged;
		}

		Resize();

		// Failsafe: never leave the pill invisible, whatever the layout did.
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () => Reveal(false));
	}

	private void OnUnloaded(object? sender, EventArgs e) => Release();

	private void Release()
	{
		if (_host is not null)
		{
			_host.SizeChanged -= OnSizeChanged;
			_host = null;
		}

		if (_label is not null)
		{
			_label.SizeChanged -= OnSizeChanged;
			_label.PropertyChanged -= OnLabelChanged;
			_label = null;
		}
	}

	private void Adopt()
	{
		if (Content is not Label label || ReferenceEquals(label, _label))
		{
			return;
		}

		if (_label is not null)
		{
			_label.SizeChanged -= OnSizeChanged;
			_label.PropertyChanged -= OnLabelChanged;
		}

		_label = label;
		label.HorizontalOptions = LayoutOptions.Start;
		label.SizeChanged += OnSizeChanged;
		label.PropertyChanged += OnLabelChanged;
	}

	private void OnLabelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == Label.TextProperty.PropertyName)
		{
			_hinted = false;
		}
	}

	private void OnSizeChanged(object? sender, EventArgs e) => Resize();

	private void Resize()
	{
		// A label that is not pinned to Start would take the viewport's width: do not measure that.
		if (_label is null || _label.Width <= 0 || _label.HorizontalOptions.Alignment != LayoutAlignment.Start)
		{
			return;
		}

		// +2: Android measures the semibold font a hair too narrow; an exact fit would clip the last glyph.
		double want = Math.Ceiling(_label.Width) + 2;
		double cap = Cap();
		double width = cap > 0 ? Math.Min(want, cap) : want;

		if (Math.Abs(width - WidthRequest) > 0.5)
		{
			WidthRequest = width;
		}

		Reveal(true);

		if (want > width + 1)
		{
			_ = HintAsync();
		}
	}

	/// <summary>Width the text area may take, or 0 for no limit.</summary>
	private double Cap()
	{
		if (Fraction <= 0 || _host is null || _host.Width <= 0)
		{
			return 0;
		}

		double chrome = 0;

		if (Pill is Border pill)
		{
			chrome = pill.Padding.HorizontalThickness + pill.Margin.HorizontalThickness + (2 * pill.StrokeThickness);
		}

		return Math.Max(48, (_host.Width * Math.Min(Fraction, 1)) - chrome);
	}

	private void Reveal(bool animate)
	{
		if (_shown || Pill is not { } pill)
		{
			return;
		}

		_shown = true;

		if (animate && Motion.Enabled)
		{
			_ = RevealAsync(pill);
		}
		else
		{
			pill.Opacity = 1;
			pill.Scale = 1;
		}
	}

	private static async Task RevealAsync(VisualElement pill)
	{
		try
		{
			await Task.WhenAll(
				pill.FadeToAsync(1, 160, Easing.CubicOut),
				pill.ScaleToAsync(1, 340, Curves.Settle));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Pill reveal skipped: {ex.Message}");
		}
		finally
		{
			pill.Opacity = 1;
			pill.Scale = 1;
		}
	}

	/// <summary>Scrolls an overflowing pill to its end and back once, so it is visible that it scrolls.</summary>
	private async Task HintAsync()
	{
		if (_hinted || !Motion.Enabled)
		{
			return;
		}

		_hinted = true;

		try
		{
			await Task.Delay(1100);

			double room = ContentSize.Width - Width;

			if (Handler is null || room <= 1 || ScrollX > 1)
			{
				return;
			}

			await ScrollToAsync(room, 0, true);
			await Task.Delay(650);

			if (Handler is not null && ScrollX > 1)
			{
				await ScrollToAsync(0, 0, true);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Pill hint skipped: {ex.Message}");
		}
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
#if WINDOWS
		AttachViewer(Handler?.PlatformView as Microsoft.UI.Xaml.Controls.ScrollViewer);
#endif
	}

#if WINDOWS
	private const double DragThreshold = 4;

	private Microsoft.UI.Xaml.Controls.ScrollViewer? _viewer;
	private double _pressX;
	private double _pressOffset;
	private bool _pressed;
	private bool _dragging;

	private void AttachViewer(Microsoft.UI.Xaml.Controls.ScrollViewer? viewer)
	{
		if (ReferenceEquals(viewer, _viewer))
		{
			return;
		}

		if (_viewer is not null)
		{
			_viewer.PointerWheelChanged -= OnWheel;
			_viewer.PointerPressed -= OnPressed;
			_viewer.PointerMoved -= OnMoved;
			_viewer.PointerReleased -= OnReleased;
			_viewer.PointerCaptureLost -= OnCaptureLost;
		}

		_viewer = viewer;

		if (viewer is null)
		{
			return;
		}

		viewer.PointerWheelChanged += OnWheel;
		viewer.PointerPressed += OnPressed;
		viewer.PointerMoved += OnMoved;
		viewer.PointerReleased += OnReleased;
		viewer.PointerCaptureLost += OnCaptureLost;
	}

	// The mouse wheel scrolls the text while it can still move that way; at either end the wheel is left to the page.
	private void OnWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer viewer || viewer.ScrollableWidth <= 0)
		{
			return;
		}

		var properties = e.GetCurrentPoint(viewer).Properties;
		double delta = properties.MouseWheelDelta;
		double target = properties.IsHorizontalMouseWheel
			? viewer.HorizontalOffset + delta
			: viewer.HorizontalOffset - delta;

		target = Math.Clamp(target, 0, viewer.ScrollableWidth);

		if (Math.Abs(target - viewer.HorizontalOffset) < 0.5)
		{
			return;
		}

		viewer.ChangeView(target, null, null);
		e.Handled = true;
	}

	// Click-drag with the mouse pans the text (touch and pen already do through the ScrollViewer).
	private void OnPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer viewer
			|| e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse
			|| !e.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_pressed = true;
		_dragging = false;
		_pressX = e.GetCurrentPoint(viewer).Position.X;
		_pressOffset = viewer.HorizontalOffset;
	}

	private void OnMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (!_pressed || sender is not Microsoft.UI.Xaml.Controls.ScrollViewer viewer || viewer.ScrollableWidth <= 0)
		{
			return;
		}

		double moved = e.GetCurrentPoint(viewer).Position.X - _pressX;

		if (!_dragging)
		{
			if (Math.Abs(moved) < DragThreshold)
			{
				return;
			}

			// Capture only once it is a drag, so a plain click still reaches whatever is under the pill.
			_dragging = viewer.CapturePointer(e.Pointer);
		}

		if (_dragging)
		{
			viewer.ChangeView(Math.Clamp(_pressOffset - moved, 0, viewer.ScrollableWidth), null, null, true);
			e.Handled = true;
		}
	}

	private void OnReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_dragging && sender is Microsoft.UI.Xaml.Controls.ScrollViewer viewer)
		{
			viewer.ReleasePointerCapture(e.Pointer);
			e.Handled = true;
		}

		_pressed = false;
		_dragging = false;
	}

	private void OnCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		_pressed = false;
		_dragging = false;
	}
#endif
}
