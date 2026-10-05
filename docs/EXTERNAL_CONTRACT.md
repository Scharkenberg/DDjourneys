# External contract of DDjourneys, version 1

This document describes how another app asks DDjourneys to do something, and what DDjourneys sends back. There is one set of commands and parameters. It reaches the app in one of three ways: as a link, as an Android intent, or as a Windows protocol launch.

The code that implements it is in `DDjourneys.Core/Contract/` (parser, reply builder, link builder, journey payload). The tests are in `DDjourneys.Tracking.Tests/ContractTests.cs`.

## 1. How a request arrives

| Platform | What the caller sends | Where the app registers for it |
|---|---|---|
| all | the link `ddjourneys://<command>?...` or `ddjourneys://v1/<command>?...` | |
| Android | `ACTION_VIEW` with such a link; or the action `dev.Scharkenberg.DDjourneys.action.CONTRACT` with string extras; or `ACTION_VIEW` with a `geo:` link (section 8) | the intent filters on `MainActivity` |
| Windows | protocol activation, for example `Start-Process "ddjourneys://..."`. The app runs once; a second start is handed over to the running window. | `Package.appxmanifest` |

An intent carries the same keys as a link, plus `command` and optionally `v`. Keys are not case sensitive and values are trimmed. Only string extras count. An extra of any other type is treated as missing.

DDjourneys does not accept shared text. Android decides which apps appear in a share sheet by the type of the content (`text/plain`) and never by what the text says, so an app that accepts plain text appears in every text share on the device. A caller that has a place name sends `ddjourneys://go?to=...`, and a caller that has a coordinate sends a `geo:` link.

If the app is not running, Android or Windows starts it and the request waits until the first screen is ready. The user then sees the result of the request, not the planner first.

A reply goes to the caller's own link, given in `x-success` and `x-error`. This is the same pattern as x-callback-url. The caller registers a custom scheme or an https App Link for it.

## 2. Commands

| Command | What it does | What comes back |
|---|---|---|
| `plan` | Fills the planner. With `search=1` it also searches and shows the results. | only an error, to `x-error` |
| `pick` | Like `plan` with a search. The user chooses a journey, and the button *Use this journey* hands it back. | the journey to `x-success` after the user taps; errors to `x-error` |
| `tracked` | Opens the followed journeys, optionally scrolled to one plan. | only an error |
| `capabilities` | Asks what this build supports. Needs no interaction. | at once, to `x-success` |
| `go` | The short form of `plan`. Only `to` is needed. It starts where the user starts, uses the time now, and searches immediately. | only an error |
| `departures` | Shows the departures, or the arrivals, at a place. Without a place it uses the stop nearest to the device. | only an error |
| `home` | "Take me home": from the device position to the user's home stop, now, searched at once. | only an error |
| `map` | Opens the map, centred on a place if one is given. | only an error |
| `disruptions` | Opens the disruptions, limited to a line if one is given. | only an error |
| `live` | Opens the live vehicles of one or more line numbers. | only an error |

Every command except `capabilities` brings the app to the front, because the user is meant to see the result. Nothing is sent back unless an error occurred or the user pressed the button in the app. A caller is therefore never thrown out of DDjourneys by surprise.

## 3. Parameters

