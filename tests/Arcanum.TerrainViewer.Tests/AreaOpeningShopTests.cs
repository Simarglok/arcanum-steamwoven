using System.Buffers.Binary;
using Arcanum.Data.Characters;
using Arcanum.Data.StaticObjects;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaNewCharacterTests;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-036 (N10b slice 4, FR-CHAR-007): the opening shop (window 15, ledger OS1–OS6) over the new-character fixture: the kit
/// made from the editor's draft (background money and items, clothes or jacket, lockpicks, bandages), the start with it (the best weapon
/// and body armour worn), the menu's window and its Back to the editor with every point still free (owner decision 2026-10-28), and the
/// kit's tables rebuilt from a session's kit. Synthetic prototypes only.</summary>
[TestClass]
public sealed class AreaOpeningShopTests
{
    // Basic numbers: 6049 a weapon, 8133 the medium women's suit, 8110 the medium jacket, 15178 lockpicks, 15179 bandages (each file + 20).
    internal static Func<int, PrototypeFile> KitFiles(int suitSizes = (int)ArmorSizes.Medium)
    {
        var files = new Dictionary<int, PrototypeFile>
        {
            [6069] = new(PrototypeFileStatus.Found, Prototype(6069, 5, new() { [21] = 4021, [22] = 6029 })),
            [8153] = new(PrototypeFileStatus.Found, Prototype(8153, 7, new() { [21] = 2850, [22] = 8133, [59] = suitSizes })),
            [8130] = new(PrototypeFileStatus.Found, Prototype(8130, 7, new() { [21] = 2851, [22] = 8110, [59] = (int)ArmorSizes.Medium })),
            [15198] = new(PrototypeFileStatus.Found, Prototype(15198, 14, new() { [21] = 3001, [22] = 15178 })),
            [15199] = new(PrototypeFileStatus.Found, Prototype(15199, 14, new() { [21] = 3002, [22] = 15179 })),
        };
        return number => files.GetValueOrDefault(number, new(PrototypeFileStatus.Missing, null));
    }

    private static AreaCreationEditor Editor(AreaNewCharacter creation, AreaHeroLoad hero)
    {
        var draft = AreaNewCharacterTests.Session(hero);
        Assert.IsNull(creation.ApplyChoices(draft, Effect));
        return new AreaCreationEditor(creation, draft);
    }

    [TestMethod]
    public void The_kit_gives_the_background_s_money_and_items_the_suit_and_tools_for_bought_points()
    {
        var (creation, hero) = ElfWoman();
        using var editor = Editor(creation, hero);
        var empty = AreaOpeningKit.Make(editor, out var detail) ?? throw new AssertFailedException(detail ?? "kit");
        Assert.AreEqual((2, 250, 8133, false, 1), (empty.Background, empty.Gold, empty.Clothes, empty.Jacket, empty.Skipped), "block 2: 250 gold; 7000 is missing");
        CollectionAssert.AreEqual(new[] { 6049, 7000, 8133 }, empty.Items.ToArray(), "no points: no lockpicks, no bandages");
        CollectionAssert.AreEqual(new[] { 6069, 8153 }, empty.Kit.Select(item => item.Kind.Prototype).ToArray(), "the missing prototype is skipped (OS4)");

        Assert.IsTrue(editor.PressSkill(10, spend: true).Changed, "heal");
        Assert.IsTrue(editor.PressSkill(14, spend: true).Changed, "pick locks");
        var kit = AreaOpeningKit.Make(editor, out detail) ?? throw new AssertFailedException(detail ?? "kit");
        CollectionAssert.AreEqual(new[] { 6049, 7000, 8133, OpeningKitRules.Lockpicks, OpeningKitRules.Bandages }, kit.Items.ToArray(), "lockpicks, then bandages (SR13)");
        Assert.AreEqual(4, kit.Kit.Count);
        Assert.AreEqual((SessionItemType.Armor, 2850, 8133), (kit.Kit[1].Kind.Type, kit.Kit[1].Kind.Name, kit.Kit[1].Kind.Description));
        Assert.AreEqual(4, kit.Hero.KitWear!.Count, "the kit's tables come with it");

        // The start: the kit's session, the window's choices and the editor's presses, then the best weapon and body armour (OS5).
        using var started = AreaNewCharacterTests.Session(kit.Hero);
        Assert.AreEqual(0, started.QueryEquipment().Worn.Count, "the new character wields nothing when its session is made");
        Assert.IsNull(creation.Apply(started, Effect));
        Assert.IsNull(AreaNewCharacter.WieldStart(started, kit.Hero.KitWear!));
        Assert.AreEqual(250, started.QueryGold());
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3", "kit:4" }, started.QueryHeroKit().Select(item => item.Entity).ToArray());
        Assert.AreEqual("kit:2", started.QueryEquipment().Worn.Single(worn => worn.Slot == EquipmentSlot.Armor).Entity, "the suit is worn");
        Assert.AreEqual(1, started.QueryHero()!.Advancement.TechSkills[(int)Arcanum.Simulation.Skills.Skill.PickLocks - Arcanum.Simulation.Skills.SkillRules.BasicCount]);
    }

