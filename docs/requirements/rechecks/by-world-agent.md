# Финальная перепроверка правок: мир, сцена и боёвка

Дата: 2026-10-03. Проверка требований read-only по Steamwoven `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae` и CE `a7ff41b300ef712f0e7d088183a3d08957110cdd`. Хэши трёх закреплённых входов совпали с переданными при назначении:

| Файл | SHA-256 |
| --- | --- |
| [`scene.md`](../scene.md) | `6a867e58df72ee8ab774ceae9ef6ebc744d8ed2ebe061b7614195f4f61b3f95c` |
| [`combat-magic-tech.md`](../combat-magic-tech.md) | `591a3aed66800f292c260e7a7611356ee86a6aaddd0f10f5690819f635b280ff` |
| [`world-travel-runtime.md`](../world-travel-runtime.md) | `32538b533d8500732d72822f48ce554fd946559a4dd6cd0d3240947ca5e357c8` |

Дополнительно для сверки матрицы прочитаны [`coverage.md`](../coverage.md), SHA-256 `4f7420efd5113afae1aab71d92e33d305efd9be876213eec099b2b8e184f8b26`, и [`source-coverage.md`](../source-coverage.md), SHA-256 `f0d2286cc565dab97e95333c3c2ffdfb76abf24549c3811e6678109045741791`.

## Результат по прошлым замечаниям

| Finding | Результат | Проверка |
| --- | --- | --- |
| RV-WA-001 | **PASS** | FR-SCENE-013 теперь владеет только связью scene blocker с directed-step assessment; полный маршрут и его исполнение явно отнесены к FR-NAV-002/004. Разделение соответствует выбранным `path_make` и animation-step ветвям. |
| RV-WA-002 | **PASS с остаточным уточнением** | В FR-NAV-002 есть отдельная приёмка slippery-step/knockdown и включения/снятия `OF_WADING`; добавлено, что fire/light — условные costs, а не hard blockers. Но отдельный acceptance case для path flags, активирующих обход огня/разной освещённости, по-прежнему не сформулирован. См. R-WA-03 ниже, если эта условная политика остаётся в объёме. |
| RV-WA-003 | **PASS** | Добавлен FR-COMBAT-013 для kill summaries и injury history. Он отделяет полезный UI/счётчики от противоречивой injury-функции CE и явно говорит, что это anomaly исходника, а не целевое правило или доказательство поведения оригинала. FR-SOCIAL-008 сохраняет границу владения вкладками журнала. |
| RV-WA-004 | **PASS** | FR-SCENE-009 теперь ограничен представлением окна в стеновом проёме; traversal отдан FR-SCENE-016 и FR-NAV-003. Приёмка окна больше не дублируется. |
| RV-WA-005 | **PASS** | Имя исправлено на `path_make` в FR-SCENE-013 и source inventory. Закреплённый CE объявляет функцию именно так (`path.c:63–138`). |
| RV-WA-006 | **PASS по смыслу; inventory stale** | FR-SCENE-010 теперь предупреждает о пустых roof DIF load/save и `is_modified == false`. Однако source inventory всё ещё перечисляет `sector_roof_list.c` среди непросмотренных, хотя требование цитирует его строки 87–123. См. R-WA-01. |
| RV-WA-007 | **PASS** | FR-SCENE-006 явно называет два дерева synthetic fixture и отрицает, что они входят в пять подтверждённых оригинальных resource records. |
| RV-WA-008 | **PASS** | FR-NAV-005 больше не приписывает `ai_can_see` прямой универсальный lighting gate; отмечена только возможная косвенная связь через конкретные stat/effect rules. |

### FR-COMBAT-013 и изменённое покрытие

FR-COMBAT-013 опирается на согласованные CE-фрагменты: kill writer/getter и caller при смерти NPC; вкладку/форматтер журнала; вызовы injury writer из critical combat outcomes. В теле `logbook_add_injury` действительно стоит `if (obj_type_is_critter(type)) return;` перед последующими self/PC branches. Это противоречит комментарию про injuries inflicted by critters и не позволяет заключить по CE, что такая attribution работает. Напротив, kill-counter ветвь и UI для неё source-confirmed в выбранных диапазонах.

