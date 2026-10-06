namespace DDjourneys.Support;

/// <summary>One selectable row of a <see cref="ChoiceGroup"/> (radio-list style, tick on the selected row).</summary>
public sealed partial class ChoiceOption : ObservableObject
{
	private readonly ChoiceGroup _group;
	private readonly Func<string> _title;

	internal ChoiceOption(
		ChoiceGroup group,
		int code,
		Func<string> title)
	{
		_group = group;
		_title = title;
		Code = code;

		SelectCommand =
			new Command(
				() => _group.Select(this));
	}

	public int Code { get; }

	public string Title => _title();

	public bool IsSelected
	{
		get;
		internal set =>
			SetProperty(
				ref field,
				value);
	}

	public Command SelectCommand { get; }

	internal void RefreshTitle() =>
		OnPropertyChanged(nameof(Title));
}


/// <summary>A single-choice setting over an enum (or any int-coded set), written through on selection.</summary>
public sealed class ChoiceGroup
{
	private readonly Func<int> _get;
	private readonly Action<int> _set;

	public ChoiceGroup(
		IEnumerable<(int Code, Func<string> Title)> options,
		Func<int> get,
		Action<int> set)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);

		_get = get;
		_set = set;

		Options =
			(List<ChoiceOption>)
			[.. options.Select(
				option => new ChoiceOption(
					this,
					option.Code,
					option.Title))];

		Refresh();
	}

	public IReadOnlyList<ChoiceOption> Options { get; }

	internal void Select(ChoiceOption option)
	{
		_set(option.Code);
		Refresh();
	}

	/// <summary>Re-reads the stored value and the (possibly re-localized) titles.</summary>
	public void Refresh()
	{
		int current = _get();

		foreach (ChoiceOption option in Options)
		{
			option.IsSelected = option.Code == current;
			option.RefreshTitle();
		}
	}
}


/// <summary>A switch row with title and description, bound to a stored value.</summary>
public sealed partial class ToggleOption : ObservableObject
{
	private readonly Func<string> _title;
	private readonly Func<string?>? _description;
	private readonly Func<bool> _get;
	private readonly Func<bool, bool> _set;

	/// <param name="set">Stores the value and returns whether it was accepted (false reverts the switch).</param>
	public ToggleOption(
		Func<string> title,
		Func<string?>? description,
		Func<bool> get,
		Func<bool, bool> set)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);

		_title = title;
		_description = description;
		_get = get;
		_set = set;
	}

	public string Title => _title();

	public string? Description => _description?.Invoke();

	public bool HasDescription => !string.IsNullOrEmpty(Description);

	public bool IsOn
	{
		get => _get();
		set
		{
			_set(value);

			// Always notify: a refused change snaps the switch back.
			OnPropertyChanged();
		}
	}

	public void Refresh()
	{
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(Description));
		OnPropertyChanged(nameof(HasDescription));
		OnPropertyChanged(nameof(IsOn));
	}
}
