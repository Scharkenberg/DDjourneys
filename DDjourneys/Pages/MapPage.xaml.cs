using DDjourneys.Core.Mapping;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>A full-screen map of one scene (journey, run of a vehicle, stops). The scene arrives through the route query.</summary>
public partial class MapPage : ContentPage, IQueryAttributable
{
	public MapPage()
	{
		InitializeComponent();
		Motion.Prepare(this);
	}

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.MapTitle, out object? title)
			&& title is string text
			&& text.Length > 0)
		{
			Title = text;
		}

		if (query.TryGetValue(Routes.MapScene, out object? value)
			&& value is MapScene scene)
		{
			_ = Map.ShowAsync(scene);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);
	}
}
