using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-303 (magic plan MT13, the Effect component of the hold spells; the T2b slice of the bug triage): the Effect component (0x451F20) on the hero, and the entangle's re-resistance
/// on each hit of its target (the hit effect 0x459A30 and 0x459C10, ledger RS10, EF9). Entangle (spell 51) adds the effect rule 62 (the speed stat −5, the "speed -5" of the rules'
/// effect table, shared with Body of Water and Plague of Insects) at its BEGIN and takes it off at its END beside the flag.
/// </summary>
public sealed partial class SessionSpellCaster
{
    // The type the run processing last cached for a target object (0x5E75AC): null is the original's −1 (reset at each action, no target object met since).
    private SpellTargetKind? _cachedTargetKind;

    /// <summary>
    /// The effect rule of a number (<see cref="SessionEffectDefinition.FromRule"/> over the effect tables, none for a number without one): the viewer sets it. Without it an Effect component
    /// that adds is not ready.
    /// </summary>
    public Func<int, SessionEffectDefinition?>? EffectDefinitions { get; set; }

    /// <summary>
    /// ARC-MAGIC-1644: the host that runs the item event scripts of an NPC's worn items (<see cref="GameSessionOwner"/>'s item script runs: the script files, the items' attachments, the effect rules) when a TraitIdx write of magick or
    /// tech points runs the rewield (0x465530, ledger RM3c); null answers none (the host's call). Without one, or when it answers null, a component for an NPC wearing an item with a wield script (24 or 25) is not ready.
    /// </summary>
    public Func<ISessionItemScriptHost?>? ItemScripts { get; set; }

    /// <summary>
    /// MG48: the cached target type is also written by a component's re-aim (0x453D40: the type of the parent for the self bit, of the source for the source bit; the last one written wins) and by the
    /// source swap after the targets (0x45148C: the type of the replaced source). A bit whose object is absent writes nothing.
    /// </summary>
    private void CacheReAimedKind(ulong apply, SpellTargetObject? self, SpellTargetObject? source)
    {
        if ((apply & 0x1) != 0 && self is not null) _cachedTargetKind = KindOf(self.Id);
        if ((apply & 0x2) != 0 && source is not null) _cachedTargetKind = KindOf(source.Id);
    }

    void ISpellRunHost.SourceSwappedBack(SpellRun run, SpellRunObject replaced) => _cachedTargetKind = KindOf(replaced.Id);

    private SpellTargetKind KindOf(string id) =>
        id == Hero ? SpellTargetKind.Pc : _map.Critter(id) is not null ? SpellTargetKind.Npc : SpellTargetKind.Other;