| Key | Used by | Meaning |
|---|---|---|
| `v` | all | The contract version the caller speaks. The default is the current one, 1. A version newer than the app speaks is refused with `unsupported_version`. |
| `from`, `to`, `via`, `at` | `plan`, `pick`, `go` (`via`), `departures` and `map` (`at`) | A place name, looked up with the provider selected in the app: an exact station name first, else the first station, else the first hit. It can also be a keyword (see below). |
| `from.stop`, `to.stop`, `via.stop`, `at.stop` | the same | A stop with the provider in front, for example `vvo:33000028`. It takes precedence over the name. A stop of a provider other than the selected one is refused with `provider_mismatch`, unless a name or coordinates are given to fall back on. |
| `from.lat`, `from.lon`, `to.lat`, `to.lon`, `via.lat`, `via.lon`, `at.lat`, `at.lon` | the same | WGS84 coordinates. Both or neither. The place is then a free point, and it takes precedence over the name. |
| `line` | `disruptions`, `live` | Line names such as `S1` or `3,11`. `live` accepts only numbers, because live positions are reported by line number. |
| `time` | `plan`, `pick`, `go`, `departures` | `now`, or `2026-10-05T08:30` (provider time, Europe/Berlin), or `2026-10-05T08:30+02:00` or `...Z` (absolute). It has to lie between today and one year ahead. |
| `mode` | `plan`, `pick`, `go`, `departures` | `dep` or `arr`. The default is the app's setting. For `departures`, `arr` means the arrivals board. |
| `search` | `plan` | `1` searches at once, `0` (the default) only fills the planner. It needs `to`. A missing `from` counts as `@start`. It is always on for `pick` and `go`. |
| `plan` | `tracked` | The plan id of a followed journey to scroll to. |
| `ref` | all | A token of your own, 1 to 64 printable ASCII characters without spaces. It is returned in every reply. |
| `x-success` | `pick`, `capabilities` | Where the result goes. |
| `x-error` | all | Where errors go. If only `x-success` is given, errors are not reported. |

The provider ids are `vvo` and `trias-vvo`. A stop key is the provider id, a colon, and the id the provider uses. It may contain letters, digits and the characters `_ . : -`.

### Keywords and the user's defaults

A place can be a keyword instead of a name. A real place name never starts with `@`, and any other word starting with `@` is refused as `invalid_parameter`.

| Keyword | Means |
|---|---|
| `@here` | the stop nearest to the device |
| `@home` | the user's home place |
| `@start` | where the user starts: the start setting of the app, which is a chosen place or else the device |

Whatever the request leaves out comes from what the user has set up in the app: the search mode (leave or arrive), the route preferences (accessibility, changes, walking pace, ticket category) and the start. `go` and a searching `plan` without `from` start at `@start`. `departures` and `map` without `at` mean the position of the device. A keyword that cannot be resolved, for example because the device has no position or no home stop is set, is not an error. The planner opens with what is known and asks for the rest, in the same way as the quick actions.

`plan` and `pick` need at least one of `from` and `to`. A single `to` fills only the destination and keeps the user's own start. `pick` needs both places and `x-success`, and the places can be keywords. A `pick` always names both ends, because the caller asks for exactly that journey.

Limits: 40 keys, 200 characters per value, 2000 characters per callback address, 8000 characters per link. Unknown keys are ignored, which is how version 1 stays open for additions. If a key appears twice, the first one counts.

## 4. Replies

A reply is a set of query parameters added to the callback address. An existing query or fragment on that address stays.

| Key | Meaning |
|---|---|
| `contract` | the version of the reply, 1 |
| `status` | `ok` or `error` |
| `command` | the command, if it could be read |
| `ref` | the caller's token, if one was given and was valid |
| `code`, `message` | for errors: a stable machine code, and an English text for logs (not meant for display) |
| `parameter` | for errors: the key that caused it, if one did |

### Result of `pick`

The reply holds a few flat values for callers that only want the headline, and the complete journey as one JSON document.

| Key | Meaning |
|---|---|
| `from`, `to` | names. `from.stop` and `to.stop` (`vvo:...`) and `from.lat`, `from.lon`, `to.lat`, `to.lon` are added when known. |
| `dep`, `arr` | ISO 8601 with the offset of the provider, real-time data included |
| `duration` | minutes |
| `transfers` | the number of changes |
| `lines` | the line names of the rides, separated by commas |
| `cancelled` | `1` or `0` |
| `journey` | the JSON document described below. It is left out, and `truncated=1` is set, when the reply would be longer than 7000 characters. |

The JSON document, schema 1:

