using System.Globalization;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// Every interface the app depends on with the version it speaks: the core list (<see cref="InterfaceSchemas"/>)
/// plus what only the app knows. Shown under Developer options and written at the top of the log file.
/// </summary>
public static class AppInterfaces
{
	/// <summary>The newest storage migration; the same number is kept in the preferences as <c>storage.version</c>.</summary>
	public static InterfaceSchema Storage { get; } =
		new(
			"storage", InterfaceKind.Internal, "On-device storage (preferences)",
			AppStorage.Migrations.Max(step => step.Version).ToString(CultureInfo.InvariantCulture),
			string.Empty,
			string.Join(", ", AppStorage.Migrations.OrderBy(step => step.Version).Select(step => $"{step.Version} {step.Name}")));

	/// <summary>The ends of followed journeys (key <c>tracking.endpoints</c>): from/via/to per plan, for a recovery search.</summary>
	public static InterfaceSchema TrackingEndpoints { get; } =
			new(
				"tracking-endpoints", InterfaceKind.Internal, "Stored followed-journey endpoints",
				"r1",
				"tracking.endpoints",
				"StoredJson envelope, one entry per plan: PlanId, From/To/Via as location objects (absent when unknown).");

	/// <summary>The lines watched for disruptions (key <c>watchedlines.list</c>): provider-scoped, the same-line rule applies.</summary>
	public static InterfaceSchema WatchedLines { get; } =
			new(
				"watched-lines", InterfaceKind.Internal, "Stored watched lines",
				"r1",
				"watchedlines.list",
				"StoredJson envelope, one entry per watched line: ProviderId, Name. The stars on the departures line rows and the run page header write it.");

	/// <summary>The last-seen disruption fingerprint per watched line (key <c>watchedlines.state</c>): bookkeeping of the change check, never passenger data — dropping it costs one suppressed comparison, never a missed notification.</summary>
	public static InterfaceSchema WatchedLineState { get; } =
			new(
				"watched-line-state", InterfaceKind.Internal, "Stored watched-line disruption fingerprints",
				"r1",
				"watchedlines.state",
				"StoredJson envelope, one entry per provider and line: ProviderId, Line (the board filter's line key), Fingerprint (FNV-1a over the sorted id|published stamps of the line's active disruptions).");

	public static IReadOnlyList<InterfaceSchema> All { get; } =
		(List<InterfaceSchema>)[.. InterfaceSchemas.Core, Storage, TrackingEndpoints, WatchedLines, WatchedLineState];

	/// <summary>The build, the system and the interface list as plain text.</summary>
	public static string Report() =>
		InterfaceSchemas.Describe(
			All,
			string.Create(
				CultureInfo.InvariantCulture,
				$"DDjourneys {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}), {DeviceInfo.Platform} {DeviceInfo.VersionString}"));
}
