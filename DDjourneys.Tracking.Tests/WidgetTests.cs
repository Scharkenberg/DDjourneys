using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Widgets;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Tracking.Tests;

/// <summary>Widget size to rows, stored configuration and snapshot, and the per-flow provider override.</summary>
public sealed class WidgetTests
{
	[Fact]
	public void The_smallest_widget_shows_one_minimal_row_at_least()
	{
		WidgetLayout layout = WidgetLayout.For(120, 54);

		Assert.Equal(WidgetDetail.Minimal, layout.Detail);
		Assert.Equal(1, layout.Rows);
		Assert.False(layout.ShowUpdated);

		// Anything smaller is drawn as the smallest.
		Assert.Equal(layout, WidgetLayout.For(40, 20));
		Assert.InRange(WidgetLayout.For(120, 120).Rows, 3, 6);
	}

	[Fact]
	public void Larger_widgets_show_more_rows_and_more_detail()
	{
		WidgetLayout small = WidgetLayout.For(150, 150);
		WidgetLayout wide = WidgetLayout.For(300, 150);
		WidgetLayout tall = WidgetLayout.For(300, 480);

		Assert.Equal(WidgetDetail.Compact, WidgetLayout.For(200, 200).Detail);
		Assert.Equal(WidgetDetail.Full, wide.Detail);
		Assert.True(tall.Rows > wide.Rows);
		Assert.True(tall.Rows > small.Rows);
		Assert.True(tall.ShowUpdated);
		Assert.True(tall.Rows <= WidgetLayout.MaxRows);
		Assert.True(WidgetLayout.For(300, 480, fontScale: 1.5).Rows <= tall.Rows);
	}

	[Fact]
	public void What_the_height_leaves_over_is_spread_over_the_rows_within_limits()
	{
		// Exactly as many rows as fit: next to nothing is left; plenty of room for few rows: the cap holds.
		WidgetLayout full = WidgetLayout.For(300, 200);
		double tight = WidgetLayout.RowPadding(200, full.Rows, full.Detail);
		double roomy = WidgetLayout.RowPadding(480, 2, full.Detail);

		Assert.InRange(tight, 0, 6);
		Assert.Equal(6, roomy);
		Assert.Equal(0, WidgetLayout.RowPadding(200, 0, full.Detail));
		Assert.True(WidgetLayout.RowPadding(200, full.Rows, full.Detail, 1.5) <= tight + 6);
	}

	[Fact]
	public void The_row_cap_of_the_user_limits_the_rows()
	{
		Assert.Equal(2, WidgetLayout.For(300, 480, maxRows: 2).Rows);
		Assert.True(WidgetLayout.For(300, 480, maxRows: 0).Rows > 2);
	}

	[Fact]
	public void A_configuration_survives_the_json_round_trip()
	{
		var config =
			new WidgetConfig
			{
				Kind = WidgetKind.Route,
				ProviderId = "vvo",
				From = WidgetPlace.Here,
				To = new WidgetPlace(new Location { Id = "33000028", Name = "Hauptbahnhof", Place = "Dresden", Latitude = 51.04, Longitude = 13.73, ProviderId = "vvo" }),
				RadiusMeters = 750,
				MaxRows = 4,
				Lines = "3, 11",
				Modes = ModeFilter.Tram | ModeFilter.CityBus,
				AutoRefresh = false,
				IntervalMinutes = 120
			};

		WidgetConfig? read = WidgetConfig.FromJson(config.ToJson());

		Assert.NotNull(read);
		Assert.Equal(WidgetKind.Route, read.Kind);
		Assert.True(read.From?.IsHere);
		Assert.Equal("Hauptbahnhof", read.To?.Place?.Name);
		Assert.Equal(13.73, read.To?.Place?.Longitude);
		Assert.Equal(750, read.RadiusMeters);
		Assert.Equal(4, read.MaxRows);
		Assert.Equal(["3", "11"], read.LineFilter);
		Assert.Equal(ModeFilter.Tram | ModeFilter.CityBus, read.Modes);
		Assert.False(read.AutoRefresh);
		Assert.Equal(120, read.IntervalMinutes);
		Assert.True(read.IsComplete);
	}

