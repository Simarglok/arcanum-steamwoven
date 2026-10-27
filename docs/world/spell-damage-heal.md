# Spell damage and heal (ARC-MAGIC-012)

Status: **Implemented bounded component; behind the run system's component port, no viewer caller.** Work item ARC-MAGIC-012
(2026-10-21), slice MT12 "Damage and Heal" of the [magic implementation plan](../research/magic-implementation-plan.md). It
covers the first two components a run applies to a target, as the original does them:
- **Damage**: typed, the `Dmg:` range or the `Scaled` amount, `Full`, the margin, the death flag, acid;
- **Heal**: hit points, poison, fatigue, `Full`, the raise and reanimation with the raise-script veto, scars and cures.

It also lands data fix DF4.

Godot-free rules in `Arcanum.Simulation.Magic` for any caster, item, critter, object and map. The damage goes through the
combat damage step the attacks use, and the heal through the life rules. The session applies both.

Requirements: [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) (damage and heal components),
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) (one damage step by type, resistance and difficulty),
[FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010) (death, knockout and the raise).

## Verification

Two independent readers read every rule in the retail 1.0.7.4 `Arcanum.exe`
([ledger "Spell damage and heal"](../research/ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012), DH1–DH10).

The function oracle compared three functions with the original on 10,000 seeded inputs each (F3):

| Function | Address | Receipt |
| --- | --- | --- |
| Damage component | 0x4518D0 | [`4518D0.json`](../verification/receipts/4518D0.json) |
| Heal component | 0x4521A0 | [`4521A0.json`](../verification/receipts/4521A0.json) |
| acid's steps | 0x4B5810 | [`4B5810.json`](../verification/receipts/4B5810.json) |

The Original setting matches on every input. The Steamwoven setting differs only on MG37's inputs (the Damage component) and
MG38's inputs (the acid). The readers disagreed on one point: whether the acid's body-armour test ever holds. The oracle settled
it: the wearer took its share in every one of the 4,653 inputs with body armour (MG38).

