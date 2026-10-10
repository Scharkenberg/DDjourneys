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

	public static IReadOnlyList<InterfaceSchema> All { get; } =
		(List<InterfaceSchema>)[.. InterfaceSchemas.Core, Storage, TrackingEndpoints];

	/// <summary>The build, the system and the interface list as plain text.</summary>
	public static string Report() =>
		InterfaceSchemas.Describe(
			All,
			string.Create(
				CultureInfo.InvariantCulture,
				$"DDjourneys {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}), {DeviceInfo.Platform} {DeviceInfo.VersionString}"));
}
