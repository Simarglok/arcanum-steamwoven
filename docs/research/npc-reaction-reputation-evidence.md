# NPC reaction and PC reputation

Status: **Research**, work item ARC-RES-028 (2026-10-16). This document fixes
how the pinned source specifies an NPC's reaction to a PC and the PC's
reputations: what is stored, how the value a dialog `RE` gate or the companion
consent reads is computed, which events change it, and how reputations feed
reaction, greetings and faction membership. It prepares a reaction owner and a
reputation owner, and changes no production API, viewer, dialog port or session
schema. Requirements:
[FR-SOCIAL-001](../requirements/story-social-ai.md#fr-social-001) and
[FR-SOCIAL-002](../requirements/story-social-ai.md#fr-social-002); consumers
are the dialog gates of the
[conversation runtime](../scripting/dialog-conversation-runtime.md) and the
follow consent of the [companion research](companion-follow-evidence.md).

## Vocabulary and boundary

- **Source fact**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License v1.0); the line ranges below are what was read. CE is
  a reconstruction lineage, not an original-executable observation. Several
  functions keep decompiler names (`sub_…`); a reading of their intent is marked
  as such.
- **Local observation**: aggregate counts and SHA-256 digests measured on the
  owner's unchanged installation by an uncommitted scratch reading through the
  merged [`ContentStack`](content-precedence.md) and
  [`DlgReader`](../formats/dlg-reader.md). It printed counts and digests only,
  never text or values of individual records. The counting rules are in
  [Method](#method) so the numbers can be reproduced.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open, or where it looks defective. Every policy
  below is a **proposal**.

Original executable parity remains **NOT_VERIFIED**.

## Answer

- **Stored state lives on the NPC**: a base value (instance field, prototype
  default 50) and a small memory of per-PC values. The effective reaction is
  **not stored**: every query recomputes it from the stored value plus race,
  the PC's beauty-derived reaction modifier ([stat.c 590–600](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L590-L600)), the PC's reputations and the NPC's
  own effects.
- **Effective reaction** of NPC *N* to PC *P* (R1): 0 if *N* has *P* on its
  enemy list; 50 if either side is not the expected type; otherwise
  `effects_N(stored + race[N][P] + modifier(P) + reputation(P, N))`, raised to
  50 when *P* mind-controls *N*. Race and modifier are skipped for aloof or
  monstrous NPCs. There is no clamp; values below 0 or above 100 occur.
- **Categories** (R4): ≤ 0 hatred, ≤ 20 dislike, ≤ 40 suspicious, ≤ 60
  neutral, ≤ 80 courteous, ≤ 100 amiable, above love.
- **Every change is a delta** (R2): `stored ← stored + pcEffects(delta)`.
  "Set to *v*" in dialog, scripts and quests is the delta `v − effective`.
  With no good/bad reaction effects on the PC the effective value then equals
  *v* exactly; with them it does not.
- **Dialog**: `RE t` passes when effective ≥ *t* (t ≥ 0) or ≤ −*t* (t < 0).
  `RE +d` / `RE -d` adjust by *d*; unsigned `RE v` sets *v*. The `>` / `<`
  forms look meant as raise-to / lower-to, but as read their operand parses
  as 0 (A8); no original DLG uses them. `ME 0/1` tests "met before", which is
  still false during the first conversation and becomes true when it ends (or
  at any reaction change outside a conversation).
- **Companion consent** reads the same effective value: refusal "dislike"
  when reaction ≤ AI parameter 5 (C2 of the companion research). A following
  NPC re-runs that check and can leave at ≤ parameter 5; after a reaction drop
  the separate leader check at ≤ parameter 5 + 20 only warns (C13).
- **Reputations** are a PC list of numbers (intended range 1000..1999, not
  enforced, A5) with the game time each
  was gained. A reputation lists up to three factions whose members treat the
  PC as one of them, and up to five effects `(adjustment, origin, faction)`
  summed into the reaction of every NPC whose origin and faction match (0 =
  any). They also pick an occasional greeting.
- **Source anomaly with data impact** (P1): as read, the CE parser of
  `rules\gamerep.mes` stops at a sign. On the owner's data **62 of 93**
  effects carry an explicit `+` or `-`, so CE stores their origin and faction
  as the adjustment value itself and they match almost no NPC. Whether the
  original executable does the same is unknown, and it decides whether most
  reputation reaction effects exist at all (decision D1).

## Stored state (source facts)

| State | Owner | Shape | Written by |
| --- | --- | --- | --- |
| Reaction base | NPC instance | one integer; text-prototype default 50 ([proto.c 16169–16173](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L16169-L16173)) | prototype / instance data only; no runtime writer was found |
| Reaction memory | NPC instance | 10 parallel slots: PC handle, value, game time ([obj.h 306–310](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L306-L310)). Slot 0 = PC currently in conversation; slots 1..9 = remembered PCs | conversation start/end, every reaction change, forget |
| Origin, faction | NPC instance | one integer each, 0 = none | dialog `OR` sets origin ([dialog.c 2179–2181](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2179-L2181)), SCR set-faction ([script.c 2745–2750](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2745-L2750)), prototype, map object patches ([map.c 1750–1752](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1750-L1752), [map.c 1923–1925](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1923-L1925)); a summon gets faction 0 or the faction of an object from its summon record, which we read as the summoner ([magictech.c 1939–1955](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L1939-L1955)) |
| AI parameter row | NPC instance → table | 17 integers per row (companion C3); parameters 5, 9 and 10 concern reaction | data |
| Reputations | PC | ordered list of numbers plus parallel game times ([obj.h 266–267](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L266-L267)) | dialog `RP`, script add/remove |
| Reputation table | module data | per number: 3 faction numbers, up to 5 effects | `rules\gamerep.mes` 1000..1999 at module load |

Single player uses one memory slot per NPC at most; the slot rules matter only
for several PCs (R3).

## Findings

| ID | Claim | Source fact | Consequence for Steamwoven |
| --- | --- | --- | --- |
| R1 | Effective reaction | [reaction.c 192–234](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L192-L234), [reaction.c 441–473](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L441-L473), [reaction.c 544–569](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L544-L569), [effect.c 930–933](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L930-L933) | Order: enemy list → 0; non-PC / non-NPC → 50; stored = this PC's slot (slot 0 included) else the base; plus the race matrix entry (row = NPC race, 11 rows; column = PC race, 8 columns) and the PC's reaction-modifier stat, both skipped for aloof or monstrous NPCs; plus the reputation sum (R10); then the NPC's own `reaction` effects (percent, multiply, divide, add, min, max in that order, [effect.c 742–849](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L742-L849)); finally a floor of 50 when this PC mind-controls the NPC. A second variant used by AI hostility checks floors the race + modifier part at −49 before adding reputation. The query is a pure function of explicit inputs and needs no clamp. |
| R2 | Reaction change | [reaction.c 236–299](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L236-L299), [effect.c 940–948](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L940-L948) | No-op for delta 0, wrong types, an NPC mind-controlled by this PC, and a negative delta when the PC has extraordinary charisma and is the NPC's PC leader. Otherwise the delta passes through the **PC's** `badreactionadj` (negative) or `goodreactionadj` (positive) effects and is added to the stored value (base when no slot). A negative change while the NPC follows this PC arms the leader check and the reaction-loyalty test (companion C13). The change refreshes the health bar and combat reaction flags. |
| R3 | Memory slots | [reaction.c 475–542](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L475-L542), [reaction.c 571–582](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L571-L582) | A write updates the PC's own slot (slot 0 first), else the first empty slot 1..9. When all are taken by other PCs it replaces the slot whose value is closest to 50, **only if** that slot is closer to 50 than the new value; otherwise the change is dropped. Times are written but never read. |
| R4 | Categories and names | [reaction.c 317–333](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L317-L333), [reaction.h 6–15](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.h#L6-L15) | Seven categories by the thresholds in the Answer, numbered love = 0 … hatred = 6; their display names are entries 0..6 of `mes\reaction.mes` ([reaction.c 151–159](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L151-L159)). |
| R5 | Conversation start and end | [dialog.c 471–505](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L471-L505), [dialog.c 549–553](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L549-L553), [reaction.c 335–382](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L335-L382) | Start copies the PC's stored value into slot 0 with the PC's handle; if it is below 100 and the PC spends the "force good reaction" fate point, the value becomes 100. End clears slot 0's handle and writes the value to the PC's remembered slot. While slot 0 holds another PC, the NPC refuses to speak (`AI_SPEAK_REACTION`, [ai.c 3120–3124](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3120-L3124)); scripts read slot 0 as "NPC is in dialog" ([script.c 1027–1040](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1027-L1040)), and destroying the NPC ends that PC's dialog ([object.c 1306–1310](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1306-L1310)). |
| R6 | Met before | [reaction.c 170–190](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L170-L190), [reaction.c 301–315](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L301-L315) | True for the NPC itself, false for wrong types, else "this PC is in slots 1..9" (slot 0 excluded). So during the first conversation `ME` is still false; any reaction change outside a conversation also makes it true. Forget clears the PC's slot 1..9 only. Readers: dialog `ME` ([dialog.c 1666–1676](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1666-L1676)), SCR "met PC before" ([script.c 729–738](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L729-L738)), greetings (R12) and a critter helper ([critter.c 2269](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2269)). |
| R7 | Dialog RE | [dialog.c 1489–1499](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1489-L1499), [dialog.c 1933–1956](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1933-L1956) | Condition as in the Answer. Action: the first non-space operand character selects `+`/`-` adjust, `>` "raise if below", `<` "lower if above", anything else set-to; all go through R2. The numeric operand is parsed before that switch ([dialog.c 1896](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1896)), so `>v` / `<v` compare against 0, not *v* (A8). The conversation runtime admits the threshold, unsigned set and `+`/`-`; `>`/`<` are refused, and no original DLG uses them (D3 below). |
| R8 | Other reaction writers | [quest.c 449–461](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c#L449-L461), [dialog.c 3519–3531](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L3519-L3531), [script.c 1719–1739](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1719-L1739), [ai.c 984–1036](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L984-L1036), [ai.c 1115–1127](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1115-L1127), [ai.c 1290–1306](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1290-L1306), [ai.c 1547–1551](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1547-L1551), [ai.c 1699–1704](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1699-L1704), [ai.c 3927–3932](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3927-L3932), [combat.c 2168–2174](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2168-L2174), [magictech.c 4071–4096](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4071-L4096), [magictech.c 4174–4177](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L4174-L4177) | Quest accepted: raise to at least 41; completed: +10 (already ported). Dialog insult: −10, and the NPC ends the talk unless reaction stays above 20. SCR: get, set-to, adjust. Hit by a non-leader PC: −10 when accidental, else lowered to AI parameter 10 (hostility variant); PC hits its own follower: + parameter 9 (A2). NPC upset that the PC attacks someone it sides with (party, good-aligned target, origin, faction): −5. NPC drops its hostility toward a PC (our reading of a decompiled helper, used for example when an animal is charmed): raised to at least 50. Wait timeout: `2 × (charisma − max charisma)`. NPC takes a PC as its combat target: lowered to parameter 10. Witness of a theft: −20, attacks at ≤ parameter 10. Resurrection by a PC: raised to 70 when between 1 and 69. Mind control: −50 at start (A3). Charm: +30 / −30, CE bypasses PC effects for both (A4); an animal is instead calmed on charm, but charm off still applies −30. |
| R9 | Barter and money | [reaction.c 384–439](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L384-L439), [item.c 919–925](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L919-L925), [dialog.c 3300–3307](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L3300-L3307), [inven_ui.c 4277–4296](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L4277-L4296), [inven_ui.c 4347–4349](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L4347-L4349) | Prices scale by reaction: ×2 below 0, ×(200 − 2r)% below 50, else ×(120 − 2r/5)% with r capped at 100 (100% at 50, 80% at 100). Afterwards a bought item costs at least worth + 1 and never 1 or 2 (then 3, [item.c 923–929](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L923-L929)); a dialog money request of 1 becomes 2. A gold surplus above 50 over an item's worth in a transfer raises reaction (surplus/40 below 200, then 5 + (surplus − 200)/100, 15 from 1000) only while the category compares below "amiable" in the source's enum order, which is **love only** (A7); the gamble path with flag 0x08 (our reading: a lost gamble) lowers it by worth/100 truncated, −2 when that is 0, at most −30. Which party is which in the transfer helper was not decoded. |
| R10 | Reputation reaction sum | [reputation.c 319–361](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L319-L361), [critter.c 211–218](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L211-L218), [critter.c 328–335](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L328-L335) | For each PC reputation, each effect whose origin is 0 or the NPC's origin **and** whose faction is 0 or the NPC's faction adds its adjustment. All matching effects of all reputations stack. Not skipped for aloof or monstrous NPCs. |
| R11 | Add, remove, has | [reputation.c 415–513](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L415-L513), [dialog.c 1817–1827](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1817-L1827), [dialog.c 2154–2160](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2154-L2160), [script.c 1145–1160](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1145-L1160), [script.c 2154–2171](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2154-L2171) | Add appends number and current game time, and is a no-op when already held (the first time is kept). Remove deletes the entry and shifts later ones, no-op when absent. Both light the logbook button for the local PC. Dialog `RP n` condition: holds *n* (n > 0) / does not hold −*n*; action: add *n* / remove −*n*. The logbook lists them in stored order, which is acquisition order ([reputation.c 390–408](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L390-L408)). No range check: any number is stored, and R10/R12/R13 index the table with `number − 1000` (A5). |
| R12 | Greetings | [dialog.c 3669–3727](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L3669-L3727), [reputation.c 522–625](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L522-L625), [dialog.c 584–600](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L584-L600) | On the generated greeting path (after the higher-priority greeting kinds), with 20% chance a reputation is picked at random among those having a matching effect whose sign agrees with the reaction: negative effect while suspicious or worse, positive while courteous or better (neutral picks none). As read, the pick returns 1000 + the reputation's **position in the PC's list**, not its number (A9). The line comes from one of four files by NPC/PC gender pair; an empty line falls back. The fallback greeting is chosen by met-before × category (love; amiable/courteous; neutral; suspicious/dislike; hatred). Farewell: neutral or better versus worse. |
| R13 | Faction membership | [reputation.c 633–661](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L633-L661), [critter.c 235–321](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L235-L321), [critter.c 357–361](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L357-L361) | PC and NPC are "same faction" when the NPC's PC leader is that PC, or the NPC's faction is non-zero and one of the PC's reputations lists it. Two PCs never are. Same origin needs a non-zero origin. AI uses this for kill-on-sight, protection, "upset attacking" and the last-hitter record ([ai.c 3293–3299](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3293-L3299), [ai.c 3454–3466](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3454-L3466), [ai.c 3511–3519](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3511-L3519)). |
| R14 | Reputation table load | [reputation.c 29–47](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L29-L47), [reputation.c 118–146](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L118-L146), [reputation.c 171–312](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reputation.c#L171-L312) | Per module: optional `rules\gamerep.mes` (entries 1000..1999), then `mes\gamereplog.mes` (logbook names) and four `mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes` (greetings). Entry text: comma-separated tokens; the first holds three faction numbers, each further token one effect of three numbers. A missing faction value aborts the whole parse (earlier entries stay), and then, like a missing `gamerep.mes`, the logbook names and greetings are not loaded either; a missing `gamereplog.mes` also skips the greetings. As read, after each number the parser skips spaces then **digits only**, so a token starting with `+` or `-` makes the next fields re-read the same number: the adjustment is right, origin and faction become equal to it (P1). |
| R15 | Presentation | [object.c 995–998](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L995-L998), [intgame.c 6500–6512](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L6500-L6512), [combat.c 3797–3803](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3797-L3803) | Hovered NPCs are tinted by category; an info line shows "reaction: value (name)"; turn-based combat marks each critter with its category. |
| R16 | Dialog-text side effect | [quest.c 318–326](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c#L318-L326) | Quest dialog lines switch to the "bad reaction" variant below 20 (after the low-intelligence variant). |

## Source anomalies and decisions

- **P1 Signed reputation effects.** R14 as read, applied to the owner's data:
  62 of 93 effects get origin = faction = adjustment, so each matches only an
  NPC whose origin **and** faction both equal its adjustment.
  NPC origin and faction values were not surveyed, so how many NPCs that is
  (likely none for negative values) is inferred, not measured. Only the 31
  unsigned effects keep their authored scope. CE notes its loop differs
  from the original code "but is the same thing"; no original observation
  exists. **D1 (owner decision)**: reproduce the source parse (parity
  candidate) or the authored intent (every effect scoped as written). Proposal:
  keep both readings in the parsed model, select by an explicit named policy,
  and default to the source parse until an original-executable observation
  says otherwise.
- **A2 Follower hit sign.** CE's own field comment says parameter 9 is how much
  a deliberate hit *lowers* a follower's reaction, but the code adds it, and
  the data values are 0..10 (none negative). The companion research C13
  repeated the comment. Open until an original observation; do not encode
  either sign as fact.
- **A3 Mind control.** −50 is applied when control starts and nothing restores
  it when control ends, while reaction changes during control are ignored
  (R2). Recorded as source behaviour, not reviewed for intent.
- **A4 Charm reversibility.** CE deliberately deviates: it bypasses the PC's
  good/bad reaction effects for charm on and off, because the original
  asymmetry left a permanent change (for example +10 for a PC with a
  `badreactionadj +10` race effect: +30 on, −20 off). **D2 (owner decision)**: CE behaviour or
  original asymmetry. Proposal: CE behaviour, labelled as a fix.
- **A5 Unvalidated numbers.** Reputation numbers are not range-checked
  (R11); 20 `RP` actions in 3 original DLG files use operands with magnitude 1
  or 2 (L5). In CE they would index outside the table. Proposal: the reputation
  owner admits 1000..1999 only and a dialog `RP` outside it is a typed
  refusal, like the existing quest and rumor domains.
- **A6 Script set-reaction operand.** The SCR set-to action reads its value
  with the third operand's type but the **second** operand's value
  ([script.c 1726–1731](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1726-L1731)).
  Whether that is a reconstruction slip or original behaviour is open; it
  matters only when an SCR uses that action.
- **A7 Gift category test.** The categories are numbered love = 0 … hatred
  = 6, and the gift rule of R9 raises reaction only when the category number
  is below amiable, that is only for an NPC that already loves the PC. The
  likely intent ("not yet amiable") is the opposite. Open, like A2.
- **A8 RE comparison operand.** The action operand is read with `atoi` on
  text that still starts with `>` or `<`, which yields 0, so CE's `RE >v`
  raises a negative reaction to 0 and `RE <v` lowers a positive one to 0,
  whatever *v* is. Intent and original behaviour are open.
- **A9 Greeting reputation number.** The greeting pick remembers the list
  position of each qualifying reputation and returns 1000 + that position, so
  the greeting line belongs to whichever reputation has that number, not
  necessarily the one that qualified (unless the PC's list happens to be
  1000, 1001, …). Open.
- **D3 Comparison forms.** No original DLG uses `RE >v` / `RE <v`; the
  conversation runtime may keep refusing them without losing content.

## Local observations (owner's installation)

| ID | Observation |
| --- | --- |
| L1 | `rules\gamerep.mes` resolves to the module archive `Arcanum.dat` (1 candidate, 3,206 bytes, SHA-256 `d11fe290…fc570429`): 76 reputations, numbers 1000..1075, none missing faction values; 5 list at least one membership faction (4 distinct factions). 93 effects: 62 reputations have 1, 13 have 2, 1 has 5. 42 effects are negative (in 30 reputations), 18 reputations have a positive effect. Scope as authored: 37 any/any, 35 origin only, 10 faction only, 11 both; 12 distinct origins, 13 distinct factions. 62 effects carry an explicit sign (42 `-`, 20 `+`) and change under the CE parse (P1). |
| L2 | `mes\gamereplog.mes` (module, `8301681d…c42ac0ae`) names all 76. Each of the four greeting files (module) covers 70 of the 76 (1000..1069). |
| L3 | `mes\reaction.mes` (`arcanum3.dat`, `da8fa015…6514a81`) has entries 0..6. `rules\ai_params.mes` (`arcanum3.dat`, `96684d94…66fb127`): 41 rows numbered 0..40, 17 values each; parameter 5 has 6 distinct values, some of them about ±1000 (so some rows effectively never or always refuse); parameter 9 has 3 distinct values, none negative; parameter 10 has 2 distinct values, none positive. The module's `rules\gameai.mes` adds one row numbered 100. |
| L4 | `rules\effect.mes` (`arcanum3.dat`, `8ac43304…db0b6b2`, 309 entries): 7 mention `badreactionadj`, 5 `goodreactionadj`, 1 `beautyreaction`, 5 a standalone `reaction` change. The module `rules\gameeffect.mes` has 1 entry and none of these. |
| L5 | DLG census over the winning copies of 709 DLG names (671 read by the current reader; 38 refused by its strictness rules and not counted). Player conditions: `RE` ≥ 782 / ≤ 895, `ME 0` 374, `ME 1` 367, `RP` has 90 / lacks 46. Player actions: `RE -` 1,316, `RE +` 893, unsigned 216, `>`/`<` 0; `RP` add 57, remove 24. NPC actions: `RE -` 31, `+` 40, unsigned 37; `RP` add 22, remove 2. 20 `RP` action operands in 3 files have magnitude below 1000 (1 or 2), outside the 76 numbers of L1. |
| L6 | Virgil's Crash Site DLG (`dlg\01324Virgil.dlg`, payload `34a49a2c…`, as in [NDE-06](crash-site-npc-dialog-evidence.md)) uses only `RE` among these opcodes: 3 `RE ≥`, 9 `RE ≤` conditions and 4 `-`, 4 `+`, 3 unsigned actions; no `ME` and no `RP`. |

## What this means for the current port

- The conversation runtime's `reaction.pc` is a single stored value seeded with
  the diagnostic 50. Per R1 it is the **stored** value only. For the
  diagnostic profile (human PC and, presumably, human Virgil, no beauty, no
  reputations, no effects) stored and effective coincide, so today's results
  stand; they diverge as soon as race, beauty, reputation or effects exist.
- Unsigned `RE v` and the quest rule "at least 41" are deltas against the
  effective value (R2), so a later owner must apply them as deltas, not by
  writing *v*.
- `ME` needs the conversation start/end of R5 and R6; it cannot be derived
  from "a value is stored".
- The follow consent (companion C2) needs Virgil's AI-data row (parameter 5)
  and his effective reaction; both stay open (O1).

## Open questions

- **O1** Virgil's reaction base, race, origin, faction, aloof/monstrous flags
  and AI-data row: they need a bounded read-only probe of his instance and
  prototype (shared with companion O1).
- **O2** P1 against the original executable: does a signed effect keep its
  origin and faction there?
- **O3** A2 (follower hit sign), A6 (script operand), A7 (gift test), A8 (RE
  comparison operand) and A9 (greeting number), same kind of observation.
- **O4** Fate-point flow (when "force good reaction" is armed and consumed)
  and the higher-priority greeting kinds before R12 were not read.
- **O5** The race matrix and beauty table are source tables, not data files;
  only their shape and order are recorded here. An implementation reads them
  from the source with citation, like the character stats owner did for the
  beauty table.
- **O6** Which party is buyer and seller in the R9 transfer helper, and the
  rounding of the price formula against original prices.

## Proposed implementation slices (project policy, not accepted)

Each slice is a separate PR. Data/Simulation/Scripting/Session stay free of
Godot.

1. **Reaction rules (Simulation)**: a pure effective-reaction function over
   explicit inputs (stored or base, NPC race, PC race, PC reaction modifier
   from the [character stats owner](../world/character-stats-runtime.md),
   reputation sum, NPC reaction effects, enemy-list and mind-control facts,
   aloof/monstrous), categories, the hostility variant, and the delta rule with
   PC good/bad effects. Synthetic tests vary one input at a time (FR-SOCIAL-001
   acceptance).
2. **Reaction state (Session)**: per NPC instance the stored value, the
   conversation slot and "met before", with begin/end conversation, adjust,
   set-to (as a delta), forget; a new snapshot schema replaces the
   `reaction.pc` map change. Single PC first; the 9-slot eviction (R3) stays
   documented, not built.
3. **Dialog binding**: `ME`, `RE` actions as deltas, the insult rule, and the
   quest rule through the owner.
4. **Reputation data and owner**: a Data reader for the four file kinds with
   the D1 policy explicit, a PC reputation list with game time, add/remove/has,
   `RP` condition and action with the A5 domain, faction membership and the
   reaction sum. The greeting pick needs a random port and speech text and can
   follow later.
5. **Consumers**: the JO consent (companion slice 2) reads the effective
   reaction and parameter 5; AI hostility, barter prices and hover colour come
   with their own domains.

## Method

Source reading at the pinned commit above: `reaction.c` and `reputation.c` in
full, every caller of their public functions found by searching `src/`, and the
helpers named in each row. No CE code or structure was copied; names in this
document are descriptive.

The local observations come from a scratch program (not committed) that opened
the owner's installation with `ContentMountPlan.Discover` + `ContentStack.Open`
(module `Arcanum`), resolved each path above to its winning copy, and printed
size, SHA-256 and counts. MES entries were taken as `{number}{text}` pairs.
Reputation tokens were split on commas; "authored" values are the
whitespace-separated numbers of each token, "CE parse" emulates R14
(C `atoi`, then skip spaces and digits). DLG names were collected from every
mounted archive and `dlg` folder and resolved through the stack; each record's
condition and action fields were split on commas, and a token counted when its
first two letters (case-insensitive) were `re`, `rp` or `me`. The sign or
comparison prefix was taken from the first non-space character after the
opcode. Files the current `DlgReader` refuses were skipped and are counted
as such. `RP` operands were also compared, by absolute value, with the
numbers present in `rules\gamerep.mes`.
