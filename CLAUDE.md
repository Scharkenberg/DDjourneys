# DDjourneys: context for Claude

Journey planner for DVB/VVO (Dresden), .NET 11 MAUI / C# 15. Repo: `Scharkenberg/DDjourneys`. Owner commits and pushes himself. Models after the Android app oeffi, but modern. Departures live in the separate repo DDepartures. Always fetch the latest repo state first; he edits code between sessions. Discard remembered code that the repo contradicts.

## Working rules
- Terse. Deliver final code per step, no elaboration. Complex points get one simple explanation. Prefer current docs; never rely on superseded information.
- Do NOT compile MAUI code or shim MAUI types in the container. Verify against the official Microsoft docs (Microsoft Learn MCP) instead. Core (net, no MAUI) may be unit-checked.
- Generated files use CRLF and tabs. Deliver changed files as a zip with repo-relative paths (SendUserFile); when asked for "just the fix", print only the code.
- JSON is source-generated, never reflected: `JsonSerializerIsReflectionEnabledByDefault=false`. Provider DTOs go through `VvoJsonContext` (add a `[JsonSerializable]` for every new DTO root); wire objects without a DTO are `JsonObject` trees (`Wire.Array`, no anonymous types, they are trimmed away on Android); stored lists are written with `StoredJson.Write(items, toNode)`. `ExpertReport` reads DTOs through `VvoJson.TypeInfo`. No `yield` inside catch; static Regex fields (no `[GeneratedRegex]`).
- Dependencies: CommunityToolkit.Maui only. Own ObservableObject/AsyncCommand, built-in DI and Shell.
- Verify before finishing: brace balance, XAML well-formedness, every `{localization:Tr Group.Key}` exists, usings, no leftovers.

## Layout
- `DDjourneys.Core`: models, providers, services, mapping (no UI).
  - Providers: Vvo (WebAPI), Trias, Tlms (live vehicles), OpenData.
  - `Mapping/MapScene`, `MapTheme`: map JSON for the web view.
  - `Models/TrackTarget`, `RunMatcher`: identify the one vehicle run for a tapped journey leg or departure (line number, course projection ≤220 m, time ±4 min, heading check).
- `DDjourneys` (MAUI): Pages (XAML + ViewModel), Controls, Support, Localization, Platforms/Android, `Resources/Raw/wwwroot`.
- Pages: Plan, PlaceSearch, Results, Journey, Departures, Disruptions (+ Disruption detail), Map, Vehicles, Run, Settings, Providers. Routes in `Support/Routes.cs`, registered in `AppShell.xaml.cs`; DI in `MauiProgram.cs`.

## Providers
- Everything a provider's ids or meaning touch is kept per provider: `PlaceStore` (recents, favourites, searched and saved routes, home) and the routing preferences in `AppSettings`. VVO keeps the original keys, every other provider uses `<key>@<provider id>`. `PlaceStore` reloads on `ProviderRegistry.SelectionChanged`; the Departures, Disruptions, Vehicles, Plan and PlaceSearch view models drop their provider-bound state on it. Any new provider-bound cache or list must do the same.

