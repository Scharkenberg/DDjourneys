using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;

namespace DDjourneys.Core.Providers.Vvo.Serialization;

/// <summary>
/// Source-generated JSON metadata for every VVO WebAPI request and response: no runtime reflection,
/// so it survives trimming and AOT on Android.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNameCaseInsensitive = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	Converters = new[] { typeof(VvoDateTimeOffsetConverter) })]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(VvoPointResponse))]
[JsonSerializable(typeof(VvoTripResponse))]
[JsonSerializable(typeof(VvoDepartureResponse))]
[JsonSerializable(typeof(VvoRunResponse))]
[JsonSerializable(typeof(VvoRouteChangesResponse))]
[JsonSerializable(typeof(VvoChangedLinesResponse))]
[JsonSerializable(typeof(VvoStopLinesResponse))]
[JsonSerializable(typeof(VvoMapPinsResponse))]
[JsonSerializable(typeof(VvoMapPolygonsResponse))]
[JsonSerializable(typeof(VvoRoute))]
[JsonSerializable(typeof(VvoPartialRoute))]
[JsonSerializable(typeof(VvoStop))]
[JsonSerializable(typeof(VvoMot))]
[JsonSerializable(typeof(VvoPlatform))]
[JsonSerializable(typeof(VvoDiva))]
[JsonSerializable(typeof(VvoTripRequest))]
[JsonSerializable(typeof(VvoPrevNextRequest))]
[JsonSerializable(typeof(VvoStandardSettings))]
[JsonSerializable(typeof(VvoMobilitySettings))]
[JsonSerializable(typeof(VvoDepartureRequest))]
[JsonSerializable(typeof(VvoDepartureRunRequest))]
[JsonSerializable(typeof(VvoPrevNextMoveRequest))]
[JsonSerializable(typeof(VvoRouteChangesRequest))]
[JsonSerializable(typeof(VvoChangedLinesRequest))]
[JsonSerializable(typeof(VvoStopLinesRequest))]
[JsonSerializable(typeof(VvoMapPinsRequest))]
[JsonSerializable(typeof(VvoMapPolygonsRequest))]
internal sealed partial class VvoJsonContext : JsonSerializerContext
{
}


/// <summary>Source-generated metadata of the VVO DTOs for code outside this assembly.</summary>
public static class VvoJson
{
	/// <summary>Metadata of a VVO DTO type; null for any other type.</summary>
	public static JsonTypeInfo? TypeInfo(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		return VvoJsonContext.Default.GetTypeInfo(type);
	}
}
