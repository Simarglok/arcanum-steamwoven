# Функциональные требования: карты, сектора и переходы

**Покрытие реализации:** аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), через PR #54. Источники исходного CE-исследования и его дата сохранены; актуальный объём — в таблице и Steamwoven-описаниях ниже. [Метод и срез доски](README.md#база-и-происхождение). Новых runtime/GUI/original-data запусков в этом аудите нет.

ARC-RES-017: [whole-exterior traversal research](../world/crash-site-area-traversal-contract.md)
для FR-WORLD-002/003/004: coordinates, neighbor binding и edge evidence;
Research/Proposed, NOT_READY, без streaming/eviction или crossing movement runtime.

**Срез исследования:** 2026-10-02. Проверенные checkout: `arcanum-ce` commit `a7ff41b300ef712f0e7d088183a3d08957110cdd` (чистый HEAD) и `arcanum-steamwoven` commit `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. Это исходный source-research baseline; текущее implementation coverage указано выше. Требования относятся только к этому домену; это не полный аудит игры и не утверждённый backlog.

## Как читать выводы

**ARC-RES-018 — RESEARCH:** [clock/light контракт](../world/time-lighting-contract.md)
связывает numeric map default/sector sentinel с активным hour scheme, не объявляя
отдельную ambient policy для каждого visible neighbour. Bounded original selection
и synthetic override/coverage проверки не реализуют world streaming/transfer;
stationary coordinates и masks остаются NOT_READY. Legacy/HD shared state —
предложение; production clock и original-runtime parity не подтверждены.

ARC-DATA-009 добавляет [bounded complete-area Data API](../world/crash-site-area-loader.md):
survey 621 → 9 core sectors и явный context, immutable world/source keys, 36,864
core terrain, 426 static и 88 mobile bodies, reconcile 7,399 aggregate records.
Это частичное data-layer покрытие FR-WORLD-001/002/003/005/007 (источники, адреса,
snapshot и numeric endpoint), не streaming/eviction runtime, crossing movement,
cave transition, DIF/PATCH или original-runtime parity. Таблица ниже учитывает этот merged объём; full-area renderer ещё не реализован.

Позднее bounded research ARC-RES-015: [перепись Crash Site area](../research/crash-site-area-inventory.md) и [предлагаемый multi-sector/mobile контракт](../world/crash-site-area-contract.md) уточняют источники, границу и потенциальные cave endpoints. Это исследование, не реализация FR-WORLD-001..007 и не original-runtime parity; актуальный production coverage указан ниже отдельно от исследования.

- **Подтверждено исходником CE** означает, что прочитан код именно указанного immutable commit. Это атрибуция source behavior, а не проверка оригинального исполняемого файла или поведения на оригинальных данных.
- **Целевая политика** и acceptance-сценарии ниже — предлагаемое поведение Steamwoven. Они не объявляют CE единственным эталоном и не являются уже принятой политикой продукта.
- **Неизвестно** означает, что кода, конкретной связи в исходных данных или проверки недостаточно для вывода.
- Покрытие Steamwoven пересверено по текущему audit baseline: Area Data API, production traversal и WORLD003 имеют разный объём. Полный exterior snapshot не закрывает live-registry/HP/state frontiers, streaming или переход в cave. См. [каталог компонентов](../project/status.md).

## Предложение по этапам

Метки **pre-alpha** и **later** — предварительная оценка только для требований этого world domain. Минимальные pre-alpha-кандидаты здесь — идентичность карты/сектора, границы секторов, один проверяемый connector, точное место прибытия и сохранение изменённого состояния при уходе/возврате. Это не полное определение pre-alpha, не требование загрузить весь мир и не ранжирование других игровых механик. Метки являются предложением для согласования раннего объёма; они не являются сроками, заданиями или полномочием создавать задачи. [Решения по архитектурному направлению](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/architecture/decisions.md), [roadmap и граница task authority](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/roadmap.md#L1-L4).

## Требования

<a id="fr-world-001"></a>

### FR-WORLD-001 — Каталог карт и региональные атрибуты

**Этап:** pre-alpha. **Наблюдаемое поведение:** по устойчивому ID система выбирает карту, её стартовую координату и размеры; региональные свойства карты (`world map`, `area`, стартовый тип) остаются привязаны к тому же ID во время игры.

**Границы:** метаданные каталога, разрешение имени/ID и проверка существования целевой карты. Порядок выбора между DAT/PATCH/loose files сюда не включён.

**CE:** `map_list_info_load` читает имя, X/Y, map type, worldmap и area; getters возвращают их по номеру карты. Цепочка загрузки карты разрешает запись списка → `map_open_in_game` строит пути → `map_open` устанавливает текущую карту. [map_list_info_load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2003-L2059), [getters](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L880-L943), [map_open_in_game → map_open](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L847).

**Зависимости:** versioned map catalogue, карта-координатная система и resolver исходных ресурсов. **Acceptance:** два синтетических map ID с разными area/start entries разрешаются независимо; неверный ID оставляет текущий мир неизменным. **Steamwoven:** Частично: [Area Data owner](../world/crash-site-area-loader.md) аутентифицирует фиксированный map5000/runtime1/townmap2 и конечный sector profile. Production-каталога всех карт, campaign bootstrap и общего выбора карты нет. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-world-002"></a>

### FR-WORLD-002 — Глобальные координаты, границы и адрес сектора

**Этап:** pre-alpha. **Наблюдаемое поведение:** локальная точка карты однозначно разрешается в сектор и локальную ячейку; размеры карты задают допустимую область, а попытка перейти за неё отклоняется.

**Границы:** координатный codec, деление карты на ячейки/сектора и проверка bounds. Проекция на экран и collision policy не определяются этим требованием.

**CE:** `sector_id_from_loc` вычисляет X/Y сектора сдвигом координат на 6 бит, `sector_loc_from_id` восстанавливает начало сектора; `map_open` задаёт location/sector limits из `map.prp`, а `sector_lock` проверяет эти пределы. Caller path: открытие карты → bounds → location → sector lookup. [sector coordinate conversion](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L592-L607), [map dimensions and limits](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L621-L628), [map limits set](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L747-L750), [sector bounds](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L860-L885).

**Зависимости:** оригинальный format profile, проверенная signedness/overflow policy и единые world coordinates для Simulation и Data. **Acceptance:** synthetic bounds проверяют две стороны каждого sector edge, включая последнюю допустимую и первую недопустимую ячейку; pack/unpack сохраняет ID на границе. **Steamwoven:** Частично: Area snapshots имеют checked sector pack/unpack, origins и distinct world/source/occurrence keys для нескольких секторов; локальные serialized static и direct mobile coordinates различаются. [Area API](../world/crash-site-area-loader.md). Movement/viewer остаются в bounded rectangle x4..8/y0..3, не общем map runtime.

<a id="fr-world-003"></a>

### FR-WORLD-003 — Загрузка и выселение соседних секторов

**Этап:** pre-alpha. **Наблюдаемое поведение:** когда actor/camera или активная world query достигает сектора, нужные секторы доступны; cache удерживает используемые записи, освобождает безопасные неиспользуемые и записывает dirty state перед выселением.

**Границы:** загрузка по sector ID, refcount/pinning, bounded cache, prefetch и unload. Не задаются точный размер cache, LRU policy или фоновая многопоточность.

**CE:** `map_precache_sectors` lock/unlock-ит 3×3 область вокруг целевой точки. `sector_lock` сначала ищет cache, проверяет bounds, затем вызывает loader; при заполнении сохраняет и вытесняет самый старый unlocked сектор. Цепочка: map opening/teleport → 3×3 precache → `sector_lock` → `sector_load_game`; обычные consumers также используют lock/unlock. [prefetch caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L850-L872), [cache/load/evict](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L860-L975), [game loader selection](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L201-L220).

**Зависимости:** sector provider, async-safe asset/runtime ownership, bounded cache and dirty-save path. **Acceptance:** synthetic route пересекает три сектора; locked sector не вытесняется, unlocked dirty sector восстанавливается после eviction/reload. **Steamwoven:** Частично: Area owner синхронно загружает девять core и одиннадцать present context секторов, явно сохраняет пять absent, владеет источниками и revocable selected-frame leases. Нет live refcount/prefetch/eviction, постоянного frame cache или секторального streaming при движении. [API/lifetime](../world/crash-site-area-loader.md).

<a id="fr-world-004"></a>

### FR-WORLD-004 — Непрерывность мира при пересечении sector edge

**Этап:** pre-alpha. **Наблюдаемое поведение:** переход через границу сектора продолжает то же actor движение и world position. Edge между техническими секторами сам по себе не становится стеной, teleport или отдельной картой; визуальный стык не даёт второй ячейки или двойного события.

**Границы:** единое chart/world identity и согласованность actor/tile/object queries около стыка. Правила passability, диагонального угла и динамических препятствий остаются отдельными evidence gates.

**CE:** одна packed map location адресует sector посредством `>>6`; `location_xy` принимает тот же location и проецирует его, а PC location update обновляет sector-dependent objects. Цепочка: actor location update (`sub_444270`) → `sector_id_from_loc` → sector-change notification. [location projector](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L134-L143), [PC location update caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5238-L5264), [sector address/bounds](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L592-L607).

**Зависимости:** FR-WORLD-002/003, движение, terrain/object query и будущая collision admission. **Acceptance:** synthetic adjacent sectors A/B имеют проверяемые маркеры на общей кромке; actor проходит через координаты 63→64 без смены chart, пропуска/дублирования клетки или сброса состояния. **Steamwoven:** Частично: world keys и terrain соседних Area sectors согласованы Data loader. WORLD003 движет актёра только внутри одного узкого участка; пересечение sector edge не интегрировано. [Area traversal research](../world/crash-site-area-traversal-contract.md) сохраняет NOT_READY из-за loaded registry/HP/state, а не отменяет bounded production profile.

<a id="fr-world-005"></a>

### FR-WORLD-005 — Базовый sector data и его изменения

**Этап:** pre-alpha. **Наблюдаемое поведение:** загрузчик собирает sector из versioned base data и применимых локальных изменений, не теряя базу, когда изменение отсутствует; dirty terrain/roof/light/script/object компоненты сохраняются отдельно для этого sector.

**Границы:** чтение установленной версии `.sec` и соответствующего `.dif`, границы разрешённых overlays и их применение. Универсальный архивный precedence и весь object/script runtime сюда не входят.

**CE:** `sector_load_game` открывает base и sector-specific diff; флаги выбирают соответствующие change sections; далее идут tile, roof, script/metadata/sound/object sections. `sector_save_game` сериализует только изменённые компоненты. Цепочка: `sector_lock` → loader → base `.sec` + optional `.dif`; eviction/flush → saver. [base/diff and tile load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1453-L1553), [remaining conditional sections](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1564-L1673), [diff save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1916-L2088).

**Зависимости:** parser для каждого компонента, fixed resource profile, typed diff policy и независимая версия источника. **Acceptance:** синтетическая база без diff совпадает с base; diff, задающий один компонент, меняет только его; повторное открытие даёт тот же aggregate state. **Steamwoven:** Частично: FILE-effective Area reader читает sector/static/mobile/prototype/default сведения с reconciliation; `DIF/PATCH/loose` overlays не применяются и состояние карты не изменяется. [Loader boundary](../world/crash-site-area-loader.md).

<a id="fr-world-006"></a>

### FR-WORLD-006 — Топология интерьеров и пещер

**Этап:** pre-alpha infrastructure; несколько оригинальных interiors — later. **Наблюдаемое поведение:** авторитетная модель может представить вход как локальный переход внутри той же map identity либо как переход в другую map identity. Тип контента «здание/пещера» сам по себе не диктует, какой вариант используется.

**Границы:** разделить sector traversal, same-map local passage и cross-map transfer. Требование не объявляет, что cave всегда является отдельной картой, и не определяет художественную/скриптовую логику входа.

**CE:** `TeleportData.map` плюс map ID позволяют запросить destination; `map==-1` разрешается как текущая карта, а `map_open_in_game` вызывается только для отличающейся map ID. JumpPoint хранит конкретные `dst_map` и `dst_loc`. Цепочка: endpoint lookup → `teleport_do` → `teleport_process` → та же карта или open destination. [TeleportData](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.h#L18-L31), [same-map resolution and conditional map open](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L191-L194), [conditional map load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L241-L248), [endpoint fields](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.h#L6-L13).

**Неизвестно:** для пещеры Crash Site связь с оригинальными данными исследована ([cave transition](../research/cave-transition-evidence.md)); для остальных cave/building entrance она не исследовалась, и какие оригинальные interiors используют same-map passage, а какие отдельную map, не определено. **Зависимости:** map identity, connector model и форматный профиль. **Acceptance:** два synthetic case одного типа «cave entrance» — один same-map, второй cross-map — должны сохранять ожидаемую топологию без эвристики по названию. **Steamwoven:** В [RES-015 inventory](../research/crash-site-area-inventory.md) исследованы отдельная cave map и её записи, а потенциальные endpoint bindings описаны в [area contract](../world/crash-site-area-contract.md). Production cave loader/scene, runtime топологии и оригинальное выполнение входа отсутствуют. Исследование ARC-WORLD-009 ([cave transition](../research/cave-transition-evidence.md)) показало, что пещера Crash Site — отдельная карта (cross-map), а вход и выход заданы двумя направленными литеральными записями в скриптах use-действия.

<a id="fr-world-007"></a>

### FR-WORLD-007 — Явные entrance/exit endpoints и направление

**Этап:** pre-alpha для одного connector; широкая сеть — later. **Наблюдаемое поведение:** endpoint задаёт source location и точные destination map/location. Обратный выход существует, только если он явно представлен как обратная запись или эквивалентный двусторонний контракт.

**Границы:** идентификатор и направление связи, проверка destination, optional flags; не реализует door animation, dialogue, quest gating или общее взаимодействие.

**CE:** сериализуемый `JumpPoint` хранит `loc`, `dst_map`, `dst_loc`; `jumppoint_get` ищет точное совпадение source location. `map_process_jumppoint` передаёт endpoint в teleport. Цепочка: PC update → map processor → source-cell lookup → teleport destination. [JumpPoint structure](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.h#L6-L16), [lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.c#L392-L405), [map processor](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1002-L1018).

**Неизвестно:** обратность записей конкретных оригинальных дверей и cave links без чтения связанного data entry; отдельные endpoints могут быть one-way. **Зависимости:** content association, стабильные map IDs, world coordinate chart и destination validation. **Acceptance:** у synthetic входа можно проверить точный exit endpoint; удаление reverse link должно сделать выход явно unavailable, а не придумывать возврат. **Steamwoven:** Частично только в данных/исследовании: Area snapshot сохраняет аутентифицированный numeric exterior-to-cave endpoint; потенциальные направленные пары рассмотрены в [area contract](../world/crash-site-area-contract.md). Нет исполнения connector, trigger dispatch, destination/return runtime или original activation parity.

<a id="fr-world-008"></a>

### FR-WORLD-008 — Endpoint срабатывает по world state, не по экрану

**Этап:** pre-alpha. **Наблюдаемое поведение:** у endpoint есть явно заданное trigger condition — например, вход actor в его source cell; решение выполняется по симуляционным координатам и не зависит от масштабирования/пиксельного попадания. Если целевое решение требует отдельного use action, оно остаётся отдельной явно заданной trigger policy.

**Границы:** обнаружение endpoint и ровно один dispatch на одно допустимое событие входа; повторный animation/render update не дублирует переход.

**CE:** в `sub_444270`, после обновления местоположения PC, цепочка идёт через `map_process_jumppoint(loc,obj)`; тот ищет точное `loc` и вызывает `teleport_do`. Это source-confirmed dispatch path, а не утверждение об association конкретной пещеры. [actor update caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5238-L5247) → [map dispatch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1002-L1018) → [exact source-location lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.c#L392-L405).

**Зависимости:** committed actor world position, trigger crossing event и transition coordinator. **Acceptance:** синтетический trigger на координате срабатывает один раз при входе при двух разных render scales; неподвижный actor и повторный кадр не запускают второй transfer. **Steamwoven:** отсутствует; WORLD-002 movement не зависит от render, но connector/trigger dispatch не реализован. Для пещеры Crash Site триггер — use-действие игрока на scenery-объекте входа, а не вход на клетку ([cave transition](../research/cave-transition-evidence.md#t3--the-trigger-is-a-use-action)), то есть отдельная trigger policy. [headless model scope](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/world/grid-movement-runtime.md#L18-L33), [world association boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L13).

<a id="fr-world-009"></a>

### FR-WORLD-009 — Переход между картами как единый lifecycle

**Этап:** pre-alpha для одного перехода. **Наблюдаемое поведение:** при cross-map connector игра закрывает старый map runtime, открывает целевой map ID и устанавливает заданную destination location; same-map connector не уничтожает и заново не создаёт весь map context.

**Границы:** sequence flush → resolve/open destination → destination actor placement → runtime rebind. Полный глобальный campaign save/load сюда не относится.

**CE:** `teleport_do` ставит запрос в очередь; `teleport_ping` вызывает `teleport_process`, который различает current/destination map и зовёт `map_open_in_game` только при смене ID; success затем устанавливает `location_origin` в destination location. `map_open_in_game` flush-ит карту и формирует base/save paths по карте. Цепочка: trigger → queued teleport → optional map flush/open → landing. [queue and ping caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L95-L138), [teleport map branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L191-L248), [map flush and open paths](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L792-L847).

**Зависимости:** FR-WORLD-001/006/007, state ownership, map load/unload and actor rebinding. **Acceptance:** synthetic A→B→A records map IDs and coordinates; после перехода активна только целевая карта, а same-map route сохраняет текущие map-owned references. **Steamwoven:** отсутствует: status прямо оставляет world transitions future work, а существующие WORLD/Simulation артефакты являются диагностическими/двигательными границами без map loader. [not implemented boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L27), [runtime exclusions](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/world/grid-movement-runtime.md#L5-L14).

<a id="fr-world-010"></a>

### FR-WORLD-010 — Переносимые actor links переживают cross-map transfer

**Этап:** later; до этого pre-alpha может ограничить вертикаль одним actor. **Наблюдаемое поведение:** перенос PC сохраняет идентичность и явные связи включённых в transfer followers; actor/container-owned payload не теряется и не создаёт дубликаты при смене карты.

**Границы:** только сериализация существующих передаваемых сущностей и повторное связывание. Система inventory, follower AI, combat, quest state и полный object model здесь не специфицируются.

**CE:** teleport собирает follower list, рекурсивно готовит группу и вложенные inventory objects; cross-map path пишет сущности в destination `mobile.mdy`, помечая исходные копии extinct. Цепочка: `teleport_process` → `schedule_teleport_obj_recursively` → cross-map serializer. [party scheduling](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L196-L211), [recursive follower/payload walk](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L280-L365), [cross-map persistence](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L381-L450).

**Зависимости:** stable entity identity, destination object store, ownership/follower references и atomic transfer. **Acceptance:** synthetic group проходит A→B→A с одинаковыми entity IDs, ownership и inventory references ровно по одному экземпляру. **Steamwoven:** отсутствует; нет original actor/entity traversal в playable world. [status](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25), [viewer limits](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L13-L14).

<a id="fr-world-011"></a>

### FR-WORLD-011 — Точное прибытие и готовность окрестных секторов

**Этап:** pre-alpha. **Наблюдаемое поведение:** actor появляется в указанной destination coordinate; origin/follow-camera центрируется на нём, а необходимое окружение готово для первого кадра/симуляционного шага.

**Границы:** destination location, camera origin и bounded neighboring-sector prefetch. Это не фиксирует исходный renderer или визуальное совпадение экрана.

**CE:** после cross-map `map_open_in_game` `teleport_process` выставляет origin в destination и вызывает `map_precache_sectors`; precache загружает 3×3 вокруг адресного сектора. [transition landing and prefetch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L241-L255), [3×3 prefetch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L850-L872).

**Зависимости:** FR-WORLD-002/003/009, camera follow policy, destination bounds and sector readiness. **Acceptance:** link arrival puts actor exactly at declared local cell, queries correct destination sector before rendering, and camera follows without snapping to the old map. **Steamwoven:** Частично: exact movement runtime и WORLD003 разделяют committed cell, continuous anchor и settled arrival; камера/спрайт не определяют cell commit. Это local movement, не прибытие на другую карту или prefetch. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

<a id="fr-world-012"></a>

### FR-WORLD-012 — Явный возврат в точку выхода

**Этап:** pre-alpha для проверенного reverse endpoint; широкое return UX — later. **Наблюдаемое поведение:** обратный connector возвращает actor на точную source/arrival location, определённую данными связи; система не угадывает вход по геометрии и не сбрасывает arrival location при повторном открытии карты.

**Границы:** обратная связь и location continuity. Автоматический stack недавних карт — отдельная опциональная политика и здесь не требуется.

**CE:** jump point хранит destination, поиск направлен по source location, а teleport передаёт объявленные map/location; автоматический return stack в прочитанном пути не обнаружен. [directed endpoint fields](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.h#L6-L13), [source lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.c#L392-L405), [destination passed to teleport](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1002-L1018).

**Зависимости:** verified endpoint association and FR-WORLD-007/009/011. **Acceptance:** enter synthetic interior, return through its explicit reverse endpoint and compare map ID/cell/camera context with the authored return cell. **Steamwoven:** отсутствует; текущие source/evidence work covers no world connectors or return behavior. [future world transitions](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-013"></a>

### FR-WORLD-013 — Изменённое состояние карты переживает уход и возврат

**Этап:** pre-alpha для bounded map-local state; полный сохранённый campaign — later. **Наблюдаемое поведение:** изменения sector и map-local entities доступны после выгрузки/повторной загрузки карты и не сбрасываются из-за перехода в интерьер или соседний регион.

**Границы:** dirty sector overlays, dynamic map entities, endpoint edits and map-specific save namespace. Полный сохранённый party/game snapshot, quests, inventory policy and crash-safe save format остаются вне требования.

**CE:** `map_open_in_game` вызывает `map_flush` для текущей карты; flush сохраняет sector, jump points, terrain и town map. `sector_save_game` пишет diff только изменённых компонентов, а map loader читает dynamic mobile state. [map flush on transition](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L802-L809), [map_flush component chain](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L952-L981), [sector diff save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1916-L2088), [dynamic map entity load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1539-L1566).

**Зависимости:** FR-WORLD-005/009, stable map identity, typed dirty records and persistence boundary. **Acceptance:** modify one synthetic sector and one map-local entity, travel away and return; both remain changed while untouched neighbor sector is byte/semantic-equivalent to baseline. **Steamwoven:** отсутствует как world state persistence; status marks persistence/save-load future work. Existing immutable probe outputs and local diagnostic exports do not persist gameplay changes. [not implemented boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25), [research outputs are bounded](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L20-L21).

<a id="fr-world-014"></a>

### FR-WORLD-014 — Переход с world map в локальный мир

**Этап:** later. **Наблюдаемое поведение:** выбранная overland destination преобразуется в координату нужной map identity; завершение travel возвращает actor в локальный world context с этой координатой, а не трактует пиксель world map как sector location.

**Границы:** регион/area mapping и travel completion handoff. Полный маршрутный AI, encounters, погода и оригинальная карта/интерфейс world map здесь не задаются.

**CE:** map catalogue несёт worldmap/area атрибуты. Wmap UI переводит выбранные координаты в map location и `wmap_ui_teleport` направляет PC на `MAP_TYPE_START_MAP`; state transition передаёт выбранную позицию в этот путь. [worldmap/area metadata](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L2036-L2049), [world map moving-state handoff](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L1669-L1747), [coordinate conversion and teleport to start map](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L1750-L1773).

**Зависимости:** FR-WORLD-001/009/011, world chart, map areas, travel clock and encounter policy. **Acceptance:** from a synthetic area map choose location `(x,y)`; final local position equals the versioned area transform and activates start map, with no screen-scale dependency. **Steamwoven:** partial data-only observation: one selected map catalogue/properties route was read, but no worldmap runtime, area binding or travel transition. [single-map data boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L9-L18), [world transitions absent](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-015"></a>

### FR-WORLD-015 — Опциональные эффекты и время перехода

**Этап:** later. **Наблюдаемое поведение:** transition record может задавать fade, sound/movie, game-time delta и follower policy независимо от map topology; пропущенный эффект не блокирует state transfer.

**Границы:** только orchestration hooks/flags and ordering. Реализация аудио, роликов, отдыха и scheduler вне этого доменного требования.

**CE:** `TeleportData` exposes fade/movie/sound/time and follower flags; chain `teleport_do` → `teleport_ping` → `teleport_process` applies requested effects before map branch and fade-in after arrival. [queue and ping caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L95-L138), [transition effect flags](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.h#L7-L31), [pre-transition effect ordering](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L164-L189), [arrival fade-in](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L269-L276).

**Зависимости:** transition coordinator, audio/video services and world clock. **Acceptance:** synthetic transition with each effect flag absent/present preserves identical destination state; only requested effects and declared elapsed time are emitted. **Steamwoven:** отсутствует; display/input labs and dialog runtime do not implement world map transition effects. [project component limits](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L11-L16), [world transitions future](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-016"></a>

### FR-WORLD-016 — Безопасная граница ошибки перехода

**Этап:** pre-alpha. **Наблюдаемое поведение:** неизвестная карта, отсутствующая связь, невалидная координата или ошибка загрузки отклоняет transfer с исходным actor/map state доступным для восстановления; ошибка имеет typed reason и не оставляет actor продублированным/потерянным.

**Границы:** validation/commit/rollback для одного transition. Это не общая resilience policy для всех загрузчиков или полная transaction system.

**CE:** source подтверждает failure points, но не требуемый rollback: `map_open_in_game` сначала flush-ит текущую карту, `map_open` закрывает её до проверки каталога/свойств; cross-map teleport сериализует переносимые объекты до попытки открыть целевую карту, а затем возвращает false при неудаче открытия. Явный rollback в этом пути не найден. [flush before destination open](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L802-L829), [close before path/properties validation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L579-L628), [cross-map entity write](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L381-L450), [map-open failure return](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L241-L248).

**Целевая политика:** проверить target/link/location до commit; если открытие всё же завершается ошибкой, восстановить исходный authoritative actor/map state либо оставить его в детерминированном recovery state. Не превращать порядок CE в norm. **Acceptance:** synthetic loader отказывает после validation и на каждом commit stage; ни один кейс не теряет и не дублирует actor, возвращает устойчивый failure receipt и сохраняет возможность продолжить из исходного или объявленного recovery state. **Steamwoven:** Частично: [Area owner](../world/crash-site-area-loader.md) не заменяет публикацию при неуспешной загрузке; [WORLD003](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy) отклоняет Blocked/Unknown/Unsupported атомарно. Полного transfer transaction/rollback нет.

## Краткая таблица покрытия Steamwoven

Статусы показывают только покрытие на закреплённом срезе. `Нет` означает отсутствие компонента в проверенной ветке, а не опровержение поведения оригинальной игры. `Частично` ограничено пояснением в ячейке. Существующие bounded-тесты засчитываются только для указанного локального контракта; предложенные переходы карт и original-data association не запускались. Просмотр исходников сам по себе не считается runtime-проверкой.

| ID | Данные | Правила | Интеграция | Проверка |
|---|---|---|---|---|
| FR-WORLD-001 | Частично — фиксированный каталог map5000/runtime1 и townmap2 | Частично — строгий выбор наружного Data-профиля | Частично — Area API; общего game-map bootstrap нет | Частично — production synthetic и полный opt-in area load |
| FR-WORLD-002 | Частично — multisector keys, checked pack/unpack и world/local координаты Area | Частично — границы Area и конечной movement-сетки | Частично — Area snapshot и viewer x4..8/y0..3; не общий map runtime | Частично — Area coordinate fixtures и bounded original bridge |
| FR-WORLD-003 | Частично — 9 core и 11 present context секторов; 5 absent явны | Частично — конечный read-only owner/lease lifecycle; нет streaming/eviction | Частично — самостоятельный Area API, не live sector cache | Частично — полная Data-загрузка/disposal; gameplay eviction не проверен |
| FR-WORLD-004 | Частично — непрерывные world keys/terrain нескольких секторов | Частично — loader сопоставляет границы; движение остаётся в одном секторе | Частично — Data continuity; crossing actor/renderer отсутствует | Частично — adjacent Data fixtures; crossing movement не проверен |
| FR-WORLD-005 | Частично — FILE-effective .sec и aggregate/mobile/prototype поля Area | Частично — framing, defaults и immutable snapshot; DIF/PATCH не применяются | Частично — Area API; изменяемое состояние карты отсутствует | Частично — malformed/identity/original load; .sec/.dif lifecycle не проверен |
| FR-WORLD-006 | Нет — production cave/topology reader отсутствует; есть source/data research | Нет — production cave/topology runtime отсутствует | Нет — cave scene/connector не подключены | Нет — есть source/data research, переход не выполнялся |
| FR-WORLD-007 | Частично — numeric exterior endpoint в Area; потенциальные пары в исследовании | Нет — endpoint activation/direction runtime отсутствует | Нет — нет world connector | Частично — Data identity endpoint; original activation не проверена |
| FR-WORLD-008 | Нет — нет trigger associations | Нет — нет dispatch по входу на клетку | Нет | Нет |
| FR-WORLD-009 | Нет — нет destination map state | Нет — нет map lifecycle перехода | Нет — нет flush/open/rebind | Нет |
| FR-WORLD-010 | Нет — нет actor transfer payload | Нет — нет переносимых игровых сущностей | Нет — нет cross-map serializer | Нет |
| FR-WORLD-011 | Частично — Area origins и bounded actor/camera anchors | Частично — local settled cell/anchor; не map arrival | Нет — нет destination-map load/prefetch/rebind | Частично — bounded center-arrival tests; cross-map arrival не проверен |
| FR-WORLD-012 | Нет — нет обратных endpoints | Нет — нет возврата к authored point | Нет | Нет |
| FR-WORLD-013 | Частично — map-local overlay в [состоянии сессии](../world/game-session-runtime.md) | Частично — переживает уход/возврат и snapshot/restore | Нет — нет загрузки карт и переходов | Частично — синтетические тесты |
| FR-WORLD-014 | Частично — один каталог карт/свойств | Нет — нет world-map/area/travel правил | Нет — нет overland runtime | Нет |
| FR-WORLD-015 | Нет — нет флагов эффектов перехода | Нет — нет времени/эффектов перехода | Нет — нет переходного coordinator | Нет |
| FR-WORLD-016 | Частично — Area failure и bounded movement refusal | Частично — failed load не заменяет snapshot, denied step не меняет cell | Частично — Data owner и diagnostic session; не transfer rollback | Частично — malformed/cancellation/admission tests; map-transfer rollback отсутствует |

Проверки Data coordinates/neighbor selection и bounded movement относятся к разным слоям. Area fixtures/load подтверждают multisector metadata, а WORLD003 проверки — движение/отказ только внутри x4..8/y0..3. Cross-sector actor, connectors и map-transfer rollback не проверены. Новых runtime запусков в аудите нет; исторические candidate-specific receipts — в [каталоге](../project/status.md#verification-that-can-be-credited).

## Source inventory и непроверенные ветки

**CE — непосредственно прочитаны указанные диапазоны** (а не весь модуль): `src/game/map.c` (map save/load, open, sector prefetch, jump-point dispatch, flush, selected diff/dynamic load, map-list metadata; строки приведены в требованиях), `map.h` (API и map-list записи), `sector.c` (init/cache, coordinates/bounds, selected load/save, diff path), `sector.h` (types/API), `location.c` (location projection), `object.c` (`sub_444270` actor-location caller), `teleport.c/.h` (queue/process, map branch, recursive transfer, effects), `jumppoint.c/.h` (structure, exact lookup, selected read/write), `ui/wmap_ui.c` (selected map positioning/state handoff only). Все функциональные утверждения привязаны к конкретным immutable GitHub ranges рядом с ними.

**Steamwoven — напрямую прочитаны** checkout-root `AGENTS.md`, `docs/architecture/decisions.md`, `docs/research/method.md`, `docs/project/status.md`, `docs/project/roadmap.md`, `docs/formats/maps.md`, `docs/world/grid-movement-contract.md`, `docs/world/grid-movement-runtime.md`, `docs/research/cardinal-traversal-evidence.md` и `docs/world/cardinal-traversal-contract.md`. Catalog-status snapshot itself dates through PR #30; repo HEAD also includes PR #35/#36 documents and bounded implementation. В тестах движения непосредственно прочитаны только `GeometryTests.cs` 19–64 и `ValidationTests.cs` 58–91; production movement source был найден по символам/пути, но его тело здесь не аудировалось.

**Идентифицированы, но тела/полные caller paths не исследовались:** `src/game/area.c/.h`, полный travel/route/time-event path in `src/ui/wmap_ui.c`, `src/game/script.c/.h` teleport op callers, `src/game/ai.c` alternate teleport callers, `src/game/timeevent.c/.h`, `src/game/gamelib.c` and `gameinit.c` lifecycle, plus sector component implementations in `sector_*_list.c`, `tile.c` and `terrain.c`. Не читались оригинальные map-list/jump-point/cave entries или другие extracted game data. Поэтому требования не определяют concrete cave/interior association, full overland route policy или all sector component semantics.

**Проверки:** source/document review only. Не выполнялись game launch, CE build, data extraction, runtime smoke checks или acceptance scenarios; они предложены как будущие target checks.

## CE TODO/stub и границы надёжности

Найденные в исследованной цепочке замечания не становятся целевым контрактом:

- `map_save` документирует, что ошибка module save прекращает цикл, но функция всё равно возвращает `true`; это открытый риск достоверности сохранения. [map_save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L392-L423).
- `map_open_in_game` помечает FIXME: после `obj_validate_system` failure запрашивается quit, но выполнение продолжается. [validation path](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L807-L845).
- `sub_40FED0` — пустая функция; назначение неизвестно, поэтому из её имени или наличия нельзя выводить world behavior. [stub](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L874-L877).
- `sector_save_game` помечает FIXME на `fclose(NULL)` при ошибке открытия diff, а cross-map serializer помечает file leak при ошибке записи. Это подтверждает необходимость отдельной fail-safe policy, но не её реализацию в CE. [sector save error](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1981-L1986), [teleport write error](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L422-L431).
- World-map source labels `wmap_ui_message_filter` with TODO “Review, split”; это рефакторинг UI-функции, не доказательство какого-либо travel contract. [TODO](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L1775-L1778).

## Открытые вопросы и что именно подтверждено

1. Не проверялись оригинальные данные, связывающие конкретные cave/interior entrance и exit с map ID, source cell и destination cell. Требование FR-WORLD-006 задаёт способность модели, а не ответ про каждую оригинальную пещеру.
2. Нет подтверждения на оригинальном исполняемом файле: вся CE-часть — чтение кода с commit `a7ff41b…`. Наличие функций или аргументов не подтверждает фактический runtime path на оригинальных данных.
3. Фактическая original-data precedence между base map, PATCH, loose files и sector diffs остаётся за рамками этой выборки. Исследование выбранного Steamwoven сектора прямо не универсализирует format/precedence.
4. Не разрешены map-specific worldmap/area associations и travel-time/encounter policy для всего каталога.
5. Точные failure/rollback semantics CE недостаточно надёжны и не должны копироваться без отдельного решения.

Исследование было read-only: игра не запускалась, данные/bytes/assets не извлекались для отчёта и не включались в него; source code не копировался или механически переводился. Source license/provenance boundary остаётся отдельным от этих функциональных требований: [Steamwoven research method](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/research/method.md#L1-L5), [CE license attribution in current evidence](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/research/cardinal-traversal-evidence.md#L52-L60).