## TRIAS (VDV 431-2, schema v1.4, checked against github.com/VDVde/TRIAS)
- A `Service` keeps its line data in `ServiceSection` (LineRef, DirectionRef, Mode/PtMode, PublishedLineName, OperatorRef); `TriasMapper.Properties` reads the first section, or the service itself for older flat responses. Destination, JourneyRef, Attribute and the status flags stay on the service.
- Journey stops are identified by stop place (`StationId`: first three DHID parts), the platform is a detail. Changes are `InterchangeLeg` (`InterchangeMode`: walk, protectedConnection, guaranteedConnection, remainInVehicle; `WalkDuration`, `BufferTime`) or `ContinuousLeg`; only the connection modes are guaranteed.
- Versions: every request is written for 1.4 and steps down 1.3, 1.2, 1.1 (`TriasClient.SendAsync(kind, build(dialect))`, `TriasDialect`). Rejected = non-transient HTTP error, unreadable XML, or an `ErrorMessage` without any result and not a "no result" code. The working version is remembered per `TriasRequestKind` and 1.4 is retried after 30 min. Unreachable/timeouts never downgrade. Each attempt is logged (`[TRIAS] ...`). Only 1.4 is in VDVde/TRIAS; which optional parameters older versions know (`HasExtendedContent` from 1.3, `HasMobilityAdditions` 1.4) is an assumption.
- Request parameters are an XSD sequence: keep `TriasRequests` in the order of `TripParam`/`StopEventParam`/`TripInfoParam` (1.4: `InterchangeLimit`, not `TransferLimit`; it is a positive integer, so "no transfers" is filtered client-side).
- TripInfo (`TriasRequests.TripInfo`, `TriasMapper.MapRun` → `RunDetail`) refreshes a run from `JourneyRef` + `OperatingDayRef` kept in `TriasRunData`; falls back to the stop event's calls. `IDepartureProvider.GetRunDetailAsync` (default: stops only) carries `RunDetail.Vehicle` (`CurrentPosition`) and `OperatingDays`; the Run page shows operating days and `MapScenes.FromRun` adds a reported-position vehicle marker (and then prefers the static map over the live lookup).
- `FaresParam` (`PassengerCategory`, last element of `TripParam`) is sent from 1.4 on for non-adult passengers (`RoutingPreferences.Passenger`, option "Tickets for" on the routing page, per provider). `IncludeOperatingDays` from 1.3; `OperatingDays` (From/To/Pattern bit string, description) → `JourneyLeg.OperatingDays`, shown on the leg as "Runs Mon–Fri" (`OperatingDaysText`, nothing when daily).
- Situations: `Situations/PtSituation` (SIRI SX: SituationNumber, Summary/Description/Detail) are matched to `SituationFullRef` on the trip and its legs and become `Journey.Notices` / `JourneyLeg.Notices`. Call-level `Occupancy` fills `RunStop.Occupancy`.
- Not implemented: `CurrentPosition` on journey maps (only the run map), traveller age/owned tickets in `FaresParam`, Trias_Booking/Facilities/Alerts.

## Quick actions and widgets
- Quick actions "take me home" (planner: from the stop nearest the device to home, now, search) and "departures from here" (departures page, locates). `AppShortcuts` (Support) is the one queue: Android dynamic shortcuts (`AndroidShortcuts`, published on resume so labels follow the app language; intent action `AppShortcuts.AndroidAction` handled in `MainActivity.Accept`), Windows jump list (`WindowsJumpList`, launch argument `shortcut=home|departures`, read in `WindowsContractActivation`). Delivery retries until the shell is ready.
- Android widgets (min 120 x 120 dp), five kinds (`WidgetKind`): route, departures, arrivals, nearby stops (meters), next departures from nearby stops. One shared layout (`widget_main.xml`: header, message, 10 row slots); `WidgetLayout.For(width, height, maxRows)` (Core) decides rows and detail (Minimal/Compact/Full) from the size, re-drawn on `OnAppWidgetOptionsChanged`. Settings per widget id (`WidgetConfig` JSON in Preferences, `WidgetStore`), snapshot cached so resizing needs no network; rows carry absolute times (`WidgetSnapshot.Upcoming` drops rows that are over). Data: `WidgetLoader` (platform neutral) under `ProviderRegistry.Override(config.ProviderId)`: a widget keeps its provider whatever the app shows. Updates: system `updatePeriodMillis` (30 min, the Android minimum) honouring the widget's own interval and auto switch; a tap on the surface refreshes at once (`WidgetNames.Refresh`); `goAsync` keeps the receiver alive, fetch limit 8 s. Location in the background is the last known one (`AndroidWidgetLocation`, then `DeviceLocator.LastFix`); needs "all the time" location for fresh values. The settings screen (`WidgetConfigActivity`) is native Android views (no MAUI UI before the app runs); place search in `PlacePickerDialog`. Widget strings: `WidgetStrings` and `Resources/values*/widget_strings.xml` (picker labels).
- Android UI classes clash with MAUI's implicit usings (`Button`, `View`, `ListView`, `Switch`, `ScrollView`, `Orientation`): alias them.

