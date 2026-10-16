# Требования к статической сцене и входу в здания

**Покрытие реализации:** аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), через PR #54. Источники исходного CE-исследования и его дата сохранены; актуальный объём — в таблице и Steamwoven-описаниях ниже. [Метод и срез доски](README.md#база-и-происхождение). Новых runtime/GUI/original-data запусков в этом аудите нет.

ARC-RES-017: [whole-exterior traversal research](../world/crash-site-area-traversal-contract.md)
для FR-SCENE-012/013/014/016 сохраняет physical/visual, state и edge frontiers;
Research/Proposed, NOT_READY, без scene collision или door/window runtime.

Дата: 2026-10-02. Исходный Steamwoven research baseline: `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (`docs/functional-requirements-20261002`). Сверяемый источник CE: [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd). Рабочие деревья baseline и CE были чистыми на момент исследования.

Область: поверхность сцены, статические предметы и деревья, стены/здания, двери-порталы, окна, крыши, их рисование и затрагивающие их правила перемещения. Условия боя, инвентарь, NPC/AI и карта переходов/`jumppoint` не специфицируются. Физический проход через дверь не означает, что здание является отдельной картой: если данные задают переход карты, он относится к отдельному контракту переходов.

Термины evidence: **CE подтверждает** означает только наличие указанной ветви/функции в закреплённом исходном коде CE. Это одна линия происхождения, а не проверка оригинального исполняемого файла. **Вывод/политика продукта** отмечает требуемое поведение, выведенное для проекта, но не измеренное в оригинальном runtime. Для всех перечисленных требований оригинальная визуальная и игровая паритетность остаётся **не подтверждена**. Этапы `prealpha-core` и `later` — предлагаемые группировки требований, не очередь задач и не оценка готовности.

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

**CE и данные.** CE загружает крыши и объекты отдельными секциями ([`sector.c`, `sector_roof_list_load` / `objlist_load`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1362-L1371), [обработка сектора](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1545-L1571)); игровой draw pipeline вызывает поверхности, объекты и крыши по отдельным путям ([`gamelib_draw_game`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). Данные: секционные tile/roof/object-записи, тип объекта, прототипы, resource mapping и ART-кадр. Приоритет DIF/PATCH/loose-файлов этим источником не установлен.

**Steamwoven сейчас.** [DATA009](../world/crash-site-area-loader.md) раздельно сохраняет core/context terrain, static/mobile occurrences, dispositions и visual chains: 36,864 core cells, 426 statics, 88 core mobiles. Inventory-insertion refusal не разрешает автоматически рисовать тело. Viewer всё ещё использует один sector/пять statics; full-area renderer в работе, не на audit main. [PR #46](https://github.com/Simarglok/arcanum-steamwoven/pull/46).

<a id="fr-scene-002"></a>

### FR-SCENE-002 — Каждый статический объект получает проверяемую цепочку до кадра (`prealpha-core`)

**Поведение и приёмка.** Для каждого обычного статического объекта в выбранной области сцена должна связать instance с current ART, mapping, resource identity, группой/кадром и palette. Ошибка или неоднозначность должна быть видимой диагностикой с исходным индексом, а не fallback-картинкой. Приёмка для сектора со всеми его статическими объектами показывает, какая доля цепочек разрешена, и не объявляет сцену полной при непокрытых записях.

**CE и данные.** `object_draw` читает `OBJ_F_CURRENT_AID`, затем подготавливает ART blit через `object_setup_blit` ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L495-L576), [`object_setup_blit`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4665-L4704)); загрузчик сектора помещает объекты в его списки ([`objlist_load`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1425-L1444)). Требуются instance/prototype current ART, выбранный mapping и resource archive с валидными group/frame/palette.

**Steamwoven сейчас.** Area API разрешает все 2,444 требуемые selected terrain/object chains объявленного original profile, actual defaults, физическую frame selection и identity-bound leases. Пять-only `StaticSceneryScenePlan` остаётся отдельной frozen регрессией; general original resource precedence не реализован. [API](../world/crash-site-area-loader.md).

<a id="fr-scene-003"></a>

### FR-SCENE-003 — Деревья и крупная растительность не теряются из-за ограниченного профиля (`prealpha-core`)

**Поведение и приёмка.** Если сектор содержит деревья или другую растительность как статические записи, каждая запись должна проходить тот же полный путь разрешения ресурса и видимого размещения, что мебель и малые предметы. Приёмка включает реальный выбранный объект растительности только после подтверждения его идентичности в локальных данных; результат должен показать ствол/крону соответствующими ART-кадрами либо назвать конкретный нерешённый ресурс. Крона, нависающая над несколькими клетками, не должна автоматически становиться такой же ширины физическим препятствием.

**CE и данные.** CE рисует типизированные записи сектора через общий `object_draw` и вычисляет кадр/прямоугольник из instance и ART ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L552-L576), [`sub_443620`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4811-L4858)). Вывод о том, что растительность должна покрываться общим статическим pipeline и что видимая крона не задаёт collision footprint, — **политика продукта**; отдельная классификация деревьев и её runtime-поведение здесь не подтверждены.

**Steamwoven сейчас.** Vegetation/static chains уже входят в полный core Area snapshot и сверяются с independent original inventory. Это data coverage наружной области, без general tree class rule или показа всей растительности в viewer. Остальные static records больше не следует считать неизученными только из-за пяти-only scene API. [Area inventory](../research/crash-site-area-inventory.md), [Data loader](../world/crash-site-area-loader.md).

<a id="fr-scene-004"></a>

### FR-SCENE-004 — Статические изображения привязаны к координате и hotspot кадра (`prealpha-core`)

**Поведение и приёмка.** Положение кадра должно учитывать координату instance, прямые instance offsets и hotspot/размер выбранного кадра; ART offsets должны оставаться отдельными данными и применяться только по доказанному правилу. Приёмка использует синтетические крупный и малый спрайты с разными hotspot и offsets: одинаковая клетка с разными instance offsets даёт разные anchors, а отсутствующий обязательный offset даёт явный отказ.

**CE и данные.** В `object_draw` экранная проекция location дополняется `OBJ_F_OFFSET_X/Y` и `+40,+20`; `sub_443620` получает frame hotspot/размер и строит rectangle ([координатная база](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L570-L576), [геометрия кадра](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4811-L4858)). Эти функции вызываются из пути рисования объекта. Розничная интерпретация всех форматов/типов остаётся непроверенной.

**Steamwoven сейчас.** Area visual descriptors сохраняют signed hotspots/ART/object offsets, dimensions, scale и mirroring. Diagnostic projection/hotspot rule не доказывает общий original placement. Viewer размещает пять statics и HumanFixed, с bounded historical metadata/raster checks. [Area API](../world/crash-site-area-loader.md), [viewer](../rendering/terrain-viewer.md).

<a id="fr-scene-005"></a>

### FR-SCENE-005 — Поверхность остаётся под объектами в доказанном слое (`prealpha-core`)

**Поведение и приёмка.** Базовая земля и её визуальные перепады должны оставаться под объектами; их footprint и screen projection не зависят от камеры и размеров viewport. Приёмка на синтетической клеточной площадке проверяет стык клетки, фасадного края и крупного статического спрайта при pan/zoom без изменения мировых координат.

**CE и данные.** В игровом draw pipeline `tile_draw` вызывается до `object_draw` (`gamelib_draw_game`, [порядок](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). `facade_draw` появляется в editor draw path, а не в показанном normal-game вызове; следовательно, точное normal-runtime поведение фасада из этого call site не подтверждается.

**Steamwoven сейчас.** Area Data покрывает tile/facade выбранных core/context sectors; viewer сохраняет bounded layer policy одного сектора. Full-area rendering, normal runtime facade/visibility и pixel parity не следуют из полного Data snapshot. [Area API](../world/crash-site-area-loader.md).

<a id="fr-scene-006"></a>

### FR-SCENE-006 — Наложение использует согласованный depth order (`prealpha-core`)

**Поведение и приёмка.** Поверхность, статические предметы, actor, стены и крыши должны рисоваться в согласованном порядке так, чтобы крупный предмет мог перекрывать и быть перекрытым соседней геометрией в соответствии с его якорем/слоем. Приёмка использует синтетическую воспроизводимую сцену с двумя перекрывающимися деревьями, actor и стеной; это не утверждение о наличии деревьев среди пяти подтверждённых original-data объектов; результат сохраняет порядок при повторном кадре и перемещении камеры.

**CE и данные.** `object_draw` распределяет blits по underlay/flat/shadow/non-flat/overlay order ranges, а `object_flush_pending_blits` сортирует очередь; roof рисуется после объекта ([группы объектов](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L525-L530), [enqueue/sort/flush](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4755-L4808), [общий draw pipeline](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). CE-комментарии также указывают на артефакты dirty rectangles для scaled sprites ([`object.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L880-L896)).

