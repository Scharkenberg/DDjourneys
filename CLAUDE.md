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
- Tracking mode: `Routes.Track` carries a `TrackTarget`; only the matched run is shown, with the full course drawn faintly (line opacity 0.4). Known limit: for journey legs the course covers only boarding→alighting, so a vehicle still approaching the boarding stop may not match.
- Disruption/notice HTML: `wwwroot/notice.html` with a whitelist sanitiser, rendered by `NoticeView`. Links go through `NoticeLinks.OpenAsync`: one GET, classify by Content-Type (sniff magic bytes if generic); images open in the in-app viewer, PDFs download to cache and open via `Launcher`, everything else goes to the browser.
- `WebBridge` holds the shared JS call and theme helpers.

## Sharing
- `JourneyShareModel` is the single source for `JourneyShareText` and `JourneyShareImage` (SkiaSharp, drawn in two passes: measure, paint). The start is always a row (`ShareStepKind.Depart`) unless the first ride boards exactly there. The image keeps its surroundings small and its content (times, stops, lines) large.

## State (end of last session, nothing compiled or run by me)
Done: disruptions overhaul, departures time picker (`WhenPicker`), "Around this stop" rows (`SectionRow`), planner shortcut redesign, walk-only trips, marker positions, single attribution, free strip, Auto-fit, run identification, journey alternative buttons wrapping, vehicles tracking banner, walking-interchange timeline and share fixes.
Open / ideas:
- Journey page: the follow, tracking and hand-off buttons are still stacked; planned to restyle into a single Card.
- Broader UI refinement pass across the remaining pages (crowded pages, consistency, icons).
- Other `HybridWebView` subscribers (`MapView` `open:`) should be checked for the same UI-thread issue.
- Map markers in popups and the Android live notification do not yet reflect the split walking interchange.
- The strings generator from the last session (`gen_strings.py`) is not in the repo; add new strings manually to the three localization files.
- Last session (nothing compiled or run): JSON source generation, opt-in logging and developer options, run page (pull to refresh, one Map button: live map for numeric lines, else the static run map), map fonts/knots/contrast, TRIAS marked experimental with robust mode/line detection, bookmark toggle, Dresden made explicit (`VvoPlaces`), chips on the journey page, notice dismissal (swipe/close) and boarding-instruction ageing, accessibility hooks, Windows entry frame.