## Fares
- `JourneyFare` (kind, price, zones, notes, valid-for, url). VVO: `VvoFareMapper` turns the route's `Price`/`PriceDayTicket` (+ zone names, `TicketNotes`) into a single and a day ticket; the API quotes nothing else. TRIAS 1.4: `TripFares/Ticket`. `FareChoice.Preferred(fares, passenger)` picks the one ticket cards, header and shares name: the passenger's own single ticket, else tickets without a passenger statement (VVO quotes only the normal price), else adult. The journey page section is collapsed by default (header: preferred ticket and price; expanded: compact rows, zones and conditions once).
- A change is "endangered" only by arithmetic once real-time times exist on either side (`TimelineBuilder.CreateBoundary`); the provider's flag counts only without real-time data.

## Logging and developer options
- No `Debug.WriteLine`, no always-on files. Everything diagnostic goes through `DiagnosticLog.Write/Api`, which writes only while Settings > Developer options > "Log to file" is on (`AppSettings.LogToFile`, needs `DeveloperOptions`). Switching it off, or the developer options off, deletes the file; so does a start with it off. `ApiClient`, `SchutzengelApi`, TLMS and notice links log their exchanges; new external API code must too.
- Expert view (raw provider data) is a developer option as well.

## Conventions
- Style classes: Card, Chip, ChipButton, Caption, Faint, Section, Subtitle, Title, Tonal, Divider, ChipLabel. Helpers: `support:Dense.MinHeight/Padding`, `support:Motion.Feedback`, `support:Themed.*`.
- Icons: vector `Icon`/`IconButton`/`IconGlyph` on a 24x24 grid (add paths in `IconGlyph.cs`). A Button cannot render them; use a tappable Border plus Icon.
- Pills only for non-interactive elements; interactive elements (pickers, tappable chips) are never pills. Native date/time pickers never inside pill chips.
- Accessibility: `SystemAccessibility` (OS text scale, remove-animations, high contrast, screen reader) feeds `Motion.Enabled`, `Dense` (min sizes scale with text), `Icon` size and the map page (font scale). Icons are decorative (not in the accessible tree); the control holding one carries the `SemanticProperties.Description`. `Title`/`Section` labels are headings.
- Bookmarks: `RouteBookmark` (planner, results, journey page) toggles a saved route; never disabled.
- Windows entries: the native WinUI text box frame is removed (`Platforms/Windows/NativeStyling`); our card draws the only contour.
- Themes: Light, Dark, Dark AMOLED, System (system dark = AMOLED). Palette keys via `Theme.ColorOf("Bg"|"Surface"|"Raised"|"Outline"|"Ink"|"InkMuted"|"Accent"...)`.
- Localization: `IUiStrings` + `EnglishUiStrings` + `GermanUiStrings`, accessed via `{localization:Tr Group.Key}`. New strings must exist in all three.
- Wrapping rows: `FlexLayout Wrap="Wrap" AlignContent="Start" AlignItems="Start"` (the default Stretch makes wrapped rows taller). FlexLayout has no ColumnSpacing; use item Margin.
- Safe area (MAUI docs): ContentPage defaults to None; Layout defaults to Container; ContentView and Border default to None.
- ViewModels: `ObservableObject`, `DisposableViewModel`, `AsyncCommand`/`AsyncCommand<T>`, `PageTeardown`, `Motion.Prepare/EnterPage`, `DiagnosticLog`.
- Threading: HybridWebView raw messages and events arrive on a background thread. Always `Dispatcher.Dispatch` before touching views (crash seen in `DisruptionPage.OnLinkTapped`, fixed).