    [TestMethod]
    public void A_character_without_a_background_press_gets_block_0_s_money_and_its_suit()
    {
        // The shop reads the PC's background field, 0 when no button was pressed (OS4): block 0's 400 gold; the human man's medium
        // suit 8049 has no prototype in the fixture and is skipped.
        var creation = new AreaNewCharacter(Generic());
        using var editor = Editor(creation, creation.Hero(out _)!);
        var kit = AreaOpeningKit.Make(editor, out var detail) ?? throw new AssertFailedException(detail ?? "kit");
        Assert.AreEqual((0, 400, 8049, 1, 0), (kit.Background, kit.Gold, kit.Clothes, kit.Skipped, kit.Kit.Count));
        Assert.AreSame(creation, kit.Creation);
    }

    [TestMethod]
    public void A_suit_the_wear_check_refuses_gives_the_size_s_jacket()
    {
        // A suit made for small races only: the elf woman (medium) cannot wear it, so the kit destroys it and makes jacket 8110 (OS3).
        var (creation, hero) = ElfWoman(KitFiles(suitSizes: (int)ArmorSizes.Small));
        using var editor = Editor(creation, hero);
        var kit = AreaOpeningKit.Make(editor, out var detail) ?? throw new AssertFailedException(detail ?? "kit");
        Assert.AreEqual((8110, true), (kit.Clothes, kit.Jacket));
        CollectionAssert.AreEqual(new[] { 6069, 8130 }, kit.Kit.Select(item => item.Kind.Prototype).ToArray());
    }

    [TestMethod]
    public void A_scripted_kit_item_or_an_unread_background_block_refuses_the_shop()
    {
        var scripted = KitFiles();
        Func<int, PrototypeFile> files = number => number == 6069
            ? new(PrototypeFileStatus.Found, Prototype(6069, 5, new() { [31] = new Dictionary<int, int> { [21] = 30001 } })) : scripted(number);
        var (creation, hero) = ElfWoman(files);
        using var editor = Editor(creation, hero);
        Assert.IsNull(AreaOpeningKit.Make(editor, out var detail));
        Assert.AreEqual("kit prototype 6069 ItemScriptUnsupported", detail);
        var unread = new AreaNewCharacter(Generic(backgrounds: false));
        using (var plain = Editor(unread, unread.Hero(out _)!))
        {
            Assert.IsNull(AreaOpeningKit.Make(plain, out detail));
            Assert.AreEqual("the background table did not read: FileMissing rules/backgrnd.mes", detail);
        }
        Assert.IsNull(AreaStartingHero.WithKit(hero with { KitPrototypes = null }, 0, [], out _, out detail));
        Assert.AreEqual("not the new character's ready load", detail);
        Assert.IsNull(AreaStartingHero.WithKit(hero, -1, [], out _, out detail));
        editor.Dispose();
        Assert.IsNull(AreaOpeningKit.Make(editor, out detail));
        Assert.AreEqual("the character editor's draft is gone", detail);
    }

