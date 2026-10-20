using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's training through the session and the dialog skill port (ARC-SKILLS-004). The fixture hero is HeroSkillsTests':
// melee 2 points (level 8, apprentice), dodge 1 (4), persuasion 3 (7, master bits), firearms 1 (+2 innate: 6).
[TestClass]
public sealed class DialogSkillPortTests
{
    private static readonly DialogParticipantId Pc = new(2);

    private static GameSessionOwner Owner(SessionHeroDefinition? hero = null)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ?? HeroSkillsTests.Skilled() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void TheHerosTrainingRisesOneTierAtATimeWithinItsLevel()
    {
        using var owner = Owner();
        var raised = owner.SetHeroSkillTraining(owner.Version, Skill.Firearms, SkillTraining.Apprentice);
        Assert.AreEqual((GameSessionDisposition.Changed, SkillTraining.Apprentice), (raised.Disposition, owner.QueryHeroSkills()![Skill.Firearms].Training));
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.AreEqual(1 | 1 << 6, owner.QueryHero()!.Advancement.TechSkills[1], "the training bits in the stored value");

        var skipped = owner.SetHeroSkillTraining(owner.Version, Skill.Melee, SkillTraining.Master);
        Assert.AreEqual((GameSessionDisposition.NoChange, SkillChangeStatus.SkipsTier), (skipped.Disposition, skipped.Change!.Value.Status));
        var low = owner.SetHeroSkillTraining(owner.Version, Skill.Melee, SkillTraining.Expert);
        Assert.AreEqual((GameSessionDisposition.NoChange, SkillChangeStatus.LevelTooLow), (low.Disposition, low.Change!.Value.Status), "melee 8 < 9");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetHeroSkillTraining(owner.Version, Skill.Melee, SkillTraining.Apprentice).Disposition, "the same tier");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetHeroSkillTraining(owner.Version, Skill.Persuasion, SkillTraining.None).Disposition, "down to none");
        Assert.AreEqual(2, owner.Version.Revision);

        Assert.AreEqual(GameSessionFailure.InvalidSkill, owner.SetHeroSkillTraining(owner.Version, (Skill)16, SkillTraining.None).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidSkill, owner.SetHeroSkillTraining(owner.Version, Skill.Bow, (SkillTraining)4).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SetHeroSkillTraining(new(owner.Identity, 0), Skill.Bow, SkillTraining.None).Failure);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.SkillsUnavailable, heroless.SetHeroSkillTraining(heroless.Version, Skill.Bow, SkillTraining.None).Failure);

        // The training is saved with the hero's stored skills.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(SkillTraining.Apprentice, restored.QueryHeroSkills()![Skill.Firearms].Training);
    }

    [TestMethod]
    public void ThePortReadsTheHerosSkillsByTheSourcesNumbers()
    {
        using var owner = Owner();
        var port = new SessionDialogSkillPort(owner, Pc);
        Assert.AreEqual((8, 1), (port.Level(new(Pc, 2)), port.Training(new(Pc, 2))));
        Assert.AreEqual((7, 3), (port.Level(new(Pc, 11)), port.Training(new(Pc, 11))));
        Assert.AreEqual(6, port.Level(new(Pc, 13)), "12 + the tech index");
        Assert.AreEqual((0, 0, 0), (port.Level(new(Pc, 16)), port.Training(new(Pc, 41)), port.Level(new(Pc, -1))), "numbers the source does not have");
        Assert.ThrowsExactly<ArgumentException>(() => port.Level(new(new DialogParticipantId(3), 2)));

        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new SkillTrainingCommand(Pc, 13, 1)));
        Assert.AreEqual(SkillTraining.Apprentice, owner.QueryHeroSkills()![Skill.Firearms].Training);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new SkillTrainingCommand(Pc, 2, 3)), "a refused tier goes on");
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new SkillTrainingCommand(Pc, 20, 1)), "an unknown skill goes on");
        Assert.AreEqual(1, owner.Version.Revision, "only the firearms training was written");
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new SkillTrainingCommand(new DialogParticipantId(3), 13, 1)));

        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        var none = new SessionDialogSkillPort(heroless, Pc);
        Assert.ThrowsExactly<InvalidOperationException>(() => none.Level(new(Pc, 2)));
        Assert.AreEqual(DialogCommandResult.Rejected, none.Execute(new SkillTrainingCommand(Pc, 2, 1)));
    }
}
