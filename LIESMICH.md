# DDjourneys

DDjourneys ist eine Fahrplanauskunft für Busse, Straßenbahnen und Züge in und um Dresden. Sie sagen der App, wohin Sie wollen und wann, und sie zeigt Ihnen, wie Sie dorthin kommen, was sich verspätet und wo Ihr Fahrzeug gerade ist. Sie läuft auf Android-Telefonen und unter Windows.

Ich habe sie geschrieben, weil ich etwas wie die alte Android-App Öffi haben wollte, aber für die Fahrplandaten des Verkehrsverbunds Oberelbe (VVO) und in einer Form, die zu heutigen Telefonen passt und auch unter Windows läuft. Sie ist keine offizielle App des VVO oder der DVB und wird von keinem von beiden hergestellt oder ausdrücklich empfohlen.

## Erste Schritte

Öffnen Sie die App, und Sie landen in der Planung. Tippen Sie auf das Feld für den Start und geben Sie ein paar Buchstaben ein, zum Beispiel "Hauptbahnhof". Wählen Sie die Haltestelle aus der Liste. Machen Sie dasselbe für das Ziel, etwa "Hellerau". Drücken Sie auf die Suchtaste, und Sie erhalten eine Liste von Verbindungen.

Wenn Sie schon am Start stehen, tippen Sie statt der Eingabe auf das kleine Standortsymbol neben dem Startfeld. Die App fragt nach der Erlaubnis, Ihre Position zu verwenden, und trägt dann die Haltestelle ein, die Ihnen am nächsten ist. Wenn Sie lieber von der genauen Adresse starten möchten, an der Sie stehen, gibt es dafür unter Einstellungen im Abschnitt Ortssuche einen Schalter.

Unter den Feldern können Sie die Zeit ändern. Die Tastenreihe verschiebt sie um eine Stunde zurück, eine Viertelstunde zurück, auf jetzt, eine Viertelstunde vor oder eine Stunde vor. Neben der Zeit wählen Sie, ob Sie zu dieser Zeit abfahren oder bis dahin ankommen wollen. Wenn Sie um 8:30 Uhr bei der Arbeit sein müssen, wählen Sie "Ankunft", stellen 8:30 ein, und die App rechnet rückwärts.

Haltestellen, Adressen und Sehenswürdigkeiten funktionieren alle als Start oder Ziel. In den Einstellungen können Sie Adressen und Sehenswürdigkeiten abschalten, wenn Sie nur Haltestellen wollen.

## Die Liste der Verbindungen

Jede Verbindung in der Liste zeigt die Zeiten, die Haltestellen und eine Reihe kleiner Schilder mit den Linien, die Sie fahren. Wischen Sie diese Reihe zur Seite, wenn sie nicht passt. Am Ende der Liste können Sie frühere oder spätere Verbindungen anfordern. Das Lesezeichen-Symbol speichert die Route, sodass Sie sie oben in der Planung unter den gespeicherten Routen wiederfinden. Routen, nach denen Sie früher gesucht haben, stehen dort ebenfalls.

## Eine Verbindung im Detail

Tippen Sie auf eine Verbindung, um sie als Zeitleiste zu sehen. Jede Fahrt hat eine Linie, eine Richtung, die Haltestellen zum Ein- und Aussteigen, das Gleis oder den Steig und die Verspätung, falls es eine gibt. Punkte neben einer Zeit zeigen, wie voll das Fahrzeug ist, wenn der Anbieter es weiß. Bei einem Umstieg sagt die App, wie lange Sie warten, ob Sie zu einer anderen Haltestelle laufen müssen und wann der Anschluss knapp oder schon verloren ist.

Unter der Linie jeder Fahrt gibt es bis zu drei Tasten. "Früher" und "Später" suchen für diese Fahrt allein ein anderes Fahrzeug und lassen den Rest Ihrer Verbindung bestehen, soweit er noch passt; wo er nicht mehr passt, wird der Rest neu geplant. Die Taste mit der Kartennadel öffnet eine Karte mit dem Fahrzeug dieser Fahrt. Das funktioniert bei Linien mit einfacher Nummer, wie Straßenbahn 11 oder Bus 61, und nur dann, wenn jemand die Position des Fahrzeugs gemeldet hat. Ein Fahrzeug kann also fehlen, obwohl es fährt.

