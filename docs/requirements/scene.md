# Требования к статической сцене и входу в здания

Дата: 2026-10-02. Steamwoven baseline: `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (`docs/functional-requirements-20261002`). Сверяемый источник CE: [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd). Рабочие деревья baseline и CE были чистыми на момент исследования.

Область: поверхность сцены, статические предметы и деревья, стены/здания, двери-порталы, окна, крыши, их рисование и затрагивающие их правила перемещения. Условия боя, инвентарь, NPC/AI и карта переходов/`jumppoint` не специфицируются. Физический проход через дверь не означает, что здание является отдельной картой: если данные задают переход карты, он относится к отдельному контракту переходов.

Термины evidence: **CE подтверждает** означает только наличие указанной ветви/функции в закреплённом исходном коде CE. Это одна линия происхождения, а не проверка оригинального исполняемого файла. **Вывод/политика продукта** отмечает требуемое поведение, выведенное для проекта, но не измеренное в оригинальном runtime. Для всех перечисленных требований оригинальная визуальная и игровая паритетность остаётся **не подтверждена**. Этапы `prealpha-core` и `later` — предлагаемые группировки требований, не очередь задач и не оценка готовности.

## Требования

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

**Steamwoven сейчас.** В выбранном Crash Site загружены 4 096 записей поверхности и проведена перепись 317 v119 статических записей. Ресурсный этап сцены разрешает ровно пять записей и явно относит 312 к `OutsideResourceScope`; это отдельный результат от агрегата проходимости всех 317 записей. Wall/portal/roof scene path отсутствует. См. [каталог компонентов](../project/status.md), [loader](../world/static-object-loader.md) и [static obstruction evidence](../research/static-obstruction-evidence.md).

<a id="fr-scene-002"></a>

### FR-SCENE-002 — Каждый статический объект получает проверяемую цепочку до кадра (`prealpha-core`)

**Поведение и приёмка.** Для каждого обычного статического объекта в выбранной области сцена должна связать instance с current ART, mapping, resource identity, группой/кадром и palette. Ошибка или неоднозначность должна быть видимой диагностикой с исходным индексом, а не fallback-картинкой. Приёмка для сектора со всеми его статическими объектами показывает, какая доля цепочек разрешена, и не объявляет сцену полной при непокрытых записях.

**CE и данные.** `object_draw` читает `OBJ_F_CURRENT_AID`, затем подготавливает ART blit через `object_setup_blit` ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L495-L576), [`object_setup_blit`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4665-L4704)); загрузчик сектора помещает объекты в его списки ([`objlist_load`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1425-L1444)). Требуются instance/prototype current ART, выбранный mapping и resource archive с валидными group/frame/palette.

**Steamwoven сейчас.** `StaticObjectLoader` разрешает census/default поля для выбранных записей и отдельно ограничивает resource resolution индексами 0–4; `StaticSceneryScenePlan.FromAuthenticatedInspection` требует ровно эти пять разрешённых ресурсов. Это полезная диагностическая грань, но не полнота сцены. [Загрузчик](../world/static-object-loader.md#api), [план сцены](../../tools/terrain-viewer/Core/StaticSceneryScenePlan.cs#L40-L60).

<a id="fr-scene-003"></a>

### FR-SCENE-003 — Деревья и крупная растительность не теряются из-за ограниченного профиля (`prealpha-core`)

**Поведение и приёмка.** Если сектор содержит деревья или другую растительность как статические записи, каждая запись должна проходить тот же полный путь разрешения ресурса и видимого размещения, что мебель и малые предметы. Приёмка включает реальный выбранный объект растительности только после подтверждения его идентичности в локальных данных; результат должен показать ствол/крону соответствующими ART-кадрами либо назвать конкретный нерешённый ресурс. Крона, нависающая над несколькими клетками, не должна автоматически становиться такой же ширины физическим препятствием.

**CE и данные.** CE рисует типизированные записи сектора через общий `object_draw` и вычисляет кадр/прямоугольник из instance и ART ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L552-L576), [`sub_443620`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4811-L4858)). Вывод о том, что растительность должна покрываться общим статическим pipeline и что видимая крона не задаёт collision footprint, — **политика продукта**; отдельная классификация деревьев и её runtime-поведение здесь не подтверждены.

**Steamwoven сейчас.** Зафиксировано пять resource chains и пять диагностических размещений, но документы не утверждают, что выбранная пятёрка покрывает класс деревьев. Остальные 312 записей имеют явный `OutsideResourceScope`, то есть результат вне ресурсной области, а не разрешённую ART-цепочку. [Ограничение выбранного ресурсного набора](../world/static-object-loader.md#api), [пять размещений](../rendering/static-scenery-contract.md#required-finite-input-record).

<a id="fr-scene-004"></a>

### FR-SCENE-004 — Статические изображения привязаны к координате и hotspot кадра (`prealpha-core`)

**Поведение и приёмка.** Положение кадра должно учитывать координату instance, прямые instance offsets и hotspot/размер выбранного кадра; ART offsets должны оставаться отдельными данными и применяться только по доказанному правилу. Приёмка использует синтетические крупный и малый спрайты с разными hotspot и offsets: одинаковая клетка с разными instance offsets даёт разные anchors, а отсутствующий обязательный offset даёт явный отказ.

**CE и данные.** В `object_draw` экранная проекция location дополняется `OBJ_F_OFFSET_X/Y` и `+40,+20`; `sub_443620` получает frame hotspot/размер и строит rectangle ([координатная база](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L570-L576), [геометрия кадра](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4811-L4858)). Эти функции вызываются из пути рисования объекта. Розничная интерпретация всех форматов/типов остаётся непроверенной.

**Steamwoven сейчас.** Для пяти отобранных записей зафиксирована формула проекции + direct instance offset − hotspot, с ART offset, сохранённым отдельно; synthetic fixtures покрывают знаковые offsets и hotspot. Это не размещение оставшихся 312 объектов. [Контракт размещения](../rendering/static-scenery-contract.md#diagnostic-coordinate-and-rectangle-rule), [реализация](../../tools/terrain-viewer/Core/StaticSceneryScenePlan.cs#L63-L90).

<a id="fr-scene-005"></a>

### FR-SCENE-005 — Поверхность остаётся под объектами в доказанном слое (`prealpha-core`)

**Поведение и приёмка.** Базовая земля и её визуальные перепады должны оставаться под объектами; их footprint и screen projection не зависят от камеры и размеров viewport. Приёмка на синтетической клеточной площадке проверяет стык клетки, фасадного края и крупного статического спрайта при pan/zoom без изменения мировых координат.

**CE и данные.** В игровом draw pipeline `tile_draw` вызывается до `object_draw` (`gamelib_draw_game`, [порядок](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). `facade_draw` появляется в editor draw path, а не в показанном normal-game вызове; следовательно, точное normal-runtime поведение фасада из этого call site не подтверждается.

**Steamwoven сейчас.** Terrain loader и диагностический viewer покрывают 4 096 tile/facade records выбранного сектора; planner задаёт диагностические слои Tile и Facade. Это не измерение normal-runtime фасадного слоя. [План поверхности](../../tools/terrain-viewer/Core/TerrainScenePlan.cs#L34-L74), [статус компонента](../project/status.md).

<a id="fr-scene-006"></a>

### FR-SCENE-006 — Наложение использует согласованный depth order (`prealpha-core`)

**Поведение и приёмка.** Поверхность, статические предметы, actor, стены и крыши должны рисоваться в согласованном порядке так, чтобы крупный предмет мог перекрывать и быть перекрытым соседней геометрией в соответствии с его якорем/слоем. Приёмка использует синтетическую воспроизводимую сцену с двумя перекрывающимися деревьями, actor и стеной; это не утверждение о наличии деревьев среди пяти подтверждённых original-data объектов; результат сохраняет порядок при повторном кадре и перемещении камеры.

**CE и данные.** `object_draw` распределяет blits по underlay/flat/shadow/non-flat/overlay order ranges, а `object_flush_pending_blits` сортирует очередь; roof рисуется после объекта ([группы объектов](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L525-L530), [enqueue/sort/flush](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4755-L4808), [общий draw pipeline](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). CE-комментарии также указывают на артефакты dirty rectangles для scaled sprites ([`object.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L880-L896)).

