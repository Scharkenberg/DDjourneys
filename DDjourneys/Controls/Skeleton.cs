using DDjourneys.Support;
using Microsoft.Maui.Controls.Shapes;

namespace DDjourneys.Controls;

/// <summary>
/// Placeholder cards in the shape of a journey card, shown while connections load. They breathe (a slow fade between
/// two opacities) so the wait reads as "something is coming" rather than "nothing is there". With animations off
/// they stay still.
/// </summary>
public sealed class Skeleton : ContentView
{
	public static readonly BindableProperty CountProperty =
		BindableProperty.Create(
			nameof(Count),
			typeof(int),
			typeof(Skeleton),
			3,
			propertyChanged: (bindable, _, _) => ((Skeleton)bindable).Build());

	private bool _running;

	public Skeleton()
	{
		Build();

		Loaded += OnLoaded;
	}

	/// <summary>Number of placeholder cards.</summary>
	public int Count
	{
		get => (int)GetValue(CountProperty);
		set => SetValue(CountProperty, value);
	}

	private void Build()
	{
		var stack = new VerticalStackLayout { Spacing = 10 };

		for (int i = 0; i < Math.Clamp(Count, 1, 6); i++)
		{
			stack.Add(Card(i));
		}

		Content = stack;
	}

	private static Border Card(int index)
	{
		// Slightly different widths per card, so the stack does not look stamped.
		double[] stops = [150, 120, 170];
		double stop = stops[index % stops.Length];

		var times = new Grid
		{
			ColumnDefinitions = new ColumnDefinitionCollection
			{
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 12
		};

		times.Add(Bar(56, 24), 0);
		times.Add(Bar(double.NaN, 4, 2), 1);
		times.Add(Bar(56, 24), 2);

		var places = new Grid
		{
			ColumnDefinitions = new ColumnDefinitionCollection
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star)
			}
		};

		places.Add(Bar(stop, 12), 0);

		View end = Bar(stop * 0.8, 12);

		end.HorizontalOptions = LayoutOptions.End;
		places.Add(end, 1);

		var chips = new HorizontalStackLayout { Spacing = 6 };

		chips.Add(Bar(70, 26, 13));
		chips.Add(Bar(46, 26, 13));
		chips.Add(Bar(90, 26, 13));

		var body = new VerticalStackLayout { Spacing = 14 };

		body.Add(times);
		body.Add(places);
		body.Add(chips);

		var card = new Border
		{
			Content = body,
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 6 }
		};

		card.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
		card.SetDynamicResource(Border.PaddingProperty, "PadCard");
		Motion.SetEnter(card, EntranceStyle.None);

		return card;
	}

	private static BoxView Bar(double width, double height, double corner = 4)
	{
		var bar = new BoxView
		{
			HeightRequest = height,
			CornerRadius = corner,
			HorizontalOptions = double.IsNaN(width) ? LayoutOptions.Fill : LayoutOptions.Start,
			VerticalOptions = LayoutOptions.Center
		};

		if (!double.IsNaN(width))
		{
			bar.WidthRequest = width;
		}

		bar.SetDynamicResource(BoxView.ColorProperty, "Raised");

		return bar;
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		if (_running)
		{
			return;
		}

		_running = true;
		_ = BreatheAsync();
	}

	private async Task BreatheAsync()
	{
		try
		{
			while (Handler is not null)
			{
				if (!Motion.Enabled || !IsVisible || !IsLoaded)
				{
					Opacity = 1;
					await Task.Delay(500);
					continue;
				}

				long started = Environment.TickCount64;

				await this.FadeToAsync(0.45, 850, Curves.Emphasized);
				await this.FadeToAsync(1, 850, Curves.Emphasized);

				if (Environment.TickCount64 - started < 1200)
				{
					await Task.Delay(2000); // system animations are off: do not spin
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Skeleton stopped: {ex.Message}");
		}
		finally
		{
			Opacity = 1;
			_running = false;
		}
	}
}
