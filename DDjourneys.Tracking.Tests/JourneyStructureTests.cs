using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Tracking;
using DDjourneys.Tracking.Schutzengel;

namespace DDjourneys.Tracking.Tests;

/// <summary>Journey identity, the single ride/transfer definition and the extracted sub-mappers.</summary>
public sealed class JourneyStructureTests
{
	private static readonly string[] DresdenAndPirna = ["Dresden", "Pirna"];
	private static readonly string[] TwoEmptyZones = ["", ""];

	private static readonly DateTimeOffset Base = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

	// ----- Ride versus transfer -----

	[Theory]
	[InlineData(TransitMode.Tram, true)]
	[InlineData(TransitMode.Bus, true)]
	[InlineData(TransitMode.RegionalTrain, true)]
	[InlineData(TransitMode.Unknown, true)]
	[InlineData(TransitMode.Walk, false)]
	[InlineData(TransitMode.Taxi, false)]
	[InlineData(TransitMode.OnDemand, false)]
	public void Only_scheduled_vehicles_are_rides(TransitMode mode, bool expected)
	{
		Assert.Equal(expected, mode.IsRide());
		Assert.Equal(expected, Leg(mode, 0, 10).IsRide);
	}

	[Fact]
	public void Transfers_are_rides_minus_one_whatever_lies_between()
	{
		Journey journey =
			Build(
				Leg(TransitMode.Tram, 0, 10, "11"),
				Leg(TransitMode.Walk, 10, 13),
				Leg(TransitMode.Taxi, 13, 20),
				Leg(TransitMode.Bus, 20, 30, "62"));

		Assert.Equal(2, journey.Rides.Count());
		Assert.Equal(1, journey.TransferCount);

		Assert.Equal(0, Build(Leg(TransitMode.Walk, 0, 5)).TransferCount);
		Assert.Equal(0, Build(Leg(TransitMode.Tram, 0, 5, "3")).TransferCount);
	}

	// ----- Identity -----

	[Fact]
	public void Identity_key_equals_the_fingerprint()
	{
		Journey journey = Build(Leg(TransitMode.Tram, 0, 10, " 11 "), Leg(TransitMode.Bus, 12, 30, "62"));

		JourneyIdentity? identity = JourneyIdentity.Of(journey);

		Assert.NotNull(identity);
		Assert.Equal(JourneyFingerprint.Of(journey), identity.Key);
		Assert.Equal(
			JourneyIdentity.Compose(
				Base.ToUnixTimeMilliseconds(),
				Base.AddMinutes(30).ToUnixTimeMilliseconds(),
				["11", "62"]),
			identity.Key);
		Assert.Equal(2, identity.Rides.Count);
	}

	[Fact]
	public void Identity_ignores_realtime()
	{
		Journey planned = Build(Leg(TransitMode.Tram, 0, 10, "11"));
		Journey late = Build(Leg(TransitMode.Tram, 0, 10, "11", delayMinutes: 7));

		Assert.True(JourneyIdentity.Of(planned)!.IsSameAs(JourneyIdentity.Of(late)));
	}

