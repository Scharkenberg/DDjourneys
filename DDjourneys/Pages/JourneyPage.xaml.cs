namespace DDjourneys.Pages;

public partial class JourneyPage : ContentPage
{
	private readonly JourneyViewModel _vm;
	private bool _headerShown;

	public JourneyPage(JourneyViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (_headerShown)
		{
			return;
		}

		_headerShown = true;

		HeaderBlock.Opacity = 0;
		HeaderBlock.TranslationY = 16;

		await Task.WhenAll(
			HeaderBlock.FadeToAsync(1, 300, Easing.CubicOut),
			HeaderBlock.TranslateToAsync(0, 0, 300, Easing.CubicOut));
	}

	/// <summary>Rows slide in one after another; expanded stops use their own short stagger.</summary>
	private async void RowLoaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement view || view.BindingContext is not Support.TimelineRow row)
		{
			return;
		}

		view.Opacity = 0;
		view.TranslationY = 14;

		await Task.Delay(Math.Min(row.Index, 14) * 45);

		await Task.WhenAll(
			view.FadeToAsync(1, 260, Easing.CubicOut),
			view.TranslateToAsync(0, 0, 260, Easing.CubicOut));
	}

	private void StopsToggled(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is Support.LegRow leg)
		{
			_vm.ToggleStopsCommand.Execute(leg);
		}
	}
}
