# Функциональные требования: карты, сектора и переходы

**Текущий статус:** статус каждого требования этого документа, реализованный объём, остаток и документация — в [матрице покрытия](coverage/world.md) (пересверено 2026-10-20, ARC-DEV-027). Поля «Steamwoven» ниже и сводный раздел покрытия — подробности на дату их последней правки (исходный аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); при расхождении верна матрица.

ARC-RES-017: [whole-exterior traversal research](../world/area-traversal-contract.md)
для FR-WORLD-002/003/004: coordinates, neighbor binding и edge evidence;
Research/Proposed, NOT_READY, без streaming/eviction или crossing movement runtime.

**Срез исследования:** 2026-10-02. Проверенный checkout: `arcanum-steamwoven` commit `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`; поведение оригинала — по функциям исполняемого файла 1.0.7.4 по указанным адресам. Это исходный source-research baseline; текущее implementation coverage указано выше. Требования относятся только к этому домену; это не полный аудит игры и не утверждённый backlog.

## Как читать выводы

**ARC-RES-018 — RESEARCH:** [clock/light контракт](../world/time-lighting-contract.md)
связывает numeric map default/sector sentinel с активным hour scheme, не объявляя
отдельную ambient policy для каждого visible neighbour. Bounded original selection
и synthetic override/coverage проверки не реализуют world streaming/transfer;
stationary coordinates и masks остаются NOT_READY. Legacy/HD shared state —
предложение; production clock и original-runtime parity не подтверждены.

ARC-DATA-009 добавляет [bounded complete-area Data API](../world/area-loader.md):
survey 621 → 9 core sectors и явный context, immutable world/source keys, 36,864
core terrain, 426 static и 88 mobile bodies, reconcile 7,399 aggregate records.
Это частичное data-layer покрытие FR-WORLD-001/002/003/005/007 (источники, адреса,
snapshot и numeric endpoint), не streaming/eviction runtime, crossing movement,
cave transition, DIF/PATCH или original-runtime parity. [Матрица покрытия](coverage/world.md) учитывает этот merged объём.

Позднее bounded research ARC-RES-015: [перепись Crash Site area](../research/area-inventory.md) и [предлагаемый multi-sector/mobile контракт](../world/area-contract.md) уточняют источники, границу и потенциальные cave endpoints. Это исследование, не реализация FR-WORLD-001..007 и не original-runtime parity; актуальный production coverage указан ниже отдельно от исследования.

- **Оригинал** — чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в [журнале сверки](../research/original-verification-ledger.md); названные строки журнала проверены в exe. Это не проверка поведения на оригинальных данных.
- **Целевая политика** и acceptance-сценарии ниже — предлагаемое поведение Steamwoven. Они опираются на поведение оригинала, но не являются уже принятой политикой продукта.
- **Неизвестно** означает, что кода, конкретной связи в исходных данных или проверки недостаточно для вывода.
- Покрытие Steamwoven пересверено по текущему audit baseline: Area Data API, production traversal и WORLD003 имеют разный объём. Полный exterior snapshot не закрывает live-registry/HP/state frontiers, streaming или переход в cave. См. [каталог компонентов](../project/status.md).

## Предложение по этапам

