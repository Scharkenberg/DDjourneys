using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>
/// Minimal base for view models.
///
/// Usage (C# 14+ field keyword):
///   public string Query { get => field; set => SetProperty(ref field, value); }
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	protected bool SetProperty<T>(
		ref T storage,
		T value,
		[CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(storage, value))
		{
			return false;
		}

		storage = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
