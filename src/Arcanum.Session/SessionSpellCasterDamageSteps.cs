using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-296 (B42): one damage step on a critter that the cast host applied through the session's life step (a spell's Damage
/// component, a trap, a burn or a script's damage): the victim, the damage packet as the step drew it, the life step's outcome and the
/// attacker (the run's parent; null for a burn, a script's damage or a trap nobody placed). The original's damage function (0x4B4390)
/// prints its floaters inside the step whoever called it (ledger CH12), so the host that draws them reads the steps from here.
/// </summary>
public sealed record SessionSpellDamageStep(string Victim, AttackDamage Damage, LifeOutcome Life, string? Attacker)
{
    /// <summary>ARC-MAGIC-1232 (DW2): the weapon block's drop of this step (a Damage component with the weapon-drop flag, Disarm): one <see cref="SessionCriticalStepKind.DropWeapon"/> step, none for a step without the flag. The host that draws
    /// the floaters reads message 19 from it as for an attack's critical drop.</summary>
    public IReadOnlyList<SessionCriticalStep> Criticals { get; init; } = [];

    /// <summary>ARC-AI-1638: true for the step of a script's damage (<c>SessionSpellCaster.ScriptDamage</c>): the script answer already asks the follower's near-death line (<c>ScriptDamageAftermath</c>, ARC-DEV-245), so the host that drains
    /// the steps must not ask it a second time. False for a spell's, a burn's and a trap's step.</summary>
    public bool FromScript { get; init; }
}

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

    private void QueueDamageStep(string victim, AttackDamage damage, LifeOutcome life, string? attacker, IReadOnlyList<SessionCriticalStep>? criticals = null, bool fromScript = false)
    {
        if (_damageSteps.Count >= MaxPendingEntries) _damageSteps.RemoveAt(0);
        _damageSteps.Add(new(victim, damage, life, attacker) { Criticals = criticals ?? [], FromScript = fromScript });
    }
}
