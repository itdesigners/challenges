---
author: 1e3fcd02b1547f847cb7fc3add4484a5
title: Kassenbuch - 1
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2016-07-17T00:00:09.0000000
source: http://ccd-school.de/coding-dojo
learningFocus: OOP
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 22
  failedCount: 95
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 231
  feasibilityIndexMod: 0
  difficultyRating: 90
  isPartOfBundle: true
  minEffort: 30 mins
  maxEffort: 2 hrs
  features: 
  activity: -289
lastEdit: 2016-07-17T00:00:09.0000000

---
Entwickle eine Anwendung zur Führung eines Kassenbuches für kleine Unternehmen. Monatsweise sollen in das Kassenbuch die "Bargeld-Bewegungen" eingetragen werden. Die Anwendung soll als Kommandozeilenanwendung realisiert werden. Gesteuert wird diese durch entsprechende Befehlsparameter.

Durch den Befehl `add` kann Geld eingelegt (Bareinlage) oder entnommen (Barentnahme) werden. Zu jeder Bewegung sind das Datum, die Art der Bewegung und der Bruttobetrag zu notieren. Die Buchung soll durch eine Sicherheitsabfrage (Ja/Nein) vom Benutzer bestätigt werden. Die Sicherheitsabfrage soll auch durch Drücken der Returntaste negativ (Nein) bestätigt werden können.

Alle durchgeführten Buchungen sollen in einer einfachen Textdatei im CSV-Format gespeichert werden. Die Textdatei hat den Dateinamen `bookings.csv`. Neue Einträge werden unabhängig vom Datum, immer am Ende hinzugefügt. Das Dateiformat kann folgendem Beispiel entnommen werden:

````
2.9.2015;Filter;-4.2
13.8.2015;Einlage;50
7.10.2015;Kaffee;-13.33
````

Beispiel:
{% output %}
> Kassenbuch.exe add 2.9.2015 Filter -4,2
Buchung wirklich anlegen? [jN] `j`
Buchung angelegt: 2.9.2015, Filter, -4,20
{% endoutput %}