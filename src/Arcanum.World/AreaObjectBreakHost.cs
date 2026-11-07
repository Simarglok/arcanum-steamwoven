using Arcanum.Data.Area;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-260: what the host ran for one object break (<see cref="AreaObjectBreakHost.Run"/>): the trap's meeting (a native run's effect, or an
/// ordinary script's fired outcome), the ordinary trap script's run (null for a native trap or none), and the BUST script's run. Null parts did not run.
/// </summary>
public sealed record AreaObjectBreakRun(string Entity, AreaTrapMeeting? Trap, AreaScriptedUse? TrapScript, AreaScriptedUse? Bust)
{
    /// <summary>Whether anything ran.</summary>
    public bool Any => Trap is not null || TrapScript is not null || Bust is not null;

    /// <summary>Identifiers, outcomes and numbers only.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Trap is { } trap) parts.Add("trap " + trap.Describe());
        if (TrapScript is { Run: not null } script) parts.Add($"trap script {script.Run.Script} {script.Outcome}");
        if (Bust is { Run: not null } bust) parts.Add($"bust script {bust.Run.Script} {bust.Run.Outcome}");
        else if (Bust is { } refused) parts.Add($"bust script not run ({refused.Detail})");
        return string.Join("; ", parts);
    }
}

/// <summary>
/// ARC-DEV-260 (ledger OD4, OD15): the host's side of an object break. The session reports what the host owes after a hit that brought an
/// object to 0 hit points (<see cref="SessionObjectHostSteps.TrapFires"/>, <see cref="SessionObjectHostSteps.BustScript"/>); this runs it for the
/// hero's attack and for damage with no attacker alike. The original runs the trap (0x4BCB00: the hero as the triggerer for a melee attack,
/// none for a ranged attack or a damage with no attacker), then the break (0x43F1C0), whose BUST script (point 30) runs with the breaker as the
/// triggerer (the hero for any attack of the hero, none without an attacker) and whose answer is ignored. Here the session applies the break in
/// one revision first, so the trap runs after it; the one script that sees the order is a respawning scenery's BUST, which the original runs before
/// the object is switched off.
/// </summary>
public static class AreaObjectBreakHost
{
    /// <summary>Runs the trap and then the BUST script that <paramref name="steps"/> name for <paramref name="row"/>.</summary>
    /// <param name="traps">The shown map's trap host; null runs no trap.</param>
    /// <param name="byHero">The hero's attack broke the object (it is the BUST breaker).</param>
    /// <param name="ranged">The attack was ranged (it fires the trap with no triggerer).</param>
    public static AreaObjectBreakRun Run(AreaUseScriptHost host, AreaTraps? traps, AreaObjectRecord row, SessionObjectHostSteps steps, bool byHero, bool ranged,
        ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        var entity = AreaUseScriptHost.EntityId(row);
        AreaTrapMeeting? trap = null;
        AreaScriptedUse? trapScript = null, bust = null;
        if (steps.HasFlag(SessionObjectHostSteps.TrapFires) && traps is not null)
        {
            var triggerByHero = byHero && !ranged;
            trap = traps.Attack(row, triggerByHero);
            if (trap is { Outcome: SessionTrapOutcome.Fired, Result.Run: null })
                trapScript = host.RunTrapScript(row, triggerByHero ? host.Pc : null, rules);
        }
        if (steps.HasFlag(SessionObjectHostSteps.BustScript)) bust = host.RunBust(entity, byHero ? host.Pc : null, rules);
        return new(entity, trap, trapScript, bust);
    }
}