```json
{
  "schema": 1,
  "from": { "name": "...", "place": "...", "stop": "...", "lat": 0, "lon": 0 },
  "to": { "name": "..." },
  "dep": "...", "arr": "...", "durationMinutes": 0, "transfers": 0, "cancelled": false,
  "legs": [
    {
      "mode": "tram", "line": "11", "direction": "...",
      "from": { "name": "..." }, "dep": "...", "depLive": "...", "depPlatform": "...",
      "to": { "name": "..." }, "arr": "...", "arrLive": "...", "arrPlatform": "...",
      "intermediateStops": 0, "cancelled": false
    }
  ]
}
```

Optional fields, left out when unknown: `place`, `stop`, `lat` and `lon` of a place, and `line`, `direction`, `depLive`, `depPlatform`, `arrLive`, `arrPlatform` and `cancelled` of a leg. `depLive` and `arrLive` appear only when real time differs from the timetable by a minute or more.

`mode` is lower case with underscores: `walk`, `tram`, `bus`, `subway`, `suburban_rail`, `regional_train`, `long_distance_train`, `ferry`, `cable_car`, `taxi`, `on_demand` or `unknown`.

A `pick` that nobody answers expires after 15 minutes. Version 1 has no reply for a cancelled pick, so a caller keeps its own timeout.

### Result of `capabilities`

| Key | Value |
|---|---|
| `app` | `DDjourneys` |
| `app.version` | the version of the app |
| `scheme` | `ddjourneys` |
| `contract` | the newest contract version the app speaks |
| `oldest` | the oldest contract version it still answers |
| `journey.schema` | the schema version of the journey document in a `pick` reply |
| `commands` | the commands, separated by commas |
| `android.action` | the name of the Android action |
| `keywords` | the keywords, separated by commas |
| `android.intents` | the Android intent kinds the app handles, `view:ddjourneys,view:geo` |

### Error codes

| Code | Raised when |
|---|---|
| `malformed` | the request cannot be read at all: not a link, too long, wrong scheme |
| `unknown_command` | the command is not one of the ten above |
| `unsupported_version` | the caller asks for a version newer than the app speaks |
| `invalid_parameter` | a key is present but its value is not usable: wrong shape, out of range, an unknown keyword, a callback that is not allowed |
| `missing_parameter` | a key the command needs is absent |
| `place_not_found` | no stop or address was found for a name |
| `provider_mismatch` | a stop key belongs to a provider other than the selected one |
| `expired` | a `pick` was not finished in time |
| `internal` | something failed inside the app, or the request took longer than 15 seconds to resolve |

## 5. What the contract cannot do, on purpose

Any app on the device can send these requests, so no command can do harm:

No command changes settings, deletes or edits followed journeys, or starts following a journey. Commands only open a page or run a search. `home` and the keywords use places the user stored earlier, and nothing about those places leaves the app.

Everything is checked before it is used: sizes, control characters, ranges and shapes. A malformed request produces an error reply and never an exception.

A callback is a link the caller controls, so the app restricts it. It has to be `https`, or a custom scheme. These are refused: `http`, `file`, `content`, `javascript`, `intent`, `data`, links that start calls or messages (`tel`, `sms`, `mailto` and similar), `ms-*`, any address with user info, and the scheme of DDjourneys itself, which would send a reply back to the app that sent it.

A journey leaves the app only after the user taps *Use this journey*.

The same request twice within two seconds is carried out once. Of a flood of requests only the newest five are kept. When Android shows an old launch intent again from the list of recent apps, the app ignores it.

A custom scheme is not exclusive. Another app can register the same one. A caller should therefore address DDjourneys directly (`intent.setPackage("dev.Scharkenberg.DDjourneys")`) and, for anything sensitive, take the reply on a verified https App Link. The data in a `pick` reply is public timetable data.

## 6. Versions

The version is the query key `v`, or the host form `ddjourneys://v1/...`.

