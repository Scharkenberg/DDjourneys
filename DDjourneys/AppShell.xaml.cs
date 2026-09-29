using DDjourneys.Pages;
using DDjourneys.Support;

namespace DDjourneys
{
	public partial class AppShell : Shell
	{
		public AppShell()
		{
			InitializeComponent();
			Routing.RegisterRoute(nameof(PlaceSearchPage), typeof(PlaceSearchPage));
		}
	}
}