	[Fact]
	public void Identity_needs_agreement_on_what_both_sides_know()
	{
		JourneyIdentity vvo = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11"), "vvo"))!;
		JourneyIdentity other = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11"), "other"))!;
		JourneyIdentity unknown = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11"), ""))!;

		Assert.False(vvo.IsSameAs(other));
		Assert.True(vvo.IsSameAs(unknown));
		Assert.True(unknown.IsSameAs(other));
		Assert.False(vvo.IsSameAs(null));

		JourneyIdentity otherLine = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "12"), "vvo"))!;
		Assert.False(vvo.IsSameAs(otherLine));
	}

	[Fact]
	public void Identity_compares_stop_keys_only_when_both_have_them()
	{
		JourneyIdentity a = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11", from: "1", to: "2"), "vvo"))!;
		JourneyIdentity b = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11", from: "1", to: "3"), "vvo"))!;
		JourneyIdentity c = JourneyIdentity.Of(Build(Leg(TransitMode.Tram, 0, 10, "11", from: "", to: ""), "vvo"))!;

		Assert.Equal("vvo:1", a.OriginKey);
		Assert.Equal("vvo:2", a.DestinationKey);
		Assert.False(a.IsSameAs(b));
		Assert.True(a.IsSameAs(c));
		Assert.Null(c.OriginKey);
	}

	// ----- VVO sub-mappers -----

	[Theory]
	[InlineData("Footpath", true)]
	[InlineData("StayForConnection", true)]
	[InlineData("MobilityElevatorUp", true)]
	[InlineData("Tram", false)]
	[InlineData(null, false)]
	public void Vvo_transfer_instructions_are_recognised(string? type, bool expected)
	{
		var route = new VvoPartialRoute { Mot = type is null ? null : new VvoMot { Type = type } };

		Assert.Equal(expected, VvoTransferMapper.IsTransfer(route));
	}

	[Fact]
	public void A_guaranteed_connection_with_stops_is_not_a_ride_in_the_plan()
	{
		var stops = new List<VvoStop> { new() { Name = "A" }, new() { Name = "B" } };

		var tram = new VvoPartialRoute { Mot = new VvoMot { Type = "Tram", Name = "7" }, RegularStops = stops };
		var stay = new VvoPartialRoute { Mot = new VvoMot { Type = "StayForConnection", Name = "gesicherter Anschluss" }, RegularStops = stops };

		Assert.True(SchutzengelTransitionMapper.IsMovement(tram));
		Assert.False(SchutzengelTransitionMapper.IsMovement(stay));
	}

	[Fact]
	public void Vvo_modes_are_mapped()
	{
		Assert.Equal(TransitMode.Walk, VvoModeMapper.MapMode(new VvoMot { Type = "Footpath" }));
		Assert.Equal(TransitMode.Tram, VvoModeMapper.MapMode(new VvoMot { Type = "Tram", Name = "11" }));
		Assert.Equal(TransitMode.SuburbanRail, VvoModeMapper.MapMode(new VvoMot { Type = "RapidTransit" }));
		Assert.Equal(TransitMode.Unknown, VvoModeMapper.MapMode(null));
		Assert.Equal(OccupancyLevel.High, VvoModeMapper.MapOccupancy(" StandingOnly "));
		Assert.Equal(OccupancyLevel.Unknown, VvoModeMapper.MapOccupancy("?"));
	}

	[Fact]
	public void Vvo_transfer_kind_follows_the_instruction_type()
	{
		Assert.Equal(
			TransferKind.Walk,
			VvoTransferMapper.DetermineTransferKind(Route("Footpath"), null, null));
		Assert.Equal(
			TransferKind.Accessibility,
			VvoTransferMapper.DetermineTransferKind(Route("MobilityRampUp"), null, null));
		Assert.Equal(
			TransferKind.Waiting,
			VvoTransferMapper.DetermineTransferKind(Route("StayForConnection"), null, null));
	}

	[Fact]
	public void Vvo_path_without_map_data_is_empty()
	{
		Assert.Empty(
			VvoPathMapper.MapPath(
				new VvoRoute(),
				new VvoPartialRoute { MapDataIndex = 3 }));
	}

	// ----- Schutzengel sub-mappers -----

	[Fact]
	public void Schutzengel_wire_codes_are_stable()
	{
		Assert.Equal(1, SchutzengelWireCodes.MotType(TransitMode.Tram));
		Assert.Equal(19, SchutzengelWireCodes.MotType(TransitMode.Walk));
		Assert.Equal(0, SchutzengelWireCodes.MotType(TransitMode.Unknown));
		Assert.Equal(1, SchutzengelWireCodes.TransitionType("Footpath"));
		Assert.Equal(12, SchutzengelWireCodes.TransitionType("StayForConnection"));
		Assert.Equal(2, SchutzengelWireCodes.TrafficNodeType("Poi"));
		Assert.Equal(1, SchutzengelWireCodes.TrafficNodeType(null));
		Assert.Equal(2, SchutzengelWireCodes.PlatformType("Railtrack"));
		Assert.Equal(4, SchutzengelWireCodes.Occupancy("StandingOnly"));
	}

	[Fact]
	public void Schutzengel_tariff_fields_are_parsed()
	{
		Assert.Equal(290, SchutzengelTariffMapper.ParsePrice("2,90"));
		Assert.Equal(0, SchutzengelTariffMapper.ParsePrice(null));
		Assert.Equal(DresdenAndPirna, SchutzengelTariffMapper.ZoneStrings("TZ Dresden (10), Pirna(20)"));
		Assert.Equal(TwoEmptyZones, SchutzengelTariffMapper.ZoneStrings(null));
	}

	[Fact]
	public void Schutzengel_vehicle_change_duration_is_the_gap_in_whole_minutes()
	{
		JourneyLeg first = Leg(TransitMode.Tram, 0, 10, "11");
		JourneyLeg second = Leg(TransitMode.Bus, 14, 30, "62");

		Assert.Equal(4, SchutzengelTransitionMapper.SyntheticVehicleChangeDuration(first, second));
		Assert.Equal(0, SchutzengelTransitionMapper.SyntheticVehicleChangeDuration(second, first));
	}

	// ----- Helpers -----

	private static VvoPartialRoute Route(string type) =>
		new() { Mot = new VvoMot { Type = type } };

	private static Journey Build(params JourneyLeg[] legs) => Build(legs, "vvo");

	private static Journey Build(JourneyLeg leg, string providerId) => Build([leg], providerId);

	private static Journey Build(JourneyLeg[] legs, string providerId) =>
		new()
		{
			From = Stop("a", providerId),
			To = Stop("b", providerId),
			ProviderId = providerId,
			Legs = legs
		};

	private static JourneyLeg Leg(
		TransitMode mode,
		int startMinute,
		int endMinute,
		string? line = null,
		int delayMinutes = 0,
		string from = "a",
		string to = "b")
	{
		string providerId = from.Length == 0 ? string.Empty : "vvo";

		return new JourneyLeg
		{
			Mode = mode,
			From = Stop(from, providerId),
			To = Stop(to, providerId),
			Line = line is null ? null : new TransitLine { Name = line, Mode = mode },
			ScheduledDeparture = Base.AddMinutes(startMinute),
			ScheduledArrival = Base.AddMinutes(endMinute),
			RealtimeDeparture = delayMinutes == 0 ? null : Base.AddMinutes(startMinute + delayMinutes),
			RealtimeArrival = delayMinutes == 0 ? null : Base.AddMinutes(endMinute + delayMinutes)
		};
	}

	private static Station Stop(string id, string providerId) =>
		new()
		{
			Id = id,
			ProviderId = providerId,
			Name = id
		};
}