**Steamwoven сейчас.** Viewer имеет finite diagnostic terrain/facade/statics/actor ordering, а Area выдаёт descriptors для будущего renderer. С ARC-RENDER-011 актёр в `crash-site-area` стоит в центре ромба своей клетки, как все тела (раньше рисовался на 40 px левее), а стены пещеры участвуют в общем upright-порядке и становятся полупрозрачными перед игроком по оригинальной проверке стен. General depth/occlusion, roof и normal original z-order не реализованы. [Viewer policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [якорь актёра и затухание стен](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011), [Data API](../world/crash-site-area-loader.md).

<a id="fr-scene-007"></a>

### FR-SCENE-007 — Стены и части здания образуют связную геометрию (`prealpha-core`)

**Поведение и приёмка.** Соседние стеновые сегменты должны собираться в правильные края, углы, стыки и проёмы согласно их ориентации и piece data; одиночная смена zoom не должна превращать стену в независимые разрывы. Приёмка покрывает прямые стены, угол, соединение и дверь/окно в каждом поддерживаемом направлении, а неподдерживаемая комбинация получает явный статус.

**CE и данные.** В `wall.c` геометрия соединений меняет p-piece по соседним объектам, но помечает вычисление вращений `TODO: Check rotations`; несколько веток `wall_delete`/редактора имеют `TODO: Incomplete`. Кроме того, `wall_draw` вызывается только для top-down view, тогда как обычная игровая сцена вызывает `object_draw` ([wall piece/rotation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L290-L385), [явные TODO](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L86-L109), [игровой draw](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). Понадобятся wall object fields, current wall ART selector, rotation и соседние wall instances.

**Steamwoven сейчас.** Data загружает wall objects пещеры Crash Site и выбирает их wall ART ([cave profile](../world/crash-site-area-loader.md#crash-site-cave-profile)); план сцены ставит стену по hotspot и flip, шаг блокирует край клетки; viewer рисует стены пещеры, а стены перед игроком затухают по оригинальной проверке стен (ARC-RENDER-011, [описание](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011)). Пересчёта соединений кусков (p-piece) и проверок связности нет. Статический объект loader ограничен обычной scenery-подсистемой и не заменяет wall/portal records. [Static loader boundary](../world/static-object-loader.md#scope).

<a id="fr-scene-008"></a>

### FR-SCENE-008 — Дверь/портал рисуется по своим данным и состоянию (`prealpha-core`)

**Поведение и приёмка.** Дверь должна визуально занимать соответствующий проём, показывать выбранное состояние/кадр и быть связана с тем же физическим объектом, которое проверяет перемещение. Приёмка переключает закрытую и открытую синтетические двери и убеждается, что видимый кадр относится к текущему состоянию портала, а не к scenery fallback.

**CE и данные.** `portal_is_open` извлекает состояние из ART frame; `portal_toggle` использует open/close анимации ([`portal.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L88-L117)). `object_draw` рисует текущий ART объекта в общем игровом проходе ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L702-L817)). Данные включают отдельные portal fields, current ART/frame и координату проёма.

**Steamwoven сейчас.** Отдельные portal records и state-aware draw pipeline не загружаются; пять ART resource chains относятся к конечному static-scene диагностическому профилю. Паритет двери не заявлен.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): у первого дома одна дверь (ART номер 55, 7 кадров, открыта при кадре ≠ 0) и четыре окна с одним кадром; поля портала, зеркалирование и путь к ART описаны по CE и данным. Реализации нет.

<a id="fr-scene-009"></a>

### FR-SCENE-009 — Окно отображается в контексте стенового проёма (`later`)

**Поведение и приёмка.** Окно представляет отдельный wall-piece контекст и согласованную видимую геометрию проёма. Прозрачность пикселей не означает физическую проходимость. Приёмка использует синтетический стеновой проём с окном и проверяет его anchor/слой/перекрытие. Capability-dependent пересечение и оценка edge принадлежат FR-SCENE-016 и FR-NAV-003; здесь не повторяется их приёмка.

**CE и данные.** `object_calc_traversal_cost_func` распознаёт подмножество wall p-pieces как окна и сообщает `is_window`; флаги traversal и тип actor влияют на блокировку ([wall/window ветвь](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3041-L3093), [capability flags в caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4764)). Отдельная общая pixel/alpha-модель окна из этих функций не следует. Приёмка здесь — предложенный synthetic fixture; выбранные оригинальные window piece и ART/resource association ещё не установлены. Она не является подтверждённым рендером оригинального окна.

**Steamwoven сейчас.** Нет wall/window data path и actor-capability traversal для окон. Static obstruction contract явно исключает wall/window/portal branches и не трактует неопознанные записи как проходимые. [Границы компонента](../world/static-obstruction-contract.md#explicit-exclusions).

<a id="fr-scene-010"></a>

### FR-SCENE-010 — Крыша читается, рисуется и сохраняет piece topology (`prealpha-core`)

**Поведение и приёмка.** Крыша должна строиться из sector roof data с её piece, fill/fade state и кадрами, а не из статического объекта с похожими пикселями. Приёмка покрывает собранный участок крыши с внутренними и внешними углами, заполненными и незаполненными частями; неизвестная крыша не заменяется вымышленным спрайтом.

**CE и данные.** CE загружает roofs как отдельную секцию сектора, `roof_draw` выбирает записи на 4-клеточной сетке и blit-ит roof ART, включая piece-specific fade alpha ([roof load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1362-L1371), [draw и piece fade](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L216-L366)). В исследованных функциях roof draw/coverage явный TODO/FIXME не найден; это не свидетельство retail parity.

**Steamwoven сейчас.** Для выбранной секции зафиксирован только framing roof section и empty discriminator; нет roof data model, ART-resolution chain, roof renderer или synthetic roof piece matrix. [Static scene scope](../rendering/static-scenery-contract.md#scope-and-status), [loader boundary](../world/static-object-loader.md#scope).

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): крыша первого дома — 9 из 256 слотов (3×3 блока по 4×4 клетки, ART номер 7, все девять видов кусков прямоугольника), кусок = кадр + флаг зеркала; fill/fade в данных сброшены. Реализации нет.

**Ограничение persistence.** Roof DIF API присутствует, но load/save тела пусты и `is_modified` возвращает false ([CE stubs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L87-L123)). Piece-topology rendering не доказывает поддержку изменённой крыши в DIF/save; исходное поведение остаётся неизвестным.

<a id="fr-scene-011"></a>

### FR-SCENE-011 — Крыша скрывается/тускнеет над внутренней областью, не теряя hit/occlusion контекст (`prealpha-core`)

**Поведение и приёмка.** Когда actor оказывается под покрывающей крышей, сцена показывает внутренность настолько, чтобы actor и релевантные объекты оставались видимы и выбираемы; после выхода крыша возвращает исходное состояние. Приёмка проходит actor через границу roof coverage в обе стороны, проверяя исчезновение/fade без просвета сквозь соседние внешние сегменты и без исчезновения внутренней стены/двери.

**CE и данные.** `roof_is_covered_loc` учитывает tile type, roof piece, fill и fade; `roof_draw` применяет piece-specific alpha, а `object_draw` проверяет roof coverage/fade перед выводом объектов ([coverage](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L658-L709), [объекты и roof checks](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L552-L571)). `wallcheck.c` включает fade при первом входе в tracked roof location и снимает при выходе ([`wallcheck_roof_faded_set/clear`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L998-L1015), [clear](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L1108-L1124)). Розничное условие и точный зрительный результат не проверялись.

**Steamwoven сейчас.** Нет roof coverage, fade, click-through или связки roof state с actor; существующий renderer рисует только finite diagnostic scene. Ни roof, ни occlusion не тестировались на оригинальном runtime.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): правило покрытия со сдвигом +3 закрывает 36 из 81 внутренней клетки; на любой внутренней клетке крыша скрывается целиком, первый шаг наружу её возвращает; fade привязан к затуханию стен. Это пересказ CE и расчёт по данным, не runtime.

<a id="fr-scene-012"></a>

### FR-SCENE-012 — Визуальная площадь не подменяет физический blocker (`prealpha-core`)

**Поведение и приёмка.** Геометрия спрайта, клеточная принадлежность и вклад в движение должны оставаться отдельными величинами. Большая крона может накрывать соседние пиксели, а scenery blocker должен определяться признанным object/terrain/wall/portal правилом. Приёмка проверяет отдельно картинку, destination cell и направленный шаг; `NoBlock` одного компонента не превращается в общий `Open`.

**CE и данные.** CE разносит визуальный frame rectangle (`object.c`) и traversal: `tile_is_blocking` отдельно использует block mask и tile-art свойства, а `object_calc_traversal_cost_func` обрабатывает scenery, wall и portal ветви ([tile blocking](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L166-L190), [object traversal](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3029)). Нужны tile masks/names/facade, direct/prototype object flags, destination/edge context, wall/portal state и actor capability.

**Steamwoven сейчас.** Owned [Data assessment](../world/cardinal-traversal-assessment.md) и WORLD003 используют physical contributors, а не sprite extent. В x4..8/y0..3 и isolated context покрыты cardinal и opt-in four-object-leg diagonal assessment. Это bounded production admission, не общий IsWalkable, live world registry или full-area permission. [PR #38](https://github.com/Simarglok/arcanum-steamwoven/pull/38), [PR #44](https://github.com/Simarglok/arcanum-steamwoven/pull/44).

<a id="fr-scene-013"></a>

### FR-SCENE-013 — Сценические препятствия подключены к оценке направленного шага (`prealpha-core`)

**Поведение и приёмка.** Отображаемые terrain, scenery и wall/portal объекты передают признанные данные об препятствиях потребителю направленного шага. При одинаковом world state видимое препятствие и assessment относятся к той же сущности/грани; камера и sprite bounds не меняют verdict. Приёмка проверяет эту связь на синтетических blocker/non-blocker и стене, включая изменённое состояние. Полный маршрут, обход и исполнение шагов принадлежат FR-NAV-002/004; это требование к scene-to-navigation интеграции, а не второй pathfinding контракт.

**CE и данные.** `path_make` проверяет blocking tile и находящиеся в целевой клетке scenery/critters; анимационный step caller отдельно проверяет `tile_is_blocking` и `object_traversal_is_blocked` ([path check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L89-L114), [step check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4748)). Это source trace; некоторые CE branches помечены неполными и полный алгоритм движения/навигации здесь не специфицирован.

**Steamwoven сейчас.** WORLD003 связывает owned Data assessment с exact MovementRuntime и viewer; разрешённый шаг стартует только с matching Supported/Open. Final terrain и четыре role-labelled object legs учитываются при map diagonal; blocked shoulder отказывает. Viewer больше не использует free/collision-not-evaluated integrator. Scope x4..8/y0..3, dynamic doors/registry/HP/full-area traversal не покрыты. [Viewer](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy), [whole-area frontier](../world/crash-site-area-traversal-contract.md).

<a id="fr-scene-014"></a>

### FR-SCENE-014 — Открытие и закрытие двери согласованы с проходимостью (`prealpha-core`)

**Поведение и приёмка.** У обычного доступного портала закрытое состояние препятствует шагу; разрешённое действие открывает дверь, состояние/кадр меняется, и следующий шаг через проём учитывает это состояние. Закрытие снова меняет traversal. Блокировка, для которой нет разрешённого открытия, сохраняет blocker-статус. Приёмка проверяет закрытый проход, открытие, проход через проём и повторное закрытие отдельно от визуальной анимации.

**CE и данные.** `portal_is_open` и `portal_toggle` реализуют state/frame transition; wall/portal traversal пропускает открытую дверь и проверяет возможность попытки открыть закрытую ([portal state](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L88-L117), [traversal caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3112)). Required inputs: portal flags/state, current ART frame, orientation, actor capability, and directed step.

**Steamwoven сейчас.** Нет portal records, open/close animation, capability-aware door action или passability integration. Статический `Blocker/NoBlock` компонент явно исключает порталы и не может отвечать на этот шаг. [Static contract](../world/static-obstruction-contract.md#explicit-exclusions).

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): закрытая дверь блокирует своё ребро; игрок входит днём, ночью дверь заперта (ключа нет, сложность 25), выход открыт всегда; окна заклинены. Реализации нет.

<a id="fr-scene-015"></a>

### FR-SCENE-015 — Проход в здание не требует неявного перехода карты (`prealpha-core`)

**Поведение и приёмка.** После разрешённого открытия физической двери actor может шагнуть из внешней клетки во внутреннюю сторону стенового проёма в рамках текущей загруженной сцены. Roof coverage/fade и видимость обновляются по внутреннему положению. Смена карты происходит только при наличии отдельного явно разрешённого перехода в данных; сама геометрия здания не создаёт `jumppoint`.

**CE и данные.** CE связывает portal state с traversal на соседнем направленном шаге ([`object_calc_traversal_cost_func`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3112), [анимационный step check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4748)). Эта локальная сцепка не доказывает, что конкретный интерьер находится на той же карте; карту и её отдельный переход должны определять map/sector data.

**Steamwoven сейчас.** Нет стен, дверей, внутренней сцены, roof visibility или actor collision integration; текущий WORLD диагностический режим ограничен одним сектором 64×64 и прямым перемещением. Map/jumppoint transitions здесь не покрыты.

**Исследование.** [ARC-RES-029](../research/first-building-evidence.md): интерьер первого дома лежит на той же карте и в том же секторе, jump points и телепортов нет, вход только через ребро двери.

<a id="fr-scene-016"></a>

### FR-SCENE-016 — Пересечение окна — отдельный вариант стенового traversal (`later`)

**Поведение и приёмка.** Если проект поддерживает проход через окно, он должен отличаться от обычного прохода через дверь и зависеть от явно объявленных состояния окна и возможностей actor. Обычный actor не проходит через стену только потому, что её текстура прозрачна. Приёмка сравнивает одинаковый оконный edge при разных actor capabilities и отдельно проверяет обычный doorway.

**CE и данные.** CE проверяет ограниченный набор wall p-pieces и выставляет `is_window`; traversal flags/actor capability меняют исход ([окно в wall ветви](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3041-L3093), [caller flags](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4750-L4764)). Это не задаёт визуальное alpha-правило, разрушаемость или точную retail-политику.

**Steamwoven сейчас.** ARC-WORLD-021: на whole-map evidence кардинальный шаг через оконную часть стены открыт как `WINDOW_JUMP`, если оконный портал и остальное не решают иначе; диагональ блокируется; маршрут платит +50; viewer даёт шагу время и анимацию прыжка (vault). Только для актёра-человека (ПК), способность прыгать других актёров не проверяется; виден в режиме карт (два интерьера с окнами). [Gait runtime](../world/gait-runtime.md#window-jump-arc-world-021).

## Ключевые разрывы покрытия

- Complete exterior Data owner разрешает terrain/static/mobile body chains, но viewer всё ещё рисует finite sector/пять statics. Full-area renderer — отдельная поставка; Data-ready не равно draw/visibility-ready.
- WORLD003 уже подключает bounded original-data step assessment к runtime/actor. Его 20-cell rectangle и isolated context не закрывают whole-exterior registry, HP/state, cross-sector permission или route search.
- Wall/portal/window/roof gameplay и normal original depth/occlusion не реализованы. Отсутствие таких active composite references в выбранном original Area profile не доказывает поддержку их форматов.
- Source TODO/unclear branches и original-runtime parity сохраняются как ограничения исходного исследования; исторические viewer/readback проверки ниже не снимают их.

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

Исследование намеренно не переносит исходный код CE и не сохраняет игровые байты, тексты, изображения или локальные пути. Для подхода к provenance и уровней проверки см. [метод исследования](../research/method.md); принятое направление проекта см. в [архитектурных решениях](../architecture/decisions.md).

## Граница просмотра CE

Ниже перечислены файлы и ветви, реально просмотренные для этого отчёта; ссылки закреплены за указанным commit. Это чтение релевантных фрагментов, не полный аудит каждого файла.

| Просмотренный файл и символы/ветви | Что проверялось |
| --- | --- |
| [`gamelib.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1745), `gamelib_draw_game`, `gamelib_draw_editor` | Порядок вызова tile/object/roof и отличие editor draw path. |
| [`object.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L495-L576), `object_draw`; [`rectangle/order helpers`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4755-L4858), `object_enqueue_blit`, `object_flush_pending_blits`, `sub_443620`; [`traversal`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3112), `object_calc_traversal_cost_func` | Объектный render path, anchor/hotspot/order и начало wall/portal traversal до оконной ветви. |
| [`sector.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1355-L1444), `sector_load`; [`DIF roof load`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1545-L1571) | Раздельная загрузка tile/roof/object секций и roof DIF branch. |
| [`tile.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L116-L128), `tile_draw`; [`tile blocking`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L166-L190), `tile_is_blocking` | Выбор draw path и отдельная terrain/block-mask проверка. |
| [`roof.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L216-L366), `roof_draw`; [`hit/coverage/fade`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L465-L501), [`roof_is_covered_loc`, `roof_is_faded`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L642-L709) | Видимые roof draw/hit/coverage/fade функции; в этих просмотренных фрагментах TODO/FIXME не обнаружен. |
| [`wall.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L86-L109), `wall_draw`, `sub_4E1490`; [`pieces/neighbors`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L290-L435), wall-piece helpers | Editor/top-down drawing boundary, incomplete helpers, соседние pieces и незакрытый rotation вопрос. |
| [`portal.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L19-L117), `portal_bust`, `portal_is_open`, `portal_toggle` | Busted/open frame state и вызовы portal open/close. |
| [`anim.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4764), `sub_425760`, `sub_4257E0`; [`door toggles`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L8321-L8375) | Directed step gate, portal/window capability flags и door animation operations. |
| [`path.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L89-L120), `path_make` | Ранние destination tile/scenery checks в обычном path creation branch. |
| [`wallcheck.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L998-L1015), [`wallcheck_roof_faded_clear`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L1108-L1124) | Refcounted roof fade on/off вызовы. |

Инвентарь включает выбранные тела `sector_roof_list_is_modified`, `sector_roof_list_load_with_dif` и `sector_roof_list_save_with_dif` ([87–123](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L87-L123)); остальной roof section serializer не аудирован.

Неисследованные в этом домене реализации включают [facade renderer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/facade.c), [object sector indexing](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c), [tile section serializer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_tile_list.c), and [`first_party/tig/src/art.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c) for low-level frame/blend behavior. Также не просматривалась оставшаяся часть `object_calc_traversal_cost_func` после `object.c:3112`, включая встречные/дополнительные объектные ветви. Поэтому отчёт не задаёт полный формат этих подсистем, все rotation/window/destroyed-state случаи, ресурсный приоритет или поведение retail runtime.
