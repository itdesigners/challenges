---
author: bcd81729d2e02d68bfecb89fb1b4e417
title: Zweitgrößter Arrayeintrag
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2019-08-06T14:10:17.0037890+00:00
source: none
learningFocus: 
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 59
  failedCount: 72
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 891
  feasibilityIndexMod: 72
  difficultyRating: 55
  isPartOfBundle: true
  minEffort: 5 mins
  maxEffort: 15 mins
  features:
  - Arrays
  - Iterations
  activity: -1897
lastEdit: 2019-08-06T14:10:17.0037890+00:00

---
In dieser Aufgabe werden einfache Operationen auf Arrays geübt. Lesen Sie dazu zunächst
ein Array a vom Benutzer ein. Der Benutzer soll dazu nach einer gewünschten Größe n
für das Array gefragt werden und anschließend nacheinander die Elemente des Arrays
eingeben. Das Array muss mindestens 2 Elemente enthalten; gibt die Benutzerin eine
kleinere Größe ein, soll sie auf ihren Irrtum hingewiesen und um erneute Eingabe gebeten
werden. 

Schreiben Sie ein Programm, welches das zweitgrößte Element des Arrays findet und
ausgibt. Gehen Sie davon aus, dass es im Feld keine Wiederholungen von Zahlen
gibt.

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
`46`
`45`
`32`

Ergebnis:
65
{% endoutput %}