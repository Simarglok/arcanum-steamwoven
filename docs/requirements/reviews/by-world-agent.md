# Независимая перекрёстная проверка: мир и перемещение

Дата проверки: 2026-10-03. Исходники CE закреплены на `a7ff41b300ef712f0e7d088183a3d08957110cdd`; Steamwoven — на `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. Проверялись конкретные исходные тела и выбранные caller paths, а не только имена функций. Игра не запускалась, сборка и проверка оригинального runtime не выполнялись; оригинальные игровые данные не исследовались. Выводы ниже относятся только к указанным версиям отчётов.

| Отчёт | SHA-256 на момент проверки |
| --- | --- |
| [`scene.md`](../scene.md) | `93744e6bdf4e9856b9a152e31f21bf90f71211c4739fe4fde224de480c92c7d5` |
| [`combat-magic-tech.md`](../combat-magic-tech.md) | `55ae4acf9896b45da958a1039ded77b3d1b9bbdaa89d567ba57870d6290a249b` |
| [`world-travel-runtime.md`](../world-travel-runtime.md) | `8386969a637a4fc711e0014c7ed06cc8d664c70102d89dac41fd38fc2963d467` |

## Подтверждённые границы

- Сцена корректно разделяет census из 317 статических записей, пять разрешённых ART-цепочек и 312 записей вне ресурсной области. Эти числа не складываются в полноту сцены или проходимости. Четыре направленных cardinal-наблюдения обозначены как локальные результаты одного контекста, а не общее правило. Это согласуется с описанием [FR-SCENE-001/002/012](../scene.md) и зафиксированной границей в [инвентаре исходников](../source-coverage.md).
- Ограничение `path_make` в восемь rotations — размер одного возвращаемого сегмента: caller записывает `max/curr`, а потребитель использует сегмент. Повторный поиск пути до дальней click-цели в просмотренной цепочке не установлен. [FR-NAV-004](../world-travel-runtime.md) аккуратно не объявляет восемь клеток общей дальностью.
- Физическое пересечение дверного/оконного проёма отделено от перехода карты; отчёты не утверждают, что всякое здание или пещера является отдельной картой. В частности, [FR-WORLD-006](../world.md) оставляет связь конкретного cave entrance с map/cell неизвестной до проверки выбранных оригинальных данных. Это правильное ограничение, а не пробел, который следует заполнять выводом из CE.
- Fast turn в CE имеет несколько потребителей: в просмотренных ветвях он меняет поворот и число шагов, обрабатываемых за ход. [FR-COMBAT-001](../combat-magic-tech.md) не сводит это к «быстрой анимации» и не распространяет наблюдение на все действия.
- [FR-NAV-007](../world-travel-runtime.md) связывает lockpick caller, outcome, сложность/tool bonus и таймер замка, одновременно оставляя повторную попытку после jam и точные последствия провала открытыми. Показанные в отчёте ветви CE не подтверждают оригинальный runtime.

## Замечания для исправления

### RV-WA-001 — Развести ответственность общей навигации и сцены

**Приоритет: P2.** [FR-SCENE-013](../scene.md) требует общего поиска пути вокруг terrain, scenery и стен, а также маршрута к двери и остановки у стены. Это пересекается с [FR-NAV-002](../world-travel-runtime.md), где уже находятся оценка шага, проходимость и совпадение path result с фактическим шагом. Оставить в scene требование, что scene blockers вносят корректный вклад в directed edge/occupancy; полный маршрут и исполнение шагов закрепить за NAV. Это устранит две точки приёмки для одного поведения.

CE trace разделяет `path_make` (`path.c:63–138`) и проверку фактического шага `sub_425760` (`anim.c:4737–4748`), которая обращается к tile и object traversal. [path source](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L63-L138), [step caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4748). Это поддерживает разделение, но само по себе не задаёт продуктовую навигацию.

### RV-WA-002 — Добавить последствия скользкой и sinkable-клетки

**Приоритет: P2.** В [FR-NAV-002](../world-travel-runtime.md) «применимые штрафы» и приёмка перечисляют двери, стены, окна, занятые клетки и terrain, но не два наблюдаемых последствия движения. В `anim.c` проверка `tile_is_slippery(new_loc)` при провале random/Dexterity останавливает текущий путь и запускает knockdown (`11523–11549`). При изменении местоположения `sub_43E770` вызывает `sub_444270`, где по `tile_is_sinkable(loc)` устанавливается/снимается `OF_WADING` (`object.c:2224–2295`, `5194–5251`). Кроме того, `path.c` имеет условные route costs для fire и изменения освещённости (`462–495`); их включённость зависит от path flags, поэтому следует проверить их как отдельные варианты приёмки, не превращая в безусловный шаговой штраф. [Скользкий шаг](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11523-L11549), [перемещение и обновление состояния](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2224-L2295), [wading и location callbacks](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5194-L5251), [conditional path costs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L462-L495).

Добавить bounded acceptance cases: slippery step может прервать маршрут и вызвать падение согласно явно принятой политике; вход на sinkable-клетку включает соответствующее состояние актёра, выход снимает его. Это CE-source evidence; точный retail-порог, шансы и последствия ещё требуют проверки. Связь `OF_WADING` с движением/статами персонажа следует ссылать на CHAR-domain, не копировать сюда.

### RV-WA-003 — Учесть журнал убийств и историю травм

**Приоритет: P2.** В [combat report](../combat-magic-tech.md) нет отдельного player-facing требования о вкладке Kills & Injuries. Это самостоятельное состояние и представление, не покрываемое только [FR-COMBAT-011](../combat-magic-tech.md) про AP/режим/очередь; story/social report явно не владеет этой вкладкой. Рекомендуемая точка — новый `FR-COMBAT-013`: группировать счётчик/сводки убийств и журнал травм с привязкой к источнику и отображением активного/исцелённого состояния; acceptance должен проверить UI и состояние независимо от combat HUD.

CE прямо обновляет kill counters/notable-enemy fields в `logbook_add_kill` и вызывает её для PC killer либо PC leader из `critter.c` (`logbook.c:18–96`, `critter.c:684–720`). UI читает сводки и injury history, отмечая ещё активную травму (`logbook_ui.c:1274–1319`, форматирование `1609–1683`). [Обновление статистики](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L18-L96), [caller при смерти NPC](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L684-L720), [вкладка/UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1274-L1319), [форматирование записей](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1609-L1683).

Важное ограничение: комментарий в `logbook_add_injury` говорит о травмах от critters, но тело сразу возвращается, когда `obj_type_is_critter(type)` истинно; затем расположены ветви self/PC attribution, которые этот guard не допускает (`logbook.c:103–145`). `combat.c` вызывает функцию для ряда критических травм (`1598–1671`), но это противоречие не позволяет утверждать, что CE корректно сохраняет исходную injury attribution. [Тело с guard](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L103-L145), [вызывающий combat path](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1598-L1671). Требование должно пометить injury-recording поведение CE как противоречивое/неподтверждённое; проверять это по оригинальному runtime нельзя подменять чтением комментария.

### RV-WA-004 — Сузить дублирующие требования об окнах

**Приоритет: P2.** [FR-SCENE-009 и FR-SCENE-016](../scene.md) оба включают оконный wall piece, разделение с дверью, actor capability и тест двух возможностей actor на одном проёме. Уточнить 009 как сценическое представление окна/стенового контекста и правило «прозрачность не означает проходимость»; оставить опциональное пересечение окна и capability-dependent edge только в 016. CE подтверждает ограниченную классификацию wall p-pieces как window и traversal flags, но не общее pixel/alpha-правило, retail-поддержку или все типы окна (`object.c:3041–3093`, `anim.c:4737–4764`). [CE wall/window branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3041-L3093), [actor traversal flags](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4764).

### RV-WA-005 — Исправить имя функции в scene evidence inventory

**Приоритет: P3.** В `scene.md` FR-SCENE-013 и source inventory называют `path_create`; в CE функция называется `path_make` (`path.c:63`), а `PathCreateInfo` — имя аргумента/типа, не функции. Заменить символ в требовании и перечне фактически просмотренных ветвей; диапазон `path.c:89–114` остаётся валидным. [Объявление/тело `path_make`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L63-L114).

### RV-WA-006 — Отделить roof DIF API от работающей поддержки изменений

**Приоритет: P3.** `sector.c` вызывает roof DIF branch, но `sector_roof_list_load_with_dif` и `save_with_dif` пусты, а `sector_roof_list_is_modified` всегда возвращает false (`sector_roof_list.c:87–123`). Поэтому наличие вызова из загрузчика не подтверждает применение/сохранение roof changes в DIF. Не смешивать это с утверждением о piece topology в FR-SCENE-010; добавить явную оговорку, если требование или acceptance касается изменённой крыши/сохранения. [CE roof DIF stubs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c#L87-L123). Это ограничение CE, не доказательство отсутствия функции в оригинале.

### RV-WA-007 — Обозначить перекрывающиеся деревья как synthetic fixture

**Приоритет: P3.** Acceptance [FR-SCENE-006](../scene.md) требует два перекрывающихся дерева, тогда как [FR-SCENE-003](../scene.md) верно говорит, что ни один из пяти разрешённых объектов не подтверждён как дерево. Назвать fixture синтетическим; реальные tree/resource records включать только после подтверждения идентичности. Это удерживает тест глубины сортировки от недоказанного утверждения о составе выбранного сектора.

### RV-WA-008 — Ограничить вывод про освещение в проверке AI-зрения

**Приоритет: P3.** В FR-NAV-005 освещение названо одним из возможных факторов обнаружения. Просмотренный `ai_can_see` напрямую проверяет состояние источника, невидимость, направление, Perception, concealment, расстояние и blocker (`ai.c:4005–4074`), но не читает light value. В `stat.c:431–447` dark background получает зависящее от света изменение характеристик, с комментарием, что perception bonus применяется через effects; это косвенная зависимость, не общий AI lighting gate. Сохранить свет как вопрос/политику продукта либо добавить source trace для конкретной perceptual-effect цепочки, но не утверждать общий порог видимости по освещению. [AI visibility check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L4005-L4074), [dark-background stat branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L431-L447).

## Покрытие Steamwoven и дополнительные открытые границы

Четыре оси покрытия в таблицах scene/world/combat соответствуют содержимому просмотренных отчётов: данные, правила, интеграция и ограниченная проверка разделены; наличие кода или исходной ссылки не названо acceptance/runtime test. В scene 16 строк таблицы согласуются с FR-SCENE-001–016. В world-travel-runtime таблица включает 16 FR-NAV/TRAVEL/RUNTIME строк; в указанном baseline явная ограниченная проверка относится к диагностическому движению/внешности/циклу viewer, не к переходам карты или world simulation. В combat/magic таблица охватывает текущие 12 COMBAT и 12 MAGIC требований; если добавить `FR-COMBAT-013`, добавить для него строку с `Нет` по Steamwoven-осям, пока отдельная игровая реализация не появится.

Дополнительные ограничения CE/source reading:

- `wall.c` содержит TODO по rotation и неполные ветви; двери/окна и passage проверены по выбранным функциям, не по всем p-pieces/состояниям. Граница физического шага против `jumppoint` сформулирована корректно.
- `path_make` не доказывает полный маршрут до дальней click-цели, а четыре cardinal-наблюдения не доказывают универсальную passability. `FR-NAV-004` и bounded coverage это сохраняют.
- Trap acceptance покрывает spotting/disarm/trigger, но полный набор trap data/script outcomes остаётся неизвестным. Lockpick jam/retry и точные retail failure последствия — открытые вопросы, уже обозначенные отчётом.
- FR-RUNTIME-004 связывает race/body/gender и equip selector с текущим ART и не обещает полного покрытия каждой комбинации или ресурсов. Steamwoven single-layer monster профиль не считается игровой интеграцией внешности.
- `animfx.c` даёт data-driven FX путь с overlay/effect присоединением; source coverage census не содержит для него доменной range-ссылки. В этой проверке прочитаны только `sub_4CCD20` и части `animfx_add` (`165–181`, `291–420`, `424–469`); полный список FX, parser, lifecycle callers и назначение эффектов по данным не аудированы. Проверить, достаточно ли презентация spell/weapon FX явно распределена между combat/magic requirements; не выводить набор игровых эффектов из существования общего executor.
- Фактическая cave/interior entrance → map/source/destination association не просмотрена и должна оставаться **Unknown** до отдельной проверки выбранных оригинальных данных. `world.md` сам фиксирует это ограничение и не делает cave-map вывода по названию/наличию CE функции.

## Инвентарь фактически просмотренного

Ниже перечислены только диапазоны, прочитанные в этом cross-review, а не полный аудит файлов.

| CE-файлы и прочитанные диапазоны | Назначение |
| --- | --- |
| `src/game/path.c:63–138, 220–292, 337–515` | Имя/тело `path_make`, destination checks, bounded path result и локальные поисковые ветви. |
| `src/game/anim.c:4737–4764, 10425–10449, 11523–11551` | Фактический шаг, flags двери/окна, продвижение пути и последствия slippery-клетки. |
| `src/game/object.c:2224–2295, 2962–3112, 3152–3256, 5194–5251` | Перенос объекта между позициями/секторами, выбранные traversal branches и location callbacks/wading. |
| `src/game/tile.c:166–210` | Tile blocker, facade walkability, sinkable и slippery queries. |
| `src/game/logbook.c:18–96, 103–145`; `src/game/combat.c:1598–1671`; `src/game/critter.c:684–720` | Kill attribution/counters и injury caller/guard consistency. |
| `src/ui/logbook_ui.c:1274–1319, 1609–1683` | Построение вкладки Kills & Injuries, активная/исцелённая injury font и форматирование. |
| `src/game/sector_roof_list.c:87–128` | Modified/DIF stubs. |
| `src/game/ai.c:4005–4132`; `src/game/stat.c:431–447`; `src/game/light.c:1288–1301` | Разделение see/hear checks и ограниченная проверка связи света с Perception. |
| `src/game/facade.c:82–155`; `src/game/gamelib.c:1702–1750`; `src/game/walkmask.c:30–70, 163–203` | Editor facade draw path, draw caller boundary и выбранные walkmask records/flags. |
| `src/game/obj_find.c:122–224`; `src/game/object.c:3304–3340` | Sector spatial-index move/insert/remove и начало object-list query; support detail, не самостоятельное требование. |
| `src/game/terrain.c:198–271`; `src/game/sector_tile_list.c:124–205`; `src/game/sector_block_list.c:1–125`; `src/game/description.c:123–147` | Выбранное terrain/list loading и lookup; инфраструктура данных, не отдельная scene acceptance. |
| `src/game/animfx.c:165–181, 291–420, 424–469`; поисковые вызовы в `anim.c`, `combat.c`, `magictech.c` | Начало FX node, выбранная часть overlay attachment и выявление caller modules; mapping/lifecycle полностью не исследованы. |

Идентифицированы, но в этой проверке не читались полностью: остальной `object_calc_traversal_cost_func` и wall/window state matrix; весь roof/tile section serializer и секторные caller paths; полный `animfx` parser/эффектный каталог; полный AI decision graph, path flags и world route state; оригинальные cave/interior records. Общая census-граница отражена в [`source-coverage.md`](../source-coverage.md): перечисление 140 игровых/UI `.c` файлов не означает полного аудита модулей.

## Область

Это review только для помощи в исправлении и связывании требований сцен/навигации/боёвки. Оно не является полным аудитом всех игровых доменов или проверкой функциональной полноты игры; staged labels в отчётах — предложения авторов, не утверждённый план работ.