Die Route oben auf der Seite bleibt stehen, während der Rest scrollt. Ihre Symbole aktualisieren die Verbindung, teilen sie als Text oder als Bild und öffnen, wenn Sie die technischen Details unter Entwickleroptionen eingeschaltet haben, diese. Auch das Herunterziehen der Seite aktualisiert: Die App fragt den Anbieter erneut und zeigt dieselbe Verbindung mit den aktuellen Zeiten, oder sie sagt Ihnen, dass der Anbieter sie nicht mehr anbietet. Die Glocke in der Karte darunter verfolgt die Verbindung, was weiter unten erklärt wird. Die anderen Symbole dort öffnen sie als PDF oder reichen sie an eine andere App weiter. Das Kartensymbol zeigt die ganze Route auf einer Karte, ohne ein Fahrzeug zu verfolgen. Fahrkarten und Preise stehen ganz unten. Die App zeigt die Einzel- und die Tageskarte, die der Anbieter für diese Verbindung nennt, und sonst nichts, weil der Anbieter sonst nichts nennt. Wo der Verkehrsverbund eine Verkaufsseite nennt, kann die Zeile mit dem Preis sie öffnen; die App selbst verkauft nichts.

## Abfahrten

Öffnen Sie die Abfahrten aus der Planung und wählen Sie eine Haltestelle. Sie erhalten die nächsten Abfahrten mit ihren Verspätungen. Schalten Sie auf Ankünfte um, wenn Sie auf jemanden warten. Tippen Sie auf eine Abfahrt, um alle Haltestellen der Fahrt dieses Fahrzeugs zu sehen. Der Abschnitt "Rund um diese Haltestelle" öffnet eine Karte der Umgebung, sagt Ihnen, wie barrierefrei die Haltestelle laut Stadt ist, und listet die Linien, die dort halten; der Karten-Knopf neben einer Linie zeigt, wo diese Linie wirklich hinfährt, in beiden Richtungen. Auf der Seite einer Fahrt zeigt ein zweiter Knopf neben der Karte die ganze Linie statt nur dieser einen Fahrt.

## Störungen

Die Seite Störungen listet, was der VVO meldet: Bauarbeiten, Umleitungen, ausgefallene Fahrten. Tippen Sie auf einen Eintrag, um ihn zu lesen. Einträge, die die Fahrplanauskunft berücksichtigt, sind entsprechend gekennzeichnet.

## Eine Verbindung verfolgen

Wenn Sie die Glocke an einer Verbindung drücken, beobachtet die App sie für Sie. Kurz vor der Abfahrt schickt sie eine Benachrichtigung, und sie schickt eine weitere, wenn sich etwas ändert, etwa eine Verspätung, durch die Sie einen Anschluss verpassen, oder ein Fahrzeug, das ausfällt. Während Sie unterwegs sind, zeigt eine Benachrichtigung die nächste Haltestelle und wann Sie aussteigen müssen. Wird ein Anschluss verpasst, kann die Benachrichtigung sofort eine neue Suche öffnen, von der Haltestelle, an der er wegbrach, bis zu Ihrem Ziel. Sie können das Verfolgen jederzeit anhalten oder beenden. Die Seite "Verfolgte Verbindungen" listet alles auf, was Sie verfolgen, mit den Linien als dieselben farbigen Marken wie in der Liste der Verbindungen; die Fahrt, auf der Sie sind, hat einen Rand in der Akzentfarbe.

Das Verfolgen funktioniert nur mit dem Anbieter VVO. Die App übergibt die Verbindung an einen Dienst der DVB, der sie weiter beobachtet. Dieser Dienst gibt der App statt eines Kontos eine anonyme Kennung. Es gibt keine Anmeldung, und ich erfahre nie, wer Sie sind.

## Karte

