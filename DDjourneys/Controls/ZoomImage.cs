using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Controls;

/// <summary>
/// An image that pinch-zooms (1 to 6, around the fingers), pans (clamped to what the zoom shows) and
/// resets on a double tap. The aspect of the picture is told by the caller, so the clamp is the picture's
/// edge, not the letterboxed frame around it.
/// </summary>
public sealed partial class ZoomImage : ContentView
{
	private const double MaxScale = 6;

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

		var reset = new TapGestureRecognizer { NumberOfTapsRequired = 2 };

		reset.Tapped += (_, _) => Reset();

		_image.GestureRecognizers.Add(pinch);
		_image.GestureRecognizers.Add(pan);
		_image.GestureRecognizers.Add(reset);

		// The scaled picture must not draw over the frame around it.
		var clip = new Grid { IsClippedToBounds = true };

		clip.Add(_image);

		Content = clip;
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

	/// <summary>Back to the whole picture (the double tap; also the honest state after a new source).</summary>
	public void Reset()
	{
		_scale = 1;
		_x = 0;
		_y = 0;

		Apply();
	}

	private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
	{
		try
		{
			if (e.Status == GestureStatus.Started)
			{
				_pinchStart = _scale;
			}
			else if (e.Status == GestureStatus.Running)
			{
				double next = Math.Clamp(_pinchStart * e.Scale, 1, MaxScale);

				// Zoom around the point between the fingers: it stays where it is on the screen.
				double originX = (e.Origin.X - 0.5) * Width;
				double originY = (e.Origin.Y - 0.5) * Height;
				double factor = _scale > 0 ? next / _scale : 1;

				_x = originX - ((originX - _x) * factor);
				_y = originY - ((originY - _y) * factor);
				_scale = next;

				Apply();
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Zoom failed: {ex.Message}");
		}
	}

	private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (e.StatusType == GestureStatus.Started)
		{
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

		double maxX = Math.Max(0, (fittedWidth * _scale - fittedWidth) / 2);
		double maxY = Math.Max(0, (fittedHeight * _scale - fittedHeight) / 2);

		_image.TranslationX = Math.Clamp(_x, -maxX, maxX);
		_image.TranslationY = Math.Clamp(_y, -maxY, maxY);
	}
}
