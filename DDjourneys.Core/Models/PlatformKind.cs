namespace DDjourneys.Core.Models;

/// <summary>
/// What a platform name designates. VVO distinguishes the two: buses and trams
/// stop at a "Steig" (<see cref="Platform"/>), railways use a "Gleis" (<see cref="Railtrack"/>).
/// The name alone ("3") cannot tell them apart.
/// </summary>
public enum PlatformKind
{
	/// <summary>The provider did not say (or the type is not recognised).</summary>
	Unknown = 0,

	/// <summary>Stop position / platform (German: Steig).</summary>
	Platform,

	/// <summary>Railway track (German: Gleis).</summary>
	Railtrack
}
