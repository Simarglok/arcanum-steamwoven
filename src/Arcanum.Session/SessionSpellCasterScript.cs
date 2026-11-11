using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-DEV-241 (MIG13 S14f, GT183/GT184): a script's cast (actions 48, 61, 118, 121, 122, 136, the dispatcher 0x455AC0 behind the invocation 0x455A20) of <paramref name="spell"/> at <paramref name="targetObject"/> by
    /// <paramref name="caster"/> (null: none, as 61 and 136 and a cast whose caster handle is absent). With a caster it is the source and the parent (as <see cref="CastByAi"/>'s, in a turn-based round with the round's actor);
    /// without one the invocation has neither, so the run has no parent and no casting goal and its BEGIN comes as an event. The spell UI's own tests are not made: not the cost (<see cref="CastAt"/>'s 0x4508A0, spell.mes 600) and not its line-of-fire
    /// message (spell.mes 604); there is no reach test. The gates are the invocation check (0x4564E0, which includes the range, the line of fire 0x456BC0 and the forbidden flags; a refusal does nothing and shows no message) and
    /// the dispatcher's own (the activation checks it a second time). <paramref name="free"/> is invocation
    /// flag 0x2 (the run's Free flag: the begin and the casting goal's pre-animation cost are not charged), <paramref name="unresistable"/> flag 0x4 (the run's Unresistable flag, RN3). A caster's casting goal, 26 or 82
    /// by the record's casting animation, is the host's (<see cref="StartGoal"/>). Nothing for a missing record, caster or target object.
    /// </summary>
    public SpellCastAttempt CastByScript(string? caster, int spell, string targetObject, bool free = false, bool unresistable = false)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        if (_records(spell) is not { } record || Facts(targetObject) is not { } target) return new(SpellCastAttemptOutcome.Nothing);
        var facts = caster is null ? null : Facts(caster);
        if (caster is not null && facts is null) return new(SpellCastAttemptOutcome.Nothing);
        var round = _owner.QueryCombatRound();
        var invocation = new SpellInvocation(spell, facts, facts, target, SpellLocation.None, null, TeleportAllowed: caster is null || TeleportAllowedFor(spell, caster), ParentIsLocalPlayer: caster is not null && caster == Hero,
            TurnBased: round is not null, TurnBasedActor: round is null ? null : round.Actor.Entity ?? Hero);
        var world = InvocationWorld();
        var check = SpellInvocationRules.Check(invocation, record.Run.Invocation, world, _owner.SpellRolls);
        if (!check.Allowed)
        {
            // ARC-MAGIC-038 (0x4564E0): the anti-teleport refusal shows message 10000 whenever the parent is the local player.
            if (check.ShowsTeleportMessage) _log.Add(new(SpellCastEventKind.Message, -1, TeleportRefusedMessage));
            return new(SpellCastAttemptOutcome.Refused);
        }
        var started = _owner.StartSpellRun(_owner.Version, new(invocation, Free: free, Unresistable: unresistable), record.Run, world, _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run) return new(SpellCastAttemptOutcome.Refused, null, started);
        _itemChains.Remove(run.Id);
        return new(SpellCastAttemptOutcome.Started, null, started, run.Id);
    }
}
