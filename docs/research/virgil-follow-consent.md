# Virgil's follow consent: AI row, level, alignment and JO/UW operands

## Status, scope and evidence discipline

Status: **Research**, work item ARC-RES-026 (2026-10-16). It answers open
questions **O1** and **O2** of the [companion research](companion-follow-evidence.md#open-questions)
(ARC-RES-024): which values Virgil feeds into the follow check, and how his
dialog calls it. It is a read-only probe plus this note. It changes no
production API, viewer, conversation runtime, `Arcanum.Session` or launcher
file, and it adds no consent evaluator or party state. Requirements:
[FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

Categories, as in [ARC-RES-023](crash-site-item-inventory.md):

- **Source (S):** read from `alexbatalov/arcanum-ce` at
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
  (Sustainable Use License 1.0, not MIT). It is a reconstruction lineage, not
  original-executable parity; decompiler names (`sub_…`) are read for intent
  and marked as such. No CE code or structure is copied.
- **Data (D):** the authenticated normal-game Crash Site aggregate, the six
  pinned archives and the installed content stack, read by the probe below. T0
  is the serialized state before any tick, script or player action.
- **Proposal (P):** a project recommendation, not accepted gameplay policy.
- **Unknown (U):** a named gap with its next check.

No original text or private path is published. The report holds numbers,
booleans, two-letter operation codes and SHA-256 digests; MES and DLG text is
parsed in memory only. Original executable parity remains **NOT_VERIFIED**.

## Answer

- **O1 (D + S).** Virgil (aggregate source index 2907, prototype 17102) uses
  **AI row 5**, whose follow fields are: dislike at reaction ≤ **−1000**,
  alignment gap limits **2001** in both directions, and a level gate of
  **30**. At T0 he is **level 1**, **alignment 1**, charisma 8, human, male,
  with **wait index 6**, reaction base **60**, no leader, no reaction memory
  and none of the forced, waiting, jilted, spread-out, check-leader or aloof
  bits. His two effects touch neither level, alignment nor reaction.
- **O2 (D).** All **21 `JO`** and all **4 `UW`** in his DLG have first
  operand **0**: none forces the join and none ignores the charisma limit. The
  `JO` nodes are 74 (15 replies), 535 (4) and 547 (2); every `UW` is
  `UW 0 518` on a reply conditioned on `WA 1`.
- **Consequence (S applied to D).** For a real PC the follow check cannot
  refuse Virgil on alignment (any two clamped alignments differ by at most
  2000), on level (it would need him 30 levels above the PC) or on dislike in practice (reaction would have to
  fall 1,060 points below his base). "Not allowed" is unreachable as read,
  because effective max followers is clamped to at least 1. The only refusals
  left are "limit reached" (the PC already leads as many non-forced followers
  as his max followers allows) and "already in group" (another leader, PC or
  NPC, with at least the PC's charisma). At the start of a new game the PC leads no one, so his
  first `JO` succeeds. Wait index 6 means a waiting Virgil never times out,
  so the C12 timeout anomaly does not affect him.

## Inputs and reproduction

The inputs are the six pinned archives of the [cave research](cave-transition-evidence.md)
(module, resource1..4, PATCH0), the normal-game exterior aggregate
(`a548f611…de6a5`, 1,823,280 bytes), the prototype directory, and the game
root for the [content stack](content-precedence.md) (ARC-DATA-011) that
resolves each MES file and the DLG by original precedence. The DLG's *name* is
found by scanning the six archives for script number 1324, so a loose
`dlg/01324*.dlg` with another name would be missed; the probe fails closed if
any stack archive or any candidate is not one of the pinned inputs. The probe opens the
strict Area snapshot through `AreaOriginalValidator.OpenFiles`, so absent
instance fields inherit the authenticated prototype value. It compares all
archive and aggregate fingerprints before and after the run.

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --companion-consent-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --companion-consent \
  --game-root "$GAME" --prototype-root "$GAME/data/proto" --mobile "$GAME/modules/Arcanum/maps/<exterior aggregate>" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE/consent.json"
```

Each resource below has exactly one candidate in the content stack, so no
override decides any value here.

| Resource | Source | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `rules/ai_params.mes` | resource3 | 4,780 | `96684d94…6fb127` |
| `rules/gameai.mes` | module | 1,384 | `082341fb…a821826` |
| `rules/effect.mes` | resource3 | 17,497 | `8ac43304…0b6b2` |
| `rules/gameeffect.mes` | module | 417 | `f8c921b7…ead0a85` |
| `dlg/01324virgil.dlg` | resource3 | 56,782 | `34a49a2c…5e53a` |

The DLG is the same payload as in the [NPC dialog evidence](crash-site-npc-dialog-evidence.md)
(409 records).

## 1. How the check reads its inputs (S)

| ID | Claim | Source |
| --- | --- | --- |
| S1 | The NPC's `OBJ_F_NPC_AI_DATA` value is a row index into one table of 17-integer rows. | [ai.c 36–59](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L36-L59), [ai.c 1564–1573](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1564-L1573) |
| S2 | Rows 0..99 come from `rules\ai_params.mes`, rows 100..149 from the module's `rules\gameai.mes`. Loading needs an entry numbered exactly 0 (100); each later entry in number order takes the next row, and each row reads up to 17 integers split on space, tab and newline with `atoi`. | [ai.c 192–228](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L192-L228), [ai.c 236–275](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L236-L275), [mes.c 191–215](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L191-L215), [mes.c 287–305](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/mes.c#L287-L305) |
| S3 | Level, alignment and charisma are stored stats (`OBJ_F_CRITTER_STAT_BASE_IDX` entries 17, 19 and 7), then adjusted by the critter's effects and clamped. Alignment is clamped to −1000..1000. Max followers is derived as charisma / 4, +1 at Persuasion expert, then clamped to **1..7**. | [stat.c 43–72](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L43-L72), [stat.c 336–381](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L336-L381), [stat.c 79–108](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L79-L108), [stat.c 512–530](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L512-L530), [stat.c 891–896](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L891-L896), [stat.c 601–613](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L601-L613) |
| S4 | Effects come from `rules\effect.mes` (50..399) and the module's `rules\gameeffect.mes` (0..49). An entry names the stats it changes by symbol (`level`, `alignment`, `ch`, …); the NPC-side reaction adjustment is the special `reaction` symbol. | [effect.c 87–116](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L87-L116), [effect.c 174–185](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L174-L185), [effect.c 257–330](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L257-L330), [effect.c 360–470](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L360-L470), [effect.c 732–830](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L732-L830), [effect.c 930–933](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L930-L933) |
| S5 | The reaction the check compares is, as we read the decompiled helpers: the stored level for this PC, or the NPC's reaction base when he has none, plus (unless the NPC is aloof or monstrous) a race-pair term and the PC's beauty reaction modifier, plus the PC's reputation term, then the NPC's reaction effects. A PC on the NPC's shit list reads 0. | [reaction.c 205–235](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L205-L235), [reaction.c 442–475](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L442-L475), [reaction.c 545–570](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L545-L570), [race table 30–142](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/reaction.c#L30-L142), [beauty modifier stat.c 179–200, 588–600](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L179-L200) |
| S6 | A dialog action reads its first operand with `atoi` right after the two-letter code. `JO` / `UW` read the node by skipping spaces and the first operand's digits, then `atoi` again; a sign is not skipped and a comma before the node reads 0. `JO a n` checks with `a` as "ignore the charisma limit" and joins with `a` as `force`; `UW a n` does the same for unwait. | [dialog.c 1862–1900](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L1862-L1900), [dialog.c 2081–2141](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2081-L2141), [dialog.c 2246–2257](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2246-L2257) |
| S7 | An unforced join and an unforced unwait each run the same check again (with the charisma limit), so a `JO 0` / `UW 0` is decided by one consent result. | [critter.c 877–928](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L877-L928), [ai.c 1505–1530](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1505-L1530) |

## 2. Virgil at T0 (D)

Fields come from the strict snapshot; *origin* says whether the instance record
or his prototype supplied the value.

| Field | Value | Origin |
| --- | --- | --- |
| AI data (row) | 5 | instance |
| Wait index | 6 | instance |
| Reaction base | 60 | instance |
| Reaction PC / level lists | null (no memory of any PC) | prototype |
| Leader, followers | null | prototype |
| NPC flags | `0x42` (day waypoints, check wield); no wait, spread-out, jilted, forced, check-leader or aloof bit | instance |
| Critter flags | `0x08000000` (no flee); not undead, animal, monster or mechanical | instance |
| Critter flags 2 | 0 (no loyalty test armed) | prototype |
| Origin, faction, social class | 0, 0, 7 | prototype |
| Auto-level scheme | 91 | instance |
| Stats | level **1**, alignment **1**, charisma 8, beauty 8, gender 1 (male), race 0 (human), age 28; the other primary stats 8 | instance (whole array) |
| Effects | 82 (cause class: strength, intelligence), 64 (cause race, the human race effect: no stat) | instance |

Neither effect names `level`, `alignment`, `ch` or `reaction`, so his
effective level and alignment equal the stored values.

**AI row 5** (entry 5 of `ai_params.mes`; all 41 engine rows and the one
module row are numbered by their own entry, so the running count of S2 and
the entry number agree here):

| Field | 0 | 1 | 2 | 3 | 4 | **5** | **6** | **7** | **8** | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Value | 0 | 1000 | 1000 | 101 | 1 | **−1000** | **2001** | **2001** | **30** | 0 | −1000 | 2001 | 2001 | 50 | 20 | 0 | 1 |

Field meanings follow the source's comments on [ai.c 36–59](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L36-L59):
5 = refuse at reaction ≤ value, 6 / 7 = largest PC-above / PC-below alignment
gap, 8 = refused when the NPC is at least this many levels above the PC,
9 = reaction drop when his leader hits him.

## 3. JO and UW in his DLG (D)

| Operation | Node | Replies (record ids) | Reply condition | Reply response |
| --- | --- | --- | --- | --- |
| `JO 0 74` | 74 | 72, 119, 121, 166, 176, 206, 211, 218, 226, 229, 236, 237, 356, 361, 372 | none | exit (0) |
| `JO 0 535` | 535 | 485, 486 | `PA -6409` | 533 |
| `JO 0 535` | 535 | 528, 529 | none | 533 |
| `JO 0 547` | 547 | 539, 540 | none | 533 |
| `UW 0 518` | 518 | 392, 393, 512, 513 | `WA 1` | 533 |

Every operation sits in a player-reply action field, every target node is an
NPC record, and no operand overflows. The `JO` operand form is the one the
[conversation runtime](../scripting/dialog-conversation-runtime.md) already
admits (`JO 0 node`, the report's `jo_zero_node_form`); this probe does not
re-check whole-field admission. The
four `UW` stay `UnsupportedOperation` there today. On a refusal the source
keeps the chosen reply's response ([C1](companion-follow-evidence.md#findings)), so for the fifteen node-74
replies the generated refusal reply would end the conversation.

His DLG's reaction actions are `RE 61` (set, records 4, 5, 7) and deltas
+10, +15 and −5 (records 471–482); none comes near −1000.

## 4. What this means for the consent check (S applied to D)

Order and reasons as in [C2](companion-follow-evidence.md#findings). For a
real PC talking to Virgil at T0:

| Step | Refusal | Reachable? |
| --- | --- | --- |
| Mind control, Persuasion master | (accept) | PC-side shortcuts; not needed |
| Other leader with charisma ≥ PC's | already in group | No at T0 (no leader); only with another leader, PC or NPC (for example set by a script) |
| Forced follower | (accept) | No: the bit is clear and every operand is 0 |
| Reaction ≤ −1000 | dislike | Not in practice: his T0 reaction for a fresh PC is 60 + race term (−5..+10 for a human NPC) + beauty modifier (−65 at beauty 1) + reputation (0), at least −10; the lowest his DLG's reaction actions produce is a set to 61 or a −5 delta (PC reaction effects can scale deltas; not read) |
| Alignment gap > 2001 | too good / too bad | No: alignments are clamped to ±1000, so any gap is at most 2000 (1001 with his alignment 1) |
| Level ≥ PC level + 30 | low level | No: his level never exceeds the PC's ([ARC-RES-031](virgil-auto-level.md), answering U3) |
| Max followers 0 | not allowed | No as read: the clamp keeps max followers ≥ 1 |
| Non-forced followers ≥ max | limit reached | Yes, when the PC already leads that many; never at the start of a new game |

While he follows, two checks of C13 apply. The armed "check leader" test
(reaction ≤ −980, alignment gap > 1901) only floats a warning. Leaving goes
through the consent check without the charisma limit
([ai.c 2412–2443](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2412-L2443),
also after a hit, [ai.c 996–1015](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L996-L1015)),
so it refuses only on dislike (≤ −1000), an alignment gap above 2001 or the
30-level gate. As read Virgil therefore never leaves on his own (the level gap
cannot arise, [ARC-RES-031](virgil-auto-level.md)); `LV` or a script disband ends the link. A hit from his
leader lowers his reaction by 0 (field 9). Wait index 6 schedules no timer
under any Persuasion bonus ([ai.c 1472–1485](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1472-L1485)),
so the timeout question O3 does not apply to him.

The project's diagnostic PROJECT profile accepts every follow request. For
Virgil with a lone PC that matches the source result; it differs only when the
PC is already at his follower limit.

## Proposal for implementation (P)

1. Keep slice 2 of the [companion research](companion-follow-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
   (consent evaluator) general: all eight results and the 1..7 clamp, driven
   by explicit inputs. Import AI rows by the S2 rule rather than by entry
   number alone, even though the two agree for the installed tables.
2. Give Virgil's real inputs (row 5, level 1, alignment 1, reaction base 60,
   wait index 6) to that evaluator from the Area snapshot, and replace the
   PROJECT profile's blanket accept with it. Synthetic tests should still
   cover each refusal with made-up rows, because Virgil exercises only the
   limit check.
3. Admit `UW 0 node` next to `JO 0 node` once party state exists; no `JO a≠0`
   or `UW a≠0` is needed for Virgil.

## Unknowns (U)

- **U1** The other critters' AI rows and the rest of row 5 (flee, combat,
  portals) are not interpreted here.
- **U2** Reaction changes outside his DLG (theft, combat with others, quest
  scripts) and the reputation term later in the game were not read.
- **U3** How auto-level scheme 91 raises his level as the game goes on was not
  read; it only matters if a 30-level gap could ever arise. Answered by
  [ARC-RES-031](virgil-auto-level.md): the scheme only spends points, his level
  moves with the PC's and can never exceed it, so the gap cannot arise.
- **U4** O3–O7 of the companion research stay open, except that O3 no longer
  concerns Virgil.

## Method

Source reading at the pinned commit, through the call chains named in each
row, plus the read-only probe `--companion-consent` (self-test
`--companion-consent-self-test` covers the operand reader, the code scan, the
AI row rule and the effect symbol scan). No CE code or structure was copied;
names in this document are descriptive.
