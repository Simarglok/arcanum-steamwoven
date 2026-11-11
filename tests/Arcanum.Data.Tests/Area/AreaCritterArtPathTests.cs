using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DEV-370 (E1-c2): the file a critter, monster or unique NPC art id names for a critter no map row carries (invented name tables).</summary>
[TestClass]
public sealed class AreaCritterArtPathTests
{
    private static string? Name(AreaMappingRole role, int key) => (role, key) switch
    {
        (AreaMappingRole.MonsterNames, 5) => "lz1",
        (AreaMappingRole.UniqueNpcNames, 3) => "dgn",
        (AreaMappingRole.MonsterNames, 6) => "bad/name",
        _ => null,
    };

    [TestMethod]
    public void ACritterArtIsNamedByBodyGenderArmorShieldWeaponAndAnimation()
    {
        // Human female, no armour, stand (animation 0), weapon 0: the file the viewer's T0 loader names for a map NPC of the same art id.
        Assert.AreEqual("art/critter/HMF/HMFUWXAa.art", AreaDataOwner.CritterArtPath(0x20002000u, Name));
        // Dwarf male (body 1, gender 1), armour 1, shield, animation 1 (walk), weapon 3: the shield and weapon the rules keep.
        var art = (2u << 28) | (1u << 27) | (1u << 24) | (1u << 20) | (1u << 19) | (1u << 6) | 3u;
        Assert.AreEqual("art/critter/DFM/DFMV1XDb.art", AreaDataOwner.CritterArtPath(art, Name), "animation 1 clears the shield");
        Assert.AreEqual("art/critter/DFM/DFMV1SDa.art", AreaDataOwner.CritterArtPath(art & ~(31u << 6), Name), "a stand keeps the shield");
    }

    [TestMethod]
    public void AMonsterAndAUniqueNpcTakeTheirNamesFromTheTables()
    {
        Assert.AreEqual("art/monster/lz1/lz1UWXAa.art", AreaDataOwner.CritterArtPath((12u << 28) | (5u << 23), Name));
        Assert.AreEqual("art/unique_npc/dgn/dgnXAa.art", AreaDataOwner.CritterArtPath((13u << 28) | (3u << 20), Name));
    }

    [TestMethod]
    public void ANumberWithNoRowAnUnsafeNameAndAnotherClassAreRefused()
    {
        Assert.ThrowsExactly<AreaException>(() => AreaDataOwner.CritterArtPath((12u << 28) | (7u << 23), Name), "no row for monster 7");
        Assert.ThrowsExactly<AreaException>(() => AreaDataOwner.CritterArtPath((12u << 28) | (6u << 23), Name), "a name with a path character");
        Assert.ThrowsExactly<AreaException>(() => AreaDataOwner.CritterArtPath(0x40000000u, Name), "a scenery art id is no critter");
        Assert.ThrowsExactly<AreaException>(() => AreaDataOwner.CritterArtPath(0x20000000u | (25u << 6), Name), "animation 25 does not exist");
    }

    [TestMethod]
    public void TheMonsterAndUniqueNpcTablesHaveTheirPaths()
    {
        Assert.AreEqual("art/monster/monster.mes", AreaObjectArt.MappingPath(AreaMappingRole.MonsterNames));
        Assert.AreEqual("art/unique_npc/unique_npc.mes", AreaObjectArt.MappingPath(AreaMappingRole.UniqueNpcNames));
    }
}
