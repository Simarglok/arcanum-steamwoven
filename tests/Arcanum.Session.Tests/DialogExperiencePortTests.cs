using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog XP action over the session (ARC-DEV-178, ledger DAC9, XP11). The experience tables are the invented ones of ExperienceStateTests
// (quest experience level 1 gives 700, level 5 gives 1300; any other level has no entry).
[TestClass]
public sealed class DialogExperiencePortTests
{
    private static readonly DialogParticipantId Pc = new(1);

    private static GameSessionOwner Owner(bool rules = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = rules ? ExperienceStateTests.Rules() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void XpGivesTheExperienceOfTheQuestLevelAndPassesTheAwardOn()
    {
        using var owner = Owner();
        var awards = new List<SessionExperienceAward>();
        var port = new SessionDialogExperiencePort(owner, Pc, awards.Add);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new QuestExperienceCommand(Pc, 1)));
        Assert.AreEqual(700, owner.QueryHeroExperience()!.Experience);
        Assert.AreEqual(700, awards.Single().Award);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new QuestExperienceCommand(Pc, 5)));
        Assert.AreEqual(2000, owner.QueryHeroExperience()!.Experience);
        Assert.AreEqual(2, awards.Count);
    }

    [TestMethod]
    public void AnUnknownLevelGivesNothingAndANegativeOneToo()
    {
        using var owner = Owner();
        var awards = new List<SessionExperienceAward>();
        var port = new SessionDialogExperiencePort(owner, Pc, awards.Add);
        var before = owner.Version;
        foreach (var level in new[] { 0, 77, -3, int.MaxValue })
            Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new QuestExperienceCommand(Pc, level)), level.ToString());
        Assert.AreEqual(0, owner.QueryHeroExperience()!.Experience);
        Assert.AreEqual(0, awards.Count);
        Assert.AreEqual(before, owner.Version, "a level without an entry writes nothing");
    }

    [TestMethod]
    public void AnotherPcAHerolessSessionAndMissingTablesAreRejected()
    {
        using var owner = Owner();
        Assert.AreEqual(DialogCommandResult.Rejected, new SessionDialogExperiencePort(owner, Pc).Execute(new QuestExperienceCommand(new DialogParticipantId(2), 1)));
        Assert.AreEqual(0, owner.QueryHeroExperience()!.Experience);
        using var noTables = Owner(rules: false);
        Assert.AreEqual(DialogCommandResult.Rejected, new SessionDialogExperiencePort(noTables, Pc).Execute(new QuestExperienceCommand(Pc, 1)));
        using var heroless = GameSessionOwner.Create(Descriptor() with { Experience = ExperienceStateTests.Rules() }).Owner!;
        Assert.AreEqual(DialogCommandResult.Rejected, new SessionDialogExperiencePort(heroless, Pc).Execute(new QuestExperienceCommand(Pc, 1)));
    }
}
