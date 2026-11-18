using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>
/// ARC-AI-1854: the answer of an NPC's plain skill attempt (<see cref="GameSessionOwner.RollNpcSkill"/>): the shared check (<see cref="SkillCheckResult"/>), the success
/// the run's flag 0x40000 takes (the spot trap's own case and the dodge gate applied), whether the master's second roll was drawn, and the version.
/// </summary>
public sealed record SessionNpcSkillRoll(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, bool Success, bool SecondRoll, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The skills <see cref="RollNpcSkill"/> answers: those the goal-33 attempt (0x42A2A0 → 0x4C7160) has no case for, whose answer is the shared check's success alone, and the two with a small gate of their own (dodge, spot trap).</summary>
    public static bool IsPlainNpcSkill(Skill skill) =>
        skill is Skill.Bow or Skill.Dodge or Skill.Melee or Skill.Throwing or Skill.Backstab or Skill.Prowling or Skill.SpotTrap or Skill.Haggle
            or Skill.Persuasion or Skill.Firearms or Skill.DisarmTraps;

    /// <summary>
    /// ARC-AI-1854 (exe 0x4C7160, the cases a goal-33 attempt of an NPC reaches with nothing but the roll): the shared check of the skill (<see cref="SkillCheckRules.Run"/>: the NPC's own level and training,
    /// its extraordinary intelligence, the game difficulty, its injuries; no local-PC term and no fate) with no write but the generator's draws (one revision). The answer is the check's success, which the host
    /// turns into the run's flag 0x40000. Skill gates: dodge answers neither success nor critical while <paramref name="incapable"/> (the actor's incapacitation, object flag 0x10000 or critter flags 0x60);
    /// spot trap gets the master's second roll after a failure that is no critical (<see cref="TrapRules.SpotCase"/>, the trap's class from <paramref name="trap"/>: a target that is no trap is non-magical here,
    /// the original's target test 0x4BBDD0 is not read); disarm traps is the roll alone (the goal never sets the invocation's trap flag, so nothing is disarmed). The other skills have no case: the roll is the
    /// whole attempt. Gambling is <see cref="RollNpcGambling"/>. NOT READY: repair (its own handler); heal, pick pocket and pick locks have their own. Refused: a skill that is none of <see cref="IsPlainNpcSkill"/>, a user the
    /// follower-skill rules refuse (<see cref="GameSessionFailure.SkillsUnavailable"/>), the version, the generator or the revision exhausted.
    /// </summary>
    public SessionNpcSkillRoll RollNpcSkill(GameSessionVersion expected, SessionNpcSkillUser user, Skill skill, bool incapable = false, TrapClass trap = TrapClass.Nonmagical)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        if (!IsPlainNpcSkill(skill)) return RejectNpcSkillRoll(GameSessionFailure.SkillsUnavailable);
        if (NpcSkillUserFailure(user, out var record, out _) != GameSessionFailure.None) return RejectNpcSkillRoll(GameSessionFailure.SkillsUnavailable);
        if (!IsCurrent(expected)) return RejectNpcSkillRoll(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return RejectNpcSkillRoll(GameSessionFailure.RevisionExhausted);
        var request = NpcSkillRequest(user, skill, record, null);
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        var success = false;
        var second = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            success = check.Success;
            if (skill == Skill.SpotTrap)
                (success, _) = TrapRules.SpotCase(check.Success, check.Critical, request.Training, trap, check.Check.Effectiveness, check.Check.Difficulty,
                    () => { second = true; return rolls.D100(SkillCheckRoll.Check); });
            if (skill == Skill.Dodge && incapable) success = false;
        }
        catch (SkillRolls.ExhaustedException) { return RejectNpcSkillRoll(GameSessionFailure.RandomExhausted); }
        _random = rolls.Trial;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, success, second, Version);
    }

    /// <summary>
    /// ARC-AI-2623 (exe 0x4C7160 with skill 8, the roll 0x4C6560): an NPC's gambling attempt, the shared check with the gambling skill's stored roll. The check's roll is the one the skill module stored before (the first
    /// draw of a session that holds none, as <see cref="Gamble"/>'s), the check's own draw is stored for the next gambling check of anyone (the hero's bet or another NPC's attempt: the module keeps one), the critical roll is
    /// drawn after it (all in one revision). The skill is opposed by the target's gambling level (<paramref name="opponentLevel"/>: 0x4C6410 over 0x4C5EB0, which reads 0 for a target that is no critter; null is no target, an
    /// effectiveness of 100); the extraordinary-intelligence term, the game difficulty (no local-PC term) and the NPC's injuries are the other plain skills'. No case of the handler follows the check (the PC's gambling fate point,
    /// 0x4F5270 kind 6, is read only for the PC itself): the answer is the check's success. Refused: a skill the user cannot use (<see cref="GameSessionFailure.SkillsUnavailable"/>), an opponent level outside 0..20
    /// (<see cref="GameSessionFailure.InvalidSkill"/>), the version, the generator or the revision exhausted.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4C6560, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    [Arcanum.Simulation.OriginalFunction(0x4C7160, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionNpcSkillRoll RollNpcGambling(GameSessionVersion expected, SessionNpcSkillUser user, int? opponentLevel)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        if (opponentLevel is < 0 or > SkillRules.MaxLevel) return RejectNpcSkillRoll(GameSessionFailure.InvalidSkill);
        if (NpcSkillUserFailure(user, out var record, out _) != GameSessionFailure.None) return RejectNpcSkillRoll(GameSessionFailure.SkillsUnavailable);
        if (!IsCurrent(expected)) return RejectNpcSkillRoll(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return RejectNpcSkillRoll(GameSessionFailure.RevisionExhausted);
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try
        {
            // A session that threw no roll draws it now, before the check's own draws (as Gamble's).
            var stored = _gamblingRoll ?? rolls.D100(SkillCheckRoll.Check);
            var request = NpcSkillRequest(user, Skill.Gambling, record, null);
            request = request with { Effectiveness = request.Effectiveness with { OpponentValue = opponentLevel }, GamblingRoll = stored };
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
        }
        catch (SkillRolls.ExhaustedException) { return RejectNpcSkillRoll(GameSessionFailure.RandomExhausted); }
        _random = rolls.Trial;
        _gamblingRoll = check.NextGamblingRoll;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, check.Success, false, Version);
    }

    private SessionNpcSkillRoll RejectNpcSkillRoll(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, false, Version);
}
