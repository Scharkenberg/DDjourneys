using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Providers;

/// <summary>
/// The providers the app ships with and the one the user selected. Selection is persisted through the
/// supplied delegates (the Core library does not know the app's settings store).
/// </summary>
public sealed class ProviderRegistry
{
	private readonly Func<string?>? _load;
	private readonly Action<string>? _save;

	private string? _selectedId;

	/// <summary>A provider used for the current async flow only (see <see cref="Override"/>).</summary>
	private readonly AsyncLocal<string?> _override = new();

	public ProviderRegistry(
		IEnumerable<ProviderInfo> providers,
		Func<string?>? load = null,
		Action<string>? save = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		Providers =
			[.. providers
				.GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())
				.OrderBy(provider => provider.Region, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)];

		_load = load;
		_save = save;
	}

	/// <summary>Raised after the selection changed (new provider id).</summary>
	public event EventHandler<string>? SelectionChanged;

	/// <summary>All providers, ordered by region, then name.</summary>
	public IReadOnlyList<ProviderInfo> Providers { get; }

	/// <summary>The selected provider; falls back to the first one when the stored id is unknown.</summary>
	public ProviderInfo? Selected =>
		Find(SelectedId)
		?? (Providers.Count > 0 ? Providers[0] : null);

	public string SelectedId
	{
		get
		{
			if (_override.Value is { } forced
				&& Find(forced) is { } known)
			{
				return known.Id;
			}

			_selectedId ??= _load?.Invoke();

			return Find(_selectedId)?.Id
				?? (Providers.Count > 0 ? Providers[0].Id : null)
				?? string.Empty;
		}
	}

	public ProviderInfo? Find(string? id) =>
		string.IsNullOrWhiteSpace(id)
			? null
			: Providers.FirstOrDefault(
				provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));

	/// <summary>Whether the selected provider supports a feature (true when nothing is registered).</summary>
	public bool Supports(ProviderCapabilities capability) =>
		Selected?.Supports(capability)
		?? true;

	/// <summary>
	/// Makes <paramref name="id"/> the provider for everything awaited inside the returned scope, without changing
	/// the user's selection (a widget belongs to the provider it was made for, whichever one the app shows now).
	/// </summary>
	public IDisposable Override(string? id)
	{
		string? previous = _override.Value;

		_override.Value = Find(id)?.Id;

		return new OverrideScope(() => _override.Value = previous);
	}

	private sealed class OverrideScope(Action restore) : IDisposable
	{
		public void Dispose() =>
			restore();
	}

	/// <summary>Selects a provider; unknown ids are ignored.</summary>
	public bool Select(string id)
	{
		ProviderInfo? provider = Find(id);

		if (provider is null
			|| string.Equals(provider.Id, SelectedId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		_selectedId = provider.Id;
		_save?.Invoke(provider.Id);
		SelectionChanged?.Invoke(this, provider.Id);

		return true;
	}

	/// <summary>
	/// True when the provider behind <paramref name="candidate"/> is the selected one,
	/// or when it does not declare itself (then it is always eligible).
	/// </summary>
	public bool IsSelected(object candidate) =>
		candidate is not IProviderDescriptor descriptor
		|| string.Equals(
			descriptor.Info.Id,
			SelectedId,
			StringComparison.OrdinalIgnoreCase);
}
