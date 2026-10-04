namespace DDjourneys.Core.Models;

/// <summary>
/// A user-saved location (e.g., "Home" or "Work").
/// </summary>
public sealed record SavedLocation(
	string Name,
	Location Location,
	bool IsHome = false)
{
	public string DisplayName => Name;
}
