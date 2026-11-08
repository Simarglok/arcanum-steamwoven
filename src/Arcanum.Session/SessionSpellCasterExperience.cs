using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

// ARC-DEV-295 (B39): the experience steps of the damage steps this host applies. The original runs them inside its damage function
// (0x4B4390, XP10) and the death notification (0x45DA20, XP9), so a spell's damage pays them like a weapon's: the pool share leaves the
// NPC's pool, the hero gets it when the damage source is the hero, and a kill pays a source the hero leads. A step with no source
// object (burn, a script's damage) pays nothing.
public sealed partial class SessionSpellCaster
{
    private readonly List<SessionExperienceAward> _awards = [];

    /// <summary>
    /// The awards the damage steps of this host gave the hero since the last call, in order, for the host to show their level-up
    /// messages and to level the followers (as it does for a weapon's hit and kill awards); the list is emptied.
    /// </summary>
    public IReadOnlyList<SessionExperienceAward> TakeExperienceAwards()
    {
        var taken = _awards.ToArray();
        _awards.Clear();
        return taken;
    }

    // One applied life step on a victim: only for an NPC the map holds, and only with a source (the packet's +0x30: the run's other
    // object, else its parent; a trap's PC placer).
    private void RecordDamageExperience(string victim, LifeOutcome outcome, int maxHitPoints, string? source, int run)
    {
        if (source is null || _map.Critter(victim)?.Combat is not { } npc) return;
        var by = source == Hero ? SessionCombatant.Hero : SessionCombatant.Critter(source);
        var result = _owner.RecordDamageExperience(_owner.Version, npc, outcome, maxHitPoints, by);
        // A pool share already given stays queued and logged when the kill step is refused afterwards.
        if (result.Hit is not null) _awards.Add(result.Hit);
        if (result.Kill is not null) _awards.Add(result.Kill);
        if (result.Hit is not null || result.Kill is not null)
            _log.Add(new(SpellCastEventKind.ExperienceGiven, run, result.Hit?.Award ?? 0, victim, result.Kill?.Award ?? 0));
        if (result.Failure != GameSessionFailure.None) _log.Add(new(SpellCastEventKind.ExperienceNotSaved, run, (int)result.Failure, victim));
    }
}
