using Microsoft.Extensions.DependencyInjection;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys
{
	public partial class App : Application
	{
		private readonly JourneyService _journeyService;


		public App(
			JourneyService journeyService)
		{
			InitializeComponent();

			_journeyService = journeyService;
			_ = TestAsync(_journeyService);
		}

		protected override Window CreateWindow(
		IActivationState? activationState)
		{
			return new Window(
				new ContentPage
				{
					Content = new Label
					{
						Text = "Testing VVO...",
						HorizontalOptions = LayoutOptions.Center,
						VerticalOptions = LayoutOptions.Center
					}
				});
		}

		private static async Task TestAsync(JourneyService journeyService)
		{
			var result =
				await journeyService.SearchAsync(
					new JourneyQuery
					{
						From = new Location
						{
							Id = "33000336",
							Name = "Kirschenstraße"
						},

						To = new Location
						{
							Id = "33000001",
							Name = "Bahnhof Mitte"
						},

						DateTime =
							DateTimeOffset.Now.AddMinutes(10),

						MaxResults = 3
					});


			System.Diagnostics.Debug.WriteLine($"Success: {result.IsSuccessful}");

			System.Diagnostics.Debug.WriteLine($"Error: {result.ErrorMessage}");

			System.Diagnostics.Debug.WriteLine($"Journeys: {result.Journeys.Count}");
		}
	}
}