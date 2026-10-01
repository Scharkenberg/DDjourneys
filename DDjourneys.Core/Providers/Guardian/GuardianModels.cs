namespace DDjourneys.Core.Providers.Guardian;

public sealed class GuardianJourney
{
	public required IReadOnlyList<GuardianEpisode> Episodes { get; init; }
		= [];
}

public sealed class GuardianEpisode
{
	public required string Type { get; init; }

	public required string Id { get; init; }

	public required GuardianMot Mot { get; init; }

	public required GuardianStation From { get; init; }

	public required GuardianStation To { get; init; }

	public required IReadOnlyList<GuardianStation> AllStations { get; init; }

	public required IReadOnlyList<GuardianCoordinates> Polyline { get; init; }

	public long? DurationSeconds { get; init; }
}

public sealed class GuardianMot
{
	public required string Type { get; init; }

	public required string Name { get; init; }

	public required string Direction { get; init; }
}

public sealed class GuardianStation
{
	public required string Name { get; init; }

	public required string Id { get; init; }

	public required GuardianCoordinates Coords { get; init; }

	public long ScheduledTime { get; init; }

	public GuardianPlatform? Platform { get; init; }
}

public sealed class GuardianCoordinates
{
	public double Lat { get; init; }

	public double Lon { get; init; }

	public required string Projection { get; init; }
}

public sealed class GuardianPlatform
{
	public required string Type { get; init; }

	public required string Name { get; init; }
}