Unter Einstellungen, Karte wählen Sie, welches Kartenmodul die Karte zeichnet. CARTO ist die scharfe Vektorkarte in den Farben der App. Sie braucht einen Schlüssel von CARTO, dem Unternehmen, das den Kartenhintergrund herstellt, und ein Telefon, dessen WebView WebGL 2 ausführen kann. Die App bringt keinen eigenen Schlüssel mit. Sie bekommen einen auf der CARTO-Website und tragen ihn unter Einstellungen, Karte ein. Leaflet ist die einfache Karte. Sie zeigt fertige Bildkacheln von OpenStreetMap, braucht keinen Schlüssel und läuft auch auf alten Telefonen, aber nur Hell und Dunkel folgen dem Design der App. Beide Module zeigen dieselben Verbindungen, Haltestellen und Fahrzeuge, und auf der schlichten Karte kann der Ebenen-Knopf die Tarifzonen des VVO, die Park-and-Ride-Plätze mit ihren freien Plätzen, die Leihfahrräder mit ihren freien Rädern und die Fahrzeuge, die gerade unterwegs sind, einblenden und den Liniennetzplan öffnen, der einmal geladen wird und danach auch offline funktioniert.

Kann das Telefon die CARTO-Karte nicht zeichnen, sagt die App das, statt ein leeres Feld zu zeigen, und bietet an, stattdessen Leaflet zu verwenden oder es trotzdem zu versuchen. Der Versuch kann die App beenden. Passiert das, merkt sich die App es und lässt die CARTO-Karte aus, bis Sie es noch einmal versuchen oder den Schlüssel erneut speichern. Alles andere in der App funktioniert ohne Karte.

Wenn die Karte die Umgebung zeigt, erscheinen Haltestellen, sobald Sie hineinzoomen. Tippen Sie auf eine Haltestelle, um ihre Abfahrten zu sehen oder eine Verbindung dorthin oder von dort zu planen.

## Android-Widgets, Schnellaktionen und Windows-Widgets

Unter Android können Sie Widgets auf den Startbildschirm legen: eine Route, die Sie oft fahren, die Abfahrten oder Ankünfte einer Haltestelle, die Haltestellen in Ihrer Nähe mit ihrer Entfernung oder die nächsten Abfahrten von den Haltestellen in Ihrer Nähe. Jedes Widget hat eigene Einstellungen, etwa seinen Anbieter, seinen Titel und wie viele Zeilen es zeigt. Ein Tippen auf ein Widget aktualisiert es. Android erlaubt Widgets höchstens alle dreißig Minuten eine Aktualisierung, und Widgets im Hintergrund verwenden die zuletzt bekannte Position des Telefons, solange Sie den Standortzugriff nicht dauerhaft erlauben.

Halten Sie das App-Symbol gedrückt, um zwei Schnellaktionen zu finden. "Nach Hause" plant eine Verbindung von der Haltestelle, die Ihnen am nächsten ist, zu Ihrer Heimat-Haltestelle. "Abfahrten von hier" zeigt die Abfahrten an der Haltestelle, die Ihnen am nächsten ist. Ihr Zuhause legen Sie in der Planung fest: Tippen Sie auf die Haustaste über der Liste der letzten Orte, oder auf den Stift daneben, sobald ein Zuhause gesetzt ist, und suchen Sie eine Haltestelle, eine Adresse oder einen Ort so, wie Sie es für einen Start tun. Unter Windows stehen dieselben Aktionen in der Sprungliste der App.

Windows hat eine eigene Widget-Leiste. Dort bietet die App zwei Arten von Widget an: die Abfahrten einer Haltestelle und die nächsten Fahrten einer Verbindung. Angeheftet wird in der Leiste selbst, eingerichtet wird in der App; ein Widget kann Sie auch direkt zu den Abfahrten seiner Haltestelle oder zur Planung seiner Verbindung schicken. Während die App läuft, werden die Widgets alle dreißig Minuten aktualisiert, und wenn die Leiste nachfragt, bekommt sie das zuletzt gespeicherte Bild. Bei geschlossener App gibt es keine Hintergrundarbeit: die Leiste zeigt das letzte Bild und sagt dies, und welche Widgets überhaupt angeboten werden, hängt von Region und Einstellungen von Windows ab.

## Einstellungen

