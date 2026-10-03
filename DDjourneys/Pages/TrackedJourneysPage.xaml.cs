using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class TrackedJourneysPage : ContentPage
{
	private readonly TrackedJourneysViewModel _vm;

	public TrackedJourneysPage(TrackedJourneysViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		BindingContext = _vm = vm;

		vm.Confirm = (title, message) =>
		{
			CommonStrings common = LocalizationService.Current.CurrentStrings.Common;

			return DisplayAlertAsync(title, message, common.Ok, common.Cancel);
		};
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		_vm.StartObserving();
		Motion.EnterPage(this);
	}

	protected override void OnDisappearing()
	{
		_vm.StopObserving();

		base.OnDisappearing();
	}
}
