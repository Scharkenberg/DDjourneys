using DDjourneys.Core.Models;

namespace DDjourneys.Controls;

/// <summary>
/// Occupancy as three dots, the granularity the provider really has (many seats, few seats,
/// standing room): one, two or three filled, the rest hollow. Fixed size, so it never wraps or
/// gets crushed; collapses completely when the level is unknown. Colour (green, amber, red) only
/// reinforces the count, it is never the sole carrier of the information.
/// </summary>
public sealed partial class OccupancyIndicator : ContentView
{
	private const int Steps = 3;
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
		int filled = DotsFor(Level);

		if (filled == 0)
		{
			Content = null;
			IsVisible = false;
			return;
		}

		string tone = filled switch
		{
			1 => "OnTime",
			2 => "Delay",
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

	/// <summary>Number of filled dots: 1 = many seats, 2 = few seats, 3 = standing room or worse.</summary>
	public static int DotsFor(OccupancyLevel level) =>
		level switch
		{
			OccupancyLevel.VeryLow => 1,
			OccupancyLevel.Low or OccupancyLevel.Medium => 2,
			OccupancyLevel.High or OccupancyLevel.Full or OccupancyLevel.Overloaded => 3,
			_ => 0
		};

	private static BoxView Filled(string tone)
	{
		var dot = Circle(Dot);
		dot.SetDynamicResource(BoxView.ColorProperty, tone);
		return dot;
	}

	private static Grid Hollow()
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
