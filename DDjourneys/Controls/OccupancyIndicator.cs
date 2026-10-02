using DDjourneys.Core.Models;

namespace DDjourneys.Controls;

/// <summary>
/// Occupancy as a row of dots: as many filled dots as the level has steps,
/// the rest hollow. Fixed size (6 dots, 52 px), so it never wraps or gets crushed;
/// collapses completely when the level is unknown. Colour only reinforces the
/// count (green, amber, red), it is never the sole carrier of the information.
/// </summary>
public sealed class OccupancyIndicator : ContentView
{
	private const int Steps = 6;
	private const double Dot = 7;
	private const double Hole = 3;
	private const double Gap = 2;

	public static readonly BindableProperty LevelProperty =
		BindableProperty.Create(
			nameof(Level),
			typeof(OccupancyLevel),
			typeof(OccupancyIndicator),
			OccupancyLevel.Unknown,
			propertyChanged: (bindable, _, _) =>
				((OccupancyIndicator)bindable).Rebuild());

	public OccupancyIndicator()
	{
		HorizontalOptions = LayoutOptions.Start;
		VerticalOptions = LayoutOptions.Center;
		Rebuild();
	}

	public OccupancyLevel Level
	{
		get => (OccupancyLevel)GetValue(LevelProperty);
		set => SetValue(LevelProperty, value);
	}

	private void Rebuild()
	{
		int filled = (int)Level;

		if (filled <= 0 || filled > Steps)
		{
			Content = null;
			IsVisible = false;
			return;
		}

		string tone = Level switch
		{
			OccupancyLevel.VeryLow or OccupancyLevel.Low => "OnTime",
			OccupancyLevel.Medium or OccupancyLevel.High => "Delay",
			_ => "Cancelled"
		};

		var row = new HorizontalStackLayout { Spacing = Gap };

		for (int i = 0; i < Steps; i++)
		{
			row.Add(
				i < filled
					? Filled(tone)
					: Hollow());
		}

		Content = row;
		IsVisible = true;
	}

	private static View Filled(string tone)
	{
		var dot = Circle(Dot);
		dot.SetDynamicResource(BoxView.ColorProperty, tone);
		return dot;
	}

	private static View Hollow()
	{
		var ring = Circle(Dot);
		ring.SetDynamicResource(BoxView.ColorProperty, "Outline");

		var hole = Circle(Hole);
		hole.HorizontalOptions = LayoutOptions.Center;
		hole.VerticalOptions = LayoutOptions.Center;
		hole.SetDynamicResource(BoxView.ColorProperty, "Surface");

		return new Grid
		{
			WidthRequest = Dot,
			HeightRequest = Dot,
			Children = { ring, hole }
		};
	}

	private static BoxView Circle(double size) =>
		new()
		{
			WidthRequest = size,
			HeightRequest = size,
			CornerRadius = (float)(size / 2)
		};
}
