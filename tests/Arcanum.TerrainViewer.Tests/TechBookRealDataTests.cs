using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-366 (ledger XA9, XA8) on the installed original (opt-in, <c>DAT_ROOT</c>): the eight tech books' real insert script 30017 and remove
/// script 30018 and the real effects 168 to 175 (<c>rules/effect.mes</c>), run through the viewer's inventory host. The books are synthetic ground
/// items that carry the scripts and counters the installed prototypes name (counter 1 the effect, counter 2 the divisor 1, counter 3 the stat 4);
/// the hero is invented. Numbers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class TechBookRealDataTests
{
    private const int FirstEffect = 168, Books = 8, Insert = 30017, Remove = 30018;
    private const int Raise = 60;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // A ground book: the insert-item and remove-item attachments with their counters (words: local flags, counters, script).
    private static AreaObjectRecord Row(int index, int effect)
    {
        var counters = (long)effect << 8 | 1L << 16 | 4L << 24;
        return new(Key(index), 0, 7, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
                [new AreaSparseElement(SessionScriptPoint.InsertItem, [0, counters, Insert]), new AreaSparseElement(SessionScriptPoint.RemoveItem, [0, counters, Remove])], null)],
            true, true, true);
    }

    private static SessionItemFacts Facts(int index) => new(new(SessionItemType.Generic, 15208 + index, 5700 + index, 100 + index), 5, 0, 0, null,
        [SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem]);

    private static int Expertise(GameSessionOwner session, int discipline) => session.QueryHeroTechnology()!.Value[discipline].Expertise;
    private static int Entries(GameSessionOwner session, int effect) => session.QueryHeroEffects()!.Effects.Count(entry => entry.Number == effect);

    [TestMethod, TestCategory("RealData")]
    public void The_installed_tech_books_give_expertise_equal_to_the_intelligence_while_carried()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { Assert.Inconclusive("DAT_ROOT is required for the opt-in tech book check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var effects = AreaStartingHero.Table(stack, AreaStartingHero.EffectsPath, ContentLoadPhase.GameStart, true, out var detail);
        Assert.IsNotNull(effects, detail);
        EffectRule? Rules(int number) => EffectRules.Read(effects, number).Rule;
        // XA8: the eight effects each name one discipline's expertise, +1, in the discipline order.
        for (var discipline = 0; discipline < Books; discipline++)
            CollectionAssert.AreEqual(new[] { new EffectChange(EffectAttributeKind.TechDiscipline, discipline, EffectChangeOperator.Add, 1) }, Rules(FirstEffect + discipline)!.Changes.ToArray(),
                $"effect {FirstEffect + discipline}");

        using var session = ConversationSessionFixture.CreateWithHero();
        // The intelligence is 20 (8 and a +12 effect), so the eight books together need 160 entries, past the 64 the list held before.
        Assert.AreEqual(GameSessionDisposition.Changed, session.AddHeroEffect(session.Version, new SessionEffectDefinition(Raise, [new(EffectAttributeKind.Stat, 4, EffectChangeOperator.Add, 12)]),
            EffectCause.Spell).Disposition);
        var intelligence = session.QueryHeroScriptStat(4)!.Value;
        Assert.AreEqual(20, intelligence);
        var baseline = Enumerable.Range(0, Books).Select(discipline => Expertise(session, discipline)).ToArray();

        var map = new AreaInventoryMap(1, new([], [.. Enumerable.Range(0, Books).Select(i => new SessionSeedItem(Entity(i), Facts(i), new CellCoord(10 + i, 20)))]),
            Enumerable.Range(0, Books).ToDictionary(Entity, i => Row(i, FirstEffect + i)));
        var host = new AreaInventoryHost([map], scripts: content) { EffectRules = Rules };
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        for (var book = 0; book < Books; book++)
        {
            var picked = host.PickUp(session, Entity(book));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (picked.Disposition, picked.Failure), $"book {book}");
            Assert.AreEqual((intelligence, baseline[book] + intelligence), (Entries(session, FirstEffect + book), Expertise(session, book)), $"book {book}");
        }
        Assert.AreEqual(Books * intelligence, session.QueryHeroEffects()!.Effects.Count(entry => entry.Number >= FirstEffect), "160 entries in all");
        Console.WriteLine($"tech books: {Books} books, intelligence {intelligence}, {Books * intelligence} effect entries, expertise +{intelligence} in every discipline");

        // The intelligence falls back to 8: the books still hold the 20 their insertion counted. A move in the grid runs the real remove script (30018,
        // call-ex into 30017's remove part, which takes away the 20 its counter 0 kept) and the insert script again (which adds the 8 it reads now).
        Assert.AreEqual(GameSessionDisposition.Changed, session.RemoveHeroEffect(session.Version, Raise).Disposition);
        Assert.AreEqual(8, session.QueryHeroScriptStat(4));
        var held = session.QueryPcInventory().Single(item => item.Entity == Entity(2));
        var moved = session.MoveHeroItem(session.Version, Entity(2), 77, new SessionGridMoveItem(held.Kind, false, [SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem]),
            host.InsertCheck, host.ItemScripts(session));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, Entity(2)), (SessionScriptPoint.InsertItem, Entity(2)) }, moved.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual((8, baseline[2] + 8), (Entries(session, FirstEffect + 2), Expertise(session, 2)), "the moved book counts the intelligence now");
        Assert.AreEqual((intelligence, baseline[5] + intelligence), (Entries(session, FirstEffect + 5), Expertise(session, 5)), "the others still count the 20");

        // The list and the counters survive a save.
        using var restored = ConversationSessionFixture.SaveAndLoad(session);
        Assert.AreEqual(session.QueryHeroEffects()!.Effects.Count, restored.QueryHeroEffects()!.Effects.Count);
        Assert.AreEqual(Expertise(session, 5), Expertise(restored, 5));
    }
}
