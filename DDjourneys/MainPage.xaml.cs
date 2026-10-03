using Location = DDjourneys.Core.Models.Location;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys;

public partial class MainPage : ContentPage
{
	private readonly JourneyService _journeyService;


	public MainPage(
		JourneyService journeyService)
	{
		InitializeComponent();

		_journeyService = journeyService;
	}


	private async void SearchClicked(
		object? sender,
		EventArgs e)
	{
		LoadingIndicator.IsVisible = true;
		LoadingIndicator.IsRunning = true;


		try
		{
			var result =
				await _journeyService.SearchAsync(
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

						DateTime = Support.Format.Now(),

						MaxResults = 3
					});


			if (!result.IsSuccessful)
			{
				await DisplayAlertAsync(
					"Error",
					result.ErrorMessage ?? "Unknown error",
					"OK");

				return;
			}


			JourneyList.ItemsSource =
				result.Journeys;

		}
		finally
		{
			LoadingIndicator.IsVisible = false;
			LoadingIndicator.IsRunning = false;
		}
	}
}
