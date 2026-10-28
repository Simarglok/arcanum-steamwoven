# Оригинальное управление клавиатурой и мышью

**Дата:** 2026-10-04. **Назначение:** требования к независимой реализации keyboard/mouse-ввода, его интерфейса и отделению диагностики от игровых действий. Этот документ развивает FR-UI-002, FR-NAV-001 и FR-PRODUCT-002. Пункты KM уточняют существующие группы требований и не создают задачи или новые группы в общем счётчике FR.

**Принято владельцем:** keyboard/mouse сохраняют оригинальную схему Arcanum и интерфейсные действия. WASD не управляют персонажем. Кастомные пресеты, прямое управление стиком, contextual prompts и иной interaction UI относятся к gamepad. Modern resolution, HiDPI и масштабирование оригинального интерфейса остаются принятыми целями; это не разрешение заменять мышиную раскладку геймпадной.

**Результат исследования:** функции ввода оригинального executable прочитаны по адресам на уровне F0 (с exe сверены строки журнала LB1, CS1, RT4, FA1–FA4), сверены оригинальный manual и Quick Reference Card. Ни один пункт не заявляет запуск или полный паритет с оригинальным executable. Код управления, игровые данные, активные карточки и расписание этим исследованием не изменены. Варианты диагностического слоя ниже предложены для выбора, а не объявлены действующим mapping.

## Источники и порядок разрешения расхождений

