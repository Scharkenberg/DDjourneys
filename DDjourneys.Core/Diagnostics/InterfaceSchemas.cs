using System.Globalization;
using System.Text;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Storage;

namespace DDjourneys.Core.Diagnostics;

/// <summary>Which side of the app an interface is on.</summary>
public enum InterfaceKind
{
	/// <summary>A service somebody else runs: the app has to follow whatever it sends.</summary>
	External,

	/// <summary>A format or protocol between parts of the app, or between the app and its own stored data.</summary>
	Internal,

	/// <summary>What other apps may send to this app and what comes back (docs/EXTERNAL_CONTRACT.md).</summary>
	Contract
}

/// <summary>One interface and the version of it this build speaks.</summary>
/// <param name="Id">Stable short name, used in logs and bug reports.</param>
/// <param name="Kind">Who owns the other end.</param>
/// <param name="Name">What it is, in words.</param>
/// <param name="Version">
/// For an interface with an official version (TRIAS, the contract) that version. For the others the app's own
/// revision of how it reads and writes the interface, <c>r1</c>, <c>r2</c>, ...
/// </param>
/// <param name="Endpoint">Where it is, for external interfaces; empty otherwise.</param>
/// <param name="Notes">What the version covers, in one line.</param>
public sealed record InterfaceSchema(
	string Id,
	InterfaceKind Kind,
	string Name,
	string Version,
	string Endpoint,
	string Notes);

/// <summary>
/// The versioned list of every interface the app depends on, the same idea as the storage schema
/// (<see cref="StorageMigrator"/>): a bug report names the versions it was made with, so a changed answer
/// of a provider, a changed stored format or a caller that speaks another contract version can be told
/// apart from a plain bug.
/// <para>
/// The revision (<c>rN</c>) of an interface whose owner publishes no version is raised by whoever changes how
/// the app reads or writes it: a new DTO shape, a changed mapping rule, a new required field. The list is shown
/// under Settings, Developer options, and written to the log file when logging starts.
/// </para>
/// </summary>
public static class InterfaceSchemas
{
	/// <summary>VVO WebAPI: stop and address search, trips, departures, lines, notices.</summary>
	public const string VvoWebApiUrl = "https://webapi.vvo-online.de";

	/// <summary>TRIAS endpoint of the VVO (efa).</summary>
	public const string TriasUrl = "http://efa.vvo-online.de:8080/std3/trias";

	/// <summary>Live vehicle positions of the TLMS community network.</summary>
	public const string TlmsUrl = "wss://socket.tlm.solutions";

	/// <summary>Open data of the city of Dresden (OGC API Features).</summary>
	public const string OpenDataUrl = "https://kommisdd.dresden.de/net4/public/ogcapi/collections";

	/// <summary>DVB Schutzengel: the service that follows a journey and sends the notices.</summary>
	public const string SchutzengelUrl = "https://m.dvb.de/schutzengel/";

	/// <summary>CARTO basemap styles, loaded by the map page (wwwroot/index.html).</summary>
	public const string CartoUrl = "https://basemaps.cartocdn.com/gl/";

	/// <summary>Raster tiles of the Leaflet engine (first choice; CARTO's raster tiles are the fallback).</summary>
	public const string OsmTileUrl = "https://tile.openstreetmap.org/";

	/// <summary>The DVB Liniennetzplan page, read for the current standard plan (JPG and PDF links).</summary>
	public const string DvbNetworkMapUrl = VvoNetworkMap.PageUrl;

	/// <summary>Park &amp; ride occupancy of the VVO, as an open-data file.</summary>
	public const string VvoPurUrl = "https://www.vvo-online.de/open_data/PuR.JSON";

	/// <summary>Shared bikes of MOBIbike Dresden (Nextbike), as GBFS discovery names them.</summary>
	public const string GbfsUrl = "https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/gbfs.json";

	/// <summary>
	/// Revisions the app keeps itself. Changing how the app reads or writes the interface means raising it here and
	/// adding a line to the history below.
	/// </summary>
	public static class Revision
	{
		// History: r1 = first schema of the interface list (October 2026). Map bridge r2 = the page's diagnostics (log: messages), r3 = the page loads MapLibre only after its own check and reports unsupported:reason, r4 = the page waits for the app (boot, ddBoot start with engine carto|leaflet and force), r5 = per-layer sends (ddMapCall layer/layers with a layer id, the overlay command is gone), polygon payloads (tariff zones) and the reserved marker kinds parking and bike of the Tier 2 city layers. Carto r2 = raster tiles for the Leaflet engine (OpenStreetMap tiles first, CARTO raster as fallback).
		public const int VvoWebApi = 1;
		public const int Schutzengel = 1;
		public const int Tlms = 1;
		public const int OpenData = 1;
		public const int Carto = 2;
		public const int MapBridge = 5;
		public const int OsmTiles = 1;
		public const int DvbNetworkMap = 1;
		public const int VvoPur = 1;
		public const int Gbfs = 1;
		public const int WidgetData = 2;
		public const int WidgetCardWin = 2;
	}