Метки **pre-alpha** и **later** — предварительная оценка только для требований этого world domain. Минимальные pre-alpha-кандидаты здесь — идентичность карты/сектора, границы секторов, один проверяемый connector, точное место прибытия и сохранение изменённого состояния при уходе/возврате. Это не полное определение pre-alpha, не требование загрузить весь мир и не ранжирование других игровых механик. Метки являются предложением для согласования раннего объёма; они не являются сроками, заданиями или полномочием создавать задачи. [Решения по архитектурному направлению](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/architecture/decisions.md), [roadmap и граница task authority](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/roadmap.md#L1-L4).

## Требования

<a id="fr-world-001"></a>

### FR-WORLD-001 — Каталог карт и региональные атрибуты

**Этап:** pre-alpha. **Наблюдаемое поведение:** по устойчивому ID система выбирает карту, её стартовую координату и размеры; региональные свойства карты (`world map`, `area`, стартовый тип) остаются привязаны к тому же ID во время игры.

**Границы:** метаданные каталога, разрешение имени/ID и проверка существования целевой карты. Порядок выбора между DAT/PATCH/loose files сюда не включён.

**Оригинал:** загрузка списка карт (0x4125C0) читает имя, X/Y, map type, worldmap и area; getters возвращают их по номеру карты. Список — одна таблица на 200 записей, чтение останавливается на первом пропуске номера (сверено с exe, строка [LC1](../research/ledger/data.md#limits-census-arc-data-029)). Цепочка загрузки карты разрешает запись списка → открытие карты в игре (0x40FC70) строит пути → открытие карты (0x40F4E0) устанавливает текущую карту. Загрузка списка (0x4125C0), getters (0x40FEE0, 0x40FF40, 0x40FF50, 0x40FF70, 0x40FFE0, 0x410020), открытие в игре → открытие карты (0x40FC70).

**Зависимости:** versioned map catalogue, карта-координатная система и resolver исходных ресурсов. **Acceptance:** два синтетических map ID с разными area/start entries разрешаются независимо; неверный ID оставляет текущий мир неизменным. **Steamwoven:** Частично: [Area Data owner](../world/area-loader.md) аутентифицирует фиксированный map5000/runtime1/townmap2 и конечный sector profile. Production-каталога всех карт, campaign bootstrap и общего выбора карты нет. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-world-002"></a>

### FR-WORLD-002 — Глобальные координаты, границы и адрес сектора

**Этап:** pre-alpha. **Наблюдаемое поведение:** локальная точка карты однозначно разрешается в сектор и локальную ячейку; размеры карты задают допустимую область, а попытка перейти за неё отклоняется.

**Границы:** координатный codec, деление карты на ячейки/сектора и проверка bounds. Проекция на экран и collision policy не определяются этим требованием.

**Оригинал:** адрес сектора (0x4CFC50) вычисляет X/Y сектора сдвигом координат на 6 бит, обратное преобразование (0x4CFC90) восстанавливает начало сектора; открытие карты (0x40F4E0) задаёт location/sector limits из `map.prp`, а захват сектора (0x4D0540) проверяет эти пределы (сверено с exe, строка [SS2](../research/ledger/world.md#main-map-streaming-arc-data-030)). Caller path: открытие карты → bounds → location → sector lookup. sector coordinate conversion (0x4CFC50, 0x4CFC90), map dimensions and limits (0x40F4E0), map limits set (0x40F4E0), sector bounds (0x4D0540).

**Зависимости:** оригинальный format profile, проверенная signedness/overflow policy и единые world coordinates для Simulation и Data. **Acceptance:** synthetic bounds проверяют две стороны каждого sector edge, включая последнюю допустимую и первую недопустимую ячейку; pack/unpack сохраняет ID на границе. **Steamwoven:** Частично: Area snapshots имеют checked sector pack/unpack, origins и distinct world/source/occurrence keys для нескольких секторов; локальные serialized static и direct mobile coordinates различаются. [Area API](../world/area-loader.md). Movement/viewer остаются в bounded rectangle x4..8/y0..3, не общем map runtime.

<a id="fr-world-003"></a>

### FR-WORLD-003 — Загрузка и выселение соседних секторов

**Этап:** pre-alpha. **Наблюдаемое поведение:** когда actor/camera или активная world query достигает сектора, нужные секторы доступны; cache удерживает используемые записи, освобождает безопасные неиспользуемые и записывает dirty state перед выселением.

**Границы:** загрузка по sector ID, refcount/pinning, bounded cache, prefetch и unload. Не задаются точный размер cache, LRU policy или фоновая многопоточность.

**Оригинал** (сверено с exe, строки [SS1–SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)): предзагрузка (0x40FE00) lock/unlock-ит 3×3 область вокруг целевой точки. Захват сектора (0x4D0540) сначала ищет в cache на 16 секторов, проверяет bounds, затем вызывает loader; при заполнении сохраняет и вытесняет давнее всех использованный сектор без ссылок, а если ссылки есть у всех — отказывает. Цепочка: teleport → 3×3 precache → захват сектора → загрузка сектора (0x4D1A30); обычные consumers также используют lock/unlock. prefetch caller (0x40FE00), cache/load/evict (0x4D0540), game loader selection (0x4CEF70).

**Зависимости:** sector provider, async-safe asset/runtime ownership, bounded cache and dirty-save path. **Acceptance:** synthetic route пересекает три сектора; locked sector не вытесняется, unlocked dirty sector восстанавливается после eviction/reload. **Steamwoven:** Частично: Area owner синхронно загружает девять core и одиннадцать present context секторов, явно сохраняет пять absent, владеет источниками и revocable selected-frame leases. Нет live refcount/prefetch/eviction, постоянного frame cache или секторального streaming при движении. [API/lifetime](../world/area-loader.md).

<a id="fr-world-004"></a>

### FR-WORLD-004 — Непрерывность мира при пересечении sector edge

**Этап:** pre-alpha. **Наблюдаемое поведение:** переход через границу сектора продолжает то же actor движение и world position. Edge между техническими секторами сам по себе не становится стеной, teleport или отдельной картой; визуальный стык не даёт второй ячейки или двойного события.

**Границы:** единое chart/world identity и согласованность actor/tile/object queries около стыка. Правила passability, диагонального угла и динамических препятствий остаются отдельными evidence gates.

**Оригинал:** одна packed map location адресует sector посредством `>>6`; проекция location (0x4B8680) принимает тот же location и проецирует его, а PC location update обновляет sector-dependent objects. Цепочка: обновление положения объекта (0x444270) → адрес сектора (0x4CFC50) → sector-change notification. location projector (0x4B8680), PC location update caller (0x444270), sector address/bounds (0x4CFC50, 0x4CFC90).

**Зависимости:** FR-WORLD-002/003, движение, terrain/object query и будущая collision admission. **Acceptance:** synthetic adjacent sectors A/B имеют проверяемые маркеры на общей кромке; actor проходит через координаты 63→64 без смены chart, пропуска/дублирования клетки или сброса состояния. **Steamwoven:** Частично: world keys и terrain соседних Area sectors согласованы Data loader. WORLD003 движет актёра только внутри одного узкого участка; пересечение sector edge не интегрировано. [Area traversal research](../world/area-traversal-contract.md) сохраняет NOT_READY из-за loaded registry/HP/state, а не отменяет bounded production profile.

<a id="fr-world-005"></a>

### FR-WORLD-005 — Базовый sector data и его изменения

**Этап:** pre-alpha. **Наблюдаемое поведение:** загрузчик собирает sector из versioned base data и применимых локальных изменений, не теряя базу, когда изменение отсутствует; dirty terrain/roof/light/script/object компоненты сохраняются отдельно для этого sector.

**Границы:** чтение установленной версии `.sec` и соответствующего `.dif`, границы разрешённых overlays и их применение. Универсальный архивный precedence и весь object/script runtime сюда не входят.

**Оригинал:** загрузка сектора (0x4D1A30) открывает base и sector-specific diff; флаги выбирают соответствующие change sections; далее идут light, tile, roof, script/metadata/sound/object sections (источники и порядок секций сверены с exe, строка [SS5](../research/ledger/world.md#main-map-streaming-arc-data-030)). Сохранение сектора (0x4D2750) сериализует только изменённые компоненты (F0). Цепочка: захват сектора → loader → base `.sec` + optional `.dif`; eviction/flush → saver. base/diff and tile load (0x4D1A30), remaining conditional sections (0x4D1A30), diff save (0x4D2750).

**Зависимости:** parser для каждого компонента, fixed resource profile, typed diff policy и независимая версия источника. **Acceptance:** синтетическая база без diff совпадает с base; diff, задающий один компонент, меняет только его; повторное открытие даёт тот же aggregate state. **Steamwoven:** Частично: FILE-effective Area reader читает sector/static/mobile/prototype/default сведения с reconciliation; `DIF/PATCH/loose` overlays не применяются и состояние карты не изменяется. [Loader boundary](../world/area-loader.md).

<a id="fr-world-006"></a>

### FR-WORLD-006 — Топология интерьеров и пещер

**Этап:** pre-alpha infrastructure; несколько оригинальных interiors — later. **Наблюдаемое поведение:** авторитетная модель может представить вход как локальный переход внутри той же map identity либо как переход в другую map identity. Тип контента «здание/пещера» сам по себе не диктует, какой вариант используется.

**Границы:** разделить sector traversal, same-map local passage и cross-map transfer. Требование не объявляет, что cave всегда является отдельной картой, и не определяет художественную/скриптовую логику входа.

**Оригинал:** поле карты в запросе телепорта плюс map ID позволяют запросить destination; карта −1 разрешается как текущая (сверено с exe, строка [TR4](../research/ledger/world.md#map-transitions-arc-data-025)), а открытие карты в игре (0x40FC70) вызывается только для отличающейся map ID (F0). Запись jump point хранит конкретные карту и клетку назначения. Цепочка: endpoint lookup → постановка телепорта (0x4D3380) → обработка телепорта (0x4D3460) → та же карта или open destination. Запрос телепорта, same-map resolution and conditional map open (0x4D3460), conditional map load (0x4D3460), endpoint fields.

**Неизвестно:** для пещеры Crash Site связь с оригинальными данными исследована ([cave transition](../research/cave-transition-evidence.md)); для остальных cave/building entrance она не исследовалась, и какие оригинальные interiors используют same-map passage, а какие отдельную map, не определено. **Зависимости:** map identity, connector model и форматный профиль. **Acceptance:** два synthetic case одного типа «cave entrance» — один same-map, второй cross-map — должны сохранять ожидаемую топологию без эвристики по названию. **Steamwoven:** В [RES-015 inventory](../research/area-inventory.md) исследованы отдельная cave map и её записи, а потенциальные endpoint bindings описаны в [area contract](../world/area-contract.md). Data загружает пещеру под собственным pinned-профилем; area viewer (ARC-WORLD-009) рисует её и переключает сцену Crash Site ↔ пещера через `AreaTransitionCoordinator` в viewer Core ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Общего production runtime топологии и остальных interiors нет. Исследование ARC-WORLD-009 ([cave transition](../research/cave-transition-evidence.md)) показало, что пещера Crash Site — отдельная карта (cross-map), а вход и выход заданы двумя направленными литеральными записями в скриптах use-действия.

<a id="fr-world-007"></a>

### FR-WORLD-007 — Явные entrance/exit endpoints и направление

**Этап:** pre-alpha для одного connector; широкая сеть — later. **Наблюдаемое поведение:** endpoint задаёт source location и точные destination map/location. Обратный выход существует, только если он явно представлен как обратная запись или эквивалентный двусторонний контракт.

**Границы:** идентификатор и направление связи, проверка destination, optional flags; не реализует door animation, dialogue, quest gating или общее взаимодействие.

**Оригинал:** сериализуемая запись jump point хранит исходную клетку, карту и клетку назначения; поиск (0x4E3450) ищет точное совпадение source location, при нескольких записях на клетке выигрывает первая, флаги не проверяются (сверено с exe, строка [TR5](../research/ledger/world.md#map-transitions-arc-data-025)). Обработка jump point карты (0x4101D0) передаёт endpoint в teleport. Цепочка: PC update → map processor → source-cell lookup → teleport destination. Запись jump point, lookup (0x4E3450), map processor (0x4101D0).

**Неизвестно:** обратность записей конкретных оригинальных дверей и cave links без чтения связанного data entry; отдельные endpoints могут быть one-way. **Зависимости:** content association, стабильные map IDs, world coordinate chart и destination validation. **Acceptance:** у synthetic входа можно проверить точный exit endpoint; удаление reverse link должно сделать выход явно unavailable, а не придумывать возврат. **Steamwoven:** Частично: Area snapshot публикует оба endpoint пещеры Crash Site (вход 2793 и явную обратную запись 1532) с картой и клеткой прибытия; viewer Core проверяет, что целевая карта загружена и клетка прибытия допущена, иначе endpoint недоступен ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Общего connector runtime и original activation parity нет; прочие пары остаются в [area contract](../world/area-contract.md).

<a id="fr-world-008"></a>

### FR-WORLD-008 — Endpoint срабатывает по world state, не по экрану

**Этап:** pre-alpha. **Наблюдаемое поведение:** у endpoint есть явно заданное trigger condition — например, вход actor в его source cell; решение выполняется по симуляционным координатам и не зависит от масштабирования/пиксельного попадания. Если целевое решение требует отдельного use action, оно остаётся отдельной явно заданной trigger policy.

**Границы:** обнаружение endpoint и ровно один dispatch на одно допустимое событие входа; повторный animation/render update не дублирует переход.

**Оригинал:** в обновлении положения объекта (0x444270), после обновления местоположения PC, цепочка идёт через обработку jump point карты (0x4101D0); та ищет точную клетку и вызывает постановку телепорта (0x4D3380). Путь сверен с exe (строка [TR3](../research/ledger/world.md#map-transitions-arc-data-025): для любого PC, пока вложенных переходов по jump point меньше 9, флаги 0); это dispatch path, а не утверждение об association конкретной пещеры. actor update caller (0x444270) → map dispatch (0x4101D0) → exact source-location lookup (0x4E3450).

**Зависимости:** committed actor world position, trigger crossing event и transition coordinator. **Acceptance:** синтетический trigger на координате срабатывает один раз при входе при двух разных render scales; неподвижный actor и повторный кадр не запускают второй transfer. **Steamwoven:** Частично, только для пещеры Crash Site: use принимается по committed-клетке актёра и клетке объекта (Чебышёв ≤ 2, шаг к объекту не заблокирован); пиксель спрайта лишь выбирает объект. Принятый use ставится в очередь и применяется один раз на settled-шаге; повторный запрос при pending отклоняется ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Проверка при разных render scales не выполнялась; триггеров по входу на клетку нет. Для пещеры Crash Site триггер — use-действие игрока на scenery-объекте входа, а не вход на клетку ([cave transition](../research/cave-transition-evidence.md#t3--the-trigger-is-a-use-action)), то есть отдельная trigger policy. [headless model scope](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/world/grid-movement-runtime.md#L18-L33), [world association boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L13).

<a id="fr-world-009"></a>

### FR-WORLD-009 — Переход между картами как единый lifecycle

**Этап:** pre-alpha для одного перехода. **Наблюдаемое поведение:** при cross-map connector игра закрывает старый map runtime, открывает целевой map ID и устанавливает заданную destination location; same-map connector не уничтожает и заново не создаёт весь map context.

**Границы:** sequence flush → resolve/open destination → destination actor placement → runtime rebind. Полный глобальный campaign save/load сюда не относится.

**Оригинал:** постановка телепорта (0x4D3380) ставит запрос в очередь (запрос при ожидающем телепорте локального PC отклоняется, прочие заменяются; строка [TR4](../research/ledger/world.md#map-transitions-arc-data-025)); обновление (0x4D3330) вызывает обработку телепорта (0x4D3460), которая различает current/destination map и зовёт открытие карты в игре (0x40FC70) только при смене ID; success затем центрирует обзор на destination location (F0). Открытие карты в игре flush-ит карту и формирует base/save paths по карте. Цепочка: trigger → queued teleport → optional map flush/open → landing. queue and ping caller (0x4D3330, 0x4D3380), teleport map branch (0x4D3460), map flush and open paths (0x40FC70).

**Зависимости:** FR-WORLD-001/006/007, state ownership, map load/unload and actor rebinding. **Acceptance:** synthetic A→B→A records map IDs and coordinates; после перехода активна только целевая карта, а same-map route сохраняет текущие map-owned references. **Steamwoven:** Частично, только Crash Site ↔ пещера в area viewer: активна одна карта; переход меняет сцену, шаговые данные, chart и сессию движения, а состояние сессии ARC-WORLD-008 записывает карту и клетку актёра. A→B→A проверен на оригинальных данных и нативно ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Отклонение от оригинала: обе карты загружаются заранее и остаются в памяти (нет unload/open при переходе); общего map lifecycle нет.

<a id="fr-world-010"></a>

### FR-WORLD-010 — Переносимые actor links переживают cross-map transfer

**Этап:** later; до этого pre-alpha может ограничить вертикаль одним actor. **Наблюдаемое поведение:** перенос PC сохраняет идентичность и явные связи включённых в transfer followers; actor/container-owned payload не теряется и не создаёт дубликаты при смене карты.

**Границы:** только сериализация существующих передаваемых сущностей и повторное связывание. Система inventory, follower AI, combat, quest state и полный object model здесь не специфицируются.

**Оригинал** (сверено с exe двумя чтецами, кроме FM2 и FM9 (F1), строки [FM1–FM14](../research/ledger/social.md#followers-across-maps-arc-res-076), [исследование](../research/follower-map-transfer.md)): телепорт собирает объект, его инвентарь и не ждущих спутников рекурсивно, на одну клетку; при смене карты каждый объект целиком, со своим неизменным ID, дописывается в `mobile.mdy` карты назначения как динамический, а исходная копия помечается уничтоженной, и её ID уходит в `mobile.des` исходной карты. Переходное боевое состояние (фокус, последний ударивший, список врагов, бегство, боевой режим, AI-события) очищается, события времени, где все объекты переезжают, переносятся. Единственная связь NPC с картой — поле 243 (карта телепорта): бывший спутник на чужой карте уходит домой при принудительном проходе AI. Цепочка: обработка телепорта (0x4D3460) → рекурсивная подготовка объектов (0x4D3760) → cross-map serializer (0x4D39A0).

**Зависимости:** stable entity identity, destination object store, ownership/follower references и atomic transfer. **Acceptance:** synthetic group проходит A→B→A с одинаковыми entity IDs, ownership и inventory references ровно по одному экземпляру. **Steamwoven:** частично (ARC-WORLD-017): в AREA viewer следующий за PC Вергилий проходит Crash Site → пещера → Crash Site с тем же entity id, ровно в одном экземпляре; его положение — map-local состояние сессии, записанное вместе с PC одним атомарным шагом ([контракт](../world/companion-follow-runtime.md#map-change-arc-world-017)). Нет переноса inventory payload, последователей последователей и других карт. Базовая запись спутника (боевые поля, строка AI, держатель инвентаря, тело) потребителями ищется среди объектов показанной карты, поэтому на чужой карте он «гость» без боевых данных; с ARC-DEV-072 (MIG1) единый резолвер [находит запись по identity](../world/record-resolver.md) (домашняя карта читается по требованию), потребители переводятся срезами MIG2–MIG5; системный разрыв и план срезов V1–V6 — [ARC-RES-076](../research/follower-map-transfer.md#fix-plan).

<a id="fr-world-011"></a>

### FR-WORLD-011 — Точное прибытие и готовность окрестных секторов

**Этап:** pre-alpha. **Наблюдаемое поведение:** actor появляется в указанной destination coordinate; origin/follow-camera центрируется на нём, а необходимое окружение готово для первого кадра/симуляционного шага.

**Границы:** destination location, camera origin и bounded neighboring-sector prefetch. Это не фиксирует исходный renderer или визуальное совпадение экрана.

**Оригинал:** после cross-map открытия карты (0x40FC70) обработка телепорта (0x4D3460) выставляет origin в destination; для локального PC после любой смены карты запускается предзагрузка (0x40FE00, через 0x4EF1E0), которая загружает 3×3 вокруг адресного сектора (сверено с exe, строка [SS4](../research/ledger/world.md#main-map-streaming-arc-data-030)). transition landing and prefetch (0x4D3460), 3×3 prefetch (0x40FE00).

**Зависимости:** FR-WORLD-002/003/009, camera follow policy, destination bounds and sector readiness. **Acceptance:** link arrival puts actor exactly at declared local cell, queries correct destination sector before rendering, and camera follows without snapping to the old map. **Steamwoven:** Частично: exact movement runtime и WORLD003 разделяют committed cell, continuous anchor и settled arrival; камера/спрайт не определяют cell commit. Для пещеры Crash Site актёр после перехода стоит ровно на authored-клетке прибытия в новой сессии, камера центрируется на нём до публикации сцены ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)). Prefetch секторов нет: единственный сектор пещеры загружен заранее. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

<a id="fr-world-012"></a>

### FR-WORLD-012 — Явный возврат в точку выхода

**Этап:** pre-alpha для проверенного reverse endpoint; широкое return UX — later. **Наблюдаемое поведение:** обратный connector возвращает actor на точную source/arrival location, определённую данными связи; система не угадывает вход по геометрии и не сбрасывает arrival location при повторном открытии карты.

**Границы:** обратная связь и location continuity. Автоматический stack недавних карт — отдельная опциональная политика и здесь не требуется.

**Оригинал:** jump point хранит destination, поиск направлен по source location, а teleport передаёт объявленные map/location; автоматический return stack в прочитанном пути не обнаружен. directed endpoint fields, source lookup (0x4E3450), destination passed to teleport (0x4101D0).

**Зависимости:** verified endpoint association and FR-WORLD-007/009/011. **Acceptance:** enter synthetic interior, return through its explicit reverse endpoint and compare map ID/cell/camera context with the authored return cell. **Steamwoven:** Частично, только пещера Crash Site: возврат идёт лишь по явной обратной записи 1532 на клетку (92913,82598); без неё объект выхода не является триггером, обратный путь не выводится. Real-data A→B→A сравнивает карту и клетку ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)); camera context не проверяется.

<a id="fr-world-013"></a>

### FR-WORLD-013 — Изменённое состояние карты переживает уход и возврат

**Этап:** pre-alpha для bounded map-local state; полный сохранённый campaign — later. **Наблюдаемое поведение:** изменения sector и map-local entities доступны после выгрузки/повторной загрузки карты и не сбрасываются из-за перехода в интерьер или соседний регион.

**Границы:** dirty sector overlays, dynamic map entities, endpoint edits and map-specific save namespace. Полный сохранённый party/game snapshot, quests, inventory policy and crash-safe save format остаются вне требования.

**Оригинал:** открытие карты в игре (0x40FC70) вызывает flush текущей карты (0x410070); flush сохраняет sector, jump points, terrain и town map. Сохранение сектора (0x4D2750) пишет diff только изменённых компонентов, а map loader читает dynamic mobile state. map flush on transition (0x40FC70), flush component chain (0x410070), sector diff save (0x4D2750), dynamic map entity load (0x411450).

**Зависимости:** FR-WORLD-005/009, stable map identity, typed dirty records and persistence boundary. **Acceptance:** modify one synthetic sector and one map-local entity, travel away and return; both remain changed while untouched neighbor sector is byte/semantic-equivalent to baseline. **Steamwoven:** отсутствует как world state persistence; status marks persistence/save-load future work. Existing immutable probe outputs and local diagnostic exports do not persist gameplay changes. [not implemented boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25), [research outputs are bounded](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L20-L21).

<a id="fr-world-014"></a>

### FR-WORLD-014 — Переход с world map в локальный мир

**Этап:** later. **Наблюдаемое поведение:** выбранная overland destination преобразуется в координату нужной map identity; завершение travel возвращает actor в локальный world context с этой координатой, а не трактует пиксель world map как sector location.

**Границы:** регион/area mapping и travel completion handoff. Полный маршрутный AI, encounters, погода и оригинальная карта/интерфейс world map здесь не задаются.

**Оригинал:** map catalogue несёт worldmap/area атрибуты. Окно карты мира переводит выбранные координаты в map location, и телепорт с карты мира (0x561860) направляет PC на стартовую карту; state transition передаёт выбранную позицию в этот путь. worldmap/area metadata (0x4125C0), world map moving-state handoff (0x5615D0), coordinate conversion and teleport to start map (0x561800, 0x561860).

**Зависимости:** FR-WORLD-001/009/011, world chart, map areas, travel clock and encounter policy. **Acceptance:** from a synthetic area map choose location `(x,y)`; final local position equals the versioned area transform and activates start map, with no screen-scale dependency. **Steamwoven:** partial data-only observation: one selected map catalogue/properties route was read, but no worldmap runtime, area binding or travel transition. [single-map data boundary](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/formats/maps.md#L9-L18), [world transitions absent](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-015"></a>

### FR-WORLD-015 — Опциональные эффекты и время перехода

**Этап:** later. **Наблюдаемое поведение:** transition record может задавать fade, sound/movie, game-time delta и follower policy независимо от map topology; пропущенный эффект не блокирует state transfer.

**Границы:** только orchestration hooks/flags and ordering. Реализация аудио, роликов, отдыха и scheduler вне этого доменного требования.

**Оригинал:** the teleport request exposes fade/movie/sound/time and follower flags (the script fade-and-teleport action sets them, checked in the executable, ledger row [TR2](../research/ledger/world.md#map-transitions-arc-data-025)); chain teleport queue (0x4D3380) → ping (0x4D3330) → process (0x4D3460) applies requested effects before map branch and fade-in after arrival (F0). queue and ping caller (0x4D3330, 0x4D3380), transition effect flags, pre-transition effect ordering (0x4D3460), arrival fade-in (0x4D3460).

**Зависимости:** transition coordinator, audio/video services and world clock. **Acceptance:** synthetic transition with each effect flag absent/present preserves identical destination state; only requested effects and declared elapsed time are emitted. **Steamwoven:** отсутствует; display/input labs and dialog runtime do not implement world map transition effects. [project component limits](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L11-L16), [world transitions future](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25).

<a id="fr-world-016"></a>

### FR-WORLD-016 — Безопасная граница ошибки перехода

**Этап:** pre-alpha. **Наблюдаемое поведение:** неизвестная карта, отсутствующая связь, невалидная координата или ошибка загрузки отклоняет transfer с исходным actor/map state доступным для восстановления; ошибка имеет typed reason и не оставляет actor продублированным/потерянным.

**Границы:** validation/commit/rollback для одного transition. Это не общая resilience policy для всех загрузчиков или полная transaction system.

**Оригинал:** чтение функций (F0) показывает failure points, но не требуемый rollback: открытие карты в игре (0x40FC70) сначала flush-ит текущую карту, открытие карты (0x40F4E0) закрывает её до проверки каталога/свойств; cross-map teleport сериализует переносимые объекты до попытки открыть целевую карту, а затем возвращает false при неудаче открытия. Явный rollback в этом пути не найден. flush before destination open (0x40FC70), close before path/properties validation (0x40F4E0), cross-map entity write (0x4D39A0), map-open failure return (0x4D3460).

**Целевая политика:** проверить target/link/location до commit; если открытие всё же завершается ошибкой, восстановить исходный authoritative actor/map state либо оставить его в детерминированном recovery state. Не превращать порядок оригинала в norm. **Acceptance:** synthetic loader отказывает после validation и на каждом commit stage; ни один кейс не теряет и не дублирует actor, возвращает устойчивый failure receipt и сохраняет возможность продолжить из исходного или объявленного recovery state. **Steamwoven:** Частично: [Area owner](../world/area-loader.md) не заменяет публикацию при неуспешной загрузке; [WORLD003](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy) отклоняет Blocked/Unknown/Unsupported атомарно. Полного transfer transaction/rollback нет.

## Покрытие Steamwoven до 2026-10-20

Таблица покрытия этого раздела перенесена в [матрицу покрытия](coverage/world.md): там у каждого требования текущий статус, объём, остаток и документация (2026-10-20). Абзацы ниже описывали прежнюю таблицу этого документа (исходный аудит 2026-10-09 с поздними правками) и сохранены как история; слова «таблица», «строки» и «столбцы» в них относятся к ней.

Статусы показывают только покрытие на закреплённом срезе. `Нет` означает отсутствие компонента в проверенной ветке, а не опровержение поведения оригинальной игры. `Частично` ограничено пояснением в ячейке. Существующие bounded-тесты засчитываются только для указанного локального контракта. Единственный выполненный переход карт — Crash Site ↔ пещера в area viewer (ARC-WORLD-009); прочие переходы и original-data association не запускались. Чтение функций оригинала само по себе не считается runtime-проверкой.

Проверки Data coordinates/neighbor selection и bounded movement относятся к разным слоям. Area fixtures/load подтверждают multisector metadata, а WORLD003 проверки — движение/отказ только внутри x4..8/y0..3. Cross-sector actor, connectors и map-transfer rollback не проверены. Новых runtime запусков в аудите нет; исторические candidate-specific receipts — в [каталоге](../project/status.md#verification-that-can-be-credited).

## Source inventory и непроверенные ветки

**Оригинал — непосредственно прочитаны указанные функции** (а не вся группа): карта (0x40EA90–0x412C90: map save/load, open, sector prefetch, jump-point dispatch, flush, selected diff/dynamic load, map-list metadata; адреса приведены в требованиях), сектор (0x4CEF70–0x4D31C0: init/cache, coordinates/bounds, selected load/save, diff path), location (0x4B8680, location projection), объект (0x444270, actor-location caller), телепорт (0x4D32D0–0x4D3F30: queue/process, map branch, recursive transfer, effects), jump points (0x4E2FB0–0x4E3890: exact lookup, selected read/write), окно карты мира (0x55F8D0–0x566D10: selected map positioning/state handoff only). Все функциональные утверждения привязаны к адресам функций рядом с ними.

**Steamwoven — напрямую прочитаны** checkout-root `AGENTS.md`, `docs/architecture/decisions.md`, `docs/research/method.md`, `docs/project/status.md`, `docs/formats/maps.md`, `docs/world/grid-movement-runtime.md`, `docs/research/cardinal-traversal-evidence.md`. Catalog-status snapshot itself dates through PR #30; repo HEAD also includes PR #35/#36 documents and bounded implementation. В тестах движения непосредственно прочитаны только `GeometryTests.cs` 19–64 и `ValidationTests.cs` 58–91; production movement source был найден по символам/пути, но его тело здесь не аудировалось.

**Идентифицированы, но тела/полные caller paths не исследовались:** функции областей (0x4CA940–0x4CB6A0), полный travel/route/time-event path окна карты мира (0x55F8D0–0x566D10), teleport op callers в исполнении сценариев (0x4446E0–0x44C820; сами действия телепорта сверены с exe, строки [TR1, TR2](../research/ledger/world.md#map-transitions-arc-data-025)), alternate teleport callers в AI (0x4A8320–0x4AFC70), очередь событий (0x45A7C0–0x45CC10), lifecycle библиотеки игры (0x4020F0–0x405070) и инициализации игры (0x4B9B90–0x4B9DB0), plus sector component implementations в функциях секторных списков и соседних групп (0x4F1150–0x4F83D0), клетках (0x4D6840–0x4D7E90) и terrain (0x4E7B00–0x4E9680). Не читались оригинальные map-list/jump-point/cave entries или другие extracted game data. Поэтому требования не определяют concrete cave/interior association, full overland route policy или all sector component semantics.

**Проверки:** source/document review only. Не выполнялись game launch, data extraction, runtime smoke checks или acceptance scenarios; они предложены как будущие target checks.

## Замечания по прочитанным функциям и границы надёжности

Найденные в исследованной цепочке замечания не становятся целевым контрактом:

- Сохранение карты (0x40EE50): по чтению (F0) ошибка module save прекращает цикл, но функция всё равно возвращает успех; это открытый риск достоверности сохранения.
- Открытие карты в игре (0x40FC70): по чтению (F0) после ошибки проверки объектной системы (0x405280) запрашивается quit, но выполнение продолжается.
- Функция 0x40FED0 пустая; назначение неизвестно, поэтому из её наличия нельзя выводить world behavior.
- Сохранение сектора (0x4D2750): по чтению (F0) при ошибке открытия diff вызывается `fclose(NULL)`, а cross-map serializer (0x4D39A0) при ошибке записи не закрывает файл. Это подтверждает необходимость отдельной fail-safe policy.

## Открытые вопросы и что именно подтверждено

1. Не проверялись оригинальные данные, связывающие конкретные cave/interior entrance и exit с map ID, source cell и destination cell. Требование FR-WORLD-006 задаёт способность модели, а не ответ про каждую оригинальную пещеру.
2. В exe сверены только строки [TR1–TR5](../research/ledger/world.md#map-transitions-arc-data-025), [SS1–SS8](../research/ledger/world.md#main-map-streaming-arc-data-030) и [LC1](../research/ledger/data.md#limits-census-arc-data-029) журнала; остальное — чтение функций (F0). Наличие функций или аргументов не подтверждает фактический runtime path на оригинальных данных.
3. Фактическая original-data precedence между base map, PATCH, loose files и sector diffs остаётся за рамками этой выборки. Исследование выбранного Steamwoven сектора прямо не универсализирует format/precedence.
4. Не разрешены map-specific worldmap/area associations и travel-time/encounter policy для всего каталога.
5. Точные failure/rollback semantics оригинала недостаточно надёжны и не должны копироваться без отдельного решения.

Исследование было read-only: игра не запускалась, данные/bytes/assets не извлекались для отчёта и не включались в него. [Steamwoven research method](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/research/method.md#L1-L5).
