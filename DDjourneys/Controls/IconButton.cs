using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;
using System.Windows.Input;

namespace DDjourneys.Controls;

/// <summary>
/// A tappable icon with a full-size touch target and a soft pressed state. Used wherever a text
/// glyph used to stand in for a button, so every control in the app shares one icon family.
/// </summary>
public sealed partial class IconButton : ContentView
{
	public static readonly BindableProperty GlyphProperty =
		BindableProperty.Create(
			nameof(Glyph),
			typeof(IconGlyph),
			typeof(IconButton),
			IconGlyph.None,
			propertyChanged: (bindable, _, value) =>
				((IconButton)bindable)._icon.Glyph = (IconGlyph)value!);

	public static readonly BindableProperty ColorProperty =
		BindableProperty.Create(
			nameof(Color),
			typeof(Color),
			typeof(IconButton),
			null,
			propertyChanged: (bindable, _, value) =>
				((IconButton)bindable).ApplyColor((Color?)value));

	public static readonly BindableProperty SizeProperty =
		BindableProperty.Create(
			nameof(Size),
			typeof(double),
			typeof(IconButton),
			22d,
			propertyChanged: (bindable, _, value) =>
				((IconButton)bindable)._icon.Size = (double)value!);

	public static readonly BindableProperty CommandProperty =
		BindableProperty.Create(
			nameof(Command),
			typeof(ICommand),
			typeof(IconButton),
			null,
			propertyChanged: (bindable, oldValue, newValue) =>
				((IconButton)bindable).OnCommandChanged(oldValue as ICommand, newValue as ICommand));

	public static readonly BindableProperty CommandParameterProperty =
		BindableProperty.Create(
			nameof(CommandParameter),
			typeof(object),
			typeof(IconButton),
			null);

	/// <summary>Smallest touch target (40 by default; the app uses 40 and 36 for strips); a row of several icons may use less.</summary>
	public static readonly BindableProperty TargetSizeProperty =
		BindableProperty.Create(
			nameof(TargetSize),
			typeof(double),
			typeof(IconButton),
			40d,
			propertyChanged: (bindable, _, value) =>
				((IconButton)bindable).ApplyTarget((double)value!));

	private readonly Icon _icon = new();
	private readonly Border _surface;

	public IconButton()
	{
		_icon.SetDynamicResource(Icon.ColorProperty, "Accent");

		_surface =
			new Border
			{
				Content = _icon,
				Padding = 0,
				StrokeThickness = 0,
				BackgroundColor = Colors.Transparent,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
				{
					CornerRadius = 4
				},
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center
			};

		var tap = new TapGestureRecognizer();
		tap.Tapped += OnTapped;
		_surface.GestureRecognizers.Add(tap);

		// Mouse hover: a soft accent fill behind the icon (nothing moves).
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => _surface.SetDynamicResource(BackgroundColorProperty, "AccentSoft");
		pointer.PointerExited += (_, _) =>
		{
			_surface.RemoveDynamicResource(BackgroundColorProperty);
			_surface.BackgroundColor = Colors.Transparent;
		};
		_surface.GestureRecognizers.Add(pointer);

		Dense.SetMinHeight(_surface, 40);
		Dense.SetMinWidth(_surface, 40);

		Content = _surface;
	}

	public double TargetSize
	{
		get => (double)GetValue(TargetSizeProperty);
		set => SetValue(TargetSizeProperty, value);
	}

	private void ApplyTarget(double size)
	{
		Dense.SetMinHeight(_surface, size);
		Dense.SetMinWidth(_surface, size);
	}

	/// <summary>Which icon to show.</summary>
	public IconGlyph Glyph
	{
		get => (IconGlyph)GetValue(GlyphProperty);
		set => SetValue(GlyphProperty, value);
	}

	/// <summary>Ink of the icon; the accent colour by default.</summary>
	public Color? Color
	{
		get => (Color?)GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	public double Size
	{
		get => (double)GetValue(SizeProperty);
		set => SetValue(SizeProperty, value);
	}

	public ICommand? Command
	{
		get => (ICommand?)GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	public object? CommandParameter
	{
		get => GetValue(CommandParameterProperty);
		set => SetValue(CommandParameterProperty, value);
	}

	/// <summary>Raised on tap, for pages that handle the action in code-behind.</summary>
	public event EventHandler? Clicked;

	private void ApplyColor(Color? color)
	{
		if (color is null)
		{
			_icon.SetDynamicResource(Icon.ColorProperty, "Accent");
		}
		else
		{
			_icon.Color = color;
		}
	}

	private void OnCommandChanged(ICommand? previous, ICommand? next)
	{
		previous?.CanExecuteChanged -= OnCanExecuteChanged;
		next?.CanExecuteChanged += OnCanExecuteChanged;

		RefreshEnabled();
	}

	private void OnCanExecuteChanged(object? sender, EventArgs e) =>
		RefreshEnabled();

	private void RefreshEnabled()
	{
		bool enabled =
			Command is null
			|| Command.CanExecute(CommandParameter);

		_surface.Opacity = enabled ? 1 : 0.35;
		_surface.InputTransparent = !enabled;
	}

	private async void OnTapped(object? sender, TappedEventArgs e)
	{
		try
		{
			if (Glyph == IconGlyph.Refresh)
			{
				_ = Motion.SpinAsync(_icon);
			}

			await Motion.TapAsync(_surface);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Icon button feedback skipped: {ex.Message}");
		}

		Clicked?.Invoke(this, EventArgs.Empty);

		if (Command is { } command
			&& command.CanExecute(CommandParameter))
		{
			command.Execute(CommandParameter);
		}
	}
}
