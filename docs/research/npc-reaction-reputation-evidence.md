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

- **Source fact**: read from the executable's functions at the addresses
  given, level F0 (not checked in the executable) unless a
  [verification ledger](original-verification-ledger.md) row is cited; earlier
  provenance is in git history. Several functions have no known name; a
  reading of their intent is marked as such.
- **Local observation**: aggregate counts and SHA-256 digests measured on the
  owner's unchanged installation by an uncommitted scratch reading through the
  merged [`ContentStack`](content-precedence.md) and
  [`DlgReader`](../formats/dlg-reader.md). It printed counts and digests only,
  never text or values of individual records. The counting rules are in
  [Method](#method) so the numbers can be reproduced.
- **Project policy**: a choice this project makes where the source leaves a
  runtime or product decision open, or where it looks defective. Every policy
  below is a **proposal**.

Original executable parity remains **NOT_VERIFIED**, except the rows the
[verification ledger](original-verification-ledger.md#reaction-runtime-arc-world-026) checked for ARC-WORLD-026
(R1–R6, R8 hit and quest): they behave as read here, except that the original applies the **PC's** `reaction`
effects in R1, not the NPC's.

## Answer

- **Stored state lives on the NPC**: a base value (instance field, prototype
  default 50) and a small memory of per-PC values. The effective reaction is
  **not stored**: every query recomputes it from the stored value plus race,
  the PC's beauty-derived reaction modifier (stat 0x4B0740), the PC's reputations and the NPC's
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
- **Source anomaly with data impact** (P1): as first read, the parser of
  `rules\gamerep.mes` stops at a sign. On the owner's data **62 of 93**
  effects carry an explicit `+` or `-`, so that reading stores their origin and faction
  as the adjustment value itself and they match almost no NPC. Whether the
  original executable does the same was unknown; ARC-RES-048 found that it
  does not (a reconstruction slip, ledger row Reputation P1), and decision D1
  keeps every effect's written scope.

## Stored state (source facts)

| State | Owner | Shape | Written by |
| --- | --- | --- | --- |
| Reaction base | NPC instance | one integer; text-prototype default 50 (prototype 0x49B5A0) | prototype / instance data only; no runtime writer was found |
| Reaction memory | NPC instance | 10 parallel slots: PC handle, value, game time (NPC object fields). Slot 0 = PC currently in conversation; slots 1..9 = remembered PCs | conversation start/end, every reaction change, forget |
| Origin, faction | NPC instance | one integer each, 0 = none | dialog `OR` sets origin (dialog 0x415BA0), SCR set-faction (script 0x447220), prototype, map object patches (map 0x411940); a summon gets faction 0 or the faction of an object from its summon record, which we read as the summoner (magic-tech 0x450C40) |
| AI parameter row | NPC instance → table | 17 integers per row (companion C3); parameters 5, 9 and 10 concern reaction | data |
| Reputations | PC | ordered list of numbers plus parallel game times (PC object fields) | dialog `RP`, script add/remove |
| Reputation table | module data | per number: 3 faction numbers, up to 5 effects | `rules\gamerep.mes` 1000..1999 at module load |

Single player uses one memory slot per NPC at most; the slot rules matter only
for several PCs (R3).

## Findings

| ID | Claim | Source fact | Consequence for Steamwoven |
| --- | --- | --- | --- |
| R1 | Effective reaction | reaction 0x4C0CC0, 0x4C0CE0, 0x4C0D00, 0x4C1290, 0x4C12D0, 0x4C12F0, 0x4C1500, effect 0x4EAA80 | Order: enemy list → 0; non-PC / non-NPC → 50; stored = this PC's slot (slot 0 included) else the base; plus the race matrix entry (row = NPC race, 11 rows; column = PC race, 8 columns) and the PC's reaction-modifier stat, both skipped for aloof or monstrous NPCs; plus the reputation sum (R10); then the NPC's own `reaction` effects (percent, multiply, divide, add, min, max in that order, effect 0x4EA6C0); finally a floor of 50 when this PC mind-controls the NPC. A second variant used by AI hostility checks floors the race + modifier part at −49 before adding reputation. The query is a pure function of explicit inputs and needs no clamp. |
| R2 | Reaction change | reaction 0x4C0DE0, effect 0x4EAAB0 | No-op for delta 0, wrong types, an NPC mind-controlled by this PC, and a negative delta when the PC has extraordinary charisma and is the NPC's PC leader. Otherwise the delta passes through the **PC's** `badreactionadj` (negative) or `goodreactionadj` (positive) effects and is added to the stored value (base when no slot). A negative change while the NPC follows this PC arms the leader check and the reaction-loyalty test (companion C13). The change refreshes the health bar and combat reaction flags. |
| R3 | Memory slots | reaction 0x4C1360, 0x4C1490, 0x4C15A0 | A write updates the PC's own slot (slot 0 first), else the first empty slot 1..9. When all are taken by other PCs it replaces the slot whose value is closest to 50, **only if** that slot is closer to 50 than the new value; otherwise the change is dropped. Times are written but never read. |
| R4 | Categories and names | reaction 0x4C0FC0, 0x4C1010 and the category enumeration | Seven categories by the thresholds in the Answer, numbered love = 0 … hatred = 6; their display names are entries 0..6 of `mes\reaction.mes` (reaction 0x4C0BD0). |
| R5 | Conversation start and end | dialog 0x412FD0, 0x413280, reaction 0x4C1020, 0x4C10A0, 0x4C1110 | Start copies the PC's stored value into slot 0 with the PC's handle; if it is below 100 and the PC spends the "force good reaction" fate point, the value becomes 100. End clears slot 0's handle and writes the value to the PC's remembered slot. While slot 0 holds another PC, the NPC refuses to speak (`AI_SPEAK_REACTION`, AI 0x4AD800); scripts read slot 0 as "NPC is in dialog" (script 0x4451B0), and destroying the NPC ends that PC's dialog (object 0x43CCA0). |
| R6 | Met before | reaction 0x4C0C40, 0x4C0F50 | True for the NPC itself, false for wrong types, else "this PC is in slots 1..9" (slot 0 excluded). So during the first conversation `ME` is still false; any reaction change outside a conversation also makes it true. Forget clears the PC's slot 1..9 only. Readers: dialog `ME` (dialog 0x4150D0), SCR "met PC before" (script 0x4451B0), greetings (R12) and a critter helper (critter 0x45F970). |
| R7 | Dialog RE | dialog 0x4150D0, 0x415BA0 | Condition as in the Answer. Action: the first non-space operand character selects `+`/`-` adjust, `>` "raise if below", `<` "lower if above", anything else set-to; all go through R2. The numeric operand is parsed before that switch (dialog 0x415BA0), so `>v` / `<v` compare against 0, not *v* (A8). The conversation runtime admits the threshold, unsigned set and `+`/`-`; `>`/`<` are refused, and no original DLG uses them (D3 below). |
| R8 | Other reaction writers | quest 0x4C4E60, dialog 0x4191E0, script 0x447220, AI 0x4A9650, 0x4A9C00, 0x4AA300, 0x4AA990, 0x4AABE0, 0x4AEE50, combat 0x4B58C0, magic-tech 0x454A10 | Quest accepted: raise to at least 41; completed: +10 (already ported). Dialog insult: −10, and the NPC ends the talk unless reaction stays above 20. SCR: get, set-to, adjust. Hit by a non-leader PC: −10 when accidental, else lowered to AI parameter 10 (hostility variant); PC hits its own follower: + parameter 9 (A2). NPC upset that the PC attacks someone it sides with (party, good-aligned target, origin, faction): −5. NPC drops its hostility toward a PC (our reading of a decompiled helper, used for example when an animal is charmed): raised to at least 50. Wait timeout: `2 × (charisma − max charisma)`. NPC takes a PC as its combat target: lowered to parameter 10. Witness of a theft: −20, attacks at ≤ parameter 10. Resurrection by a PC: raised to 70 when between 1 and 69. Mind control: −50 at start (A3). Charm: +30 / −30, both through the PC's effects in the original (ledger row R2 of the reaction runtime; the earlier reading bypassed them, A4); an animal is instead calmed on charm, but charm off still applies −30. |
| R9 | Barter and money | reaction 0x4C1150, 0x4C11D0, item 0x461F80, dialog 0x418A40, inventory UI 0x578EA0 | Prices scale by reaction: ×2 below 0, ×(200 − 2r)% below 50, else ×(120 − 2r/5)% with r capped at 100 (100% at 50, 80% at 100). Afterwards a bought item costs at least worth + 1 and never 1 or 2 (then 3, item 0x461F80); a dialog money request of 1 becomes 2. A gold surplus above 50 over an item's worth in a transfer raises reaction (surplus/40 below 200, then 5 + (surplus − 200)/100, 15 from 1000) only while the category compares below "amiable" in the source's enum order, which is **love only** (A7); the gamble path with flag 0x08 (our reading: a lost gamble) lowers it by worth/100 truncated, −2 when that is 0, at most −30. Which party is which in the transfer helper was not decoded. |
| R10 | Reputation reaction sum | reputation 0x4C1AC0, critter 0x45D080, 0x45D290 | For each PC reputation, each effect whose origin is 0 or the NPC's origin **and** whose faction is 0 or the NPC's faction adds its adjustment. All matching effects of all reputations stack. Not skipped for aloof or monstrous NPCs. |
| R11 | Add, remove, has | reputation 0x4C1CB0, 0x4C1D20, 0x4C1E10, dialog 0x4150D0, 0x415BA0, script 0x4451B0, 0x447220 | Add appends number and current game time, and is a no-op when already held (the first time is kept). Remove deletes the entry and shifts later ones, no-op when absent. Both light the logbook button for the local PC. Dialog `RP n` condition: holds *n* (n > 0) / does not hold −*n*; action: add *n* / remove −*n*. The logbook lists them in stored order, which is acquisition order (reputation 0x4C1C30). No range check: any number is stored, and R10/R12/R13 index the table with `number − 1000` (A5). |
| R12 | Greetings | dialog 0x419260, reputation 0x4C1F80, 0x4C2100, dialog 0x4133B0 | On the generated greeting path (after the higher-priority greeting kinds), with 20% chance a reputation is picked at random among those having a matching effect whose sign agrees with the reaction: negative effect while suspicious or worse, positive while courteous or better (neutral picks none). As read, the pick returns 1000 + the reputation's **position in the PC's list**, not its number (A9). The line comes from one of four files by NPC/PC gender pair; an empty line falls back. The fallback greeting is chosen by met-before × category (love; amiable/courteous; neutral; suspicious/dislike; hatred). Farewell: neutral or better versus worse. |
| R13 | Faction membership | reputation 0x4C21E0, critter 0x45D110, 0x45D320 | PC and NPC are "same faction" when the NPC's PC leader is that PC, or the NPC's faction is non-zero and one of the PC's reputations lists it. Two PCs never are. Same origin needs a non-zero origin. AI uses this for kill-on-sight, protection, "upset attacking" and the last-hitter record (AI 0x4ADCC0, 0x4AE120, 0x4AE3A0). |
| R14 | Reputation table load | reputation 0x4C1620, 0x4C1730 and their static tables | Per module: optional `rules\gamerep.mes` (entries 1000..1999), then `mes\gamereplog.mes` (logbook names) and four `mes\game_rp_npc_{m2m,m2f,f2f,f2m}.mes` (greetings). Entry text: comma-separated tokens; the first holds three faction numbers, each further token one effect of three numbers. A missing faction value aborts the whole parse (earlier entries stay), and then, like a missing `gamerep.mes`, the logbook names and greetings are not loaded either; a missing `gamereplog.mes` also skips the greetings. As read, after each number the parser skips spaces then **digits only**, so a token starting with `+` or `-` makes the next fields re-read the same number: the adjustment is right, origin and faction become equal to it (P1). |
| R15 | Presentation | object 0x43C270, in-game UI 0x553F70, combat 0x4B80E0 | Hovered NPCs are tinted by category; an info line shows "reaction: value (name)"; turn-based combat marks each critter with its category. |
| R16 | Dialog-text side effect | quest 0x4C4C00 | Quest dialog lines switch to the "bad reaction" variant below 20 (after the low-intelligence variant). |

## Source anomalies and decisions

**Original check (ARC-RES-048):** P1 is a reconstruction slip (the original keeps a signed effect's written origin and faction), A2 is the original's own sign, and A4's asymmetry is confirmed. The owner decided D1 and A2 on 2026-10-17 (below). The verdicts in the [verification ledger](original-verification-ledger.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024) supersede the earlier readings below where they differ.

- **P1 Signed reputation effects.** R14 as read, applied to the owner's data:
  62 of 93 effects get origin = faction = adjustment, so each matches only an
  NPC whose origin **and** faction both equal its adjustment.
  NPC origin and faction values were not surveyed, so how many NPCs that is
  (likely none for negative values) is inferred, not measured. Only the 31
  unsigned effects keep their authored scope. **D1 (owner decision,
  2026-10-17)**: every effect keeps its
  written scope in both rule settings, with no switch. The retail executable
  skips the sign together with the digits ([ARC-RES-048](original-verification-ledger.md)),
  so the "source parse" is a reconstruction slip (ledger row Reputation P1);
  the earlier proposal (keep
  both readings, default to the source parse) is withdrawn.
- **A2 Follower hit sign.** The earlier reading's field comment says parameter 9 is how much
  a deliberate hit *lowers* a follower's reaction, but the code adds it, and
  the data values are 0..10 (none negative). The companion research C13
  repeated the comment. The retail executable adds it too
  ([ARC-RES-048](original-verification-ledger.md)), so the rise is a bug of the
  original game (the field comment's meaning is the evidence for intent).
  **Decision (owner, 2026-10-17)**: the Steamwoven setting lowers
  the follower's reaction by parameter 9; the Original setting keeps the rise
  through a switch of the [rule modes](../world/rule-modes.md).
- **A3 Mind control.** −50 is applied when control starts and nothing restores
  it when control ends, while reaction changes during control are ignored
  (R2). Recorded as source behaviour, not reviewed for intent.
- **A4 Charm reversibility.** The earlier reading deliberately deviated (a
  reconstruction change): it bypassed the PC's good/bad reaction effects for
  charm on and off, because the original asymmetry leaves a permanent change
  (for example +10 for a PC with a `badreactionadj +10` race effect: +30 on,
  −20 off). The original applies the effects both ways (ledger row R2 of the
  reaction runtime). **D2 (owner decision)**: symmetric charm or original
  asymmetry. Proposal: symmetric charm, labelled as a fix.
- **A5 Unvalidated numbers.** Reputation numbers are not range-checked
  (R11); 20 `RP` actions in 3 original DLG files use operands with magnitude 1
  or 2 (L5). As read they would index outside the table (ledger row
  Reputation A5: the original has no range check either). Proposal: the reputation
  owner admits 1000..1999 only and a dialog `RP` outside it is a typed
  refusal, like the existing quest and rumor domains.
- **A6 Script set-reaction operand.** The SCR set-to action reads its value
  with the third operand's type but the **second** operand's value
  (script 0x447220).
  Whether that is a reconstruction slip or original behaviour is open; it
  matters only when an SCR uses that action.
- **A7 Gift category test.** The categories are numbered love = 0 … hatred
  = 6, and the gift rule of R9 raises reaction only when the category number
  is below amiable, that is only for an NPC that already loves the PC. The
  likely intent ("not yet amiable") is the opposite. Open until checked in the
  original executable.
- **A8 RE comparison operand.** The action operand is read with `atoi` on
  text that still starts with `>` or `<`, which yields 0, so as read `RE >v`
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
| L1 | `rules\gamerep.mes` resolves to the module archive `Arcanum.dat` (1 candidate, 3,206 bytes, SHA-256 `d11fe290…fc570429`): 76 reputations, numbers 1000..1075, none missing faction values; 5 list at least one membership faction (4 distinct factions). 93 effects: 62 reputations have 1, 13 have 2, 1 has 5. 42 effects are negative (in 30 reputations), 18 reputations have a positive effect. Scope as authored: 37 any/any, 35 origin only, 10 faction only, 11 both; 12 distinct origins, 13 distinct factions. 62 effects carry an explicit sign (42 `-`, 20 `+`) and change under the as-read parse (P1). |
| L2 | `mes\gamereplog.mes` (module, `8301681d…c42ac0ae`) names all 76. Each of the four greeting files (module) covers 70 of the 76 (1000..1069). |
| L3 | `mes\reaction.mes` (`arcanum3.dat`, `da8fa015…6514a81`) has entries 0..6. `rules\ai_params.mes` (`arcanum3.dat`, `96684d94…66fb127`): 41 rows numbered 0..40, 17 values each; parameter 5 has 6 distinct values, some of them about ±1000 (so some rows effectively never or always refuse); parameter 9 has 3 distinct values, none negative; parameter 10 has 2 distinct values, none positive. The module's `rules\gameai.mes` adds one row numbered 100. |
| L4 | `rules\effect.mes` (`arcanum3.dat`, `8ac43304…db0b6b2`, 309 entries): 7 mention `badreactionadj`, 5 `goodreactionadj`, 1 `beautyreaction`, 5 a standalone `reaction` change. The module `rules\gameeffect.mes` has 1 entry and none of these. |
| L5 | DLG census over the winning copies of 709 DLG names (671 read by the current reader; 38 refused by its strictness rules and not counted). Player conditions: `RE` ≥ 782 / ≤ 895, `ME 0` 374, `ME 1` 367, `RP` has 90 / lacks 46. Player actions: `RE -` 1,316, `RE +` 893, unsigned 216, `>`/`<` 0; `RP` add 57, remove 24. NPC actions: `RE -` 31, `+` 40, unsigned 37; `RP` add 22, remove 2. 20 `RP` action operands in 3 files have magnitude below 1000 (1 or 2), outside the 76 numbers of L1. |
| L6 | Virgil's Crash Site DLG (`dlg\01324Virgil.dlg`, payload `34a49a2c…`, as in [NDE-06](crash-site-npc-dialog-evidence.md)) uses only `RE` among these opcodes: 3 `RE ≥`, 9 `RE ≤` conditions and 4 `-`, 4 `+`, 3 unsigned actions; no `ME` and no `RP`. |

## What this means for the current port

**Since ARC-WORLD-026** slices 1 and 2 below are implemented ([NPC reaction runtime](../world/npc-reaction-runtime.md)):
the rules in `Arcanum.Simulation.Reaction`, the reaction memory in the session (snapshot schema 10), and the viewer's
`RE` and quest rules over it; `reaction.pc` is gone. The notes below describe the state before it.

- The conversation runtime's `reaction.pc` is a single stored value seeded with
  the diagnostic 50. Per R1 it is the **stored** value only. The
  [Virgil consent research](virgil-follow-consent.md) (ARC-RES-026) measured
  his real instance: reaction base **60**, human, origin 0, faction 0, not
  aloof, no reaction memory. So the seed differs from his data by 10, and for
  a real PC his effective reaction adds the human-row race term, the PC's
  beauty modifier and only the reputation effects scoped to any origin and
  any faction (under P1 no signed effect reaches him). With the diagnostic
  profile (human PC, no beauty, no reputations, no effects) stored and
  effective still coincide.
- Unsigned `RE v` and the quest rule "at least 41" are deltas against the
  effective value (R2), so a later owner must apply them as deltas, not by
  writing *v*.
- `ME` needs the conversation start/end of R5 and R6; it cannot be derived
  from "a value is stored".
- The follow consent (companion C2) compares that effective reaction with
  Virgil's AI row 5, whose dislike threshold ARC-RES-026 measured as −1000,
  so reaction cannot refuse him in practice; his parameter 9 is 0, so the A2
  sign question does not affect him either.

## Open questions

- **O1** Answered for Virgil by ARC-RES-026 (see above). Other Crash Site
  NPCs' bases, races, origins and factions were not surveyed.
- **O2** Answered by ARC-RES-048: the original keeps a signed effect's origin
  and faction (decision D1).
- **O3** A6 (script operand), A7 (gift test), A8 (RE comparison operand) and A9
  (greeting number), same kind of observation. A2 was answered by ARC-RES-048
  and decided on 2026-10-17.
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

1. **Reaction rules (Simulation)** (done, ARC-WORLD-026): a pure effective-reaction function over
   explicit inputs (stored or base, NPC race, PC race, PC reaction modifier
   from the [character stats owner](../world/character-stats-runtime.md),
   reputation sum, NPC reaction effects, enemy-list and mind-control facts,
   aloof/monstrous), categories, the hostility variant, and the delta rule with
   PC good/bad effects. Synthetic tests vary one input at a time (FR-SOCIAL-001
   acceptance).
2. **Reaction state (Session)** (done, ARC-WORLD-026): per NPC instance the stored value, the
   conversation slot and "met before", with begin/end conversation, adjust,
   set-to (as a delta), forget; a new snapshot schema replaces the
   `reaction.pc` map change. Single PC first; the 9-slot eviction (R3) stays
   documented, not built.
3. **Dialog binding**: `ME`, `RE` actions as deltas, the insult rule, and the
   quest rule through the owner.
4. **Reputation data and owner**: a Data reader for the four file kinds with
   every effect in its written scope (D1), a PC reputation list with game time, add/remove/has,
   `RP` condition and action with the A5 domain, faction membership and the
   reaction sum. The greeting pick needs a random port and speech text and can
   follow later.
5. **Consumers**: the JO consent (companion slice 2) reads the effective
   reaction and parameter 5; AI hostility, barter prices and hover colour come
   with their own domains.

## Method

Source reading: the reaction functions (0x4C0BD0–0x4C15A0) and the reputation
functions (0x4C15D0–0x4C21E0) in full, every caller of their public functions,
and the helpers named in each row; earlier provenance is in git history. Names
in this document are descriptive.

The local observations come from a scratch program (not committed) that opened
the owner's installation with `ContentMountPlan.Discover` + `ContentStack.Open`
(module `Arcanum`), resolved each path above to its winning copy, and printed
size, SHA-256 and counts. MES entries were taken as `{number}{text}` pairs.
Reputation tokens were split on commas; "authored" values are the
whitespace-separated numbers of each token, "as-read parse" emulates R14
(C `atoi`, then skip spaces and digits). DLG names were collected from every
mounted archive and `dlg` folder and resolved through the stack; each record's
condition and action fields were split on commas, and a token counted when its
first two letters (case-insensitive) were `re`, `rp` or `me`. The sign or
comparison prefix was taken from the first non-space character after the
opcode. Files the current `DlgReader` refuses were skipped and are counted
as such. `RP` operands were also compared, by absolute value, with the
numbers present in `rules\gamerep.mes`.
