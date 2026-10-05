# DDjourneys external contract, version 1

How other apps talk to DDjourneys, and how DDjourneys answers. One vocabulary, three transports.
Reference implementation: `DDjourneys.Core/Contract/` (parser, reply builder, link builder, journey payload);
tests: `DDjourneys.Tracking.Tests/ContractTests.cs`.

## 1. Transports

| Platform | Inbound | Registered in |
|---|---|---|
| all | link `ddjourneys://<command>?...` or `ddjourneys://v1/<command>?...` | |
| Android | `ACTION_VIEW` of such a link, or action `dev.Scharkenberg.DDjourneys.action.CONTRACT` with **string** extras; also a `geo:` link and shared plain text (section 8) | `MainActivity` intent filters |
| Windows | protocol activation (`Start-Process "ddjourneys://..."`); the app is single-instance, a second launch is redirected to the running window | `Package.appxmanifest` |

Extras use the link's keys, plus `command` (and optionally `v`). Keys are case-insensitive, values are trimmed.
Only strings count; other extra types read as absent.

Replies go to the caller's own link (`x-success`, `x-error`), the same pattern as x-callback-url.
The caller registers a scheme or an https App Link for it.

## 2. Commands

| Command | Does | Replies |
|---|---|---|
| `plan` | Fills the planner; with `search=1` also searches and shows the results | only on error (to `x-error`) |
| `pick` | Like `plan` with search; the user chooses a journey, and *Use this journey* hands it back | to `x-success` after the user's tap; errors to `x-error` |
| `tracked` | Opens the followed journeys, optionally focused on one plan | only on error |
| `capabilities` | Asks what this build supports; needs no interaction | at once, to `x-success` |
| `go` | The short form of `plan`: only `to` is needed. Starts where the user starts, now, and searches at once | only on error |
| `departures` | The departures (or arrivals) at a place; without one, at the stop nearest to the device | only on error |
| `home` | "Take me home": from where the device is to the user's home, now, searched | only on error |
| `map` | Opens the map, centred on a place when one is given | only on error |
| `disruptions` | Opens the disruptions, limited to a line when one is given | only on error |
| `live` | Opens the live vehicles of line number(s) | only on error |

All commands except `capabilities` bring the app to the front; the user is meant to see them. Nothing is ever sent
back without either an error or the user's explicit tap, so a caller is not thrown back out of DDjourneys.

## 3. Parameters

| Key | Used by | Meaning |
|---|---|---|
| `v` | all | Contract version, default current (1). Newer than the app speaks: `unsupported_version` |
| `from`, `to`, `via`, `at` | plan, pick, go (`via`), departures and map (`at`) | Place name. Looked up with the selected provider: exact station name, else first station, else first hit. Or a keyword (below) |
| `from.stop`, `to.stop`, `via.stop`, `at.stop` | the same | Provider-qualified stop, `vvo:33000028`. Takes precedence over the name. A stop of another provider than the selected one is refused (`provider_mismatch`) unless a name or coordinates are given to fall back on |
| `from.lat`, `from.lon`, `to.lat`, `to.lon`, `via.*`, `at.*` | the same | WGS84, both or neither. Used as a free place; beats the name |
| `line` | disruptions, live | Line name(s), `S1` or `3,11`; `live` knows only numbers (the live positions are by number) |
| `time` | plan, pick, go, departures | `now`, `2026-10-05T08:30` (provider time, Europe/Berlin) or `2026-10-05T08:30+02:00` / `...Z` (absolute). Must lie between today and a year ahead |
| `mode` | plan, pick, go, departures | `dep` or `arr`; default: the app's setting (departures: departures). With `departures`, `arr` is the arrivals board |
| `search` | plan | `1` to search at once; default `0` (fill only). Needs `to`; a missing `from` is the keyword `@start`. Always on for `pick` and `go` |
| `plan` | tracked | Plan id of a followed journey to scroll to |
| `ref` | all | Opaque token, 1–64 printable ASCII characters without spaces; echoed in every reply |
| `x-success` | pick, capabilities | Where the result goes |
| `x-error` | all | Where errors go. With only `x-success`, errors are not reported |

### Keywords and the user's defaults

A place may be a keyword instead of a name (a real name never starts with `@`; any other `@…` is `invalid_parameter`):

