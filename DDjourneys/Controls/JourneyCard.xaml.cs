namespace DDjourneys.Controls;

public partial class JourneyCard : ContentView
{
	public JourneyCard()
	{
		InitializeComponent();

		FromStop.Tapped += OnEndpointTapped;
		ToStop.Tapped += OnEndpointTapped;
	}

	/// <summary>A tap on an endpoint name, which scrolls and therefore keeps the tap from the card on Android.</summary>
	public event EventHandler<TappedEventArgs>? EndpointTapped;

	private long _lastTap;

	private void OnEndpointTapped(object? sender, EventArgs e) => Forward();

	/// <summary>The legs line (it scrolls, so on Android it takes the tap from the card) counts as a tap on the card.</summary>
	private void OnLegsTapped(object? sender, TappedEventArgs e) => Forward();

	private void Forward()
	{
		// Scroller and line may both report the same tap.
		long now = Environment.TickCount64;

		if (now - _lastTap < 400)
		{
			return;
		}

		_lastTap = now;
		EndpointTapped?.Invoke(this, new TappedEventArgs(null));
	}
}
