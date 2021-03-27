---
author: bcd81729d2e02d68bfecb89fb1b4e417
title: Rechnen mit Arrays
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2019-06-21T11:55:23.2327400+00:00
source: none
learningFocus: 
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 97
  failedCount: 116
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 836
  feasibilityIndexMod: 0
  difficultyRating: 60
  isPartOfBundle: true
  minEffort: 5 mins
  maxEffort: 15 mins
  features:
  - Arrays
  - Iterations
  activity: -1206
lastEdit: 2019-11-06T08:55:36.5111483+00:00

---
In dieser Aufgabe werden einfache Operationen auf Arrays geübt. Lese dazu zunächst
ein Array `a` vom Benutzer ein. Der Benutzer soll zuerst nach einer gewünschten Größe `n`
für das Array gefragt werden und anschließend nacheinander die Elemente des Arrays
eingeben. Das Array muss mindestens `2` Elemente enthalten; gibt der Benutzer eine
kleinere Größe ein, soll er auf seinen Irrtum hingewiesen und um erneute Eingabe gebeten
werden. 

Nach dem Einlesen sollen alle Elemente an geraden Positionen (=Indices) addiert und
von der Summe der Elemente an ungeraden Positionen subtrahiert werden. Beachte dabei, dass bei Arrays der erste Indice bei `0` (=gerade) beginnt.

{% output %}
> array.exe
Wieviele Elemente soll das Array beinhalten: `1`
Bitte geben Sie eine Zahl größer 1 ein!
Wieviele Elemente soll das Array beinhalten: `8`
Geben Sie nun die Elemente der Reihenfolge nach ein:
`83`
`65`
`18`
`25`
`23`
`45`
`45`
`32`

Ergebnis:
2
{% endoutput %}