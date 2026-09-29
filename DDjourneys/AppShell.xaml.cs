using DDjourneys.Pages;
using DDjourneys.Support;

namespace DDjourneys
{
	public partial class AppShell : Shell
	{
		public AppShell()
		{
			InitializeComponent();
			Routing.RegisterRoute(Routes.PlaceSearch, typeof(PlaceSearchPage));
		}
	}
}
