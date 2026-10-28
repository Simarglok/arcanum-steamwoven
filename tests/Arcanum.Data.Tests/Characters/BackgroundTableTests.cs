using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>ARC-DATA-044: the background table (ledger BD1–BD8) over synthetic <c>backgrnd.mes</c> and <c>gameback.mes</c>.</summary>
[TestClass]
public sealed class BackgroundTableTests
{
    private const int Human = 0, Dwarf = 1, Elf = 2, HalfOgre = 7, Female = 0, Male = 1;

    // Block 0 empty condition; 1 elves of both genders; 2 any; 3 NPC only; 4 human men (lower case); 5 dwarf woman; 6 a
    // condition naming no code.
    private const string Blocks =
        "{0}{1000}{1}{86}{2}{}{3}{400}{4}{}\n" +
        "{10}{1001}{11}{90}{12}{ELF ELM}{13}{100}{14}{9001 9002}\n" +
        "{20}{1002}{21}{91}{22}{xxANYxx}{23}{0}{24}{}\n" +
        "{30}{1003}{31}{}{32}{NPC}{33}{400}{34}{}\n" +
        "{40}{1004}{41}{92}{42}{hum}{43}{ 50 }{44}{9003\t9004\n9005}\n" +
        "{50}{1005}{51}{93}{52}{DWF}{53}{800}{54}{}\n" +
        "{60}{1006}{61}{94}{62}{400}{63}{400}{64}{}";

    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-backgrounds-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Blocks_hold_description_effect_condition_money_and_items()
    {
        var table = Table(Blocks);
        Assert.AreEqual(7, table.Count);
        var elves = table.Block(1).Block!;
        Assert.AreEqual((1, 1001, 90, false, "ELF ELM", 100), (elves.Number, elves.Description, elves.Effect, elves.EffectSlotEmpty, Ascii(elves.Condition), elves.Money));
        CollectionAssert.AreEqual(new[] { 9001, 9002 }, elves.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 9003, 9004, 9005 }, table.Block(4).Block!.Items.ToArray(), "split on space, tab and line feed");
        Assert.AreEqual(50, table.Block(4).Block!.Money, "spaces around a number");
        var npc = table.Block(3).Block!;
        Assert.AreEqual((0, true), (npc.Effect, npc.EffectSlotEmpty), "an empty effect slot is effect 0, the module's effect");
        Assert.AreEqual(400, table.BaseMoney());
        Assert.AreEqual(new BackgroundChoice(1, 1001, 90), table.Choice(1));
        Assert.AreEqual((BackgroundBlockStatus.Unreachable, (BackgroundChoice?)null), (table.Block(7).Status, table.Choice(7)));
        Assert.AreEqual(BackgroundBlockStatus.Unreachable, table.Block(-1).Status);
        Assert.AreEqual(BackgroundBlockStatus.Unreachable, table.Block(int.MaxValue).Status);
    }

    [TestMethod]
    public void Blocks_exist_while_their_condition_message_does()
    {
        // Block 1 has no condition message: the first search stops there, but a block after the gap still exists.
        var table = Table("{0}{1000}{1}{1}{2}{}{3}{0}{4}{}\n{10}{1001}{11}{1}{13}{0}{14}{}\n{20}{1002}{21}{1}{22}{}{23}{0}{24}{}");
        Assert.AreEqual(1, table.Count);
        Assert.IsFalse(table.HasBlock(1));
        Assert.IsTrue(table.HasBlock(2));
        Assert.AreEqual(0, table.First(Human, Male));
        Assert.IsNull(table.Next(0, Human, Male), "the forward search stops at the absent block");
        Assert.AreEqual(2, table.Next(1, Human, Male), "a search that starts past the gap finds the later block");
        Assert.IsNull(table.Previous(2, Human, Male), "the backward search stops at the absent block too");
    }

    [TestMethod]
    public void A_condition_allows_its_race_and_gender_codes_any_and_npc()
    {
        var table = Table(Blocks);
        Assert.IsTrue(table.Allows(0, HalfOgre, Male), "an empty condition allows everyone");
        Assert.IsTrue(table.Allows(1, Elf, Female) && table.Allows(1, Elf, Male));
        Assert.IsFalse(table.Allows(1, Human, Male));
        Assert.IsTrue(table.Allows(2, Dwarf, Male), "ANY anywhere in the condition");
        Assert.IsFalse(table.Allows(3, Human, Male), "NPC blocks are for NPCs only");
        Assert.IsTrue(table.Allows(3, Human, Male, npc: true));
        Assert.IsTrue(table.Allows(4, Human, Male), "the condition is compared upper-cased");
        Assert.IsFalse(table.Allows(4, Human, Female));
        Assert.IsTrue(table.Allows(5, Dwarf, Female), "the table, not the female rule of race selection, decides");
        Assert.IsFalse(table.Allows(6, Human, Male), "a condition that names no code allows no one");
        Assert.IsFalse(table.Allows(7, Human, Male), "an absent block");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.Allows(0, 8, Male));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.First(Human, 2));
    }

    [TestMethod]
    public void Searches_step_through_allowed_blocks_and_the_buttons_wrap()
    {
        var table = Table(Blocks);
        // Human man: 0, 2, 4.
        Assert.AreEqual(0, table.First(Human, Male));
        Assert.AreEqual(2, table.Next(0, Human, Male));
        Assert.AreEqual(4, table.Next(2, Human, Male));
        Assert.IsNull(table.Next(4, Human, Male));
        Assert.AreEqual(2, table.Previous(4, Human, Male));
        Assert.IsNull(table.Previous(0, Human, Male));
        Assert.AreEqual(0, table.ButtonNext(4, Human, Male), "next wraps to the first");
        Assert.AreEqual(4, table.ButtonPrevious(0, Human, Male), "previous wraps to the last");
        Assert.AreEqual(2, table.ButtonPrevious(4, Human, Male));
        // Elf woman: 0, 1, 2.
        Assert.AreEqual(1, table.ButtonNext(0, Elf, Female));
        // The NPC-only block is skipped by the buttons, which ask for the PC.
        Assert.AreEqual(4, table.ButtonNext(2, Human, Male));

        var lone = Table("{0}{1000}{1}{1}{2}{ELF}{3}{0}{4}{}\n{10}{1001}{11}{1}{12}{HUM}{13}{0}{14}{}");
        Assert.IsNull(lone.ButtonNext(1, Dwarf, Male), "nothing allowed: the button changes nothing");
        Assert.AreEqual(1, lone.ButtonPrevious(1, Dwarf, Male), "previous always reports success and keeps the current block");
        Assert.AreEqual(1, lone.ButtonPrevious(1, Human, Male), "the only allowed block is its own previous");
    }

    [TestMethod]
    public void Slots_the_original_would_read_partially_are_refused()
    {
        Assert.AreEqual((BackgroundBlockStatus.MissingMessage, 3), Status("{0}{1000}{1}{1}{2}{}{4}{}"));
        Assert.AreEqual((BackgroundBlockStatus.MissingMessage, 4), Status("{0}{1000}{1}{1}{2}{}{3}{0}"));
        Assert.AreEqual((BackgroundBlockStatus.MissingMessage, 0), Status("{1}{1}{2}{}{3}{0}{4}{}"));
        Assert.AreEqual((BackgroundBlockStatus.Malformed, 3), Status("{0}{1000}{1}{1}{2}{}{3}{40x}{4}{}"));
        Assert.AreEqual((BackgroundBlockStatus.Malformed, 1), Status("{0}{1000}{1}{-5}{2}{}{3}{0}{4}{}"));
        Assert.AreEqual((BackgroundBlockStatus.Malformed, 0), Status("{0}{1234567890}{1}{1}{2}{}{3}{0}{4}{}"));
        Assert.AreEqual((BackgroundBlockStatus.Malformed, 4), Status("{0}{1000}{1}{1}{2}{}{3}{0}{4}{9001,9002}"));
        Assert.AreEqual((BackgroundBlockStatus.InvalidText, 4), Status("{0}{1000}{1}{1}{2}{}{3}{0}{4}{9001\u000b9002}"));
        Assert.AreEqual((BackgroundBlockStatus.InvalidText, 3), Status("{0}{1000}{1}{1}{2}{}{3}{4\u00010}{4}{}"));
        var table = Table("{0}{}{1}{1}{2}{}{3}{}{4}{   }");
        Assert.AreEqual((0, 0, 0), (table.Block(0).Block!.Description, table.Block(0).Block!.Money, table.Block(0).Block!.Items.Count), "empty slots read 0");
    }

    [TestMethod]
    public void The_text_has_its_name_on_the_first_line()
    {
        var table = BackgroundTable.Create(Mes(Blocks),
            Mes("{1000}{Plain\nNo special past.}\n{1001}{Long\nfirst\nsecond}\n{1002}{No name line}\n{1003}{" + new string('n', 31) + "\nok}\n" +
                "{1004}{" + new string('n', 32) + "\ntoo long}\n{999}{Not a background}"));
        var plain = table.Text(1000);
        Assert.AreEqual((BackgroundTextStatus.Found, "Plain", "No special past."), (plain.Status, Ascii(plain.Name), Ascii(plain.Body)));
        Assert.AreEqual("Plain", Ascii(table.Text(0).Name), "description 0 reads the default text 1000");
        Assert.AreEqual("first\nsecond", Ascii(table.Text(1001).Body), "the body is everything after the first line feed");
        var noName = table.Text(1002);
        Assert.AreEqual((BackgroundTextStatus.NoName, "No name line"), (noName.Status, Ascii(noName.Body)));
        Assert.AreEqual(BackgroundTextStatus.Found, table.Text(1003).Status, "a 31-byte name fits the 32-byte buffer");
        Assert.AreEqual((BackgroundTextStatus.NoName, "too long"), (table.Text(1004).Status, Ascii(table.Text(1004).Body)));
        Assert.AreEqual(BackgroundTextStatus.Missing, table.Text(999).Status, "1..999 are never background texts");
        Assert.AreEqual(BackgroundTextStatus.Missing, table.Text(1005).Status);
        Assert.AreEqual(BackgroundTextStatus.Missing, table.Text(-1).Status);
    }

    [TestMethod]
    public void Both_files_are_read_from_the_base_sources_and_both_are_required()
    {
        var module = Path.Combine(_root, "module"); var data = Path.Combine(_root, "data");
        Write(module, BackgroundTable.BackgroundsPath, Blocks);
        Write(module, BackgroundTable.TextsPath, "{1000}{Module\ntext}");
        var missing = Load(module, data);
        Assert.AreEqual((BackgroundTableStatus.FileMissing, BackgroundTable.BackgroundsPath), (missing.Status, missing.File), "a module's file is not read at init");
        Write(data, BackgroundTable.BackgroundsPath, Blocks);
        Assert.AreEqual((BackgroundTableStatus.FileMissing, BackgroundTable.TextsPath), (Load(module, data).Status, Load(module, data).File));
        Write(data, BackgroundTable.TextsPath, "{1000}{Base\ntext}");
        var loaded = Load(module, data);
        Assert.AreEqual((BackgroundTableStatus.Ready, "Base"), (loaded.Status, Ascii(loaded.Table!.Text(0).Name)));
        Write(data, BackgroundTable.TextsPath, "{1000}{Base");
        var broken = Load(module, data);
        Assert.AreEqual((BackgroundTableStatus.ParseFailed, BackgroundTable.TextsPath), (broken.Status, broken.File));
        Write(data, BackgroundTable.TextsPath, "{1000}{Base\ntext}");
        Assert.AreEqual(MesParseStatus.TooLarge, Load(module, data, new() { MaxBytes = 4 }).Parse!.Status);
    }

    private (BackgroundBlockStatus, int?) Status(string blocks)
    {
        var read = Table(blocks).Block(0);
        return (read.Status, read.Message);
    }

    private BackgroundTableLoad Load(string module, string data, MesReaderOptions? options = null)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return BackgroundTable.Load(stack, options);
    }

    private static BackgroundTable Table(string blocks) => BackgroundTable.Create(Mes(blocks), Mes("{1000}{Plain\ntext}"));

    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"line {parsed.Line}");
        return parsed.File!;
    }

    private static string Ascii(ReadOnlyMemory<byte> bytes) => Encoding.ASCII.GetString(bytes.Span);

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}

