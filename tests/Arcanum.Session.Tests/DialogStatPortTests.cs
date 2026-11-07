using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog PC stat conditions over the session (ARC-DEV-192, ledger DCC2, DCC3): the hero's stats by the original's numbers. Numbers are invented.
[TestClass]
public sealed class DialogStatPortTests
{
    private static readonly DialogParticipantId Pc = new(1), Other = new(2);

    [TestMethod]
    public void The_port_gives_the_heros_stats_by_the_originals_numbers_and_nothing_without_a_hero()
    {
        using var owner = CreateWith(HalfElf() with { Level = 7, Alignment = -120 });
        var port = new SessionDialogStatPort(owner, Pc);
        Assert.AreEqual(7, port.Stat(new(Pc, GameSessionOwner.ScriptStatLevel)));
        Assert.AreEqual(-120, port.Stat(new(Pc, GameSessionOwner.ScriptStatAlignment)));
        Assert.AreEqual(owner.QueryHeroScriptStat(7), port.Stat(new(Pc, 7)), "charisma");
        Assert.IsNull(port.Stat(new(Pc, 8)), "a stat the session does not hold");
        Assert.ThrowsExactly<ArgumentException>(() => port.Stat(new(Other, 7)));
        using var none = Create();
        Assert.IsNull(new SessionDialogStatPort(none, Pc).Stat(new(Pc, 7)), "no hero");
    }
}
