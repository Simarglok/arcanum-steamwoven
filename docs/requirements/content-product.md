# Данные, совместимость и отдельные цели Steamwoven

**Текущий статус:** статус каждого требования этого документа, реализованный объём, остаток и документация — в [матрице покрытия](coverage/content-product.md) (пересверено 2026-10-20, ARC-DEV-027). Поля «Steamwoven» ниже и сводный раздел покрытия — подробности на дату их последней правки (исходный аудит 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); при расхождении верна матрица.

Draft, 2026-10-02. Исходный Steamwoven research pin `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. **Источник (F0)** ниже — чтение функций оригинала по указанным адресам, уровень F0 (не проверено в exe), если строки нет в [журнале сверки](../research/original-verification-ledger.md); новых original-data или runtime проверок не проводилось. Подтверждение чтением функции не равно наблюдению оригинального поведения. Сценарии ниже — предложенная приёмка, не выполненные тесты. См. [правила реестра](README.md).

## Контент и системные зависимости

<a id="fr-content-001"></a>

### FR-CONTENT-001 — Оригинальные ресурсы читаются из выбранных источников

**Поведение.** Запуск использует данные собственной установленной игры пользователя; логическое имя ресурса сохраняется независимо от физического архива/каталога. Отсутствующий обязательный источник имеет явный результат ошибки. **Источник (F0):** загрузка module DAT и каталога (0x404C10; порядок подключения сверен в строке журнала «Prototype phase»), подключение репозитория; политика не распространять assets взята из [vision](../project/vision.md), не из оригинала. **Зависимости/приёмка:** внешний data source, DAT/loose lookup; корректная установка разрешает объявленный ресурс, неполная даёт диагностируемую ошибку без подмены. **Покрытие:** частично — authenticated [Area owner/selected-frame leases](../world/crash-site-area-loader.md), bounded DAT и explicit selected NPC SCR/DLG sources. ARC-DATA-011 добавил read-only discovery установки и общий resolver ([исследование и API](../research/content-precedence.md)); Area loader и NPC-диалог пока читают явно выбранные архивы.

<a id="fr-content-002"></a>

### FR-CONTENT-002 — Приоритеты ресурсов и патчей задаются проверенным профилем

**Поведение.** При нескольких версиях одного ресурса выбирается версия согласно объявленному профилю игры/патчей, одинаково для всех потребителей. **Источник (F0):** module patch registration (0x404C10), lookup с database flags и writable repository. Здесь недостаточно правила «последний каталог всегда выигрывает»: архивная ветвь учитывает flags и отдельный поиск loose-файла. **Зависимости/приёмка:** независимый resolver; synthetic конфликт base/patch/loose с явными expected outcomes по утверждённому профилю. **Покрытие:** частично (ARC-DATA-011, [исследование](../research/content-precedence.md)). Порядок монтирования и правило поиска прослежены по функциям оригинала (F0; монтирование модуля после базовых источников — строка «Prototype phase»); на статической установке первое чтение и exists/extract выбирают одну копию — первый источник в порядке поиска. `Arcanum.Data.Content` реализует это правило с synthetic и real-data тестами; на установке владельца 109 перекрытий, все внутри архивов. Нынешние loaders ещё принимают выбранные caller archives (скрипт Вергилия читается из перекрытой копии `arcanum2.dat`). Не моделируются: файлы, записанные во время игры, sticky-флаг первого открытия, `.ignore`, `.dif` и другие модули; паритет с оригиналом NOT_VERIFIED.

<a id="fr-content-003"></a>

### FR-CONTENT-003 — Кампания/модуль имеют собственные начальные параметры

**Поведение.** Выбранный модуль задаёт campaign identity, исходную карту/персонажа и начальное время в пределах поддержанного профиля; нельзя неявно применять параметры одной кампании к другой. **Источник (F0):** список модулей (0x4027C0), mod-load callbacks и подготовка mobiles (0x402A50), adventure/time metadata (0x4B9C30; стартовые 1885 год и 15:00 сверены — строка «Clock start»). Прослежен initial reset: сброс новой игры (0x4B9BE0) выбирает shopping map с fallback на start map, открывает её и вызывает создание персонажа игрока (0x40D8A0). Это не устанавливает конечный campaign map/PC/prototype binding, конкретную точку появления или оригинальный runtime result; они остаются неизвестными. **Приёмка:** два synthetic module profiles не смешивают identity и стартовые часы; неподдержанный модуль отклонён явно. **Покрытие:** отсутствует; fixed Crash Site diagnostic selector не является campaign bootstrap.

<a id="fr-content-004"></a>

### FR-CONTENT-004 — Объекты сохраняют identity, тип и наследуемые свойства

**Поведение.** Instance override и prototype default различаются; ссылки на сущности не заменяются индексом спрайта или временным UI handle. **Источник (F0):** чтение объекта выбирает proto/instance (0x406600), выборка поля — direct/transient/prototype ветви (0x408A20; замена поля прототипа полем экземпляра сверена — строка «Field inheritance»), solitary object reader (0x4E6BC0). **Приёмка:** отличающиеся instance/default значения, сохранённая identity при переходе, явный unsupported type. **Покрытие:** частично — [Area](../world/crash-site-area-loader.md) сохраняет multisector v119 static/mobile/prototype identity, direct/effective presence и immutable occurrences; [NPC entry](../scripting/npc-dialog-entry-runtime.md) сохраняет typed bindings. Mutable registry, save/actor lifecycle отсутствуют.

<a id="fr-content-005"></a>

### FR-CONTENT-005 — Системы продолжают читать оригинальные структуры

**Поведение.** DAT/ART/PRO/MOB/MES/DLG/SCR/maps остаются источниками для поддержанных областей; одноразовая конверсия не скрывает оригинальную identity и несовместимые записи. **Происхождение:** принятое [архитектурное направление](../architecture/decisions.md); функции оригинала показывают separate DAT variants и object reader (0x406600, F0), но не доказывают допустимость любого варианта. **Приёмка:** поддержанные/неподдержанные форматы и версии имеют явный результат; cache не теряет оригинальные ID. **Покрытие:** частично — bounded DAT/ART/MES/SEC/PRO/mobile/DLG readers и selected SCR entry graph используются напрямую. Поддержка всех вариантов, overlays/DIF/PATCH, save и полной VM не заявлена. [Catalogue](../project/status.md).

<a id="fr-content-006"></a>

### FR-CONTENT-006 — Текстовые ресурсы и encoding не подменяются вручную

**Поведение.** Имена, описания, диалоги, книги и UI-текст загружаются по исходным идентификаторам; профиль encoding и локализации задаётся явно. **Источник (F0):** MES load (0x4D4290), numeric/text record parse (0x4D4A60). Эти функции не устанавливают универсальный Unicode/локализационный контракт. **Приёмка:** synthetic кириллица/латиница и отсутствующий message ID обрабатываются по объявленному профилю без порчи raw bytes. **Покрытие:** частично — byte-preserving [DLG reader](../formats/dlg-reader.md), ограниченные MES projections для данных и [MES reader ARC-DATA-013](../formats/mes-reader.md) с явными профилями ASCII/Windows-1252/Windows-1251 и typed отсутствием номера (кириллица с `я` пока отклоняется); локализация, UI-текст и потребители MES reader отсутствуют.

<a id="fr-content-007"></a>

### FR-CONTENT-007 — Случайность относится к gameplay state

**Поведение.** Gameplay checks имеют объявленную политику RNG; камера, частота кадров или смена visual mode не должны расходовать игровые броски. **Источник:** seed/state и integer-range draws (0x4CEED0, 0x4CEEE0, 0x4CEF00, 0x4CEF40, 0x4CEF50); генератор, диапазоны, seed и пересевы сверены — строка журнала «Random (R1–R6)». Разделение rendering/RNG — **project policy**; точный порядок вызовов в оригинале не восстановлен этим отчётом. **Приёмка:** при заданном seed и последовательности игровых команд визуальные изменения не меняют броски. **Покрытие:** частично — [генератор сессии](../world/session-random-runtime.md) (ARC-WORLD-019): SplitMix64 от seed хоста, равномерные броски с меткой, seed и позиция в snapshot-схеме 7, источник бросков для резолвера атаки; броски делает только явный вызов, поэтому отрисовка их не тратит. Решение задачи: проектный генератор вместо LCG оригинала (в оригинале seed берётся из времени и не сохраняется, поэтому исходную последовательность не воспроизвести) и без modulo-смещения оригинала; владелец может его пересмотреть. Ни один хост пока не бросает (viewer — seed 0, бой не подключён); пересев генератора оригиналом для инвентаря и диалога не моделируется.

<a id="fr-content-008"></a>

### FR-CONTENT-008 — Mods и патчи имеют измеряемое покрытие

**Поведение.** Совместимость указывается для конкретного профиля данных/патча/мода и поддержанных функций. Наличие загрузчика DAT не означает поддержку UAP/Multiverse или изменений executable. **Происхождение:** [accepted compatibility direction](../project/vision.md) и module/patch hookup оригинала (0x404C10, F0); широкая совместимость не подтверждена. **Приёмка:** reproducible synthetic override и opt-in локальная выборка выбранного профиля без изменения установки; unsupported engine extensions названы. **Покрытие:** общее мод-покрытие отсутствует; bounded loaders работают с конкретными caller inputs. Поддержка существующих mods — поэтапная цель.

## Современные product requirements — не поведение оригинала

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

Базовая графика использует существующие спрайты; будущий upscale сохраняет мировой размер/anchors/кадры/направления/timing. Runtime toggle сохраняет actor/world/action state, camera framing и UI; отсутствующий HD asset допускает объявленный Legacy fallback. **Основание:** [visual modes direction](../architecture/decisions.md), [vision](../project/vision.md). **Приёмка:** одинаковый world anchor и размер, toggle при движении/свете без map reload, идентичные gameplay outcomes; human review HD assets. **Покрытие:** Legacy diagnostic rendering частично; [контракт HD-отображения](../rendering/hd-presentation-contract.md) (ARC-RENDER-013: масштабированный кроп, якоря Legacy, fallback по кадру, бюджеты, протокол переключения, синтетические тесты); [исследование методов апскейла 2×](../rendering/hd-upscale-research.md) (ARC-RES-033: рекомендованный метод, альфа, слоты палитры, швы земли, лицензии); [HD-путь отрисовки](../rendering/terrain-viewer.md#hd-render-path-arc-render-014) (ARC-RENDER-014: персонажи и объекты в AREA/WORLD, откат по кадру, бюджеты, счётчик HUD); [переключатель Legacy/HD](../rendering/terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006) (ARC-UI-006: сохраняемая настройка, клавиша H и палитра, коммит за один кадр или через 500 мс; снапшоты Session побайтно равны в Legacy, HD и при переключениях, в том числе на оригинальном Crash Site с диалогом, ходьбой, часом и входом в пещеру); настоящего asset pack нет. Дополнительные кадры и 3D — будущие варианты.

<a id="fr-product-005"></a>

### FR-PRODUCT-005 — Платформы проверяются на одной функциональной базе

macOS, Windows, Linux и handheld PC должны запускать один объявленный профиль данных/симуляции; handheld readability, performance и controller UX проверяются отдельно. **Основание:** [platform sequence](../architecture/decisions.md). **Приёмка:** воспроизводимый выбранный slice на каждой поддержанной платформе с теми же state outcomes. **Покрытие:** разработка и bounded diagnostics на macOS; общая игровая cross-platform compatibility не подтверждена. Это будущая продуктовая цель, не результат наличия Godot platform export.

<a id="fr-product-006"></a>

### FR-PRODUCT-006 — Совместимость и намеренные исправления различаются

Оригинальная симуляция — principal reference. Улучшенный pathfinding или исправления gameplay bug могут приниматься отдельно с явно описанными отличиями; изменение нельзя маскировать как восстановленное original behavior. **Основание:** [vision](../project/vision.md) и обсуждение возможного улучшения пути. **Приёмка:** source/observed baseline, regression сценарий и объявленная разница перед активацией override. **Покрытие:** policy и [bounded path study](../input/arcanum-pathfinding-study.md); доказанного path-quality improvement или gameplay fix profile нет.

<a id="fr-product-007"></a>

### FR-PRODUCT-007 — Поздние улучшения картинки имеют отдельные границы

Normal/depth/occlusion/emissive maps, soft shadows, fog, weather, particles, improved water и более плавные визуальные переходы day/night — варианты Remastered presentation. Они не должны менять authoritative geometry, gameplay luminance, RNG или clocks. **Происхождение:** исходное product vision владельца; это кандидаты развития, не восстановленные функции оригинала и не обязательный объём раннего Legacy milestone. **Приёмка:** для каждой отдельно принятой возможности показать одинаковые gameplay state outcomes при разных visual settings; asset review и performance проверяются отдельно. **Покрытие:** отсутствует. Если оригинал уже имеет сходный эффект, его legacy-поведение исследуется отдельно; сходное название не доказывает одинаковую семантику.

## Покрытие Steamwoven до 2026-10-20

Таблица покрытия этого раздела перенесена в [матрицу покрытия](coverage/content-product.md): там у каждого требования текущий статус, объём, остаток и документация (2026-10-20). Абзацы ниже описывали прежнюю таблицу этого документа (исходный аудит 2026-10-09 с поздними правками) и сохранены как история; слова «таблица», «строки» и «столбцы» в них относятся к ней.

Ни одна строка не закрывает требование полностью. «Частично» обозначает ровно объём в последнем столбце, а «Проверка» — ограниченные исторические проверки связанных компонентов, не новый запуск и не оригинальный паритет.

## Source inventory и вопросы

Перепись включает группу идентификаторов объектов 0x4E62A0–0x4E6B00 (13 функций; GUID и resident/static identity; выбранные функции 0x4E62A0, 0x4E62D0, 0x4E6340), таблицы полей прототипов группы 0x4681B0–0x49C610 (данные; не полный bootstrap), метаданные ART и отсутствующих изображений группы 0x41CA40–0x41F240, инициализацию служебных индикаторов групп 0x4BB720–0x4BB980 и 0x4BB9A0–0x4BBCE0 и контейнерную инфраструктуру разреженных массивов. Выбранные функции пула объектов (0x4E4CD0–0x4E59A0), хранения полей (0x4E3F80–0x4E6240), узлов списков объектов (0x4E9920–0x4E99C0), узлов магии и технологии (0x4BAC10–0x4BAF50) и разреженных массивов (0x4E7430–0x4E7AB0) описаны в [перечне инфраструктуры объектов](source-coverage.md#инфраструктура-объектов); полные lifetime/restore paths не исследованы. Из темы группы не выводятся новые требования.

Группа маршрутных точек (0x4BFCE0–0x4C0370) содержит draw entry (0x4BFE00) и отрисовку NPC waypoint markers (0x4C0140, 0x4C0280). Найден caller в editor draw path (0x404740); заполнение private tracked-object list и его фактическое использование не установлены. Это граница знания об overlay, а не подтверждённая дополнительная игровая механика.

Прочитаны указанные функции групп библиотеки игры (0x4020F0–0x405070), параметров модуля (0x4B9B90–0x4B9DB0), хранилища объектов (0x405110–0x40D670), файлов объектов (0x4E6B60–0x4E71B0), чтения MES (0x4D4290–0x4D4DD0), генератора случайных чисел (0x4CEEB0–0x4CEF50) и функции библиотеки движка для файлов и архивов DAT; это не построчный аудит каждой группы. Сверены current Data/Simulation/Presentation docs и путь `StaticSceneryScenePlan`. Загрузчики всех proto fields, base-install registration, art variant/resource cache, charset/font pipeline и все platform-specific lookup branches требуют дополнительной детализации. Большие группы функций не считаются полностью исследованными из-за одной ссылки.

Архитектурные ограничения — независимый MIT runtime, data/simulation separation от Godot и запрет включать original assets — записаны в [architecture](../architecture/decisions.md) и [research method](../research/method.md). Это constraints всей реализации, а не дополнительные игровые механики.
