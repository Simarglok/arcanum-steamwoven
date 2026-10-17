using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-027 viewer start as a pre-made hero over invented values (no original data): the hero list, the
/// <c>--hero</c> argument, the new-game flags with the hero's flag, the session, the inventory window and the status line.</summary>
[TestClass]
public sealed class AreaStartingHeroTests
{
    // Invented description numbers and text.
    private static readonly ObjectDescriptionCatalog Catalog = ObjectDescriptionCatalog.Create(
        MesFile.Parse(Encoding.Latin1.GetBytes("{101}{Synthetic Knife}{102}{Synthetic Coat}")).File!);

    private static SessionHeroDefinition Definition(int gold = 385) => new(16089, CharacterRace.HalfElf, CharacterGender.Male,
        [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, new(67, [new(CharacterStat.Dexterity, StatModifierOperator.Add, 1)]), null, gold,
        [new(new(SessionItemType.Weapon, 6049, 4021, 101)), new(new(SessionItemType.Armor, 8188, 4022, 102)), new(new(SessionItemType.Armor, 8069, 4023, 999))]);

    private static AreaHeroLoad Ready(int flag = 2352) =>
        new(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, Definition(), flag, 5, null, "synthetic");

    private static GameSessionOwner Start(AreaHeroLoad? hero)
    {
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    private static bool? Flag(GameSessionOwner session, int flag)
    {
        var read = session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.Flags.Version, new FlagId(flag)));
        return read.Status == GlobalFlagReadStatus.Known ? read.Value : null;
    }

