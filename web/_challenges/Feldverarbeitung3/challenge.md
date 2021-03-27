---
author: bcd81729d2e02d68bfecb89fb1b4e417
title: Verändertes Array
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2019-06-21T12:34:59.0724847+00:00
source: none
learningFocus: Collections
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 33
  failedCount: 57
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 580
  feasibilityIndexMod: 2
  difficultyRating: 70
  isPartOfBundle: true
  minEffort: 15 mins
  maxEffort: 45 mins
  features:
  - Arrays
  - Iterations
  activity: -1730
lastEdit: 2019-06-21T12:34:59.0724847+00:00

---
In dieser Aufgabe werden einfache Operationen auf Arrays geübt. Lesen Sie dazu zunächst
ein Array a vom Benutzer ein. Der Benutzer soll dazu nach einer gewünschten Größe n
für das Array gefragt werden und anschließend nacheinander die Elemente des Arrays
eingeben. Das Array muss mindestens 2 Elemente enthalten; gibt die Benutzerin eine
kleinere Größe ein, soll sie auf ihren Irrtum hingewiesen und um erneute Eingabe gebeten
werden. Nutzen Sie diesen Teil des Programms für die folgenden Teilaufgaben.

Schreiben Sie ein Programm, welches jeweils zwei benachbarte Elemente addiert
und das erste mit der Summe überschreibt. Hat das Array eine ungerade Anzahl
Elemente, so bleibt am Ende des Arrays ein einzelnes Element übrig. Dieses soll
unverändert bleiben. Das Ergebnis-Feld soll ausgegeben werden.

{% output %}
> array.exe
Wieviele Elemente soll das Array beinhalten: `1`
Bitte geben Sie eine Zahl größer 1 ein!
Wieviele Elemente soll das Array beinhalten: `4`
Geben Sie nun die Elemente der Reihenfolge nach ein:
`4`
`3`
`7`
`1`

Ergebnis:
[7, 3, 8, 1]
{% endoutput %}