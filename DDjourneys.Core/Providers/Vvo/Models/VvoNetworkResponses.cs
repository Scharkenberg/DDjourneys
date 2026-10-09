using System.Text.Json.Serialization;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Models;

public sealed class VvoDepartureResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	[JsonPropertyName("Name")]
	public string? Name { get; init; }

	[JsonPropertyName("Place")]
	public string? Place { get; init; }

	[JsonPropertyName("Departures")]
	public IReadOnlyList<VvoDeparture> Departures { get; init; } = [];
}


public sealed class VvoDeparture
{
	[JsonPropertyName("Id")]
	public string? Id { get; init; }

	[JsonPropertyName("DlId")]
	public string? DlId { get; init; }

	[JsonPropertyName("LineName")]
	public string? LineName { get; init; }

	[JsonPropertyName("Direction")]
	public string? Direction { get; init; }

	[JsonPropertyName("Platform")]
	public VvoPlatform? Platform { get; init; }

	[JsonPropertyName("Mot")]
	public string? Mot { get; init; }

	[JsonPropertyName("RealTime")]
	public DateTimeOffset? RealTime { get; init; }

	[JsonPropertyName("ScheduledTime")]
	public DateTimeOffset? ScheduledTime { get; init; }

	[JsonPropertyName("State")]
	public string? State { get; init; }

	[JsonPropertyName("RouteChanges")]
	public IReadOnlyList<string> RouteChanges { get; init; } = [];

	[JsonPropertyName("Diva")]
	public VvoDiva? Diva { get; init; }

	[JsonPropertyName("Occupancy")]
	public string? Occupancy { get; init; }
}


public sealed class VvoRunResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	[JsonPropertyName("Stops")]
	public IReadOnlyList<VvoRunStop> Stops { get; init; } = [];

	[JsonPropertyName("MapData")]
	[JsonConverter(typeof(VvoMapDataConverter))]
	public IReadOnlyList<string> MapData { get; init; } = [];
}


public sealed class VvoRunStop
{
	[JsonPropertyName("Id")]
	public string? Id { get; init; }

	[JsonPropertyName("Place")]
	public string? Place { get; init; }

	[JsonPropertyName("Name")]
	public string? Name { get; init; }

	/// <summary>Previous, Current, Next, Onward.</summary>
	[JsonPropertyName("Position")]
	public string? Position { get; init; }

	[JsonPropertyName("Platform")]
	public VvoPlatform? Platform { get; init; }

	[JsonPropertyName("Time")]
	public DateTimeOffset? Time { get; init; }

	[JsonPropertyName("RealTime")]
	public DateTimeOffset? RealTime { get; init; }

	[JsonPropertyName("State")]
	public string? State { get; init; }

	[JsonPropertyName("Latitude")]
	public double Latitude { get; init; }

	[JsonPropertyName("Longitude")]
	public double Longitude { get; init; }

	[JsonPropertyName("Occupancy")]
	public string? Occupancy { get; init; }
}


public sealed class VvoRouteChangesResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	[JsonPropertyName("Changes")]
	public IReadOnlyList<VvoRouteChange> Changes { get; init; } = [];

	[JsonPropertyName("Banners")]
	public IReadOnlyList<VvoBanner> Banners { get; init; } = [];

	[JsonPropertyName("Lines")]
	public IReadOnlyList<VvoChangedLine> Lines { get; init; } = [];
}


public sealed class VvoRouteChange
{
	[JsonPropertyName("Id")]
	public string? Id { get; init; }

	[JsonPropertyName("Title")]
	public string? Title { get; init; }

	/// <summary>HTML.</summary>
	[JsonPropertyName("Description")]
	public string? Description { get; init; }

	/// <summary>"Scheduled" for planned work.</summary>
	[JsonPropertyName("Type")]
	public string? Type { get; init; }

	[JsonPropertyName("TripRequestInclude")]
	public bool TripRequestInclude { get; init; }

	[JsonPropertyName("PublishDate")]
	public DateTimeOffset? PublishDate { get; init; }

	[JsonPropertyName("LineIds")]
	public IReadOnlyList<string> LineIds { get; init; } = [];

	[JsonPropertyName("ValidityPeriods")]
	public IReadOnlyList<VvoValidityPeriod> ValidityPeriods { get; init; } = [];
}


public sealed class VvoValidityPeriod
{
	[JsonPropertyName("Begin")]
	public DateTimeOffset? Begin { get; init; }

	[JsonPropertyName("End")]
	public DateTimeOffset? End { get; init; }
}


public sealed class VvoBanner
{
	[JsonPropertyName("Title")]
	public string? Title { get; init; }

	[JsonPropertyName("Description")]
	public string? Description { get; init; }

	[JsonPropertyName("Type")]
	public string? Type { get; init; }

	[JsonPropertyName("ModifiedTime")]
	public DateTimeOffset? ModifiedTime { get; init; }

	[JsonPropertyName("TripRequestInclude")]
	public bool TripRequestInclude { get; init; }
}


public sealed class VvoChangedLine
{
	[JsonPropertyName("Id")]
	public string? Id { get; init; }

	[JsonPropertyName("Name")]
	public string? Name { get; init; }

	[JsonPropertyName("Mot")]
	public string? Mot { get; init; }

	[JsonPropertyName("TransportationCompany")]
	public string? TransportationCompany { get; init; }

	[JsonPropertyName("Divas")]
	public IReadOnlyList<VvoDiva> Divas { get; init; } = [];
}


public sealed class VvoChangedLinesResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	[JsonPropertyName("Lines")]
	public IReadOnlyList<VvoChangedLine> Lines { get; init; } = [];
}


public sealed class VvoStopLinesResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	[JsonPropertyName("Lines")]
	public IReadOnlyList<VvoStopLine> Lines { get; init; } = [];
}


public sealed class VvoStopLine
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }

	[JsonPropertyName("Mot")]
	public string? Mot { get; init; }

	[JsonPropertyName("Changes")]
	public IReadOnlyList<string> Changes { get; init; } = [];

	[JsonPropertyName("Directions")]
	public IReadOnlyList<VvoStopLineDirection> Directions { get; init; } = [];

	[JsonPropertyName("Diva")]
	public VvoDiva? Diva { get; init; }
}


public sealed class VvoStopLineDirection
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }

	[JsonPropertyName("TimeTables")]
	public IReadOnlyList<VvoTimeTable> TimeTables { get; init; } = [];
}


public sealed class VvoTimeTable
{
	[JsonPropertyName("Id")]
	public string? Id { get; init; }

	[JsonPropertyName("Name")]
	public string? Name { get; init; }
}


public sealed class VvoMapPinsResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	/// <summary>Pipe-delimited like PointFinder entries: id||place|name|northing|easting|extra.</summary>
	[JsonPropertyName("Pins")]
	public IReadOnlyList<string> Pins { get; init; } = [];
}


public sealed class VvoMapPolygonsResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }

	/// <summary>"zone|name|#colour|centreNorthing|centreEasting|northing|easting|...".</summary>
	[JsonPropertyName("Polygons")]
	public IReadOnlyList<string> Polygons { get; init; } = [];

	[JsonPropertyName("ExpirationTime")]
	public DateTimeOffset? ExpirationTime { get; init; }
}
