using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

[TestClass]
public sealed class ObjectDisplayNameTests
{
    private const int Wall = 0, Container = 2, Weapon = 5, Gold = 8, Key = 11, Generic = 14, Pc = 15, Npc = 16, Trap = 17;
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-mes-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static readonly ObjectDescriptionCatalog Catalog = ObjectDescriptionCatalog.Create(
        Mes("{100}{Coat}{101}{Odd Coat}{200}{Stranger}{201}{Bandit}{5600}{Generic}"),
        Mes("{30000}{Ring}{30005}{Strange Ring}"),
        Mes("{0}{Shed Key}"));

    private static ObjectNameResolution Name(int type, Dictionary<int, int> fields, CritterRecognition recognition = CritterRecognition.Recognized, ObjectDescriptionCatalog? catalog = null) =>
        ObjectDisplayName.Resolve(type, slot => fields.TryGetValue(slot, out var value) ? value : null, catalog ?? Catalog, recognition);

    private static string Text(ObjectNameResolution resolution) => Encoding.ASCII.GetString(resolution.Message!.Bytes.Span);

    [TestMethod]
    public void Numbers_below_30000_use_the_base_file_and_the_rest_the_module_file()
    {
        Assert.AreEqual(30005, Catalog.MaxNumber);
        Assert.AreEqual((DescriptionLookupStatus.Found, ObjectDescriptionFile.Description), (Catalog.Find(100).Status, Catalog.Find(100).File));
        Assert.AreEqual((DescriptionLookupStatus.Found, ObjectDescriptionFile.GameDescription), (Catalog.Find(30000).Status, Catalog.Find(30000).File));
        Assert.AreEqual((DescriptionLookupStatus.NotFound, ObjectDescriptionFile.Description), (Catalog.Find(29999).Status, Catalog.Find(29999).File));
        Assert.AreEqual(DescriptionLookupStatus.NotFound, Catalog.Find(30001).Status);
        Assert.AreEqual(new DescriptionLookup(DescriptionLookupStatus.OutOfRange, -1, null, null), Catalog.Find(-1));
        Assert.AreEqual(DescriptionLookupStatus.OutOfRange, Catalog.Find(30006).Status);
        Assert.AreEqual(DescriptionLookupStatus.Found, Catalog.FindKey(0).Status);
        Assert.AreEqual((DescriptionLookupStatus.NotFound, ObjectDescriptionFile.GameKey), (Catalog.FindKey(-3).Status, Catalog.FindKey(-3).File));
    }

    [TestMethod]
    public void The_accepted_range_ends_at_the_last_loaded_file_with_messages()
    {
        var baseFile = Mes("{10}{a}{29000}{b}");
        Assert.AreEqual(29000, ObjectDescriptionCatalog.Create(baseFile).MaxNumber);
        var emptyModule = ObjectDescriptionCatalog.Create(baseFile, Mes("// empty"), Mes(""));
        Assert.AreEqual(29000, emptyModule.MaxNumber, "An empty module file is not loaded and keeps the base range.");
        Assert.AreEqual((null, null), (emptyModule.GameDescription, emptyModule.GameKey));
        Assert.AreEqual(DescriptionLookupStatus.ModuleFileAbsent, emptyModule.FindKey(0).Status);

        // A module file whose last number is below a base number cuts the base range short.
        var shortModule = ObjectDescriptionCatalog.Create(Mes("{10}{a}{29000}{b}{31000}{c}"), Mes("{30000}{d}"));
        Assert.AreEqual(30000, shortModule.MaxNumber);
        Assert.AreEqual(DescriptionLookupStatus.Found, shortModule.Find(29000).Status);
        Assert.AreEqual(DescriptionLookupStatus.OutOfRange, shortModule.Find(31000).Status);

        var noModule = ObjectDescriptionCatalog.Create(Mes("{10}{a}{31000}{c}"));
        Assert.AreEqual((DescriptionLookupStatus.ModuleFileAbsent, ObjectDescriptionFile.GameDescription), (noModule.Find(30000).Status, noModule.Find(30000).File));
        Assert.AreEqual(DescriptionLookupStatus.ModuleFileAbsent, noModule.FindKey(0).Status);
        Assert.ThrowsExactly<ArgumentException>(() => ObjectDescriptionCatalog.Create(Mes("")));
    }

