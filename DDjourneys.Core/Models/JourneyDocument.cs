namespace DDjourneys.Core.Models;

/// <summary>A printable version of a journey (PDF), as downloaded from the provider.</summary>
public sealed record JourneyDocument(
	byte[] Content,
	string FileName);
