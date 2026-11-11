using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Magic;

/// <summary>What a magic roll decides; every roll carries one, so a trace shows which roll decided what.</summary>
public enum SpellRollPurpose
{
    /// <summary>The d100 against the target's magic resistance (0x4537B0).</summary>
    ResistanceD100,
    /// <summary>The target's saving throw d20 (0x4537B0).</summary>
    SaveD20,
    /// <summary>The fail-chance predictor's own d20 (0x453B20, MG14): asking the question moves the random stream.</summary>
    FailChanceD20,
    /// <summary>The invocation check's d100 against a technologist target (0x4564E0, MG5): rolled although it never refuses.</summary>
    InvocationAptitudeD100,
    /// <summary>A Damage component's amount in its <c>Dmg:</c> range (0x4518D0).</summary>
    Damage,
    /// <summary>A Heal component's amount in its <c>Dmg:</c> range (0x4521A0).</summary>
    Heal,
    /// <summary>An item spell's chance flag (0x4CB830, ARC-MAGIC-020): d100, fires at or below the flag's percentage.</summary>
    ItemTriggerChance,
    /// <summary>ARC-MAGIC-038: the Movement component's random-tile direction, 0..8 (0x4F4E40's one draw of 0x4CEF00; 8 is a step in place).</summary>
    MovementDirection,
}

/// <summary>
/// The injected randomness of magic: an inclusive uniform integer in <c>min..max</c>, the original's random range function
/// (0x4CEF00). The session's seeded source labels each roll <c>magic.</c> and the purpose; tests script each roll.
/// </summary>
public interface ISpellRollSource
{
    int Roll(SpellRollPurpose purpose, int min, int max);
}

/// <summary>
/// The PC target's armed fate "save against magick" (0x4F5270 with kind 4): consumes it and answers whether it was armed. The
/// session fills it from its fate owner (ARC-WORLD-054); a target without fate points passes <see cref="SpellFateSave.None"/>.
/// It is asked only for a PC target that is not the caster, at most once per resolution.
/// </summary>
public interface ISpellFateSave
{
    bool ConsumeSaveAgainstMagick();
}

/// <summary>The fate save of a target without an armed fate.</summary>
public sealed class SpellFateSave : ISpellFateSave
{
    private SpellFateSave() { }

    public static ISpellFateSave None { get; } = new SpellFateSave();

    public bool ConsumeSaveAgainstMagick() => false;
}

/// <summary>The kind of target the resolution reads: a PC, an NPC, or any other object (no saving throw).</summary>
public enum SpellTargetKind { Pc, Npc, Other }

/// <summary>
/// The record facts the resistance reads: its number (Disintegrate, 34, has a special case), the technology flag, the damaging
/// flag (a Damage component), the aggressive <c>Info:</c>, the maintenance period, and the <c>Resist:</c> stat (−1 for none, then
/// any other stat number) and modifier.
/// </summary>
public readonly record struct SpellResistRecord(int Spell, bool Technology, bool Damaging, bool Aggressive, int MaintainPeriod,
    int ResistStat, int ResistModifier)
{
    public bool HasSave => ResistStat != SpellResistanceRules.NoSave;
}

/// <summary>
/// The target facts the resistance reads: whether it is the run's parent (the caster), its kind, the "no disintegrate" critter
/// flag, the magic resistance the save reads (<see cref="StoredMagicResistance"/>), its effective magick/tech aptitude, the effective value of the
/// record's <c>Resist:</c> stat, and whether that stat is extraordinary. The original reads the <em>stored</em> resistance (the object's resistance 4, no armour,
/// effects or clamp: O9, RE2); Steamwoven reads it under the target's <c>resistmagic</c> effect changes (<see cref="SpellResistanceRules.ResistanceUnderEffects"/>,
/// ARC-MAGIC-033, the owner's decision D6c, both modes), so Resist Magick works.
/// </summary>
public readonly record struct SpellResistTarget(bool IsCaster, SpellTargetKind Kind, bool NoDisintegrate, int StoredMagicResistance,
    int Aptitude, int ResistStatValue, bool ResistStatExtraordinary);

/// <summary>
/// The run facts the resistance reads: whether the action is BEGIN (resistance runs only then), whether the run is unresistable
/// (the script's "cast unresistable spell" and <c>No_Resist:</c> records, MG19), and whether this action is fate-maximised
/// ("spell at maximum": no aptitude scaling of the resistance).
/// </summary>
public readonly record struct SpellResistRun(bool Begin, bool Unresistable, bool FateMaximized);

