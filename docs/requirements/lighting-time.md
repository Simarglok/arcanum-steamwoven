# Время мира и освещение — функциональные требования

**Срез исследования:** 2026-10-02. **Объём:** игровое время, день/ночь, глобальный и локальный свет, его связь с соответствующими проверками. Это ограниченная спецификация, не аудит всех систем игры. Погода, полный планировщик заданий/AI и движок за пределами непосредственно подтверждённых зависимостей.

**Уровни утверждений.** «Наблюдение CE» означает только прочитанный код `arcanum-ce` на `a7ff41b300ef712f0e7d088183a3d08957110cdd`; это не отдельное измерение оригинального исполняемого файла. «Предложение проекта» — функциональная политика для независимой реализации. «Неизвестно» — поведение, которое нельзя закрепить без дополнительной проверки данных или оригинального runtime. Требования и сценарии ниже предложены и не реализованы этим документом.

**Снимок Steamwoven:** проверен фактический `main` на `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (PR #36). Каталог [статуса проекта](../project/status.md) всё ещё указывает базу `0bc9962f90aed467b09ca977206ce456e31a4616`, через PR #30; поэтому покрытие ниже сверено с кодом текущего среза, а не выведено из устаревшей строки каталога. Кодовые ссылки в отчёте закреплены на этих двух SHA.

## Требования

<a id="fr-light-001"></a>

### FR-LIGHT-001 — единые игровые часы и граница дня/ночи

**Предлагаемая фаза: Pre-alpha.** Симуляция должна иметь одни игровые дату и время. Системы, которым нужны часы или признак дня, читают их из этого состояния, а не вычисляют собственный таймер. Игроку должны быть доступны текущее игровое время и состояние «день/ночь» в предусмотренном проектом интерфейсе.

**Наблюдение CE:** `timeevent_ping` хранит игровые и анимационные часы отдельно от реального времени; `datetime_current_hour/minute/day` читают игровые часы, а `game_time_is_day` считает дневным интервал `[06:00, 18:00)`. Интерфейсный callback обновляет игровой циферблат и планирует следующее обновление. [Часы и календарные accessors](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L235-L375), [day predicate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L467-L471), [clock UI callback](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L5393-L5408). CE-source не доказывает, что это исчерпывающая проверка оригинального runtime.

**Зависимости:** сериализуемое состояние игровых часов; профиль совместимости для календаря и порогов; единый интерфейс чтения времени для симуляции и отображения. **Предложенные сценарии:** проверить границы 05:59/06:00 и 17:59/18:00 на CE-профиле; перевести часы через полночь и убедиться, что интерфейс, освещение и потребители признака дня видят одно состояние.

**Steamwoven на `b1245e5`:** в проверенных источниках Data, Simulation и terrain viewer игрового clock/day-night состояния нет; текущий viewer показывает диагностическую сцену, а не игровой интерфейс. [Каталог текущего bounded scope](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L12-L25). Покрытие требования отсутствует.

<a id="fr-light-002"></a>

### FR-LIGHT-002 — ход времени, остановка и явное ожидание

**Предлагаемая фаза: Pre-alpha.** Игровые часы продвигаются по правилам выбранного профиля времени. Когда профиль приостанавливает симуляцию, игровые часы стоят и сами по себе не приближают будущий game-time deadline; явное действие ожидания/сна продвигает время на указанную длительность и обновляет состояние освещения до того, как мир снова показывается игроку.

**Наблюдение CE:** `timeevent_ping` добавляет `8 × delta` к игровым часам вне пошагового боя и при открытом внутреннем gate; при пошаговом бое игровые часы не меняются, хотя анимационные меняются. Сам `delta` пропускается ниже 5 ms и ограничивается 250 ms, поэтому множитель 8 нельзя трактовать как безусловную скорость относительно реального времени. Сон прибавляет игровой час явно; ambient update выключается на время затемнения и возобновляется при пробуждении. [Тик и условия продвижения](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L801-L842), [шаг сна](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L591-L640), [затемнение и обновление при пробуждении](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L670-L719). Внутренний gate с неинформативным именем не раскрывает полного набора причин остановки.

**Границы очередей.** CE после обновления часов отдельно обходит real-time, game-time и animation queues ([queue loop](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L843-L897)). При остановке game clock уже наступивший game-time callback всё ещё может быть обработан; остановка часов не доказывает глобальную блокировку callbacks. Предлагаемая приёмка различает будущий deadline, уже due event, real-time и animation event при gate/TB. Если проект приостанавливает все callbacks, это отдельная project policy, а не вывод из CE.

**Зависимости:** профиль скорости времени, пошаговый бой и другие состояния остановки, очередь игровых событий и операции ожидания. **Предложенные сценарии:** render-frame пауза не меняет игровое время; приостановка профиля блокирует часовой переход; ожидание на час продвигает ровно на час по выбранной политике и снимает затемнение только после синхронизации света. Число `8×` следует принять лишь после решения о целевом профиле и проверке оригинального runtime.

**Steamwoven на `b1245e5`:** в проверенных Simulation/UI источниках нет игрового времени, паузы или ожидания. Таймлайн ART-анимации — отдельный presentation-инструмент и не покрывает это требование. [Каталог bounded scope](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L8-L13). Покрытие отсутствует.

<a id="fr-light-003"></a>

### FR-LIGHT-003 — почасовая схема ambient-цветов

**Предлагаемая фаза: Pre-alpha.** Активная схема содержит отдельные RGB-цвета окружения для внутренних и наружных поверхностей на каждый час суток. Цвета поступают из версионированных игровых данных и адресуются идентификатором схемы. Числа из исходных ресурсов не должны встраиваться в код как универсальная палитра.

**Наблюдение CE:** `light_scheme_mod_load` требует индексный каталог lighting schemes; `light_scheme_set` разрешает id схемы в соответствующий файл данных, а `light_scheme_parse` читает 24 почасовые записи вида outdoor RGB + indoor RGB. [Загрузка и выбор схемы](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L111-L121), [разрешение id в данные](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L213-L254), [формат почасовой записи](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L331-L371).

**Зависимости:** идентификаторы и содержимое таблицы схем; шестиканальная запись на час; безопасная проверка входных диапазонов и полноты. **Предложенные сценарии:** синтетическая схема с различными indoor/outdoor компонентами выдаёт 24 независимых пары и не меняет данные исходного ресурса; отсутствующий файл или некорректная запись дают явную диагностическую ошибку вместо молчаливой правдоподобной палитры.

**Steamwoven на `b1245e5`:** таблица lighting scheme и её интерпретация отсутствуют. MES reader для terrain применяется к ограниченным картографическим записям и не является lighting scheme loader. [Текущий bounded terrain loader](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainLoader.cs#L228-L255). Покрытие отсутствует.

<a id="fr-light-004"></a>

### FR-LIGHT-004 — пустые часы и проверяемая интерполяция данных схемы

**Предлагаемая фаза: Pre-alpha.** Если формат профиля допускает пустые часовые записи, нормализация схемы должна определять их цвет до применения схемы и документировать правила для начального и конечного пустых диапазонов. Этот шаг заполняет временную таблицу; он сам по себе не означает плавного перехода изображения между минутами.

**Наблюдение CE:** в `light_scheme_parse` непустая запись задаёт следующий якорь, пустые записи помечают разрыв, а при встрече последующего непустого якоря промежуточные каналы заполняются целочисленными шагами. Код не замыкает диапазон после последнего якоря и не устанавливает циклический wrap-around; массив до разбора выделяется нулевым. Поэтому поведение краёв требует подтверждения на допустимых исходных данных и не обобщается как циклическая интерполяция. [Парсер и заполнение разрыва](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L331-L413), [нулевая инициализация таблицы](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L85-L92).

**Зависимости:** подтверждённая грамматика схемы и правила её крайних слотов. **Предложенные сценарии:** на синтетических якорях подтвердить промежуточное значение каждого канала и округление; отдельно зафиксировать ожидаемое поведение пустых слотов до первого и после последнего якоря; reject или безопасно диагностировать таблицу без достаточных якорей.

**Steamwoven на `b1245e5`:** интерполятора и модели схем нет. Текущая палитра ART для отладочной отрисовки не покрывает цветовую схему мира. [Декодер ART и неохваченные эффекты](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/art.md#L74-L86). Покрытие отсутствует.

<a id="fr-light-005"></a>

### FR-LIGHT-005 — почасовое применение схемы

**Предлагаемая фаза: Pre-alpha.** При смене игрового часа renderer получает почасовую пару indoor/outdoor текущей схемы и обновляет зависимые поверхности. В CE-derived compatibility profile это дискретное применение по часовому событию. Интерполяция пробелов в конфигурации из FR-LIGHT-004 не должна выдаваться за непрерывный runtime-переход между часами.

**Наблюдение CE и цепочка:** ambient callback считывает `datetime_current_hour()`, вызывает `light_scheme_set_hour()`, затем планирует следующее событие через 3 600 000 игровых миллисекунд. `light_scheme_set_hour()` вызывает `light_set_colors()`, которое обновляет ambient palettes и инвалидирует отрисовку. [Почасовой callback и расписание](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/anim_ui.c#L242-L275), [применение часа](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L271-L303), [обновление цветов и invalidation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L362-L376). Значение `3 600 000` здесь — задержка в шкале игрового времени, а не 60 минут реального времени.

**Зависимости:** clock и схема из FR-LIGHT-001/003; события игрового времени; инвалидирование palette/light buffers. **Предложенные сценарии:** в синтетической схеме цвета неизменны между двумя игровыми часовыми границами, меняются ровно при переходе часа и после восстановления из паузы сразу соответствуют текущему времени.

**Steamwoven на `b1245e5`:** hourly callback, palette application и world-light invalidation не реализованы; текущий terrain viewer не имеет runtime clock. [Viewer draw path](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/tools/terrain-viewer/Godot/TerrainCanvas.cs#L120-L140), [явно отсутствующие эффекты в контракте viewer](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/rendering/terrain-viewer.md#L103-L111). Покрытие отсутствует.

<a id="fr-light-006"></a>

### FR-LIGHT-006 — indoor/outdoor цвет на соответствующих поверхностях

**Предлагаемая фаза: Pre-alpha.** Отрисовка выбирает ambient-цвет по проверенному типу поверхности и правилам слоя. Наружные и внутренние поверхности используют свои каналы; крыши, скрытие крышей и иные слои применяют отдельно установленные правила. Это не сводится к одному экранному color-grade поверх всей сцены.

**Наблюдение CE:** terrain draw берёт indoor или outdoor цвет по классу ART tile и при локальном свете может блендить цветовые значения; roof draw передаёт наружный ambient-цвет. [Выбор цвета и blit tile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L670-L735), [цвет roof](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L231-L244). Классификация и композиция должны быть подтверждены для всех целевых слоёв отдельно.

**Зависимости:** terrain/roof classification, ambient pair, tile/facade/roof pass order, source palettes и renderer blend behavior. **Предложенные сценарии:** контрастная синтетическая схема визуально различает indoor/outdoor terrain; смена одного канала затрагивает только соответствующий класс; крыша не получает indoor channel по умолчанию.

**Steamwoven на `b1245e5`:** в `TerrainCanvas` выполняется декодированная текстура с обычной альфой, без ambient tint, lighting или shading. [Фактическая отрисовка terrain](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/tools/terrain-viewer/Godot/TerrainCanvas.cs#L120-L140), [ограничения диагностического renderer](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/rendering/terrain-viewer.md#L103-L111). Покрытие отсутствует и не требуется от диагностического viewer.

<a id="fr-light-007"></a>

### FR-LIGHT-007 — выбор map-default и per-sector scheme

**Предлагаемая фаза: Pre-alpha.** При загрузке карты применяется её схема по умолчанию; сектор может задать собственную схему. При переходе границы сектора ambient-пара выбирается для активного сектора при текущем игровом часе. Неопределённый или отсутствующий id должен иметь явное профильное правило.

**Наблюдение CE и цепочка:** `map_load_extension` читает map-default из метаданных карты; сектор сохраняет `light_scheme`; callback смены сектора разрешает специальный id «map default» и применяет выбранную схему на текущем часу. [Map default](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1598-L1638), [поле сектора](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.h#L24-L39), [применение при sector change](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L680-L702), [чтение значения из сектора](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1396-L1417).

**Зависимости:** карта, селектор сектора, mapinfo, поле sector light scheme и таблица scheme ids. **Предложенные сценарии:** два соседних синтетических сектора с разными схемами переключаются на границе; сектор с id map-default следует текущему map-default; переключение сохраняет тот же игровой час.

**Steamwoven на `b1245e5`:** поддержан только фиксированный диагностический сектор. Парсер сохраняет framing-метаданные света, но не разрешает map/sector scheme. [Fixed terrain selection и отсутствие precedence/runtime](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainModels.cs#L162-L181), [границы terrain loader](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainLoader.cs#L182-L213). Покрытие отсутствует.

<a id="fr-light-008"></a>

### FR-LIGHT-008 — секторные неподвижные источники света

**Предлагаемая фаза: Pre-alpha.** Секторный список света создаёт и удаляет локальные источники в координатах мира, учитывая смещение, активность, тип (свет/затемнение), ART-профиль и tint. Свет влияет на соответствующую локальную область, может анимироваться и корректно обрабатывается у границ соседних секторов.

**Наблюдение CE и цепочка:** загрузка сектора читает count и затем каждый `Light`; сериализованная структура имеет размер `0x30` (48 байт); добавление ставит источник в список и при необходимости запускает анимацию; renderer обходит списки видимых секторов и применяет активный light ART и tint. [48-байтовая запись и чтение](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L24-L46), [read light](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1838-L1866), [sector list load/add](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L54-L153), [sector load caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1349-L1368), [visible light renderer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L2359-L2497).

**Зависимости:** sector records, light records, save/DIF override, light ART/palette, map coordinates and neighboring sectors. **Предложенные сценарии:** синтетический stationary light affects only intersecting tiles; OFF suppresses it; DARK subtracts rather than adds; an overlapping emitter in a neighboring sector remains visible without duplicate contribution.

**Steamwoven на `b1245e5`:** `ParseSector` bounds count and skips exactly `count × 48` bytes, keeping only `LightCount` and byte ranges; it does not decode a light record or retain a light-source object. Для фактического выбранного диагностического сектора зафиксирован `light_count = 0`; это свойство одного выбранного входа, а не свидетельство отсутствия света в других секторах/картах. [Current framing parser](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainLoader.cs#L182-L213), [выбранный сектор и предел наблюдения](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L37-L45). Coverage limited to framing, no light behavior.

<a id="fr-light-009"></a>

### FR-LIGHT-009 — объектные, overlay и переносимые источники

**Предлагаемая фаза: Pre-alpha.** Свет, связанный с мировым объектом, следует за его расположением/смещением и жизненным циклом; overlay-света принадлежат тому же объекту. Носимое/экипированное снаряжение может обновлять размер/цвет локального света персонажа согласно подтверждённым правилам предметов. Предмет в инвентаре не должен сам оставаться отдельным world emitter, если политика профиля этого не требует.

**Наблюдение CE и цепочка:** `sector_light_list_fold` отбрасывает inventory objects из обычного секторного обхода, добавляет primary и до четырёх overlay light handles; экипировка и снятие вызывают `item_recalc_light`, который пересчитывает флаги персонажа, получает профиль через `critter_light_get` и вызывает `object_set_light`. [Секторное добавление object/overlay lights](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L392-L456), [recalc при unequip](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4388-L4411), [equipped flags → object light](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4425-L4484), [critter light profile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2322-L2356), [object light binding](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2337-L2355).

**Зависимости:** object/prototype and item flags, equipment slots/effectiveness, actor state, object movement, primary/overlay light ART, sector membership. **Предложенные сценарии:** экипировать/снять синтетический light item меняет только ожидаемый actor emitter; движение переносит источник; удаление owner удаляет связанные источники; inventory item не добавляется вторым источником рядом с персонажем.

**Steamwoven на `b1245e5`:** terrain parser не декодирует light handles и отдельная limited static-object feature не строит общий runtime actor light graph. Существующие actor pixels в диагностической сцене не получают lighting pass. [Object window boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainLoader.cs#L197-L213), [viewer draw path](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/tools/terrain-viewer/Godot/TerrainCanvas.cs#L120-L140). Покрытие отсутствует.

<a id="fr-light-010"></a>

### FR-LIGHT-010 — включение time-sensitive источников

**Предлагаемая фаза: Pre-alpha.** Если объект/источник имеет подтверждённую привязку к времени суток, смена дня и ночи меняет его активность и визуальное состояние на той же временной границе, что и профиль. Критерии включения должны принадлежать данным/правилам объекта; нельзя вывести их только из слова «ночной» или имени ресурса.

**Наблюдение CE:** при загрузке сектора `sector_light_list_fold` классифицирует час как день `[06:00,18:00)` и может выключать/включать соответствующую scenery по типу базового tile. При смене ambient-цветов `light_set_colors` обходит загруженные сектора и вызывает обновление; `sub_4DDD90` синхронизирует флаги объекта и его light handles. [Fold при загрузке и временное включение](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L392-L456), [color update → sector enumeration](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L362-L376), [часовая обработка scenery и lights](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1888-L1959). Источник подтверждает CE ветку, но не полный набор оригинальных типов объектов.

**Зависимости:** игровой clock, схема, scenery flags, terrain tile class, object state и секторная загрузка. **Предложенные сценарии:** синтетический time-sensitive emitter выключен днём и включён ночью по профилю; загрузка сектора сразу вычисляет корректный флаг из текущего часа; смена часа не добавляет light handle дважды.

**Steamwoven на `b1245e5`:** нет моделирования флагов состояния/активности emitters; выбранный сектор имеет ноль записей света, но это лишь частный структурный факт. [Framing-only `Lights` section](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainModels.cs#L162-L181), [selected-sector count](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L37-L45). Покрытие отсутствует.

<a id="fr-light-011"></a>

### FR-LIGHT-011 — luminance для явно light-aware проверок

**Предлагаемая фаза: Pre-alpha.** Механики, у которых подтверждена зависимость от света, используют общий запрос luminance для объекта или координаты. Проверка должна учитывать базовый indoor/outdoor ambient, активные локальные lights, их направление add/darken, а также только явно подтверждённые модификаторы источника/цели. Это требование не утверждает, что вся видимость, AI-поиск или line-of-sight зависят от такой оценки.

**Наблюдение CE и цепочка:** `skill` evaluation для флагов `SKILL_CHECK_LIGHT`/`CHECK_SEEING` читает серую luminance цели, источника либо координаты через `sub_4DCE10`/`sub_4D9240`; последняя вызывает `sub_4D89E0`, где складываются indoor/outdoor baseline и активные локальные световые маски. Затем расчет применяет коэффициенты светового излучения криттера и dark sight и добавляет конкретный skill penalty. [Light-aware caller and calculation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L2391-L2453), [позиционная выборка и light contribution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L427-L543), [object luminance accessor](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1288-L1301).

**Зависимости:** единый light sampling API; skill invocation context; emitted-light flags и dark-sight capabilities; object/coordinate luminance. **Предложенные сценарии:** синтетический свет меняет только явно light-aware результат; одинаковые параметры объекта и координаты дают согласованный sample; dark-sight/emission модификаторы проверяются независимо; не относящиеся к свету действия не меняются.

**Steamwoven на `b1245e5`:** lighting API и игровой skill evaluation отсутствуют. Фиксированный набор actor/static rendering не устанавливает их взаимодействие с игровой видимостью. [Ограничения component catalogue](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L12-L25). Покрытие отсутствует.

<a id="fr-light-012"></a>

### FR-LIGHT-012 — сохранение времени и применённой схемы

**Предлагаемая фаза: Pre-alpha.** Save/load сохраняет авторитетные игровые часы и достаточно состояния освещения, чтобы после загрузки активная схема, текущий час и запланированные обновления совпадали. Загрузка не должна сначала показывать устаревший ambient слой поверх уже обновлённого мира.

**Наблюдение CE:** `timeevent_save/load` сохраняет real/game/animation DateTime и сериализуемые события; отдельная регистрация `LightScheme` сохраняет текущий scheme id и час, а загрузчик вызывает `light_scheme_set`. [Clock save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L570-L635), [clock load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L679-L718), [scheme save/load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L154-L177), [game library component registration](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L148-L160).

**Зависимости:** save schema/versioning, world clock, active scheme, sector state and event queue. **Предложенные сценарии:** сохранить synthetic world перед переходом часа, загрузить после нескольких wall-clock минут и получить прежние часы/схему; сохранить во время приостановленного мира и проверить точное возобновление без пропуска или повтора hourly event.

**Steamwoven на `b1245e5`:** persistence игрового мира и lighting отсутствует; сохранение состояния ART inspector playback не покрывает игровые часы. [Project status boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25). Покрытие отсутствует.

## Поздние визуальные идеи и границы доказательств

<a id="fr-light-013"></a>

### FR-LIGHT-013 — отдельный Remastered visual profile

**Предлагаемая фаза: Later / Remastered.** Мягкие тени и normal-map treatment могут быть предложены как отдельные визуальные опции после Legacy совместимости. Они не меняют игровые часы, карту схем, luminance, skill outcome или состояние мира и не должны быть объявлены восстановленными особенностями оригинала.

**Наблюдение CE:** в просмотренных pinned-source участках есть локальные источники, dark-light ветка, legacy shadow API/settings и рисунок света, но подтверждения именно современного soft-shadow профиля или normal-map освещения здесь нет. Это не доказывает отсутствие любой формы теней в оригинале; эти два конкретных улучшения остаются проектной идеей. **Зависимости:** отдельный presentation backend и явный visual-mode switch, сохраняющий simulation state согласно принятому [направлению режимов](../architecture/decisions.md#architecture-direction). **Предложенный сценарий:** переключение Legacy/Remastered меняет только картинку; синтетические clock, luminance gameplay values и сохранённое состояние остаются идентичны.

**Steamwoven на `b1245e5`:** renderer не реализует lighting, shadows или normal maps и не проверяет режимы качества; в текущем документе проекта принято Legacy-first, а позднее улучшение графики отделено от runtime parity. [Viewer limitations](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/rendering/terrain-viewer.md#L103-L111), [visual modes decision](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/architecture/decisions.md#L8-L18). Покрытие отсутствует; это поздняя идея продукта.

## Текущее покрытие и неопределённости

1. **Есть только bounded framing:** current `TerrainLoader.ParseSector` проверяет count и лимиты, вычисляет 48-байтовый диапазон записей и пропускает его; `TerrainSectorFraming` предоставляет `LightCount` и byte ranges, но не типизированный `Light` или список emitters. Это полезная структурная точка продолжения, не runtime lighting. [Парсер на `b1245e5`](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainLoader.cs#L182-L213), [модель framing](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/src/Arcanum.Data/Terrain/TerrainModels.cs#L162-L181).
2. **Визуальная граница:** текущий viewer отрисовывает заданные текстуры диагностически; его контракт прямо исключает lighting, shadows и grading. Это корректная ограниченная сцена, но она не может служить свидетельством отсутствия оригинальных light records или поведения в иных секторах. [Canvas](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/tools/terrain-viewer/Godot/TerrainCanvas.cs#L120-L140), [контракт renderer](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/rendering/terrain-viewer.md#L103-L111).
3. **Нулевой selected-sector count:** `light_count = 0` установлен только для выбранного сектора в зафиксированном формате/входе. Другие секторы, карты, сохранённые DIF-варианты и объектные источники этим наблюдением не охвачены. [Ограниченное наблюдение входа](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L37-L45).
4. **Неизвестное оригинальное поведение:** CE-source не заменяет независимое наблюдение игрового runtime. Требуют проверки целевой профиль скорости игрового времени, крайние пустые слоты lighting schemes, точные правила indoor/outdoor для каждого слоя, полный охват object/overlay/carried emitters, правила видимости за пределами явно caller-linked skill checks и поведение на реальных карта/сохранениях.
5. **Изоляция источников:** ссылки на CE — только для описания поведения. Документ не копирует CE-код и не является разрешением механически переносить его структуру. Любая реализация остаётся независимой и должна пройти применимые требования provenance/licensing из [метода исследования](../research/method.md).

## Сводная матрица покрытия Steamwoven

| ID | Данные | Правила | Интеграция | Проверка |
| --- | --- | --- | --- | --- |
| FR-LIGHT-001 | Нет: игровая дата/схема не загружается | Нет: авторитетных игровых часов нет | Нет: game runtime отсутствует | Нет: clock scenarios не проверялись |
| FR-LIGHT-002 | Нет: нет профиля шага времени | Нет: нет game pause/advance runtime | Нет: ожидание и симуляция не связаны | Нет: поведение времени не проверялось |
| FR-LIGHT-003 | Нет: нет игровых ambient palette records | Нет: схема цвета не применяется | Нет: production renderer не подключён | Нет: почасовые цвета не проверялись |
| FR-LIGHT-004 | Нет: scheme entries не читаются | Нет: нет нормализации пустых часов | Нет: нет передаточной таблицы в renderer | Нет: края/пропуски не проверялись |
| FR-LIGHT-005 | Нет: нет активных game schemes | Нет: runtime не применяет hourly event | Нет: viewer исключает lighting | Нет: смена часа не проверялась |
| FR-LIGHT-006 | Нет: нет слоёв indoor/outdoor lighting | Нет: нет правил поверхностей | Нет: production lighting path отсутствует | Нет: крыши/сектора не сравнивались |
| FR-LIGHT-007 | Нет: схемы карты/сектора не читаются | Нет: scheme selection отсутствует | Нет: нет world-sector lighting bridge | Нет: map/sector transition не проверялся |
| FR-LIGHT-008 | Частично: только framing секции статического света и нулевой count одного выбранного сектора; записи не декодируются | Нет: нет правил локального света | Нет: локальные emitters не рисуются | Нет: runtime источники не проверялись |
| FR-LIGHT-009 | Частично: ограниченное framing карты не описывает object/carried emitters | Нет: нет жизненного цикла объектного света | Нет: нет item/actor lighting integration | Нет: экипировка/overlay не проверялись |
| FR-LIGHT-010 | Частично: просмотренный секторный framing не устанавливает time-sensitive свойства | Нет: нет временной активации источников | Нет: нет clock-to-light bridge | Нет: день/ночь для emitters не проверялись |
| FR-LIGHT-011 | Нет: нет световых оценок для gameplay | Нет: нет подтверждённых light-aware callers | Нет: нет visibility/skill integration | Нет: luminance scenarios отсутствуют |
| FR-LIGHT-012 | Нет: game save payload не реализован | Нет: нет восстановления clock/scheme | Нет: нет save-to-light lifecycle | Нет: save/load lighting не проверялось |
| FR-LIGHT-013 | Неприменимо: это предложенная поздняя визуальная цель, не CE-derived data | Нет: нет soft-shadow/normal-map lighting policy в runtime | Нет: production renderer не имеет этого lighting path | Нет: визуальный результат не проверялся |

Эта матрица относится к проверенной Steamwoven базе, а не к наличию функций в оригинальной игре. Framing-секция и выбранный нулевой count не доказывают отсутствие данных света в других секторах или игровых runtime.

## Инвентарь просмотренных источников и охват

**CE — прочитаны релевантные тела функций и связанные callers:**

- Игровые часы и события: `src/game/timeevent.c`, `timeevent.h` — DateTime accessors, day predicate, тик, явное увеличение времени, save/load и типы событий.
- Ambient schemes и local lights: `src/game/light_scheme.c`, `light_scheme.h`, `light.c`, `light.h` — загрузка/разбор/выбор схемы, обновление цветов, размер и чтение light-record, позиционная выборка, отрисовка и invalidation секторов.
- Секторные/object источники: `src/game/sector_light_list.c`, `sector_light_list.h`, `sector.c`, `sector.h`, `map.c`, `sector_object_list.c` — load/add/fold, поле scheme, map default и переход сектора. DIF loader прочитан только в части вызова; полная политика сохранённых различий не реконструировалась.
- Время суток, предметы, luminance и поверхности: `src/ui/anim_ui.c`, `sleep_ui.c`, релевантные ветки `wmap_ui.c`, `src/game/item.c`, `critter.c`, `object.c`, `skill.c`, `tile.c`, `roof.c`, `gamelib.c`.

**Связанные CE области, найденные при поиске, но не прошедшие полный разбор:** остальные callers проверки света/видимости; object/prototype flag definitions и все equip/update paths; полный save lifecycle; tile/object/roof render ordering и нижний TIG palette/blit backend; другие UI/settings paths выбора lighting scheme. Погода, полный AI/quest scheduler и игровые системы вне прямой зависимости от света за рамками этого среза.

**Steamwoven — прямо просмотрены:** `src/Arcanum.Data/Terrain/TerrainLoader.cs` (sector framing и пропуск light-section), `TerrainModels.cs` (framing model), `tools/terrain-viewer/Godot/TerrainCanvas.cs` (draw path), а также `docs/formats/maps.md`, `docs/rendering/terrain-viewer.md`, `docs/project/status.md`, `docs/architecture/decisions.md` и `docs/research/method.md`. Выполнен поиск lighting/time terms по текущим `src`, `tools/terrain-viewer` и `tests`; отсутствие функциональности сформулировано только для проверенных Data/Simulation/Presentation/viewer путей.

**Не проверялись:** содержимое оригинальных resource payloads и lighting scheme tables, игровой executable/runtime capture, сборка и поведение вне указанной области. Поэтому палитры из оригинальных данных, полное соответствие CE оригинальному runtime и целевой time multiplier здесь не подтверждаются.