| Keyword | Is |
|---|---|
| `@here` | the stop nearest to the device |
| `@home` | the user's home place |
| `@start` | where the user starts: the app's start setting (a chosen place, else the device) |

Everything not given is what the user has set up in the app: the search mode (departure or arrival), the routing
preferences (accessibility, transfers, pace, ticket category), the start. `go` and a searching `plan` without `from` start at `@start`;
`departures` and `map` without `at` mean where the device is. A keyword that cannot be resolved (no device position, no home
set) is not an error: the planner opens with what is known and asks for the rest, as the quick actions do.

`plan` and `pick` need at least one of `from`/`to`. A single `to` fills only the destination and keeps the user's start.
`pick` additionally needs both places and `x-success`.

Limits: 40 keys, 200 characters per value, 2000 per callback, 8000 per link.
A `pick` always names both ends (keywords included), since the caller asks for exactly that journey.
Unknown keys are ignored (that is how version 1 stays additive); duplicate keys keep the first.

## 4. Replies

Appended to the callback as query parameters (an existing query and fragment are kept):

| Key | |
|---|---|
| `contract` | version of the reply (1) |
| `status` | `ok` or `error` |
| `command` | the command, when it was read |
| `ref` | the caller's token, when given and valid |
| `code`, `message` | errors: stable machine code, English text for logs (not for display) |
| `parameter` | errors: the offending key, when one applies |

### `pick` result

Flat values for callers that only want the headline, plus the whole journey as one JSON document.

| Key | |
|---|---|
| `from`, `to` | names; `from.stop`/`to.stop` (`vvo:…`) and `from.lat`/`from.lon`/`to.lat`/`to.lon` when known |
| `dep`, `arr` | ISO 8601 with the provider offset, real-time included |
| `duration` | minutes; `transfers`: number of changes |
| `lines` | comma-separated line names of the rides |
| `cancelled` | `1` or `0` |
| `journey` | JSON, schema 1 (below). Dropped with `truncated=1` when the reply would exceed 7000 characters |

JSON, schema 1: `{ "schema":1, "from":{name,place?,stop?,lat?,lon?}, "to":{…}, "dep", "arr", "durationMinutes", "transfers", "cancelled", "legs":[ { "mode", "line?", "direction?", "from":{…}, "dep", "depLive?", "depPlatform?", "to":{…}, "arr", "arrLive?", "arrPlatform?", "intermediateStops", "cancelled?" } ] }`.
`mode` is lower snake case (`walk`, `tram`, `bus`, `subway`, `suburban_rail`, `regional_train`, `long_distance_train`, `ferry`, `cable_car`, `taxi`, `on_demand`, `unknown`).
`depLive`/`arrLive` appear only when real time differs from the plan by a minute or more.
A pick that nobody answers just lapses after 15 minutes; version 1 has no cancel reply, so callers keep their own timeout.

### `capabilities` result

`app`, `app.version`, `scheme`, `oldest` (oldest contract version still answered), `commands`, `android.action`, `keywords`, `android.intents`.

### Error codes

`malformed`, `unknown_command`, `unsupported_version`, `invalid_parameter`, `missing_parameter`, `place_not_found`, `provider_mismatch`, `expired`, `internal`.

## 5. Security model

Any app can send these requests, so every command is harmless by construction:

- Nothing destructive: no command changes settings, deletes or edits followed journeys, or starts tracking. The new commands only open a page or search; `home` and the keywords use places the user already stored, and nothing about them leaves the app.
- Everything is validated before use (sizes, control characters, ranges, shapes); malformed input becomes an error reply, never an exception.
- Callbacks are links the caller controls, so the target is restricted: `https`, or a custom scheme. Refused: `http`, `file`, `content`, `javascript`, `intent`, `data`, calls and messages (`tel`, `sms`, `mailto`, …), `ms-*`, user info in the URL, and this app's own scheme (no reply loops).
- A journey leaves the app only after the user taps *Use this journey* in the app.
- Double deliveries (same request within two seconds) and floods (only the newest five queued) are dropped. Android's replay of an old launch intent from the recents list is ignored.
- A custom scheme is not exclusive: another app can register the same one. Callers should address DDjourneys explicitly (`intent.setPackage("dev.Scharkenberg.DDjourneys")`) and, for anything sensitive, take the reply on a verified https App Link. The data in a `pick` reply is public timetable data.

