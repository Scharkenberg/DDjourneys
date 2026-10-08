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

	private void OnEndpointTapped(object? sender, EventArgs e) =>
		EndpointTapped?.Invoke(this, new TappedEventArgs(null));
}
