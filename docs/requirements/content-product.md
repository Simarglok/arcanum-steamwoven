# Данные, совместимость и отдельные цели Steamwoven

**Покрытие реализации:** аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), через PR #54. Источники исходного CE-исследования и его дата сохранены; актуальный объём — в таблице и Steamwoven-описаниях ниже. [Метод и срез доски](README.md#база-и-происхождение). Новых runtime/GUI/original-data запусков в этом аудите нет.

Draft, 2026-10-02. CE source pin `a7ff41b300ef712f0e7d088183a3d08957110cdd`; исходный Steamwoven research pin `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. Чтение исходников; новых original-data или runtime проверок не проводилось. CE-source-confirmed означает подтверждение конкретного кода, а не оригинального поведения. Сценарии ниже — предложенная приёмка, не выполненные тесты. См. [правила реестра](README.md).

## Контент и системные зависимости

<a id="fr-content-001"></a>

### FR-CONTENT-001 — Оригинальные ресурсы читаются из выбранных источников

**Поведение.** Запуск использует данные собственной установленной игры пользователя; логическое имя ресурса сохраняется независимо от физического архива/каталога. Отсутствующий обязательный источник имеет явный результат ошибки. **CE-source:** [загрузка module DAT и каталога](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1838-L1902), [подключение репозитория](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/file.c#L573-L649); политика не распространять assets взята из [vision](../project/vision.md), не из CE. **Зависимости/приёмка:** внешний data source, DAT/loose lookup; корректная установка разрешает объявленный ресурс, неполная даёт диагностируемую ошибку без подмены. **Покрытие:** частично — authenticated [Area owner/selected-frame leases](../world/crash-site-area-loader.md), bounded DAT и explicit selected NPC SCR/DLG sources. ARC-DATA-011 добавил read-only discovery установки и общий resolver ([исследование и API](../research/content-precedence.md)); Area loader и NPC-диалог пока читают явно выбранные архивы.

<a id="fr-content-002"></a>

### FR-CONTENT-002 — Приоритеты ресурсов и патчей задаются проверенным профилем

**Поведение.** При нескольких версиях одного ресурса выбирается версия согласно объявленному профилю игры/патчей, одинаково для всех потребителей. **CE-source:** [module patch registration](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1854-L1893), [lookup с database flags и writable repository](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/file.c#L1729-L1803). Здесь недостаточно правила «последний каталог всегда выигрывает»: архивная ветвь учитывает flags и отдельный поиск loose-файла. **Зависимости/приёмка:** независимый resolver; synthetic конфликт base/patch/loose с явными expected outcomes по утверждённому профилю. **Покрытие:** частично (ARC-DATA-011, [исследование](../research/content-precedence.md)). Порядок монтирования и правило поиска прослежены по CE; на статической установке первое чтение и exists/extract выбирают одну копию — первый источник в порядке поиска. `Arcanum.Data.Content` реализует это правило с synthetic и real-data тестами; на установке владельца 109 перекрытий, все внутри архивов. Нынешние loaders ещё принимают выбранные caller archives (скрипт Вергилия читается из перекрытой копии `arcanum2.dat`). Не моделируются: файлы, записанные во время игры, sticky-флаг первого открытия, `.ignore`, `.dif` и другие модули; паритет с оригиналом NOT_VERIFIED.

<a id="fr-content-003"></a>

### FR-CONTENT-003 — Кампания/модуль имеют собственные начальные параметры

**Поведение.** Выбранный модуль задаёт campaign identity, исходную карту/персонажа и начальное время в пределах поддержанного профиля; нельзя неявно применять параметры одной кампании к другой. **CE-source:** [список модулей](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L569-L637), [mod-load callbacks и подготовка mobiles](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L662-L731), [adventure/time metadata](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gameinit.c#L81-L122). Прослежен initial reset: `gameinit_reset` выбирает shopping map с fallback на start map, открывает её и вызывает `player_create` ([reset caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gameinit.c#L59-L71)). Это не устанавливает конечный campaign map/PC/prototype binding, конкретную точку появления или оригинальный runtime result; они остаются неизвестными. **Приёмка:** два synthetic module profiles не смешивают identity и стартовые часы; неподдержанный модуль отклонён явно. **Покрытие:** отсутствует; fixed Crash Site diagnostic selector не является campaign bootstrap.

<a id="fr-content-004"></a>

### FR-CONTENT-004 — Объекты сохраняют identity, тип и наследуемые свойства

**Поведение.** Instance override и prototype default различаются; ссылки на сущности не заменяются индексом спрайта или временным UI handle. **CE-source:** [`obj_read` выбирает proto/instance](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L1233-L1252), [`obj_field_fetch` direct/transient/prototype ветви](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L2485-L2535), [solitary object reader](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_file.c#L43-L64). **Приёмка:** отличающиеся instance/default значения, сохранённая identity при переходе, явный unsupported type. **Покрытие:** частично — [Area](../world/crash-site-area-loader.md) сохраняет multisector v119 static/mobile/prototype identity, direct/effective presence и immutable occurrences; [NPC entry](../scripting/npc-dialog-entry-runtime.md) сохраняет typed bindings. Mutable registry, save/actor lifecycle отсутствуют.

<a id="fr-content-005"></a>

### FR-CONTENT-005 — Системы продолжают читать оригинальные структуры

**Поведение.** DAT/ART/PRO/MOB/MES/DLG/SCR/maps остаются источниками для поддержанных областей; одноразовая конверсия не скрывает оригинальную identity и несовместимые записи. **Происхождение:** принятое [архитектурное направление](../architecture/decisions.md); CE демонстрирует separate [DAT variants](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/database.c#L92-L127) и [object reader](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L1233-L1252), но не доказывает допустимость любого варианта. **Приёмка:** поддержанные/неподдержанные форматы и версии имеют явный результат; cache не теряет оригинальные ID. **Покрытие:** частично — bounded DAT/ART/MES/SEC/PRO/mobile/DLG readers и selected SCR entry graph используются напрямую. Поддержка всех вариантов, overlays/DIF/PATCH, save и полной VM не заявлена. [Catalogue](../project/status.md).

<a id="fr-content-006"></a>

### FR-CONTENT-006 — Текстовые ресурсы и encoding не подменяются вручную

**Поведение.** Имена, описания, диалоги, книги и UI-текст загружаются по исходным идентификаторам; профиль encoding и локализации задаётся явно. **CE-source:** [MES load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L101-L138), [numeric/text record parse](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L516-L544). Эти функции не устанавливают универсальный Unicode/локализационный контракт. **Приёмка:** synthetic кириллица/латиница и отсутствующий message ID обрабатываются по объявленному профилю без порчи raw bytes. **Покрытие:** частично — byte-preserving [DLG reader](../formats/dlg-reader.md), ограниченные MES projections для данных и [MES reader ARC-DATA-013](../formats/mes-reader.md) с явными профилями ASCII/Windows-1252/Windows-1251 и typed отсутствием номера (кириллица с `я` пока отклоняется); локализация, UI-текст и потребители MES reader отсутствуют.

<a id="fr-content-007"></a>

### FR-CONTENT-007 — Случайность относится к gameplay state

**Поведение.** Gameplay checks имеют объявленную политику RNG; камера, частота кадров или смена visual mode не должны расходовать игровые броски. **CE-source:** [seed/state и integer-range draws](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/random.c#L40-L107). Разделение rendering/RNG — **project policy**; точный порядок вызовов в оригинале не восстановлен этим отчётом. **Приёмка:** при заданном seed и последовательности игровых команд визуальные изменения не меняют броски. **Покрытие:** нет общего игрового RNG. Выбор legacy sequence, replay/save policy и точный compat baseline остаются решениями исследования; имя генератора не заменяет это решение.

<a id="fr-content-008"></a>

### FR-CONTENT-008 — Mods и патчи имеют измеряемое покрытие

**Поведение.** Совместимость указывается для конкретного профиля данных/патча/мода и поддержанных функций. Наличие загрузчика DAT не означает поддержку UAP/Multiverse или изменений executable. **Происхождение:** [accepted compatibility direction](../project/vision.md) и CE [module/patch hookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1838-L1902); широкая совместимость не подтверждена. **Приёмка:** reproducible synthetic override и opt-in локальная выборка выбранного профиля без изменения установки; unsupported engine extensions названы. **Покрытие:** общее мод-покрытие отсутствует; bounded loaders работают с конкретными caller inputs. Поддержка существующих mods — поэтапная цель.

## Современные product requirements — не поведение, извлечённое из CE

<a id="fr-product-001"></a>

### FR-PRODUCT-001 — Современные разрешения, HiDPI и читаемый UI

Мир и UI масштабируются независимо; viewport не меняет simulation. Приняты modern resolution, HiDPI, scalable UI и независимость simulation от viewport. Конкретный UI выбора разрешения/window mode/UI scale предлагается для обсуждения; доступность текста и действий остаётся целью. **Основание:** [vision](../project/vision.md), наблюдение владельца о мелких legacy sprites на QHD. **Предлагаемая приёмка:** реальный QHD/HiDPI и отдельно выбранная resolution matrix, usable settings/navigation, world/UI independence; точный набор устройств/режимов ещё не утверждён. **Покрытие:** частично — [synthetic/native display lab](../input/display-ui-validation.md); production UI и физическая читаемость всей игры не подтверждены.

<a id="fr-product-002"></a>

### FR-PRODUCT-002 — Keyboard/mouse и semantic gamepad управление

**Принятое уточнение 2026-10-04:** клавиатура/мышь используют оригинальную схему Arcanum и её интерфейсные действия; движение на WASD не требуется. Новые пресеты и contextual UI относятся к gamepad. Детальная спецификация и диагностические расширения — в [original-keyboard-mouse](original-keyboard-mouse.md); modern resolution/HiDPI/UI scaling из FR-PRODUCT-001 сохраняются.

Gamepad передаёт намерения актёра/камеры/действий, UI показывает contextual prompts; прямое управление не требует отдельно двигать курсор и нажимать движение на каждом шаге. Базовый target проверки — Xbox Series по Bluetooth; это не заявление о проверке всех контроллеров. **Основание:** [accepted controller direction](../project/vision.md#accepted-controller-direction). **Приёмка:** движение/выбор/диалог/меню доступны обоими input methods; reconnection и actual device UX проверены отдельно. **Покрытие:** частично — [input lab](../../experiments/gamepad-lab/README.md) и diagnostic scene; original-world interaction и production menus отсутствуют.

<a id="fr-product-003"></a>

### FR-PRODUCT-003 — Независимая камера, follow и управляемый zoom

Левый стик управляет актёром; правый — камерой с независимыми настройками обеих осей. Manual pan снимает follow; после нейтрали актёра/камеры новая actor-only команда возвращает follow. LT — zoom out, RT — zoom in; точные presets для возврата к 100% и комфортному увеличению предлагаются как деталь UX, пока не являются принятым конкретным mapping. **Основание:** прямые решения владельца в обсуждении; [current diagnostic policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy). Предпочтительное направление описывается видимым движением картинки, не названием checkbox. **Приёмка:** обе оси задаются независимо mouse/gamepad, neutral/follow сценарий, точный preset, saved preferences. **Покрытие:** частично — merged WORLD zoom/Follow/grid integration: LT-out/RT-in, 0.125..8, exact 1/2/3x presets/numeric entry, ordinary default2x и отдельная persisted zoom policy. [PR #37](https://github.com/Simarglok/arcanum-steamwoven/pull/37), [WORLD003](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy). Numerical1.00 не physical pixel1:1; production limits и Bluetooth/QHD comfort отдельно не подтверждены.

<a id="fr-product-004"></a>

### FR-PRODUCT-004 — Legacy и будущий upscale имеют один мир

Базовая графика использует существующие спрайты; будущий upscale сохраняет мировой размер/anchors/кадры/направления/timing. Runtime toggle сохраняет actor/world/action state, camera framing и UI; отсутствующий HD asset допускает объявленный Legacy fallback. **Основание:** [visual modes direction](../architecture/decisions.md), [vision](../project/vision.md). **Приёмка:** одинаковый world anchor и размер, toggle при движении/свете без map reload, идентичные gameplay outcomes; human review HD assets. **Покрытие:** Legacy diagnostic rendering частично; [контракт HD-отображения](../rendering/hd-presentation-contract.md) (ARC-RENDER-013: масштабированный кроп, якоря Legacy, fallback по кадру, бюджеты, протокол переключения, синтетические тесты); upgraded backend/asset pack/instant switch отсутствуют. Дополнительные кадры и 3D — будущие варианты.

<a id="fr-product-005"></a>

### FR-PRODUCT-005 — Платформы проверяются на одной функциональной базе

macOS, Windows, Linux и handheld PC должны запускать один объявленный профиль данных/симуляции; handheld readability, performance и controller UX проверяются отдельно. **Основание:** [platform sequence](../architecture/decisions.md). **Приёмка:** воспроизводимый выбранный slice на каждой поддержанной платформе с теми же state outcomes. **Покрытие:** разработка и bounded diagnostics на macOS; общая игровая cross-platform compatibility не подтверждена. Это будущая продуктовая цель, не результат наличия Godot platform export.

<a id="fr-product-006"></a>

### FR-PRODUCT-006 — Совместимость и намеренные исправления различаются

Оригинальная симуляция — principal reference. Улучшенный pathfinding или исправления gameplay bug могут приниматься отдельно с явно описанными отличиями; изменение нельзя маскировать как восстановленное original behavior. **Основание:** [vision](../project/vision.md) и обсуждение возможного улучшения пути. **Приёмка:** source/observed baseline, regression сценарий и объявленная разница перед активацией override. **Покрытие:** policy и [bounded path study](../input/arcanum-pathfinding-study.md); доказанного path-quality improvement или gameplay fix profile нет.

<a id="fr-product-007"></a>

### FR-PRODUCT-007 — Поздние улучшения картинки имеют отдельные границы

Normal/depth/occlusion/emissive maps, soft shadows, fog, weather, particles, improved water и более плавные визуальные переходы day/night — варианты Remastered presentation. Они не должны менять authoritative geometry, gameplay luminance, RNG или clocks. **Происхождение:** исходное product vision владельца; это кандидаты развития, не восстановленные функции CE и не обязательный объём раннего Legacy milestone. **Приёмка:** для каждой отдельно принятой возможности показать одинаковые gameplay state outcomes при разных visual settings; asset review и performance проверяются отдельно. **Покрытие:** отсутствует. Если CE/source/оригинал уже имеют сходный эффект, его legacy-поведение исследуется отдельно; сходное название не доказывает одинаковую семантику.

## Сводное покрытие

Ни одна строка не закрывает требование полностью. «Частично» обозначает ровно объём в последнем столбце, а «Проверка» — ограниченные исторические проверки связанных компонентов, не новый запуск и не оригинальный паритет.

| ID | Данные | Правила | Интеграция | Проверка | Ограниченный объём |
| --- | --- | --- | --- | --- | --- |
| FR-CONTENT-001 | Частично | Частично | Частично | Частично | Authenticated Area owner/frame leases и explicit NPC SCR/DLG sources; read-only discovery установки и resolver `Arcanum.Data.Content` (ARC-DATA-011), но потребители к нему ещё не подключены. |
| FR-CONTENT-002 | Частично | Частично | Нет | Частично | Порядок источников по CE и snapshot resolver (первый источник в порядке поиска выигрывает), отчёт о 109 перекрытиях установки (ARC-DATA-011); потребители не переподключены, runtime-записи и `.ignore` не моделируются. |
| FR-CONTENT-003 | Нет | Нет | Нет | Нет | Campaign bootstrap отсутствует. |
| FR-CONTENT-004 | Частично | Частично | Частично | Частично | Multisector v119 static/mobile/prototype identity/default/presence и typed NPC bindings; не mutable world registry/save. |
| FR-CONTENT-005 | Частично | Неприменимо | Частично | Частично | Bounded DAT/ART/MES/SEC/PRO/mobile/DLG/SCR consumers; не все format variants/overlays. |
| FR-CONTENT-006 | Частично | Частично | Нет | Частично | Raw DLG/MES projections; [ARC-DATA-013](../formats/mes-reader.md): byte-preserving MES reader с typed отказами и явными профилями ASCII/Windows-1252/Windows-1251 (synthetic латиница/кириллица, отсутствующий номер); файл Windows-1251 с `я` (0xFF) отклоняется — NOT READY; не UI/локализация. |
| FR-CONTENT-007 | Нет | Нет | Нет | Нет | Gameplay RNG отсутствует. |
| FR-CONTENT-008 | Нет | Нет | Нет | Нет | Общего mod compatibility profile нет. |
| FR-PRODUCT-001 | Неприменимо | Частично | Частично | Частично | Display lab; не production game UI/readability. |
| FR-PRODUCT-002 | Неприменимо | Частично | Частично | Частично | Synthetic/diagnostic input, не все gameplay actions. |
| FR-PRODUCT-003 | Неприменимо | Частично | Частично | Частично | WORLD LT-out/RT-in, 1/2/3x+numeric zoom, saved default2x, Follow/Manual и grid integration; не production camera или physical1:1. |
| FR-PRODUCT-004 | Частично | Нет | Частично | Частично | Selected Legacy frames/viewer; [HD contract](../rendering/hd-presentation-contract.md) ARC-RENDER-013 с синтетическими тестами; нет HD assets/backend/toggle, а проверка касается только Legacy diagnostics. |
| FR-PRODUCT-005 | Неприменимо | Неприменимо | Частично | Частично | macOS diagnostics; не playable cross-platform validation. |
| FR-PRODUCT-006 | Неприменимо | Нет | Нет | Нет | Только direction/source study; нет утверждённого gameplay fix profile. |
| FR-PRODUCT-007 | Нет | Нет | Нет | Нет | Только future visual candidates. |

## Source inventory и вопросы

Инвентарь включает `obj_id.c` 1–75 (GUID и resident/static identity; [выбранный диапазон](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_id.c#L17-L42)), `proto.c` 1–120 (объявления и таблицы полей; не полный bootstrap), `name.c` 1–75 (ART/missing-art metadata), `ci.c`/`li.c` 1–100 (indicator initialization) и `sa.h` (контейнерная инфраструктура). Выбранные тела `obj_pool.c`, `obj_private.c`, `object_node.c`, `mt_obj_node.c` и `sa.c` описаны в [инвентаре инфраструктуры объектов](source-coverage.md#инфраструктура-объектов); полные lifetime/restore paths не исследованы. Из названия модуля не выводятся новые требования.

`wp.c` содержит [draw entry](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wp.c#L115-L132) и [отрисовку NPC waypoint markers](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wp.c#L170-L222). Найден [caller в editor draw path](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L1731-L1743); заполнение private tracked-object list и его фактическое использование не установлены. Это граница знания об overlay, а не подтверждённая дополнительная игровая механика.

Прочитаны указанные диапазоны `gamelib.c`, `gameinit.c`, `obj.c`, `obj_file.c`, `mes.c`, `random.c`, `first_party/tig/src/file.c`, `database.c`; это не построчный аудит каждого файла. Сверены current Data/Simulation/Presentation docs и путь `StaticSceneryScenePlan`. Загрузчики всех proto fields, base-install registration, art variant/resource cache, charset/font pipeline и все platform-specific lookup branches требуют дополнительной детализации. Большие `.c` не считаются полностью исследованными из-за одной ссылки.

Архитектурные ограничения — независимый MIT runtime, no CE copying/mechanical translation, data/simulation separation от Godot и запрет включать original assets — записаны в [architecture](../architecture/decisions.md) и [research method](../research/method.md). Это constraints всей реализации, а не дополнительные игровые механики.
