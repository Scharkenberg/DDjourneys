namespace DDjourneys.Support;

/// <summary>
/// Decides when a page is gone for good. In Shell a pushed page stays alive underneath the new
/// one (<see cref="NavigationType.Push"/>); it is gone after a pop, a removal or a replacement.
/// Call from <c>OnNavigatedFrom</c>.
/// </summary>
public static class PageTeardown
{
	public static bool IsLeavingForGood(NavigatedFromEventArgs args)
	{
		ArgumentNullException.ThrowIfNull(args);

		return args.NavigationType is
			NavigationType.Pop
			or NavigationType.PopToRoot
			or NavigationType.Remove
			or NavigationType.Replace;
	}

	/// <summary>Disposes the page's view model when the page has been left for good.</summary>
	public static void DisposeIfLeft(NavigatedFromEventArgs args, object? bindingContext)
	{
		if (IsLeavingForGood(args) && bindingContext is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}
}
