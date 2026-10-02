using DDjourneys.Core.Models;

namespace DDjourneys.Controls;

/// <summary>
/// A stop name with its city/region right behind it on the same line ("Hauptbahnhof, Dresden"),
/// wrapping together when the line is narrow. The city is set in the muted ink colour.
/// </summary>
public sealed class StopNameView : ContentView
{
	public static readonly BindableProperty StopProperty =
		BindableProperty.Create(
			nameof(Stop),
			typeof(string),
			typeof(StopNameView),
			string.Empty,
			propertyChanged: (bindable, _, _) =>
				((StopNameView)bindable).Rebuild());

	public static readonly BindableProperty PlaceProperty =
		BindableProperty.Create(
			nameof(Place),
			typeof(string),
			typeof(StopNameView),
			null,
			propertyChanged: (bindable, _, _) =>
				((StopNameView)bindable).Rebuild());

	public static readonly BindableProperty CompactProperty =
		BindableProperty.Create(
			nameof(Compact),
			typeof(bool),
			typeof(StopNameView),
			false,
			propertyChanged: (bindable, _, _) =>
				((StopNameView)bindable).Rebuild());

	private readonly Label _label =
		new()
		{
			LineBreakMode = LineBreakMode.WordWrap
		};

	public StopNameView()
	{
		Content = _label;
		Rebuild();
	}

	/// <summary>The stop's name.</summary>
	public string Stop
	{
		get => (string)GetValue(StopProperty);
		set => SetValue(StopProperty, value);
	}

	/// <summary>City, village or region; may be empty.</summary>
	public string? Place
	{
		get => (string?)GetValue(PlaceProperty);
		set => SetValue(PlaceProperty, value);
	}

	/// <summary>Caption-sized, regular weight (intermediate stops, chips).</summary>
	public bool Compact
	{
		get => (bool)GetValue(CompactProperty);
		set => SetValue(CompactProperty, value);
	}

	private void Rebuild()
	{
		_label.StyleClass =
			Compact
				? ["Caption"]
				: null;

		var text = new FormattedString();

		var name =
			new Span
			{
				Text = Stop
			};

		if (!Compact)
		{
			name.FontFamily = "OpenSansSemibold";
		}

		text.Spans.Add(name);

		if (StopLabel.PlaceFor(Stop, Place) is { } city)
		{
			var place =
				new Span
				{
					Text = $", {city}"
				};

			text.Spans.Add(place);
			_label.FormattedText = text;
			place.SetDynamicResource(Span.TextColorProperty, "InkMuted");
		}
		else
		{
			_label.FormattedText = text;
		}
	}
}