    /// <summary>
    /// The Effect component (0x451F20, EF9) on its target object: the entry count is <c>Count:</c> plus, for a <c>Scaled:</c> above 0 and a PC or NPC parent, <c>Scaled × the parent's
    /// aptitude cached with the run / 100</c> (toward zero; 100 when the PC's armed "spell at maximum" fired, the run's parent carries it); a count of 0 or less does nothing. An add runs the
    /// effect add (0x4E9F70) that many times with the record's cause, a removal the typed removal (0x4EA100: the first entry of the number, whoever added it) that many times. A null target does nothing.
    /// The hero and every NPC have an effect list in the session (ARC-DEV-348, <see cref="GameSessionOwner.AddNpcEffect"/>); an object that is no critter takes nothing (the effect
    /// functions test the critter themselves). The session's refusals (a full list, a rule it does not admit) are logged and the spell goes on, as the original's add has no error answer.
    /// </summary>
    [OriginalFunction(0x451F20, OriginalFunctionScope.Partial)]
    private bool EffectComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Effect is not { } data) return false;
        if (target.Object is not { } obj) return true;
        var count = data.Count;
        if (data.Scaled > 0 && run.Parent is { IsCritter: true } parent) count = unchecked(count + data.Scaled * parent.Aptitude / 100);
        if (count <= 0) return true;
        var isHero = obj.Id == Hero;
        // ARC-MAGIC-027 (MA-2): an NPC target has its own list since ARC-DEV-348; an object that is no critter takes nothing (the effect functions test the critter themselves).
        var npc = isHero ? null : _map.Critter(obj.Id);
        if (!isHero && npc is null) return KindOf(obj.Id) == SpellTargetKind.Other;
        if (data.Add)
        {
            var cause = Enum.IsDefined((EffectCause)data.Cause) ? (EffectCause)data.Cause : EffectCause.Spell;
            if (EffectDefinitions?.Invoke(data.Effect) is not { } definition) return false;
            var added = isHero ? _owner.AddHeroEffect(_owner.Version, definition, cause, count) : _owner.AddNpcEffect(_owner.Version, npc!.Combat.Target, definition, cause, count);
            _log.Add(added.Disposition == GameSessionDisposition.Changed ? new(SpellCastEventKind.EffectAdded, run.Id, data.Effect, obj.Id, count)
                : new(SpellCastEventKind.EffectNotSaved, run.Id, (int)added.Failure, obj.Id, data.Effect));
            return true;
        }
        var removed = isHero ? _owner.RemoveHeroEffect(_owner.Version, data.Effect, count) : _owner.RemoveNpcEffect(_owner.Version, npc!.Combat.Target, data.Effect, count);
        if (removed.Disposition == GameSessionDisposition.Changed) _log.Add(new(SpellCastEventKind.EffectRemoved, run.Id, data.Effect, obj.Id, count));
        else if (removed.Disposition == GameSessionDisposition.Rejected) _log.Add(new(SpellCastEventKind.EffectNotSaved, run.Id, (int)removed.Failure, obj.Id, data.Effect));
        return true;
    }

    private const int NoRun = -1;

    /// <summary>The run finder (0x459170, ER6) as far as it is modelled: the first active run (table order) whose target is <paramref name="victim"/> and whose record sets every bit of <paramref name="flag"/>; the runs' object lists (+0x130, +0x134) are not modelled.</summary>
    private SpellRun? RunProvidingFlag(string victim, int flag) =>
        _owner.LiveSpellRuns().Runs.FirstOrDefault(r => (r.Flags & SpellRunFlags.Active) != 0 && r.Target?.Id == victim
            && _records(r.Spell) is { } record && (unchecked((int)record.Data.SpellFlagsSet) & flag) == flag);

    /// <summary>
    /// The hit effect (0x459A30, called by every hit that reaches a PC or NPC: the attack end 0x4B6930 and the damage effect 0x433020). For each of the spell flags shielded (0x400), magnetic inversion
    /// (0x400000) and entangled (0x1000000) the critter has now it looks for the first active run whose object it is and whose record sets that flag (the run finder 0x459170, ER6, SFR3).
    /// <b>Shielded and magnetic inversion</b> (ARC-MAGIC-045): the run's spell plays its hit eye candy (entry 6 × spell + 5, no play flags, caused by the run) on the critter, an
    /// <see cref="SpellCastEventKind.EyeCandyAdded"/> the host draws; no run found plays nothing for shielded (the original prints an assertion text), and for magnetic inversion the fixed entry
    /// <see cref="MagneticInversionFixedEyeCandyEntry"/> with no run (-1). No state changes. <b>Entangled</b> (RS10): that run's save is asked again
    /// (<see cref="SpellResistanceRules.ReResist"/>, 0x459C10). A critter that resists again plays "resisted" (0x45A520) and the run ends by event (0x4571E0): the END clears the flag and takes the speed
    /// effect off. One that does not keeps the hold. The cached target type the save reads is the run processing's last (<see cref="MagicAnomalyProfile.ReResistReadsCachedTargetType"/>,
    /// MG48) or the critter's own. The one prototype whose hit plays a fixed entry (0x4EB, a packet field not read yet) is not modelled. Returns whether the
    /// critter's entangle ended now.
    /// </summary>
    [OriginalFunction(0x459A30, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x459170, OriginalFunctionScope.Partial)]
    public bool CritterHit(string victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        var flags = CurrentFlags(victim, SessionObjectFlagField.Spell);
        if ((flags & (ShieldedSpellFlag | MagneticInversionSpellFlag | EntangledSpellFlag)) == 0) return false;
        // Shielded first, then magnetic inversion (the order of 0x459A30): the eye candy goes to the host before the entangled branch's save.
        if ((flags & ShieldedSpellFlag) != 0 && RunProvidingFlag(victim, ShieldedSpellFlag) is { } shield)
            _log.Add(new(SpellCastEventKind.EyeCandyAdded, shield.Id, SpellEyeCandyRules.EntryIndex(shield.Spell, SpellEyeCandyRules.Hit), victim, 0));
        if ((flags & MagneticInversionSpellFlag) != 0)
        {
            if (RunProvidingFlag(victim, MagneticInversionSpellFlag) is { } magnet)
                _log.Add(new(SpellCastEventKind.EyeCandyAdded, magnet.Id, SpellEyeCandyRules.EntryIndex(magnet.Spell, SpellEyeCandyRules.Hit), victim, 0));
            else _log.Add(new(SpellCastEventKind.EyeCandyAdded, NoRun, MagneticInversionFixedEyeCandyEntry, victim, 0));
        }
        if ((flags & EntangledSpellFlag) == 0) return false;
        var run = RunProvidingFlag(victim, EntangledSpellFlag);
        if (run is null || RunObject(victim) is null) return false; // 0x459C8F: no object, the save holds before any die is rolled
        var target = ResistTarget(run.Spell, run.Parent?.Id, RunObject(victim));
        var cached = Profile.ReResistReadsCachedTargetType ? _cachedTargetKind ?? SpellTargetKind.Other : target.Kind;
        var answer = SpellResistanceRules.ReResist(ResistRecord(run.Spell), target, cached, (run.Flags & SpellRunFlags.Unresistable) != 0, Rolls, FateSave, Profile);
        if (answer.Holds)
        {
            _log.Add(new(SpellCastEventKind.EntangleHeld, run.Id, 0, victim));
            return false;
        }
        _log.Add(new(SpellCastEventKind.Resisted, run.Id, 0, victim));
        if (IsLocalPlayer(run.Parent)) _log.Add(new(SpellCastEventKind.Message, run.Id, ResistedMessage));
        _log.Add(new(SpellCastEventKind.EntangleBroken, run.Id, 0, victim));
        // 0x4571E0 only queues the run's interrupt: while the run rules are under way (a hit inside a spell's damage) the ending waits for the outer action, as the other endings do.
        if (!_goneObjects.Any(g => g.Kind == SessionSpellRunActionKind.EndByEvent && g.Processing == run.Id) && _goneObjects.Count < MaxPendingRunEnds)
            _goneObjects.Add((SessionSpellRunActionKind.EndByEvent, victim, run.Id));
        FirePendingItems();
        return true;
    }

    /// <summary>Critter flags 2 (field 0xDB) bit 0x100: the damage effect (0x433020) returns before it reaches the hit effect for such a critter (the "no blood splotch" bit), so no re-resistance follows.</summary>
    public const int NoDamageEffectCritterFlag2 = 0x100;

    /// <summary>
    /// ARC-MAGIC-1143 (ledger RH1–RH5): the damage effect of a hit on a PC or NPC (0x433020), which ends by calling the hit effect (0x459A30, <see cref="CritterHit"/>). It returns first (no hit
    /// effect, so no re-resistance) for a critter that is destroyed or switched off (object flags 0x1, 0x2) and for one whose critter flags 2 hold <see cref="NoDamageEffectCritterFlag2"/>; the multiplayer gate does not apply here. Returns whether the critter's entangle ended now.
    /// </summary>
    private bool DamageEffect(string victim)
    {
        if ((CurrentFlags(victim, SessionObjectFlagField.Object) & (SpellTargetObject.Destroyed | SpellTargetObject.Off)) != 0) return false;
        if ((CurrentFlags(victim, SessionObjectFlagField.Critter2) & NoDamageEffectCritterFlag2) != 0) return false;
        return CritterHit(victim);
    }

    /// <summary>
    /// ARC-MAGIC-1143 (RH1, RH3): the tail of a spell's Damage component (0x4518D0, call 0x451AE2) for the critter <paramref name="victim"/> whose damage step just ran: when the step dealt damage
    /// (the packet's "damage dealt" flag 0x200000, which the damage step 0x4B4390 sets for a PC or NPC whose hit-point or fatigue damage is above 0; a poison amount does not count) and the victim is not
    /// destroyed or off, the damage effect runs (<see cref="DamageEffect"/>), so an entangled victim asks its save again as for a weapon hit. A step that dealt nothing (a prevented, vetoed or dead
    /// victim, or damage resisted down to 0) runs nothing. Returns whether the critter's entangle ended now.
    /// </summary>
    public bool SpellDamageHit(string victim, AttackDamage step)
    {
        ArgumentNullException.ThrowIfNull(victim);
        ArgumentNullException.ThrowIfNull(step);
        return step.Prevented == DamagePrevention.None && (step.HitPointDamage > 0 || step.FatigueDamage > 0) && DamageEffect(victim);
    }

    /// <summary>
    /// ARC-MAGIC-1143 (RH4): the attack end (0x4B6930) a native trap's damage runs through its context flag 0x80000 (TT13; set for the damage scripts 30001 and 30003..30006), inside the damage step on the critter
    /// <paramref name="victim"/>: after the immunity zeroing and the resistance step (0x4B6860) but before the dead test, so it reads the per-type amounts that are left (<see cref="AttackDamage.Final"/>: normal, poison,
    /// electrical, fire). An amount above 0 in any of them runs the damage effect (<see cref="DamageEffect"/>); none (fully resisted or immune) calls the hit effect directly (<see cref="CritterHit"/>). The caller has made
    /// the gates that come before it in the original (the victim's taking-damage script, the invulnerable and destroyed gate). Returns whether the critter's entangle ended now.
    /// </summary>
    public bool TrapDamageHit(string victim, AttackDamage step)
    {
        ArgumentNullException.ThrowIfNull(victim);
        ArgumentNullException.ThrowIfNull(step);
        var positive = step.Final.Normal > 0 || step.Final.Poison > 0 || step.Final.Electrical > 0 || step.Final.Fire > 0;
        return positive ? DamageEffect(victim) : CritterHit(victim);
    }
}
