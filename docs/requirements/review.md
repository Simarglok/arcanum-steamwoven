# Проверка проекта функционального реестра

**Статус: draft для аппрува владельцем.** Исследование начато 2026-10-02; перекрёстное ревью и повторные проверки выполнялись 2026-10-03. Каталог содержит 183 группы требований в 10 доменах. Это ревью текста и его доказательств, не приёмка реализации всей игры.

## Метод и разделение ответственности

Три субагента GPT-6 Luna с effort Max исследовали независимые наборы систем. Родительский агент интегрировал результаты, подготовил раздел данных/product goals, проверил спорные source-ветви и исправил замечания. Затем каждый субагент проверил документы другого автора:

| Ревьюер | Независимо проверенные документы | Первое ревью | Повторная проверка исправленного варианта |
| --- | --- | --- | --- |
| World | scene; combat-magic-tech; world-travel-runtime | [8 замечаний](reviews/by-world-agent.md) | [Точный кандидат](rechecks/by-world-agent.md) |
| Lighting | world; characters-skills; story-social-ai; content-product | [6 замечаний](reviews/by-lighting-agent.md) | [Точный кандидат](rechecks/by-lighting-agent.md) |
| Scene | lighting-time; items-economy; ui-media-lifecycle | [2 замечания](reviews/by-scene-agent.md) | [Точный кандидат](rechecks/by-scene-agent.md) |

Первые отчёты сохранены вместе с хэшами проверявшихся входов. Они описывают прежние тексты и не выдаются за ревью исправленного варианта. Повторные проверки связывают замечания с окончательными хэшами ниже. Прочитанные тела, semantic spot-checks, обнаруженные поиском пути и непроверенный объём различаются в каждом отчёте.

Использование Luna/Max здесь подтверждает, что такая схема выполнила ограниченное исследование с внешней проверкой. Сравнительного измерения стоимости или качества против других моделей не проводилось; это не доказательство оптимальности Max для любой задачи.

## Источники и реализация