Unter Darstellung wählen Sie hell, dunkel, die Systemeinstellung oder die Zeiten von Sonnenauf- und -untergang als Wechsel zwischen hell und dunkel (berechnet für die Stadt des Anbieters; ohne Standortberechtigung), dazu einen Farbsatz, eine Schrift und wie dicht die Bildschirme gepackt sind. Unter Windows können Sie außerdem ein Fenstermaterial wählen: Mica ist das ruhige und bleibt nah an den Farben der App, Mica Alt ist das ausgeprägtere und lässt mehr vom Hintergrundbild und von der Akzentfarbe durch. Als Sprache stehen Deutsch und Englisch zur Verfügung. Die Routenvorgaben betreffen die Fahrzeugarten, wie viele Umstiege Sie akzeptieren, wie schnell Sie gehen, was Sie für die Barrierefreiheit brauchen und wie lange die Verbindung an einem Zwischenhalt halten soll; ab drei Minuten fallen Verbindungen heraus, die dort nur durchfahren. Optionen, die der gewählte Anbieter nicht nutzen kann, erscheinen nicht. Auf der Anbieterseite wechseln Sie zwischen den Schnittstellen VVO und TRIAS des VVO. Die zweite ist als experimentell gekennzeichnet, weil sie weniger kann und ich sie weniger getestet habe.

Unter Start können Sie die App so einstellen, dass sie mit schon ausgefülltem Start und bereiter Zielsuche öffnet, sodass eine Fahrt nach Hause zwei Tippen braucht.

## Aus anderen Apps nutzen

Andere Apps können DDjourneys mit einem Link öffnen. Zum Beispiel plant `ddjourneys://go?to=Hellerau` eine Verbindung nach Hellerau mit Start jetzt. Jede App, die einen Kartenort sendet, also einen `geo:`-Link, kann den Ort unter Android an DDjourneys übergeben. Die Einzelheiten für Entwickler stehen in `docs/EXTERNAL_CONTRACT.md`.

## Woher die Daten kommen

Verbindungen, Abfahrten und Störungen kommen aus den Schnittstellen des VVO. Das Verfolgen einer Verbindung nutzt den oben erwähnten Dienst der DVB. Die Live-Positionen kommen aus dem Gemeinschaftsnetz TLMS, das Funktelegramme und GPS-Positionen sammelt und deshalb von Natur aus unvollständig ist. Die Live-Fahrzeuge auf der schlichten Karte kommen aus demselben Netz. Die Barrierefreiheit von Haltestellen und Servicestellen stammt aus den offenen Daten der Stadt Dresden. Die Park-and-Ride-Plätze mit ihren freien Plätzen kommen aus den offenen Daten des VVO. Die Leihfahrräder und ihre Zahlen kommen aus dem MOBIbike-Netz von Nextbike. Der Kartenhintergrund kommt von CARTO und OpenStreetMap und wird von den quelloffenen Bibliotheken MapLibre und Leaflet gezeichnet. Der Liniennetzplan wird von der Website der DVB geladen; alle Rechte daran bleiben bei der DVB. Die Fahrplandaten gehören dem VVO und der DVB.

## Datenschutz

Die App hat kein Konto, keine Werbung und keine Auswertung. Sie speichert Ihre Einstellungen, Orte und Routen auf Ihrem Telefon. Ihre Position wird nur verwendet, wenn Sie danach fragen, wenn ein Widget oder eine Schnellaktion sie braucht, und sie geht als Koordinate an den VVO, um die nächste Haltestelle zu finden. Suchen gehen beim Tippen an den VVO. An mich wird nichts gesendet.

## Wenn etwas nicht funktioniert

Unter Einstellungen finden Sie die Entwickleroptionen. Wenn Sie sie einschalten, können Sie eine Protokolldatei aufzeichnen, die jede Anfrage der App und die Antwort darauf auflistet. Löschen Sie sie, wenn Sie fertig sind. Das Protokoll beginnt mit der Version der App und der Version jeder Schnittstelle, mit der sie spricht, was die Suche nach der Ursache erheblich erleichtert. Wenn Sie ein Problem auf der Projektseite bei GitHub melden, github.com/Scharkenberg/DDjourneys, hängen Sie das Protokoll an und schreiben Sie, was Sie getan haben. (Die App protokolliert sonst weder Handlungen noch Nutzerdaten.)

## Lizenz

DDjourneys ist freie Software unter der MIT-Lizenz. Der Text steht in der Datei LICENSE.txt.