/// <summary>
/// One target's resolution: whether the spell applies to it (false: the target is skipped and plays "resisted"); the run's
/// margin and the displayed margin (null when the resolution returned before resetting them: not BEGIN, a technology record or
/// an unresistable run; the run keeps its previous margin; after a made save the displayed margin equals the run's); the penalty the resistance put on the save; whether the AI of the
/// target is told of an aggressive spell it shrugged off (extraordinary willpower).
/// </summary>
public sealed record SpellResistance(bool Applies, int? Margin, int? DisplayMargin, int Penalty, bool NotifiesAi);

/// <summary>
/// ARC-DEV-303 (RS10): the answer of the entangle re-resistance after a hit: whether the hold stays (false: the target resisted, so the hold's run ends
/// and "resisted" plays) and whether the AI of the target is told of an aggressive spell it shrugged off (extraordinary willpower; the entangle's own save is
/// strength, so no installed spell reaches it).
/// </summary>
public sealed record SpellReResistance(bool Holds, bool NotifiesAi);

/// <summary>
/// ARC-MAGIC-005 (magic plan MT6): the original's per-target resistance and saving throw (0x4537B0) and its fail-chance
/// predictor (0x453B20, 0x453CC0), as the 1.0.7.4 executable has them (contract docs/world/spell-resistance-runtime.md, ledger
/// "Spell resistance", RS1–RS10). The rolls come from the caller's <see cref="ISpellRollSource"/>; the PC's fate save from an
/// <see cref="ISpellFateSave"/>. One switch (<see cref="MagicAnomalyProfile.SaveSubtractsResistance"/>, MG4); MG13 (the fate
/// save's margin 10, display 100, and the save still rolls) and MG14 (the predictor's own d20) follow the original in both modes.
/// Arithmetic is the original's 32-bit integer arithmetic.
/// </summary>
public static class SpellResistanceRules
{
    public const int NoSave = -1, Willpower = 6, Disintegrate = 34, DamagingFloor = 50, FateMargin = 10, FateDisplayMargin = 100;
    /// <summary>The predictor's "certain" answer, and the resistance above which a spell without a save is certain to fail.</summary>
    public const int Certain = 100, CertainWithoutSave = 40;