CE закреплён на `a7ff41b300ef712f0e7d088183a3d08957110cdd`; исследованный checkout чистый. Steamwoven coverage закреплён на `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. Последующие merge не приписываются этому срезу; [README](README.md#база-и-происхождение) отдельно раскрывает уже наблюдаемый PR #37.

В [census](source-coverage.md) перечислены все 140 `.c` из CE `src/game` и `src/ui`. В доменных документах есть range-ссылки для 112 модулей; для 28 таких ссылок нет. Это инвентаризация трассировки. Она не доказывает проверку каждого тела, каждой ветви или полноту всех механик оригинала. Дополнительные first-party источники, заголовки и инфраструктурные чтения указаны отдельно.

В описаниях различаются CE-source-confirmed, project policy, inference и unknown. Даже реально прочитанный CE path не является original-runtime-verified. Условия приёмки в FR — будущие сценарии, а не результаты их выполнения в этой работе. Данные, поведение, интеграция и проверка остаются четырьмя независимыми измерениями покрытия.

## Исправления по перекрёстному ревью

| Замечание | Исправление и сохранённая граница |
| --- | --- |
| World RV-WA-001 | SCENE-013 сужен до вклада сценических объектов в navigation; общий route/step contract остаётся NAV. Scene-to-navigation adapter не зачтён как реализованный. |
| World RV-WA-002 | NAV-002 включает slippery knockdown, sinkable/wading и условные path costs; точные original probabilities не установлены. |
| World RV-WA-003 | Добавлен COMBAT-013: журнал убийств и травм. Противоречивый guard CE записан как аномалия, не как норма оригинала. |
| World RV-WA-004 | SCENE-009 описывает окно в сцене; capability-dependent traversal находится в SCENE-016/NAV-003. |
| World RV-WA-005 | Исправлено имя source-функции: `path_make`. |
| World RV-WA-006 | Roof DIF calls не выданы за рабочую поддержку: load/save stubs и всегда false modified check раскрыты. |
| World RV-WA-007 | Перекрывающиеся деревья в приёмке явно synthetic; пять разрешённых real-data объектов не названы подтверждёнными деревьями. |
| World RV-WA-008 | AI see check не объявлен общим luminance gate; specific dark-background stat effect отделён от общей видимости. |
| Lighting 1 | CONTENT-003 включает initial reset/shopping-or-start path; final campaign/PC binding остаётся неизвестным. |
| Lighting 2 | PRODUCT-001 отделяет принятый display baseline от конкретных settings и физической test matrix, пока предложенных для обсуждения. |
| Lighting 3 | PRODUCT-003 учитывает существующий diagnostic wheel/+/- zoom. LT/RT — принятые цели владельца; presets — предложение. Позднейшие изменения не зачтены в старом pin. |
| Lighting 4 | CHAR-013/014 раскрывают отдельную terminal max-level ветвь; helper schedule не универсализирован. |
| Lighting 5 | SOCIAL-004 включает Enter/typed broadcast caller; original phrases/mapping и сетевой scope не приписаны источнику. |
| Lighting 6 | SCRIPT/DIALOG inventory учитывает дополнительное чтение script_name и подозрение автора CE о buckets; original associations остаются unknown. |
| Scene 1 | LIGHT-002 различает clocks, будущие сроки и уже due callbacks: остановка game clock сама по себе не прекращает обход очереди. |
| Scene 2 | UI-007 отделяет camera pan от UI scrolling; CE Perception limit и центрирование не превращены в современную gamepad policy. |

Первая перепроверка World выявила ещё три уточнения: устаревшие domain source inventories, неизвестный original window ART и отсутствие flag-specific cost scenario. Исправлены соответствующие границы; результат повторного прохода фиксируется в addendum этого же отчёта.

Дополнительно MAGIC-007 раскрывает ограниченно прочитанное Legacy FX/overlay infrastructure; modern effects не заменяют gameplay outcome или весь Legacy effect lifecycle. Родительская проверка исправила ссылку object-node pool, выходившую за EOF. Подробности и непроверенные ветви сохранены в source inventory.

## Автоматическая проверка документов

Проверены уникальность всех 183 FR ID и anchors, наличие ровно одной доменной строки покрытия на ID, равенство четырёх значений доменной и общей матрицы, совпадение названий и ссылок. Source-ссылки на закреплённые CE/Steamwoven commits проверены по локальным файлам: существование пути, правильный pin и допустимые границы строк. Это не сетевой тест доступности GitHub.

Проверены относительные Markdown-ссылки и anchors, отсутствие персональных локальных путей/явных token patterns в новых отчётах, отсутствие trailing whitespace и `git diff --check`. Каталог не содержит игровых assets, диалоговых payloads, binaries или перенесённой реализации CE. Основной checkout и установка игры не менялись.

Новые сборки, parser/runtime tests, graphical/device tests, запуск CE/оригинальной игры и чтение игровых payloads не выполнялись: задача меняет только документацию. Существующие bounded test результаты не выданы за новый прогон или паритет.

## Хэши окончательных доменных документов

Хэши относятся к содержимому файлов, включая стабильные FR anchors; расположение worktree и Git commit метаданные на них не влияют.

| Документ | SHA-256 |
| --- | --- |
| [world.md](world.md) | `b2285afad4045881afab97343a9385ac95ea8e01889d745218ce5641c3a7f9a4` |
| [scene.md](scene.md) | `08313f3f9511ff202f3ff14eb2d44cae549a4a7481b11adb2eb75392ae9c8918` |
| [lighting-time.md](lighting-time.md) | `08ff47c1b86b9ed570d5458d7b043df9c2f85a89c652727ec2141d25699c5932` |
| [characters-skills.md](characters-skills.md) | `4e2c620bc3a0a201ac6e845e05b95d603cc9c0154f05ca0305aef188e5e85b7c` |
| [combat-magic-tech.md](combat-magic-tech.md) | `721d4fe6c1ff7b0f12b81ee2f38d98dd27ac31da4b36a1628d480b9fdcbe0bf8` |
| [items-economy.md](items-economy.md) | `2374598aab28ca3bfb58336285c12942be8f4b5abddeaab760f54075bb2e0fbc` |
| [story-social-ai.md](story-social-ai.md) | `93b1ddd152d2254d92361e6c6eb7e4b3e363bdd8ae9a8daa5f422d15f878efe7` |
| [world-travel-runtime.md](world-travel-runtime.md) | `be5e9a6684855ca6b48c8f8f797e4ed70cb0beab23e0749aafc07f4d001aaf59` |
| [ui-media-lifecycle.md](ui-media-lifecycle.md) | `94188d5f8d95c4f6d7e3b0ff09b4e7d89c86dcfc807b2809c16c970a07e39095` |
| [content-product.md](content-product.md) | `d8c25ae0b8f524ea7d9c19f8c5766bee106cd8c4431ad0eb0350255742eb9881` |

## Окончательные отчёты перепроверки

| Отчёт | SHA-256 |
| --- | --- |
| [by-world-agent](rechecks/by-world-agent.md) | `02aae37616662ce9135dc82fbe22cb8d276a40ae505bfa3e9bf452b2c6b3d5c6` |
| [by-lighting-agent](rechecks/by-lighting-agent.md) | `844390a3a4e45aec4f85a0096781d89ce302147a3d130cbbc402a3725097f9c7` |
| [by-scene-agent](rechecks/by-scene-agent.md) | `923be2a62ac3d0f83655959f07a26057e16136d3af7be0d0a90d62848987cb17` |

## Решение и остаток

Все 16 первоначальных замечаний и три остаточных уточнения после первой перепроверки исправлены. Независимые rechecks подтвердили правки на точных хэшах; новых существенных замечаний в проверенных областях не осталось. Принятие полноты и смысла каталога владельцем остаётся отдельным решением. Source/runtime неизвестные ниже не объявляются закрытыми.

До аппрува полезно проверить список с позиции игрока: пропущенные механики, ошибочные объединения, смысл социальных и редких сценариев. [Краткий обзор](player-review.md) открывает эту проверку, [полная матрица](coverage.md) даёт переход к каждому FR.

Открытые source/data/runtime вопросы не скрываются: concrete Crash Site entrance/exit bindings, полные formulas/RNG draw order, resource precedence/profile, все SCR opcodes/quest branches, unsupported object/art cases, CE stubs/anomalies и platform-specific media/save paths. Они ограничивают будущие утверждения о совместимости; functional draft не требует угадывать ответы. Ранние приоритеты авторов и возможный playable slice — предложения, не созданные задачи. Hermes board остаётся единственным источником задач; этот реестр не разрешает dispatch.
