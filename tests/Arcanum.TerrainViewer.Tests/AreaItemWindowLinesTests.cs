using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-108 (ledger OH12–OH18): the lines of the item window after the name — a weapon's speed, the second line (effects text,
/// weapon, armour, quantity, keys, usage bonus), the magick or tech line, the charges, uses or ammunition per shot, the weight and the hit
/// points (0x554F10, 0x555910, 0x555B50, 0x555780) — over invented items (no original data).</summary>
[TestClass]
public sealed class AreaItemWindowLinesTests
{
    private static readonly Func<int, string> Labels = AreaGameBarTexts.Project("test").Text;
    private static Func<int, string> Ammo => type => new[] { "Arrow", "Bullet", "Battery", "Fuel" }[type];
    private static readonly SessionItemViewer Viewer = new(40, [SkillTraining.None, SkillTraining.Expert, SkillTraining.None, SkillTraining.None], 3);

    private static CombatObjectFields Common(int type, int points = 0, uint art = 0) =>
        new(type, 1000, 0, 0, 0, 0, 0, points, 0, 0, 4, [0, 0, 0, 0, 0], art);

    private static CombatItemFields Item(int weight = 6, int complexity = 0, int charges = 0, int magicWeight = 0, uint inventoryArt = 0) =>
        new(0, weight, 0, 0, complexity, 0, 0, [10000, 10000, 10000, 10000, 10000], charges, 0, inventoryArt, 0) { MagicWeight = magicWeight };

    private static CombatWeaponFields Weapon(int low = 1, int high = 6, int range = 1, int toHit = 0, int speed = 8, int magicSpeed = 0, int ammoType = CombatFieldReader.NoAmmo,
        int consumption = 0, int magicDamage = 0, int magicHit = 0, int magicRange = 0, CombatWeaponSkill skill = CombatWeaponSkill.Melee) =>
        new(CombatWeaponFlags.None, skill, toHit, magicHit, [new(low, high), new(0, 0), new(0, 0), new(2, 3), new(1, 2)],
            [magicDamage, 0, 0, 0, 0], speed, magicSpeed, range, magicRange, 0, 0, ammoType, consumption, 0, 0, 0, 0, 0, 0);

    private static CombatFields WeaponItem(CombatWeaponFields weapon, int complexity = 0, int weight = 6, int points = 40, int charges = 0) =>
        new(CombatFieldOrigin.Prototype, Common(5, points), Item(weight, complexity, charges), weapon, null, null, null, null, null);

    private static AreaItemLines Lines(CombatFields fields, bool identified = true, string? effects = null, int? units = null, int? gold = null, int? charges = null,
        ItemCondition? condition = null, SessionItemViewer? viewer = null) =>
        AreaItemWindowLines.Build(new(fields, identified, viewer ?? Viewer) { EffectsText = effects, Units = units, GoldQuantity = gold, Charges = charges, Condition = condition },
            Labels, Ammo)!;

    [TestMethod]
    public void AWeaponShowsItsSpeedDamageLinesAndWeightLikeTheOriginal()
    {
        // The second line is 0x555910's: damage, fatigue, to hit, range, poison, fire, electrical, each as "label:" + value + two spaces.
        var lines = Lines(WeaponItem(Weapon(1, 6, range: 5, toHit: 3)));
        Assert.AreEqual("D:1-6  FT:1-2  TH:+3  RNG:5  FD:2-3  ", lines.Second);
        // 0x465CF0: the speed factor 8 (the bow's training does not count for a melee weapon); 0x5C7864's weight; 0x5C7848's hit points.
        Assert.AreEqual("Speed: 8", lines.Speed);
        Assert.AreEqual("Weight: 6 stone", lines.Weight);
        Assert.AreEqual("40/40", lines.HitPoints);
        Assert.AreEqual("", lines.Third);
        Assert.AreEqual("", lines.ThirdRight);
    }

    [TestMethod]
    public void AMeleeRangeOfOneAndEmptyEntriesAreLeftOut()
    {
        var lines = Lines(WeaponItem(Weapon(2, 4, range: 1)));
        Assert.AreEqual("D:2-4  FT:1-2  FD:2-3  ", lines.Second, "range 1 shows nothing (0x555A21); poison and electrical are empty");
    }

