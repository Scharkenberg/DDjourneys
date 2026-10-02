using Shapes = Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Controls;

/// <summary>
/// A vector icon from the app's own set. The geometry is drawn on a 24x24 grid and scaled from its
/// top-left corner, so an icon is identical at every size (stroke included) on every platform, and
/// its colour is an ordinary bindable value that follows the theme.
/// </summary>
public sealed class Icon : ContentView
{
	private const double GridSize = 24;
	private const double Thickness = 2;

	public static readonly BindableProperty GlyphProperty =
		BindableProperty.Create(
			nameof(Glyph),
			typeof(IconGlyph),
			typeof(Icon),
			IconGlyph.None,
			propertyChanged: OnGlyphChanged);

	public static readonly BindableProperty ColorProperty =
		BindableProperty.Create(
			nameof(Color),
			typeof(Color),
			typeof(Icon),
			null,
			propertyChanged: OnPaintChanged);

	public static readonly BindableProperty SizeProperty =
		BindableProperty.Create(
			nameof(Size),
			typeof(double),
			typeof(Icon),
			22d,
			propertyChanged: OnSizeChanged);

	private readonly Shapes.Path _path =
		new()
		{
			Aspect = Shapes.Stretch.None,
			StrokeThickness = Thickness,
			StrokeLineCap = Shapes.PenLineCap.Round,
			StrokeLineJoin = Shapes.PenLineJoin.Round,
			HorizontalOptions = LayoutOptions.Start,
			VerticalOptions = LayoutOptions.Start,
			WidthRequest = GridSize,
			HeightRequest = GridSize,

			// Scaling happens from the top-left corner, so the 24x24 grid maps exactly onto the
			// requested size - no centring maths, and the stroke scales with the drawing.
			AnchorX = 0,
			AnchorY = 0
		};

	public Icon()
	{
		Content =
			new Microsoft.Maui.Controls.Grid
			{
				Children = { _path }
			};

		HorizontalOptions = LayoutOptions.Center;
		VerticalOptions = LayoutOptions.Center;

		ApplySize();
		ApplyGlyph();
	}

	/// <summary>Which icon to draw.</summary>
	public IconGlyph Glyph
	{
		get => (IconGlyph)GetValue(GlyphProperty);
		set => SetValue(GlyphProperty, value);
	}

	/// <summary>Ink of the icon; defaults to the body text colour of the theme.</summary>
	public Color? Color
	{
		get => (Color?)GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	/// <summary>Edge length in device-independent units.</summary>
	public double Size
	{
		get => (double)GetValue(SizeProperty);
		set => SetValue(SizeProperty, value);
	}

	private static void OnGlyphChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((Icon)bindable).ApplyGlyph();

	private static void OnPaintChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((Icon)bindable).ApplyPaint();

	private static void OnSizeChanged(BindableObject bindable, object? oldValue, object? newValue) =>
		((Icon)bindable).ApplySize();

	private void ApplySize()
	{
		double size = Math.Max(1, Size);

		WidthRequest = size;
		HeightRequest = size;

		_path.Scale = size / GridSize;
	}

	private void ApplyGlyph()
	{
		IconDef? def = IconPaths.For(Glyph);

		if (def is null)
		{
			// Nothing to draw; the control keeps its box so layouts do not jump.
			_path.Data = null;
			return;
		}

		try
		{
			_path.Data = Parse(def.Data);
		}
		catch (Exception ex)
		{
			// A broken path must never take the page down with it.
			System.Diagnostics.Debug.WriteLine($"Icon '{Glyph}' failed to parse: {ex.Message}");

			_path.Data = null;
			return;
		}

		ApplyPaint();
	}

	private void ApplyPaint()
	{
		if (IconPaths.For(Glyph) is not { } def)
		{
			return;
		}

		_path.RemoveDynamicResource(Shapes.Shape.StrokeProperty);
		_path.RemoveDynamicResource(Shapes.Shape.FillProperty);

		if (Color is { } color)
		{
			_path.Stroke = new SolidColorBrush(color);
			_path.Fill = def.Filled ? new SolidColorBrush(color) : null;
		}
		else
		{
			_path.Fill = null;
			_path.SetDynamicResource(Shapes.Shape.StrokeProperty, "InkBrush");

			if (def.Filled)
			{
				_path.SetDynamicResource(Shapes.Shape.FillProperty, "InkBrush");
			}
		}
	}

	private static Shapes.Geometry? Parse(string data) =>
		(Shapes.Geometry?)new Shapes.PathGeometryConverter()
			.ConvertFromInvariantString(data);
}
