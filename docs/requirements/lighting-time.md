# Время мира и освещение — функциональные требования

**Покрытие реализации:** аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), через PR #54. Источники исходного CE-исследования и его дата сохранены; актуальный объём — в таблице и Steamwoven-описаниях ниже. [Метод и срез доски](README.md#база-и-происхождение). Новых runtime/GUI/original-data запусков в этом аудите нет.

**Срез исследования:** 2026-10-02. **Объём:** игровое время, день/ночь, глобальный и локальный свет, его связь с соответствующими проверками. Это ограниченная спецификация, не аудит всех систем игры. Погода, полный планировщик заданий/AI и движок за пределами непосредственно подтверждённых зависимостей.

**Уровни утверждений.** «Наблюдение CE» означает только прочитанный код `arcanum-ce` на `a7ff41b300ef712f0e7d088183a3d08957110cdd`; это не отдельное измерение оригинального исполняемого файла. «Предложение проекта» — функциональная политика для независимой реализации. «Неизвестно» — поведение, которое нельзя закрепить без дополнительной проверки данных или оригинального runtime. Требования и сценарии ниже предложены и не реализованы этим документом.

**Снимок Steamwoven:** проверен фактический `main` на `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (PR #36). Каталог [статуса проекта](../project/status.md) всё ещё указывает базу `0bc9962f90aed467b09ca977206ce456e31a4616`, через PR #30; поэтому покрытие ниже сверено с кодом текущего среза, а не выведено из устаревшей строки каталога. Кодовые ссылки в отчёте закреплены на этих двух SHA.

## Требования

**ARC-RES-018 — RESEARCH (2026-10-07):** [канонический clock/light контракт](../world/time-lighting-contract.md)
трассирует raw clock, относительное hour scheduling, numeric map/sector schemes,
indoor/outdoor channels и stationary/primary/overlay provenance; bounded inspector
и независимые synthetic/original-data проверки отделены от production coverage.
В выбранном exterior нет stationary records, но есть non-invalid object-light
descriptions; это не доказывает их активность. Календарные quirks, gaps/reload,
coordinate/mask readiness и layer-specific tint не заменены guessed defaults.
FR-LIGHT остаются предложенными: production clock/scheduler/renderer и original
executable/gameplay/visual parity **не реализованы / NOT_VERIFIED** этим research.

<a id="fr-light-001"></a>

### FR-LIGHT-001 — единые игровые часы и граница дня/ночи

**Предлагаемая фаза: Pre-alpha.** Симуляция должна иметь одни игровые дату и время. Системы, которым нужны часы или признак дня, читают их из этого состояния, а не вычисляют собственный таймер. Игроку должны быть доступны текущее игровое время и состояние «день/ночь» в предусмотренном проектом интерфейсе.

**Наблюдение CE:** `timeevent_ping` хранит игровые и анимационные часы отдельно от реального времени; `datetime_current_hour/minute/day` читают игровые часы, а `game_time_is_day` считает дневным интервал `[06:00, 18:00)`. Интерфейсный callback обновляет игровой циферблат и планирует следующее обновление. [Часы и календарные accessors](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L235-L375), [day predicate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L467-L471), [clock UI callback](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L5393-L5408). CE-source не доказывает, что это исчерпывающая проверка оригинального runtime.

**Зависимости:** сериализуемое состояние игровых часов; профиль совместимости для календаря и порогов; единый интерфейс чтения времени для симуляции и отображения. **Предложенные сценарии:** проверить границы 05:59/06:00 и 17:59/18:00 на CE-профиле; перевести часы через полночь и убедиться, что интерфейс, освещение и потребители признака дня видят одно состояние.

**Steamwoven:** частично, [ARC-WORLD-010](../rendering/area-lighting.md): одни часы сессии ([WORLD-004](../world/game-clock-runtime.md) внутри [WORLD-008](../world/game-session-runtime.md)) в viewer `crash-site-area` идут сами от host-времени (CE-наблюдение 5/250 мс, 8×, NOT_VERIFIED), стоят на паузе Menu при открытой палитре или диалоге без догона и двигаются явным ожиданием «+1 час» даже на паузе; освещение, HUD и снимок читают эти часы. Сна, пошагового боя и очереди игровых событий нет.

<a id="fr-light-002"></a>

### FR-LIGHT-002 — ход времени, остановка и явное ожидание

**Предлагаемая фаза: Pre-alpha.** Игровые часы продвигаются по правилам выбранного профиля времени. Когда профиль приостанавливает симуляцию, игровые часы стоят и сами по себе не приближают будущий game-time deadline; явное действие ожидания/сна продвигает время на указанную длительность и обновляет состояние освещения до того, как мир снова показывается игроку.

**Наблюдение CE:** `timeevent_ping` добавляет `8 × delta` к игровым часам вне пошагового боя и при открытом внутреннем gate; при пошаговом бое игровые часы не меняются, хотя анимационные меняются. Сам `delta` пропускается ниже 5 ms и ограничивается 250 ms, поэтому множитель 8 нельзя трактовать как безусловную скорость относительно реального времени. Сон прибавляет игровой час явно; ambient update выключается на время затемнения и возобновляется при пробуждении. [Тик и условия продвижения](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L801-L842), [шаг сна](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L591-L640), [затемнение и обновление при пробуждении](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L670-L719). Внутренний gate с неинформативным именем не раскрывает полного набора причин остановки.

**Границы очередей.** CE после обновления часов отдельно обходит real-time, game-time и animation queues ([queue loop](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L843-L897)). При остановке game clock уже наступивший game-time callback всё ещё может быть обработан; остановка часов не доказывает глобальную блокировку callbacks. Предлагаемая приёмка различает будущий deadline, уже due event, real-time и animation event при gate/TB. Если проект приостанавливает все callbacks, это отдельная project policy, а не вывод из CE.

**Зависимости:** профиль скорости времени, пошаговый бой и другие состояния остановки, очередь игровых событий и операции ожидания. **Предложенные сценарии:** render-frame пауза не меняет игровое время; приостановка профиля блокирует часовой переход; ожидание на час продвигает ровно на час по выбранной политике и снимает затемнение только после синхронизации света. Число `8×` следует принять лишь после решения о целевом профиле и проверке оригинального runtime.

**Steamwoven:** частично, [ARC-WORLD-010](../rendering/area-lighting.md): одни часы сессии ([WORLD-004](../world/game-clock-runtime.md) внутри [WORLD-008](../world/game-session-runtime.md)) в viewer `crash-site-area` идут сами от host-времени (CE-наблюдение 5/250 мс, 8×, NOT_VERIFIED), стоят на паузе Menu при открытой палитре или диалоге без догона и двигаются явным ожиданием «+1 час» даже на паузе; освещение, HUD и снимок читают эти часы. Сна, пошагового боя и очереди игровых событий нет.

<a id="fr-light-003"></a>

### FR-LIGHT-003 — почасовая схема ambient-цветов

**Предлагаемая фаза: Pre-alpha.** Активная схема содержит отдельные RGB-цвета окружения для внутренних и наружных поверхностей на каждый час суток. Цвета поступают из версионированных игровых данных и адресуются идентификатором схемы. Числа из исходных ресурсов не должны встраиваться в код как универсальная палитра.

**Наблюдение CE:** `light_scheme_mod_load` требует индексный каталог lighting schemes; `light_scheme_set` разрешает id схемы в соответствующий файл данных, а `light_scheme_parse` читает 24 почасовые записи вида outdoor RGB + indoor RGB. [Загрузка и выбор схемы](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L111-L121), [разрешение id в данные](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L213-L254), [формат почасовой записи](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L331-L371).

**Зависимости:** идентификаторы и содержимое таблицы схем; шестиканальная запись на час; безопасная проверка входных диапазонов и полноты. **Предложенные сценарии:** синтетическая схема с различными indoor/outdoor компонентами выдаёт 24 независимых пары и не меняет данные исходного ресурса; отсутствующий файл или некорректная запись дают явную диагностическую ошибку вместо молчаливой правдоподобной палитры.

**Steamwoven:** частично, [ARC-RENDER-010](../rendering/area-lighting.md): ограниченная реализация для viewer `crash-site-area` (Data `Arcanum.Data.Lighting`, часы WORLD-004, шейдерный множитель по каналу). Границы, проверки и NOT_VERIFIED — в [описании компонента](../rendering/area-lighting.md); исследование — [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-004"></a>

### FR-LIGHT-004 — пустые часы и проверяемая интерполяция данных схемы

**Предлагаемая фаза: Pre-alpha.** Если формат профиля допускает пустые часовые записи, нормализация схемы должна определять их цвет до применения схемы и документировать правила для начального и конечного пустых диапазонов. Этот шаг заполняет временную таблицу; он сам по себе не означает плавного перехода изображения между минутами.

**Наблюдение CE:** в `light_scheme_parse` непустая запись задаёт следующий якорь, пустые записи помечают разрыв, а при встрече последующего непустого якоря промежуточные каналы заполняются целочисленными шагами. Код не замыкает диапазон после последнего якоря и не устанавливает циклический wrap-around; массив до разбора выделяется нулевым. Поэтому поведение краёв требует подтверждения на допустимых исходных данных и не обобщается как циклическая интерполяция. [Парсер и заполнение разрыва](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L331-L413), [нулевая инициализация таблицы](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L85-L92).

**Зависимости:** подтверждённая грамматика схемы и правила её крайних слотов. **Предложенные сценарии:** на синтетических якорях подтвердить промежуточное значение каждого канала и округление; отдельно зафиксировать ожидаемое поведение пустых слотов до первого и после последнего якоря; reject или безопасно диагностировать таблицу без достаточных якорей.

**Steamwoven:** частично, [ARC-RENDER-010](../rendering/area-lighting.md): ограниченная реализация для viewer `crash-site-area` (Data `Arcanum.Data.Lighting`, часы WORLD-004, шейдерный множитель по каналу). Границы, проверки и NOT_VERIFIED — в [описании компонента](../rendering/area-lighting.md); исследование — [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-005"></a>

### FR-LIGHT-005 — почасовое применение схемы

**Предлагаемая фаза: Pre-alpha.** При смене игрового часа renderer получает почасовую пару indoor/outdoor текущей схемы и обновляет зависимые поверхности. В CE-derived compatibility profile это дискретное применение по часовому событию. Интерполяция пробелов в конфигурации из FR-LIGHT-004 не должна выдаваться за непрерывный runtime-переход между часами.

**Наблюдение CE и цепочка:** ambient callback считывает `datetime_current_hour()`, вызывает `light_scheme_set_hour()`, затем планирует следующее событие через 3 600 000 игровых миллисекунд. `light_scheme_set_hour()` вызывает `light_set_colors()`, которое обновляет ambient palettes и инвалидирует отрисовку. [Почасовой callback и расписание](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/anim_ui.c#L242-L275), [применение часа](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L271-L303), [обновление цветов и invalidation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L362-L376). Значение `3 600 000` здесь — задержка в шкале игрового времени, а не 60 минут реального времени.

**Зависимости:** clock и схема из FR-LIGHT-001/003; события игрового времени; инвалидирование palette/light buffers. **Предложенные сценарии:** в синтетической схеме цвета неизменны между двумя игровыми часовыми границами, меняются ровно при переходе часа и после восстановления из паузы сразу соответствуют текущему времени.

**Steamwoven:** частично, [ARC-RENDER-010](../rendering/area-lighting.md): ограниченная реализация для viewer `crash-site-area` (Data `Arcanum.Data.Lighting`, часы WORLD-004, шейдерный множитель по каналу). Границы, проверки и NOT_VERIFIED — в [описании компонента](../rendering/area-lighting.md); исследование — [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-006"></a>

### FR-LIGHT-006 — indoor/outdoor цвет на соответствующих поверхностях

**Предлагаемая фаза: Pre-alpha.** Отрисовка выбирает ambient-цвет по проверенному типу поверхности и правилам слоя. Наружные и внутренние поверхности используют свои каналы; крыши, скрытие крышей и иные слои применяют отдельно установленные правила. Это не сводится к одному экранному color-grade поверх всей сцены.

**Наблюдение CE:** terrain draw берёт indoor или outdoor цвет по классу ART tile и при локальном свете может блендить цветовые значения; roof draw передаёт наружный ambient-цвет. [Выбор цвета и blit tile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L670-L735), [цвет roof](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L231-L244). Классификация и композиция должны быть подтверждены для всех целевых слоёв отдельно.

**Зависимости:** terrain/roof classification, ambient pair, tile/facade/roof pass order, source palettes и renderer blend behavior. **Предложенные сценарии:** контрастная синтетическая схема визуально различает indoor/outdoor terrain; смена одного канала затрагивает только соответствующий класс; крыша не получает indoor channel по умолчанию.

**Steamwoven:** частично, [ARC-RENDER-010](../rendering/area-lighting.md): ограниченная реализация для viewer `crash-site-area` (Data `Arcanum.Data.Lighting`, часы WORLD-004, шейдерный множитель по каналу). [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012) добавляет цвет объекта CE для тел, стен и актёра (класс тайла под клеткой плюс локальный свет) и indoor-схему пещеры. Границы, проверки и NOT_VERIFIED — в [описании компонента](../rendering/area-lighting.md); исследование — [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-007"></a>

### FR-LIGHT-007 — выбор map-default и per-sector scheme

**Предлагаемая фаза: Pre-alpha.** При загрузке карты применяется её схема по умолчанию; сектор может задать собственную схему. При переходе границы сектора ambient-пара выбирается для активного сектора при текущем игровом часе. Неопределённый или отсутствующий id должен иметь явное профильное правило.

**Наблюдение CE и цепочка:** `map_load_extension` читает map-default из метаданных карты; сектор сохраняет `light_scheme`; callback смены сектора разрешает специальный id «map default» и применяет выбранную схему на текущем часу. [Map default](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1598-L1638), [поле сектора](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.h#L24-L39), [применение при sector change](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L680-L702), [чтение значения из сектора](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1396-L1417).

**Зависимости:** карта, селектор сектора, mapinfo, поле sector light scheme и таблица scheme ids. **Предложенные сценарии:** два соседних синтетических сектора с разными схемами переключаются на границе; сектор с id map-default следует текущему map-default; переключение сохраняет тот же игровой час.

**Steamwoven:** частично, [ARC-RENDER-010](../rendering/area-lighting.md): ограниченная реализация для viewer `crash-site-area` (Data `Arcanum.Data.Lighting`, часы WORLD-004, шейдерный множитель по каналу). Границы, проверки и NOT_VERIFIED — в [описании компонента](../rendering/area-lighting.md); исследование — [RES-018](../world/time-lighting-contract.md).

<a id="fr-light-008"></a>

### FR-LIGHT-008 — секторные неподвижные источники света

**Предлагаемая фаза: Pre-alpha.** Секторный список света создаёт и удаляет локальные источники в координатах мира, учитывая смещение, активность, тип (свет/затемнение), ART-профиль и tint. Свет влияет на соответствующую локальную область, может анимироваться и корректно обрабатывается у границ соседних секторов.

**Наблюдение CE и цепочка:** загрузка сектора читает count и затем каждый `Light`; сериализованная структура имеет размер `0x30` (48 байт); добавление ставит источник в список и при необходимости запускает анимацию; renderer обходит списки видимых секторов и применяет активный light ART и tint. [48-байтовая запись и чтение](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L24-L46), [read light](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1838-L1866), [sector list load/add](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L54-L153), [sector load caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1349-L1368), [visible light renderer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L2359-L2497).

**Зависимости:** sector records, light records, save/DIF override, light ART/palette, map coordinates and neighboring sectors. **Предложенные сценарии:** синтетический stationary light affects only intersecting tiles; OFF suppresses it; DARK subtracts rather than adds; an overlapping emitter in a neighboring sector remains visible without duplicate contribution.

**Steamwoven:** частично, [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012): неподвижные источники core-секторов читаются из strict snapshot и рисуются в viewer `crash-site-area` (решётка 40×20 для terrain, цвет объекта для тел, градиент стен). На оригинале — 4 источника пещеры 5078, снаружи ни одного. Анимированный свет, DIF/save-переопределения и источники соседних карт не реализованы. [Research frontier](../world/time-lighting-contract.md), [Area loader](../world/crash-site-area-loader.md).

<a id="fr-light-009"></a>

### FR-LIGHT-009 — объектные, overlay и переносимые источники

**Предлагаемая фаза: Pre-alpha.** Свет, связанный с мировым объектом, следует за его расположением/смещением и жизненным циклом; overlay-света принадлежат тому же объекту. Носимое/экипированное снаряжение может обновлять размер/цвет локального света персонажа согласно подтверждённым правилам предметов. Предмет в инвентаре не должен сам оставаться отдельным world emitter, если политика профиля этого не требует.

**Наблюдение CE и цепочка:** `sector_light_list_fold` отбрасывает inventory objects из обычного секторного обхода, добавляет primary и до четырёх overlay light handles; экипировка и снятие вызывают `item_recalc_light`, который пересчитывает флаги персонажа, получает профиль через `critter_light_get` и вызывает `object_set_light`. [Секторное добавление object/overlay lights](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L392-L456), [recalc при unequip](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4388-L4411), [equipped flags → object light](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4425-L4484), [critter light profile](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2322-L2356), [object light binding](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2337-L2355).

**Зависимости:** object/prototype and item flags, equipment slots/effectiveness, actor state, object movement, primary/overlay light ART, sector membership. **Предложенные сценарии:** экипировать/снять синтетический light item меняет только ожидаемый actor emitter; движение переносит источник; удаление owner удаляет связанные источники; inventory item не добавляется вторым источником рядом с персонажем.

**Steamwoven:** production runtime для этого требования отсутствует на audit baseline. Area/terrain reader сохраняет light-section framing/counts и raw fields, без light records/handles/activity semantics или emitters. [Research frontier](../world/time-lighting-contract.md), [Area loader](../world/crash-site-area-loader.md).

<a id="fr-light-010"></a>

### FR-LIGHT-010 — включение time-sensitive источников

**Предлагаемая фаза: Pre-alpha.** Если объект/источник имеет подтверждённую привязку к времени суток, смена дня и ночи меняет его активность и визуальное состояние на той же временной границе, что и профиль. Критерии включения должны принадлежать данным/правилам объекта; нельзя вывести их только из слова «ночной» или имени ресурса.

**Наблюдение CE:** при загрузке сектора `sector_light_list_fold` классифицирует час как день `[06:00,18:00)` и может выключать/включать соответствующую scenery по типу базового tile. При смене ambient-цветов `light_set_colors` обходит загруженные сектора и вызывает обновление; `sub_4DDD90` синхронизирует флаги объекта и его light handles. [Fold при загрузке и временное включение](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c#L392-L456), [color update → sector enumeration](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L362-L376), [часовая обработка scenery и lights](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1888-L1959). Источник подтверждает CE ветку, но не полный набор оригинальных типов объектов.

**Зависимости:** игровой clock, схема, scenery flags, terrain tile class, object state и секторная загрузка. **Предложенные сценарии:** синтетический time-sensitive emitter выключен днём и включён ночью по профилю; загрузка сектора сразу вычисляет корректный флаг из текущего часа; смена часа не добавляет light handle дважды.

**Steamwoven:** частично, [ARC-RENDER-012](../rendering/area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012): неподвижный свет секторов по источнику не зависит от часа (проверяется только OFF), поэтому при смене часа пересобирается только смесь с новым ambient; источники с INDOOR/OUTDOOR берут цвет текущего часа (synthetic, у источников пещеры флаги 0). Ночное включение scenery-объектов и их свет относятся к FR-LIGHT-009 и не реализованы. [Research frontier](../world/time-lighting-contract.md).

<a id="fr-light-011"></a>

### FR-LIGHT-011 — luminance для явно light-aware проверок

**Предлагаемая фаза: Pre-alpha.** Механики, у которых подтверждена зависимость от света, используют общий запрос luminance для объекта или координаты. Проверка должна учитывать базовый indoor/outdoor ambient, активные локальные lights, их направление add/darken, а также только явно подтверждённые модификаторы источника/цели. Это требование не утверждает, что вся видимость, AI-поиск или line-of-sight зависят от такой оценки.

**Наблюдение CE и цепочка:** `skill` evaluation для флагов `SKILL_CHECK_LIGHT`/`CHECK_SEEING` читает серую luminance цели, источника либо координаты через `sub_4DCE10`/`sub_4D9240`; последняя вызывает `sub_4D89E0`, где складываются indoor/outdoor baseline и активные локальные световые маски. Затем расчет применяет коэффициенты светового излучения криттера и dark sight и добавляет конкретный skill penalty. [Light-aware caller and calculation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L2391-L2453), [позиционная выборка и light contribution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L427-L543), [object luminance accessor](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L1288-L1301).

**Зависимости:** единый light sampling API; skill invocation context; emitted-light flags и dark-sight capabilities; object/coordinate luminance. **Предложенные сценарии:** синтетический свет меняет только явно light-aware результат; одинаковые параметры объекта и координаты дают согласованный sample; dark-sight/emission модификаторы проверяются независимо; не относящиеся к свету действия не меняются.

**Steamwoven:** production runtime для этого требования отсутствует на audit baseline. См. [каталог компонентов](../project/status.md); исследовательский контракт не закрывает gameplay/save/Remastered интеграцию.

<a id="fr-light-012"></a>

### FR-LIGHT-012 — сохранение времени и применённой схемы

**Предлагаемая фаза: Pre-alpha.** Save/load сохраняет авторитетные игровые часы и достаточно состояния освещения, чтобы после загрузки активная схема, текущий час и запланированные обновления совпадали. Загрузка не должна сначала показывать устаревший ambient слой поверх уже обновлённого мира.

**Наблюдение CE:** `timeevent_save/load` сохраняет real/game/animation DateTime и сериализуемые события; отдельная регистрация `LightScheme` сохраняет текущий scheme id и час, а загрузчик вызывает `light_scheme_set`. [Clock save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L570-L635), [clock load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L679-L718), [scheme save/load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c#L154-L177), [game library component registration](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L148-L160).

**Зависимости:** save schema/versioning, world clock, active scheme, sector state and event queue. **Предложенные сценарии:** сохранить synthetic world перед переходом часа, загрузить после нескольких wall-clock минут и получить прежние часы/схему; сохранить во время приостановленного мира и проверить точное возобновление без пропуска или повтора hourly event.

**Steamwoven:** частично, [ARC-WORLD-010](../world/game-session-runtime.md#snapshot-schema-3): снимок сессии схемы 3 несёт часы (время, пауза) и применённую схему (id и час); viewer записывает схему после каждой смены часа или сектора, restore отклоняет схему другого часа. Быстрое сохранение F7/F8 (ARC-WORLD-011) пишет и загружает эти часы и схему, драйвер дня/ночи перепривязывается к загруженной сессии; списка сохранений и очереди событий нет.

## Поздние визуальные идеи и границы доказательств

<a id="fr-light-013"></a>

### FR-LIGHT-013 — отдельный Remastered visual profile

**Предлагаемая фаза: Later / Remastered.** Мягкие тени и normal-map treatment могут быть предложены как отдельные визуальные опции после Legacy совместимости. Они не меняют игровые часы, карту схем, luminance, skill outcome или состояние мира и не должны быть объявлены восстановленными особенностями оригинала.

**Наблюдение CE:** в просмотренных pinned-source участках есть локальные источники, dark-light ветка, legacy shadow API/settings и рисунок света, но подтверждения именно современного soft-shadow профиля или normal-map освещения здесь нет. Это не доказывает отсутствие любой формы теней в оригинале; эти два конкретных улучшения остаются проектной идеей. **Зависимости:** отдельный presentation backend и явный visual-mode switch, сохраняющий simulation state согласно принятому [направлению режимов](../architecture/decisions.md#architecture-direction). **Предложенный сценарий:** переключение Legacy/Remastered меняет только картинку; синтетические clock, luminance gameplay values и сохранённое состояние остаются идентичны.

**Steamwoven:** production runtime для этого требования отсутствует на audit baseline. См. [каталог компонентов](../project/status.md); исследовательский контракт не закрывает gameplay/save/Remastered интеграцию.

## Текущее покрытие и неопределённости

[RES-018 time/light](../world/time-lighting-contract.md) и [RES-020 clock policy](../world/game-clock-policy.md) смержены как исследование. Их bounded source/raw-data/table checks не являются тестами production clock или lighting. На audit baseline публичных clock/scheduler/light-scheme/emitters/rendering/save систем нет. WORLD004 существует на доске, но не вошёл в этот merged срез. Exact movement time и ART playback clocks остаются отдельными доменами.

Area Data owner сохраняет секционные границы/counts и raw fields; декодирование источников/схем, их значение и активность требуют своих реализаций. Состояние исследования и production coverage разделены в матрице.

## Сводная матрица покрытия Steamwoven

| ID | Данные | Правила | Интеграция | Проверка |
| --- | --- | --- | --- | --- |
| FR-LIGHT-001 | Частично: часы сессии стартуют от `rules/gameinit.mes` модуля (год, час) | Частично: одни часы WORLD-004 в `GameSessionOwner`; день `[06:00,18:00)` | Частично: viewer crash-site-area — освещение, HUD и снимок читают часы сессии (ARC-WORLD-010) | Частично: литералы границ часов/полуночи WORLD-004; synthetic смена схемы на границе часа; native smoke |
| FR-LIGHT-002 | Частично: профиль хода времени — CE-наблюдение 5/250 мс и 8× (`GameTimeFlow`), NOT_VERIFIED | Частично: обычный ход стоит на паузе без догона; явное ожидание `ExplicitWait` идёт и на паузе | Частично: viewer — пауза Menu при открытой палитре или диалоге, «+1 час» как ожидание; сна, пошагового боя и очереди событий нет | Частично: worksheet-литералы допуска, пауза без догона, ожидание на паузе; native smoke 400 кадров палитры/диалога |
| FR-LIGHT-003 | Частично: scheme MES модуля по id через каталог схем, 24 часа outdoor/indoor | Частично: строгий разбор и typed failure, без угадываемой палитры | Частично: viewer crash-site-area применяет пару часа | Частично: synthetic литералы и opt-in original identity (scheme 1, digest) |
| FR-LIGHT-004 | Частично: пустые часы читаются как пропуски | Частично: строгая интерполяция внутренних пропусков по своим якорям; края 0/23 обязательны | Частично: заполненная таблица идёт в viewer | Частично: synthetic gap/edge/malformed; CE multi-gap/reload не воспроизводится |
| FR-LIGHT-005 | Частично: активная схема core-секторов Crash Site | Частично: дискретный выбор по целому игровому часу; ревизия меняется только при смене часа/схемы | Частично: viewer пересчитывает после каждого изменения часов сессии (ход времени, ожидание +1 час) | Частично: synthetic + native smoke 15/21/06/12 ч; без планировщика событий |
| FR-LIGHT-006 | Частично: канал по ART type и type bit tile/facade; класс тайла под клеткой тела/стены (ARC-RENDER-012) | Частично: правило palette callback CE для unlit-типов; цвет объекта CE (класс тайла + свет) для тел и актёра; стены 0/6 по тайлу, 2/4 outdoor | Частично: множитель на каждый draw item в шейдере viewer; градиент стен; решётка 40×20 для terrain | Частично: synthetic пиксели compositor; native smoke original: outdoor снаружи, indoor-схема пещеры с источниками; крыши не проверены |
| FR-LIGHT-007 | Частично: mapinfo LightScheme и слово схемы core-секторов (+8), привязанные к snapshot | Частично: sentinel 0 → map default; явная схема сектора; без угадываемого fallback | Частично: активный сектор по клетке актёра, тот же час | Частично: synthetic override/sentinel; в original все 9 core секторов = sentinel; переходы карт нет |
| FR-LIGHT-008 | Частично: 48-байтовые записи core-секторов из strict snapshot (владелец 0, клетка внутри своего сектора, известные флаги, статичный light ART через `art/light/light.mes`); пещера 5078 — 4 источника, снаружи 0 | Частично: прямоугольник кадра как у тела, ключ цвета, tint c·t/255, OFF, DARK вычитает, INDOOR/OUTDOOR берут цвет часа | Частично: решётка lighter/darker в шейдере area viewer, точка-якорь для тел, два конца основания для стен | Частично: literal synthetic тесты и пиксели; opt-in RealData пещеры; native smoke пещеры (пол, тело, стена при двух часах); TIG-округление и анимация NOT_VERIFIED |
| FR-LIGHT-009 | Частично: ограниченное framing карты не описывает object/carried emitters | Нет: нет жизненного цикла объектного света | Нет: нет item/actor lighting integration | Нет: экипировка/overlay не проверялись |
| FR-LIGHT-010 | Частично: флаги неподвижных источников (OFF, INDOOR/OUTDOOR) прочитаны; у четырёх источников пещеры флаги 0 | Частично: неподвижный свет секторов не зависит от часа (рендер проверяет только OFF); INDOOR/OUTDOOR следуют цвету часа; ночные scenery-объекты не реализованы | Частично: пересборка решётки и цветов тел при каждой смене часа | Частично: synthetic INDOOR/OUTDOOR; native smoke пещеры: решётка одинакова на 12 и 18 ч; ночное включение объектов не проверялось |
| FR-LIGHT-011 | Нет: нет световых оценок для gameplay | Нет: нет подтверждённых light-aware callers | Нет: нет visibility/skill integration | Нет: luminance scenarios отсутствуют |
| FR-LIGHT-012 | Частично: снимок сессии схемы 3 несёт часы и применённую схему (`lighting`: id и час) | Частично: restore отклоняет схему другого часа; пауза сохраняется как есть | Частично: viewer записывает схему после каждой смены часа/сектора; F7/F8 (ARC-WORLD-011) сохраняет и загружает часы и схему, драйвер дня/ночи перепривязывается к загруженной сессии | Частично: synthetic capture → decode → restore с той же схемой; native smoke restore в отдельного владельца |
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
