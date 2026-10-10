# DDjourneys

DDjourneys is a journey planner for buses, trams and trains in and around Dresden. You tell it where you are going and when, and it shows you how to get there, what is late, and where your vehicle is right now. It runs on Android phones and on Windows.

I wrote it because I wanted something like the old Android app Öffi, but made for the timetable data of the Verkehrsverbund Oberelbe (VVO) and in a shape that fits current phones and works on Windows too. It is not an official app of the VVO or the DVB, and it is not made or actively endorsed by either of them.

## Getting started

Open the app and you land on the planner. Tap the field for the start and type a few letters, for example "Hauptbahnhof". Pick the stop from the list. Do the same for the destination, say "Hellerau". Press the search button and you get a list of journeys.

If you are standing at the start, tap the small location symbol next to the start field instead of typing. The app asks for permission to use your position and then fills in the stop nearest to you. If you would rather start from the exact address you are at, there is a switch for that under Settings, in the place search section.

Below the fields you can change the time. The row of buttons moves it back an hour, back a quarter of an hour, to now, forward a quarter of an hour or forward an hour. Next to the time you choose whether you want to leave at that time or arrive by it. If you have to be at work at 8:30, choose "Arrive by", set 8:30, and the app works backwards.

Stops, addresses and sights all work as start or destination. In the settings you can switch addresses and sights off if you only want stops.

## The list of journeys

Each journey in the list shows the times, the stops, and a row of small tags with the lines you ride. Swipe that row sideways if it does not fit. At the bottom of the list you can ask for earlier or later journeys. The bookmark symbol saves the route, so you find it again at the top of the planner under saved routes. Routes you searched for before are listed there too.

## One journey in detail

Tap a journey to see it as a timeline. Every ride has a line, a direction, the stops where you get on and off, the platform, and the delay if there is one. Dots next to a time show how full the vehicle is, when the provider knows. At a change the app tells you how long you wait, whether you have to walk to another stop, and when the connection is tight or already lost.

Below the line of each ride there are up to three buttons. "Earlier" and "Later" look for another vehicle for that ride alone and keep the rest of your journey as far as it still fits; where it no longer does, the rest is planned again. The button with the map pin opens a map with the vehicle of this ride on it. That works for lines with a plain number, such as tram 11 or bus 61, and only when somebody has reported the position of the vehicle, so a vehicle can be missing even though it is running.

The route at the top of the page stays in place while the rest scrolls. Its icons refresh the journey, share it as text or as a picture and, if you switched on the technical details under Developer options, open them. Pulling the page down refreshes it too: the app asks the provider again and shows the same connection with its current times, or tells you that the provider no longer offers it. The bell in the card below follows the journey, which is explained further down. The other symbols there open it as a PDF or hand it to another app. The map symbol shows the whole route on a map without following a vehicle. Tickets and prices are at the bottom; the app shows the single and day ticket the provider quotes for this journey, and nothing else, because the provider quotes nothing else. Where the transport authority names a sale page, the price row can open it; the app itself sells nothing.

## Departures

Open departures from the planner and choose a stop. You get the next departures with their delays. Switch to arrivals if you are waiting for someone. Tap a departure to see all stops of that vehicle's run. The section "Around this stop" opens a map of the surroundings, tells you how accessible the stop is according to the city, and lists the lines that serve it; the map button next to a line shows where that line actually goes, in both directions. On the page of one run, a second button beside the map shows the whole line instead of just that one run.

## Disruptions

The disruptions page lists what the VVO reports: construction work, diversions, cancelled services. Tap an entry to read it. Entries that the journey planner takes into account are marked as such.

## Following a journey

If you press the bell on a journey, the app watches it for you. Shortly before you leave it sends a notification, and it sends another when something changes, such as a delay that makes you miss a connection or a vehicle that is cancelled. While you are on the way, a notification shows the next stop and when to get off. When a connection is missed, the notification can open a new search at once, from the stop where it broke to your destination. You can pause or stop following at any time. The page "Followed journeys" lists everything you follow, with the lines as the same coloured tags as in the list of journeys; the ride you are on has an outline in the accent colour.

Following works with the VVO provider only. The app hands the journey to a service run by the DVB, which keeps watching it. That service gives the app an anonymous token instead of an account. There is no login, and I never see who you are.

## Map

