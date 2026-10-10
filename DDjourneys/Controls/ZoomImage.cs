using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// An image that pinch-zooms (1 to 6, around the fingers), pans (clamped to what the zoom shows), zooms in
/// and back out on a double tap (around the tap, animated), and on a desktop zooms with the mouse wheel around
/// the pointer. The aspect of the picture is told by the caller, so the clamp is the picture's edge, not the
/// letterboxed frame around it.
/// </summary>
public sealed partial class ZoomImage : ContentView
{
	private const double MaxScale = 6;

	/// <summary>Where a double tap goes: far enough to read the small print, short of the limit.</summary>
	private const double TapScale = 3;

	public static readonly BindableProperty SourceProperty =
		BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(ZoomImage), null,
			propertyChanged: (bindable, _, value) => ((ZoomImage)bindable)._image.Source = (ImageSource?)value);

	public static readonly BindableProperty ContentAspectProperty =
		BindableProperty.Create(nameof(ContentAspect), typeof(double), typeof(ZoomImage), 0d);

	private readonly Image _image = new() { Aspect = Aspect.AspectFit };

	private double _scale = 1;
	private double _pinchStart;
	private double _x;
	private double _y;
	private double _panStartX;
	private double _panStartY;

	public ZoomImage()
	{
		var pinch = new PinchGestureRecognizer();

		pinch.PinchUpdated += OnPinchUpdated;

		var pan = new PanGestureRecognizer();

		pan.PanUpdated += OnPanUpdated;

		var toggle = new TapGestureRecognizer { NumberOfTapsRequired = 2 };

		toggle.Tapped += OnDoubleTapped;

		_image.GestureRecognizers.Add(pinch);
		_image.GestureRecognizers.Add(pan);
		_image.GestureRecognizers.Add(toggle);

		// The scaled picture must not draw over the frame around it.
		var clip = new Grid { IsClippedToBounds = true };

		clip.Add(_image);

		Content = clip;

		SizeChanged += (_, _) => Apply();
		HandlerChanged += OnHandlerChanged;
	}

	/// <summary>The picture to show.</summary>
	public ImageSource? Source
	{
		get => (ImageSource?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	/// <summary>The picture's width divided by its height (0: unknown, the frame is clamped instead).</summary>
	public double ContentAspect
	{
		get => (double)GetValue(ContentAspectProperty);
		set => SetValue(ContentAspectProperty, value);
	}

	/// <summary>Whether the picture is zoomed in at all.</summary>
	public bool IsZoomed => _scale > 1.01;

	/// <summary>Back to the whole picture (also the honest state after a new source).</summary>
	public void Reset()
	{
		_scale = 1;
		_x = 0;
		_y = 0;

		Apply();
	}

	/// <summary>One step in or out around the middle of the frame (the buttons of a pointer without pinch).</summary>
	public Task ZoomStepAsync(bool zoomIn)
	{
		double next = Math.Clamp(zoomIn ? _scale * 1.6 : _scale / 1.6, 1, MaxScale);

		return GoToAsync(next, 0, 0, aroundCentre: true);
	}

	private void OnDoubleTapped(object? sender, TappedEventArgs e)
	{
		// In: around the tap, to a readable zoom. Out again: the whole picture.
		if (IsZoomed)
		{
			_ = GoToAsync(1, 0, 0, aroundCentre: true);

			return;
		}

		Point? tap = e.GetPosition(this);

		double aroundX = tap is { } at ? at.X - (Width / 2) : 0;
		double aroundY = tap is { } on ? on.Y - (Height / 2) : 0;

		_ = GoToAsync(TapScale, aroundX, aroundY, aroundCentre: false);
	}

	/// <summary>
	/// Moves to a zoom level that keeps the point (measured from the centre of the frame) where it is on the
	/// screen, with a short ease where motion is welcome and at once where it is not.
	/// </summary>
	private async Task GoToAsync(double scale, double aroundX, double aroundY, bool aroundCentre)
	{
		double fromScale = _scale;
		double fromX = _x;
		double fromY = _y;

		double factor = fromScale > 0 ? scale / fromScale : 1;

		double toX = aroundCentre && scale <= 1 ? 0 : aroundX - ((aroundX - fromX) * factor);
		double toY = aroundCentre && scale <= 1 ? 0 : aroundY - ((aroundY - fromY) * factor);

		if (!Motion.Enabled)
		{
			_scale = scale;
			_x = toX;
			_y = toY;

			Apply();

			return;
		}

		await Motion.TweenAsync(
			this,
			"zoom",
			0,
			1,
			220,
			Easing.CubicOut,
			t =>
			{
				_scale = fromScale + ((scale - fromScale) * t);
				_x = fromX + ((toX - fromX) * t);
				_y = fromY + ((toY - fromY) * t);

				Apply();
			});
	}

	private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
	{
		try
		{
			if (e.Status == GestureStatus.Started)
			{
				this.AbortAnimation("zoom");

				_pinchStart = _scale;
			}
			else if (e.Status == GestureStatus.Running)
			{
				double next = Math.Clamp(_pinchStart * e.Scale, 1, MaxScale);

				ZoomAround(next, (e.ScaleOrigin.X - 0.5) * Width, (e.ScaleOrigin.Y - 0.5) * Height);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Zoom failed: {ex.Message}");
		}
	}

	/// <summary>Zoom around a point (from the frame's centre): it stays where it is on the screen.</summary>
	private void ZoomAround(double next, double originX, double originY)
	{
		double factor = _scale > 0 ? next / _scale : 1;

		_x = originX - ((originX - _x) * factor);
		_y = originY - ((originY - _y) * factor);
		_scale = next;

		Apply();
	}

	private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (e.StatusType == GestureStatus.Started)
		{
			this.AbortAnimation("zoom");

			_panStartX = _x;
			_panStartY = _y;
		}
		else if (e.StatusType == GestureStatus.Running)
		{
			_x = _panStartX + e.TotalX;
			_y = _panStartY + e.TotalY;

			Apply();
		}
	}

	private void Apply()
	{
		_image.Scale = _scale;

		// What the picture really covers inside this frame (AspectFit letterboxes around it).
		double fittedWidth = Width;
		double fittedHeight = Height;

		if (ContentAspect > 0
			&& Width > 0
			&& Height > 0)
		{
			if (ContentAspect > Width / Height)
			{
				fittedWidth = Width;
				fittedHeight = Width / ContentAspect;
			}
			else
			{
				fittedHeight = Height;
				fittedWidth = Height * ContentAspect;
			}
		}

		double maxX = Math.Max(0, (fittedWidth * _scale - Width) / 2);
		double maxY = Math.Max(0, (fittedHeight * _scale - Height) / 2);

		// The clamped offset is the offset: a drag past the edge must not pile up out of sight and make the
		// picture "stick" when the finger turns back.
		_x = Math.Clamp(_x, -maxX, maxX);
		_y = Math.Clamp(_y, -maxY, maxY);

		_image.TranslationX = _x;
		_image.TranslationY = _y;
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
#if WINDOWS
		AttachWheel(Handler?.PlatformView as Microsoft.UI.Xaml.UIElement);
#endif
	}

#if WINDOWS
	private const double WheelStep = 1.2;

	private Microsoft.UI.Xaml.UIElement? _wheelView;

	// The mouse wheel zooms around the pointer; the frame takes the wheel, so the page behind never scrolls.
	private void AttachWheel(Microsoft.UI.Xaml.UIElement? view)
	{
		if (ReferenceEquals(view, _wheelView))
		{
			return;
		}

		if (_wheelView is not null)
		{
			_wheelView.PointerWheelChanged -= OnWheel;
		}

		_wheelView = view;

		if (view is not null)
		{
			view.PointerWheelChanged += OnWheel;
		}
	}

	private void OnWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_wheelView is not { } view)
		{
			return;
		}

		Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(view);
		int delta = point.Properties.MouseWheelDelta;

		if (delta == 0)
		{
			return;
		}

		e.Handled = true;

		this.AbortAnimation("zoom");

		double next = Math.Clamp(delta > 0 ? _scale * WheelStep : _scale / WheelStep, 1, MaxScale);

		ZoomAround(next, point.Position.X - (Width / 2), point.Position.Y - (Height / 2));
	}
#endif
}
