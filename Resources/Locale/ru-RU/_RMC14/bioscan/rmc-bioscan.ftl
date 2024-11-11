rmc-bioscan-ares-announcement = [color=white][font size=16][bold]Статус биосканирования ARES v3.2[/bold][/font][/color][color=red][font size=14][bold]
    {$message}[/bold][/font][/color]

rmc-bioscan-ares = Биосканирование завершено.

  Датчики указывают { $shipUncontained ->
    [0] отсутствие
    *[other] {$shipUncontained}
  } неизвестных форм жизни { $shipUncontained ->
    [0] сигнатур
    [1] сигнатуру
    *[other] сигнатур
  } на корабле{ $shipLocation ->
    [none] {""}
    *[other], включая одну в {$shipLocation},
  } и { $onPlanet ->
    [0] отсутствие
    *[other] примерно {$onPlanet}
  } { $onPlanet ->
    [0] сигнатур
    [1] сигнатуру
    *[other] сигнатур
  } в других местах{ $planetLocation ->
    [none].
    *[other], включая одну в {$planetLocation}
  }

rmc-bioscan-xeno-announcement = [color=#318850][font size=14][bold]Материнская Королева простирается в ваш разум из далёких миров.
  {$message}[/bold][/font][/color]

rmc-bioscan-xeno = Моим детям и их Королеве: Я чувствую { $onShip ->
  [0] отсутствие хозяев
  [1] примерно 1 хозяина
  *[other] примерно {$onShip} хозяев
} в металлическом улье{ $shipLocation ->
  [none] {""}
  *[other], включая одного в {$shipLocation},
} и {$onPlanet ->
  [0] отсутствие
  *[other] {$onPlanet}
} разбросанных в других местах{$planetLocation ->
  [none].
  *[other], включая одного в {$planetLocation}
}