    /// <summary>
    /// The resistance of one target to a magic action (0x4537B0), in the original's order:
    /// <list type="number">
    /// <item>not BEGIN, a technology record or an unresistable run: applies, margins untouched;</item>
    /// <item>the margins reset to 0; Disintegrate against a PC or NPC with "no disintegrate": skipped;</item>
    /// <item>unless the target is the caster: a PC whose armed fate "save against magick" fires gets margin 10 (display 100,
    /// MG13); otherwise R is the stored magic resistance, scaled for a negative aptitude unless the action is fate-maximised
    /// (<c>100 − (100 − R) × (A + 100) / 100</c>), a d100 below R makes the margin R − roll; a non-damaging
    /// spell with a maintenance period not above 0 and R &gt; 0 then gets a save penalty of margin / 10, or, without a save, is skipped when the roll was below R;</item>
    /// <item>the caster, a record without a save or a target that is no PC or NPC: applies;</item>
    /// <item>a willpower save against an extraordinary willpower: skipped (the AI is told of an aggressive spell);</item>
    /// <item>the save <c>S = stat − penalty + modifier</c> (Steamwoven: <c>+ penalty</c>, MG4); S ≤ 0 or a d20 above S: applies;
    /// a d20 ≤ S adds S − d20 to the run's margin and sets the displayed one to it; a damaging spell then applies with at least 50;
    /// a maintenance period other than 0 applies; any other is skipped.</item>
    /// </list>
    /// </summary>
    [OriginalFunction(0x4537B0, OriginalFunctionScope.Partial)]
    public static SpellResistance Resist(SpellResistRecord record, SpellResistTarget target, SpellResistRun run, ISpellRollSource rolls,
        ISpellFateSave fate, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(fate);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(target.Kind)) throw new ArgumentOutOfRangeException(nameof(target));
        if (!run.Begin || record.Technology || run.Unresistable) return new(true, null, null, 0, false);
        int margin = 0, display = 0, penalty = 0;
        var critter = target.Kind is SpellTargetKind.Pc or SpellTargetKind.Npc;
        if (record.Spell == Disintegrate && critter && target.NoDisintegrate) return new(false, 0, 0, 0, false);
        if (!target.IsCaster)
        {
            if (target.Kind == SpellTargetKind.Pc && fate.ConsumeSaveAgainstMagick())
                (display, margin) = (FateDisplayMargin, FateMargin);
            else
            {
                var resistance = Resistance(target, run.FateMaximized);
                var roll = rolls.Roll(SpellRollPurpose.ResistanceD100, 1, 100);
                if (roll < resistance) display = margin = unchecked(resistance - roll);
                if (resistance > 0 && !record.Damaging && record.MaintainPeriod <= 0)
                {
                    if (record.HasSave) penalty = margin / 10;
                    else if (roll < resistance) return new(false, margin, display, 0, false);
                }
            }
        }
        if (target.IsCaster || !record.HasSave || !critter) return new(true, margin, display, penalty, false);
        if (record.ResistStat == Willpower && target.ResistStatExtraordinary) return new(false, margin, display, penalty, record.Aggressive);
        var save = Save(record, target, penalty, profile);
        if (save <= 0) return new(true, margin, display, penalty, false);
        var d20 = rolls.Roll(SpellRollPurpose.SaveD20, 1, 20);
        if (d20 > save) return new(true, margin, display, penalty, false);
        display = margin = unchecked(margin + (save - d20));
        if (record.Damaging)
        {
            if (margin < DamagingFloor) display = margin = DamagingFloor;
            return new(true, margin, display, penalty, false);
        }
        return new(record.MaintainPeriod != 0, margin, display, penalty, false);
    }

    /// <summary>
    /// ARC-DEV-303 (RS10): the entangle's re-resistance on each hit of its target (0x459C10, called by the hit effect 0x459A30 with the run found for the entangled flag).
    /// A copy of <see cref="Resist"/>'s decision that writes no margin and has three differences, read in the 1.0.7.4 executable:
    /// <list type="number">
    /// <item>it has no BEGIN test: a technology record or an unresistable run holds (answers true), any other action is resolved;</item>
    /// <item>the save part reads the target's type from the global the run processing last cached (0x5E75AC, the type of the last target any run action met),
    /// not the object's own, so <paramref name="cachedKind"/> is that type: a cached type that is no PC or NPC skips the save and the hold stays
    /// (<see cref="MagicAnomalyProfile.ReResistReadsCachedTargetType"/>, MG48; Steamwoven asks the struck critter's own type);</item>
    /// <item>a made save always resists (answers false); <see cref="Resist"/>'s damaging, maintained and other tails (apply or skip by the record) do not exist here.</item>
    /// </list>
    /// Otherwise as <see cref="Resist"/>: Disintegrate against a critter with "no disintegrate" resists; a PC's armed fate save sends the target to the save part
    /// with no penalty; R is the stored resistance scaled for a negative aptitude (the fate-maximised flag is the last action's global, not modelled: the
    /// scaling applies); a d100 below R gives a margin whose tenth is the save's penalty for a record without damage and a maintenance period
    /// not above 0 (a record without a save resists on that roll); the caster, a record without a save or a cached type that is no PC or NPC holds; a willpower
    /// save against an extraordinary willpower resists; the save <c>S = stat − penalty + modifier</c> (Steamwoven: <c>+</c>, MG4) at 0 or below, or a d20 above it, holds.
    /// </summary>
    [OriginalFunction(0x459C10, OriginalFunctionScope.Partial)]
    public static SpellReResistance ReResist(SpellResistRecord record, SpellResistTarget target, SpellTargetKind cachedKind, bool unresistable,
        ISpellRollSource rolls, ISpellFateSave fate, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(fate);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(target.Kind) || !Enum.IsDefined(cachedKind)) throw new ArgumentOutOfRangeException(nameof(target));
        if (record.Technology || unresistable) return new(true, false);
        var penalty = 0;
        if (record.Spell == Disintegrate && target.Kind is SpellTargetKind.Pc or SpellTargetKind.Npc && target.NoDisintegrate) return new(false, false);
        if (!target.IsCaster && !(target.Kind == SpellTargetKind.Pc && fate.ConsumeSaveAgainstMagick()))
        {
            var resistance = Resistance(target, false);
            var roll = rolls.Roll(SpellRollPurpose.ResistanceD100, 1, 100);
            var margin = roll < resistance ? unchecked(resistance - roll) : 0;
            if (resistance > 0 && !record.Damaging && record.MaintainPeriod <= 0)
            {
                if (record.HasSave) penalty = margin / 10;
                else if (roll < resistance) return new(false, false);
            }
        }
        if (target.IsCaster || !record.HasSave || cachedKind == SpellTargetKind.Other) return new(true, false);
        if (record.ResistStat == Willpower && target.ResistStatExtraordinary) return new(false, record.Aggressive);
        var save = Save(record, target, penalty, profile);
        if (save <= 0) return new(true, false);
        return new(rolls.Roll(SpellRollPurpose.SaveD20, 1, 20) > save, false);
    }

    /// <summary>
    /// The fail-chance predictor the AI and the spell UI ask (0x453B20, MG14), which follows the resistance's shape but differs:
    /// a technology record is 0; R is the stored resistance (scaled for a negative aptitude unless <paramref name="fateMaximized"/>,
    /// the flag the last processed action left); for a non-damaging spell with a maintenance period not above 0 and R &gt; 0 a record without a save
    /// is 100 above R 40, else the save's penalty is R / 10; the caster, a record without a save or a target that is no PC or NPC
    /// answers R (0 for the caster); a willpower save against an extraordinary willpower answers 100; the save
    /// <c>S = stat − penalty + modifier</c> (Steamwoven <c>+</c>, MG4) at 0 or below, or a d20 above it, answers R; otherwise a
    /// damaging spell answers at least 50, a maintained one R, any other 100. The d20 is rolled whenever the save is reached.
    /// </summary>
    [OriginalFunction(0x453B20, OriginalFunctionScope.Partial)]
    public static int FailChance(SpellResistRecord record, SpellResistTarget target, bool fateMaximized, ISpellRollSource rolls,
        MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(target.Kind)) throw new ArgumentOutOfRangeException(nameof(target));
        if (record.Technology) return 0;
        int chance = 0, penalty = 0;
        if (!target.IsCaster)
        {
            chance = Resistance(target, fateMaximized);
            if (chance > 0 && !record.Damaging && record.MaintainPeriod <= 0)
            {
                if (!record.HasSave)
                {
                    if (chance > CertainWithoutSave) return Certain;
                }
                else penalty = chance / 10;
            }
        }
        if (target.IsCaster || !record.HasSave || target.Kind == SpellTargetKind.Other) return chance;
        if (record.ResistStat == Willpower && target.ResistStatExtraordinary) return Certain;
        var save = Save(record, target, penalty, profile);
        if (save <= 0) return chance;
        if (rolls.Roll(SpellRollPurpose.FailChanceD20, 1, 20) > save) return chance;
        if (record.Damaging) return Math.Max(chance, DamagingFloor);
        return record.MaintainPeriod != 0 ? chance : Certain;
    }

    /// <summary>
    /// The predictor for an item's spell (0x453CC0): an item that is magical (<paramref name="itemMagical"/>) and holds a spell
    /// (<paramref name="itemSpell"/> not −1) answers <see cref="FailChance"/> for that spell; any other item answers 0.
    /// </summary>
    [OriginalFunction(0x453CC0, OriginalFunctionScope.Partial)]
    public static int ItemFailChance(bool itemMagical, int itemSpell, Func<int, int> failChance)
    {
        ArgumentNullException.ThrowIfNull(failChance);
        return itemMagical && itemSpell != -1 ? failChance(itemSpell) : 0;
    }

    /// <summary>
    /// ARC-MAGIC-033 (owner decision D6c, magic audit RES-103; a bug of the game fixed in both modes, no switch): the magic resistance a save reads once the target's
    /// <c>resistmagic</c> effect changes (Resist Magick, spell 40, effect 56, and the magic parts of effects 51, 52, 55, 154 and 155) count. The original's save
    /// reads <paramref name="stored"/> alone (0x4537B0, RE2). With no change the value is <paramref name="stored"/> exactly (an immune 100 stays 100). With changes it is the effect
    /// aggregation over the stored value (0x4EA6C0, as the sheet's Magick line does: 0x43D6D0 puts the effects after the stored value) cut to 0..95 for a PC and an
    /// ordinary NPC, but never outside an already wider stored value (a stored 100 or a stored −30 keeps its side), so a blessing cannot make an immune NPC less resistant; a monstrous NPC is cut to the resolver's ±1000 only.
    /// </summary>
    public static int ResistanceUnderEffects(int stored, IReadOnlyList<EffectModifier> magic, bool unclamped)
    {
        ArgumentNullException.ThrowIfNull(magic);
        if (magic.Count == 0) return stored;
        var value = EffectModifiers.Apply(stored, magic);
        return unclamped ? Math.Clamp(value, -AttackResolver.MaxResistance, AttackResolver.MaxResistance)
            : Math.Clamp(value, Math.Min(0, stored), Math.Max(CombatantRules.MaxResistance, stored));
    }

    // The stored resistance, scaled for a negative aptitude unless fate-maximised (toward zero, 32-bit).
    private static int Resistance(SpellResistTarget target, bool fateMaximized)
    {
        var resistance = target.StoredMagicResistance;
        if (!fateMaximized && target.Aptitude < 0)
            resistance = unchecked(100 - (100 - resistance) * (target.Aptitude + 100) / 100);
        return resistance;
    }

    private static int Save(SpellResistRecord record, SpellResistTarget target, int penalty, MagicAnomalyProfile profile) =>
        unchecked(target.ResistStatValue + (profile.SaveSubtractsResistance ? -penalty : penalty) + record.ResistModifier);
}