    [TestMethod]
    public void TheSpeedAddsTheMagicAdjustmentAsThePcGetsItAndFiveForTraining()
    {
        // Complexity 20, aptitude 40: power (20 + 40) / 2 = 30 clamped to 20, so the adjustment 4 stays whole; the bow's expert training adds 5.
        var bow = Weapon(speed: 8, magicSpeed: 4, skill: CombatWeaponSkill.Bow);
        Assert.AreEqual("Speed: 17", Lines(WeaponItem(bow, complexity: 20)).Speed);
        // A technologist's aptitude -20 gives a magickal item of 20 no power: the adjustment is 0; no training: no five.
        var melee = Weapon(speed: 8, magicSpeed: 4);
        Assert.AreEqual("Speed: 8", Lines(WeaponItem(melee, complexity: 20), viewer: Viewer with { Aptitude = -20 }).Speed);
    }

    [TestMethod]
    public void AnIdentifiedMagickalItemShowsItsBonusesInBracketsAndAnUnidentifiedOneDoesNot()
    {
        var weapon = Weapon(1, 6, range: 5, toHit: 3, magicDamage: 2, magicHit: 4, magicRange: -1);
        Assert.AreEqual("D:1-6(+2)  FT:1-2  TH:+3(+4)  RNG:5(-1)  FD:2-3  ", Lines(WeaponItem(weapon, complexity: 20), identified: true).Second);
        Assert.AreEqual("D:1-6  FT:1-2  TH:+3  RNG:5  FD:2-3  ", Lines(WeaponItem(weapon, complexity: 20), identified: false).Second);
    }

    [TestMethod]
    public void TheEffectsTextReplacesTheWeaponLineOnlyForANonMagickalOrIdentifiedItem()
    {
        var item = WeaponItem(Weapon(), complexity: 20);
        Assert.AreEqual("Damage:1-6 Fatigue:1-2", Lines(item, identified: true, effects: "Damage:1-6 Fatigue:1-2").Second);
        Assert.AreEqual("D:1-6  FT:1-2  FD:2-3  ", Lines(item, identified: false, effects: "Damage:1-6 Fatigue:1-2").Second, "unidentified: the weapon's own line");
        Assert.AreEqual("Text", Lines(WeaponItem(Weapon()), identified: true, effects: "Text").Second);
        Assert.AreEqual("D:1-6  FT:1-2  FD:2-3  ", Lines(WeaponItem(Weapon()), effects: "").Second, "an empty text counts as none (0x555119)");
    }

    [TestMethod]
    public void AnIdentifiedMagickItemShowsItsMagickPercentAndATechItemItsFailureAdjustment()
    {
        // 0x4614A0 over 0x4614A0's clamp: power (40 + 20) / 2 = 30 of 40 is 75 %.
        Assert.AreEqual("Magick power: 75%", Lines(WeaponItem(Weapon(), complexity: 40), viewer: Viewer with { Aptitude = 20 }).Third);
        // 0x461700: the aptitude 50 times the complexity −40 over 100, shown signed.
        Assert.AreEqual("Tech failure adj.: -20%", Lines(WeaponItem(Weapon(), complexity: -40), viewer: Viewer with { Aptitude = 50 }).Third);
        Assert.AreEqual("Tech failure adj.: +0%", Lines(WeaponItem(Weapon(), complexity: -40), viewer: Viewer with { Aptitude = -10 }).Third, "no positive aptitude: 0");
        Assert.AreEqual("", Lines(WeaponItem(Weapon(), complexity: 40), identified: false).Third, "an unidentified item hides it");
        Assert.AreEqual("", Lines(WeaponItem(Weapon())).Third, "a neutral item has none");
    }

    [TestMethod]
    public void TheChargesAreHiddenUntilTheItemIsIdentified()
    {
        var item = WeaponItem(Weapon(), complexity: 20, charges: 5);
        Assert.AreEqual("Uses: ??", Lines(item, identified: false).ThirdRight);
        Assert.AreEqual("Uses: 5", Lines(item).ThirdRight);
        Assert.AreEqual("Uses: 2", Lines(item, charges: 2).ThirdRight, "the session's count wins");
        Assert.AreEqual("Uses: +", Lines(item, charges: -1).ThirdRight, "0 or less is shown as +");
        Assert.AreEqual("", Lines(WeaponItem(Weapon(), complexity: 20)).ThirdRight, "no charges: nothing");
    }

    [TestMethod]
    public void ARangedWeaponShowsItsAmmunitionPerShotOnTheRight()
    {
        Assert.AreEqual("Bullet: 2", Lines(WeaponItem(Weapon(ammoType: 1, consumption: 2))).ThirdRight);
        Assert.AreEqual("", Lines(WeaponItem(Weapon(ammoType: 1, consumption: 0))).ThirdRight);
    }