Within one version only additions happen: new optional keys, new reply keys and new commands. Callers ignore reply keys they do not know. A change that would break an existing caller gets a new number. The app then answers every version from `oldest` to `contract` and refuses newer ones with `unsupported_version`.

A caller can send `capabilities` first, or simply send `v=1` and handle the refusal.

The versions this build speaks, together with the versions of every other interface it uses, are shown in the app under Settings, Developer options, Interface versions, and are written at the top of the log file. See section 9.

## 7. Examples

A link, which also works from a browser or a QR code:

```
ddjourneys://plan?from=Hauptbahnhof&to=Hellerau&time=2026-10-05T08:30&mode=arr&search=1
```

From a shell on Android. `-p` addresses this app explicitly, and the link needs quoting:

```
adb shell am start -a android.intent.action.VIEW -p dev.Scharkenberg.DDjourneys -d "ddjourneys://plan?to=Hellerau\&search=0"
adb shell am start -a dev.Scharkenberg.DDjourneys.action.CONTRACT -p dev.Scharkenberg.DDjourneys --es command plan --es to Hellerau
```

Asking for a journey and receiving it on a custom scheme (Android, Kotlin):

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
val journeyJson = data?.getQueryParameter("journey") // missing when truncated=1
```

From a .NET app that references `DDjourneys.Core`, for example DDepartures:

```csharp
Uri link = ContractLinks.Pick(
    new ContractPlace("Hauptbahnhof", null, null, null),
    new ContractPlace("Hellerau", null, null, null),
    success: new Uri("ddepartures://journey"),
    error: new Uri("ddepartures://journey-error"),
    reference: "trip-42");
await Launcher.Default.OpenAsync(link);

// On the way back, read the query (Uri.UnescapeDataString for each value). "journey" holds the JSON.
```

On Windows: `Start-Process "ddjourneys://tracked"`.

## 8. The short ways in

A caller that does not want to learn the whole contract can use one of these.

| You have | Send |
|---|---|
| a destination name | `ddjourneys://go?to=Hellerau`. It starts where the user starts, now, and searches. |
| a place on a map | a `geo:51.05,13.73?q=Hellerau` link. Any maps app, browser or messenger already sends such links, and DDjourneys then runs `go` to that place. A `geo:0,0` link without a place is ignored. |
| a stop | `ddjourneys://departures?at.stop=vvo:33000028`, or `?at=Postplatz&mode=arr` for the arrivals |
| nothing in particular | `ddjourneys://departures` (the stop nearest to the user), `ddjourneys://home` or `ddjourneys://map` |
| a line | `ddjourneys://disruptions?line=S1` or `ddjourneys://live?line=3,11` |

From a .NET app that references `DDjourneys.Core`:

```csharp
await Launcher.Default.OpenAsync(ContractLinks.Go(new ContractPlace("Hellerau", null, null, null)));
await Launcher.Default.OpenAsync(ContractLinks.Departures(new ContractPlace(null, "vvo:33000028", null, null), arrivals: true));
await Launcher.Default.OpenAsync(ContractLinks.Home());
await Launcher.Default.OpenAsync(ContractLinks.Live("3,11"));
```

From Android code without any library:

```kotlin
fun open(link: String) = startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(link)).setPackage("dev.Scharkenberg.DDjourneys"))

open("ddjourneys://go?to=" + Uri.encode("Hellerau"))
open("ddjourneys://departures?at=" + Uri.encode("Postplatz"))
open("geo:51.05,13.73?q=" + Uri.encode("Hellerau"))
```

## 9. Finding out why a request did nothing

Switch on Settings, Developer options, Log to file, and send the request again. The log begins with the version of the app and the version of every interface, and then contains one line for each request:

```
[Contract v1] go accepted (request version 1)
[Contract v1] refused: place_not_found: ...
[Contract v1] go dropped: same request twice within 2 s
```

A link that never reaches the app produces no line. In that case the problem is in how it is sent: on Android check the package name in `-p` or `setPackage`, on Windows check that the app is installed, because the package registers the protocol.
