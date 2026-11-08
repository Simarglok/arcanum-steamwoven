using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-296 (B42): one damage step on a critter that the cast host applied through the session's life step (a spell's Damage
/// component, a trap, a burn or a script's damage): the victim, the damage packet as the step drew it, the life step's outcome and the
/// attacker (the run's parent; null for a burn, a script's damage or a trap nobody placed). The original's damage function (0x4B4390)
/// prints its floaters inside the step whoever called it (ledger CH12), so the host that draws them reads the steps from here.
/// </summary>
public sealed record SessionSpellDamageStep(string Victim, AttackDamage Damage, LifeOutcome Life, string? Attacker);

public sealed partial class SessionSpellCaster
{
    /// <summary>The most damage steps and experience awards the host keeps for its taker; the oldest go first.</summary>
    public const int MaxPendingEntries = 256;

    private readonly List<SessionSpellDamageStep> _damageSteps = [];

    /// <summary>The damage steps applied since the last call, in order; the list is emptied.</summary>
    public IReadOnlyList<SessionSpellDamageStep> TakeDamageSteps()
    {
        var taken = _damageSteps.ToArray();
        _damageSteps.Clear();
        return taken;
    }

    private void QueueDamageStep(string victim, AttackDamage damage, LifeOutcome life, string? attacker)
    {
        if (_damageSteps.Count >= MaxPendingEntries) _damageSteps.RemoveAt(0);
        _damageSteps.Add(new(victim, damage, life, attacker));
    }
}
