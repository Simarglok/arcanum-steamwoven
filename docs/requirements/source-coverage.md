# Инвентарь исходников и границы исследования

**Обновление implementation coverage 2026-10-09:** [аудит PR/main и доски](README.md#база-и-происхождение) пересверяет Steamwoven, а не все 140 CE-модулей. Census и counts range-ссылок ниже относятся к исходному source-аудиту 2026-10-02/03; новый полный CE census здесь не заявлен. Последующие source/data исследования связаны из соответствующих FR и [каталога компонентов](../project/status.md).

**Срез: CE `a7ff41b300ef712f0e7d088183a3d08957110cdd`; исследование начато 2026-10-02.** Все 140 `.c` в `src/game` и `src/ui` включены в census ниже. Это инвентаризация модулей, а не заявление о построчном аудите всей игры.

## Как читать инвентарь

- «Есть диапазоны» означает, что доменный отчёт содержит привязки к строкам данного модуля. Это не означает проверки всего модуля или всех callers.
- Прочитанные тела/участки и найденные только поиском пути различаются в source inventory каждого доменного отчёта. Наличие ссылки само по себе не доказывает семантику требования.
- «Без range-ссылки» сохраняет явный пробел трассировки. Файл мог быть найден поиском или частично просмотрен; это не закрывает исследование его поведения.
- Оригинальный executable и игровые payloads не исследовались в этой задаче. CE TODO/stub не позволяет исключить соответствующую функцию оригинальной игры.
- Заголовки и first-party платформенные пути указаны отдельным разделом по фактическим привязкам. Сторонние библиотеки не объявляются полностью исследованными.

## Все игровые и UI-модули

| Модуль | Диапазоны в реестре | Доменные ссылки |
| --- | --- | --- |
| [`src/game/a_name.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c) | Есть диапазоны: 1 | [characters-skills](characters-skills.md) |
| [`src/game/ai.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c) | Есть диапазоны: 16 | [story-social-ai](story-social-ai.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/anim.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c) | Есть диапазоны: 23 | [combat-magic-tech](combat-magic-tech.md), [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/anim_private.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim_private.c) | Есть диапазоны: 3 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/animfx.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/animfx.c) | Есть диапазоны: 1 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/antiteleport.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/antiteleport.c) | Есть диапазоны: 2 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/area.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/area.c) | Есть диапазоны: 1 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/background.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/background.c) | Есть диапазоны: 5 | [characters-skills](characters-skills.md) |
| [`src/game/bless.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/bless.c) | Есть диапазоны: 1 | [characters-skills](characters-skills.md) |
| [`src/game/brightness.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/brightness.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/broadcast.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/broadcast.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/ci.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ci.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/combat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c) | Есть диапазоны: 23 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/critter.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c) | Есть диапазоны: 13 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md), [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md) |
| [`src/game/curse.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/curse.c) | Есть диапазоны: 1 | [characters-skills](characters-skills.md) |
| [`src/game/description.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/description.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/dialog.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c) | Есть диапазоны: 9 | [story-social-ai](story-social-ai.md) |
| [`src/game/effect.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c) | Есть диапазоны: 3 | [characters-skills](characters-skills.md) |
| [`src/game/facade.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/facade.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/fate.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/fate.c) | Есть диапазоны: 1 | [characters-skills](characters-skills.md) |
| [`src/game/gameinit.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gameinit.c) | Есть диапазоны: 2 | [content-product](content-product.md) |
| [`src/game/gamelib.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c) | Есть диапазоны: 19 | [content-product](content-product.md), [lighting-time](lighting-time.md), [scene](scene.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/gfade.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gfade.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/gmovie.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gmovie.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/gsound.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gsound.c) | Есть диапазоны: 6 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/highres_config.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/highres_config.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/hrp.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/hrp.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/invensource.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/invensource.c) | Есть диапазоны: 3 | [items-economy](items-economy.md) |
| [`src/game/item.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c) | Есть диапазоны: 36 | [characters-skills](characters-skills.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/item_effect.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item_effect.c) | Есть диапазоны: 1 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| [`src/game/jumppoint.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.c) | Есть диапазоны: 1 | [world](world.md) |
| [`src/game/level.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c) | Есть диапазоны: 7 | [characters-skills](characters-skills.md) |
| [`src/game/li.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/li.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/light.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c) | Есть диапазоны: 7 | [lighting-time](lighting-time.md) |
| [`src/game/light_scheme.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light_scheme.c) | Есть диапазоны: 7 | [lighting-time](lighting-time.md) |
| [`src/game/location.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c) | Есть диапазоны: 1 | [world](world.md) |
| [`src/game/logbook.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/logbook.c) | Есть диапазоны: 2 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/magictech.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c) | Есть диапазоны: 19 | [combat-magic-tech](combat-magic-tech.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/map.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c) | Есть диапазоны: 17 | [lighting-time](lighting-time.md), [world](world.md) |
| [`src/game/mes.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c) | Есть диапазоны: 2 | [content-product](content-product.md) |
| [`src/game/monstergen.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/monstergen.c) | Есть диапазоны: 3 | [story-social-ai](story-social-ai.md) |
| [`src/game/mp_utils.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mp_utils.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/mt_ai.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_ai.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/mt_item.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_item.c) | Есть диапазоны: 6 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| [`src/game/mt_obj_node.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_obj_node.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/multiplayer.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/multiplayer.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/name.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/name.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/newspaper.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/newspaper.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/obj.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c) | Есть диапазоны: 2 | [content-product](content-product.md) |
| [`src/game/obj_file.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_file.c) | Есть диапазоны: 1 | [content-product](content-product.md) |
| [`src/game/obj_find.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_find.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/obj_flags.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/obj_id.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_id.c) | Есть диапазоны: 1 | [content-product](content-product.md) |
| [`src/game/obj_pool.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_pool.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/obj_private.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_private.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/object.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c) | Есть диапазоны: 30 | [items-economy](items-economy.md), [lighting-time](lighting-time.md), [scene](scene.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world](world.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/object_node.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object_node.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/oname.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/oname.c) | Есть диапазоны: 1 | [items-economy](items-economy.md) |
| [`src/game/party.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/party.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/path.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c) | Есть диапазоны: 8 | [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/player.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/player.c) | Есть диапазоны: 2 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/portal.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c) | Есть диапазоны: 2 | [scene](scene.md) |
| [`src/game/portrait.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portrait.c) | Есть диапазоны: 3 | [characters-skills](characters-skills.md) |
| [`src/game/proto.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c) | Есть диапазоны: 1 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/quest.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c) | Есть диапазоны: 3 | [story-social-ai](story-social-ai.md) |
| [`src/game/random.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/random.c) | Есть диапазоны: 1 | [content-product](content-product.md) |
| [`src/game/reaction.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c) | Есть диапазоны: 6 | [story-social-ai](story-social-ai.md) |
| [`src/game/reputation.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c) | Есть диапазоны: 5 | [story-social-ai](story-social-ai.md) |
| [`src/game/resistance.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/resistance.c) | Есть диапазоны: 2 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/roof.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/roof.c) | Есть диапазоны: 5 | [lighting-time](lighting-time.md), [scene](scene.md) |
| [`src/game/rumor.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/rumor.c) | Есть диапазоны: 4 | [story-social-ai](story-social-ai.md) |
| [`src/game/sa.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sa.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/script.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c) | Есть диапазоны: 8 | [story-social-ai](story-social-ai.md) |
| [`src/game/script_name.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c) | Есть диапазоны: 3 | [story-social-ai](story-social-ai.md) |
| [`src/game/scroll.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/scroll.c) | Есть диапазоны: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/sector.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c) | Есть диапазоны: 14 | [lighting-time](lighting-time.md), [scene](scene.md), [world](world.md) |
| [`src/game/sector_block_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_block_list.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/sector_light_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_light_list.c) | Есть диапазоны: 2 | [lighting-time](lighting-time.md) |
| [`src/game/sector_object_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c) | Есть диапазоны: 3 | [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/sector_roof_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_roof_list.c) | Есть диапазоны: 1 | [scene](scene.md) |
| [`src/game/sector_script.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_script.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/sector_script_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_script_list.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/sector_sound_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_sound_list.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/sector_tile_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_tile_list.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/settings.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/settings.c) | Есть диапазоны: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/sfx.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sfx.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/skill.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c) | Есть диапазоны: 20 | [characters-skills](characters-skills.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/spell.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/spell.c) | Есть диапазоны: 2 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/stat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c) | Есть диапазоны: 13 | [characters-skills](characters-skills.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/target.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/target.c) | Есть диапазоны: 3 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/tb.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tb.c) | Есть диапазоны: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/tc.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tc.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/tech.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tech.c) | Есть диапазоны: 3 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md) |
| [`src/game/teleport.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c) | Есть диапазоны: 11 | [world](world.md) |
| [`src/game/terrain.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/terrain.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/text_filter.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/text_filter.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/tf.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tf.c) | Есть диапазоны: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/tile.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c) | Есть диапазоны: 3 | [lighting-time](lighting-time.md), [scene](scene.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/tile_block.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_block.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/tile_script.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script.c) | Есть диапазоны: 2 | [story-social-ai](story-social-ai.md) |
| [`src/game/tile_script_list.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script_list.c) | Есть диапазоны: 1 | [story-social-ai](story-social-ai.md) |
| [`src/game/timeevent.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c) | Есть диапазоны: 7 | [lighting-time](lighting-time.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/townmap.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/townmap.c) | Есть диапазоны: 1 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/trap.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c) | Есть диапазоны: 5 | [items-economy](items-economy.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/game/ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ui.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/walkmask.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/walkmask.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/game/wall.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wall.c) | Есть диапазоны: 3 | [scene](scene.md) |
| [`src/game/wallcheck.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wallcheck.c) | Есть диапазоны: 2 | [scene](scene.md) |
| [`src/game/wp.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/wp.c) | Есть диапазоны: 2 | [content-product](content-product.md) |
| [`src/ui/anim_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/anim_ui.c) | Есть диапазоны: 1 | [lighting-time](lighting-time.md) |
| [`src/ui/broadcast_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/broadcast_ui.c) | Есть диапазоны: 1 | [story-social-ai](story-social-ai.md) |
| [`src/ui/charedit_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/charedit_ui.c) | Есть диапазоны: 10 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/combat_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/combat_ui.c) | Есть диапазоны: 2 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/ui/compact_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/compact_ui.c) | Есть диапазоны: 3 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/cyclic_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/cyclic_ui.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/ui/dialog_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c) | Есть диапазоны: 7 | [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/fate_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/fate_ui.c) | Есть диапазоны: 3 | [characters-skills](characters-skills.md) |
| [`src/ui/follower_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/follower_ui.c) | Есть диапазоны: 3 | [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/gameuilib.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/gameuilib.c) | Есть диапазоны: 1 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/hotkey_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/hotkey_ui.c) | Есть диапазоны: 2 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/intgame.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c) | Есть диапазоны: 9 | [combat-magic-tech](combat-magic-tech.md), [items-economy](items-economy.md), [lighting-time](lighting-time.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/inven_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c) | Есть диапазоны: 12 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/iso.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/iso.c) | Есть диапазоны: 1 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/item_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/item_ui.c) | Есть диапазоны: 2 | [items-economy](items-economy.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/logbook_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/logbook_ui.c) | Есть диапазоны: 7 | [combat-magic-tech](combat-magic-tech.md), [story-social-ai](story-social-ai.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/mainmenu_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c) | Есть диапазоны: 19 | [characters-skills](characters-skills.md), [ui-media-lifecycle](ui-media-lifecycle.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/options_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/options_ui.c) | Есть диапазоны: 4 | [combat-magic-tech](combat-magic-tech.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/roller_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/roller_ui.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/ui/schematic_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/schematic_ui.c) | Есть диапазоны: 5 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/scrollbar_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/scrollbar_ui.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/ui/skill_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/skill_ui.c) | Есть диапазоны: 4 | [characters-skills](characters-skills.md) |
| [`src/ui/sleep_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c) | Есть диапазоны: 5 | [lighting-time](lighting-time.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/slide_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/slide_ui.c) | Есть диапазоны: 4 | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/spell_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/spell_ui.c) | Есть диапазоны: 5 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/ui/tb_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c) | Есть диапазоны: 3 | [characters-skills](characters-skills.md), [combat-magic-tech](combat-magic-tech.md), [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/ui/tech_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tech_ui.c) | Есть диапазоны: 1 | [combat-magic-tech](combat-magic-tech.md) |
| [`src/ui/textedit_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/textedit_ui.c) | Без range-ссылки | Открытый пробел; см. оговорку выше. |
| [`src/ui/wmap_rnd.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_rnd.c) | Есть диапазоны: 4 | [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/wmap_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c) | Есть диапазоны: 8 | [ui-media-lifecycle](ui-media-lifecycle.md), [world](world.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/ui/written_ui.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/written_ui.c) | Есть диапазоны: 3 | [items-economy](items-economy.md), [ui-media-lifecycle](ui-media-lifecycle.md) |

В доменных документах есть range-привязки для **113 из 140** модулей; для **27** таких привязок нет. Это показатели трассировки источников, не полноты поведения.

## Дополнительные привязки вне census

| Источник | Доменные ссылки |
| --- | --- |
| [`first_party/bink_compat/src/bink_compat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/bink_compat/src/bink_compat.c) | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`first_party/mss_compat/src/mss_compat.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/mss_compat/src/mss_compat.c) | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`first_party/tig/src/database.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/database.c) | [content-product](content-product.md) |
| [`first_party/tig/src/file.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/file.c) | [content-product](content-product.md) |
| [`first_party/tig/src/movie.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/movie.c) | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`first_party/tig/src/sound.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/sound.c) | [ui-media-lifecycle](ui-media-lifecycle.md) |
| [`src/game/criticals.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/criticals.h) | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/jumppoint.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/jumppoint.h) | [world](world.md) |
| [`src/game/magictech.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.h) | [combat-magic-tech](combat-magic-tech.md) |
| [`src/game/obj.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h) | [items-economy](items-economy.md) |
| [`src/game/script.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h) | [story-social-ai](story-social-ai.md) |
| [`src/game/sector.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.h) | [lighting-time](lighting-time.md) |
| [`src/game/teleport.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.h) | [world](world.md) |
| [`src/main.c`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/main.c) | [ui-media-lifecycle](ui-media-lifecycle.md), [world-travel-runtime](world-travel-runtime.md) |
| [`src/net_compat.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/net_compat.h) | [ui-media-lifecycle](ui-media-lifecycle.md) |