| Обозначение | Источник и локатор |
| --- | --- |
| M | Предоставленный владельцем GOG manual.pdf, Arcanum Players Guide, 187 PDF-страниц. SHA-256: daf0e1851e95725c3f0572bc4923df8d33873fcfad6d855a81e9bafe5566c892. Appendix A-10 Quick Keys: PDF 181–182, печатные 188–189. |
| Q | Предоставленный владельцем Arcanum_Quick_Reference_Card.pdf, 2 PDF-страницы; Quick Keys находится на PDF 2. SHA-256: 5cf8414c918f233295f51b9aaa1caa8f6db4984e42ea9838240ebcb2094977fb. Это скан; таблица проверена визуально. |
| EXE | Retail `Arcanum.exe` 1.0.7.4. Коды K/P/U/T ниже указывают функции оригинала по адресам в скобках ([коды локаторов](#коды-локаторов)); основание — чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в журнале сверки. |
| SW | Срез исходников Steamwoven для списка текущих расхождений: db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c, без незамерженных кандидатов. Это инвентарь кода, не повторная проверка сборок или реальный GUI-тест. |

В manual часть страниц разделяет общий content stream разворота и разные cropbox. Текстовая выгрузка одного PDF листа может включать соседнюю печатную страницу. Поэтому локаторы содержат отдельно PDF-номер и печатные номера; релевантные развороты и оба листа Appendix проверены визуально. PDF и их полные текстовые выгрузки не включаются в MIT-репозиторий. Здесь сохранены функциональные описания, идентификаторы и происхождение источников, без игровых изображений и извлечённых игровых реплик.

Статусы знания:

- **Original-documented:** действие описано в M/Q; это документированный контракт, а не наблюдение runtime.
- **F0:** чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в журнале сверки; конкретный вызов и условия описаны, но это не доказательство полного совпадения с retail.
- **Exe-verified:** поведение подтверждено строкой [журнала сверки](../research/original-verification-ledger.md) (например LB1, RT4, FA1–FA4).
- **Owner-policy:** принятое направление Steamwoven, включая разделение способов управления.
- **Open:** требуются данные, более глубокая проверка или наблюдение оригинального runtime.
- **Proposal:** предложенный новый диагностический слой; раскладка не выбрана и не реализована.

При расхождении F0-наблюдения с M/Q исходным требованием остаётся документированное поведение оригинала, а расхождение записывается отдельно. Нельзя молча переносить bug, stub, cheat или convenience feature вне документированного оригинала в original compatibility. Если наблюдение оригинальной версии расходится с руководством, нужно явно выбрать versioned compatibility baseline и обновить соответствующий пункт. M и Q частично повторяют одну таблицу; их нельзя считать двумя независимыми runtime-проверками.

## KM-01 — Два адаптера, общая игровая симуляция

Keyboard/mouse и gamepad передают семантические команды в общую симуляцию, но сохраняют разные способы выбора действий и целей. Для keyboard/mouse нельзя преобразовывать W/A/S/D в направление движения, I в Inspect, C в универсальное Context, Space в универсальное Confirm или F1 в Settings. Эти клавиши имеют игровые назначения.

Прямое управление левым стиком, правая камера, LT/RT zoom, gamepad presets и подсветка по RB остаются отдельным gamepad-контрактом. Настройка инверсии правого стика не должна инвертировать оригинальные стрелки/мышь. Оригинальный mouse-hover сообщает сведения в Message Window; controller contextual labels не подменяют этот интерфейс в keyboard/mouse-режиме. Смена input method сохраняет actor/world/action state; случайное движение указателя или переключение prompts не дублирует команду. Неисследованный или ещё не реализованный игровой экран не освобождает его клавишу под WASD/диагностику: действие остаётся недоступным с явным статусом.

Приёмка: W/A/S/D не создают MoveCharacter/step/goal; W/S/A выполняют только свои разрешённые интерфейсные действия, D не имеет normal actor binding. Результаты gamepad-лаборатории не засчитываются за original keyboard/mouse parity.

## KM-02 — Документированная игровая раскладка

Во всех строках этой таблицы M означает Appendix A-10 (PDF 181–182 / печатные 188–189), Q — PDF 2. Документы задают действие; тип события down/up и часть ограничений ниже установлены только на уровне F0 и требуют сверки в exe/runtime для заявления точного паритета.

| Ввод | Требуемое действие оригинала | Наблюдение по адресам и границы |
| --- | --- | --- |
| Escape | Главное игровое меню; во вложенном main menu — возврат к предыдущему окну. | Up: возврат к MAIN из части режимов либо in-play/locked menu. Locked применяется при dialog, world map или чужом TB-ходе. K1 (0x401560). В окнах главного меню (Single Player, New Game, браузер героев) Escape — назад; в корневом окне — ничего при первом открытии, при повторном — выход из игры; буква кнопки (сообщение подписи + 10 в `MainMenu.mes`) выбирает её ([MM6](../research/ledger/ui-audio.md#main-menu-arc-ui-011), ARC-UI-011). |
| F1 | Приказать всем Followers идти к выбранной точке. | Up: broadcast index 0, локация под указателем. M дополнительно PDF 74–75 / печатные 74–75. K2 (0x54DE50), K12 (0x4C3B90, 0x4C3BE0) |
| F2 | Приказать всем Followers атаковать выбранную цель. | Up: broadcast index 1; тот же source/target context. K2 (0x54DE50) |
| F3 | Followers следуют ближе. | Up: broadcast index 2. Это не свободный overlay hotkey. K2 (0x54DE50) |
| F4 | Followers следуют на большем расстоянии. | Up: broadcast index 3. K2 (0x54DE50) |
| F5 | Followers прекращают бой со своей целью. | Up: broadcast index 4. K2 (0x54DE50) |
| F7 | Быстрое сохранение в выделенный auto-save слот. | Up: живой PC, допустимый собственный ход; при открытой карте её сначала закрывают, затем сохраняют SlotAuto. K1 (0x401560) |
| F8 | Быстрая загрузка самого нового сохранения. | Up: helper берёт сохранение с самым поздним временем изменения файла (auto-save или любой номерной слот), не SlotAuto явно; проверено двумя чтениями exe ([SL2](../research/ledger/ui-audio.md#save-list-arc-ui-010)), во viewer с ARC-UI-010. K1 (0x401560), K8 (0x403850) |
| F12 | Снимок экрана. | Настроен отдельный TIG callback по началу обнаруженного удержания; обычный key-up handler игрового интерфейса не используется. Пути/формат файлов не задаются этим документом. K7 (0x401000), T3 |
| NumLock | Переключение режима постоянного бега. | Оригинал 1.0.7.4 читает состояние Num Lock (переключается каждым нажатием) и бежит при выключенной опции Always Run; опцию клавиша не пишет ([ARC-RES-047 L2](../research/locomotion-evidence.md)). В `crash-site-area` реализовано (ARC-WORLD-020). P3 (0x4304C0) |
| Enter | Перейти к вводу broadcast/chat-сообщения; повторный Enter отправляет введённый текст. | Down: открывает broadcast; в окне количества вместо этого подтверждает quantity. M PDF 72–73 / печатные 72–73; K3 (0x54B5D0), U2 (0x566F00) |
| Space | Закрытие игровых интерфейсов и переключение Turn-Based/Real-Time согласно контексту оригинала. | Down: вызывается переключение TB; клавиша включает его только для активного PC ([RT4](../research/ledger/combat.md#real-time-combat-arc-combat-004)); прямой вызов не содержит общего close UI. В Fate Space закрывает этот экран. Не превращать в общий Confirm. В чужой TB-ход Space down тоже переключает TB ([CT6](../research/ledger/combat.md#turn-based-host-arc-combat-020)). Во вьюере AREA переключение TB реализовано (ARC-COMBAT-020), закрытие интерфейсов нет. K3 (0x54B5D0), U12 (0x56FE70), U13 (0x4B6D20) |
| A | Запустить последнее Active действие возле вращающегося окна. | Logical key down активирует первый recent-action слот, если нет text focus/main menu. Это не движение влево. K4 (0x57DC60) |
| C | Переключить лист/редактор текущего персонажа. | Logical key down вызывает character UI. K3 (0x54B5D0) |
| F | Переключить Fate Point Window. | Logical key down вызывает Fate UI; не Fit Camera. K3 (0x54B5D0) |
| I | Переключить инвентарь. | Logical key down вызывает inventory; не Inspect. K3 (0x54B5D0) |
| L | Переключить Logbook. | Logical key down: в оригинальном exe символы `l`/`L` вызывают переключение журнала для локального PC (строка [LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)); AREA viewer делает так же (ARC-UI-008). |
| O | Открыть Options. | Physical scancode up; при text focus не выполняется. В options O может закрывать окно. K1 (0x401560), U3 (0x546EE0) |
| R | Ready/Unready, режим Attack/Talk. | Logical key down переключает combat readiness; это не TB/RT и не Reset Scene. K3 (0x54B5D0) |
| S | Sleep/Wait UI. | Logical key down вызывает sleep UI. Это не движение вниз. M PDF 52–53 / печатные 52–53. K3 (0x54B5D0) |
| T | Переключить технические схематики. | Logical key down вызывает schematic UI: символы `t`/`T` открывают окно схем для локального PC как крафтера и зрителя (строка [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)); AREA viewer делает так же (ARC-TECHNOLOGY-005). K3 (0x54B5D0) |
| V | Показать информацию о версии. | По F0 обрабатывается только при cheat level >= 1. Для документированного original действия cheat flag не должен требоваться. K1 (0x401560) |
| W | Переключить World Map UI. | Logical key down вызывает world map. Это не движение вверх. AREA viewer в режиме карты делает так же на стартовой карте с ARC-UI-048 (диагностическая карта мира, [карта мира](../rendering/area-map-mode.md#world-map-arc-ui-048)). K3 (0x54B5D0) |
| Удерживать запятую при атаке | Прицельная атака в голову. | Выбирается scratch value 1 ([FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); cursor/art и combat semantics проверяются отдельно. M PDF 60–61 / печатные 60–61; K5 (0x54ED30) |
| Удерживать точку при атаке | Прицельная атака в руки. | Выбирается scratch value 2 (рука, [FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)). Какой cursor art выбирает K13 для значения 2, в exe не сверено; не выводить из него правило боя. K5 (0x54ED30), K13 (0x553990) |
| Удерживать слеш при атаке | Прицельная атака в ноги. | Выбирается scratch value 3 (нога, [FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); cursor art K13 для значения 3 не сверен. K5 (0x54ED30), K13 (0x553990) |

Дополнительные бинды, документированные в основном тексте, либо найденные только по адресам (F0):

| Ввод и контекст | Действие | Основание |
| --- | --- | --- |
| 1–9 и 0 верхнего ряда | Активировать соответствующий из десяти quick slots; 0 — десятый. Назначение slot через drag-and-drop; при необходимости начинается отдельный выбор цели. | Original-documented: M PDF 40–41 / печатная 41, Hot Key Bank. F0: scancode up, вне text focus, до режимного dispatch и после turn guard. K10 (0x54DE50), K11 (0x57F1D0) |
| Стрелки в игровом мире | Панорамировать изометрический вид, а не перемещать персонажа. | Original-documented: M PDF 174 / печатная 174. F0: held scancode polling, диагонали, разрешённые scrolling modes. P1 (0x401560, 0x401F50, 0x402010) |
| Home в игровом мире | Центрирование на PC; по адресам K2/K6 при уже центрированном PC в TB — на текущем участнике хода. | F0; оригинальная runtime-семантика дополнительного TB-case не проверена. K2 (0x54DE50), K6 (0x54EB60) |
| K / M | Skills / Spells pane toggle на scancode up, если main menu не открыт. | F1 (ARC-MAGIC-015, [SU14](../research/ledger/spell-runs.md#spell-ui-arc-magic-015)): отпускание M (0x32) переключает панель интерфейса на заклинания (0x54DBF0), K (0x25) — на навыки. M во вьюере AREA показывает и скрывает панель заклинаний (ARC-MAGIC-015); K не реализован. K3 (0x54B5D0) |
| E в TB combat UI | Продвинуть под-ход локального PC при допустимой анимации и без text focus. | Key-up (и кнопка конца хода): при ходе локального PC без анимации или только с анимацией цели 2 сразу следующий под-ход ([CT5](../research/ledger/combat.md#turn-based-host-arc-combat-020)); не Next Target. Во вьюере AREA реализовано для простаивающего героя (ARC-COMBAT-020). U5 (0x56F9B0) |
| Z / Home / стрелки в map UI | Z up переключает уровень карты; Home центрирует карту; стрелки прокручивают. | F0. Home/стрелки filter обрабатывает без pressed guard; отдельное held polling тоже существует. Это иной контекст, чем world camera. U1 (0x5618B0) |
| F6 | По адресу K2 — шестой broadcast index, подпись из mes ID 505. | F0; M/Q документируют F1–F5, значение F6 не установлено. Не придумывать шестую команду. K2 (0x54DE50) |
| F9 | В исследованных функциях и M A-10/Q binding не найден. | Отрицательный результат ограничен этими источниками; оригинальный runtime и доставка ОС не проверены. Кандидат diagnostic prefix, не доказанно свободная клавиша. |
| F10 | Compact/interface toggle на key-up, с fullscreen-условием в вызываемом helper. | F0, отсутствует в Quick Keys; нельзя объявлять обычным fullscreen hotkey или новым игровым binding без выбора baseline. K1 (0x401560), K9 (0x5578E0) |
| Tab | В text edit optional on_tab callback. | F0; global gameplay Tab mapping не найден. Не объявлять отсутствующим во всех оригинальных контекстах. U2 (0x566F00) |

## KM-03 — Мышь задаёт цель маршрута, а не направление каждого кадра

Клик по земле задаёт клетку назначения; персонаж идёт по игровому маршруту к этой точке. Он не телепортируется и не движется по WASD или вслед за перетаскиванием камеры. Route planner использует оригинальный world/grid/traversal contract и общий исполнитель движения; не подменяет проходимость sprite bounds или автоматически выведенной Godot navigation.

Original-documented: M PDF 54–55 / печатные 54–55. F0: MAIN LMB-down после world-window/picker guard выбирает move/run goal; LMB-up только сбрасывает локальный action guard и не отменяет маршрут. Повторное назначение той же клетки сохраняет действующий ground goal; другая клетка обновляет цель и отмечает путь для пересчёта. P2 (0x54DE50), P4 (0x433640)

Gamepad-правило завершения/отката шага при отпускании стика не становится правилом отпускания мыши. Клик продолжает действовать после отпускания кнопки. Бюджет/сегментация пути, объектные goal interrupts и конкретный результат маршрута при движущемся препятствии исследуются отдельно; текущий source study не подтверждает весь pathfinder или полный Crash Site traversal. См. [world-travel-runtime](world-travel-runtime.md#fr-nav-001) и [pathfinding study](../input/arcanum-pathfinding-study.md).

Приёмка: один click-down/up запускает достижение заданной клетки; отпускание не останавливает маршрут; повторный клик заменяет оставшуюся цель без скачка; тот же target не создаёт дублирующую очередь. Blocked/Unknown/outside admitted zone не превращаются в Open. Ограниченная диагностическая зона и отсутствие полного маршрутизатора обозначаются честно.

## KM-04 — Объект, режим и модификатор определяют действие

| Контекст / ввод | Требуемый результат | Источник |
| --- | --- | --- |
| Hover мышью | Оригинальные сведения в Message Window: имя/подходящие свойства NPC, предмета, scenery, portal/container. Не все scenery обязаны иметь текст. | M PDF 52–55 / печатные 53–55; документированное mouse-hover поведение; детали feedback/picking по адресам не объявляются полностью проверенными. |
| ЛКМ вне Combat Mode | Дверь — открыть/использовать; живой NPC — разговор; предмет — подобрать; сундук/труп — loot; земля — идти. Действие учитывает состояние и доступность цели. | M PDF 54–55 / печатная 54; выбор goals по типу P5 (0x54ED30). |
| ЛКМ в Combat Mode | Для допустимой живой non-follower цели — атака; состояние/faction/party и состояние объекта определяют допустимость. | M PDF 54–57 / печатные 55–57; P5 (0x54ED30). |
| Ctrl+ЛКМ по земле | Бег вместо обычной ходьбы в документированном случае. Бег в Combat Mode расходует fatigue по отдельным игровым правилам. | M PDF 54–55 / печатная 55, M188/Q; P2 (0x54DE50), P3 (0x4304C0). |
| Shift+ЛКМ по цели | Атаковать/действовать стоя на месте; не автоматически подходить. Ranged attack используется, если он доступен и требуется. По земле обычная move-ветка подавляется (F0). | M55, M188/Q; P2 (0x54DE50), P5 (0x54ED30); по живой цели в Combat Mode Shift даёт attempt-attack goal ([FA1](../research/ledger/combat.md#hero-attack-arc-combat-005)). Полное значение animation flag не устанавливается по одному числу. |
| Alt+ЛКМ в Combat Mode | Принудительная атака, включая подходящие scenery/двери/сундуки/Followers. | M55, M188/Q. P5 имеет разные ветви по типу и Alt+Shift; это не единый универсальный object action. Своего Follower клик атакует только с левым Alt ([FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)), мёртвую цель с Alt обыскивает ([FA3](../research/ledger/combat.md#hero-attack-arc-combat-005)). P5 (0x54ED30) |
| Alt+ЛКМ по предмету/трупу вне боя | Переместить соседний предмет/труп к позиции персонажа; персонаж должен стоять непосредственно рядом. Это не camera drag и не обещание mouse drag-and-drop жеста. | M PDF 54–57 / печатные 55–56, Q; вызывается object helper P5 (0x54ED30), F0. |
| Выбор Spell / Item / Skill | Если цель нужна, перейти в подходящий targeting mode; допустимый click применяет действие, ПКМ отменяет соответствующее нацеливание. Если цели не требуется, действие может выполняться сразу. | M PDF 50–51, 62–63, 76–77, 143 / печатные 50–51, 62–63, 76–77, 142–143; P6 (0x54DE50), U6 (0x57BC70). |
| Shift при выборе заклинания / применимого предмета | Немедленное применение к себе, только если действие допускает себя как цель. | M PDF 76–77 / печатная 76; U6 (0x57BC70), U7 (0x571AA0, 0x571C80, 0x571CB0). |
| Ctrl при Pick Lock / Disarm Trap | Подавить автоматический выбор более умелого Follower; использовать своего персонажа. | M PDF 70–71 / печатная 71; U7 (0x571AA0, 0x571C80, 0x571CB0), U15 (0x57A320, 0x57A5E0, 0x57A620). Не general modifier для диагностики. |
| ПКМ по портрету Follower | Его командное меню; команды Walk/Attack переходят в выбор цели; ПКМ отменяет меню/нацеливание. Недоступные действия не активируются. | M PDF 74–75 / печатная 75; U8 (0x56A9D0, 0x56AFD0, 0x56B0F0, 0x56B110, 0x56B180). |
| Town Map ЛКМ / ПКМ | Добавить / убрать waypoint; отдельная кнопка запускает маршрут по waypoints. | M PDF 48–49 / печатная 48. Не переносить это правило на обычный мир. U1 (0x5618B0) |

ПКМ не является универсальным Cancel для всего мира. В MAIN на RMB-up (F0) выполняется отдельная, условно допустимая логика interrupts, combat/concealment и ориентации к указателю; остальные режимы имеют другие handlers. Нельзя заменить все эти действия одной отменой пути. P2 (0x54DE50), P6 (0x54DE50)

В режиме TARGETING для Spell/Skill/Item/Follower действие часто выполняется на LMB-up, тогда как обычная ходьба/interaction — на down. TIG генерирует повторный down после интервала более 250 ms и repeat flag; основной world handler не проверяет repeat. Отдельного double-click сообщения в перечисленном наборе нет. Это данные F0 (часть TIG без сопоставленных адресов), не доказательство retail timing; не добавлять двойному клику новую игровую семантику без отдельного требования. P7, T4

## KM-05 — Оригинальная камера не использует ЛКМ для перетаскивания

Стрелки панорамируют игровой вид; мышь выбирает перемещение и объекты. По адресам P1 (F0) есть также edge-scroll с диагоналями в углах, который останавливается при неактивном приложении. Порог 8 px является наблюдением F0, а не доказанным modern-HiDPI размером зоны. Его реальные границы/скорость и original baseline требуют отдельной сверки. P1 (0x401560, 0x401F50, 0x402010)

В исследованном world handler MMB camera drag и wheel world zoom не назначены. Колесо используется UI-scrollbar; middle-up может иметь действия в иных окнах/слайдах. Поэтому ранее предложенное MMB-перетаскивание не принято как оригинальное управление. Обычный wheel/+/- world zoom и left camera drag из диагностического Viewer не должны автоматически переходить в keyboard/mouse compatibility. Будущие дополнительные camera actions допустимы через отдельно выбранную диагностическую поверхность или отдельное новое product решение. Gamepad LT/RT остаются принятым отдельным zoom-контрактом. U9 (0x569770), U14 (0x580B10), T4

Приёмка: ЛКМ по миру не перетаскивает камеру; стрелки/Home сохраняют допустимый контекст, не приобретают gamepad-инверсию; map UI и world camera различаются; обработка mouse button не заменяется безусловным Confirm/Cancel.

## KM-06 — Контекстный UI владеет событием

Маршрут по адресам (F0): TIG mouse-button обработка и оконные фильтры → scrollbar/hotkey/interface filter → dialog/turn/text guards → режимный world handler (0x54DE50) → оставшиеся global keys главного цикла (0x401560). Поглощённое кнопкой или оконным фильтром событие не доходит до main loop; но один только early return из обработчика событий игрового интерфейса (0x54DE50) не запрещает последующие global actions главного цикла. Это поведенческая спецификация, не инструкция копировать очередь сообщений оригинала. U10, T5, T6 (0x54B5D0), K1 (0x401560)

| Контекст | Правило по адресам (F0) | Граница требования |
| --- | --- | --- |
| Текстовое поле | Home/стрелки/Insert/Delete/Backspace/Tab/Return обрабатываются на key-down; символы приходят отдельным text event. Неизвестная клавиша может вернуть false. | Не превращать набор текста в actor intent. Не описывать focus как абсолютное поглощение всех клавиш. U2 (0x566F00) |
| Save/Load menu | Key-up стрелки выбирают запись, Delete/Backspace удаляют, Return/KP Enter подтверждают, Escape закрывает. | Это действия save UI, не Cancel/Confirm/WorldMovement из общей lab раскладки. Точные original/default semantics подтверждаются отдельно. U3 (0x546EE0) |
| Main menu | Logical key-down может активировать видимую кнопку через её accelerator; Enter имеет отдельные пути. Escape в отдельном цикле меню обрабатывается на release. | Не выдумывать буквенные accelerators без выбранных ресурсов/локализации. Routing скрытого игрового интерфейса при открытом меню в exe не сверен. U3 (0x546EE0) |
| Диалог | В проверенном tc-handler выбор ответа по ЛКМ/позиции; прямой выбор цифрой/буквой там не найден. | Не вводить 1–0 как выбор ответа только потому, что так делает другой RPG или gamepad mock. Исследование не исключает другие неизвестные original ветви. U4 (0x4C9A10) |
| Журнал/character/inventory | Фильтры используют кнопки/мышь и общий big-window lock; это не blanket lock всего ввода. U16 (0x5572D0, 0x557330) | L из M/Q подтверждён в exe ([LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)). Сохранить оригинальные mouse inventory/equipment/use/drop/throw/quantity flows. U11 (0x53F0E0, 0x53F2F0, 0x53F350), U7 (0x571AA0, 0x571C80, 0x571CB0) |
| Чужой TB-ход | World handler игнорирует команды; ISO filter допускает Space down. A recent action фильтруется раньше, а часть main globals имеет свои guards. | Нельзя заменить это обобщением «вообще все клавиши отключены». Проверять каждый route/context и доступность действия. K1 (0x401560), T6 (0x54B5D0), K4 (0x57DC60) |
| Slides | Key-up заканчивает слайд; Escape может прекратить последовательность. Mouse releases тоже участвуют. | Это отдельный media контекст. Не переносить в gameplay. U9 (0x569770) |

Global Escape/F7/F8/F10/F11 главного цикла не имеют общего text-focus guard (F0); O и последующие cheat shortcuts имеют. В FOLLOWER mode F1–F6/Home стоят под положительным text-focus условием, в MAIN/SPELL/SKILL — под отрицательным. Это отмеченная F0-аномалия, которую нельзя молча нормализовать или считать уже проверенным оригинальным правилом. K1 (0x401560), K2 (0x54DE50)

## KM-07 — Переносимость key identity и модификаторов

Ввод по адресам (F0) хранит одновременно logical key, physical scancode и pressed; текстовый ввод идёт отдельно. W/S/R/T/C/F/I/A проверяются как logical keys (буквы `c`/`C`, `l`/`L` и `t`/`T` приходят как символьные сообщения, [CS1](../research/ledger/character.md#character-sheet-arc-ui-007), [LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008), [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)); ряд цифр, K/M, глобальные F-keys и held arrows — по scancode. Буквы A, C, F, I, L, R, S, T, W приходят только из WM_CHAR: Shift даёт заглавную (то же действие), Ctrl — управляющий код вне таблицы, Alt — WM_SYSCHAR, который игра не читает; поэтому с Ctrl/Alt (и Command на Mac) эти клавиши ничего не делают ([WK1–WK4](../research/ledger/ui-audio.md#window-key-modifiers-arc-input-004), ARC-INPUT-004). Модификаторы читаются как состояние удерживаемых клавиш, а не из полей сообщения: клик по цели в бою принимает левый или правый Shift/Alt ([FA1–FA3](../research/ledger/combat.md#hero-attack-arc-combat-005)). T1

Независимый adapter сохраняет смысл выбранного compatibility baseline и не смешивает физический код с локализованным текстовым символом. Документ не подтверждает поведение всех русских/английских/нестандартных раскладок retail. Ctrl не заменяется на Command автоматически. Проверить real native delivery на macOS, функцию NumLock при разных клавиатурах, function row/Fn, обе стороны Ctrl/Shift/Alt, ввод текста, потерю фокуса и отсутствие залипания. Для действий, где функции оригинала проверяют LeftCtrl/LeftAlt отдельно (проверка своего Follower читает только левый Alt, [FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)), эту асимметрию сохранять; непроверенные случаи остаются F0 до сверки в exe. Platform convenience не должна молча менять combat или follower override.

## KM-08 — Расхождения и открытые вопросы

| Вопрос | Доказательство | Требование к следующей реализации/проверке |
| --- | --- | --- |
| L в оригинальном exe | M188/Q задают Logbook; таблица символов обработчика клавиш в exe вызывает журнал на `l`/`L` ([LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)). | Решено ARC-UI-008: L переключает журнал; отсутствие ветки в чтении F0 не считать доказательством отсутствия клавиши в оригинале. |
| V находится под cheat gate | M189/Q задают ordinary version info; главный цикл K1 по F0 требует cheat >= 1; начальное значение cheat level T2 в exe не сверено. K1 (0x401560), T2 (0x401000) | Ordinary version action не зависит от cheat mode; version string нового runtime — собственная информация, не скопированная строка версии оригинала. |
| Space и закрытие интерфейсов | M188/Q явно описывают close UI + TB/RT; вызываемая функция U13 меняет настройку TB без общего close UI вызова ([RT4](../research/ledger/combat.md#real-time-combat-arc-combat-004)). K3 (0x54B5D0), U13 (0x4B6D20) | Отдельно проверить Space в inventory/character/Fate/других экранах и в мире; не вывести полноту из одного toggle call. |
| F8: fixed auto-slot против save-list helper | Решено чтением exe (ARC-UI-010, [SL2](../research/ledger/ui-audio.md#save-list-arc-ui-010), два читателя): helper сортирует по времени изменения без закрепления auto-save, F8 грузит самое новое сохранение; M188/Q («auto-slot») верно только когда auto-save самое новое. K8 (0x403850) | Наблюдение в запущенной игре (F4) не делалось. |
| Called-shot точка/слеш | M189/Q: руки/ноги; exe хранит 2 (рука) и 3 (нога) ([FA4](../research/ledger/combat.md#hero-attack-arc-combat-005)); выбор cursor art K13 не сверен. Scratch values 2/3 не устанавливают итоговый damage semantics. K5 (0x54ED30), K13 (0x553990) | Исследовать эффект боя/ART mapping отдельно; не переставлять исходную раскладку на основании непроверенного cursor art. |
| F6/недокументированные K/M/E/Home/F10 | Есть handlers по адресам (F0), но не все действия входят в original Quick Keys. | Не потерять source inventory и не объявлять complete retail verification. F6 label остаётся неизвестным. |
| NumLock и Always Run | Original docs задают эффективный постоянный бег; функции по адресам имеют menu option, Num modifier и Ctrl override. P3 (0x4304C0) | Таблица истинности и темп установлены по оригиналу ([ARC-RES-047](../research/locomotion-evidence.md) L1–L8): опция включена — бег, Ctrl — шаг; выключена — бег при Ctrl или Num Lock; клик по объекту всегда бегом. Реализовано в `crash-site-area` ([ARC-WORLD-020](../world/gait-runtime.md)); Shift и отложенные goals не исследованы. |
| Полный stop/interrupt/path lifecycle | Ground-goal update и LMB-up известны; общее прерывание требует состояние/priority и анимационные ветви. P2 (0x54DE50), P4 (0x433640) | Не обещать универсальную RMB отмену/идеальный маршрутизатор; конкретные cases отдельно. |
| Удобства вне оригинала и cheats | LeftAlt highlight и меню по clock в exe не подтверждены. Cheat/profile/lighting/gamma keys не ordinary binding. K1 (0x401560), K17 (0x54B5D0), T2 (0x401000) | Не включать автоматически в original mapping. RB-held highlighting остаётся gamepad policy. |
| Modifier/text-focus source anomalies | FOLLOWER focus inversion; global handlers не имеют единого focus guard; menu keyboard routing в exe не сверен. K1 (0x401560), K2 (0x54DE50), U3 (0x546EE0) | Отдельное baseline decision/runtime case, не механическое перенесение подозрительных условий. |

Cheat-only inventory, чтобы не принять его за игровой input: D/Y/Shift+4 при level >=2; H/N/grave/P/F11 при >=3; V/E/X/U/brackets/apostrophe/backslash/G, Ctrl+G, Shift+G и Ctrl+KP7/8/9 при >=1. Ctrl+LeftAlt при открытии Logbook активирует отдельную скрытую ветку. Эти режимы не становятся обязательной production функциональностью нового runtime. K1 (0x401560), U11 (0x53F0E0, 0x53F2F0, 0x53F350)

## KM-09 — Диагностический слой: варианты после сверки

**Owner-policy:** диагностические команды требуют дополнительного слоя с модификаторами. Современные дополнительные действия Viewer являются диагностическими расширениями, а не восстановленными original shortcuts.

**Выбор 2026-10-04:** владелец выбрал вариант B (палитра, вход Ctrl+Shift+F9) для bounded реализации. **Статус binding:** реализован в Terrain Viewer WORLD задачей ARC-INPUT-001, контракт и доказательства в [diagnostic-palette.md](../input/diagnostic-palette.md). Software-проверки (synthetic Godot events) не подтверждают native доставку macOS и физическую клавиатуру; это подтвердила ручная проверка владельца ARC-INPUT-002 (PASS 2026-10-17, [результат](../input/diagnostic-palette.md#result-2026-10-17)). Таблица вариантов ниже сохранена как исследование.

Модификатор не делает любую занятую клавишу свободной: handlers F1–F6 по адресам K2 (F0) часто не проверяют Ctrl/Shift. Поэтому Ctrl+Shift+F3 всё равно способен вызвать original broadcast на key-up, если diagnostic handler перехватывает только down. Alt особенно неудобен как общий prefix: он уже влияет на original force-attack/drag/targeting, а левый Alt ещё разрешает атаку своего Follower ([FA2](../research/ledger/combat.md#hero-attack-arc-combat-005)). Command/Option не рассматриваются как замена original Ctrl/Alt; платформенные варианты требуют собственной проверки.

| Вариант | Как работает | Плюсы / ограничения |
| --- | --- | --- |
| A. Прямые сочетания | В явно включённом diagnostic host Ctrl+Shift+F3 — overlay; Ctrl+Shift+R — reset; Ctrl+Shift+F — fit; Ctrl+Shift+L — reload; Ctrl+Shift+wheel — diagnostic zoom над миром. | Быстро и похоже на текущие имена. Эти base keys уже заняты; chord является намеренным diagnostic override, а не найденной свободной original комбинацией. Нужно полностью подавить original обработку всего lifetime и не перехватывать UI-wheel/набор текста. Больше collision cases. |
| B. Диагностическая палитра | Кандидат Ctrl+Shift+F9 открывает отдельную palette/menu; там checkbox проходимости, fit/reset/reload, zoom presets 1.00/2.00 и краткая диагностика. | Рекомендуемый начальный вариант: один вход в слой, функции видны, обычные F1–F5/R/F/L сохранены. F9 не найден в исследованной карте, но retail/OS доставку ещё проверять. Дополнительное действие для toggle, зато меньше зарезервированных chords. |
| C. Временный удерживаемый слой | Ctrl+Shift+F9 удерживается как prefix; 1 — overlay, 2 — fit, 3 — reset, 4 — reload, 5 — details; отпускание prefix возвращает original keyboard route. | Быстрые повторные операции после входа, явно видимый активный слой. Нужно удерживать несколько клавиш, больше state/focus/release случаев; функция 1–0 не должна активировать original quick slot после выхода из слоя. Требуется проверка доступности chord на реальной клавиатуре. |

Рекомендация B сохраняет обычную original раскладку и уменьшает число overrides. Она не выбирает окончательную кнопку, не создаёт shortcut в коде и не изменяет существующие контракты задач. Palette доступна как инструмент диагностики; production keyboard/mouse UI остаётся оригинальным. Открытие palette подчиняется явно выбранной modal/pause policy host, не вводит неявное изменение игрового времени. Сам passability overlay остаётся read-only и не меняет actor/route/camera/time.

Обязательные правила любой реализации слоя:

1. Bare modifier не запускает диагностическую команду; оригинальные Ctrl/Shift/Alt+мышь сохраняются вне активного diagnostic контекста.
2. Chord распознаётся раньше original mapping; владелец события сохраняется до отпускания основной клавиши, даже если модификатор отпущен первым. Key-up не должен утечь в broadcast, quick slot или UI toggle.
3. Только одно действие на полный chord; echo/повторный down не делает несколько Reset/Reload/Toggle.
4. При text entry, gameplay modal или недопустимом фокусе слой не присваивает себе набор текста/игровое действие. Для самой palette определяется отдельный modal context.
5. Losing focus, открытие/закрытие слоя и смена input method очищают/изолируют held state; возвращение не создаёт отложенную команду. Пока кнопки не отпущены, нельзя трактовать их как новое намерение мира.
6. Все диагностические actions доступны из palette/settings без необходимости иметь полную F-клавиатуру. Gamepad входит через свою UI-поверхность, без новых обязательных игровых кнопок.
7. Mapping слоя настраивается отдельно от original KBM и gamepad presets. Shared physical shortcut не должен silently override gameplay или system shortcut.
8. Collision проверяется по комбинации, контексту, down/up, live modifiers и повтору, а не только по имени клавиши. Проверка на macOS и реальной клавиатуре остаётся самостоятельным gate.

## KM-10 — Текущие расхождения Viewer и границы работы

На SW-срезе [GodotInputAdapter](https://github.com/Simarglok/arcanum-steamwoven/blob/db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c/src/Arcanum.Input.Godot/GodotInputAdapter.cs) создаёт WASD actor vector, I=Inspect, C=Context, Enter/Space=Confirm, F1=Settings и W/S=menu navigation. [TerrainViewerWorld](https://github.com/Simarglok/arcanum-steamwoven/blob/db8bcd0ff48983b4e90a4ff30a66a9ad48d79e7c/tools/terrain-viewer/Godot/TerrainViewerWorld.cs) использует LMB drag, R reset, F fit, L reload и wheel/+/- zoom. Это диагностическая схема; она не является original controls и не должна стать production default. Новый кандидат нужно проверить отдельно: данный инвентарь не заявляет live состояние всех веток/сборок.

**После ARC-INPUT-001:** в WORLD-режимах Viewer клавиатура больше не создаёт actor vector, F1/R/F/L, I/C/Space и wheel/+/-/LMB drag не несут диагностических действий; оригинальные клавиши зарезервированы как ещё не реализованные, стрелки панорамируют с фиксированным направлением, а Fit/Reset scene/Reload/exact zoom перенесены в палитру ([diagnostic-palette.md](../input/diagnostic-palette.md)). Лабораторная раскладка GodotInputAdapter осталась только для Gamepad Lab и standalone-режимов. Команды Followers, quick slots и игровые экраны по-прежнему не реализованы.

**После ARC-WORLD-007:** в `crash-site-area` нажатие LMB по земле (не поглощённое HUD/палитрой) задаёт клетку-цель маршрута ([click route](../world/click-route-runtime.md#viewer-binding-stage-2)); отпускание ничего не отменяет, повтор той же клетки сохраняет цель, стик сразу перехватывает движение (KM-03). Курсор и превью пути не реализованы; физическая мышь проверена только ручной проверкой владельца ARC-INPUT-002 (2026-10-17: LMB по земле задаёт цель, [результат](../input/diagnostic-palette.md#result-2026-10-17)); бег (Ctrl+ЛКМ, Num Lock, опция Always Run) добавлен в ARC-WORLD-020 ([gait runtime](../world/gait-runtime.md)).

Нужны отдельные реализации original KBM adapter/context routing, click-to-move goal + bounded route planning, доступных оригинальных UI actions и выбранного diagnostic layer. Они используют общую simulation и существующие gamepad contracts, не требуют создания второй игровой симуляции. Общая лабораторная клавиатура для synthetic controller tests не подтверждает оригинальный keyboard mapping. Tests, жёстко требующие WASD actor movement, нельзя использовать как будущий compatibility gate.

Этот документ не авторизует менять контракты задач, запускать разработку, выполнять cleanup чужих worktrees, выпуск или merge. Статусы существующих задач и их прежние acceptance evidence не переписываются результатом исследования. Задачи ставит владелец.

## KM-11 — Приёмочные сценарии для будущих задач

| Группа | Обязательная проверка |
| --- | --- |
| Раскладка | W — map, S — sleep/wait, A — active recent action, R — ready/unready; ни одна WASD-клавиша не меняет actor position. C/F/I/L/O/T/V и соответствующие toggles имеют исходные действия. |
| Quick actions | 1–0 активируют ровно свой slot; target-required action входит в targeting; A повторяет designated active action. Drag назначает/удаляет слот; controller template не сокращает original bank. |
| Follower | F1–F5 соответствуют M/Q, цель выбирается по original pointer context. Обычный F3 работает даже если debug overlay существует. F6 не получает выдуманной команды. |
| Movement | Click-down/up задаёт путь и продолжает его; новая клетка заменяет цель; Shift-ground не начинает обычную ходьбу; Blocked/Unknown не игнорируются. Мышиная цель не отменяется нейтралью gamepad-style surrogate keyboard. |
| Mouse actions | Door/NPC/item/container/corpse, readiness и modifiers выбирают именно документированные действия; Alt перенос соседнего трупа не требует mouse-camera drag. UI click не дублируется в мире. |
| Camera | Стрелки панорамируют view; click задаёт actor goal; Home/map context и edge-scroll проверяются отдельно. Изменение gamepad inversion не меняет arrow direction. |
| Combat | Space в мире меняет TB/RT, R — readiness; Space в интерфейсах проверяется отдельно. Called shots: comma/head, period/arms, slash/legs, без доверия к конфликтующим cursor comments. |
| Saves | F7 пишет выбранный auto-slot; F8 грузит самое новое сохранение (SL2, ARC-UI-010). Запрет save на чужом ходе/у мёртвого PC проверяется по baseline; не заявлять original save compatibility. |
| Input ownership | Text entry, modal UI, follower targeting, чужой turn, lose/refocus и переход KBM/gamepad не вызывают двойного действия. Original global guards фиксируются отдельно, без blanket запретов. |
| Diagnostics | У выбранного layer есть проверки modifier-first/key-first release, echo, losing focus, modal/text entry, original F3 broadcast и slot 1 после ухода из слоя. Toggle overlay не меняет simulation state. |
| Platform | Реальная клавиатура/мышь на macOS, function row, доступные modifier sides/NumLock и выбранные раскладки. Synthetic InputEvent tests не доказывают physical delivery/удобство или retail parity. |

Приёмка разделяется на (1) source/document validation; (2) независимые synthetic command/context tests; (3) native Godot UI/export и реальные устройства; (4) original-runtime comparison для спорных cases. Этот документ закрывает только исследование и сверку источников, не пункты 2–4. Каждая implementation карточка задаёт поддерживаемый subset, source references и конкретные проверки; недоступные части не объявляются восстановленными.

## Покрытие исследования и остаток

Просмотрены центральные keyboard/mouse routes главного цикла (0x401000–0x402010, 4 функции) и игрового интерфейса (0x549B70–0x557CF0, 160 функций), TIG message/kb/mouse/window/button (адреса не сопоставлены), выбранные ground goals/interrupt/path entry, broadcast и contextual consumers hotkey, dialog/tc, follower, combat, Fate, world map, textedit, mainmenu, logbook, character, inventory, item, skill, spell, schematic, written, sleep, slide и compact UI. Поиск scancodes/keycodes/modifiers охватывал функции интерфейса и игровой логики; это не утверждение, что каждая функция этих групп или multiplayer/editor исследована полностью.

Остаются: original executable timing/auto-repeat/полный путь; локализованные broadcast/rules и меню; F6; exact called-shot combat/ART association; Space across all windows; F8 baseline; Shift-ветка бега и отложенные goals (таблица NumLock/AlwaysRun/Ctrl установлена в ARC-RES-047); focus anomalies; платформенная доставка ввода. Отсутствие ветки в чтении F0 не доказывает отсутствие оригинальной функции. Новые source/runtime findings обновляют этот один тематический документ; отдельные history/review/recheck файлы в requirements не нужны.

## Коды локаторов

K, P, U и T — коды мест в retail `Arcanum.exe` 1.0.7.4; адрес функции указан в скобках рядом с кодом. Коды без адреса (P7, T1, T3, T4, T5, U10) относятся к библиотеке TIG внутри exe (очередь сообщений, клавиатура, мышь, окна); их адреса ещё не сопоставлены. Основание всех кодов — чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в журнале сверки; подтверждённые места названы строками журнала (LB1, CS1, RT4, FA1–FA4).

