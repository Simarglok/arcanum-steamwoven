using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog trainer seam over the session (ARC-SCRIPT-1150, ledger DTR1–DTR9): the skill names are the host's, the gold is the hero's own stack
// (0x464780), not the followers'. Over the half-elf hero (385 gold); the names are invented.
[TestClass]
public sealed class DialogTrainerPortTests
{
    private static readonly DialogParticipantId Pc = new(1);

    [TestMethod]
    public void ThePortAnswersTheHerosOwnGoldAndTheHostsSkillNames()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), null);
        using var _ = owner;
        var port = new SessionDialogTrainerPort(owner, Pc, skill => skill is >= 0 and < 16 ? $"Skill {skill}" : null);
        Assert.AreEqual(385, port.PcGold(new DialogGoldQuery(Pc)));
        Assert.AreEqual(("Skill 9", "Skill 13", (string?)null), (port.SkillName(9), port.SkillName(13), port.SkillName(16)));
        Assert.ThrowsExactly<ArgumentException>(() => port.PcGold(new DialogGoldQuery(new DialogParticipantId(2))));
        using var heroless = Attached();
        Assert.ThrowsExactly<InvalidOperationException>(() => new SessionDialogTrainerPort(heroless, Pc, _ => null).PcGold(new DialogGoldQuery(Pc)));
    }
}