## Инфраструктура объектов

Следующие исследованные участки относятся к identity, lifetime и сериализации. Они не задают отдельные действия игрока или обязательство воспроизвести устройство памяти C-кода:

- [object-node pool, создание и освобождение](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object_node.c#L20-L105).
- [magic/tech node pool и начальные поля](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mt_obj_node.c#L23-L100).
- [object handle allocation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_pool.c#L383-L430): различаются runtime handle и постоянная identity. Полный lookup/restore/invalidation path не исследован.
- [сериализация SizeableArray](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sa.c#L110-L137) и [сброс типизированных object fields](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_private.c#L218-L260): структуры хранения, не самостоятельная механика инвентаря/квестов.
- [таблица имён групп флагов и полей](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.c#L5-L52): registry metadata, не доказательство исполнения всех значений флагов.

Эти источники описывают инфраструктуру CE и сами по себе не подтверждают покрытие Steamwoven. Таблица выше считает привязки в тематических документах; выбранные инфраструктурные диапазоны приведены здесь отдельно.

## Следующие исследования и непроверенный объём

Полный форматный/API/формульный аудит не выполнен. Для выбранной карточки проверять конкретные caller paths и значения оригинальных данных, поддержанный профиль и результаты независимого runtime. Особые пробелы: точные входы/выходы Crash Site, формулы и draw order RNG, ресурсные приоритеты полной установки, неподдержанные object/art варианты, все script opcodes/ветви квестов и platform-specific media/save paths. Это список пробелов знания, не разрешение запускать новые задачи.
