# Независимая проверка требований по закреплённым исходникам

Проверка выполнена 2026-10-03; она охватывает только четыре замороженных отчёта ниже и не является полным аудитом игры. Проверенные pins: CE a7ff41b300ef712f0e7d088183a3d08957110cdd, Steamwoven b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae.

## Точность проверяемых снимков

| Файл | SHA-256 |
| --- | --- |
| docs/requirements/content-product.md | 67edf843b8a0a5136238c30ef854ece2dff82318a28bc1101e3ab16dfe342f7f |
| docs/requirements/world.md | 6f579562bd29b3899d6d935670240b62fccc365f7d7b04aa428dae327d2017d7 |
| docs/requirements/characters-skills.md | 133c55c7c684a4a44c664639ae0a2fb414b3b491101dc841ad48123fd789bbf2 |
| docs/requirements/story-social-ai.md | e9d8a33a38f4cb8b85d138a5b97526ebc291d7b53273889dd58cca6947c94b74 |

Тексты прочитаны целиком. Сверил каждый FR с его формулировкой, источниками и соответствующей строкой таблицы Steamwoven: всего 78 FR и 78 строк покрытия. Проверил 330 закреплённых GitHub-ссылок CE/Steamwoven: указанный файл существует в соответствующем pin-checkout, а диапазон не выходит за EOF. Это проверка путей и диапазонов, не полной семантики всех ссылок; приёмочные сценарии, сборки, игра, оригинальные данные и внешняя доступность ссылок не проверялись.

## Findings для исправления