    [TestMethod]
    public void ArmourShowsItsClassResistancesNoisePenaltyAndGauntletsTheirUnarmedBonus()
    {
        var armor = new CombatArmorFields(CombatArmorFlags.None, 5, 3, [7, 2, 0, 1, 0], [1, 0, 0, 0, 0], -10, 4, 6);
        // The resistance order is the original's (normal, fire, electrical, poison, magic) and the window's (normal, poison, fire, electrical, magic).
        var item = new CombatFields(CombatFieldOrigin.Prototype, Common(7, 30), Item(8), null, null, armor, null, null, null);
        var lines = Lines(item);
        Assert.AreEqual("AC:5  DR:+7  PR:+1  FR:+2  NP:-10  ", lines.Second);
        Assert.AreEqual("30/30", lines.HitPoints);
        var gauntlets = item with { Item = Item(8, inventoryArt: (6u << 28) | (3u << 14)) };
        Assert.AreEqual("AC:5  DR:+7  PR:+1  FR:+2  NP:-10  D:+6  ", Lines(gauntlets).Second);
        var magickal = item with { Item = Item(8, complexity: 20) };
        Assert.AreEqual("AC:5(+3)  DR:+7(+1)  PR:+1  FR:+2  NP:-10(+4)  ", Lines(magickal).Second);
    }

    [TestMethod]
    public void AmmunitionAndGoldShowTheirQuantityAndKeysTheKeyRingsCount()
    {
        var ammo = new CombatFields(CombatFieldOrigin.Prototype, Common(6), Item(4), null, new(0, 25, 0), null, null, null, null);
        var lines = Lines(ammo);
        Assert.AreEqual("Quantity: 25", lines.Second);
        Assert.AreEqual("Weight: 25 stone", lines.Weight, "unit weight 4 times 25 units over 4 (0x461370)");
        Assert.AreEqual("Quantity: 10", Lines(ammo, units: 10).Second, "the session's units win");
        Assert.AreEqual("", Lines(ammo).HitPoints);

        var gold = new CombatFields(CombatFieldOrigin.Prototype, Common(8), Item(9), null, null, null, null, null, null);
        Assert.AreEqual("Quantity: 120", Lines(gold, gold: 120).Second);
        Assert.AreEqual("Weight: 0 stone", Lines(gold, gold: 120).Weight, "gold weighs nothing");

        var ring = new CombatFields(CombatFieldOrigin.Prototype, Common(12), Item(1), null, null, null, null, null, null);
        Assert.AreEqual("Keys: 3", Lines(ring).Second);
        var key = new CombatFields(CombatFieldOrigin.Prototype, Common(11), Item(1), null, null, null, null, null, null);
        Assert.AreEqual("", Lines(key).Second);
    }

    [TestMethod]
    public void AGenericItemShowsItsEffectsTextOrUsageBonusUsesAndHitPoints()
    {
        CombatFields Generic(uint flags, int bonus = 0, int uses = 0, int complexity = 0, int points = 15) =>
            new(CombatFieldOrigin.Prototype, Common(14, points), Item(2, complexity), null, null, null, new CombatGenericFields(flags, bonus, uses), null, null);

        Assert.AreEqual("Bonus to Pick Locks skill: +20%", Lines(Generic(0x2, 20)).Second);
        Assert.AreEqual("Bonus to Heal skill: -5%", Lines(Generic(0x8, -5, 4)).Second);
        Assert.AreEqual("Uses: 4", Lines(Generic(0x8, 10, 4)).ThirdRight, "a heal item's uses");
        Assert.AreEqual("Some text", Lines(Generic(0x2, 20), effects: "Some text").Second);
        // A magickal, unidentified item skips the effects text but not the usage bonus (0x5551E6 jumps to 0x55523B).
        Assert.AreEqual("Bonus to Pick Locks skill: +20%", Lines(Generic(0x2, 20, complexity: 20), identified: false, effects: "Some text").Second);
        Assert.AreEqual("15/15", Lines(Generic(0x20)).HitPoints);
        Assert.AreEqual("", Lines(Generic(0)).HitPoints, "the hit-point flag 0x20 decides");
    }

