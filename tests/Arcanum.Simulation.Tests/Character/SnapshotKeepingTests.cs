using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>ARC-RENDER-3362: a snapshot is kept while the revision and the context stand, and never past a change.</summary>
[TestClass]
public sealed class SnapshotKeepingTests
{
    [TestMethod]
    public void OneContextGivesOneSnapshotUntilAChange()
    {
        var owner = Owner();
        var first = Snap(owner);
        Assert.AreSame(first, Snap(owner));
        Changed(owner.SetBase(CharacterStat.Perception, 12));
        var raised = Snap(owner);
        Assert.AreNotSame(first, raised);
        Assert.AreEqual(12, raised.GetEffective(CharacterStat.Perception));
        var added = owner.AddEffect(Effect(7, Add(CharacterStat.Perception, 3)), EffectCause.Spell);
        Changed(added);
        Assert.AreEqual(15, Snap(owner).GetEffective(CharacterStat.Perception));
        Changed(owner.RemoveEffect(added.AddedEffect!.Value));
        Assert.AreEqual(12, Snap(owner).GetEffective(CharacterStat.Perception));
        var kept = Snap(owner);
        Assert.AreEqual(CharacterStatsDisposition.NoChange, owner.SetBase(CharacterStat.Perception, 12).Disposition);
        Assert.AreSame(kept, Snap(owner), "a change that changes nothing keeps the snapshot");
    }

    [TestMethod]
    public void EveryContextGetsItsOwnValuesHoweverManyAreAsked()
    {
        var owner = Owner();
        var plain = Snap(owner);
        // More contexts than are kept, then the first again: each answer is its context's.
        for (var round = 0; round < 3; round++)
            for (var poison = 0; poison <= 6; poison++)
            {
                var context = new CharacterStatContext(0, poison * 20);
                var snapshot = owner.Query(context).Snapshot!;
                Assert.AreEqual(context, snapshot.Context);
                var fresh = Owner().Query(context).Snapshot!;
                foreach (var stat in Primary) Assert.AreEqual(fresh.GetEffective(stat), snapshot.GetEffective(stat), $"{stat} at poison {poison * 20}");
            }
        Assert.AreEqual(plain.GetEffective(CharacterStat.Dexterity), Snap(owner).GetEffective(CharacterStat.Dexterity));
    }
}
