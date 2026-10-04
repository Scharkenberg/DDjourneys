namespace DDjourneys.Core.Models;

/// <summary>A connection the user named and kept: where from, where to.</summary>
public sealed record SavedRoute(
	string Name,
	Location From,
	Location To)
{
	/// <summary>"From → To", for lists.</summary>
	public string Description =>
		$"{From.Name} → {To.Name}";
}
