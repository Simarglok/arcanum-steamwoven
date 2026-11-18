using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>
/// ARC-AI-1854: the answer of an NPC's plain skill attempt (<see cref="GameSessionOwner.RollNpcSkill"/>): the shared check (<see cref="SkillCheckResult"/>), the success
/// the run's flag 0x40000 takes (the spot trap's own case and the dodge gate applied), the critical bit the run's flag 0x10 takes after the same cases (a forced failure of the dodge gate or of the spot trap's magical-trap refusal
/// clears it; <see cref="Check"/> keeps the roll's own), whether the master's second roll was drawn, and the version.
/// </summary>
public sealed record SessionNpcSkillRoll(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, bool Success, bool SecondRoll, GameSessionVersion Version)
{
    /// <summary>The critical bit (0x10 of the invocation's word) the attempt ends with: the check's critical, cleared by a forced failure (exe 0x4C73FB); false for a refused attempt.</summary>
    public bool Critical { get; init; }
}

public sealed partial class GameSessionOwner
{
    /// <summary>The skills <see cref="RollNpcSkill"/> answers: those the goal-33 attempt (0x42A2A0 → 0x4C7160) has no case for, whose answer is the shared check's success alone, and the two with a small gate of their own (dodge, spot trap).</summary>
    public static bool IsPlainNpcSkill(Skill skill) =>
        skill is Skill.Bow or Skill.Dodge or Skill.Melee or Skill.Throwing or Skill.Backstab or Skill.Prowling or Skill.SpotTrap or Skill.Haggle
            or Skill.Persuasion or Skill.Firearms or Skill.DisarmTraps;

    /// <summary>
    /// ARC-AI-1854 (exe 0x4C7160, the cases a goal-33 attempt of an NPC reaches with nothing but the roll): the shared check of the skill (<see cref="SkillCheckRules.Run"/>: the NPC's own level and training,
    /// its extraordinary intelligence, the game difficulty, its injuries; no local-PC term and no fate) with no write but the generator's draws (one revision). The answer is the check's success, which the host
    /// turns into the run's flag 0x40000. Skill gates: dodge answers neither success nor critical while <paramref name="incapable"/> (exe case 1: the actor unconscious (0x45D800), stoned (spell flag 0x10000) or stunned or paralysed (critter flags 0x60),
    /// as the host reads them, ARC-AI-2625);
    /// spot trap gets the master's second roll after a failure that is no critical (<see cref="TrapRules.SpotCase"/>); the trap's class from <paramref name="trap"/> is the target test 0x4BBDD0's answer (ARC-AI-2625: 2 is
    /// <see cref="TrapClass.Magical"/>, which refuses a success of an attempt below expert and clears the critical; a target that holds no trap, or the default, changes nothing); disarm traps is the roll alone (the goal never sets the invocation's trap flag, so nothing is disarmed). The other skills have no case: the roll is the
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
        var critical = false;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            success = check.Success;
            critical = check.Critical;
            if (skill == Skill.SpotTrap)
                (success, critical) = TrapRules.SpotCase(check.Success, check.Critical, request.Training, trap, check.Check.Effectiveness, check.Check.Difficulty,
                    () => { second = true; return rolls.D100(SkillCheckRoll.Check); });
            if (skill == Skill.Dodge && incapable) (success, critical) = (false, false);
        }
        catch (SkillRolls.ExhaustedException) { return RejectNpcSkillRoll(GameSessionFailure.RandomExhausted); }
        _random = rolls.Trial;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, success, second, Version) { Critical = critical };
    }

    private SessionNpcSkillRoll RejectNpcSkillRoll(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, false, Version);
}