1. **FR-CONTENT-003, content-product.md:15-17: слишком широкое «начальный actor/map binding не прослежен».** Ссылка только на gameinit_mod_load не показывает reset caller. В CE gameinit_reset выбирает карту типа shopping с fallback на start, вызывает map_open_in_game, затем player_create; загрузка adventure metadata вызывает reset. [CE gameinit reset/module-load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gameinit.c#L59-L123). Уточнить наблюдаемый bootstrap и оставить неизвестным конкретный выбранный кампанией map/actor/prototype result и оригинальный runtime binding. Это не доказывает конечную карту Crash Site или фактическое появление конкретного PC в оригинале.

2. **FR-PRODUCT-001, content-product.md:41-43: конкретные настройки сформулированы как базовое требование без явной границы принятого направления.** ADR/vision принимают modern resolution, HiDPI, scalable UI и независимость viewport от simulation, но не фиксируют конкретные экраны/опции выбора resolution/window mode/UI scale или QHD test matrix. [ADR](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/architecture/decisions.md#L10-L14), [vision](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/vision.md#L7-L11). Сохранить принятый baseline и пометить выбор параметров и точную физическую матрицу как proposal/acceptance choice.

3. **FR-PRODUCT-003, content-product.md:49-51,85: пометка о zoom в текущем pin неточна.** В Steamwoven tools/terrain-viewer/Godot/TerrainViewerWorld.cs уже вызывает ZoomWorld на wheel-up/down и клавишах +/-; в той же ветви нет LT/RT и 100%-preset. [Текущая diagnostic input ветвь](https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/tools/terrain-viewer/Godot/TerrainViewerWorld.cs#L224-L243). По доверенному контексту владельца LT — zoom out и RT — zoom in являются принятым будущим направлением; этот review не оспаривает их авторизацию. Переформулировать coverage как «ограниченный diagnostic wheel/+/- zoom присутствует; gamepad triggers, точные 100%/comfort presets и production camera path не подтверждены». Exact presets остаются предложением, если отдельно не приняты владельцем.

4. **FR-CHAR-014, characters-skills.md:68-70: график очков описан как применяемый ко всем level-ups, но указанная CE функция — только часть ветви.** calculate_bonus_character_points выдаёт одно очко за шаг уровня плюс за каждый пятый, однако level_recalc при cur_level == max_level входит в отдельную ветвь, которая лишь ограничивает XP и не устанавливает уровень и не выдаёт очки; schedule вызывается только в else. [CE level-up terminal branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L432-L534). Уточнить предел наблюдения и пометить терминальный CE путь как аномалию/открытый вопрос; не выводить из одного helper универсальную политику максимального уровня.

5. **FR-SOCIAL-004, story-social-ai.md:60-62: не показан второй путь ввода приказов спутникам.** Рассмотренные ссылки покрывают follower menu и broadcast parser, но CE также открывает текстовое broadcast-поле по Enter; submit передаёт текст и позицию под курсором в broadcast_msg, где prefix/команда разрешается в follower orders. [Enter caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L2551-L2559), [text input open/submit](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/broadcast_ui.c#L66-L111), [command interpreter](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/broadcast.c#L184-L370). Добавить bounded subcase/citations для typed order input либо cross-reference к UI/media requirement; отдельно оставить фразы/синонимы и original resource mapping неизвестными. Это не означает, что сетевой чат входит в продуктовую цель.

6. **FR-SCRIPT/DIALOG source inventory, story-social-ai.md:134-136: после дополнительного чтения обновить классификацию script_name.c.** Ранее файл перечислен как нечитанный. Прочитанный код индексирует module numbers 1–999 и system numbers ≥1000 и строит DLG path из script number; комментарии CE сами подозревают неверное разделение и описывают неожиданные группы файлов. [system index/comment](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L38-L84), [module index/comment](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L102-L181), [DLG path builder](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L189-L207). Перенести диапазон в прямо прочитанный inventory и назвать подозрение именно заметкой автора CE, а не восстановленной политикой оригинала. Неизвестность исходных SCR/DLG associations оставить.

## Per-ID просмотр постановки, coverage и traceability

Статус «ограниченно прослежен» означает, что формулировка/граница, ссылка и заявленная строка покрытия согласованы на уровне этого чтения. Это не утверждение, что каждый алгоритм или ветвь CE аудированы. Finding выше имеет приоритет над этой сводкой.

| FR | Review |
| --- | --- |
| FR-CONTENT-001 | Ограниченно прослежен: caller-selected sources и отсутствие общего resolver явно разведены. |
| FR-CONTENT-002 | Ограниченно прослежен: нет общей precedence policy; caveat про patch/loose порядок не упрощает lookup. |
| FR-CONTENT-003 | Finding 1: нужно включить наблюдаемый initial reset path. |
| FR-CONTENT-004 | Ограниченно прослежен: выбранные static records не выдаются за общий mutable registry. |
| FR-CONTENT-005 | Ограниченно прослежен: bounded readers не равняются поддержке каждого формата/версии. |
| FR-CONTENT-006 | Ограниченно прослежен: encoding/localization оставлены неизвестными. |
| FR-CONTENT-007 | Ограниченно прослежен: разделение RNG/render явно названо policy, не CE finding. |
| FR-CONTENT-008 | Ограниченно прослежен: mod support заявляется по профилю, не по наличию DAT reader. |
| FR-PRODUCT-001 | Finding 2: разделить принятый display baseline и конкретные settings/test proposals. |
| FR-PRODUCT-002 | Ограниченно прослежен: high-level controller direction привязана к accepted vision; production UI/device не засчитаны. |
| FR-PRODUCT-003 | Finding 3: скорректировать bounded zoom coverage, сохранив принятые LT/RT goals. |
| FR-PRODUCT-004 | Ограниченно прослежен: принятая общая sim/switching direction отделена от отсутствующей реализации. |
| FR-PRODUCT-005 | Ограниченно прослежен: support платформ назван будущей валидацией. |
| FR-PRODUCT-006 | Ограниченно прослежен: original-reference vs intentional-fix — policy. |
| FR-PRODUCT-007 | Ограниченно прослежен: поздние визуальные идеи помечены как кандидаты, не legacy факты. |
| FR-WORLD-001 | Ограниченно прослежен: метаданные каталога отделены от разрешения источников; Steamwoven runtime отсутствует. |
| FR-WORLD-002 | Ограниченно прослежен: sector codec/bounds отделены от screen projection и collision. |
| FR-WORLD-003 | Ограниченно прослежен: cache/refcount/prefetch — целевая политика; CE LRU-like eviction не нормируется как продукт. |
| FR-WORLD-004 | Ограниченно прослежен: cross-sector continuity — целевой контракт, не доказанная текущей сеткой интеграция. |
| FR-WORLD-005 | Ограниченно прослежен: .sec/.dif aggregate сформулирован bounded; полный source resolver исключён. |
| FR-WORLD-006 | Ограниченно прослежен: требования к модели topology не приписывают конкретным пещерам cross-map связь. |
| FR-WORLD-007 | Ограниченно прослежен: forward/reverse endpoints разделены; обратность конкретных исходных ссылок неизвестна. |
| FR-WORLD-008 | Ограниченно прослежен: код подтверждает map dispatch по location; экранная независимость — тестируемая цель. |
| FR-WORLD-009 | Ограниченно прослежен: lifecycle описан как цепочка CE, но прямо не нормируется CE порядок. |
| FR-WORLD-010 | Ограниченно прослежен: CE сериализатор не заявлен гарантией stable ID; identity — целевой контракт. |
| FR-WORLD-011 | Ограниченно прослежен: landing/prefetch bounded; камера и готовность кадра остаются target policy. |
| FR-WORLD-012 | Ограниченно прослежен: точный return требует authored reverse endpoint; auto-stack исключён. |
| FR-WORLD-013 | Ограниченно прослежен: map-local persistence отделена от campaign save; ошибки CE отдельно отмечены. |
| FR-WORLD-014 | Ограниченно прослежен: overland handoff — later; route/encounter/time policy явно за границей. |
| FR-WORLD-015 | Ограниченно прослежен: effect flags/orchestration отделены от реализации media/time domains. |
| FR-WORLD-016 | Ограниченно прослежен: fail-safe rollback — предложенная политика, CE failure path не выдан за безопасный. |
| FR-CHAR-001 | Ограниченно прослежен: новый PC отделён от предсозданных героев; Steamwoven покрытие отсутствует. |
| FR-CHAR-002 | Ограниченно прослежен: placeholder rejection в CE ограничена; правила пробелов/нормализации неизвестны. |
| FR-CHAR-003 | Ограниченно прослежен: допустимость расы/пола привязана к таблице CE, не к извлечённым данным. |
| FR-CHAR-004 | Ограниченно прослежен: фильтр portrait IDs не превращён в утверждение о составе каталога. |
| FR-CHAR-005 | Ограниченно прослежен: эффект/ID/сброс разделены от нечитанных текстов и значений эффектов. |
| FR-CHAR-006 | Ограниченно прослежен: выбранные контекстные branches отмечены; полный набор эффектов неизвестен. |
| FR-CHAR-007 | Ограниченно прослежен: item/background call order поддерживает bounded стартовый набор; данные предметов не перечислены. |
| FR-CHAR-008 | Ограниченно прослежен: базовые и effective getters различены; общая потребительская интеграция не заявлена реализованной. |
| FR-CHAR-009 | Ограниченно прослежен: причины gender/race modifiers показаны отдельно от исходных таблиц. |
| FR-CHAR-010 | Ограниченно прослежен: clamp/max код и профиль совместимости разведены. |
| FR-CHAR-011 | Ограниченно прослежен: производные примеры привязаны к getter; не претендуют на полный formula audit. |
| FR-CHAR-012 | Ограниченно прослежен: отображение пяти resistance rows отделено от формул/урона. |
| FR-CHAR-013 | Ограниченно прослежен: XP thresholds/award/cap связаны, исходные таблицы и все award callers открытыми остаются. |
| FR-CHAR-014 | Finding 4: schedule helper не покрывает terminal max-level branch. |
| FR-CHAR-015 | Ограниченно прослежен: бюджет, snapshot и prerequisites названы; не все spell/tech branches читались. |
| FR-CHAR-016 | Ограниченно прослежен: skill rank/cost/cap sources указаны; полный rank formula audit не заявлен. |
| FR-CHAR-017 | Ограниченно прослежен: техническое развитие отделено от действий взлома/ловушек/оружия. |
| FR-CHAR-018 | Ограниченно прослежен: переходы training tier и Teacher effect приведены; UI setter anomaly раскрыта. |
| FR-CHAR-019 | Ограниченно прослежен: skill check — общий вход, исход предметного действия вынесен; gambling stake link необходим отдельно. |
| FR-CHAR-020 | Ограниченно прослежен: derived aptitude и неизвестные источники spell points/sector adjustment обозначены. |
| FR-CHAR-021 | Ограниченно прослежен: auto-level schema — отдельный поздний домен и не all-category mechanic. |
| FR-CHAR-022 | Ограниченно прослежен: effect cause/order показаны; длительность всех типов не заявлена. |
| FR-CHAR-023 | Ограниченно прослежен: blessing/curse records связаны с эффектами; expiry не выводится из stored time. |
| FR-CHAR-024 | Ограниченно прослежен: deferred fate path ограничен; источники и полный набор событий неизвестны. |
| FR-CHAR-025 | Ограниченно прослежен: carry weight/encumbrance cross-link к предметам и точные штрафы остаются вне этого ID. |
| FR-CHAR-026 | Ограниченно прослежен: death predicate/gates показаны; revive/combat state вынесены. |
| FR-DIALOG-001 | Ограниченно прослежен: CE start/UI path есть; конкретные NPC→SCR→DLG associations неизвестны. |
| FR-DIALOG-002 | Ограниченно прослежен: явно названы выборочные условия и не полный opcode/data audit. |
| FR-DIALOG-003 | Ограниченно прослежен: record/variant/UI пути разделены от нечитанных текстов, озвучки и локализаций. |
| FR-DIALOG-004 | Ограниченно прослежен: последствия перехода не выданы за полный контракт target domains; азартная игра cross-referenced к FR-ITEM-019. |
| FR-SCRIPT-001 | Ограниченно прослежен: объект/tile/sector dispatch boundaries и отсутствие data associations разведены; см. Finding 6. |
| FR-SCRIPT-002 | Ограниченно прослежен: 53 conditions/142 actions названы catalogue, не совместимостью всех ветвей. |
| FR-SCRIPT-003 | Ограниченно прослежен: timers/story state/persistence связаны без гарантий восстановления. |
| FR-SCRIPT-004 | Ограниченно прослежен: quest state, XP/status and journal разделены; конкретные quest records неизвестны. |
| FR-SOCIAL-001 | Ограниченно прослежен: reaction formula/profile unknown вынесен из CE path. |
| FR-SOCIAL-002 | Ограниченно прослежен: reputation/origin/faction state не утверждает конкретные значения исходных данных. |
| FR-SOCIAL-003 | Ограниченно прослежен: recruitment checks/follower links отделены от NPC exceptions in data. |
| FR-SOCIAL-004 | Finding 5: дополнить typed broadcast UI path или связать с UI/media requirement. |
| FR-SOCIAL-005 | Ограниченно прослежен: wait/unwait/loyalty branches отделены от порогов и data profiles. |
| FR-SOCIAL-006 | Ограниченно прослежен: co-op условен и не объявлен продуктовой поддержкой по одному CE коду. |
| FR-SOCIAL-007 | Ограниченно прослежен: knowledge/refutation/UI state разделены от исходных фраз и associations. |
| FR-SOCIAL-008 | Ограниченно прослежен: игровые повествовательные вкладки указаны; combat kill/injury tabs явно вынесены. |
| FR-SOCIAL-009 | Ограниченно прослежен: queue order/time hook описаны отдельно от газетного текста и чтения UI. |
| FR-AI-001 | Ограниченно прослежен: heartbeat/schedule route — CE code evidence, не runtime на конкретных NPC данных. |
| FR-AI-002 | Ограниченно прослежен: source claim ограничен target selection/hand-off; исход combat вынесен. |
| FR-AI-003 | Ограниченно прослежен: NPC portal access отделён от topology и concrete map links. |
| FR-AI-004 | Ограниченно прослежен: generator lifecycle и random placement не универсализированы на каждый authored generator. |

## Дополнительная source-проверка breadth и пробелы

Дополнительно непосредственно прочитаны в CE: gameinit.c:59-123; map.c:850-872,1002-1018; sector.c:860-975; jumppoint.c:392-405; teleport.c:95-138,164-276,280-365,381-450, teleport.h:7-31; stat.c:336-526,540-657,692-798,891-960; charedit_ui.c:1700-1783, resistance.c:1-10; item.c:207-240, background.c:336-380, level.c:432-560, critter.c:1832-1869, skill.c:771-958,1518-1617; script_name.c:38-207, sector_script.c:40-68, mt_ai.c:1-220, logbook.c:1-187, broadcast_ui.c:66-111, intgame.c:2551-2559, broadcast.c:178-370,453-483,499-507, follower_ui.c:690-718,948-968, ai.c:288-375,1903-2043,3725-3790. Дополнительно в Steamwoven прочитаны TerrainViewerWorld.cs:224-243, decisions.md:1-20, vision.md:9-17. В более раннем content проходе дополнительно перепроверены gamelib.c:568-644,662-731,1820-1903, TIG file.c:573-649,1729-1803, database.c:92-135, obj.c:1233-1252,2485-2535, obj_file.c:43-64, mes.c:101-138,516-544 и random.c:17-107. Прочтение каждого перечисленного файла целиком не подразумевается.

- logbook.c:18-96,103-145 содержит CE code для kill/injury статистики. Story/social FR-SOCIAL-008 правильно исключает боевые вкладки; cross-domain inventory должен подтвердить их владельца COMBAT. При этом комментарий injury writer противоречит проверке типов: ветка возвращается для critter attacker, хотя комментарий обещает запись атак от critters. Не считать это рабочим injury behavior; CE anomaly, не оригинальная политика. [CE ветка injury writer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L103-L145).
- mt_ai.c:41-52,171-220 строит magic/tech AI action lists. В этом limited pass он выступает границей combat/magic-tech, а не пропущенной player progression mechanic.
- sector_script.c:40-68 даёт get/set состояния записи сектора через sector lock. Это support path для script dispatch, не самостоятельная механика игрока.
- broadcast_ui.c добавляет пользовательский typed-entry path для приказов, описанный Finding 5. Исходные команды/фразы и сетевые сообщения не исследовались.
- script_name.c раскрывает source-side numeric SCR/DLG filename resolution и возможную ошибку классификации; это не доказывает карту оригинальных module/system associations.
- В world.md overland route, encounter, weather и travel-time policy прямо оставлены за границей FR-WORLD-014; их наличие в этой выборке требований не подтверждено. Это остаточный catalogue gap/unknown, если его не закроют отдельные WORLD/TIME/AI требования.
- В STORY отчёте порядок/условия ограничены выборочно прочитанными ветвями; каталог 53 SCR conditions и 142 actions не является полным compatibility proof. Оригинальные DLG/SCR/map/prototype entries, оригинальный исполняемый файл и поведение не исследовались.
- Политика точных resolution/window presets и полного набора доступности контроллера остаётся вопросом product acceptance. Принятые современные display/controller directions и принятые визуальные переключения не понижаются в findings из-за неполноты source code.
- Source review подтверждает только атрибутированные участки кода CE и состояния Steamwoven в закреплённом commit. Никакие acceptance сценарии, сборки, запуски игры, извлечение оригинальных ресурсов или runtime parity checks не выполнялись.