    [TestMethod]
    public void The_name_comes_from_the_description_and_never_from_the_name_class()
    {
        var generic = Name(Generic, new() { [ObjectDisplayName.NameSlot] = 5600, [ObjectDisplayName.DescriptionSlot] = 100, [ObjectDisplayName.ItemComplexitySlot] = 0 });
        Assert.AreEqual((ObjectNameStatus.Resolved, ObjectNameSource.Description, ObjectDisplayName.DescriptionSlot, 100), (generic.Status, generic.Source, generic.Field, generic.Number));
        Assert.AreEqual("Coat", Text(generic));

        foreach (var type in new[] { Wall, Container, Trap })
            Assert.AreEqual("Coat", Text(Name(type, new() { [ObjectDisplayName.DescriptionSlot] = 100 })), $"type {type}");
        Assert.AreEqual("Ring", Text(Name(Gold, new() { [ObjectDisplayName.DescriptionSlot] = 30000, [ObjectDisplayName.ItemComplexitySlot] = -5 })));
    }

    [TestMethod]
    public void An_item_shows_its_unknown_description_until_it_is_identified()
    {
        var unidentified = new Dictionary<int, int> { [ObjectDisplayName.DescriptionSlot] = 30000, [ObjectDisplayName.ItemComplexitySlot] = 40, [ObjectDisplayName.ItemFlagsSlot] = 0x2, [ObjectDisplayName.ItemUnknownDescriptionSlot] = 30005 };
        var hidden = Name(Weapon, unidentified);
        Assert.AreEqual((ObjectNameSource.UnknownItemDescription, ObjectDisplayName.ItemUnknownDescriptionSlot), (hidden.Source, hidden.Field));
        Assert.AreEqual("Strange Ring", Text(hidden));

        unidentified[ObjectDisplayName.ItemFlagsSlot] = unchecked((int)0x8000_0003);
        Assert.AreEqual("Ring", Text(Name(Weapon, unidentified)), "The identified flag shows the description.");

        var missingFlags = Name(Weapon, new() { [ObjectDisplayName.ItemComplexitySlot] = 1, [ObjectDisplayName.DescriptionSlot] = 100 });
        Assert.AreEqual((ObjectNameStatus.FieldUnresolved, (ObjectNameSource?)null, ObjectDisplayName.ItemFlagsSlot), (missingFlags.Status, missingFlags.Source, missingFlags.Field));
        var missingComplexity = Name(Generic, new() { [ObjectDisplayName.DescriptionSlot] = 100 });
        Assert.AreEqual((ObjectNameStatus.FieldUnresolved, ObjectDisplayName.ItemComplexitySlot), (missingComplexity.Status, missingComplexity.Field));
        Assert.AreEqual("Coat", Text(Name(Container, new() { [ObjectDisplayName.DescriptionSlot] = 100 })), "Non-items read no item fields.");
    }

    [TestMethod]
    public void Keys_npcs_and_player_characters_take_their_own_branches()
    {
        var key = Name(Key, new() { [ObjectDisplayName.KeyIdSlot] = 0, [ObjectDisplayName.ItemComplexitySlot] = 99, [ObjectDisplayName.DescriptionSlot] = 100 });
        Assert.AreEqual((ObjectNameSource.KeyName, ObjectDescriptionFile.GameKey), (key.Source, key.Lookup!.File));
        Assert.AreEqual("Shed Key", Text(key), "A key ignores identification and its description.");

        var npc = new Dictionary<int, int> { [ObjectDisplayName.DescriptionSlot] = 201, [ObjectDisplayName.CritterUnknownDescriptionSlot] = 200 };
        Assert.AreEqual("Bandit", Text(Name(Npc, npc, CritterRecognition.Recognized)));
        var stranger = Name(Npc, npc, CritterRecognition.NotRecognized);
        Assert.AreEqual((ObjectNameSource.UnknownCritterDescription, ObjectDisplayName.CritterUnknownDescriptionSlot), (stranger.Source, stranger.Field));
        Assert.AreEqual("Stranger", Text(stranger));
        Assert.AreEqual("Coat", Text(Name(Generic, new() { [ObjectDisplayName.DescriptionSlot] = 100, [ObjectDisplayName.ItemComplexitySlot] = 0 }, CritterRecognition.NotRecognized)), "Recognition only matters for NPCs.");

        var pc = Name(Pc, npc, CritterRecognition.NotRecognized);
        Assert.AreEqual(new ObjectNameResolution(ObjectNameStatus.PlayerNameNotModelled, ObjectNameSource.PlayerName, null, null, null), pc);
        Assert.IsNull(pc.Message);
    }

