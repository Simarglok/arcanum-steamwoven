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

    // ARC-AI-1680 (0x4B4390 calls 0x4AEAB0 after the hit points are written and before the AI pass 0x4B52FC):
    /// <summary>
    /// The host's callback for a damage step at the moment it lands, inside the cast host's damage apply right after the step's life outcome is written and before the next step, the AI pass or the experience. The session's state is then the state after this step
    /// and none after a later one, so a speaker's can-speak gate reads it as the original does (the follower's near-death line is asked in the damage step). The steps stay queued for <see cref="TakeDamageSteps"/> (the floaters are the drain's). Null: no callback.
    /// </summary>
    public Action<SessionSpellDamageStep>? DamageStepApplied { get; set; }

    private void QueueDamageStep(string victim, AttackDamage damage, LifeOutcome life, string? attacker, IReadOnlyList<SessionCriticalStep>? criticals = null, bool fromScript = false)
    {
        if (_damageSteps.Count >= MaxPendingEntries) _damageSteps.RemoveAt(0);
        var step = new SessionSpellDamageStep(victim, damage, life, attacker) { Criticals = criticals ?? [], FromScript = fromScript };
        _damageSteps.Add(step);
        DamageStepApplied?.Invoke(step);
    }
}