Предположение «статистика и история травм переживают save/load» сформулировано как целевое поведение раздела, не как утверждение о CE или retail parity; сам save payload этим источниковым проходом не проверялся. В тексте указана persistence как зависимость. Для последующего связывания достаточно сохранить эту границу и не переводить требование в статус источниково подтверждённого save поведения.

В coverage table есть строка FR-COMBAT-013 со статусом `Нет` по четырём осям. Автоматическая сверка заголовков требований с ссылочными строками матрицы дала 183 уникальных ID в исходных разделах и 183 уникальных строки в coverage; отсутствующих/лишних ID и дублирующихся ID нет. Все локальные file/HTML-anchor targets строк матрицы существуют. Добавленный FR-COMBAT-013 не потерян и ведёт к `#fr-combat-013`.

Ограниченный FX-пункт FR-MAGIC-007 также сверен: текст говорит об executor/overlay infrastructure `animfx_add`, но явно оставляет состав FX, parser и полный caller/lifecycle неаудированными. Он не объявлен полным покрытием spell/VFX. Матрица MAGIC-007 остаётся `Нет` по всем четырём осям; `source-coverage.md` отражает один выбранный диапазон `animfx.c`.

## Оставшиеся замечания

### R-WA-01 — Обновить границу чтения source inventory

**Приоритет: P2 для целостности свидетельств.** В [`scene.md`](../scene.md) строка 111 уже цитирует `sector_roof_list.c:87–123` и правильно отмечает no-op DIF/stub, но в строке 223 тот же файл указан как не просмотренный. Изменить inventory на «точечно просмотрены `is_modified`, `load_with_dif`, `save_with_dif` (87–123); остальной serializer не аудирован» либо явно отличить дополнительную проверку от диапазона доменного автора. Не удалять остаток о непросмотренной полной сериализации.

В [`combat-magic-tech.md`](../combat-magic-tech.md) FR-COMBAT-013 и FR-MAGIC-007 ссылаются на прочитанные `logbook.c`, `logbook_ui.c`, caller `critter.c:684–720` и `animfx.c:291–420`, но раздел «Граница чтения CE» заканчивается без этих строк/модулей. Добавить только выбранные диапазоны и оставить parser/save remainder непроверенным; `source-coverage.md` уже показывает доменные ссылки для `logbook.c`, `logbook_ui.c` и `animfx.c`. Сами FR и ссылки корректны, несогласован именно inventory внутри отчёта.

### R-WA-02 — Закрепить точечное чтение окна в scene inventory

**Приоритет: P3.** FR-SCENE-009 теперь принимает anchor/слой/перекрытие synthetic wall opening, но его CE paragraph ссылается только на `object_calc_traversal_cost_func` и traversal flags. Общий объектный draw path подтверждает вывод объектов, но отдельный window-piece ART/resource association этим не доказан. Сохранить текущую продуктовую synthetic acceptance и добавить в открытые вопросы, что выбранный оригинальный window piece/ART ещё не установлен; не описывать synthetic fixture как восстановленную сцену из данных.

### R-WA-03 — Приёмка условной стоимости огня и света

**Приоритет: P3, зависит от принятого объёма.** `path.c:477–495` содержит отдельные flag-gated costs для огня и изменения освещённости. FR-NAV-002 теперь верно говорит, что это не hard blockers, но не задаёт scenario для соответствующих flags. Если эти costs входят в целевой movement contract, добавьте сравнение выбора двух путей при выключенном/включённом flag. Если пока не входят, зафиксируйте их как изученную CE-ветвь вне принятого продуктового поведения.

Эти пункты ограничены источниковым учётом и полнотой acceptance; они не опровергают исправление восьми исходных замечаний и не меняют вывод FR-COMBAT-013.