Under Settings, Map you choose which engine draws the map. CARTO is the sharp vector map in the colours of the app. It needs a key from CARTO, the company that makes the map background, and a phone whose web view can run WebGL 2. The app does not come with a key of its own. You get one on the CARTO website and paste it under Settings, Map. Leaflet is the simple map. It shows ready-made picture tiles from OpenStreetMap, needs no key and runs on old phones too, but only light and dark follow the theme of the app. Both engines show the same journeys, stops and vehicles, and on the map that the map symbol in the header of the planner opens (also under Settings, Map), the layers button at the top right can show the tariff zones of the VVO, the park and ride sites with how many spaces are free, the shared bikes with how many are ready to take and the vehicles moving around right now, and opens the line network map, which is downloaded once and then also works offline.

If the phone cannot draw the CARTO map, the app says so instead of showing an empty square, and offers to use Leaflet instead or to try anyway. Trying anyway can end the app. If that happens the app remembers it and keeps the CARTO map off until you try again or save the key again. Everything else in the app works without a map.

When the map shows the surroundings, stops appear once you zoom in. Tap a stop to see its departures or to plan a journey to or from it.

## Android widgets, quick actions and Windows widgets

On Android you can put widgets on your home screen: a route you travel often, the departures or arrivals of one stop, the stops around you with their distance, or the next departures from the stops around you. Each widget has its own settings, such as its provider, its title and how many rows it shows. A tap on a widget refreshes it. Android allows widgets to update every thirty minutes at most, and widgets in the background use the last position the phone knows unless you allow location access all the time.

Press and hold the app icon to find two quick actions. "Take me home" plans a journey from the stop nearest to you to your home stop. "Departures from here" shows the departures at the stop nearest to you. You set your home in the planner: tap the home button above the list of recent places, or the pencil next to it once a home is set, and search for a stop, an address or a place the way you do for a start. On Windows the same actions are in the jump list of the app.

Windows has its own widgets board. There the app offers two kinds of widget: the departures of a stop and the next journeys of a route. You pin them from the board itself, and their settings are made in the app; the widget can also send you straight to the departures of its stop or the plan of its route. While the app runs, the widgets are refreshed every thirty minutes, and when the board asks for them it is served the last picture it was given. When the app is fully closed there is no background work: the board shows the last picture and says so, and which widgets are offered at all varies by the Windows region and settings.

## Settings

Under Appearance you choose light, dark, the system setting, or the times of sunrise and sunset as the switch between light and dark (computed for the provider's city; no location permission), plus a colour set, a font, and how tightly the screens are packed. On Windows you can also choose a window material: Mica is the calm one and stays close to the colours of the app, Mica Alt is the more pronounced one and lets more of the wallpaper and the accent colour through. Language is German or English. Route preferences cover the kinds of vehicles, how many changes you accept, how fast you walk, what you need for accessibility, and how long the journey should stay at a stop over; from three minutes on, journeys that only pass through the stop over drop out. Options that the selected provider cannot use do not appear. The provider page lets you switch between the VVO and the TRIAS interface of the VVO. The second one is marked as experimental because it does less and I have tested it less.

Under Start you can make the app open with the start already filled in and the destination search ready, so that a trip home takes two taps.

## Using it from other apps

Other apps can open DDjourneys with a link, for example `ddjourneys://go?to=Hellerau` plans a journey to Hellerau starting now. Any app that sends a map location, a `geo:` link, can hand the place to DDjourneys on Android. The details for developers are in `docs/EXTERNAL_CONTRACT.md`.

## Where the data comes from

Journeys, departures and disruptions come from the interfaces of the VVO. Following a journey uses the DVB service mentioned above. Live positions come from the TLMS community network, which collects radio telegrams and GPS positions, so it is incomplete by nature. The live vehicles on the plain map come from the same network. Accessibility of stops and service points come from the open data of the city of Dresden. The park and ride sites with their free spaces come from the open data of the VVO. The shared bikes and their counts come from the MOBIbike network of Nextbike. The map background comes from CARTO and OpenStreetMap, drawn by the open source libraries MapLibre and Leaflet. The line network map is downloaded from the DVB website; all rights to it stay with the DVB. The timetable data belongs to the VVO and the DVB.

## Privacy

The app has no account, no advertising and no analytics. It keeps your settings, places and routes on your phone. Your position is used only when you ask for it, when a widget or a quick action needs it, and is sent to the VVO as a coordinate to find the nearest stop. Searches go to the VVO as you type. Nothing is sent to me.

## When something does not work

Under Settings you find Developer options. If you switch them on you can record a log file that lists every request the app makes and what came back. Delete it when you are done. The log starts with the version of the app and the version of every interface it talks to, which makes it much easier to find the cause. If you report a problem on the project page on GitHub, github.com/Scharkenberg/DDjourneys, attach the log and say what you did. (The app does not log actions or user data otherwise.)

## Licence

DDjourneys is free software under the MIT licence. The text is in the file LICENSE.txt.