	/// <summary>Interfaces that exist in the core library; the app adds its own (see <c>AppInterfaces</c>).</summary>
	public static IReadOnlyList<InterfaceSchema> Core { get; } =
	[
		new(
			"vvo-webapi", InterfaceKind.External, "VVO WebAPI",
			R(Revision.VvoWebApi), VvoWebApiUrl,
			"unversioned by the provider; DTOs in VvoJsonContext, mapping in Providers/Vvo"),
		new(
			"trias", InterfaceKind.External, "TRIAS (VDV 431-2)",
			"1.4", TriasUrl,
			"requests are written for 1.4 and step down to 1.3, 1.2, 1.1 when the server rejects them"),
		new(
			"schutzengel", InterfaceKind.External, "DVB Schutzengel",
			R(Revision.Schutzengel), SchutzengelUrl,
			"followed journeys; the service numbers its own data (data_version)"),
		new(
			"tlms", InterfaceKind.External, "TLMS live positions",
			R(Revision.Tlms), TlmsUrl,
			"WebSocket, JSON objects, filter sent after connecting"),
		new(
			"opendata-dresden", InterfaceKind.External, "Open data Dresden",
			R(Revision.OpenData), OpenDataUrl,
			"OGC API Features: stop accessibility and service points"),
		new(
			"carto", InterfaceKind.External, "CARTO basemap styles",
			R(Revision.Carto), CartoUrl,
			"vector styles (MapLibre, recoloured by layer role in wwwroot/index.html) and raster tiles (Leaflet fallback source, key if set)"),
		new(
			"osm-tiles", InterfaceKind.External, "OpenStreetMap raster tiles",
			R(Revision.OsmTiles), OsmTileUrl,
			"Leaflet engine, first source; dark mode inverts the picture by CSS; replaced by CARTO raster when it delivers no tile"),
		new(
			"dvb-networkmap", InterfaceKind.External, "DVB Liniennetzplan page",
			R(Revision.DvbNetworkMap), DvbNetworkMapUrl,
			"the page is read for dvb_lnp_n_st_n_jpg.jpg / _pdf.pdf links; the plan is DVB's, downloaded on demand and credited, never bundled"),
		new(
			"vvo-pur", InterfaceKind.External, "VVO park & ride occupancy",
			R(Revision.VvoPur), VvoPurUrl,
			"open-data file of about 56 sites with live free counts (one observed sample, field list unverified); the per-space sensor block is only read for sites without a count of their own"),
		new(
			"gbfs", InterfaceKind.External, "Shared bikes (GBFS)",
			R(Revision.Gbfs), GbfsUrl,
			"GBFS 2.x, discovery-first; station_information cached an hour, station_status at most a minute (the feed's ttl when smaller); Lime reserved for v1.1 (dockless: free_bike_status)"),
		new(
			"storage-lists", InterfaceKind.Internal, "Stored lists (places, routes)",
			Number(StoredJson.CurrentVersion), string.Empty,
			"versioned envelope; unreadable entries are skipped one by one"),
		new(
			"map-bridge", InterfaceKind.Internal, "Map page messages",
			R(Revision.MapBridge), string.Empty,
			"ddMapCall(command, json) or ddMapCall('layer', id, json) for the layer/layers commands, and the raw messages boot, ready, layers, open, tap, view, point, error, auto, log, unsupported"),
		new(
			"widget-data", InterfaceKind.Internal, "Widget settings and snapshot",
			R(Revision.WidgetData), string.Empty,
			"WidgetConfig and WidgetSnapshot as JSON in the preferences (Android) or the application data settings (Windows); the formats are the same"),
		new(
			"widget-card-win", InterfaceKind.Internal, "Windows widget cards",
			R(Revision.WidgetCardWin), string.Empty,
			"the Adaptive Card (1.6) of a Windows widget, built for the size the board reports: the header overridden with the stop or route, literal rows (like the docs' weather example) with drawn line chips (data-URI images), the host's semantic colours for the statuses, refresh and open actions (select action open) and a set-up card"),
		new(
			"contract", InterfaceKind.Contract, "External contract (links, intents, protocol launches)",
			Number(ContractVersion.Current) + " (oldest answered: " + Number(ContractVersion.Oldest) + ")",
			ContractVersion.Scheme + "://",
			"commands, parameters and reply keys; Android action " + ContractVersion.AndroidAction),
		new(
			"contract-journey", InterfaceKind.Contract, "Journey in a pick reply",
			Number(JourneyPayload.SchemaVersion), string.Empty,
			"the JSON document in the journey key of a pick reply")
	];

	/// <summary>One text block for a log header or a bug report.</summary>
	public static string Describe(IEnumerable<InterfaceSchema> schemas, string heading)
	{
		ArgumentNullException.ThrowIfNull(schemas);

		var text = new StringBuilder();
		text.Append(heading).Append('\n');

		foreach (IGrouping<InterfaceKind, InterfaceSchema> group in schemas.GroupBy(item => item.Kind).OrderBy(item => item.Key))
		{
			text.Append(Heading(group.Key)).Append('\n');

			foreach (InterfaceSchema item in group)
			{
				text.Append("  ").Append(item.Id).Append(' ').Append(item.Version);

				if (item.Endpoint.Length > 0)
				{
					text.Append("  ").Append(item.Endpoint);
				}

				text.Append('\n');
			}
		}

		return text.ToString().TrimEnd('\n');
	}

	public static string Heading(InterfaceKind kind) =>
		kind switch
		{
			InterfaceKind.External => "External services",
			InterfaceKind.Internal => "Internal formats",
			_ => "Contract"
		};

	private static string R(int revision) =>
		"r" + Number(revision);

	private static string Number(int value) =>
		value.ToString(CultureInfo.InvariantCulture);
}
