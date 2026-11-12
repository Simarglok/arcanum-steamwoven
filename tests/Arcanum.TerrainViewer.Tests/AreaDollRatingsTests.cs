using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1121 (B30, IW25, IW29, IW41–IW43): the combat host reads the loot window's doll numbers for a body (invented values, no original data): a guard (0) wearing a
/// body armour (1) at 1006 with normal resistance 20.</summary>
[TestClass]
public sealed class AreaDollRatingsTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A body armour (type 7, torso coverage in its inventory art, every size) worn at 1006 with normal resistance 20 and armour class 10.
    private static AreaObjectRecord Armour(int index) => new(Key(index), 0, 7, 8114, "p", "p", "r", "s", AreaScope.Core, null, null, null,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, (long)(CombatItemArt.ItemArtType << 28)), Field(43, 1006),
            Field(44, 0x60000000), Field(45, 0), Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000),
            Field(54, 0), Field(55, 0),
            Field(59, 0x7), Field(61, 0), Field(62, 10), Array(63, 20, 0, 0, 0, 0), Array(64), Field(65, 0), Field(66, 0), Field(67, 0),
        ], true, true, true);

    // A human NPC (type 16, no monster flags) with stats 8, level 1, no skills, 20 hit points.
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 8, 8, 8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47), Field(57, 5), Field(63, 0),
            Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private static (AreaCombatHost Host, AreaInventoryHost Inventory) Host()
    {
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)])],
                [new SessionSeedItem(Entity(1), new(new(SessionItemType.Armor, 8114, 6002, 6003), 1, 0, 1006, null, []), null)]),
            new[] { Npc(0), Armour(1) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        return (new AreaCombatHost(inventory), inventory);
    }

    [TestMethod]
    public void TheArmourRatingFollowsWhatTheBodyWearsAndNothingIsReadForAnObjectThatIsNoNpc()
    {
        using var session = ConversationSessionFixture.Create();
        var (host, inventory) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var guard = Entity(0);
        var dressed = host.DollRatings(session, guard);
        Assert.IsNotNull(dressed);
        Assert.IsFalse(dressed.WeaponTooHeavy, "no weapon in the slot");
        Assert.IsTrue(dressed.Armour > 0, $"armour class and a normal resistance of 20 rate {dressed.Armour}");

        // The armour breaks and comes off: the rating falls, the damage rating stays (bare hands either way).
        var wear = host.WornItems(session, 1, guard)!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(1), wear[EquipmentSlot.Armor].Baseline).Disposition);
        var bare = host.DollRatings(session, guard)!;
        Assert.IsTrue(bare.Armour < dressed.Armour, $"{bare.Armour} under {dressed.Armour}");
        Assert.AreEqual(dressed.Damage, bare.Damage);

        // An armour row is no NPC; an entity the map does not hold neither.
        Assert.IsNull(host.DollRatings(session, Entity(1)));
        Assert.IsNull(host.DollRatings(session, "oid:unknown"));
    }

    [TestMethod]
    public void TheHeroHostReadsTheHeroValuesAndNothingWithoutAHero()
    {
        // ARC-UI-1267 (IW25): the combat host reads the hero's own rating numbers from the session's hero (the session's tests cover the values, the armour identification and the
        // weapon overlay); a session without a hero has none.
        using var plain = ConversationSessionFixture.Create();
        Assert.IsNull(Host().Host.HeroDollRatings(plain));
        using var session = ConversationSessionFixture.CreateWithHero();
        var (host, _) = Host();
        var rated = host.HeroDollRatings(session);
        Assert.IsNotNull(rated);
        var direct = session.QueryHeroDollRatings(_ => false)!;
        Assert.AreEqual(direct.Damage, rated.Damage, "bare hands, nothing worn: the same damage rating whichever way identification is asked");
        Assert.IsFalse(rated.WeaponTooHeavy);
    }

    [TestMethod]
    public void TheOwnWindowsRatingBoxesAreTheOriginalsTables()
    {
        // IW25: 0x5CAEF8 and 0x5CAF08, 23 × 35 each; the damage rating's box lies left of the doll's boots, the armour rating's right of them (both under the slot rectangles).
        Assert.AreEqual((80, 358, 23, 35), AreaInventoryArtLayout.DamageRatingBox);
        Assert.AreEqual((261, 358, 23, 35), AreaInventoryArtLayout.ArmourRatingBox);
    }
}