    [TestMethod]
    public void The_menu_opens_the_shop_from_the_editor_starts_from_it_and_goes_back_with_every_point_free()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null);
        menu.Type('s'); menu.Type('n');
        var (creation, hero) = ElfWoman();
        menu.OpenNewCharacter(creation);
        menu.BeginName(); menu.Type('n'); menu.NameKey(backspace: false);
        Assert.AreEqual(AreaMenuCommandKind.OpenCharacterEditor, menu.Accept().Kind);
        var editor = Editor(creation, hero);
        menu.OpenCharacterEditor(editor);
        Assert.IsTrue(editor.PressStat(2, spend: true).Changed, "strength before the shop");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.OpenShop), menu.Accept());
        var kit = AreaOpeningKit.Make(editor, out var detail) ?? throw new AssertFailedException(detail ?? "kit");
        menu.OpenShop(kit);
        Assert.AreEqual((AreaMenuWindow.Shop, 0, 0), (menu.Window, menu.Shown.Count, menu.Focus));
        Assert.AreSame(kit, creation.Kit);
        Assert.AreSame(editor, menu.Editor, "the editor and its draft stay for the shop's Back");
        Assert.IsFalse(editor.Disposed);
        CollectionAssert.Contains(menu.HiddenNotes.ToArray(), AreaMainMenu.ShopLoading, "ARC-UI-038: the shop map's merchant loads behind the menu (OS2)");
        Assert.AreSame(kit.Hero, creation.StartHero(out _));
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('a'), "no letters (art buttons)");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewCharacter), menu.Accept(), "Accept (or Return, CM5) starts the game");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewCharacter), menu.Confirm(), "the controller's Accept row");
        menu.OpenShop(kit);
        Assert.AreEqual(AreaMenuWindow.Shop, menu.Window, "a second offer outside the editor changes nothing");

        // Back (the controller's second row): the editor again, the same draft, the kit gone; the strength bought before the shop
        // can still be taken back, down to the character's start (CM4, owner decision 2026-10-28).
        menu.Move(1);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm());
        Assert.AreEqual((AreaMenuWindow.CharacterEditor, 0), (menu.Window, menu.Focus));
        Assert.AreSame(editor, menu.Editor);
        Assert.IsNull(creation.Kit);
        Assert.IsNull(creation.StartHero(out detail));
        Assert.AreEqual("the opening shop made no kit yet", detail);
        Assert.IsTrue(editor.PressStat(2, spend: false).Changed, "the point spent before the shop is free again");
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", editor.PressStat(2, spend: false).Message, "down to the start");
        CollectionAssert.AreEqual(new[] { new AreaCreationPress(AdvancementTrack.Stat, (int)CharacterStat.Strength, true),
            new(AdvancementTrack.Stat, (int)CharacterStat.Strength, false) }, creation.Presses.ToArray(), "both presses replay at the start");

        // Escape from the shop goes back too; Escape from the editor then goes back to window 12 and drops the draft (CM6).
        menu.Accept();
        menu.OpenShop(AreaOpeningKit.Make(editor, out _)!);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Escape());
        Assert.AreEqual(AreaMenuWindow.CharacterEditor, menu.Window);
        menu.Escape();
        Assert.AreEqual((AreaMenuWindow.NewCharacter, true), (menu.Window, editor.Disposed));

        // The new character the window makes again takes its own kit (block 0), once; a kit made for the old character is refused.
        var next = menu.Creation!;
        menu.BeginName(); menu.Type('m'); menu.NameKey(backspace: false);
        menu.Accept();
        var nextEditor = Editor(next, next.Hero(out _)!);
        menu.OpenCharacterEditor(nextEditor);
        menu.Accept();
        menu.OpenShop(kit);
        Assert.AreEqual(AreaMenuWindow.CharacterEditor, menu.Window, "another character's kit does not open the shop");
        var nextKit = AreaOpeningKit.Make(nextEditor, out _)!;
        menu.OpenShop(nextKit);
        Assert.AreEqual((AreaMenuWindow.Shop, 400, 0), (menu.Window, next.Kit!.Gold, next.Kit.Kit.Count));
        Assert.IsNull(creation.Kit, "the old character keeps no kit");
        menu.DropEditor();
    }

    [TestMethod]
    public void The_tables_are_rebuilt_from_a_session_s_kit_by_kit_number()
    {
        var (creation, hero) = ElfWoman();
        using var editor = Editor(creation, hero);
        var kit = AreaOpeningKit.Make(editor, out var detail) ?? throw new AssertFailedException(detail ?? "kit");
        using var started = AreaNewCharacterTests.Session(kit.Hero);
        var held = started.QueryHeroKit();
        // A load reads the generic start again (no kit): its tables come back from the session's kit, and a number no longer held
        // (kit:1 here) has none.
        var rebuilt = AreaStartingHero.WithSessionKit(creation.Generic, [held[1]], out detail) ?? throw new AssertFailedException(detail ?? "kit");
        Assert.AreEqual(2, rebuilt.KitWear!.Count);
        Assert.IsNull(rebuilt.KitWear[0]);
        Assert.AreEqual(kit.Hero.KitWear![1], rebuilt.KitWear[1]);
        Assert.AreSame(creation.Generic, AreaStartingHero.WithSessionKit(creation.Generic, [], out _), "no kit: the load as it is");
        Assert.IsNull(AreaStartingHero.WithSessionKit(creation.Generic, [new("pc:1", held[0].Kind)], out detail));
        Assert.AreEqual("kit item pc:1 has no kit number", detail);
    }

    // The installed original (opt-in): every background block's money and items and every clothes, jacket and tool prototype read as a
    // kit for the new character (no item refused; the counts of read and skipped items are asserted, no text is printed).
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_background_kit_reads()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        var load = AreaStartingHero.Load(Path.Combine(root, "modules", "Arcanum.dat"), AreaStartingHero.NewCharacter);
        Assert.AreEqual(AreaHeroStatus.Ready, load.Status, load.Detail);
        var table = load.Backgrounds!;
        int read = 0, skipped = 0, withItems = 0;
        for (var block = 0; block < table.Count; block++)
        {
            var listed = table.Block(block).Block ?? throw new AssertFailedException("block " + block);
            if (listed.Items.Count > 0) withItems++;
            var kit = AreaStartingHero.WithKit(load, listed.Money, listed.Items, out var missing, out var detail);
            Assert.IsNotNull(kit, $"block {block}: {detail}");
            read += kit.Definition!.Kit.Count; skipped += missing;
        }
        var tools = AreaStartingHero.WithKit(load, 0, [8065, 8049, 8069, 8134, 8133, 8135, 8118, 8110, 8126, OpeningKitRules.Lockpicks, OpeningKitRules.Bandages],
            out var toolsMissing, out var toolsDetail);
        Assert.IsNotNull(tools, toolsDetail);
        Assert.AreEqual((11, 0), (tools.Definition!.Kit.Count, toolsMissing), "the clothes, jackets, lockpicks and bandages all read");
        Assert.IsTrue(tools.KitWear!.Take(9).All(wear => wear is not null), "the clothes and jackets are wearable");
        Assert.AreEqual(10, withItems, "CC-04: 10 blocks carry an item list");
        Assert.AreEqual(0, skipped, "no background item prototype is missing");
        Assert.IsTrue(read >= withItems, $"{read} background items read");
    }

    /// <summary>A v119 prototype (the Data tests' builder): every slot of the type in order; an int value for an int32 slot, a key→script
    /// map for a script array; the item spell slots none; every other slot absent.</summary>
    private static byte[] Prototype(int number, int type, Dictionary<int, object> values)
    {
        // An item's five spell slots (49..53) hold 10000 for none.
        for (var slot = 49; slot <= 53; slot++) values.TryAdd(slot, 10000);
        var bytes = new List<byte>();
        I32(bytes, 119); Id(bytes, -1, 0); Id(bytes, 1, number); I32(bytes, type);
        for (var word = 0; word < StaticObjectLoader.MaskWords(type); word++) I32(bytes, 0);
        var kinds = StaticObjectLoader.FieldsFor(type);
        for (var slot = 0; slot < kinds.Length; slot++)
        {
            values.TryGetValue(slot, out var value);
            switch (kinds[slot])
            {
                case StaticObjectLoader.WireKind.I32: I32(bytes, value is int i ? i : 0); break;
                case StaticObjectLoader.WireKind.ArrScript when value is IReadOnlyDictionary<int, int> scripts:
                    bytes.Add(1); I32(bytes, 12); I32(bytes, scripts.Count); I32(bytes, 0);
                    foreach (var pair in scripts.OrderBy(pair => pair.Key)) { bytes.AddRange(new byte[8]); I32(bytes, pair.Value); }
                    var words = scripts.Keys.Max() / 32 + 1;
                    I32(bytes, words);
                    for (var w = 0; w < words; w++) I32(bytes, scripts.Keys.Where(key => key / 32 == w).Aggregate(0, (bits, key) => bits | 1 << (key % 32)));
                    break;
                default: bytes.Add(0); break;
            }
        }
        return [.. bytes];
    }

    private static void Id(List<byte> bytes, short kind, int number)
    {
        var raw = new byte[24];
        BinaryPrimitives.WriteInt16LittleEndian(raw, kind);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(8), number);
        bytes.AddRange(raw);
    }

    private static void I32(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));
}
