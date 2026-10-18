# Требования к статической сцене и входу в здания

**Покрытие реализации:** аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), через PR #54. Источники исходного исследования и его дата сохранены; актуальный объём — в таблице и Steamwoven-описаниях ниже. [Метод и срез доски](README.md#база-и-происхождение). Новых runtime/GUI/original-data запусков в этом аудите нет.

ARC-RES-017: [whole-exterior traversal research](../world/crash-site-area-traversal-contract.md)
для FR-SCENE-012/013/014/016 сохраняет physical/visual, state и edge frontiers;
Research/Proposed, NOT_READY, без scene collision или door/window runtime.

Дата: 2026-10-02. Исходный Steamwoven research baseline: `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (`docs/functional-requirements-20261002`). Поведение оригинала — по функциям исполняемого файла 1.0.7.4 по указанным адресам. Рабочее дерево baseline было чистым на момент исследования.

Область: поверхность сцены, статические предметы и деревья, стены/здания, двери-порталы, окна, крыши, их рисование и затрагивающие их правила перемещения. Условия боя, инвентарь, NPC/AI и карта переходов/`jumppoint` не специфицируются. Физический проход через дверь не означает, что здание является отдельной картой: если данные задают переход карты, он относится к отдельному контракту переходов.

Термины evidence: **Оригинал** — чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в [журнале сверки](../research/original-verification-ledger.md); названные строки журнала проверены в exe. **Вывод/политика продукта** отмечает требуемое поведение, выведенное для проекта, но не измеренное в оригинальном runtime. Для всех перечисленных требований оригинальная визуальная и игровая паритетность остаётся **не подтверждена**. Этапы `prealpha-core` и `later` — предлагаемые группировки требований, не очередь задач и не оценка готовности.

## Требования

**ARC-RES-018 — RESEARCH:** [clock/light контракт](../world/time-lighting-contract.md)
отделяет tile/facade/roof palette paths, emitter tint sources и object/overlay
описания от активного света и submitted pixels. Нулевой stationary count не
означает отсутствия object lights; original masks/coordinate/state binding остаются
NOT_READY. Это не реализация FR-SCENE, lighting renderer или cave transition.

ARC-DATA-009: [complete exterior data owner](../world/crash-site-area-loader.md)
даёт отдельные terrain/sector/object metadata, все 426 core statics и 88 mobile
body references с explicit dispositions, presence/default provenance и
identity-bound group/frame/palette/placement inputs. Это data-layer покрытие
FR-SCENE-001/002, а не submitted draw commands, ordering/visibility, roof/wall
rendering или passability. Active unsupported required composite layers fail
readiness; source-frame decoding не объявляет FR-SCENE-001..009 реализованными.

Позднее исследование ARC-RES-015: [area inventory](../research/crash-site-area-inventory.md) учитывает 426 статических и 88 core mobile records, отдельные resource/placement gaps и cave sources; [area contract](../world/crash-site-area-contract.md) сохраняет flags/order/cache границы. Это не расширяет production five-only profile и не объявляет FR-SCENE-001..009 реализованными.

<a id="fr-scene-001"></a>

### FR-SCENE-001 — Сцена учитывает каждый тип источника отдельно (`prealpha-core`)

**Поведение и приёмка.** Для загруженного сектора сцена должна отдельно учитывать поверхность, facade/перепады поверхности, обычные статические объекты, стены, порталы и крышу. Для каждой записи результат должен быть видимым элементом либо явным статусом «не применимо», «не поддерживается» или «не разрешено»; число записей само по себе не считается числом нарисованных или проверенных на проходимость элементов. Приёмка сверяет независимые количества записей, разрешённых ресурсов, рассчитанных размещений, выведенных команд и вкладов в проходимость; неизвестный ресурс не превращается в пропуск без статуса.

**Оригинал и данные.** Оригинал загружает крыши и объекты отдельными секциями (загрузка секций сектора (0x4D1530, редакторский вариант), игровая загрузка сектора (0x4D1A30; источники и порядок секций сверены с exe, строка [SS5](../research/original-verification-ledger.md#main-map-streaming-arc-data-030))); игровой draw pipeline вызывает поверхности, объекты и крыши по отдельным путям (отрисовка игры (0x4046F0)); крыши рисуются после всех объектов (строка [RF2](../research/original-verification-ledger.md#roofs-arc-render-021)). Данные: секционные tile/roof/object-записи, тип объекта, прототипы, resource mapping и ART-кадр. Приоритет DIF/PATCH/loose-файлов этим чтением не установлен.

**Steamwoven сейчас.** [DATA009](../world/crash-site-area-loader.md) раздельно сохраняет core/context terrain, static/mobile occurrences, dispositions и visual chains: 36,864 core cells, 426 statics, 88 core mobiles. Inventory-insertion refusal не разрешает автоматически рисовать тело. Viewer всё ещё использует один sector/пять statics; full-area renderer в работе, не на audit main. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-scene-002"></a>

### FR-SCENE-002 — Каждый статический объект получает проверяемую цепочку до кадра (`prealpha-core`)

**Поведение и приёмка.** Для каждого обычного статического объекта в выбранной области сцена должна связать instance с current ART, mapping, resource identity, группой/кадром и palette. Ошибка или неоднозначность должна быть видимой диагностикой с исходным индексом, а не fallback-картинкой. Приёмка для сектора со всеми его статическими объектами показывает, какая доля цепочек разрешена, и не объявляет сцену полной при непокрытых записях.

**Оригинал и данные.** Отрисовка объекта (0x43B390) читает `OBJ_F_CURRENT_AID`, затем подготавливает ART blit (0x443170; выбор blend сверен с exe, строка [BL1](../research/original-verification-ledger.md#rendering-body-blends-and-wading-arc-render-018)); загрузчик сектора помещает объекты в его списки (0x4D1A30). Требуются instance/prototype current ART, выбранный mapping и resource archive с валидными group/frame/palette.

**Steamwoven сейчас.** Area API разрешает все 2,444 требуемые selected terrain/object chains объявленного original profile, actual defaults, физическую frame selection и identity-bound leases. Пять-only `StaticSceneryScenePlan` остаётся отдельной frozen регрессией; general original resource precedence не реализован. [API](../world/crash-site-area-loader.md).

<a id="fr-scene-003"></a>

### FR-SCENE-003 — Деревья и крупная растительность не теряются из-за ограниченного профиля (`prealpha-core`)

**Поведение и приёмка.** Если сектор содержит деревья или другую растительность как статические записи, каждая запись должна проходить тот же полный путь разрешения ресурса и видимого размещения, что мебель и малые предметы. Приёмка включает реальный выбранный объект растительности только после подтверждения его идентичности в локальных данных; результат должен показать ствол/крону соответствующими ART-кадрами либо назвать конкретный нерешённый ресурс. Крона, нависающая над несколькими клетками, не должна автоматически становиться такой же ширины физическим препятствием.

**Оригинал и данные.** Оригинал рисует типизированные записи сектора через общую отрисовку объекта и вычисляет кадр/прямоугольник из instance и ART (отрисовка объекта (0x43B390), геометрия кадра (0x443620)). Вывод о том, что растительность должна покрываться общим статическим pipeline и что видимая крона не задаёт collision footprint, — **политика продукта**; отдельная классификация деревьев и её runtime-поведение здесь не подтверждены.

**Steamwoven сейчас.** Vegetation/static chains уже входят в полный core Area snapshot и сверяются с independent original inventory. Это data coverage наружной области, без general tree class rule или показа всей растительности в viewer. Остальные static records больше не следует считать неизученными только из-за пяти-only scene API. [Area inventory](../research/crash-site-area-inventory.md), [Data loader](../world/crash-site-area-loader.md).

<a id="fr-scene-004"></a>

### FR-SCENE-004 — Статические изображения привязаны к координате и hotspot кадра (`prealpha-core`)

**Поведение и приёмка.** Положение кадра должно учитывать координату instance, прямые instance offsets и hotspot/размер выбранного кадра; ART offsets должны оставаться отдельными данными и применяться только по доказанному правилу. Приёмка использует синтетические крупный и малый спрайты с разными hotspot и offsets: одинаковая клетка с разными instance offsets даёт разные anchors, а отсутствующий обязательный offset даёт явный отказ.

**Оригинал и данные.** В отрисовке объекта (0x43B390) экранная проекция location дополняется `OBJ_F_OFFSET_X/Y` и `+40,+20`; функция 0x443620 получает frame hotspot/размер и строит rectangle (координатная база (0x43B390), геометрия кадра (0x443620)). Эти функции вызываются из пути рисования объекта. Розничная интерпретация всех форматов/типов остаётся непроверенной.

**Steamwoven сейчас.** Area visual descriptors сохраняют signed hotspots/ART/object offsets, dimensions, scale и mirroring. Diagnostic projection/hotspot rule не доказывает общий original placement. Viewer размещает пять statics и HumanFixed, с bounded historical metadata/raster checks. [Area API](../world/crash-site-area-loader.md), [viewer](../rendering/terrain-viewer.md).

<a id="fr-scene-005"></a>

### FR-SCENE-005 — Поверхность остаётся под объектами в доказанном слое (`prealpha-core`)

**Поведение и приёмка.** Базовая земля и её визуальные перепады должны оставаться под объектами; их footprint и screen projection не зависят от камеры и размеров viewport. Приёмка на синтетической клеточной площадке проверяет стык клетки, фасадного края и крупного статического спрайта при pan/zoom без изменения мировых координат.

**Оригинал и данные.** В игровом draw pipeline отрисовка клеток (0x4D6950) вызывается до отрисовки объектов (0x43B390) (отрисовка игры, порядок (0x4046F0)). Отрисовка фасадов (0x4C9E70) появляется в editor draw path (0x404740), а не в показанном normal-game вызове; следовательно, точное normal-runtime поведение фасада из этого call site не подтверждается.

**Steamwoven сейчас.** Area Data покрывает tile/facade выбранных core/context sectors; viewer сохраняет bounded layer policy одного сектора. Full-area rendering, normal runtime facade/visibility и pixel parity не следуют из полного Data snapshot. [Area API](../world/crash-site-area-loader.md).

<a id="fr-scene-006"></a>

### FR-SCENE-006 — Наложение использует согласованный depth order (`prealpha-core`)

**Поведение и приёмка.** Поверхность, статические предметы, actor, стены и крыши должны рисоваться в согласованном порядке так, чтобы крупный предмет мог перекрывать и быть перекрытым соседней геометрией в соответствии с его якорем/слоем. Приёмка использует синтетическую воспроизводимую сцену с двумя перекрывающимися деревьями, actor и стеной; это не утверждение о наличии деревьев среди пяти подтверждённых original-data объектов; результат сохраняет порядок при повторном кадре и перемещении камеры.

**Оригинал и данные.** Отрисовка объекта (0x43B390) распределяет blits по underlay/flat/shadow/non-flat/overlay order ranges, а сброс отложенных blits (0x443560) сортирует очередь; roof рисуется после всех объектов (сверено с exe, строка [RF2](../research/original-verification-ledger.md#roofs-arc-render-021)) (группы объектов (0x43B390), enqueue/sort/flush (0x443440, 0x443560, 0x443600), общий draw pipeline (0x4046F0)). Исходный прямоугольник масштабированного спрайта округляется до ближайшего (строка [BL4](../research/original-verification-ledger.md#rendering-body-blends-and-wading-arc-render-018)); возможные артефакты dirty rectangles для scaled sprites в exe не проверялись.

**Steamwoven сейчас.** Viewer имеет finite diagnostic terrain/facade/statics/actor ordering, а Area выдаёт descriptors для будущего renderer. С ARC-RENDER-011 актёр в `crash-site-area` стоит в центре ромба своей клетки, как все тела (раньше рисовался на 40 px левее), а стены пещеры участвуют в общем upright-порядке и становятся полупрозрачными перед игроком по оригинальной проверке стен. General depth/occlusion, roof и normal original z-order не реализованы. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [якорь актёра и затухание стен](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011), [Data API](../world/crash-site-area-loader.md).

<a id="fr-scene-007"></a>

### FR-SCENE-007 — Стены и части здания образуют связную геометрию (`prealpha-core`)

**Поведение и приёмка.** Соседние стеновые сегменты должны собираться в правильные края, углы, стыки и проёмы согласно их ориентации и piece data; одиночная смена zoom не должна превращать стену в независимые разрывы. Приёмка покрывает прямые стены, угол, соединение и дверь/окно в каждом поддерживаемом направлении, а неподдерживаемая комбинация получает явный статус.

**Оригинал и данные.** В функциях стен (0x4DF390–0x4E2C50) геометрия соединений меняет p-piece по соседним объектам (0x4E2C50); вычисление вращений в exe не сверено (F0), редакторские ветви (0x4DF500, 0x4E1490) не дочитаны. Кроме того, отрисовка стены (0x4DF500) вызывается только для top-down view, тогда как обычная игровая сцена вызывает отрисовку объекта (wall piece/rotation (0x4E2C50), игровой draw (0x4046F0)). Понадобятся wall object fields, current wall ART selector, rotation и соседние wall instances.

**Steamwoven сейчас.** Data загружает wall objects пещеры Crash Site и выбирает их wall ART ([cave profile](../world/crash-site-area-loader.md#crash-site-cave-profile)); план сцены ставит стену по hotspot и flip, шаг блокирует край клетки; viewer рисует стены пещеры, а стены перед игроком затухают по оригинальной проверке стен (ARC-RENDER-011, [описание](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011)). Пересчёта соединений кусков (p-piece) и проверок связности нет. Статический объект loader ограничен обычной scenery-подсистемой и не заменяет wall/portal records. [Static loader boundary](../world/static-object-loader.md#scope).

<a id="fr-scene-008"></a>

### FR-SCENE-008 — Дверь/портал рисуется по своим данным и состоянию (`prealpha-core`)

**Поведение и приёмка.** Дверь должна визуально занимать соответствующий проём, показывать выбранное состояние/кадр и быть связана с тем же физическим объектом, которое проверяет перемещение. Приёмка переключает закрытую и открытую синтетические двери и убеждается, что видимый кадр относится к текущему состоянию портала, а не к scenery fallback.

**Оригинал и данные.** Проверка открытости портала (0x4F08C0) извлекает состояние из ART frame; переключение портала (0x4F08F0) использует open/close анимации. Отрисовка объекта (0x43B390) рисует текущий ART объекта в общем игровом проходе. Данные включают отдельные portal fields, current ART/frame и координату проёма.

**Steamwoven сейчас.** Отдельные portal records и state-aware draw pipeline не загружаются; пять ART resource chains относятся к конечному static-scene диагностическому профилю. Паритет двери не заявлен.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): у первого дома одна дверь (ART номер 55, 7 кадров, открыта при кадре ≠ 0) и четыре окна с одним кадром; поля портала, зеркалирование и путь к ART описаны по чтению функций оригинала и данным. Реализации нет.

<a id="fr-scene-009"></a>

### FR-SCENE-009 — Окно отображается в контексте стенового проёма (`later`)

**Поведение и приёмка.** Окно представляет отдельный wall-piece контекст и согласованную видимую геометрию проёма. Прозрачность пикселей не означает физическую проходимость. Приёмка использует синтетический стеновой проём с окном и проверяет его anchor/слой/перекрытие. Capability-dependent пересечение и оценка edge принадлежат FR-SCENE-016 и FR-NAV-003; здесь не повторяется их приёмка.

**Оригинал и данные.** Расчёт стоимости прохода (0x43FE00) распознаёт подмножество wall p-pieces как окна и сообщает признак окна (оконные части и кто может прыгать сверены с exe, строка [W1, W2, W7](../research/original-verification-ledger.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock)); флаги traversal и тип actor влияют на блокировку (wall/window ветвь (0x43FE00), capability flags в caller (0x425760, 0x4257E0)). Отдельная общая pixel/alpha-модель окна из этих функций не следует. Приёмка здесь — предложенный synthetic fixture; выбранные оригинальные window piece и ART/resource association ещё не установлены. Она не является подтверждённым рендером оригинального окна.

**Steamwoven сейчас.** Нет wall/window data path и actor-capability traversal для окон. Static obstruction contract явно исключает wall/window/portal branches и не трактует неопознанные записи как проходимые. [Границы компонента](../world/static-obstruction-contract.md#explicit-exclusions).

<a id="fr-scene-010"></a>

### FR-SCENE-010 — Крыша читается, рисуется и сохраняет piece topology (`prealpha-core`)

**Поведение и приёмка.** Крыша должна строиться из sector roof data с её piece, fill/fade state и кадрами, а не из статического объекта с похожими пикселями. Приёмка покрывает собранный участок крыши с внутренними и внешними углами, заполненными и незаполненными частями; неизвестная крыша не заменяется вымышленным спрайтом.

**Оригинал и данные.** Оригинал загружает roofs как отдельную секцию сектора; отрисовка крыш (0x439140) выбирает записи на 4-клеточной сетке и blit-ит roof ART, включая piece-specific fade alpha (roof load (0x4D1A30), draw и piece fade (0x439140); сетка, отрисовка и fade сверены с exe, строки [RF1, RF2](../research/original-verification-ledger.md#roofs-arc-render-021)).

**Steamwoven сейчас.** Для выбранной секции зафиксирован только framing roof section и empty discriminator; нет roof data model, ART-resolution chain, roof renderer или synthetic roof piece matrix. [Static scene scope](../rendering/static-scenery-contract.md#scope-and-status), [loader boundary](../world/static-object-loader.md#scope).

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): крыша первого дома — 9 из 256 слотов (3×3 блока по 4×4 клетки, ART номер 7, все девять видов кусков прямоугольника), кусок = кадр + флаг зеркала; fill/fade в данных сброшены. Реализации нет.

**Ограничение persistence.** Roof DIF API присутствует, но по чтению функций (F0) тела load/save пусты, а признак изменения возвращает false (0x4F7FC0, 0x4F7FD0, 0x4F7FE0). Piece-topology rendering не доказывает поддержку изменённой крыши в DIF/save; исходное поведение остаётся неизвестным.

<a id="fr-scene-011"></a>

### FR-SCENE-011 — Крыша скрывается/тускнеет над внутренней областью, не теряя hit/occlusion контекст (`prealpha-core`)

**Поведение и приёмка.** Когда actor оказывается под покрывающей крышей, сцена показывает внутренность настолько, чтобы actor и релевантные объекты оставались видимы и выбираемы; после выхода крыша возвращает исходное состояние. Приёмка проходит actor через границу roof coverage в обе стороны, проверяя исчезновение/fade без просвета сквозь соседние внешние сегменты и без исчезновения внутренней стены/двери.

**Оригинал и данные.** Проверка покрытия крышей (0x43A030) учитывает tile type, roof piece, fill и fade (строка [RF4](../research/original-verification-ledger.md#roofs-arc-render-021)); отрисовка крыш (0x439140) применяет piece-specific alpha (RF2), а отрисовка объекта (0x43B390) проверяет roof coverage/fade перед выводом объектов (coverage (0x439FF0, 0x43A030), объекты и roof checks (0x43B390)). Проверка стен включает fade крыши при первом счёте её клетки и снимает при освобождении последнего (счёт (0x438720), clear (0x438A50); строка [RF9](../research/original-verification-ledger.md#roofs-arc-render-021)). Точный зрительный результат не проверялся.

**Steamwoven сейчас.** Нет roof coverage, fade, click-through или связки roof state с actor; существующий renderer рисует только finite diagnostic scene. Ни roof, ни occlusion не тестировались на оригинальном runtime.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): правило покрытия со сдвигом +3 закрывает 36 из 81 внутренней клетки; на любой внутренней клетке крыша скрывается целиком, первый шаг наружу её возвращает; fade привязан к затуханию стен. Это пересказ чтения функций оригинала (правила покрытия и заполнения позже сверены с exe, строки [RF3–RF5](../research/original-verification-ledger.md#roofs-arc-render-021)) и расчёт по данным, не runtime.

<a id="fr-scene-012"></a>

### FR-SCENE-012 — Визуальная площадь не подменяет физический blocker (`prealpha-core`)

**Поведение и приёмка.** Геометрия спрайта, клеточная принадлежность и вклад в движение должны оставаться отдельными величинами. Большая крона может накрывать соседние пиксели, а scenery blocker должен определяться признанным object/terrain/wall/portal правилом. Приёмка проверяет отдельно картинку, destination cell и направленный шаг; `NoBlock` одного компонента не превращается в общий `Open`.

**Оригинал и данные.** Оригинал разносит визуальный frame rectangle (отрисовка объекта, 0x43B390) и traversal: проверка блокировки клетки (0x4D7110) отдельно использует block mask и tile-art свойства, а расчёт стоимости прохода (0x43FE00) обрабатывает scenery, wall и portal ветви (tile blocking (0x4D7110), object traversal (0x43FD70, 0x43FDC0, 0x43FE00)). Нужны tile masks/names/facade, direct/prototype object flags, destination/edge context, wall/portal state и actor capability.

**Steamwoven сейчас.** Owned [Data assessment](../world/cardinal-traversal-assessment.md) и WORLD003 используют physical contributors, а не sprite extent. В x4..8/y0..3 и isolated context покрыты cardinal и opt-in four-object-leg diagonal assessment. Это bounded production admission, не общий IsWalkable, live world registry или full-area permission. [PR #38](https://github.com/Simarglok/arcanum-steamwoven/pull/38), [PR #44](https://github.com/Simarglok/arcanum-steamwoven/pull/44).

<a id="fr-scene-013"></a>

### FR-SCENE-013 — Сценические препятствия подключены к оценке направленного шага (`prealpha-core`)

**Поведение и приёмка.** Отображаемые terrain, scenery и wall/portal объекты передают признанные данные об препятствиях потребителю направленного шага. При одинаковом world state видимое препятствие и assessment относятся к той же сущности/грани; камера и sprite bounds не меняют verdict. Приёмка проверяет эту связь на синтетических blocker/non-blocker и стене, включая изменённое состояние. Полный маршрут, обход и исполнение шагов принадлежат FR-NAV-002/004; это требование к scene-to-navigation интеграции, а не второй pathfinding контракт.

**Оригинал и данные.** Поиск пути (0x41F3C0) проверяет blocking tile и находящиеся в целевой клетке scenery/critters; анимационный step caller отдельно проверяет блокировку клетки (0x4D7110) и блокировку прохода объектами (0x43FD70) (path check (0x41F3C0), step check (0x425760)). Это чтение функций (F0; диагональный шаг из четырёх кардинальных частей сверен с exe, строка [LV3](../research/original-verification-ledger.md#leaving-a-building-through-a-locked-portal-arc-res-049)); полный алгоритм движения/навигации здесь не специфицирован.

**Steamwoven сейчас.** WORLD003 связывает owned Data assessment с exact MovementRuntime и viewer; разрешённый шаг стартует только с matching Supported/Open. Final terrain и четыре role-labelled object legs учитываются при map diagonal; blocked shoulder отказывает. Viewer больше не использует free/collision-not-evaluated integrator. Scope x4..8/y0..3, dynamic doors/registry/HP/full-area traversal не покрыты. [Viewer](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [whole-area frontier](../world/crash-site-area-traversal-contract.md).

<a id="fr-scene-014"></a>

### FR-SCENE-014 — Открытие и закрытие двери согласованы с проходимостью (`prealpha-core`)

**Поведение и приёмка.** У обычного доступного портала закрытое состояние препятствует шагу; разрешённое действие открывает дверь, состояние/кадр меняется, и следующий шаг через проём учитывает это состояние. Закрытие снова меняет traversal. Блокировка, для которой нет разрешённого открытия, сохраняет blocker-статус. Приёмка проверяет закрытый проход, открытие, проход через проём и повторное закрытие отдельно от визуальной анимации.

**Оригинал и данные.** Проверка открытости (0x4F08C0) и переключение портала (0x4F08F0) реализуют state/frame transition; wall/portal traversal пропускает открытую дверь и проверяет возможность попытки открыть закрытую (порядок проверок попытки открытия и выход из здания через запертый портал сверены с exe, строки [LV2–LV5](../research/original-verification-ledger.md#leaving-a-building-through-a-locked-portal-arc-res-049)) (portal state (0x4F08C0, 0x4F08F0), traversal caller (0x43FE00)). Required inputs: portal flags/state, current ART frame, orientation, actor capability, and directed step.

**Steamwoven сейчас.** Нет portal records, open/close animation, capability-aware door action или passability integration. Статический `Blocker/NoBlock` компонент явно исключает порталы и не может отвечать на этот шаг. [Static contract](../world/static-obstruction-contract.md#explicit-exclusions).

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): закрытая дверь блокирует своё ребро; игрок входит днём, ночью дверь заперта (ключа нет, сложность 25), выход открыт всегда; окна заклинены. Реализации нет.

<a id="fr-scene-015"></a>

### FR-SCENE-015 — Проход в здание не требует неявного перехода карты (`prealpha-core`)

**Поведение и приёмка.** После разрешённого открытия физической двери actor может шагнуть из внешней клетки во внутреннюю сторону стенового проёма в рамках текущей загруженной сцены. Roof coverage/fade и видимость обновляются по внутреннему положению. Смена карты происходит только при наличии отдельного явно разрешённого перехода в данных; сама геометрия здания не создаёт `jumppoint`.

**Оригинал и данные.** Оригинал связывает portal state с traversal на соседнем направленном шаге (расчёт стоимости прохода (0x43FE00), анимационный step check (0x425760); сверено с exe, строки [LV3, LV4](../research/original-verification-ledger.md#leaving-a-building-through-a-locked-portal-arc-res-049)). Эта локальная сцепка не доказывает, что конкретный интерьер находится на той же карте; карту и её отдельный переход должны определять map/sector data.

**Steamwoven сейчас.** Нет стен, дверей, внутренней сцены, roof visibility или actor collision integration; текущий WORLD диагностический режим ограничен одним сектором 64×64 и прямым перемещением. Map/jumppoint transitions здесь не покрыты.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): интерьер первого дома лежит на той же карте и в том же секторе, jump points и телепортов нет, вход только через ребро двери.

<a id="fr-scene-016"></a>

### FR-SCENE-016 — Пересечение окна — отдельный вариант стенового traversal (`later`)

**Поведение и приёмка.** Если проект поддерживает проход через окно, он должен отличаться от обычного прохода через дверь и зависеть от явно объявленных состояния окна и возможностей actor. Обычный actor не проходит через стену только потому, что её текстура прозрачна. Приёмка сравнивает одинаковый оконный edge при разных actor capabilities и отдельно проверяет обычный doorway.

**Оригинал и данные.** Оригинал проверяет ограниченный набор wall p-pieces и выставляет признак окна (строка [W1, W2, W7](../research/original-verification-ledger.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock)); traversal flags/actor capability меняют исход (окно в wall ветви (0x43FE00), caller flags (0x4257E0)). Это не задаёт визуальное alpha-правило, разрушаемость или точную retail-политику.

**Steamwoven сейчас.** ARC-WORLD-021: на whole-map evidence кардинальный шаг через оконную часть стены открыт как `WINDOW_JUMP`, если оконный портал и остальное не решают иначе; диагональ блокируется; маршрут платит +50; viewer даёт шагу время и анимацию прыжка (vault). Только для актёра-человека (ПК), способность прыгать других актёров не проверяется; виден в режиме карт (два интерьера с окнами). [Gait runtime](../world/gait-runtime.md#window-jump-arc-world-021).

## Ключевые разрывы покрытия

- Complete exterior Data owner разрешает terrain/static/mobile body chains, но viewer всё ещё рисует finite sector/пять statics. Full-area renderer — отдельная поставка; Data-ready не равно draw/visibility-ready.
- WORLD003 уже подключает bounded original-data step assessment к runtime/actor. Его 20-cell rectangle и isolated context не закрывают whole-exterior registry, HP/state, cross-sector permission или route search.
- Wall/portal/window/roof gameplay и normal original depth/occlusion не реализованы. Отсутствие таких active composite references в выбранном original Area profile не доказывает поддержку их форматов.
- Недочитанные ветви функций и original-runtime parity сохраняются как ограничения исходного исследования; исторические viewer/readback проверки ниже не снимают их.

## Сводное покрытие на Steamwoven baseline

Статусы относятся только к закреплённому Steamwoven source snapshot. Проверка исходных данных, synthetic fixtures и просмотр кода не равны проверке оригинального runtime. Под «интеграцией» понимается подключение к сцене/симуляции, а не наличие отдельного диагностического API.

| ID | Данные | Правила | Интеграция | Проверка |
| --- | --- | --- | --- | --- |
| FR-SCENE-001 | Частично — 36,864 core terrain, 426 statics и 88 mobiles; context отдельно | Частично — occurrence/disposition/visual chains различаются; не live registry | Частично — Area API; viewer сохраняет один сектор и 5 statics | Частично — полный original Data census; full-area rendering не проверен |
| FR-SCENE-002 | Частично — 2,444 selected terrain/object visual chains Area | Частично — actual instance/prototype/resource/frame/default chain и leases | Частично — самостоятельный Area API; viewer по-прежнему 5 records | Частично — каждый selected chain сверяется с independent original manifest |
| FR-SCENE-003 | Частично — vegetation/static body chains входят в core Area census | Частично — source selectors/defaults; нет общего live vegetation path | Частично — Data API; полная растительность в viewer ещё не показана | Частично — original area chain reconciliation, не full-scene visual parity |
| FR-SCENE-004 | Частично — signed offsets/hotspots/scale/mirror для Area visuals | Частично — diagnostic projection; общий placement/visibility не ратифицирован | Частично — пять static placements и fixed Human в viewer | Частично — metadata/original frame checks и historical bounded raster |
| FR-SCENE-005 | Частично — tile/facade данные всех выбранных Area sectors | Частично — bounded terrain/facade layer policy | Частично — diagnostic sector; full-area renderer не смержен | Частично — original Data и historical source/export viewer checks |
| FR-SCENE-006 | Частично — Area occurrence/visual metadata, без общего depth model | Частично — finite diagnostic ordering; актёр в центре ромба; не original z-order/occlusion | Частично — terrain/facade/statics/actor и стены пещеры с затуханием, без roof | Частично — bounded ordering/readback; полная сцена не сравнивалась |
| FR-SCENE-007 | Частично: wall objects и выбор wall ART (пещера) | Частично: стена блокирует край клетки в шаге; оригинальная проверка стен (затухание); соединения кусков нет | Частично: viewer рисует стены пещеры по hotspot и flip, затухание перед игроком | Частично: синтетические и real-data тесты wall ART и проверки стен, нативное наблюдение затухания |
| FR-SCENE-008 | Нет: portal state/resources | Нет: physical door traversal | Нет: state-aware portal rendering | Нет |
| FR-SCENE-009 | Нет: window/wall records | Нет: window traversal | Нет: wall/window integration | Нет |
| FR-SCENE-010 | Частично: roof-section framing/discriminator only | Нет: piece topology rules | Нет: roof renderer | Нет: no roof scenario |
| FR-SCENE-011 | Частично: roof section boundary only | Нет: coverage/fade/hit-state integration | Нет: actor-roof integration | Нет |
| FR-SCENE-012 | Частично — terrain/static census плюс owned directed evidence | Частично — CardinalOnly и opt-in BoundedAdjacent; sprite extent не blocker | Частично — WORLD003 diagnostic admission в x4..8/y0..3 | Частично — original cardinal/shoulder rows и source/export grid cases |
| FR-SCENE-013 | Частично — identity-bound terrain/static directed step evidence | Частично — final terrain и 4 object legs для map diagonal в узком контексте | Частично — Data → MovementRuntime → viewer; нет общего navigation adapter | Частично — historical original bridge и 18 source/export gates; не full-area permission |
| FR-SCENE-014 | Нет: portal state absent | Нет: open/close-to-traversal path | Нет: no door gameplay | Нет |
| FR-SCENE-015 | Нет: building/interior identity unresolved | Нет: no same-map building traversal policy | Нет: no integrated interior scene | Нет |
| FR-SCENE-016 | Нет: window data absent | Нет: window-specific edge rule | Нет: no window interaction | Нет |

Подробные границы и доказательства см. в соответствующих FR выше: [loader](../world/static-object-loader.md), [five-object scene contract](../rendering/static-scenery-contract.md), [static obstruction evidence](../research/static-obstruction-evidence.md), [four directed cardinal observations](../research/cardinal-traversal-evidence.md) и [movement catalogue](../project/status.md). Статус одного компонента не складывается с другим: данные сцены, ресурсная полнота, blocker и полный runtime verdict остаются разными измерениями.

Исследование не сохраняет игровые байты, тексты, изображения или локальные пути. Для подхода к provenance и уровней проверки см. [метод исследования](../research/method.md); принятое направление проекта см. в [архитектурных решениях](../architecture/decisions.md).

## Граница просмотра функций оригинала

Ниже перечислены функции и ветви, реально просмотренные для этого отчёта. Это чтение релевантных фрагментов, не полный аудит каждой группы функций.

| Просмотренные функции и ветви | Что проверялось |
| --- | --- |
| Отрисовка игры (0x4046F0) и редактора (0x404740) | Порядок вызова tile/object/roof и отличие editor draw path. |
| Отрисовка объекта (0x43B390); rectangle/order helpers (0x443440, 0x443560, 0x443600, 0x443620); traversal (0x43FD70, 0x43FDC0, 0x43FE00) | Объектный render path, anchor/hotspot/order и начало wall/portal traversal до оконной ветви. |
| Загрузка секций сектора (0x4D1530, редакторский вариант); игровая загрузка с roof DIF (0x4D1A30) | Раздельная загрузка tile/roof/object секций и roof DIF branch. |
| Отрисовка клеток (0x4D6950); блокировка клетки (0x4D7110) | Выбор draw path и отдельная terrain/block-mask проверка. |
| Отрисовка крыш (0x439140); hit/coverage/fade (0x439890, 0x439FA0, 0x439FF0, 0x43A030) | Видимые roof draw/hit/coverage/fade функции; сверены с exe, строки [RF1–RF10](../research/original-verification-ledger.md#roofs-arc-render-021). |
| Отрисовка стены (0x4DF500), функция 0x4E1490; pieces/neighbors (0x4E2C50), wall-piece helpers | Editor/top-down drawing boundary, недочитанные helpers, соседние pieces и незакрытый rotation вопрос. |
| Порталы: поломка (0x4F06E0), удаление (0x4F0790), проверка открытости (0x4F08C0), переключение (0x4F08F0) | Busted/open frame state и вызовы portal open/close. |
| Анимация: шаг (0x425760), флаги возможностей (0x4257E0); door toggles (0x42B640, 0x42B6F0) | Directed step gate, portal/window capability flags и door animation operations. |
| Поиск пути (0x41F3C0) | Ранние destination tile/scenery checks в обычном path creation branch. |
| Проверка стен: счёт крыши (0x438720), сброс (0x438A50) | Refcounted roof fade on/off вызовы. |

Инвентарь включает функции признака изменения, загрузки и сохранения с разницей списка крыш сектора (0x4F7FC0, 0x4F7FD0, 0x4F7FE0); остальной roof section serializer не аудирован.

Неисследованные в этом домене реализации включают facade renderer, object sector indexing, tile section serializer и функции ART библиотеки TIG для low-level frame/blend behavior. Также не просматривалась оставшаяся часть расчёта стоимости прохода (0x43FE00) после оконной ветви, включая встречные/дополнительные объектные ветви (её части для порталов позже сверены с exe, строка [LV3](../research/original-verification-ledger.md#leaving-a-building-through-a-locked-portal-arc-res-049)). Поэтому отчёт не задаёт полный формат этих подсистем, все rotation/window/destroyed-state случаи, ресурсный приоритет или поведение retail runtime.