/// <summary>ARC-DATA-044 on the installed original (opt-in, <c>DAT_ROOT</c>), against ARC-RES-025 CC-03..CC-06 and ARC-RES-027
/// SH-07. Counts and numbers only; no text is asserted or printed.</summary>
[TestClass]
public sealed class BackgroundTableRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_backgrounds_match_the_research_counts()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = BackgroundTable.Load(stack);
        Assert.AreEqual(BackgroundTableStatus.Ready, loaded.Status, $"{loaded.File} {loaded.Read} {loaded.Parse?.Status}");
        var table = loaded.Table!;

        Assert.AreEqual(79, table.Count, "CC-02");
        var blocks = Enumerable.Range(0, table.Count).Select(table.Block).ToArray();
        Assert.IsTrue(blocks.All(read => read.Status == BackgroundBlockStatus.Parsed), string.Join(",", blocks.Where(r => r.Block is null).Select(r => $"{r.Status} {r.Message}")));
        var first = blocks[0].Block!;
        Assert.AreEqual((1000, 86, 400), (first.Description, first.Effect, first.Money), "CC-03, SH-07");
        Assert.AreEqual(65, blocks.Count(read => read.Block!.Money == 400), "CC-04");
        Assert.AreEqual(10, blocks.Count(read => read.Block!.Items.Count > 0), "CC-04, SH-07");
        Assert.IsTrue(Enumerable.Range(39, 12).All(b => blocks[b].Block!.EffectSlotEmpty && !table.Allows(b, 0, 1) && table.Allows(b, 0, 1, npc: true)), "CC-05");

        // CC-06: blocks offered per race and gender (Arcanum4.dat wins, block 0 counted).
        var offered = new[] { (0, 1, 49), (0, 0, 52), (1, 1, 34), (2, 1, 36), (2, 0, 38), (3, 1, 39), (3, 0, 41), (4, 1, 43), (5, 1, 44), (6, 1, 44), (6, 0, 46), (7, 1, 38) };
        foreach (var (race, gender, count) in offered)
            Assert.AreEqual(count, Enumerable.Range(0, table.Count).Count(b => table.Allows(b, race, gender)), $"race {race} gender {gender}");
        // The buttons walk the same set in a cycle.
        var walked = new HashSet<int>();
        var current = table.First(0, 1)!.Value;
        while (walked.Add(current)) current = table.ButtonNext(current, 0, 1)!.Value;
        Assert.AreEqual(49, walked.Count);

        // Every description a PC can see has a name within the 32-byte buffer.
        var texts = Enumerable.Range(0, table.Count).Where(b => offered.Any(o => table.Allows(b, o.Item1, o.Item2)))
            .Select(b => table.Text(blocks[b].Block!.Description)).ToArray();
        Console.WriteLine($"backgrounds {table.Count}, offered to a PC {texts.Length}, named {texts.Count(t => t.Status == BackgroundTextStatus.Found)}");
        Assert.IsTrue(texts.All(text => text.Status == BackgroundTextStatus.Found && text.Name.Length > 0 && text.Body.Length > 0));
    }
}
