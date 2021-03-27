---
author: bcd81729d2e02d68bfecb89fb1b4e417
title: FizzBuzz
ratingMethod: Fixed
category: Katas
freezeDifficultyRating: false
date: 2019-08-06T21:08:02.5403413+00:00
source: http://ccd-school.de/coding-dojo/
learningFocus: 
isDraft: false
includeTests: []
dependsOn: []
languages: []
state:
  passedCount: 80
  failedCount: 23
  hasError: false
  errorDescription: ''
  lastEditorId: 
  feasibilityIndex: 3478
  feasibilityIndexMod: 0
  difficultyRating: 20
  isPartOfBundle: false
  minEffort: 30 mins
  maxEffort: 8 hrs
  features:
  - Iterations
  activity: -14304
lastEdit: 2019-08-06T21:08:02.5403413+00:00

---
Schreibe eine Anwendung, welche den Benutzer nach einer Zahl zwischen `1` und `100` fragt. Die eingegebene Zahl soll dann auf dem Bildschirm ausgegeben werden, wobei bestimmte Zahlen dabei nach folgenden Regeln übersetzt werden sollen: 

* Für ein vielfaches von `3` gib `Fizz` statt der Zahl aus.
* Für ein vielfaches von `5` gib `Buzz` statt der Zahl aus.
* Für ein vielfaches von `3` und `5` gib `FizzBuzz` statt der Zahl aus.

Beispiele:
{% output %}
> FizzBuzz.exe
Gib eine Zahl ein: `3`
Fizz
{% endoutput %}

{% output %}
> FizzBuzz.exe
Gib eine Zahl ein: `7`
7
{% endoutput %}

{% output %}
> FizzBuzz.exe
Gib eine Zahl ein: `15`
FizzBuzz
{% endoutput %}

{% output %}
> FizzBuzz.exe
Gib eine Zahl ein: `10`
Buzz
{% endoutput %}