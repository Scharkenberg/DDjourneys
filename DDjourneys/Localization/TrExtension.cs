using Microsoft.Maui.Controls.Xaml;

namespace DDjourneys.Localization;

[ContentProperty(nameof(Key))]
public sealed class TrExtension : IMarkupExtension<BindingBase>
{
	public string Key { get; set; } = string.Empty;

	public BindingBase ProvideValue(IServiceProvider serviceProvider)
	{
		if (string.IsNullOrWhiteSpace(Key))
		{
			throw new InvalidOperationException("A localization key is required.");
		}

		return new Binding
		{
			Source = LocalizationService.Current,
			Path = $"CurrentStrings.{Key}"
		};
	}

	object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
		ProvideValue(serviceProvider);
}