## Что перепроверено и пределы

В требованиях непосредственно перечитаны FR-SCENE-006/009/010/013/016 и scene source inventory; FR-NAV-002/005; FR-COMBAT-013, FR-MAGIC-007 и combat source inventory. Перепроверена FR-SOCIAL-008 граница журнала. По матрице считаны все FR headings, все 183 link rows, дубликаты и наличие локальных anchors. В `source-coverage.md` проверены строки `animfx.c`, `logbook.c` и `logbook_ui.c`.

В закреплённом CE повторно прочитаны: `path.c:63–138, 462–495`; `anim.c:4737–4764, 11523–11551`; `object.c:2224–2295, 2962–3112, 3152–3256, 5194–5251`; `sector_roof_list.c:87–123`; `logbook.c:18–96, 103–145`; `critter.c:684–720`; `combat.c:1598–1671`; `logbook_ui.c:1274–1319, 1609–1683`; `animfx.c:291–420`. Выбранные диапазоны подтвердили описание кода; они не образуют полный аудит этих модулей.

Не проверялись оригинальный executable/runtime, игровые ресурсы, конкретный window ART или cave/interior entrance records. Не выполнялись builds, запуски или новые acceptance tests. Вывод ограничен source review закреплённого CE и состоянием Steamwoven baseline; наличие CE-ветви не доказывает, что она совпадает с оригинальной игрой.

## Addendum — bounded recheck residual R-WA-01..03 (2026-10-03)

Повторно проверена только редакция трёх назначенных входов; их SHA-256 совпали с переданными при назначении:

| Файл | SHA-256 |
| --- | --- |
| [`scene.md`](../scene.md) | `08313f3f9511ff202f3ff14eb2d44cae549a4a7481b11adb2eb75392ae9c8918` |
| [`combat-magic-tech.md`](../combat-magic-tech.md) | `721d4fe6c1ff7b0f12b81ee2f38d98dd27ac31da4b36a1628d480b9fdcbe0bf8` |
| [`world-travel-runtime.md`](../world-travel-runtime.md) | `be5e9a6684855ca6b48c8f8f797e4ed70cb0beab23e0749aafc07f4d001aaf59` |

| Residual | Итог | Основание и предел |
| --- | --- | --- |
| R-WA-01 — source inventory сцены и боёвки | **PASS** | В `scene.md` добавлено отдельное supplemental чтение `sector_roof_list.c:87–123`; оно ограничено `is_modified`, `load_with_dif` и `save_with_dif`, а полный roof-section serializer оставлен неаудированным. В `combat-magic-tech.md` инвентаризированы выбранные диапазоны `logbook.c`, callers в `critter.c`/`combat.c`, журнал UI и FX attachment infrastructure; injury guard противоречию исходника не приписывается успешная работа. Полнота injury lifecycle, save payload и FX parser/caller/lifecycle явно остаётся открытой. |
| R-WA-02 — оригинальная ассоциация окна | **PASS** | FR-SCENE-009 называет fixture синтетическим и прямо фиксирует, что оригинальные window piece и ART/resource association не установлены; synthetic acceptance не объявлена подтверждённым рендером оригинального окна. |
| R-WA-03 — условные path costs | **PASS** | В FR-NAV-002 предложен будущий сценарий с парой synthetic маршрутов и переключением соответствующего path flag при неизменных остальных условиях. Он проверяет применимую стоимость и выбор пути, но не превращает клетки в hard blockers и не утверждает универсальную actor policy или результат retail runtime. |

Хэши и перечисленные формулировки проверены непосредственно в трёх требованиях. Coverage headings/строки не менялись по сообщённой границе задачи; полная матрица и исходники повторно не аудировались. Не запускались игра, builds или новые acceptance tests. Эта addendum фиксирует только устранение трёх редакционных/контрактных остатков в текущих source-research документах; она не подтверждает поведение оригинального runtime и не меняет выводы о неполноте исследования.
