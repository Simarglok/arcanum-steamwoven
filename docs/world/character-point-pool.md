# Character point pool (ARC-WORLD-052)

Status: **Implemented bounded component, not wired.** Work item ARC-WORLD-052 (2026-10-21), task S5 of the
[skills plan](../research/skills-plan.md) (rest of K1). The one spend-a-point / refund-a-point path over the hero's unspent
character points that the character editor uses, for primary stats, hit and fatigue points, basic and tech skills, and,
through a lane each, spell ranks (the magic lane's MT3) and tech degrees (the technology lane's TECH-2). Godot-free: pure
rules in `Arcanum.Simulation.Character` and the editor's floor in `Arcanum.Session`. No snapshot schema change. The sheet's
+ and − buttons are task S6.

Requirements: [FR-CHAR-015](../requirements/characters-skills.md#fr-char-015) (stats bought and refunded),
[FR-CHAR-016](../requirements/characters-skills.md#fr-char-016) (basic skill points),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017) (tech skill points and tech points).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Character point pool"](../research/original-verification-ledger.md#character-point-pool-arc-world-052), rows
CP1–CP12). Code: [`CharacterPointRules`](../../src/Arcanum.Simulation/Character/CharacterPointRules.cs),
[`GameSessionPoints`](../../src/Arcanum.Session/GameSessionPoints.cs); tests:
[`CharacterPointRulesTests`](../../tests/Arcanum.Simulation.Tests/Character/CharacterPointRulesTests.cs),
[`PointPoolStateTests`](../../tests/Arcanum.Session.Tests/PointPoolStateTests.cs).

## The editor's floor

Owner rule (2026-10-20): points spent on a stat, a pool, a skill, a spell rank or a tech degree can be refunded only while
the character editor is open, and only down to the value it had when the editor opened; once the editor closes they are
never refundable. The original takes that snapshot when its editor opens (0x5597C0, CP1): the 23 editor fields (unspent
points, level, hit and fatigue points, the eight stats' base and effective values), the eight degrees, the sixteen college
ranks and the sixteen skills' points.

`OpenHeroEditor()` takes the hero's advancement sheet as the floor (`QueryHeroEditorFloor()`); `CloseHeroEditor()` drops
it. Opening is refused without a hero or for a dead one (CS2); an open editor keeps its floor. The floor is not saved: a
restored session starts with the editor closed, so nothing bought before a save is refundable after loading it. Opening
and closing do not move the revision.

## Spending and refunding

`SpendHeroPoint(expected, track, index)` and `RefundHeroPoint(expected, track, index, click)` take an `AdvancementTrack`
(stat 0..7, basic skill 0..11, tech skill 0..3, college 0..15, tech discipline 0..7, hit points 0, fatigue 0). They need
an open editor and the current version; a change adds one revision and the hero's character owner follows the stats.
A refusal of the rules is `PointRefused` with the rules' `PointChange` (status, value, and the stat or skill it names).

| Track | Spend (+) | Refund (−) | Original |
| --- | --- | --- | --- |
| Stat | Refused when the **effective** value has reached the race's maximum; cost `StatCost(base + 1)` (0 up to 1, 1 above); refused without the points; the base rises by one, refused when it is at the race's maximum | At the floor refused; refunds `StatCost(base)`; the base falls by one unless the stat setter refuses: the race's minimum, a skill keyed on the stat whose points the lower value no longer allows, for intelligence a known spell rank (5) and then a tech degree (0, 5, 8, 11, 13, 15, 17, 19), for willpower a known spell rank's willpower, each against the effective value the lower base gives | 0x55A5C0, setter guards 0x4B0980 (SR8), 0x4C6B50, 0x4B1B90, 0x4B02B0, 0x4B1C00; CP3, CP4, CP8 |
| Hit points, fatigue | Cost 1; no maximum in the original, the session stops at 100 bought points | Refunds 1 | 0x55A5C0 rows 0 and 1; CP3, CP4 |
| Basic skill | Cost 1; the points setter refuses four levels per point above the key stat's cap: "at maximum" when the skill's level is 20, else the key stat's refusal | One point per pass, refunding 1 each, repeated until the skill's level changes (the original's click) or, with `SkillRefundClick.OnePoint`, once; stops at the floor; a first pass the key stat refuses (its cap fell below the points left, for example under an effect) is refused | 0x57ACD0, 0x57AEB0; CP5, CP6 |
| Tech skill | As a basic skill; a tech point moves with each point | One point | 0x57ACD0, 0x57AEB0; CP5, CP7 |
| College, tech discipline | Through the lane (`IPointLane`), `LaneNotReady` until MT3 and TECH-2 plug in | Through the lane, down to the floor | college handler, degree sell 0x589AF0; CP2, TC10 |

The editor's stat hint (`NextStatCost`, 0x55AE70) shows `StatCost(base + 1)` while the **base** is below the race's maximum,
and 0 at it: when an effect holds the effective value at the maximum, the hint shows a cost the + button then refuses
(CP3, the original's behaviour). The skill hint is `SkillRules.MinimumKeyStat(4 × points + 4)` (SR7).

**Refund click (SK2, CP6).** One − click of a basic skill in the original removes points one at a time until the skill's
level changes. `RefundHeroPoint` takes the click as an explicit option for task S6: `UntilLevelChanges` (the default, the
original's click) or `OnePoint`. Either way the loop ends at the level change, the floor or a refusal.

**Lanes.** A lane implements `IPointLane.Raise` and `Lower` for its track: one step of its own fields (a rank and the
magick points, a degree and the tech points) and the points that step costs or refunds. The pool checks the points, the
floor and that the lane leaves a valid sheet with the unspent points untouched, then moves the unspent points. The session
names each track's lane in one place (`PointLane`).

## Differences from the original

Owner decision 2026-10-20: every difference and every fix is documented. This task adds no rule-mode switch: the floor is
strict in both modes.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| The editor compares a value with its snapshot for equality before a refund. | At or below the floor is refused. A value can only fall below its floor by another path while the editor is open; the original would then refund past the snapshot. | both |
| A basic skill's − click checks the floor only before its first pass; when an effect holds the skill's level (for example key stat 18, 4 points at open, +4: after a + to 5 one − goes 5 → 4 → 3), the click goes on below the floor, down to no points, refunding each (CP6, an original bug; pending owner decision, recommended: fix in both modes). | The click stops at the floor. | both |
| Selling a tech degree is not compared with the snapshot taken when the editor opened (TC10); the in-game editor only hides the − button at it (CP2), editor modes 0 and 3 draw it whenever the degree is above 0. | Down to the floor, as every other track (owner decision 2026-10-20, no switch). | both |
| Hit and fatigue points have no maximum. | At most 100 bought points each (the session's bound). | both |
| Refunds write the unspent points through the stat setter, which clamps them to 0..56. | The same clamp; the strict floor makes it unreachable. | both |
| The snapshot lives in the editor window's globals while it is open. | The floor is session state that is not saved; a restored session starts with the editor closed. | — |
| A network client sends each click to the host. | Single player only. | — |
| The editor shows a message for each refusal and refreshes its window. | The typed status; the messages are task S6's. | — |

## Not ready

- No caller: task S6 puts the + and − buttons on the AREA viewer's character sheet; the viewer has no editor yet.
- Spell ranks and tech degrees answer `LaneNotReady` until the magic lane's MT3 (spell book) and the technology lane's
  TECH-2 (degrees) add their lanes.
- The character editor's other modes (creation, the level-up of followers, multiplayer) are not modelled.
