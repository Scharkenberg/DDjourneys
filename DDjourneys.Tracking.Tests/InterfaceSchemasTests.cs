using DDjourneys.Core.Contract;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Trias;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Storage;

namespace DDjourneys.Tracking.Tests;

/// <summary>The versioned list of interfaces that a bug report quotes.</summary>
public sealed class InterfaceSchemasTests
{
	[Fact]
	public void Every_interface_has_an_unique_id_a_name_and_a_version()
	{
		IReadOnlyList<InterfaceSchema> all = InterfaceSchemas.Core;

		Assert.Equal(all.Count, all.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
		Assert.All(all, item =>
		{
			Assert.Matches("^[a-z][a-z0-9-]*$", item.Id);
			Assert.False(string.IsNullOrWhiteSpace(item.Name));
			Assert.False(string.IsNullOrWhiteSpace(item.Version));
			Assert.False(string.IsNullOrWhiteSpace(item.Notes));
		});
	}

	[Fact]
	public void An_external_service_has_an_address_the_others_do_not()
	{
		foreach (InterfaceSchema item in InterfaceSchemas.Core)
		{
			if (item.Kind == InterfaceKind.External)
			{
				Assert.True(Uri.TryCreate(item.Endpoint, UriKind.Absolute, out Uri? uri), item.Id);
				Assert.Contains(uri!.Scheme, new[] { "http", "https", "wss" });
			}
			else if (item.Kind == InterfaceKind.Internal)
			{
				Assert.Equal(string.Empty, item.Endpoint);
			}
		}
	}

	[Fact]
	public void The_app_owned_revisions_read_r_and_a_number()
	{
		foreach (string id in new[] { "vvo-webapi", "schutzengel", "tlms", "opendata-dresden", "carto", "map-bridge", "widget-data" })
		{
			Assert.Matches("^r[1-9][0-9]*$", InterfaceSchemas.Core.Single(item => item.Id == id).Version);
		}
	}

	[Fact]
	public void The_official_versions_are_the_ones_the_code_uses()
	{
		Assert.Equal("1.4", InterfaceSchemas.Core.Single(item => item.Id == "trias").Version);
		Assert.Equal(StoredJson.CurrentVersion.ToString(), InterfaceSchemas.Core.Single(item => item.Id == "storage-lists").Version);
		Assert.StartsWith(ContractVersion.Current.ToString(), InterfaceSchemas.Core.Single(item => item.Id == "contract").Version);
		Assert.Equal(JourneyPayload.SchemaVersion.ToString(), InterfaceSchemas.Core.Single(item => item.Id == "contract-journey").Version);
	}

	[Fact]
	public void The_description_names_every_interface_under_its_kind()
	{
		string text = InterfaceSchemas.Describe(InterfaceSchemas.Core, "DDjourneys test");

		Assert.StartsWith("DDjourneys test\n", text, StringComparison.Ordinal);
		Assert.Contains("External services", text, StringComparison.Ordinal);
		Assert.Contains("Internal formats", text, StringComparison.Ordinal);
		Assert.Contains("Contract", text, StringComparison.Ordinal);
		Assert.All(InterfaceSchemas.Core, item => Assert.Contains(item.Id + " " + item.Version, text, StringComparison.Ordinal));
		Assert.DoesNotContain('\r', text);
		Assert.False(text.EndsWith('\n'));
	}

	[Fact]
	public void The_log_starts_with_the_build_and_the_interfaces()
	{
		string path = Path.Combine(Path.GetTempPath(), $"ddj-log-{Guid.NewGuid():N}.txt");

		try
		{
			DiagnosticLog.FilePath = path;
			DiagnosticLog.StartInfo = () => "DDjourneys 9.9 (1)\nExternal services\n  trias 1.4";
			DiagnosticLog.Enabled = false;
			DiagnosticLog.Enabled = true;

			string log = File.ReadAllText(path);

			Assert.Contains("[Log] started", log, StringComparison.Ordinal);
			Assert.Contains("[Info] DDjourneys 9.9 (1)", log, StringComparison.Ordinal);
			Assert.Contains("[Info]   trias 1.4", log, StringComparison.Ordinal);
		}
		finally
		{
			DiagnosticLog.Enabled = false;
			DiagnosticLog.StartInfo = null;
			DiagnosticLog.FilePath = null;
			File.Delete(path);
		}
	}

	[Fact]
	public void A_failing_start_info_does_not_stop_the_log()
	{
		string path = Path.Combine(Path.GetTempPath(), $"ddj-log-{Guid.NewGuid():N}.txt");

		try
		{
			DiagnosticLog.FilePath = path;
			DiagnosticLog.StartInfo = () => throw new InvalidOperationException("no device info");
			DiagnosticLog.Enabled = false;
			DiagnosticLog.Enabled = true;

			string log = File.ReadAllText(path);

			Assert.Contains("[Log] started", log, StringComparison.Ordinal);
			Assert.Contains("no device info", log, StringComparison.Ordinal);
		}
		finally
		{
			DiagnosticLog.Enabled = false;
			DiagnosticLog.StartInfo = null;
			DiagnosticLog.FilePath = null;
			File.Delete(path);
		}
	}
}

/// <summary>What each provider can do with a preference: a control for something it ignores is not shown.</summary>
public sealed class ProviderCapabilityTests
{
	[Fact]
	public void The_vvo_web_api_walks_to_stops_and_filters_supplements_but_has_no_optimiser()
	{
		ProviderInfo vvo = VvoProviderInfo.Value;

		Assert.True(vvo.Supports(ProviderCapabilities.WalkToStops));
		Assert.True(vvo.Supports(ProviderCapabilities.SupplementFilter));
		Assert.False(vvo.Supports(ProviderCapabilities.RouteOptimisation));
		Assert.False(vvo.Supports(ProviderCapabilities.PassengerFares));
	}

	[Fact]
	public void Trias_optimises_and_prices_by_passenger_but_ignores_the_vvo_only_options()
	{
		ProviderInfo trias = TriasProviderInfo.Value;

		Assert.True(trias.Supports(ProviderCapabilities.RouteOptimisation));
		Assert.True(trias.Supports(ProviderCapabilities.PassengerFares));
		Assert.False(trias.Supports(ProviderCapabilities.WalkToStops));
		Assert.False(trias.Supports(ProviderCapabilities.SupplementFilter));
	}

	[Fact]
	public void The_capability_flags_do_not_overlap()
	{
		ProviderCapabilities[] flags = [.. Enum.GetValues<ProviderCapabilities>().Where(flag => flag != ProviderCapabilities.None)];

		Assert.All(flags, flag => Assert.Equal(1, int.PopCount((int)flag)));
		Assert.Equal(flags.Length, flags.Distinct().Count());
	}
}
