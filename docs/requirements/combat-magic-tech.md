# Требования к бою, магии и технологиям

Дата: 2026-10-02. Steamwoven baseline: `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` (`docs/functional-requirements-20261002`). Сверяемый CE: [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd). Оба рабочих дерева были чистыми на момент исходного среза; последующие незамерженные изменения не входят в оценку. Документ — draft полного каталога для проверки и аппрува владельцем, не утверждённая спецификация и не очередь задач.

Область: игрок инициирует бой, выбирает цели и действия, тратит action points, наносит/получает урон, применяет и поддерживает магические или технологические эффекты, взаимодействует с активными предметами и видит соответствующие игровые состояния. Характеристики, навыки, aptitude и их развитие относятся к разделу персонажа; модель экипировки, владения предметами, боезапас как содержимое инвентаря, торговля и crafting — к разделу предметов. Здесь указаны только их входы в боевые и magic-tech правила. Переключение карт/дальние путешествия от spell effects должны быть согласованы с отдельным разделом мира.

**Как читать доказательства.** «CE подтверждает» — в указанном фрагменте закреплённого исходного кода есть описанная ветвь или вызов; это не независимая проверка оригинального исполняемого файла. Целевые сценарии ниже — предлагаемые наблюдаемые требования продукта. Каждая описанная механика остаётся **original-runtime-unverified**. CE TODO/stub и непроверенная ветвь — предел этого источника, а не доказательство отсутствия механики в оригинальной игре. Не извлекался и не копировался список заклинаний, код, тексты или игровые данные.

## Требования

<a id="fr-combat-001"></a>

### FR-COMBAT-001 — Игрок переключает реальное время и пошаговый бой

**Поведение и приёмка.** Игрок может войти в бой, продолжить его в real-time или включить turn-based режим, завершить свой ход и выйти из боя по допустимому условию. Если сохраняется fast-turn настройка, её ожидаемое влияние на движение, поворот, ожидание и доступность действий нужно проверить отдельно; исходная инвариантность набора действий просмотренным CE-кодом не установлена. Приёмка: один сценарий запускает и завершает бой в каждом режиме; журнал фиксирует смену режима и действующего актёра без потери состояния боя.

