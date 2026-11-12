using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-MAGIC-1232 (DW2, XR15 of ARC-RES-1109): the weapon block of a spell's damage step (0x4B4390) on a critter the step reached, for a Damage component with the weapon-drop flag (<c>Weap_Dropped</c>, 0x2000: Disarm, record 0, one
    /// component of amount 0): the **target's** wielded weapon (slot 1004) is dropped through the session's critical drop (<see cref="GameSessionOwner.SpellWeaponDrop"/>, the step an attack's critical uses: CR14's drop place, the drop script and the
    /// no-drop flag may refuse, the hero's item joins its placed items, an NPC's seed item lies loose). The zero amount ends nothing (DW1); a spell's step has no critical procedure, so an NPC's Disarm drops a PC's weapon too. Message 19 is the host's
    /// floater, read from <see cref="SessionSpellDamageStep.Criticals"/>. Each step is logged (<see cref="SpellCastEventKind.WeaponDropped"/>); a refused write is logged and the damage stands.
    /// </summary>
    private IReadOnlyList<SessionCriticalStep> DropWeapon(SpellRun run, string victim, SessionLifeTarget who)
    {
        // The hero's drop is at its own cell (the session's); an NPC's at the cell the map shows it on (a cell outside the drop's packing is refused by the step).
        CellCoord? cell = victim != Hero && _map.Object(victim) is { } found && found.Location.X <= int.MaxValue && found.Location.Y <= int.MaxValue
            ? new CellCoord((int)found.Location.X, (int)found.Location.Y) : null;
        var steps = _owner.SpellWeaponDrop(who, _map.WornBySlot(victim), cell, _map.DropMap, out var failure);
        foreach (var step in steps) _log.Add(new(SpellCastEventKind.WeaponDropped, run.Id, (int)step.Outcome, victim));
        if (failure != GameSessionFailure.None) _log.Add(new(SpellCastEventKind.WeaponDropNotSaved, run.Id, (int)failure, victim));
        return steps;
    }
}