**Steamwoven сейчас.** Диагностический static sort — `(anchorY, anchorX, sourceIndex)`; canvas отдельно рисует поверхность, пять статических команд и foreground actor. Это проектная воспроизводимая политика, не восстановленный оригинальный z-order; walls/roofs отсутствуют. [План](../../tools/terrain-viewer/Core/StaticSceneryScenePlan.cs#L22-L30), [canvas](../../tools/terrain-viewer/Godot/TerrainCanvas.cs#L120-L179), [ограничение](../rendering/static-scenery-contract.md#ordering-and-overlap-policy).

<a id="fr-scene-007"></a>

### FR-SCENE-007 — Стены и части здания образуют связную геометрию (`prealpha-core`)

**Поведение и приёмка.** Соседние стеновые сегменты должны собираться в правильные края, углы, стыки и проёмы согласно их ориентации и piece data; одиночная смена zoom не должна превращать стену в независимые разрывы. Приёмка покрывает прямые стены, угол, соединение и дверь/окно в каждом поддерживаемом направлении, а неподдерживаемая комбинация получает явный статус.

**CE и данные.** В `wall.c` геометрия соединений меняет p-piece по соседним объектам, но помечает вычисление вращений `TODO: Check rotations`; несколько веток `wall_delete`/редактора имеют `TODO: Incomplete`. Кроме того, `wall_draw` вызывается только для top-down view, тогда как обычная игровая сцена вызывает `object_draw` ([wall piece/rotation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L290-L385), [явные TODO](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c#L86-L109), [игровой draw](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1702-L1715)). Понадобятся wall object fields, current wall ART selector, rotation и соседние wall instances.

**Steamwoven сейчас.** Нет wall-instance loader, building wall scene plan или проверок связности. Статический объект loader ограничен обычной scenery-подсистемой и не заменяет wall/portal records. [Static loader boundary](../world/static-object-loader.md#scope).

<a id="fr-scene-008"></a>

### FR-SCENE-008 — Дверь/портал рисуется по своим данным и состоянию (`prealpha-core`)

**Поведение и приёмка.** Дверь должна визуально занимать соответствующий проём, показывать выбранное состояние/кадр и быть связана с тем же физическим объектом, которое проверяет перемещение. Приёмка переключает закрытую и открытую синтетические двери и убеждается, что видимый кадр относится к текущему состоянию портала, а не к scenery fallback.

**CE и данные.** `portal_is_open` извлекает состояние из ART frame; `portal_toggle` использует open/close анимации ([`portal.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L88-L117)). `object_draw` рисует текущий ART объекта в общем игровом проходе ([`object_draw`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L702-L817)). Данные включают отдельные portal fields, current ART/frame и координату проёма.

**Steamwoven сейчас.** Отдельные portal records и state-aware draw pipeline не загружаются; пять ART resource chains относятся к конечному static-scene диагностическому профилю. Паритет двери не заявлен.

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

**Ограничение persistence.** Roof DIF API присутствует, но load/save тела пусты и `is_modified` возвращает false ([CE stubs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L87-L123)). Piece-topology rendering не доказывает поддержку изменённой крыши в DIF/save; исходное поведение остаётся неизвестным.

<a id="fr-scene-011"></a>

### FR-SCENE-011 — Крыша скрывается/тускнеет над внутренней областью, не теряя hit/occlusion контекст (`prealpha-core`)

**Поведение и приёмка.** Когда actor оказывается под покрывающей крышей, сцена показывает внутренность настолько, чтобы actor и релевантные объекты оставались видимы и выбираемы; после выхода крыша возвращает исходное состояние. Приёмка проходит actor через границу roof coverage в обе стороны, проверяя исчезновение/fade без просвета сквозь соседние внешние сегменты и без исчезновения внутренней стены/двери.

**CE и данные.** `roof_is_covered_loc` учитывает tile type, roof piece, fill и fade; `roof_draw` применяет piece-specific alpha, а `object_draw` проверяет roof coverage/fade перед выводом объектов ([coverage](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c#L658-L709), [объекты и roof checks](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L552-L571)). `wallcheck.c` включает fade при первом входе в tracked roof location и снимает при выходе ([`wallcheck_roof_faded_set/clear`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L998-L1015), [clear](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c#L1108-L1124)). Розничное условие и точный зрительный результат не проверялись.

**Steamwoven сейчас.** Нет roof coverage, fade, click-through или связки roof state с actor; существующий renderer рисует только finite diagnostic scene. Ни roof, ни occlusion не тестировались на оригинальном runtime.

<a id="fr-scene-012"></a>

### FR-SCENE-012 — Визуальная площадь не подменяет физический blocker (`prealpha-core`)

**Поведение и приёмка.** Геометрия спрайта, клеточная принадлежность и вклад в движение должны оставаться отдельными величинами. Большая крона может накрывать соседние пиксели, а scenery blocker должен определяться признанным object/terrain/wall/portal правилом. Приёмка проверяет отдельно картинку, destination cell и направленный шаг; `NoBlock` одного компонента не превращается в общий `Open`.

**CE и данные.** CE разносит визуальный frame rectangle (`object.c`) и traversal: `tile_is_blocking` отдельно использует block mask и tile-art свойства, а `object_calc_traversal_cost_func` обрабатывает scenery, wall и portal ветви ([tile blocking](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L166-L190), [object traversal](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3029)). Нужны tile masks/names/facade, direct/prototype object flags, destination/edge context, wall/portal state и actor capability.

**Steamwoven сейчас.** Проверяются отдельно terrain obstruction components и 317-записный ordinary-static destination-cell component. Последний знает 58 Blocker / 259 NoBlock для одного фиксированного контекста, но не общий `IsWalkable`, стену, портал, footprint или route. Обобщённая occupancy/step оценка остаётся Unknown; отдельно ARC-RES-014 зафиксировал четыре gap-free directed rows — 3 Open и 1 Blocked — для одного аутентифицированного исследования и контекста. Это не общий production permission API и не интеграция с игровой сценой. [Static evidence](../research/static-obstruction-evidence.md#fixed-profile-aggregate-run), [общая граница passability](../world/passability-contract.md#composition-rule), [узкие cardinal rows](../research/cardinal-traversal-evidence.md#measured-directed-component-table).

<a id="fr-scene-013"></a>

### FR-SCENE-013 — Сценические препятствия подключены к оценке направленного шага (`prealpha-core`)

**Поведение и приёмка.** Отображаемые terrain, scenery и wall/portal объекты передают признанные данные об препятствиях потребителю направленного шага. При одинаковом world state видимое препятствие и assessment относятся к той же сущности/грани; камера и sprite bounds не меняют verdict. Приёмка проверяет эту связь на синтетических blocker/non-blocker и стене, включая изменённое состояние. Полный маршрут, обход и исполнение шагов принадлежат FR-NAV-002/004; это требование к scene-to-navigation интеграции, а не второй pathfinding контракт.

**CE и данные.** `path_make` проверяет blocking tile и находящиеся в целевой клетке scenery/critters; анимационный step caller отдельно проверяет `tile_is_blocking` и `object_traversal_is_blocked` ([path check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L89-L114), [step check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4748)). Это source trace; некоторые CE branches помечены неполными и полный алгоритм движения/навигации здесь не специфицирован.

**Steamwoven сейчас.** `MovementRuntime` моделирует шаги и timing на переданных assessments, но у текущего WORLD diagnostic указано `collision not evaluated`; загрузчик не подключает blocker components к перемещению. Поэтому движение вокруг сцены пока отсутствует как интегрированное игровое поведение. [World policy](../../tools/terrain-viewer/Core/DiagnosticWorldPolicy.cs#L3-L14), [runtime](../../src/Arcanum.Simulation/Movement/MovementRuntime.cs#L15-L40), [каталог](../project/status.md).

<a id="fr-scene-014"></a>

### FR-SCENE-014 — Открытие и закрытие двери согласованы с проходимостью (`prealpha-core`)

**Поведение и приёмка.** У обычного доступного портала закрытое состояние препятствует шагу; разрешённое действие открывает дверь, состояние/кадр меняется, и следующий шаг через проём учитывает это состояние. Закрытие снова меняет traversal. Блокировка, для которой нет разрешённого открытия, сохраняет blocker-статус. Приёмка проверяет закрытый проход, открытие, проход через проём и повторное закрытие отдельно от визуальной анимации.

**CE и данные.** `portal_is_open` и `portal_toggle` реализуют state/frame transition; wall/portal traversal пропускает открытую дверь и проверяет возможность попытки открыть закрытую ([portal state](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L88-L117), [traversal caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3112)). Required inputs: portal flags/state, current ART frame, orientation, actor capability, and directed step.

**Steamwoven сейчас.** Нет portal records, open/close animation, capability-aware door action или passability integration. Статический `Blocker/NoBlock` компонент явно исключает порталы и не может отвечать на этот шаг. [Static contract](../world/static-obstruction-contract.md#explicit-exclusions).

<a id="fr-scene-015"></a>

### FR-SCENE-015 — Проход в здание не требует неявного перехода карты (`prealpha-core`)

**Поведение и приёмка.** После разрешённого открытия физической двери actor может шагнуть из внешней клетки во внутреннюю сторону стенового проёма в рамках текущей загруженной сцены. Roof coverage/fade и видимость обновляются по внутреннему положению. Смена карты происходит только при наличии отдельного явно разрешённого перехода в данных; сама геометрия здания не создаёт `jumppoint`.

**CE и данные.** CE связывает portal state с traversal на соседнем направленном шаге ([`object_calc_traversal_cost_func`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3112), [анимационный step check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4748)). Эта локальная сцепка не доказывает, что конкретный интерьер находится на той же карте; карту и её отдельный переход должны определять map/sector data.

**Steamwoven сейчас.** Нет стен, дверей, внутренней сцены, roof visibility или actor collision integration; текущий WORLD диагностический режим ограничен одним сектором 64×64 и прямым перемещением. Map/jumppoint transitions здесь не покрыты.

<a id="fr-scene-016"></a>

### FR-SCENE-016 — Пересечение окна — отдельный вариант стенового traversal (`later`)

**Поведение и приёмка.** Если проект поддерживает проход через окно, он должен отличаться от обычного прохода через дверь и зависеть от явно объявленных состояния окна и возможностей actor. Обычный actor не проходит через стену только потому, что её текстура прозрачна. Приёмка сравнивает одинаковый оконный edge при разных actor capabilities и отдельно проверяет обычный doorway.

**CE и данные.** CE проверяет ограниченный набор wall p-pieces и выставляет `is_window`; traversal flags/actor capability меняют исход ([окно в wall ветви](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3041-L3093), [caller flags](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4750-L4764)). Это не задаёт визуальное alpha-правило, разрушаемость или точную retail-политику.

**Steamwoven сейчас.** Window/wall records и этот traversal branch отсутствуют; включать их в ordinary-static passability запрещает граница текущего компонента. Поддержка и семантика остаются открытыми до более полного оригинального наблюдения.

## Ключевые разрывы покрытия

- Три независимых измерения статической сцены нельзя складывать в одно число: 317 записей в census, пять разрешённых ART resource chains (плюс 312 явных `OutsideResourceScope`) и 317 вкладов ordinary-static obstruction отвечают на разные вопросы. Обычная obstruction исследована только как destination-cell component в одном фиксированном context; ARC-RES-014 добавляет четыре точных направленных cardinal результата, но это не полнота рендера и не общий production movement verdict.
- Статический viewer сейчас рисует конечную пятёрку с собственной диагностической сортировкой. Не доказано, что среди неё есть дерево; нет scene coverage остальных статических ресурсов, wall/portal/window/roof decoder и renderer, нормальной глубины/occlusion или collision integration.
- CE даёт конкретные реализации для нескольких путей, но содержит `TODO: Check rotations` и `TODO: Incomplete` в wall helper/editor коде. В осмотренных roof draw/coverage функциях TODO/FIXME не обнаружен. `object_draw` отдельно помечает один индексный расчёт как `TODO: Unclear` и документирует scaled-sprite dirty-rectangle артефакт. Это открытые вопросы одного CE source lineage; только оригинальный runtime может подтвердить совместимость.

## Сводное покрытие на Steamwoven baseline

Статусы относятся только к закреплённому Steamwoven source snapshot. Проверка исходных данных, synthetic fixtures и просмотр кода не равны проверке оригинального runtime. Под «интеграцией» понимается подключение к сцене/симуляции, а не наличие отдельного диагностического API.

| ID | Данные | Правила | Интеграция | Проверка |
| --- | --- | --- | --- | --- |
| FR-SCENE-001 | Частично: 4,096 tile и 317 v119 static records одного сектора | Частично: отдельные census/resource/obstruction результаты | Частично: поверхность и конечные 5 scenery records | Частично: data probe и synthetic/unit scope; без runtime |
| FR-SCENE-002 | Частично: 5 ART chains; прочие 312 `OutsideResourceScope` | Частично: selected fields/defaults | Частично: renderer принимает ровно 5 records | Частично: limited records и synthetic tests |
| FR-SCENE-003 | Нет: класс дерева не установлен | Нет: tree-specific coverage | Нет: tree path не доказан | Нет |
| FR-SCENE-004 | Частично: ART metadata/offsets для выбранных 5 | Частично: diagnostic anchor formula | Частично: 5 scene placements | Частично: synthetic hotspot/offset fixtures |
| FR-SCENE-005 | Частично: выбранные tile/facade данные | Частично: bounded surface/facade layer policy | Частично: diagnostic terrain и scenery | Частично: historical component/viewer checks |
| FR-SCENE-006 | Нет: полный набор depth records не покрыт | Частично: `(anchorY, anchorX, sourceIndex)` — diagnostic policy | Частично: finite static scene, без wall/roof | Частично: bounded ordering tests |
| FR-SCENE-007 | Нет: wall/building data model | Нет: wall-piece connection rules | Нет: wall scene path | Нет |
| FR-SCENE-008 | Нет: portal state/resources | Нет: physical door traversal | Нет: state-aware portal rendering | Нет |
| FR-SCENE-009 | Нет: window/wall records | Нет: window traversal | Нет: wall/window integration | Нет |
| FR-SCENE-010 | Частично: roof-section framing/discriminator only | Нет: piece topology rules | Нет: roof renderer | Нет: no roof scenario |
| FR-SCENE-011 | Частично: roof section boundary only | Нет: coverage/fade/hit-state integration | Нет: actor-roof integration | Нет |
| FR-SCENE-012 | Частично: terrain components and 317 static flag records | Частично: one static destination-cell context; separate terrain components | Нет: no general movement admission | Частично: aggregate plus four directed cardinal rows; not universal passability |
| FR-SCENE-013 | Частично: bounded terrain/static assessments | Нет: scene-to-navigation adapter отсутствует | Нет: no scene collision integration | Частично: только component/headless tests, не проверка adapter или original traversal |
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
