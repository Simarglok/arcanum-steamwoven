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
    /// whole attempt. NOT READY: gambling (its stored roll), heal, pick pocket, repair and pick locks (their own handlers). Refused: a skill that is none of <see cref="IsPlainNpcSkill"/>, a user the
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

    private SessionNpcSkillRoll RejectNpcSkillRoll(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, false, Version);
}