    [TestMethod]
    public void The_start_wields_the_kits_best_items_and_a_refusal_fails_it()
    {
        // ARC-WORLD-034: kit:1 a knife, kit:2 a medium coat, kit:3 armour of another size (invented values).
        var knife = new Arcanum.Simulation.Equipment.EquipmentItem
        {
            Kind = Arcanum.Simulation.Equipment.EquipmentItemKind.Weapon,
            Weapon = new Arcanum.Simulation.Equipment.EquipmentWeapon { Damage = Arcanum.Simulation.Combat.DamageRanges.None with { Normal = new(1, 4) } },
        };
        Arcanum.Simulation.Equipment.EquipmentItem Coat(Arcanum.Simulation.Equipment.ArmorSizes sizes) => new()
        {
            Kind = Arcanum.Simulation.Equipment.EquipmentItemKind.Armor,
            Armor = new Arcanum.Simulation.Equipment.EquipmentArmor { Coverage = Arcanum.Simulation.Equipment.ArmorCoverage.Torso, Sizes = sizes, AcAdjustment = 4 },
        };
        var kit = Definition().Kit;
        SessionWearableItem?[] wear =
        [
            new(kit[0].Kind, [], false, knife), new(kit[1].Kind, [], false, Coat(Arcanum.Simulation.Equipment.ArmorSizes.Medium)) { Worth = 40 },
            new(kit[2].Kind, [], false, Coat(Arcanum.Simulation.Equipment.ArmorSizes.Small)) { Worth = 90 },
        ];
        using var session = Start(Ready() with { KitWear = wear });
        CollectionAssert.AreEqual(new[] { ("kit:1", Arcanum.Simulation.Equipment.EquipmentSlot.Weapon), ("kit:2", Arcanum.Simulation.Equipment.EquipmentSlot.Armor) },
            session.QueryEquipment().Worn.Select(worn => (worn.Entity, worn.Slot)).ToArray(), "the small coat is worth more but does not fit a half-elf");
        Assert.AreEqual(1, session.Version.Revision, "one revision for the start's wield");
        Assert.AreEqual("hero: half-elf-male level 1 (5 points unspent), gold 385, kit 3, worn 2", AreaStartingHero.Summary(session));

        wear[0] = wear[0]! with { HasItemSpells = true };
        var refused = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, Ready() with { KitWear = wear });
        Assert.AreEqual((null, GameSessionFailure.ItemEffectUnsupported), (refused.Owner, refused.Failure));
    }

    [TestMethod]
    public void The_twelve_heroes_are_the_main_menu_prototypes_in_order_with_unique_names()
    {
        CollectionAssert.AreEqual(Enumerable.Range(16067, 12).ToArray(), AreaStartingHero.Premade.Select(hero => hero.BasicPrototype).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 12).ToArray(), AreaStartingHero.Premade.Select(hero => hero.Index).ToArray());
        Assert.AreEqual(12, AreaStartingHero.Premade.Select(hero => hero.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(AreaStartingHero.Premade.All(hero => hero.Name.All(c => c is >= 'a' and <= 'z' or '-')));
        Assert.AreEqual(16075, AreaStartingHero.Find("human-female")!.BasicPrototype);
        Assert.IsNull(AreaStartingHero.Find("Human-Female"), "names are exact");
        Assert.IsNull(AreaStartingHero.Find("16075"));
        Assert.IsNull(AreaStartingHero.Find(""));
    }

    [TestMethod]
    public void Hero_argument_is_an_area_option_naming_one_pre_made_hero()
    {
        string[] Area(params string[] extra) => ["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--source-id", "s", "--prototypes", "p",
            "--actor-resource", "a", "--area-mobile", "mo", "--area-effects", "e", "--area-patch", "pa", .. extra];
        Assert.IsNull(TerrainViewerArguments.Parse(Area()).Hero);
        Assert.AreEqual(16069, TerrainViewerArguments.Parse(Area("--hero", "half-elf-male")).Hero!.BasicPrototype);
        Assert.AreEqual(16078, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--source-id", "s",
            "--actor-resource", "a", "--game-root", "g", "--area-map", "some map", "--hero", "half-elf-female"]).Hero!.BasicPrototype, "map mode");
        foreach (var bad in new[] { "16069", "Half-Elf-Male", "", "ogre-male" })
            Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--hero", bad)), bad);
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--hero", "elf-male", "--hero", "elf-male")), "once");
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "world-diagnostic", "--module", "m", "--resource", "r",
            "--source-id", "s", "--prototypes", "p", "--actor-resource", "a", "--hero", "elf-male"]), "AREA only");
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--game-root", "g", "--hero", "elf-male")),
            "the hero's game folder is the module archive's, as for names");
    }

    [TestMethod]
    public void New_game_flags_set_only_the_heros_flag()
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var epoch = new OwnerEpoch(Guid.NewGuid());
        var plain = OriginalGlobalFlags.NewGame(identity, epoch);
        Assert.IsFalse(AreaStartingHero.NewGameFlags(identity, epoch, null).Bootstrap.Any(value => value.Value), "no hero: the plain new game");
        var flags = AreaStartingHero.NewGameFlags(identity, epoch, 2355);
        CollectionAssert.AreEqual(plain.Domain.ToArray(), flags.Domain.ToArray());
        Assert.AreEqual(2355, flags.Bootstrap.Single(value => value.Value).Flag.Value);
        Assert.AreEqual(OriginalGlobalFlags.Count, flags.Bootstrap.Count);
        Assert.IsTrue(AreaStartingHero.NewGameFlags(identity, epoch, 0).Bootstrap.Single(value => value.Value).Flag.Value == 0,
            "flag 0 is written by the original (CE's > 0 is a reconstruction slip)");
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaStartingHero.NewGameFlags(identity, epoch, OriginalGlobalFlags.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaStartingHero.NewGameFlags(identity, epoch, -1));
    }

    [TestMethod]
    public void A_ready_hero_starts_the_session_with_its_flag_gold_and_kit()
    {
        using var session = Start(Ready());
        Assert.AreEqual(16089, session.QueryHero()!.Prototype);
        Assert.AreEqual(385, session.QueryGold());
        Assert.AreEqual(3, session.QueryHeroKit().Count);
        Assert.AreEqual(true, Flag(session, 2352));
        Assert.AreEqual(false, Flag(session, 2350), "the other heroes' flags stay false");
        Assert.AreEqual(new Arcanum.Scripting.Dialog.DialogPcFacts(Arcanum.Scripting.Dialog.DialogPcGender.Male, 8, false, 3), session.QueryHeroDialogFacts());
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        Assert.AreEqual((16089, 385, true), (loaded.QueryHero()!.Prototype, loaded.QueryGold(), Flag(loaded, 2352)), "a quick save keeps hero and flag");

        using var plain = Start(null);
        Assert.IsNull(plain.QueryHero());
        Assert.AreEqual(false, Flag(plain, 2352));
        foreach (var refused in new[] { Ready() with { Status = AreaHeroStatus.HeroRefused }, Ready() with { Definition = null }, Ready() with { Flag = null } })
            Assert.Throws<ArgumentException>(() => Start(refused), refused.Status.ToString());
    }

    [TestMethod]
    public void The_window_lists_the_kit_first_with_gold_and_the_status_line_names_the_hero()
    {
        using var session = Start(Ready());
        var host = new AreaInventoryHost([]);
        var view = host.View(session, Catalog, 1);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, view.Pc.Select(line => line.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "Synthetic Knife (weapon) (starting kit)", "Synthetic Coat (armor) (starting kit)", "[armor: unnamed] (armor) (starting kit)" },
            view.Pc.Select(line => line.Text).ToArray());
        Assert.AreEqual(385, view.Gold);
        Assert.AreEqual("[weapon: name unavailable]", AreaInventoryHost.KitName(session.QueryHeroKit()[0], null));
        Assert.AreEqual("hero: half-elf-male level 1 (5 points unspent), gold 385, kit 3, worn 0", AreaStartingHero.Summary(session));

        // ARC-WORLD-028 session equipment: a wielded kit item is marked worn.
        var blade = new Arcanum.Simulation.Equipment.EquipmentItem
        {
            Kind = Arcanum.Simulation.Equipment.EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new Arcanum.Simulation.Equipment.EquipmentWeapon
            {
                Skill = Arcanum.Simulation.Combat.CombatSkill.Melee, Damage = Arcanum.Simulation.Combat.DamageRanges.None with { Normal = new(2, 8) },
                SpeedFactor = 15, MinimumStrength = 6,
            },
        };
        var wielded = session.Equip(session.Version, "kit:1", Arcanum.Simulation.Equipment.EquipmentSlot.Weapon,
            new(session.QueryHeroKit()[0].Kind, [], false, blade), (_, _) => true, _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionFailure.None, wielded.Failure);
        CollectionAssert.AreEqual(new[] { "Synthetic Knife (weapon) (starting kit) (worn)", "Synthetic Coat (armor) (starting kit)" },
            host.View(session, Catalog, 1).Pc.Take(2).Select(line => line.Text).ToArray());

        using var plain = Start(null);
        Assert.AreEqual(0, host.View(plain, Catalog, 1).Pc.Count);
        Assert.AreEqual(0, host.View(plain, Catalog, 1).Gold);
        Assert.AreEqual("hero: none (diagnostic PC profile)", AreaStartingHero.Summary(plain));
    }
}