## VVO API facts
- A partial route with `Mot.Type == "Footpath"` is a transfer without stops; `StayForConnection` is an ensured connection (shown as a message, not a real change); `Mobility*` is accessibility.
- Walk-only trips have only transfers, so the mapper builds a synthetic `TransitMode.Walk` leg (`VvoJourneyMapper.WalkOnly`).
- `Journey.Origin/Destination` come from the searched Location (centre point); `leg.From/To` carry platform-level coordinates (GK4→WGS84). Map start/end markers use `MapScenes.Terminal` to prefer the leg position when it is the same stop.
- Timeline: `TimelineBuilder` yields Ride/Walk/Boundary items; `TimelineRows` renders them. An interchange node is merged only when both legs use the same stop (`SameStation`). A change that walks to another stop renders alight → walk → board.

## Maps and web views
- MapLibre GL JS 5.24.0 inside a `HybridWebView` (`wwwroot/index.html`, `ddMapCall(command,json)`: init/theme/set/focus). Raw messages: `ready`, `open:`, `error:`, `auto:true|false`. CARTO vector styles use a key from `Resources/Raw/secrets.json` (gitignored). Exactly one attribution control (collapsed "i"). A 32 px free strip sits under the map so swiping off the map is always possible. Option "Auto-fit" (persisted `Preferences` key `MapAutoFit`); manual pan/zoom switches it off for the session.
- Following a journey leg (`TrackTargets.FromLeg`): stops with positions and times, else the leg geometry with times spread by distance (TRIAS calls carry no coordinates; `TriasProvider` also looks stop positions up for runs). A leg with neither opens nothing (the live page would show every vehicle of the line). "In progress" is evaluated against the clock (`LegRow.IsActive`, page timer).
- Tracking mode: `Routes.Track` carries a `TrackTarget`; only the matched run is shown, with the full course drawn faintly (line opacity 0.4). Known limit: for journey legs the course covers only boarding→alighting, so a vehicle still approaching the boarding stop may not match.
- Disruption/notice HTML: `wwwroot/notice.html` with a whitelist sanitiser, rendered by `NoticeView`. Links go through `NoticeLinks.OpenAsync`: one GET, classify by Content-Type (sniff magic bytes if generic); images open in the in-app viewer, PDFs download to cache and open via `Launcher`, everything else goes to the browser.
- `WebBridge` holds the shared JS call and theme helpers.

## Sharing
- Provider notices are never part of a share. The model also carries the preferred price (figures line) and guaranteed connections (`IsGuaranteed`, "✓ guaranteed").
- `JourneyShareModel` is the single source for `JourneyShareText` and `JourneyShareImage` (SkiaSharp, drawn in two passes: measure, paint). The start is always a row (`ShareStepKind.Depart`) unless the first ride boards exactly there. The image keeps its surroundings small and its content (times, stops, lines) large.

## State (end of last session, nothing compiled or run by me)
Done: disruptions overhaul, departures time picker (`WhenPicker`), "Around this stop" rows (`SectionRow`), planner shortcut redesign, walk-only trips, marker positions, single attribution, free strip, Auto-fit, run identification, journey alternative buttons wrapping, vehicles tracking banner, walking-interchange timeline and share fixes.
Open / ideas:
- Journey page: the follow, tracking and hand-off buttons are still stacked; planned to restyle into a single Card. The map button now lives in the overview card (`JourneyCardModel.MapCommand`, bottom right).
- Broader UI refinement pass across the remaining pages (crowded pages, consistency, icons).
- Other `HybridWebView` subscribers (`MapView` `open:`) should be checked for the same UI-thread issue.
- Map markers in popups and the Android live notification do not yet reflect the split walking interchange.
- The strings generator from the last session (`gen_strings.py`) is not in the repo; add new strings manually to the three localization files.
- Last session (nothing compiled or run): JSON source generation, opt-in logging and developer options, run page (pull to refresh, one Map button: live map for numeric lines, else the static run map), map fonts/knots/contrast, TRIAS marked experimental with robust mode/line detection, bookmark toggle, Dresden made explicit (`VvoPlaces`), chips on the journey page, notice dismissal (swipe/close) and boarding-instruction ageing, accessibility hooks, Windows entry frame.
