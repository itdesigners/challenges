---
author: bcd81729d2e02d68bfecb89fb1b4e417
title: Pfad zerlegen
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2019-08-07T13:41:56.7193722+00:00
source: none
learningFocus: 
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 21
  failedCount: 41
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 538
  feasibilityIndexMod: 0
  difficultyRating: 75
  isPartOfBundle: false
  minEffort: 10 mins
  maxEffort: 30 mins
  features:
  - String Operations
  activity: -1735
lastEdit: 2019-08-07T13:41:56.7193722+00:00

---
Erstelle eine kleine Konsolenanwendung, welcher beim Programmstart einen Dateipfad als Parameter übergeben wird. Dieser Pfad soll dann in seine Bestandteile Verzeichnis, Name und Erweiterung zerlegt werden. Die Bestandteile sollen dann auf dem Bildschirm ausgegeben werden.

Beispiel:
{% output %}
> PathSplitting.exe
Dateipfad: `c:\Program Files (x86)\Microsoft Office\Office15\Word.exe`

Verzeichnis: c:\Program Files (x86)\Microsoft Office\Office15
Dateiname:   Word
Erweiterung: exe
{% endoutput %}