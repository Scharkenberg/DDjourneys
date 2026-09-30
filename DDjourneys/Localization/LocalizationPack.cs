using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DDjourneys.Localization;

public sealed class LocalizationPack : INotifyPropertyChanged
{
	private bool _isSelected;

	public LocalizationPack(
		string code,
		string displayName,
		IUiStrings strings)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(code);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
		ArgumentNullException.ThrowIfNull(strings);

		Code = code;
		DisplayName = displayName;
		Strings = strings;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Code { get; }

	public string DisplayName { get; }

	public IUiStrings Strings { get; }

	public bool IsSelected
	{
		get => _isSelected;
		internal set
		{
			if (_isSelected == value)
			{
				return;
			}

			_isSelected = value;
			OnPropertyChanged();
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}