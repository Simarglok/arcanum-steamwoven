using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Reputation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-044: the AREA viewer's reaction reads the session PC's reputation sum for each speaker's origin and faction
/// (its NPC fields), with or without a hero; synthetic catalogue and rows.</summary>
[TestClass]
public sealed class AreaReputationTests
{
    private static readonly ReputationCatalog Catalog = ReputationCatalog.Create([new(1001, [6, 0, 0], [new(-15, 0, 6), new(4, 2, 0)])])!;

    [TestMethod]
    public void A_speaker_reaction_adds_the_reputation_effects_of_its_origin_and_faction()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock,
            Arcanum.Scripting.GlobalFlags.OriginalGlobalFlags.NewGame(new(new(Guid.NewGuid()), new(Guid.NewGuid())), new(Guid.NewGuid())),
            new(new(area.RuntimeMap), new(0, 0), Arcanum.Simulation.Movement.NeighborDirection.XPlus), Reputations: Catalog));
        using var session = created.Owner!;
        var state = new AreaConversationState(session, new(area.RuntimeMap), ["oid:a", "oid:b", "oid:c"], 50, scopes: [(0, 0), (2, 6), (2, 0)]);
        Assert.AreEqual((50, 50, 50), (state.Reaction(0), state.Reaction(1), state.Reaction(2)));
        Assert.AreEqual(GameSessionDisposition.Changed, session.AddReputation(session.Version, 1001).Disposition);
        Assert.AreEqual((50, 50 - 15 + 4, 50 + 4), (state.Reaction(0), state.Reaction(1), state.Reaction(2)), "the diagnostic PC reads the sum too");
        Assert.AreEqual(-11, state.PcFactsFor(1).ReputationAdjustment);
        Assert.AreEqual(0, state.PcFacts.ReputationAdjustment, "no speaker: origin 0, faction 0");
        Assert.ThrowsExactly<ArgumentException>(() => new AreaConversationState(session, new(area.RuntimeMap), ["oid:a"], 50, scopes: []));

        // The scope is the NPC's own fields 0x123 and 0x124 (slots 80 and 81); another object type has none.
        var row = area.Objects.First();
        var npc = row with { Type = 16, EffectiveFields = [.. row.EffectiveFields, Number(80, 2), Number(81, 6)] };
        Assert.AreEqual((2, 6), AreaConversationHost.Scope(npc));
        Assert.AreEqual((0, 0), AreaConversationHost.Scope(npc with { Type = 15 }));
        Assert.AreEqual((0, 0), AreaConversationHost.Scope(row with { Type = 16 }));
    }

    [TestMethod]
    public void The_catalogue_keeps_a_stopped_entrys_factions_and_refuses_an_effect_beyond_the_bounds()
    {
        static Arcanum.Data.Messages.MesFile Mes(string text) => Arcanum.Data.Messages.MesFile.Parse(System.Text.Encoding.ASCII.GetBytes(text)).File!;
        var stopped = Arcanum.Data.Reputations.ReputationTable.Create(Mes("{1000}{1 2 3, 5 0 0}\n{1001}{4 5}")).Table!;
        var catalog = AreaReputations.Catalog(stopped)!;
        CollectionAssert.AreEqual(new[] { 4, 5, 0 }, catalog.Find(1001)!.Factions.ToArray(), "the original keeps the stopped entry's numbers");
        Assert.AreEqual(1, catalog.Find(1000)!.Effects.Count);
        Assert.IsTrue(ReputationRules.GrantsFaction([1001], catalog, 4));
        Assert.IsNull(AreaReputations.Catalog(Arcanum.Data.Reputations.ReputationTable.Create(Mes("{1000}{1 2 3, 2000000 0 0}")).Table!));
    }

    private static AreaFieldValue Number(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
}
