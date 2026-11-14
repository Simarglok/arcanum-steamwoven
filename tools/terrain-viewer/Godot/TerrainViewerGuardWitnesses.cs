using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-AI-2084 (FR-AI, ledger TB28 and GW1-GW8): the guard witnesses (0x4AEE50) of an act on a portal, container or scenery in the shown map. The AI host owns the rule (AreaAiHost.GuardWitnesses: the square of
// 10 tiles around the object, name or guard class, sight or hearing, the catching-thief script, the reaction, the attack event and the line to the actor); this partial asks it from the callers the original has:
// the damage step of an attack (loud), the pick-locks skill (silent) and a spell that clears a lock (loud). The object's guard field is its notify NPC (portal slot 39, container slot 42).
public partial class TerrainViewerApp
{
    private const int NotifyNpcPortalSlot = 39, NotifyNpcContainerSlot = 42;

    /// <summary>
    /// The damage step's witnesses of an attack on the object <paramref name="entity"/> (the damage step, after the new hit points are written and once the break's trap and BUST script ran): the call is mode 0 for a hit that broke the object,
    /// mode 1 for one that left it whole, both loud, and only for an attacker that is the hero or an NPC a player character leads (the AI host's own gate); the host steps say which of the two the session decided.
    /// </summary>
    private void WitnessesOfObjectHit(string entity, AreaObjectRecord row, SessionObjectHostSteps steps, string actor)
    {
        var mode = (steps & SessionObjectHostSteps.WitnessesHearBreak) != 0 ? GuardWitnessMode.Done
            : (steps & SessionObjectHostSteps.WitnessesHearAttack) != 0 ? GuardWitnessMode.Attempt : (GuardWitnessMode?)null;
        if (mode is { } called) GuardWitnessesOf(entity, row, actor, called, Loudness.Loud, "damage");
    }

    /// <summary>
    /// The pick-locks skill's witnesses (only for an object that was locked): mode 0 when the pick opened the lock, mode 1 when it failed (<see cref="PickLockOutcome.WitnessFailure"/>), both silent, so a witness
    /// must see the picker or hear it at the silent range. <paramref name="actor"/> is the picker (the hero or an NPC a player character leads).
    /// </summary>
    private void WitnessesOfLockPick(string entity, AreaObjectRecord row, string actor, Arcanum.Simulation.Skills.PickLockOutcome? outcome)
    {
        if (outcome is not { WitnessesCalled: true } called) return;
        GuardWitnessesOf(entity, row, actor, called.WitnessFailure ? GuardWitnessMode.Attempt : GuardWitnessMode.Done, Loudness.Silent, "lock pick");
    }

    /// <summary>The witnesses of a spell that cleared the lock flag of a portal or container (the flag component's remove path, mode 0, loud; never a line).</summary>
    private void WitnessesOfLockSpell(string entity, AreaObjectRecord row, string caster) => GuardWitnessesOf(entity, row, caster, GuardWitnessMode.Done, Loudness.Loud, "lock spell");

    /// <summary>The shown map's row of a portal, container or scenery by its entity id (the cast map's, else the inventory host's map).</summary>
    private AreaObjectRecord? ObjectRowByEntity(string entity) =>
        _spellCastMap?.Row(entity) ?? (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row) ? row : null);

    /// <summary>The witnesses of an NPC's pick of a lock (the AI host's pick-locks attempt, a follower's own skill goal): the target string names a door or window, or a container's "inv:" string; the NPC is the actor, which
    /// the host's rule admits only when a player character leads it.</summary>
    private void WitnessesOfNpcLockPick(string npc, string target, SessionPickLockResult pick)
    {
        if (pick.Disposition == GameSessionDisposition.Rejected) return;
        string? entity = null;
        AreaObjectRecord? row = null;
        if (_areaDoors?.PortalOf(target) is { } door) { entity = door.Entity; row = ObjectRowByEntity(door.Entity); }
        else if (ContainerOfSkillTarget(target) is { } container) { entity = container.Entity; row = container.Row; }
        if (entity is not null && row is not null) WitnessesOfLockPick(entity, row, npc, pick.Outcome);
    }

    private void GuardWitnessesOf(string entity, AreaObjectRecord row, string actor, GuardWitnessMode mode, Loudness loudness, string why)
    {
        if (AreaAiHostNow() is not { } ai || row.WorldX is not { } x || row.WorldY is not { } y) return;
        var notify = row.EffectiveFields.FirstOrDefault(f => f.Slot == (row.Type == 1 ? NotifyNpcPortalSlot : NotifyNpcContainerSlot) && f.Presence == AreaPresence.Value)?.Number;
        var at = new Arcanum.Simulation.Movement.CellCoord(checked((int)x), checked((int)y));
        var reports = ai.GuardWitnesses(actor, entity, row.Type, notify is { } n ? unchecked((int)n) : 0, at, mode, loudness);
        if (reports.Count == 0) return;
        GD.Print($"ARC-AI-2084 guard-witnesses {why} object={entity} actor={actor} mode={mode} loudness={loudness} witnesses={string.Join(",", reports.Select(r => $"{r.Witness}:{(r.ReactionLowered ? "lowered" : "-")}:{(r.Attacked ? "attacked" : "-")}:{(r.Shouted ? "line" : "-")}"))}");
    }

    /// <summary>
    /// A guard witness's catching-thief script (point 11) at an act on <paramref name="guarded"/>: the witness is the attachee, the actor the triggerer (the hero when it is the actor) and the guarded object the extra object.
    /// True when the witness goes on ("run default", or no script that runs); anything else, the script's skip of the default or a run the viewer cannot finish, leaves the witness out.
    /// </summary>
    private bool AiGuardCatchingThief(string witness, string actor, string guarded)
    {
        if (_readyArea is not { } area || _areaSession is not { } session || UseScriptHost(area.Snapshot) is not { } host
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map || !map.TryGetRow(witness, out var row)
            || host.Slot(new(CurrentAreaMap, witness), AreaUseScriptHost.CatchingThiefPoint) is not { Script: > 0 }) return true;
        Arcanum.Scripting.Scr.ScrObjectRef? thief = actor == Arcanum.Simulation.Ai.AiSchedulerRules.Pc ? null : new(CurrentAreaMap, actor);
        var ran = Awarded(host, host.RunCatchingThief(row, session.Rules.Scripts, thief, new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, guarded)));
        GD.Print($"ARC-AI-2084 guard catching-thief witness={witness} actor={actor} object={guarded} outcome={ran.Outcome} run={ran.Run?.Outcome} detail={ran.Detail}");
        return ran.Outcome == AreaScriptedUseOutcome.NoTeleport && ran.Run?.Outcome != Arcanum.Scripting.Scr.ScrOutcome.SkipDefault;
    }
}
