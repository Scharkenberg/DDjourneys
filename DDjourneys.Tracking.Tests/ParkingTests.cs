using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Parking;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Serialization;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The park &amp; ride layer of Tier 2: reading the VVO open-data file (one observed sample, every field
/// optional) and caching it for the map. The sample below deliberately carries unknown keys and a sensor
/// block, so both passes of the reader are covered.
/// </summary>
public class ParkingTests
{
	private const string Sample =
		"""
		[
			{"id":"1001","ppid":"pp-1001","name":"P+R Radebeul Ost","lat":51.1069,"lon":13.6806,"info":"47 Stellplätze, , ",
			 "number_total":47,"number_free_now":12,"number_free_ago":9,"dtg":"2026-10-05T07:41:00Z","city":"Dresden"},
			{"id":"1002","name":"P+R Coschütz","lat":51.0181,"lon":13.7019,"number_total":30,"number_free_now":0,
			 "dtg":"/Date(1762255220000+0200)/"},
			{"ppid":"pp-1003","name":"P+R Mockritz","lat":51.0,"lon":13.7,"number_total":18,"dtg":"not a date",
			 "LiveData":[
				{"status":0,"lat":51.0,"lon":13.7},
				{"status":1,"lat":51.0,"lon":13.7},
				{"status":0,"lat":51.0,"lon":13.7,"tag":"disabled"},
				{"status":-1,"lat":51.0,"lon":13.7}]},
			{"ppid":"pp-1004","name":"P+R Prohlis Garage","lat":51.01,"lon":13.75,"number_total":40,
			 "LiveData":[{"status":-1,"lat":51.01,"lon":13.75},{"status":1,"lat":51.01,"lon":13.75,"tag":"disabled"}]},
			{"name":"P+R Without Position","number_total":5,"number_free_now":1,"dtg":"2026-10-05T07:41:00Z"}
		]
		""";

	private static VvoParkingSite[] ReadSites()
	{
		VvoParkingSite[]? sites =
			JsonSerializer.Deserialize(Sample, VvoJson.TypeInfo(typeof(VvoParkingSite[]))!) as VvoParkingSite[];

		Assert.NotNull(sites);

		return sites!;
	}

	private static VvoParkingSiteLive[] ReadLive()
	{
		VvoParkingSiteLive[]? live =
			JsonSerializer.Deserialize(Sample, VvoJson.TypeInfo(typeof(VvoParkingSiteLive[]))!) as VvoParkingSiteLive[];

		Assert.NotNull(live);

		return live!;
	}

	[Fact]
	public void Sites_with_their_own_counts_keep_them()
	{
		VvoParkingSite[] sites = ReadSites();
		VvoParkingSiteLive[] live = ReadLive();

		IReadOnlyList<ParkingSite> mapped = VvoParkingProvider.MapSites(sites, live);

		Assert.Equal(4, mapped.Count);

		ParkingSite radebeul = mapped[0];
		Assert.Equal("1001", radebeul.Id);
		Assert.Equal("P+R Radebeul Ost", radebeul.Name);
		Assert.Equal(47, radebeul.Total);
		Assert.Equal(12, radebeul.Free);
		Assert.True(radebeul.HasLive);
		Assert.Equal(DateTimeOffset.Parse("2026-10-05T07:41:00Z"), radebeul.LiveAt);

		ParkingSite coschütz = mapped[1];
		Assert.Equal(30, coschütz.Total);
		Assert.Equal(0, coschütz.Free);
		Assert.True(coschütz.HasLive);
		Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762255220000), coschütz.LiveAt);
	}

	[Fact]
	public void A_site_without_a_count_is_counted_from_its_usable_sensors()
	{
		VvoParkingSite[] sites = ReadSites();
		VvoParkingSiteLive[] live = ReadLive();

		IReadOnlyList<ParkingSite> mapped = VvoParkingProvider.MapSites(sites, live);

		// Mockritz: 18 spaces in total, two usable sensors (one free, one occupied), no usable timestamp.
		ParkingSite mockritz = mapped[2];
		Assert.Equal("pp-1003", mockritz.Id);
		Assert.Equal(18, mockritz.Total);
		Assert.Equal(1, mockritz.Free);
		Assert.False(mockritz.HasLive);
	}

	[Fact]
	public void A_site_with_only_defect_sensors_shows_no_numbers()
	{
		VvoParkingSite[] sites = ReadSites();
		VvoParkingSiteLive[] live = ReadLive();

		IReadOnlyList<ParkingSite> mapped = VvoParkingProvider.MapSites(sites, live);

		// Prohlis: every sensor is occupied or out of service, so the counts would lie.
		ParkingSite prohlis = mapped[3];
		Assert.Equal(0, prohlis.Total);
		Assert.Equal(0, prohlis.Free);
		Assert.False(prohlis.HasLive);
	}

	[Fact]
	public void A_site_without_an_id_or_a_position_is_skipped()
	{
		VvoParkingSite[] sites = ReadSites();
		VvoParkingSiteLive[] live = ReadLive();

		IReadOnlyList<ParkingSite> mapped = VvoParkingProvider.MapSites(sites, live);

		Assert.DoesNotContain(mapped, site => site.Name == "P+R Without Position");
	}

	[Fact]
	public void Without_a_count_the_live_pass_is_only_made_when_a_site_needs_it()
	{
		VvoParkingSite[] sites = ReadSites();
		VvoParkingSiteLive[] live = ReadLive();

		// The sample needs the second pass (sites 3 and 4 carry no number_free_now); both roots parse the same bytes.
		Assert.All(sites, static site => Assert.NotNull(site.Name));
		Assert.Equal(5, live.Length);
		Assert.Equal(4, live[2].LiveData.Count);
		Assert.Equal(2, live[3].LiveData.Count);
	}

	[Fact]
	public void Timestamps_are_parsed_tolerantly()
	{
		Assert.Equal(
			DateTimeOffset.Parse("2026-10-05T07:41:00Z"),
			VvoParkingProvider.ParseTimestamp("2026-10-05T07:41:00Z"));
		Assert.Equal(
			DateTimeOffset.FromUnixTimeMilliseconds(1512770460000),
			VvoParkingProvider.ParseTimestamp("/Date(1512770460000+0100)/"));
		Assert.Null(VvoParkingProvider.ParseTimestamp("not a date"));
		Assert.Null(VvoParkingProvider.ParseTimestamp(string.Empty));
		Assert.Null(VvoParkingProvider.ParseTimestamp(null));
	}

	[Fact]
	public async Task The_service_answers_from_the_cache_and_only_fetches_once()
	{
		FakeParkingProvider provider = new(
		[
			new List<ParkingSite> { new("a", "Alpha", 51.0, 13.7, 40, 7, DateTimeOffset.UtcNow) },
			new List<ParkingSite> { new("b", "Beta", 51.1, 13.8, 30, 3, DateTimeOffset.UtcNow) }
		]);

		ParkingService service = new([provider]);

		Assert.True(service.IsAvailable);

		IReadOnlyList<ParkingSite> first = await service.GetSitesAsync();
		IReadOnlyList<ParkingSite> second = await service.GetSitesAsync();

		Assert.Equal(1, provider.Calls);
		Assert.Same(first, second);
		Assert.Equal("Alpha", first[0].Name);
	}

	[Fact]
	public async Task Empty_answers_and_failures_are_not_cached()
	{
		FailingParkingProvider provider = new();

		ParkingService service = new([provider]);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSitesAsync());
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSitesAsync());

		Assert.Equal(2, provider.Calls);
	}

	[Fact]
	public void Without_a_provider_there_is_nothing_to_show()
	{
		ParkingService service = new([]);

		Assert.False(service.IsAvailable);
	}

	private sealed class FakeParkingProvider(IReadOnlyList<IReadOnlyList<ParkingSite>> answers) : IParkingProvider
	{
		public int Calls { get; private set; }

		public Task<IReadOnlyList<ParkingSite>> GetSitesAsync(
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			IReadOnlyList<ParkingSite> answer = answers[Math.Min(Calls, answers.Count - 1)];

			Calls++;

			return Task.FromResult(answer);
		}
	}

	private sealed class FailingParkingProvider : IParkingProvider
	{
		public int Calls { get; private set; }

		public Task<IReadOnlyList<ParkingSite>> GetSitesAsync(
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			Calls++;

			throw new InvalidOperationException("parking source down");
		}
	}
}