**CE и данные.** `combat_init` читает настройки `turn_based`, `fast_turn_based` и `auto_attack`; `sub_4B6C90`, `combat_turn_based_start/end` и `sub_4B8040` выполняют переключение/начало/завершение/условие быстрого продолжения ([`combat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L198-L227), [mode и fast turn](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2857-L2890), [TB lifecycle](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3106-L3147), [fast-turn branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3733-L3765)). Его просмотренные animation callers используют предикат, чтобы выбрать мгновенный поворот либо обработать несколько шагов пути за один ход с оставшимися AP ([поворот](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L8920-L8940), [движение и AP](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10415-L10449)). Это не доказывает поведение всех действий. Нужны mode settings, combatant state, animation/combat rules. **Steamwoven:** отсутствует; каталог отмечает, что combat остаётся будущей работой ([status][status]).

<a id="fr-combat-002"></a>

### FR-COMBAT-002 — Очередь участников и инициатива объяснимы

**Поведение и приёмка.** Активные участники получают устойчивый порядок хода, пропуск неактивного/недееспособного участника и явный переход к следующему раунду. Приёмка сравнивает несколько актёров с разными параметрами и показывает фактический порядок и причину пропуска; правило инициативы не фиксируется до проверки оригинального поведения.

**CE и данные.** CE собирает PC/NPC в прямоугольнике вокруг локального PC, пересчитываемом из Perception, а `combat_turn_based_next_subturn` идёт по связанному списку, пропуская неактивных; на конце списка следующий ход продвигает время на секунду ([сбор/диапазон](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3106-L3156), [последовательность хода](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3283-L3330)). `sort_combat_list` перемещает PC к хвосту и пересчитывает reaction, но просмотренное тело не сортирует по инициативе ([`sort_combat_list`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3633-L3731)). **Steamwoven:** отсутствует ([status][status]); правило инициативы и исходная очередь неизвестны.

<a id="fr-combat-003"></a>

### FR-COMBAT-003 — Действия ограничены видимым бюджетом AP

**Поведение и приёмка.** Пошаговый режим проверяет и расходует AP на движение, melee/ranged атаку, cast, навык, применение/подбор предмета; недостаточный бюджет запрещает действие или завершает ход по правилам выбранной политики. Любое разрешённое overexert должно явно учитывать цену для состояния персонажа. Приёмка проверяет действия около порога AP, уход за соседней целью, end-turn и отображённый остаток.

**CE и данные.** `combat_check_*`, `combat_move_cost`, `combat_attack_cost`, `combat_consume_action_points` задают проверки/расход: атака зависит от скорости оружия, cast/skill используют фиксированную цену в просмотренной ветви, а локальный PC может израсходовать остаток AP ценой fatigue damage ([AP checks/costs](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3339-L3581)). Нужны weapon fields, movement-cost callers и персонажные навыки/состояния. **Steamwoven:** нет боевого AP/economy или его UI ([status][status]); персонажная стоимость действий отдельно не определена.

<a id="fr-combat-004"></a>

### FR-COMBAT-004 — Цель действия валидируется до применения

**Поведение и приёмка.** До атаки система проверяет, что выбранная цель допустима для конкретного действия; цель может быть critter, объектом или клеткой, а ограничения включают тип/состояние/дальность/область. Отмена или отказ оставляет цель и ресурсы без побочного изменения. Приёмка покрывает допустимую, мёртвую, невидимую/недоступную цель и клетку вне допустимого радиуса.

**CE и данные.** `target_context_evaluate` проверяет флаги spell/target, типы, стороны, состояние и ограничения tile; `target_context_build_list` создаёт object/tile/radius/wall/circle targets. Его вызывают выбор цели из `intgame.c`, spell UI и magic-tech invocation ([validation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c#L463-L1047), [target lists](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c#L1148-L1530), [`target_pick_at_screen_xy` и callers](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c#L250-L265), [`intgame` cast caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L2838-L2852)). **Steamwoven:** отсутствует ([status][status]). Cone/count, некоторые non-party checks и одна radius-проверка имеют TODO/FIXME в CE; это требует отдельного подтверждения.

<a id="fr-combat-005"></a>

### FR-COMBAT-005 — Атака разрешает попадание, уклонение и промах

**Поведение и приёмка.** Melee/ranged атака проходит через skill/weapon check, целевые условия, hit/miss, dodge, aim/backstab и критический исход; попадание запускает damage только после разрешения проверки, а промах может иметь собственный эффект. Приёмка с фиксированным seed и актёрами по обе стороны порога сопоставляет выбранную цель, die/result, dodge и итоговое изменение состояния.

**CE и данные.** `sub_4B3170` выполняет проверку weapon aptitude, ranged blocker, `SkillInvocation`, dodge, fate crits и затем вызывает melee/ranged processor ([attack resolution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L830-L995)). `sub_4B2210` строит контекст из оружия/навыка и отмечает melee/ranged/backstab ([context](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L349-L404)). Требуются навыки, оружие, состояния и RNG; сами skill formulas принадлежат разделу персонажа. **Steamwoven:** нет combat resolver ([status][status]); эти входы не реализованы как gameplay.

<a id="fr-combat-006"></a>

### FR-COMBAT-006 — Анимация атаки синхронизирована с игровым действием

**Поведение и приёмка.** Запуск melee/ranged/throwing action связывает позу, направление, длительность и момент выстрела/удара с результатом действия; нельзя наносить урон дважды из-за повторного кадра или пропустить исход при смене режима. Приёмка проверяет hit/miss и action-frame для разных скоростей оружия и при прерывании/недееспособности.

**CE и данные.** `anim_goal_attack_ex` создаёт attack goal, при default-throw переводит в бросок и проверяет возможность атаки; `AGbeginAnimAttack` списывает AP, блокирует стон/паралич/stun, вычисляет задержку из FPS и weapon speed, а update обрабатывает action frame ([goal start](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L13932-L14023), [attack begin](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L12729-L12817), [frame update](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L12820-L12901)). Нужны actor/weapon ART events and FPS. `mp_anim_modify` помечена `TODO: Incomplete`; weapon FX содержит ещё один TODO. **Steamwoven:** только ограниченный diagnostic playback, без действий боя и action events ([status][status]).

<a id="fr-combat-007"></a>

### FR-COMBAT-007 — Снаряды, броски и боезапас разрешаются по типу оружия

**Поведение и приёмка.** Ranged action выбирает траекторию/снаряд, учитывает попадание, блокирующего актёра/объект, дальность и промах; бросаемый предмет может быть потерян или возвращён по своему типу; оружие с боезапасом проверяет и расходует его до выстрела. Приёмка сопоставляет projectile path, blocker, расход ammo и результат промаха для стрелкового, метательного и возвращаемого варианта.

**CE и данные.** `combat_process_ranged_attack` задаёт missile ART, смещение точки промаха, двойную стрелу для соответствующего training и создаёт projectile; `combat_create_projectile`/`sub_4B2870` обрабатывают полёт, препятствия и попадание; `combat_consume_ammo` проверяет тип/количество/расход; `combat_throw` создаёт бросок ([ranged setup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1057-L1116), [projectile resolution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L428-L730), [ammo/throw](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1131-L1160), [ammo check](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2574-L2604)). Inventory lifecycle и боезапас предметов входят в раздел предметов. **Steamwoven:** отсутствует ([status][status]).

<a id="fr-combat-008"></a>

### FR-COMBAT-008 — Урон складывается по типу, сопротивлению и защите

**Поведение и приёмка.** Каждая атака вычисляет компоненты normal/poison/electrical/fire/fatigue, применяет сопротивления и иммунитеты, hit location/armor, flags weapon и выбранную difficulty; один вызов не смешивает damage и fatigue. Допустимые удары по разрушаемым порталам/контейнерам используют материал и HP, после чего вызывают bust/trap path. Приёмка сравнивает одну и ту же атаку на разных resistance, armor slot и material.

**CE и данные.** `combat_calc_dmg`, `combat_apply_resistance` и `combat_dmg` рассчитывают типы/сопротивления, immunity и последствия для critter; `combat_critter_armor` выбирает экипированную защиту по hit location; non-critter branch обрабатывает portal/container damage ([damage/resistance](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1390-L1475), [armor, material and object damage](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1897-L1943), [damage calculation/resistance](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2606-L2710), [resistance fields](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/resistance.c#L1-L9)). Armor and item stats are shared with предметным разделом. **Steamwoven:** damage, armor, portal/container damage absent ([status][status]).

<a id="fr-combat-009"></a>

### FR-COMBAT-009 — Критические попадания и промахи имеют последствия

**Поведение и приёмка.** Критический исход использует weapon hit/miss profile и target body type для возможного бонусного урона, stun/knockdown/knockout, blind/cripple/scar, drop/damage/break gear или loss of ammo; допустимые эффекты ограничены видом цели. Приёмка подаёт сценарии по разным body/chart combinations и подтверждает, что результат и затронутый объект соответствуют выбранной таблице.

**CE и данные.** `combat_process_crit_hit`/`combat_process_crit_miss` разбирают таблицы удара и промаха, а `criticals.h` перечисляет источниковые категории hit, miss и body profile ([hit/miss resolution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2327-L2561), [chart enums](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/criticals.h#L1-L57)). Нужны weapon/prototype chart fields, target body type и RNG. **Steamwoven:** нет critical resolver или данных профилей ([status][status]). CE не заменяет проверку отдельных значений/вероятностей в оригинальных игровых данных.

<a id="fr-combat-010"></a>

### FR-COMBAT-010 — Усталость, нокаут и смерть остаются разными исходами

**Поведение и приёмка.** HP-death и fatigue-based unconsciousness моделируются отдельно: fatigue может лишить сознания без смерти, а восстановление fatigue позволяет подняться; stun, паралич, stone, poison, blind/cripple и другие статусы влияют только на явно назначенные действия. CE содержит `OCF_NON_LETHAL_COMBAT` guard в auto-attack continuation, но этот флаг сам по себе не доказывает преобразование урона в knockout или nonlethal поведение ручной атаки. Приёмка различает knockout от HP≤0, auto-attack guard и lethal/manual attack paths.

**CE и данные.** `critter_is_unconscious` использует fatigue threshold с исключением undead, а `critter_is_dead` проверяет HP; `critter_fatigue_damage_set` инициирует demaintain и knockdown, recovery event может вернуть actor в сознание. `combat_heal` включает обычное лечение, очистку injuries/состояний и gated resurrect/reanimate ([critter states](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L462-L615), [recovery](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1320-L1368), [combat states](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1556-L1600), [healing/resurrection](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2104-L2278)). `anim.c` проверяет `OCF_NON_LETHAL_COMBAT` при продолжении автоматической атаки, тогда как просмотренные `combat_dmg` branches не используют его как damage conversion ([auto-attack continuation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7015-L7077), [damage resolver](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1390-L1456)). `critter.c` помечает fatigue recovery math TODO. **Steamwoven:** отсутствует ([status][status]).

<a id="fr-combat-011"></a>

### FR-COMBAT-011 — Боевой интерфейс показывает режим, AP и очередь

**Поведение и приёмка.** Во время пошагового боя игрок видит доступные/зарезервированные AP, текущего участника, окончание хода и отказ действия; действия из клавиатуры и кнопки вызывают тот же turn resolver. Приёмка сверяет видимую цену с фактическим AP после атаки, перемещения и spell/item action, а переход AI→PC не маскирует чью очередь ждут.

**CE и данные.** `combat_ui` рисует AP-индикатор, NPC-turn cue и обрабатывает кнопку/E для `combat_turn_based_next_subturn`; `tb_ui_init` связывает UI callbacks со spell maintenance, skill, fatigue/health и tech UI ([AP and end-turn UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/combat_ui.c#L317-L396), [turn indication/input](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/combat_ui.c#L542-L689), [`tb_ui` callback boundary](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L60-L100)). **Steamwoven:** production gameplay UI и combat UI отсутствуют; synthetic display lab не доказывает их покрытие ([status][status]).

<a id="fr-combat-012"></a>

### FR-COMBAT-012 — Автоматическая атака и выбор оружия подчиняются режиму и настройкам

**Поведение и приёмка.** Игрок может включать/выключать автоматическое повторение допустимой атаки в real-time и отдельную настройку автоматического выбора оружия. Auto-attack прекращается для невалидной цели, в turn-based режиме и при явно запрещающем состоянии; ручная атака остаётся самостоятельным действием. Приёмка сравнивает настройку on/off, валидность цели, active turn-based и nonlethal flag, затем проверяет weapon-switch после исчерпания/потери подходящего оружия.

**CE и данные.** `combat_auto_attack_get` возвращает false в пошаговом бою; animation continuation проверяет PC/combat mode/живую цель/nonlethal flag/boomerang. `pc_switch_weapon` вызывает `item_wield_best`, если включён auto-switch; Options UI подключает оба setting ([auto-attack continuation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L7015-L7077), [settings and switch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3843-L3889), [Options UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/options_ui.c#L441-L468)). Нужны current target, combat mode и экипированные предметы. **Steamwoven:** этих combat settings/actions нет ([status][status]); auto-weapon equipment details относятся к item report.

<a id="fr-combat-013"></a>

### FR-COMBAT-013 — Журнал убийств и история травм

**Поведение и приёмка.** Игрок видит накопленную статистику убийств и записи полученных травм, включая связанную сущность/состояние в поддержанном профиле. Эти данные относятся к игровому состоянию и переживают собственное save/load; они не выводятся из текущих анимаций или числа видимых тел. Приёмка: kill/event не учитывается дважды, принадлежность PC/лидеру задаётся явным правилом, запись травмы и её активное состояние отображаются согласованно. Формулы, ограничения записи и исходная attribution требуют отдельной проверки.

**CE и границы.** `logbook_add_kill` обновляет counters/notable-enemy записи; смерть NPC вызывает её для PC-killer либо PC-leader ([writer/getter](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L18-L96), [death caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L684-L720)). Вкладка читает kill/injury данные и оформляет записи ([UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1274-L1319), [formatting](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1609-L1683)). Combat вызывает injury writer, но в `logbook_add_injury` комментарий о critters противоречит guard: он выходит именно для critter, поэтому последующие self/PC branches не доказывают работающую attribution ([critical caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1598-L1671), [contradictory writer](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L103-L145)). Это CE anomaly, не целевое правило и не доказательство оригинального runtime.

**Зависимости и покрытие.** Combat outcome, character injury-state, journal UI и persistence; FR-SOCIAL-008 остаётся владельцем quest/social записей. Steamwoven: нет данных, правил, интеграции и проверок этой игровой истории. Проверки выше предложены, а не выполнены.

<a id="fr-magic-001"></a>

### FR-MAGIC-001 — Заклинания и технологические эффекты имеют загружаемые определения

**Поведение и приёмка.** Определение ability сообщает тип (spell/tech), college/degree association, разрешённое действие, target parameters, cost, длительность/поддержание, save/resistance и effect components. UI может показать определение из подключённых данных, не подменяя неизвестные поля выдуманной карточкой. Приёмка сверяет несколько фактических data records по полям и проверяет явный статус для неизвестных/некорректных компонентов; список spell names здесь не нормируется.

**CE и данные.** `magictech_init`, `sub_44FE30` и `sub_44FFA0` читают `Rules\magictech.mes`, `mes\spell.mes`, `Rules\SpellEyeCandy.mes` и `Rules\spelllist.mes`, собирая записи и effect-компоненты; `MagicTechInfo` хранит общий типовой runtime record ([loader](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L743-L809), [records/components](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1301-L1434), [`magictech.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.h#L1-L210)). **Steamwoven:** нет magic-tech data model, invocation runtime или in-game spell/tech view ([status][status]); приёмка оригинальных records не запускалась.

<a id="fr-magic-002"></a>

### FR-MAGIC-002 — Книга заклинаний и предпосылки следуют последовательности школ

**Поведение и приёмка.** Для изучения заклинания UI показывает college/уровень и разрешает только допустимое следующее звено при нужных условиях уровня и характеристик; снятие заклинания не нарушает уже изученную последовательность. Tech degree и характеристические пороги проверяются через отдельный character contract. Приёмка проверяет первый и следующий rank, блокировку пропущенного rank, insufficient prerequisite и удаление последнего известного.

**CE и данные.** `spell_add` проверяет предшествующий spell level, min character level, intelligence и willpower; `spell_remove` работает только для последнего уровня college. `spell_ui_add/remove` связывают это с очками и UI ([spell learning/removal](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L429-L573), [UI callers](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L490-L542)). CE `magictech_min_intelligence` сейчас возвращает константу 5, поэтому эту ветвь нельзя принимать за установленную полную таблицу prerequisites ([getter](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1472-L1490)). **Steamwoven:** отсутствует ([status][status]); spell point/stat progression остаётся за разделом персонажа.

<a id="fr-magic-003"></a>

### FR-MAGIC-003 — Цель магического действия выбирается по параметрам определения

**Поведение и приёмка.** Cast может требовать себя, объект, группу объектов или tile/radius, проверяет дальность и line of sight, запрещает недопустимую цель и сохраняет её только после успешного invocation. Для NPC fallback на допустимую клетку не должен менять правила игрока без явного основания. Приёмка покрывает self, valid object, blocked LOS, слишком дальнюю цель и tile/radius case.

**CE и данные.** `magictech_invocation_check` проверяет turn, range, LOS, target flags и допустимость invocation; `magictech_check_los` учитывает blocker/shoot-through. `spell_ui_activate/apply` переводят игрока в targeting и отправляют invocation; `target_context_build_list` предоставляет кандидатов ([invocation validation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4928-L5068), [LOS](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5148-L5182), [spell UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L180-L329)). Data: range, target flags/radius/count, map geometry and spell fields. **Steamwoven:** отсутствует ([status][status]); CE target cone/count cases имеют отмеченные implementation doubts.

<a id="fr-magic-004"></a>

### FR-MAGIC-004 — Применение заклинания или техноэффекта расходует указанный ресурс

**Поведение и приёмка.** Ability проверяет цену до действия, списывает ресурс только один раз при успешной фазе и обрабатывает нехватку без частичного повреждения состояния. Для magic возможны fatigue, charge cells или equipped-item mana store; применимые race/mastery modifiers остаются входами персонажа. Приёмка следит за исходным и конечным запасом на success, fail, interrupt и insufficient-funds paths.

**CE и данные.** `magictech_can_charge_spell_fatigue`, `sub_450420` и `spell_cast_cost` применяют cost, могут расходовать charge-cell/mana-store и fatigue; мастерство колледжа и dwarf меняют цену/maintenance period. `spell_ui_apply` сначала проверяет affordability, затем создаёт invocation ([cost paths](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1536-L1751), [spell cost/mastery](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c#L347-L406), [UI affordability](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L291-L329)). Item charge storage/wear semantics принадлежат разделу предметов. **Steamwoven:** отсутствуют spells, resource charging и invocation ([status][status]).

<a id="fr-magic-005"></a>

### FR-MAGIC-005 — Магическая склонность, сопротивление и очки судьбы дают предсказуемый исход

**Поведение и приёмка.** Magic проверяет target magic resistance/aptitude и spell-specific saving throw; tech effect подчиняется отдельно определённому правилу и не получает магическую защиту автоматически. Source aptitude может повлиять на возможность cast; fate-based maximization/defense меняет только предусмотренные effect/result параметры. Приёмка сравнивает один effect при нейтральном, magick-aligned и tech-aligned target и проверяет fate modifiers раздельно.

**CE и данные.** `magictech_cast_spell_fail_chance` отделяет spell от tech, рассчитывает magic resistance и save; `magictech_invocation_check` применяет source/target aptitude; `magictech_process` и fate calls включают путь maximized effect ([resistance/save](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3447-L3539), [aptitude checks](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5023-L5047), [fate and target processing](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1986-L2102)). CE comment states that one source branch omits equipment magic-resistance adjustments; this is a CE statement, not independently verified here. Общая metamagic menu/model source-confirmed не обнаружена; отдельные Fate modifiers не доказывают полный metamagic contract. **Steamwoven:** отсутствует ([status][status]).

<a id="fr-magic-006"></a>

### FR-MAGIC-006 — Поддерживаемые заклинания имеют жизненный цикл и правила прерывания

**Поведение и приёмка.** Effect может иметь begin/maintain/end stages, duration/maintenance interval, periodically charge its owner, target or both as defined by data, and end on explicit interrupt, expiration, caster unconsciousness or prohibited state. UI exposes active maintained effects and allows stopping them. Приёмка проверяет begin, periodic tick, interrupt, natural expiration and unconsciousness without duplicate or stranded effects.

**CE и данные.** `magictech_process` dispatches component actions; `magictech_interrupt`, scheduled duration/maintenance and `magictech_demaintain_spells` end or update active runs. `spell_ui_maintain_add/click/refresh` link active effects to slots ([process](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1986-L2102), [interrupt/demaintain](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5359-L5508), [maintained UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L332-L480), [unconscious caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L481-L491)). Effect records and time events are required inputs. **Steamwoven:** no active-effect engine/maintenance UI ([status][status]).

<a id="fr-magic-007"></a>

### FR-MAGIC-007 — Эффекты заклинаний охватывают урон, лечение, усиления и состояния

**Поведение и приёмка.** Loaded components may apply typed damage, heal HP/fatigue/poison or injuries, add/remove timed status/effect, dispel/interrupt, or change object/critter flags. Each effect respects target compatibility and owns a clear duration/removal path. Приёмка runs paired apply/remove or damage/heal cases and validates unchanged unrelated stats; names alone never define component effect.

**CE и данные.** `MTComponentDamage_ProcFunc`, `MTComponentHeal_ProcFunc`, `MTComponentEffect_ProcFunc`, `MTComponentDispel_ProcFunc` call combat/effect APIs; the dispatcher applies component data to current target ([damage/heal/effect functions](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2220-L2295), [effect/dispel](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2322-L2486), [healing component](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2555-L2589)). Required input includes effect definitions, damage flags, resistance, target type and game-time lifecycle. **Steamwoven:** no spell/effect/combat APIs ([status][status]); character status storage/aptitude remains separately specified.

**Legacy-представление эффектов.** Игровой исход отделён от data-driven FX/overlay, прикреплённого к actor или позиции; [выбранный `animfx` path](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/animfx.c#L291-L420) показывает исполнительную инфраструктуру. Состав каждого original FX, ресурсный parser и весь lifecycle/caller набор не аудирован; наличие executor не означает полного spell/VFX покрытия. Modern particles из FR-PRODUCT-007 не заменяют эту Legacy-функцию.

<a id="fr-magic-008"></a>

### FR-MAGIC-008 — Призыв создаёт связанный эффект с ограниченным сроком/объёмом

**Поведение и приёмка.** Summoning selects a defined prototype or data-backed list candidate, creates it at a permitted location, associates it with the caster/effect, applies summoned status and cleans up or deactivates it when the owning effect ends. Приёмка covers invalid prototype/list, blocked location, active summon and expiration; summon must not create an unbounded duplicate on repeat maintenance.

**CE и данные.** `MTComponentSummon_ProcFunc` selects a prototype or list entry, calls `magictech_effect_summon`; the latter sets summoned/owner state and tracks the created actor ([summon component](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2760-L2781), [summoned effect lifecycle](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1856-L1962)). Data includes prototype/list, placement, faction/palette and owning run. **Steamwoven:** no spell, actor spawn or active magic-tech runs ([status][status]). AI summoning policy belongs to NPC/AI requirements.

<a id="fr-magic-009"></a>

### FR-MAGIC-009 — Телепортация и эффекты среды учитывают ограничения мира

**Поведение и приёмка.** A supported movement spell changes actor location only through an explicit valid destination; anti-teleport regions/flags block it with clear feedback. Other environment-changing effects are implemented only where their data-defined target and observable world-state mutation are understood. Приёмка checks allowed and blocked destinations, same-map vs explicit map destination, follower/state preservation and environment effects independently.

**CE и данные.** `magictech_invocation_check` explicitly gates teleport via `antiteleport_check`; `MTComponentMovement` can request current/start-map relocation. But `MTComponentEnvFlag_ProcFunc` only logs “Invalid Component Type!” and performs no observed state change ([teleport gate](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5050-L5068), [movement/teleport component](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L2652-L2749), [environment component stub](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3033-L3042)). Destination map and transitions cross-reference the world/travel report. **Steamwoven:** no magic movement or world-state effect system ([status][status]); the CE stub does not imply original absence.

<a id="fr-magic-010"></a>

### FR-MAGIC-010 — Для магии и технологий действуют явные правила противодействия

**Поведение и приёмка.** Magic and tech abilities share invocation/effect infrastructure only where their rules agree. Their differences in resource, target resistance, aptitude interaction and eligibility remain explicit. Приёмка casts paired magic and tech effects at targets with changing magick-tech aptitude, resistance and source aptitude; logs distinguish blocked, resisted, save and resource failure. Tech degree/eligibility calculation remains a shared input to the character domain.

**CE и данные.** `magictech` marks tech records and branches effect resistance separately; CE’s `magictech_cast_spell_fail_chance` applies magic resistance to spells, while `tech_degree_level_get` derives effective degree from stored degree, intelligence and effects ([tech discrimination](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1319-L1332), [resistance branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3447-L3539), [tech effective level](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c#L333-L358)). **Steamwoven:** no tech/spell invocation or aptitude opposition ([status][status]). Exact coupling remains subject to original-runtime evidence.

<a id="fr-magic-011"></a>

### FR-MAGIC-011 — Магические и технологические предметы срабатывают по событию

**Поведение и приёмка.** Device/item effects trigger only on their declared event and target: use, wear/unwear, hit/stun/unconscious/death, attack opponent/location or target hit. Frequency/probability and resource checks happen once; effect is linked to source device and event actor. Приёмка independently covers on-use, on-hit, on-wear and failed charge/cost without firing unrelated triggers.

**CE и данные.** `handle_item_event_func` scans item spell slots, matches event flags, tests defined chance, builds a `MagicTechInvocation`, sets actor/target/location and runs it; combat/critter callers notify these trigger events ([item trigger executor](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c#L187-L269), [combat notifications](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c#L289-L322), [item event families](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c#L389-L529)). `item_effect.c` reads description strings only; it is not the event/effect resolver ([description API](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item_effect.c#L31-L130)). **Steamwoven:** no item event/effect runtime ([status][status]); inventory/equipment lifecycle belongs to item requirements.

<a id="fr-magic-012"></a>

### FR-MAGIC-012 — Интерфейс магии и технологий показывает допустимые действия и активное состояние

**Поведение и приёмка.** Игрок видит доступность spell/tech action, выбор цели, ошибку отказа, maintained effect and cancel/interrupt; UI state matches authoritative simulation. Tech degree editing and associated point/stat accounting are specified in character development, not duplicated here. Приёмка casts and cancels an action through hotkey and mouse path, observes maintained slot, then confirms final effect/resource state in gameplay model.

**CE и данные.** `spell_ui_activate/apply` checks turn cost, target mode, affordability and LOS; maintenance UI adds/stops effects and renders slots. `tech_ui` changes degree through character editor; `tb_ui` wires callbacks, while `intgame.c` routes player selection/casting ([spell UI](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c#L180-L480), [tech UI boundary](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tech_ui.c#L14-L104), [`tb_ui` callbacks](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L60-L100), [input caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L2838-L2852)). **Steamwoven:** game UI and cast/tech state are absent ([status][status]); display lab is not game integration.

## Сводное покрытие на Steamwoven baseline

Статусы относятся только к текущему Steamwoven source snapshot: для этих доменов нет игрового data model, resolver или UI integration. `Нет` не оценивает наличие соответствующей ветви в CE; CE source inspection — исследование, а не выполнение acceptance scenario. Ограниченный общий animation playback и диагностические/UI лаборатории не считаются combat/magic-tech покрытием.

| ID | Данные | Правила | Интеграция | Проверка |
| --- | --- | --- | --- | --- |
| FR-COMBAT-001 | Нет | Нет | Нет | Нет |
| FR-COMBAT-002 | Нет | Нет | Нет | Нет |
| FR-COMBAT-003 | Нет | Нет | Нет | Нет |
| FR-COMBAT-004 | Нет | Нет | Нет | Нет |
| FR-COMBAT-005 | Нет | Нет | Нет | Нет |
| FR-COMBAT-006 | Нет | Нет | Нет | Нет |
| FR-COMBAT-007 | Нет | Нет | Нет | Нет |
| FR-COMBAT-008 | Нет | Нет | Нет | Нет |
| FR-COMBAT-009 | Нет | Нет | Нет | Нет |
| FR-COMBAT-010 | Нет | Нет | Нет | Нет |
| FR-COMBAT-011 | Нет | Нет | Нет | Нет |
| FR-COMBAT-012 | Нет | Нет | Нет | Нет |
| FR-COMBAT-013 | Нет | Нет | Нет | Нет |
| FR-MAGIC-001 | Нет | Нет | Нет | Нет |
| FR-MAGIC-002 | Нет | Нет | Нет | Нет |
| FR-MAGIC-003 | Нет | Нет | Нет | Нет |
| FR-MAGIC-004 | Нет | Нет | Нет | Нет |
| FR-MAGIC-005 | Нет | Нет | Нет | Нет |
| FR-MAGIC-006 | Нет | Нет | Нет | Нет |
| FR-MAGIC-007 | Нет | Нет | Нет | Нет |
| FR-MAGIC-008 | Нет | Нет | Нет | Нет |
| FR-MAGIC-009 | Нет | Нет | Нет | Нет |
| FR-MAGIC-010 | Нет | Нет | Нет | Нет |
| FR-MAGIC-011 | Нет | Нет | Нет | Нет |
| FR-MAGIC-012 | Нет | Нет | Нет | Нет |

## Важные пределы источника и открытые вопросы

- Источник подтверждает конкретные combat/AP/damage/fatigue/spell/device code paths, но не устанавливает оригинальную runtime parity. Полная формула порядка инициативы из просмотренного `sort_combat_list` не выводится; `combat_recalc_reaction` вызывается, но наблюдённой сортировки по reaction/initiative в этом теле нет. Общий gameplay passability/LOS и внутреннее устройство skills/effects/data records также не полностью исследовались.
- Усталость и knockout в просмотренном CE разделены от HP-death и дают подходящую основу для nonlethal результата. Но отдельный пользовательский combat toggle “не убивать” и его скриптовые исключения не подтверждены просмотренными фрагментами.
- В CE есть material для дальнейшей проверки: `magictech_min_intelligence` возвращает 5; `MTComponentEnvFlag_ProcFunc` — stub; `target.c` имеет отмеченные сомнения в cone/count/radius и некоторые target branches; `mt_item.c`/`mt_ai.c` содержат FIXME по неиспользуемым или сомнительным AI полям; `anim.c` имеет неполный `mp_anim_modify` и TODO к одному FX ID; `critter.c` помечает fatigue recovery math TODO. Эти замечания ограничивают изученный CE, но не описывают отсутствующую оригинальную механику.
- `mt_ai.c` содержит AI-очереди spell/device actions и изучен только на выбранных ветвях; полный выбор целей, приоритетов и принятия решений относится к будущему AI-разделу. Точное содержимое spell lists и всех tech devices не перечислялось.

## Граница чтения CE

Это список реально просмотренных фрагментов для этого домена, а не полный аудит каждого файла. Ссылки закреплены на commit из заголовка.

| CE files actually inspected | Reviewed path/range | Purpose |
| --- | --- | --- |
| [`combat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c) | `combat_init`; attack context/resolution/melee/ranged/projectile/throw/damage/heal/critical/ammo; mode toggle/TB roster/order/AP; `sort_combat_list`/fast turn. Основные интервалы: 198–227, 349–476, 548–730, 830–1160, 1390–1943, 1947–1958, 2104–2278, 2327–2710, 2843–2907, 3106–3581, 3633–3765. | Player-facing combat modes, actions, outcome and state. |
| [`combat.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.h), [`criticals.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/criticals.h), [`damage_type.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/damage_type.h), [`resistance.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/resistance.c) | `combat.h` damage/AP/context declarations; all of `criticals.h`, `damage_type.h`, `resistance.c`. | Enum categories and resistance fields. |
| [`anim.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c), [`critter.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c) | `anim.c`: attack goal/action frame ranges 12729–12901, 13932–14023; auto-attack continuation 7015–7077; selected TODO checks 11589, 15270–15335. `critter.c`: fatigue/state/death and recovery ranges 448–680, 1320–1368. | Action timing, auto-repeat eligibility, actor states and unconscious/death lifecycle. |
| [`target.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c) | Target init/pick and selected validation/list-building branches: 124–172, 250–385, 463–1047, 1148–1530. | Combat/spell object and tile selection. |
| [`magictech.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c), [`spell.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c), [`tech.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c) | `magictech.c`: init/data records 743–830, 1301–1434; ability fields/costs 1452–1751; action/target/effect branches 1856–2102, 2220–2295, 2322–2486, 2555–2781, 3033–3042; resistance/invocation/LOS 3447–3539, 4928–5182; interrupt/de-maintain 5359–5508; selected parser/fate callers. `spell.c`: 347–525, 551–573, 737–789. `tech.c`: 221–358, 366–410, 424–453. | Ability lifecycle, spell prerequisites, resistance, tech level and data dependencies. |
| [`mt_item.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c), [`mt_ai.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c), [`item_effect.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item_effect.c), [`ci.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ci.c) | `mt_item.c` trigger/caller ranges 1–272, 289–530, 547–633; `mt_ai.c` selected list/cache/populate/action selection 1–220, 223–475; `item_effect.c` 31–130; all of `ci.c`. | Device effect entry points, boundary to AI, effect descriptions, combat indicator. `item_effect.c` does not resolve effect behavior. |
| [`combat_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/combat_ui.c), [`spell_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c), [`tech_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tech_ui.c), [`tb_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c), [`intgame.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c), [`options_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/options_ui.c) | Combat AP/turn UI 97–149, 317–396, 542–689; spell targeting/maintenance/learning 180–232, 291–542; tech degree UI 14–104; TB callbacks 60–100, 330–338; selected intgame target/cast/mode callers; auto-attack option 441–468. | Player-visible AP, spell targeting/maintenance, tech UI handoff, combat settings and caller flow. |
| [`magictech.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.h), [`item_effect.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item_effect.h) | Relevant type/action/component declarations. | Record and API boundaries. |

Дополнительное чтение при ревью и добавлении FR-COMBAT-013/MAGIC-007: [logbook writer/getter](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L18-L96), [injury writer и противоречивый guard](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c#L103-L145), [death caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L684-L720), [critical injury callers](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1598-L1671), [вкладка журнала](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1274-L1319) и [форматтер записей](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c#L1609-L1683), [выбранная FX attachment infrastructure](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/animfx.c#L291-L420). Остальные ветви этих модулей, полнота injury lifecycle, logbook save payload и полный FX parser/caller/lifecycle не проверены.

Related but uninspected or inspected only as callers/declarations: `game/effect.c` and `effect.h` (effect internals); skill and stat formula bodies; `game/fate.c`; `game/item.c` full weapon/armor/ammo semantics; full `game/mt_ai.c` including later candidate scoring/selection branches; full `game/tb.c`; remainder of `target.c` after 1530; rest of `anim.c` and animation goal scheduler; remaining inventory, character editor and dialog/script callers; remaining parser/data-record branches in `magictech.c`. These gaps mean this report does not define every formula, status stacking rule, event, AI decision, tech device or multiplayer path.

[status]: https://github.com/Simarglok/arcanum-steamwoven/blob/b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae/docs/project/status.md#L23-L25