## 6. Versioning

- The version is `v` in the query, or the host form `ddjourneys://v1/...`.
- Within a version only additive changes happen: new optional keys, new reply keys, new commands. Callers ignore reply keys they do not know.
- A breaking change gets a new number; the app answers `oldest..current` and refuses newer ones with `unsupported_version`.
  A caller can ask `capabilities` first, or just send `v=1` and handle the refusal.

## 7. Examples

Link, handy in a browser or a QR code:

```
ddjourneys://plan?from=Hauptbahnhof&to=Hellerau&time=2026-10-05T08:30&mode=arr&search=1
```

Android, from the shell (`-p` addresses this app explicitly; quote the link):

```
adb shell am start -a android.intent.action.VIEW -p dev.Scharkenberg.DDjourneys -d "ddjourneys://plan?to=Hellerau\&search=0"
adb shell am start -a dev.Scharkenberg.DDjourneys.action.CONTRACT -p dev.Scharkenberg.DDjourneys --es command plan --es to Hellerau
```

Android (Kotlin), asking for a journey and receiving it on a custom scheme:

```kotlin
val back = Uri.parse("myapp://journey")
val link = Uri.Builder().scheme("ddjourneys").authority("v1").path("/pick")
    .appendQueryParameter("from", "Hauptbahnhof")
    .appendQueryParameter("to", "Hellerau")
    .appendQueryParameter("ref", "trip-42")
    .appendQueryParameter("x-success", back.toString())
    .appendQueryParameter("x-error", "myapp://journey-error")
    .build()
startActivity(Intent(Intent.ACTION_VIEW, link).setPackage("dev.Scharkenberg.DDjourneys"))

// In the activity registered for myapp://journey:
val data = intent.data
val dep = data?.getQueryParameter("dep")
val journeyJson = data?.getQueryParameter("journey") // absent when truncated=1
```

.NET (shares `DDjourneys.Core`, e.g. DDepartures):

```csharp
Uri link = ContractLinks.Pick(
    new ContractPlace("Hauptbahnhof", null, null, null),
    new ContractPlace("Hellerau", null, null, null),
    success: new Uri("ddepartures://journey"),
    error: new Uri("ddepartures://journey-error"),
    reference: "trip-42");
await Launcher.Default.OpenAsync(link);

// On the way back, parse the query (Uri.UnescapeDataString per value); "journey" holds the JSON.
```

Windows: `Start-Process "ddjourneys://tracked"`.

## 8. Easy ways in

For callers that do not want to learn the contract:

| You have | Do |
|---|---|
| a destination name | `ddjourneys://go?to=Hellerau`: starts where the user starts, now, and searches |
| a place on a map | send a `geo:51.05,13.73?q=Hellerau` link (any maps app, browser or messenger already does): DDjourneys offers itself and runs `go` to it |
| some text | the share sheet: the first line is the destination (a URL is ignored) |
| a stop | `ddjourneys://departures?at.stop=vvo:33000028`, or `?at=Postplatz&mode=arr` for arrivals |
| nothing | `ddjourneys://departures` (the stop nearest to the user), `ddjourneys://home`, `ddjourneys://map` |
| a line | `ddjourneys://disruptions?line=S1`, `ddjourneys://live?line=3,11` |

.NET callers (`DDjourneys.Core`):

```csharp
await Launcher.Default.OpenAsync(ContractLinks.Go(new ContractPlace("Hellerau", null, null, null)));
await Launcher.Default.OpenAsync(ContractLinks.Departures(new ContractPlace(null, "vvo:33000028", null, null), arrivals: true));
await Launcher.Default.OpenAsync(ContractLinks.Home());
await Launcher.Default.OpenAsync(ContractLinks.Live("3,11"));
```

Android (Kotlin), without any library:

```kotlin
fun open(link: String) = startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(link)).setPackage("dev.Scharkenberg.DDjourneys"))

open("ddjourneys://go?to=" + Uri.encode("Hellerau"))
open("ddjourneys://departures?at=" + Uri.encode("Postplatz"))
open("geo:51.05,13.73?q=" + Uri.encode("Hellerau"))
```
