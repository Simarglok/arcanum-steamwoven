using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

// The dialog sc condition over the session hero's spell book (ARC-DEV-197, ledger SK1–SK3). Numbers are invented.
[TestClass]
public sealed class DialogCollegePortTests
{
    private static readonly DialogParticipantId Pc = new(1), Other = new(2);

    [TestMethod]
    public void The_port_reads_the_heros_rank_in_the_college_and_nothing_else()
    {
        using var owner = CreateWith(HalfElf([8, 8, 8, 8, 8, 8, 9, 8, 0, 0]));
        var port = new SessionDialogCollegePort(owner, Pc);
        Assert.AreEqual(0, port.Rank(new(Pc, 4)), "an empty book: every rank 0");
        owner.OpenHeroEditor();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendHeroPoint(owner.Version, AdvancementTrack.College, 4).Disposition);
        Assert.AreEqual(1, port.Rank(new(Pc, 4)));
        Assert.AreEqual(0, port.Rank(new(Pc, 3)), "another college");
        Assert.AreEqual(0, port.Rank(new(Pc, 15)));
        Assert.IsNull(port.Rank(new(Pc, 16)), "no seventeenth college");
        Assert.IsNull(port.Rank(new(Pc, -1)));
        Assert.ThrowsExactly<ArgumentException>(() => port.Rank(new(Other, 4)));
    }

    [TestMethod]
    public void Without_a_hero_there_is_no_rank()
    {
        using var owner = Arcanum.Session.Tests.SessionFixtures.Create();
        Assert.IsNull(new SessionDialogCollegePort(owner, Pc).Rank(new(Pc, 4)));
    }
}
