using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1146 (the leftovers of the 0x44–0x45 triage, ARC-RES-108, ledger RM1–RM8): the component executors the table 0x5B0BA8 still had no handler for, and the spell stop on an object.
/// <list type="bullet">
/// <item><b>EnvFlag</b> (type 10, 0x452F10): the original's entry only writes a log line, so the component does nothing; the table takes it with no effect and no "not ready" line.</item>
/// <item><b>Recharge</b> (type 17, 0x452800 → 0x454920): for an item target (object type 5..14) adds the component's amount to the item's spell charges (field 0x69), not above <c>Max:</c> (9999) and only while they are below it;
/// answers 1 for an item and does nothing to any other object (a tile does nothing either). The session keeps the charges as it keeps the ones a payment spends (<see cref="GameSessionOwner.RechargeItemCharges"/>, no schema step).</item>
/// <item><b>Trait64</b> (type 23, 0x452E40): with the source <c>Target_Tile</c> the component writes the target's 64-bit field (<c>Teleport_Tile</c>, 242) with the run's target tile and field 243 (the teleport map) with the current map; with
/// <c>Caster_Obj</c> or <c>Caster_Owner_Obj</c> (the original reads the same run field for both) it writes the run's parent handle into the field (<c>obj_f_critter_fleeing_from</c>, 230). Modelled for an NPC of the session
/// (<see cref="GameSessionOwner.SetTeleportTile"/>, <see cref="GameSessionOwner.WriteFleeingFrom"/>); any other field, source or target is not ready. No installed spell uses it.</item>
/// <item><b>TraitIdx</b> (type 22, 0x452D80): built later, in ARC-MAGIC-1295 (<c>SessionSpellCasterTraitIndex.cs</c>).</item>
/// </list>
/// And the spell stop on an object (0x459290 and the script action 130, 0x4593F0): <see cref="StopSpellOnObject"/>.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object fields Trait64 writes in this host: the teleport tile (242, with the teleport map 243) and the one the critter flees from (230).</summary>
    public const int TeleportTileField = 242, FleeingFromField = 230;

    // 0x452F10: the stub of entry 10 does nothing but write a log line (the line is no game state).
    [OriginalFunction(0x452F10, OriginalFunctionScope.Full)]
    private static SpellComponentOutcome EnvFlagComponent() => SpellComponentOutcome.None;

    // 0x454920: an item (types 5..14) gains the amount, not above the maximum; any other target (a critter, scenery, a tile) is left and answers 0, which the component ignores.
    [OriginalFunction(0x454920, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? RechargeComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Recharge is not { } data) return null;
        if (target.Object is not { } obj || obj.Type is < 5 or > 14) return SpellComponentOutcome.None;
        // The item's charges are the host's (an item it does not know is not ready, as the pane's is); the host's answer already reads the session's count first (Item).
        if (Item(obj.Id) is not { } item) return null;
        var result = _owner.RechargeItemCharges(_owner.Version, obj.Id, item.Charges, data.Amount, data.Maximum);
        switch (result.Disposition)
        {
            case GameSessionDisposition.Changed:
                _log.Add(new(SpellCastEventKind.ItemRecharged, run.Id, _owner.QueryItemCharges(obj.Id) ?? item.Charges, obj.Id, data.Amount));
                break;
            case GameSessionDisposition.Rejected:
                _log.Add(new(SpellCastEventKind.ItemRechargeNotSaved, run.Id, (int)result.Failure, obj.Id));
                break;
        }
        return SpellComponentOutcome.None;
    }

    // 0x452E40: the field is the component's (242 or 230 modelled); the source index 0 writes the target tile and the current map, 1 and 2 the run's parent handle. The target is an NPC of the session.
    [OriginalFunction(0x452E40, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? Trait64Component(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Trait64 is not { } data) return null;
        // No current target object: the original's writes go to the null handle (which it would not survive); the component does nothing here, as the other field writers do.
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        // A parent that is no critter (a trap's or scenery's spell) is not kept as a creature link by the session: not ready.
        if (data.Source is 1 or 2 && run.Parent is { IsCritter: false }) return null;
        if (obj.Type != SpellObjectFacts.NpcType || !obj.Id.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return null;
        if (data.Source == 0 && data.Field == TeleportTileField)
        {
            var map = _owner.QueryActor().Map;
            var written = _owner.SetTeleportTile(_owner.Version, obj.Id, map, target.Tile.X, target.Tile.Y);
            _log.Add(written.Disposition == GameSessionDisposition.Rejected ? new(SpellCastEventKind.Trait64NotSaved, run.Id, (int)written.Failure, obj.Id)
                : new(SpellCastEventKind.Trait64Written, run.Id, data.Field, obj.Id, data.Source));
            return SpellComponentOutcome.None;
        }
        if (data.Source is 1 or 2 && data.Field == FleeingFromField)
        {
            // A parent that is gone (the original copies its handle all the same) writes none: the field clears.
            var written = _owner.WriteFleeingFrom(_owner.Version, obj.Id, run.Parent?.Id);
            _log.Add(written.Disposition == GameSessionDisposition.Rejected ? new(SpellCastEventKind.Trait64NotSaved, run.Id, (int)written.Failure, obj.Id)
                : new(SpellCastEventKind.Trait64Written, run.Id, data.Field, obj.Id, data.Source));
            return SpellComponentOutcome.None;
        }
        return null;
    }

    /// <summary>
    /// 0x459290 with 0x4593F0 (the script action 130 "stop spell on object"): the first active run, in slot order, of <paramref name="spell"/> whose target object, run list (+0x130) or summoned list (+0x134) holds
    /// <paramref name="obj"/> is interrupted (0x457110, <see cref="SpellRunSystem.Interrupt"/>: the maintained run's END, or its callbacks). The answer is whether such a run was found, even when the interrupt then changed nothing
    /// (the original answers 1 after calling the interrupt). A run that is not active, another spell and a null object find none. The script action is the interpreter's action 130 of set S42 (ARC-SCRIPT-1294), which the AI host's `ScriptStopSpell` answers.
    /// </summary>
    [OriginalFunction(0x4593F0, OriginalFunctionScope.Full)]
    public bool StopSpellOnObject(int spell, string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (FindRunOnObject(spell, obj) is not { } run) return false;
        Act(new(SessionSpellRunActionKind.Interrupt, run.Id));
        return true;
    }

    /// <summary>0x459290: the first run (slot order) that is active, runs <paramref name="spell"/> and holds <paramref name="obj"/> as its target, in its object list or in its summoned list; null when none does.</summary>
    [OriginalFunction(0x459290, OriginalFunctionScope.Full)]
    public SpellRun? FindRunOnObject(int spell, string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        foreach (var run in _owner.QuerySpellRuns().Runs.OrderBy(r => r.Id))
        {
            if ((run.Flags & SpellRunFlags.Active) == 0 || run.Spell != spell) continue;
            if (run.Target?.Id == obj || run.ObjectList.Holds(obj) || ((ISpellRunHost)this).SummonedListHolds(run, obj)) return run;
        }
        return null;
    }
}
