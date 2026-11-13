using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-1475 (FR-MAGIC-007): the combat host reads an NPC with the base stats and resistances a TraitIdx spell wrote into it (<see cref="SessionCritterIndexedWrites"/>)
/// laid over the stored fields: by identity, in the session-less build, in the doll ratings (magic resistance) and in the session build. Invented rows, no original data.</summary>
[TestClass]
public sealed class AreaCombatHostIndexedWritesTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A human NPC (type 16) with stats 8 (race 0 human, gender 1, level 1), stored resistances normal 10, fire 5, electrical 0, poison 0, magic 20, and 20 hit points.
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30, 10, 5, 0, 0, 20), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 8, 8, 8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47), Field(57, 5), Field(63, 0),
            Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private static (AreaCombatHost Host, AreaInventoryHost Inventory) Host(GameSessionOwner? session)
    {
        var map = new AreaInventoryMap(1, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [])], []), new[] { Npc(0) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        return (new AreaCombatHost(inventory, null, session is null ? null : () => session), inventory);
    }

    private static void Write(GameSessionOwner session, string entity, bool resistance, int index, int value) =>
        Assert.AreEqual(GameSessionDisposition.Changed, (resistance ? session.WriteCritterResistance(session.Version, session.QueryActor().Map, entity, index, value)
            : session.WriteCritterStat(session.Version, session.QueryActor().Map, entity, index, value)).Disposition);

    [TestMethod]
    public void The_stored_fields_by_identity_and_the_session_less_build_see_what_a_spell_wrote()
    {
        using var session = ConversationSessionFixture.CreateWithHero();
        var (host, inventory) = Host(session);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var guard = Entity(0);
        Assert.AreEqual(SessionCritterCombatFailure.None, host.Build(1, guard).Failure);
        Assert.AreEqual((8, 5), (host.Fields(guard)!.Critter!.BaseStats[0], host.Fields(guard)!.Object.Resistances[1]));
        Assert.AreEqual((8, 5), (host.Critter(1, guard)!.Facts.Strength, host.Critter(1, guard)!.SheetResistances(8, 20).Damage.Fire), "built and cached before the write");
        Write(session, guard, false, 0, 13);
        Write(session, guard, true, 1, 50);
        var fields = host.Fields(guard)!;
        Assert.AreEqual((13, 50), (fields.Critter!.BaseStats[0], fields.Object.Resistances[1]), "the written strength and fire resistance over the stored ones");
        Assert.AreEqual((13, 50), (host.Critter(1, guard)!.Facts.Strength, host.Critter(1, guard)!.SheetResistances(8, 20).Damage.Fire), "the cached build follows a new write");
        Assert.AreEqual((13, 50), (host.Critter(guard)!.Facts.Strength, host.Critter(guard)!.SheetResistances(8, 20).Damage.Fire), "by identity");
        Write(session, guard, false, 0, 14);
        Assert.AreEqual(14, host.Critter(1, guard)!.Facts.Strength, "a later write rebuilds again");
    }

    [TestMethod]
    public void A_host_without_a_session_reads_the_stored_fields_and_the_dolls_magic_resistance_follows_a_written_one()
    {
        using var session = ConversationSessionFixture.CreateWithHero();
        var guard = Entity(0);
        // No session accessor: the stored fields, even where the session wrote.
        var (plain, _) = Host(null);
        Write(session, guard, true, 1, 50);
        Assert.AreEqual(5, plain.Fields(guard)!.Object.Resistances[1]);
        Assert.AreEqual(5, plain.Critter(1, guard)!.SheetResistances(8, 20).Damage.Fire);

        var (host, inventory) = Host(session);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var before = host.DollRatings(session, guard);
        Assert.IsNotNull(before);
        Write(session, guard, true, 4, 60);
        var after = host.DollRatings(session, guard)!;
        Assert.IsTrue(after.Armour > before.Armour, $"the written magic resistance 60 over the stored 20 rates {after.Armour} over {before.Armour}");
        Assert.AreEqual(before.Damage, after.Damage);
    }
}