    [TestMethod]
    public void TheHitPointsFollowTheSessionsConditionAndAreHiddenForABrokenItem()
    {
        var item = WeaponItem(Weapon(), points: 40);
        Assert.AreEqual("31/40", Lines(item, condition: new(40, 0, 9, false)).HitPoints);
        Assert.AreEqual("-2/40", Lines(item, condition: new(40, 0, 42, false)).HitPoints, "no clamp: the current value may be negative");
        Assert.AreEqual("", Lines(item, condition: new(40, 0, 0, true)).HitPoints, "a broken item (the art's destroyed bit, 0x5045A0) shows none");
        var brokenByArt = item with { Object = Common(5, 40, (6u << 28) | 0x400) };
        Assert.AreEqual("", Lines(brokenByArt).HitPoints);
    }

    [TestMethod]
    public void ThePcsMagickAptitudeScalesTheWeightByTheMagicAdjustment()
    {
        // 0x461370 with the PC as holder: the stored weight 6 plus the magic weight adjustment −4 times the power (20 of 40 here), 6 − 2 = 4.
        var item = new CombatFields(CombatFieldOrigin.Prototype, Common(5, 40), Item(6, 40, magicWeight: -4), Weapon(), null, null, null, null, null);
        Assert.AreEqual("Weight: 4 stone", Lines(item, viewer: Viewer with { Aptitude = 0 }).Weight);
    }

    [TestMethod]
    public void AnItemWindowIsDrawnWithTheLinesAtTheOriginalsRectanglesFontsAndColours()
    {
        var lines = new AreaItemLines("Speed: 8", "D:1-6  ", "Magick power: 75%", "Uses: 5", "Weight: 6 stone", "40/40");
        var entry = AreaHoverDescriptions.ItemWindow(new(441, "Sword", AreaMessageColour.Magick) { Lines = lines });
        var draw = AreaMessageLayout.Draw(entry);
        Assert.AreEqual(441, draw.Icon);
        var texts = draw.Texts;
        Assert.AreEqual(7, texts.Count);
        Assert.AreEqual(((290, 504, 291, 19), AreaMessageFont.Title, "Sword", false, AreaMessageColour.Magick), (texts[0].Rect, texts[0].Font, texts[0].Text, texts[0].Right, texts[0].Colour));
        // The weapon's speed: red, right-aligned on the title's rectangle.
        Assert.AreEqual(((290, 504, 291, 19), "Speed: 8", true, AreaMessageColour.Red), (texts[1].Rect, texts[1].Text, texts[1].Right, texts[1].Colour));
        // Lines two and three white and left; the third's right part right-aligned; the fourth red, the weight left and the hit points right.
        Assert.AreEqual(((290, 528, 291, 18), "D:1-6  ", false, AreaMessageColour.White), (texts[2].Rect, texts[2].Text, texts[2].Right, texts[2].Colour));
        Assert.AreEqual(((290, 546, 291, 18), "Magick power: 75%", false, AreaMessageColour.White), (texts[3].Rect, texts[3].Text, texts[3].Right, texts[3].Colour));
        Assert.AreEqual(((290, 546, 291, 18), "Uses: 5", true, AreaMessageColour.White), (texts[4].Rect, texts[4].Text, texts[4].Right, texts[4].Colour));
        Assert.AreEqual(((290, 564, 291, 18), "Weight: 6 stone", false, AreaMessageColour.Red), (texts[5].Rect, texts[5].Text, texts[5].Right, texts[5].Colour));
        Assert.AreEqual(((290, 564, 291, 18), "40/40", true, AreaMessageColour.Red), (texts[6].Rect, texts[6].Text, texts[6].Right, texts[6].Colour));
    }

    [TestMethod]
    public void AnItemWindowWithoutLinesDrawsOnlyTheNameAndASettingOfEmptyLinesDrawsNothingMore()
    {
        var name = AreaMessageLayout.Draw(AreaHoverDescriptions.ItemWindow(new(441, "Sword", AreaMessageColour.White)));
        Assert.AreEqual(1, name.Texts.Count);
        var empty = AreaMessageLayout.Draw(AreaHoverDescriptions.ItemWindow(new(441, "Sword", AreaMessageColour.White) { Lines = AreaItemLines.Empty }));
        Assert.AreEqual(1, empty.Texts.Count);
    }

    [TestMethod]
    public void ATypeThatIsNotAnItemHasNoLines()
    {
        var scenery = new CombatFields(CombatFieldOrigin.Prototype, Common(3), null, null, null, null, null, null, null);
        Assert.IsNull(AreaItemWindowLines.Build(new(scenery, true, Viewer), Labels, Ammo));
    }
}