    [TestMethod]
    public void Missing_text_and_bad_inputs_are_typed()
    {
        var missing = Name(Wall, new() { [ObjectDisplayName.DescriptionSlot] = 150 });
        Assert.AreEqual((ObjectNameStatus.NoText, DescriptionLookupStatus.NotFound), (missing.Status, missing.Lookup!.Status));
        Assert.IsNull(missing.Message);
        Assert.AreEqual(DescriptionLookupStatus.OutOfRange, Name(Wall, new() { [ObjectDisplayName.DescriptionSlot] = 99999 }).Lookup!.Status);
        Assert.AreEqual(DescriptionLookupStatus.ModuleFileAbsent,
            Name(Key, new() { [ObjectDisplayName.KeyIdSlot] = 0 }, catalog: ObjectDescriptionCatalog.Create(Mes("{1}{a}"))).Lookup!.Status);
        Assert.AreEqual(ObjectNameStatus.FieldUnresolved, Name(Wall, []).Status);
        Assert.AreEqual(ObjectNameStatus.UnsupportedType, Name(18, new() { [ObjectDisplayName.DescriptionSlot] = 100 }).Status);
        Assert.AreEqual(ObjectNameStatus.UnsupportedType, Name(-1, new() { [ObjectDisplayName.DescriptionSlot] = 100 }).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Name(Wall, [], (CritterRecognition)7));
    }

    [TestMethod]
    public void The_catalog_reads_the_winning_copies_from_the_content_stack()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(data, "MES/Description.mes", "{100}{Base Coat}{29000}{last}");
        Write(module, "mes/description.mes", "{100}{Module Coat}");
        Write(module, "mes/gamedesc.mes", "{30000}{Ring}");
        var loaded = Load(module, data);

        Assert.AreEqual(DescriptionCatalogStatus.Ready, loaded.Status);
        var catalog = loaded.Catalog!;
        Assert.AreEqual(ContentSourceRole.DataDirectory, catalog.DescriptionSource!.Source.Role, "The base file is read before any module source is mounted.");
        Assert.AreEqual("Base Coat", Text(Name(Wall, new() { [ObjectDisplayName.DescriptionSlot] = 100 }, catalog: catalog)));
        Assert.AreEqual(ContentSourceRole.ModuleDirectory, catalog.GameDescriptionSource!.Source.Role);
        Assert.AreEqual(30000, catalog.MaxNumber);
        Assert.IsNull(catalog.GameKey);
        Assert.IsNull(catalog.GameKeySource);
        Assert.AreEqual(DescriptionLookupStatus.ModuleFileAbsent, catalog.FindKey(0).Status);
    }

    [TestMethod]
    public void The_catalog_refuses_a_missing_base_file_and_unparsable_tables()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(module, "mes/gamedesc.mes", "{30000}{Ring}");
        Write(data, "readme.txt", "x");
        Assert.AreEqual(new DescriptionCatalogLoad(DescriptionCatalogStatus.DescriptionMissing, null, ObjectDescriptionFile.Description), Load(module, data));
        Write(module, "mes/description.mes", "{100}{Module Coat}");
        Assert.AreEqual(DescriptionCatalogStatus.DescriptionMissing, Load(module, data).Status, "A module copy is never the base file.");
        Write(data, "mes/description.mes", "// no messages");
        Assert.AreEqual(DescriptionCatalogStatus.DescriptionMissing, Load(module, data).Status, "An empty base file is not loaded.");

        Write(data, "mes/description.mes", "{100}{Coat}");
        Write(module, "mes/gamekey.mes", "{0}{Key");
        var broken = Load(module, data);
        Assert.AreEqual((DescriptionCatalogStatus.ParseFailed, ObjectDescriptionFile.GameKey, MesParseStatus.UnterminatedField), (broken.Status, broken.File, broken.Parse!.Status));
        Assert.IsNull(broken.Catalog);

        var tooSmall = Load(module, data, new() { MaxBytes = 4 });
        Assert.AreEqual((DescriptionCatalogStatus.ParseFailed, ObjectDescriptionFile.Description, MesParseStatus.TooLarge), (tooSmall.Status, tooSmall.File, tooSmall.Parse!.Status));
    }

    private DescriptionCatalogLoad Load(string module, string data, MesReaderOptions? options = null)
    {
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return ObjectDescriptionCatalog.Load(stack, options);
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}