Code:
- [`SpellDamageRules`](../../src/Arcanum.Simulation/Magic/SpellDamageRules.cs) — the two components and the acid;
- [`SpellLifeComponents`](../../src/Arcanum.Simulation/Magic/SpellLifeComponents.cs) — the executor over the world port;
- `AttackResolver.DamageStep` — the shared damage step (an attack's step without its rolls);
- `LifeRules.Heal` — the raise-script port;
- `DataFixProfile.RepeatedDamageFlagsLost` — DF4.

Tests:
- [`SpellDamageRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellDamageRulesTests.cs);
- the three oracle tests in `SpellRunOriginalExeTests`;
- the DF4 reader test in `SpellComponentTests`;
- the census and DF4 check in `SpellRecordRealDataTests`;
- the raise veto in the session's `LifeStateTests`.

## Damage (`SpellDamageRules.Damage`, 0x4518D0)

The component fills one slot of the damage step's five (normal, poison, electrical, fire, fatigue). Its flags pass to the step,
except `Scaled` and the fatigue slot's `Full`, which the component clears.

1. **Scaled.** With `Scaled` and a critter parent, the amount is `min + (max − min) × aptitude / 100`, truncated toward zero.
   - The aptitude is the parent's, captured at the cast.
   - An armed fate "spell at maximum" made it 100 for the action (MG17, ARC-MAGIC-011).
   - At an aptitude of 0 or less the amount is the minimum.
   - Nothing is drawn.

   A parent that is no critter (a trap) rolls the range.
2. **Roll.** Otherwise `Dmg:` is rolled. An empty range draws nothing, and a reversed one is swapped (0x4CEF00).
3. **Full.** On the fatigue slot (`Dmg_Magic`), the amount is the target's current fatigue + 10, a knockout. On any other slot
   the step takes the target's current hit points instead of the sum.
4. **Acid.** Type 5 fills the normal slot. Its steps are below, and no margin applies to them.
5. **Margin.** A resistance margin (MT6, the run's last target's) cancels a component that has the death flag. Otherwise it
   takes `margin %` of the type's amount, at least 1 (MG37).

**The damage step** (`AttackResolver.DamageStep`, the original's 0x4B4390 for a spell's context).

The step is the attacks' own code, without the attack roll and the critical procedure. In order:
1. Undead and mechanical critters take no poison, and a stoned critter takes nothing.
2. An invulnerable target takes nothing.
3. A nigh-invulnerable critter takes nothing unless the context's weapon is "uber" (MG36).
4. The resistances apply per type: normal, poison, electrical, fire, and three quarters of normal for fatigue. They are skipped
   for a context weapon that ignores resistance (MG36).
5. The hit-point amount is normal + electrical + fire, or the current hit points for `Full` or `Death`.
6. A PC attacker's difficulty share applies: ×1.5 on easy, −¼ on hard. In the original it also applies to the current
   hit points (MG35).
7. A critter takes the fatigue (none when fatigue-immune) and the poison. A portal or container loses its material's
   value.
8. The step reports armour wear for fire above 0 after resistance.

The packet's effects (`LifeDamage`) go to the session's damage step (`LifeRules.ApplyDamage`) in that order: stun (0x40),
blinding (0x200), an arm (0x400), the legs (0x800), a scar (0x1000) and the dropped weapon (0x2000, the item owner's).
`Crippling` in the data is all three of 0x200, 0x400 and 0x800. A death with the death flag falls with the plain animation.

**Acid** (`SpellDamageRules.Acid`, 0x4B5810).
- On a critter, each item worn in slots 1000..1008 takes the normal amount, in slot order. The step lowers the amount in
  place by that item's resistance, so it shrinks from item to item. The executor chains the amounts through the steps
  themselves: a prevented step lowers nothing, and neither does one whose context weapon ignores resistance (MG36).
- The wearer then takes a third of what is left. In the original this happens even when it wears body armour (MG38).
- Any other target takes a third at once.

**Host's duties.** `SpellLifeComponents.Damage` returns the steps (the object, the step and the life damage) and the damage's
source: the run's other object, else its parent, which the original uses for experience and the AI. The host keeps these
duties:
- the AI's attack notice, once per target per run (0x453F20);
- the experience share and the AI reaction of the step;
- the item effects;
- the damage effect when the step dealt damage;
- the taking-damage script (point 0x17), which can cancel the step.

## Heal (`SpellDamageRules.Heal`, 0x4521A0)

- **Amounts.** Without `Full`, the amount is scaled or rolled as for Damage. It goes to the poison level (type 1), the fatigue
  (type 4) or the hit points (any other type).
- **Full.** With `Full` there is no amount: the full heal takes all hit-point damage, and the fatigue only with a fatigue
  amount.
- **Flags.** The flags give the raise (`Resurrect`, 0x2), reanimation (0x400000 with it), five scars (0x1000), and the cures of
  blinding, the arms and the legs.
- **No margin.** A heal takes no margin and no AI notice.
- **Applier.** The session's heal (`LifeRules.Heal`, 0x4B58C0, ledger HL4) applies it.
- **Raise.** A raise first asks the target's raise script (point 0x12), the host's port. The original runs that script even
  when the target's flags refuse the raise, and either refusal stops it. The session asks the port while it computes the step,
  before its later refusals, so the port answers without side effects and does not call the session.

## Data fix DF4

- **Defect.** Record 221's Damage component writes `Dmg_Flags:` twice. The original reads each key once, so it loses the
  second value, `Scaled`.
- **Fix.** Under the owner's data-fix rule (ARC-DATA-022), Steamwoven reads the repeated value and ORs it in
  (`DataFixProfile.RepeatedDamageFlagsLost`, Original true). Callers pass the rule mode's profile to `SpellData.Open`; none
  reads the data as the original does. The viewer's spells page passes none, as it reads no damage flags.
- **No effect on damage.** Under the original's damage rules the restored flag changes nothing. The component is a full death
  hit with a 0–0 range: its amount is the target's current hit points whatever `Scaled` gives, and a margin cancels it either
  way.

## Differences from the original

Owner decisions 2026-10-20 and 2026-10-21: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A PC's full or death hit is scaled by difficulty after it took the current hit points: on hard the target lives with a quarter (MG35). | The amount is not scaled. | `MagicAnomalyProfile.DifficultyScalesKillDamage` (owner decision 2026-10-21: fix) |
| The spell's context carries the caster's wielded weapon: ignore-resistance, "uber" and damage-armour flags reach the spell (MG36). | No weapon: a nigh-invulnerable critter takes no spell damage. | `MagicAnomalyProfile.SpellDamageUsesCastersWeapon` (owner decision 2026-10-21: fix) |
| A margin takes at least 1 from an amount of 0, so a resisted stun-only spell heals a hit point (MG37). | Nothing is taken from 0 or less. | `MagicAnomalyProfile.MarginTakesFromNoDamage` (owner decision 2026-10-21: fix) |
| The acid's body-armour test compares the loop index with the slot, so the wearer always takes a third (MG38). | A wearer who wears body armour is spared. | `MagicAnomalyProfile.AcidIgnoresBodyArmour` (owner decision 2026-10-21: fix) |
| Record 221 loses its repeated `Scaled` (DF4). | Restored (no effect on any amount). | `DataFixProfile.RepeatedDamageFlagsLost` (decided) |
| A spell's damage step is cancelled by the target's taking-damage script; the AI notice, experience share, item effects and eye candy run inside the step. | The host's (no SCR runtime for point 0x17 yet; the session applies the life damage). | — |

Latent, both modes follow the original:
- acid takes no margin;
- a `Full` hit on a hit-point slot without the death flag ignores the margin, since the step takes the current hit points.

No installed record reaches either: the one acid component is a technology record, which resistance never reaches, and
`Full` alone is used only on the fatigue slot.

## Not ready

- No viewer caller: the casting UI (MT20a).
- The world port behind the session's NPCs is the casting host's (MT20a).
- The taking-damage script (point 0x17) runs no SCR yet.
