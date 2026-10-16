# Rule modes: Steamwoven and Original

Status: **Implemented bounded component**, ARC-WORLD-023. The game has two rule modes (owner decision D8 of the
[NPC combat AI plan](../research/npc-combat-ai-plan.md#owner-decisions-2026-10-16), extended to every domain by the
owner on 2026-10-17): **Steamwoven**, the original game with the recorded fixes, which is the default, and
**Original**, the retail 1.0.7.4 behaviour including the bugs of the original game. The player will pick the mode in
the settings menu and change it during play without a restart (there is no menu yet, see [Not ready](#not-ready)); the
mechanism below already allows it. This contract fixes the mechanism every domain uses and
keeps the registry of mode differences. The architecture decision is recorded in
[architecture direction](../architecture/decisions.md).

## What is a mode difference

The [original executable check](../research/original-executable-check.md) classifies each anomaly a domain study finds,
and the [verification ledger](../research/original-verification-ledger.md) records the verdicts:

| Verdict | Steamwoven | Original |
| --- | --- | --- |
| CE reconstruction slip (CE differs from the original) | follows the original | follows the original |
| Bug of the original game, fix recorded under D2 | the fix | the original's bug (a switch) |
| Bug of the original game, no fix recorded | the original's bug | the original's bug |
| Behaviour of the original | the original | the original |
| Project improvement recorded by the owner (for example persisted AI ticks, OQ2) | the improvement | the original's behaviour (a switch) |

So a switch exists only where Steamwoven deliberately differs from the retail game. A CE slip never becomes a switch.
Project policy (proposed with this contract, open to the owner): **the Original mode never reproduces a crash, a hang
or an out-of-bounds access**; such bugs are fixed in both modes and listed in the registry as "both fixed", because
reproducing them would end the game rather than show the original rules.

## Mechanism

- `Arcanum.Session.RuleMode` names the mode (`Steamwoven`, `Original`); `RuleModeNames` gives the stable names
  `steamwoven` and `original`, exact and case-sensitive.
- Each domain that has a switch owns one **profile**: an immutable, stateless record in its own assembly with a stable
  `Name`, two static instances `Steamwoven` and `Original`, `All` and `FromName` (the shape of
  `CombatAnomalyProfile`). Each switch is a boolean named after the original's behaviour and documented with its
  ledger ID. A profile changed with `with` is a custom mix for comparison runs and must get its own name.
- `Arcanum.Session.GameRules` composes the domain profiles of one mode: `GameRules.Steamwoven`, `GameRules.Original`,
  `For(mode)`, `All`, `Name`. Every profile in a `GameRules` carries the mode's name (a test checks it).
- The session holds the active rules: `GameSessionDescriptor.Rules` and `GameSessionRestoreOptions.Rules` start a
  session in a mode (default Steamwoven; an undefined value is `InvalidDescriptor`), `GameSessionOwner.Rules` returns
  them and `GameSessionOwner.SelectRules(mode)` switches at once (`Changed`, `NoChange`, or `Rejected` for an
  undefined value).
- `RuleModeSettingCodec` stores the player's choice as `{"schema":1,"rules":"steamwoven"|"original"}`; a missing,
  unreadable or unknown value loads as Steamwoven. The settings menu writes it and calls `SelectRules`; the next new
  game or load passes it to the descriptor or restore options.

### Rules that make switching safe without a restart

1. **Read at the decision.** A domain takes its profile from the session's current `Rules` at the moment it decides
   (an attack, a cast, a script action, a restore) and does not keep it between calls.
2. **No state depends on the mode.** Saved and live state have one schema for both modes; a switch changes how the
   next decision is made, never converts existing state (design rule 7 of the AI plan, D4).
3. **Not saved.** The snapshot does not record the mode; any save loads under either mode. Switching moves no session
   revision and draws no random number.
4. **One switch, one difference.** A domain adds a switch only for a ledger row whose verdict makes it a mode
   difference (table above), and adds its profile to `GameRules` in the same PR, with tests for both modes.

## Registry

Implemented switches (the Original value is `true`; Steamwoven `false`):

| Domain | Profile | Switch | Ledger ID | Original behaviour |
| --- | --- | --- | --- | --- |
| Combat | `CombatAnomalyProfile` | `InvocationModifierCountedTwice` | A2 | the bed's −30 counts twice |
| Combat | `CombatAnomalyProfile` | `ArmorClassEffectsCountedTwice` | A6 | AC-adjustment effects count twice |
| Combat | `CombatAnomalyProfile` | `CriticalSelfHitRolledTwice` | A7 | a critical self-hit rolls the critical procedure twice |
| Combat | `CombatAnomalyProfile` | `BackstabDamageOnSelfHit` | A10 | the backstab bonus applies to a self-hit |
| Event queue | `GameEventQueueProfile` | `RebuildOnLoad` | OQ2 | AI ticks are rebuilt with fresh jitter on load |
| Event queue | `GameEventQueueProfile` | `PoisonRecoveryTwice` | TE10 | from the second recovery tick on, poison recovers twice every 120 s (ARC-WORLD-024 life timers) |

Reserved and candidate differences, added by the slice that implements the rule (the slice records the D2 decision;
until then both modes behave as the original):

| Domain | Ledger IDs | Note |
| --- | --- | --- |
| Combat | A9 | `FirstSubturnStartedTwice` reserved (fix accepted). |
| Magic | MG3, MG4, MG5, MG6, MG7, MG9, MG10, MG11, MG15, MG18, MG19, LM10; CE changes: shell order, charm bonus, summon palette, resistance source (O9), effect min/max | MG12 and LM20 (out-of-bounds writes and copies) are fixed in both modes. |
| Skills | SK8, LM17, SK3 repair off-by-one (multiplayer only), auto-level A3 (both fixed: null read) | SK1, SK2, SK4, SK5, LM1, LM2 and dodge are CE slips, no switch; dodge already follows the original's 2 × level in `AttackRules`. |
| AI and reaction | LM3 and LM12 (both fixed: out-of-bounds, latent), reputation A5 (both fixed: out-of-bounds read, latent), reaction A2 (a deliberate hit raises the follower's reaction; Steamwoven fix proposed, owner to confirm), reaction A4 charm asymmetry (decision D2 of that study), P7 (label open) | P1, P2, P3, LM13, LM18, reputation P1 (decision D1 to revisit) and the companion wait timer are CE slips. |
| Scripts | X3 tile writeback, X10 unmasked flag write, X2 (both fixed: uninitialised read) | X1, X3 sector, X6 detail, X7, X8 are CE slips. |
| Event queue | TE1 (overflow edges), TE3/OQ1 tie order (FIFO decided without a switch) | |
| World | LA4 direction-5 leaving on the portal tile; clock day carry only above one day (off by one), clock subtraction underflow, exchanged current day/month getters (dead code); wilderness A6 (both fixed: a null name faults) | LA1, LA2 and the tile swap itself are CE slips. |

The list covers every "Original bug" and "CE change" row of the ledger.

## Not ready

- Combat is not wired into the session yet and does not read `GameSessionOwner.Rules`. The event queue does
  (ARC-WORLD-024): a restore uses `Rules.Events` for `RebuildOnLoad`, and the poison recovery timer reads
  `PoisonRecoveryTwice` when it fires.
- There is no settings menu yet; the viewer has no mode toggle. The codec is ready for the menu. The AREA quick load
  (F8) keeps the current session's mode.
- Domains other than combat and the event queue have no profile yet; their first switch adds one.
