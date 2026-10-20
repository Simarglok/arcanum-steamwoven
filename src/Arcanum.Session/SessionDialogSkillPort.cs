using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// Dialog skill seam over a <see cref="GameSessionOwner"/> (ARC-SKILLS-004): the hero's effective skill levels and training
/// (<see cref="GameSessionOwner.QueryHeroSkills"/>) for the gates PS, HA, SK, TR and ET, and the training action TR
/// (<see cref="GameSessionOwner.SetHeroSkillTraining"/>). A skill number outside 0..15 reads level 0 and no training and its
/// training action changes nothing, as the source's getters and setters answer 0 for it. A PC other than
/// <paramref name="playerCharacter"/>, or a session without a hero, throws on a read (the conversation records a query failure)
/// and is Rejected on the action. The session is read at each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogSkillPort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogSkillPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public int Level(DialogSkillQuery query) => Skill(query) is { } skill ? skill.Level : 0;

    public int Training(DialogSkillQuery query) => Skill(query) is { } skill ? (int)skill.Training : 0;

    /// <summary>A tier the rules refuse, the same tier, or a skill or tier out of range is Applied with nothing written, as the
    /// source goes on after its setter refuses.</summary>
    public DialogCommandResult Execute(SkillTrainingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter || _session.QueryHero() is null) return DialogCommandResult.Rejected;
        if (command.Skill is < 0 or >= SkillRules.Count || command.Training is < 0 or > (int)SkillTraining.Master) return DialogCommandResult.Applied;
        var result = _session.SetHeroSkillTraining(_session.Version, (Skill)command.Skill, (SkillTraining)command.Training);
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    private SessionSkill? Skill(DialogSkillQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        var skills = _session.QueryHeroSkills() ?? throw new InvalidOperationException("The session has no hero.");
        return query.Skill is >= 0 and < SkillRules.Count ? skills[(Skill)query.Skill] : null;
    }
}