	[Fact]
	public void An_unfinished_configuration_is_not_complete()
	{
		Assert.False(new WidgetConfig { Kind = WidgetKind.Route, From = WidgetPlace.Here }.IsComplete);
		Assert.False(new WidgetConfig { Kind = WidgetKind.Departures }.IsComplete);
		Assert.True(new WidgetConfig { Kind = WidgetKind.NearbyStops }.IsComplete);
	}

	[Fact]
	public void Unreadable_text_is_no_configuration()
	{
		Assert.Null(WidgetConfig.FromJson(null));
		Assert.Null(WidgetConfig.FromJson("not json"));
		Assert.Null(WidgetConfig.FromJson("[1,2]"));
	}

	[Fact]
	public void A_snapshot_survives_the_json_round_trip()
	{
		var snapshot =
			new WidgetSnapshot
			{
				Title = "Hauptbahnhof",
				UpdatedAt = new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.FromHours(2)),
				IsStale = true,
				Rows =
				[
					new WidgetRow { Chip = "11", Mode = TransitMode.Tram, Main = "Zschertnitz", Sub = "Platform 2", Time = "08:41", Delay = "+2 min", DelayLevel = WidgetDelay.Late },
					new WidgetRow { Kind = WidgetRowKind.Header, Main = "Postplatz", Time = "120 m" },
					new WidgetRow { Chip = "7", Time = "23:09", Arrival = "23:17", Duration = "8 min", Transfers = "Direct", Lines = "7 \u203a 333", Lead = "Walk 4 min" }
				]
			};

		WidgetSnapshot? read = WidgetSnapshot.FromJson(snapshot.ToJson());

		Assert.NotNull(read);
		Assert.True(read.IsStale);
		Assert.Equal(snapshot.UpdatedAt, read.UpdatedAt);
		Assert.Equal(3, read.Rows.Count);
		Assert.Equal(TransitMode.Tram, read.Rows[0].Mode);
		Assert.Equal(WidgetDelay.Late, read.Rows[0].DelayLevel);
		Assert.Equal(WidgetRowKind.Header, read.Rows[1].Kind);
		Assert.Equal("23:17", read.Rows[2].Arrival);
		Assert.Equal("8 min", read.Rows[2].Duration);
		Assert.Equal("Direct", read.Rows[2].Transfers);
		Assert.Equal("7 \u203a 333", read.Rows[2].Lines);
		Assert.Equal("Walk 4 min", read.Rows[2].Lead);
	}

	[Fact]
	public void Rows_that_are_over_drop_out_with_their_empty_header()
	{
		DateTimeOffset now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

		var snapshot =
			new WidgetSnapshot
			{
				Rows =
				[
					new WidgetRow { Kind = WidgetRowKind.Header, Main = "Old stop" },
					new WidgetRow { Main = "gone", At = now.AddMinutes(-5) },
					new WidgetRow { Kind = WidgetRowKind.Header, Main = "Current stop" },
					new WidgetRow { Main = "just left", At = now.AddSeconds(-30) },
					new WidgetRow { Main = "later", At = now.AddMinutes(12) },
					new WidgetRow { Main = "no time" }
				]
			};

		Assert.Equal(["Current stop", "just left", "later", "no time"], snapshot.Upcoming(now).Select(row => row.Main));
	}

	[Fact]
	public async Task The_provider_override_lasts_for_the_flow_only()
	{
		var registry =
			new ProviderRegistry(
				[
					new ProviderInfo("a", "A", "A", "X", "X", ProviderCapabilities.Journeys),
					new ProviderInfo("b", "B", "B", "X", "X", ProviderCapabilities.Journeys)
				],
				() => "a");

		Assert.Equal("a", registry.SelectedId);

		using (registry.Override("b"))
		{
			await Task.Yield();

			Assert.Equal("b", registry.SelectedId);
		}

		Assert.Equal("a", registry.SelectedId);
	}
}
