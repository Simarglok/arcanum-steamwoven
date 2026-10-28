using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>ARC-DATA-045: the portrait catalogue (ledger PO1–PO10) over synthetic <c>gameport.mes</c> and <c>userport.mes</c>.</summary>
[TestClass]
public sealed class PortraitCatalogueTests
{
    private const int Human = 0, Dwarf = 1, Elf = 2, HalfOgre = 7, Female = 0, Male = 1;

    // Human man: 1000, 1001 (lower case), 1003 (ANY); 1004 is NPC only; user 1 human woman, 2 elf man, 3 ANY.
    private const string Game = "{1000}{HUM_01}\n{1001}{hum_02}\n{1002}{ELF_01}\n{1003}{ANY_01}\n{1004}{NPC_01}\n{1005}{DWF_01}\n{1006}{HOM_01}";
    private const string User = "{1}{HUF_u1}\n{2}{ELM_u2}\n{3}{anyu}";

    private static readonly PortraitSubject HumanMan = new(Human, Male), HumanWoman = new(Human, Female);

    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-portraits-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Ids_pick_the_file_and_the_size_picks_the_image()
    {
        var catalogue = Catalogue(Game, User);
        var small = catalogue.Image(1000, 32);
        Assert.AreEqual((PortraitImageStatus.Found, "portrait/HUM_01.bmp", (string?)null), (small.Status, small.Path, small.Fallback));
        Assert.AreEqual((1000, false), (small.Stem.Number, small.Stem.User));
        var large = catalogue.Image(1000, PortraitCatalogue.LargeSize);
        Assert.AreEqual(("portrait/HUM_01_b.bmp", "portrait/HUM_01.bmp"), (large.Path, large.Fallback), "the large image falls back to the small one");
        Assert.AreEqual("portrait/HUM_01.bmp", catalogue.Image(0, 0).Path, "id 0 reads 1000");
        Assert.AreEqual("portrait/HUF_u1.bmp", catalogue.Image(1, 32).Path, "1..999 are the user file's");
        Assert.AreEqual((PortraitStemStatus.Found, true), (catalogue.Stem(2).Status, catalogue.Stem(2).User));
        Assert.AreEqual(PortraitStemStatus.Missing, catalogue.Stem(4).Status);
        Assert.AreEqual(PortraitStemStatus.Missing, catalogue.Stem(1007).Status);
        Assert.AreEqual(PortraitImageStatus.Missing, catalogue.Image(1007, 32).Status);

        var gameOnly = Catalogue(Game);
        Assert.IsFalse(gameOnly.HasUserFile);
        Assert.AreEqual(PortraitStemStatus.NoUserFile, gameOnly.Stem(1).Status, "no user file: an empty path");
        Assert.AreEqual((PortraitImageStatus.Missing, PortraitStemStatus.NegativeId), (gameOnly.Image(-1, 32).Status, gameOnly.Image(-1, 32).Stem.Status),
            "a negative id is refused (the original reads through its buffer as a file handle)");

        var odd = Catalogue("{1000}{HUM_b}\n{1001}{}\n{1002}{HUM\u0001}\n{1003}{a\\b}\n{1004}{a/b}\n{1005}{HUM_B}");
        Assert.AreEqual(("portrait/HUM_b.bmp", "portrait/HUM.bmp"), (odd.Image(1000, 32).Path, odd.Image(1000, 32).Fallback),
            "the fallback strips any _b.bmp ending, so a small stem ending in _b falls back too");
        Assert.AreEqual("portrait/HUM.bmp", odd.Image(1005, 32).Fallback, "the ending is compared ignoring case");
        foreach (var id in new[] { 1001, 1002, 1003, 1004 })
            Assert.AreEqual(PortraitImageStatus.InvalidStem, odd.Image(id, 32).Status, $"stem {id}");
    }

    [TestMethod]
    public void A_portrait_matches_by_its_first_three_letters()
    {
        Assert.IsTrue(Match("HUM_01", HumanMan));
        Assert.IsTrue(Match("hUm", HumanMan), "case is ignored");
        Assert.IsFalse(Match("HUF_01", HumanMan));
        Assert.IsTrue(Match("HUF_01", HumanWoman));
        Assert.IsTrue(Match("ANY", new(HalfOgre, Male)));
        Assert.IsTrue(Match("any_x", new(Dwarf, Female)));
        Assert.IsFalse(Match("xANY", HumanMan), "a prefix, not a substring");
        Assert.IsFalse(Match("HU", HumanMan), "a text shorter than three letters never matches");
        Assert.IsFalse(Match("NPC_01", HumanMan), "NPC portraits are for NPCs only");
        Assert.IsTrue(Match("NPC_01", new(Human, Male, npc: true)));
        Assert.IsTrue(Match("ELM", new(8, Male, npc: true)), "race 8 reuses EL");
        Assert.IsTrue(Match("HGM", new(9, Male, npc: true)), "race 9 reuses HG");
        Assert.IsTrue(Match("HOF", new(10, Female, npc: true)), "race 10 reuses HO");
        Assert.IsTrue(Match("DWM", new(Dwarf, Male)) && Match("HEF", new(3, Female)) && Match("GNM", new(4, Male)) && Match("HAM", new(5, Male)) &&
            Match("HOM", new(6, Male)) && Match("HGM", new(HalfOgre, Male)) && Match("ELF", new(Elf, Female)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PortraitSubject(11, Male));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PortraitSubject(Human, 2));
        Assert.ThrowsExactly<ArgumentException>(() => new PortraitSubject(Human, Male, npc: true, multiplayerPc: true));
        Assert.IsTrue(Catalogue(Game).Allows(0, HumanMan), "Allows reads ids as the path does");
        Assert.IsFalse(Catalogue(Game).Allows(1007, HumanMan));
    }

    [TestMethod]
    public void Searches_walk_game_ids_then_user_ids_without_wrapping()
    {
        var catalogue = Catalogue(Game, User);
        Assert.AreEqual(1000, catalogue.First(HumanMan));
        Assert.AreEqual(1001, catalogue.Next(1000, HumanMan));
        Assert.AreEqual(1003, catalogue.Next(1001, HumanMan));
        Assert.AreEqual(3, catalogue.Next(1003, HumanMan), "after the game ids, the user ids from 1");
        Assert.IsNull(catalogue.Next(3, HumanMan));
        Assert.AreEqual(1, catalogue.Next(0, HumanWoman), "from the unset id 0 the next search reads the user ids");
        Assert.AreEqual(1003, catalogue.Previous(3, HumanMan), "before the user ids, the game ids down from count + 999");
        Assert.AreEqual(1000, catalogue.Previous(1001, HumanMan));
        Assert.IsNull(catalogue.Previous(1000, HumanMan), "nothing comes before 1000");
        Assert.AreEqual((1003, 1), (catalogue.First(HumanWoman), catalogue.Next(1003, HumanWoman)));
        Assert.AreEqual(1003, catalogue.Last(1000, HumanMan), "the last search stays in the game ids when it finds one there");
        Assert.AreEqual(3, catalogue.Last(1004, HumanMan), "none left in the game ids: the last user id");
        Assert.AreEqual(3, catalogue.Last(2, HumanMan), "a user start reads the user ids from 1");
        Assert.AreEqual(1004, catalogue.Next(1003, new(Human, Male, npc: true)));
        Assert.AreEqual(1, catalogue.Previous(3, HumanWoman), "the backward search finds a user id on its way down");
        Assert.AreEqual(1003, Catalogue(Game).Previous(5, HumanMan), "no user file: from a user id straight to the game ids");
        Assert.AreEqual(3, Catalogue("{0}{HUM}", User).Last(0, HumanMan), "a match at id 0 counts as none, so the last search reads the user ids");

        // A gap ends the game ids, and the backward search starts at count + 999, not at the highest id.
        var gap = Catalogue("{1000}{HUM}\n{1002}{HUM}", User);
        Assert.AreEqual(3, gap.Next(1000, HumanMan), "1001 is absent, so 1002 is never reached");
        Assert.AreEqual(2, gap.GameCount);
        Assert.IsNull(gap.Previous(1, HumanMan), "two messages: the game search starts at 1001, which is absent");
        Assert.IsNull(Catalogue("{1001}{HUM}").First(HumanMan), "the game ids start at 1000");

        var multiplayer = new PortraitSubject(Human, Male, multiplayerPc: true);
        Assert.IsTrue(catalogue.First(multiplayer) is null && catalogue.Next(1000, multiplayer) is null && catalogue.Previous(1001, multiplayer) is null &&
            catalogue.Last(1000, multiplayer) is null, "a multiplayer PC finds no portrait");
    }

    [TestMethod]
    public void The_creation_buttons_wrap_and_a_race_change_picks_the_first()
    {
        var catalogue = Catalogue(Game, User);
        Assert.AreEqual(1000, catalogue.Press(PortraitButton.Next, 3, HumanMan), "next wraps to the first");
        Assert.AreEqual(1003, catalogue.Press(PortraitButton.Previous, 1000, HumanMan), "previous wraps to the last game portrait, not to the user ones");
        Assert.AreEqual(1001, catalogue.Press(PortraitButton.Previous, 1003, HumanMan));
        Assert.AreEqual(1001, catalogue.Press(PortraitButton.Next, 1000, HumanMan));

        var humans = Catalogue("{1000}{HUM}");
        var elf = new PortraitSubject(Elf, Male);
        Assert.AreEqual(0, humans.Press(PortraitButton.Next, 1000, elf), "nothing allowed: next stores 0");
        Assert.AreEqual(1000, humans.Press(PortraitButton.Previous, 1000, elf), "nothing allowed: previous keeps the portrait");
        Assert.AreEqual(1000, humans.Press(PortraitButton.Previous, 1000, HumanMan), "the only portrait is its own previous");
        Assert.AreEqual(1002, catalogue.AfterRaceOrGender(1000, new(Elf, Female)), "the first portrait of the new race and gender");
        Assert.AreEqual(1003, catalogue.AfterRaceOrGender(1000, elf), "ELF_01 is an elf woman's; the elf man gets ANY");
        Assert.AreEqual(1000, humans.AfterRaceOrGender(1000, elf), "none: the portrait stays");
        Assert.AreEqual(0, catalogue.Press(PortraitButton.Next, 1000, new(Human, Male, multiplayerPc: true)));
    }

    [TestMethod]
    public void An_npc_with_the_players_portrait_shows_another()
    {
        var catalogue = Catalogue(Game, User);
        var npc = new PortraitSubject(Human, Male, npc: true);
        Assert.AreEqual(1001, catalogue.Shown(1000, npc, 1000), "the next one");
        Assert.AreEqual(3, catalogue.Shown(1004, npc, 1004), "next goes on into the user ids before previous is tried");
        Assert.AreEqual(1001, catalogue.Shown(1001, npc, 1000), "a different portrait is shown as stored");
        Assert.AreEqual(1000, catalogue.Shown(1000, HumanMan, 1000), "a PC shows its own");
        Assert.AreEqual(0, catalogue.Shown(0, npc, 0), "an unset portrait stays 0");
        var lone = Catalogue("{1000}{HUM}\n{1001}{HUM}");
        Assert.AreEqual(1000, lone.Shown(1001, npc, 1001), "the last one takes the previous");
        Assert.AreEqual(0, Catalogue("{1000}{HUM}").Shown(1000, npc, 1000), "no other: 0");
    }

    [TestMethod]
    public void Both_files_are_read_from_the_base_sources_and_only_the_game_file_is_required()
    {
        var module = Path.Combine(_root, "module"); var data = Path.Combine(_root, "data");
        Write(module, PortraitCatalogue.GamePath, Game);
        var missing = Load(module, data);
        Assert.AreEqual((PortraitCatalogueStatus.FileMissing, PortraitCatalogue.GamePath), (missing.Status, missing.File), "a module's file is not read at init");
        Write(data, PortraitCatalogue.GamePath, Game);
        var gameOnly = Load(module, data);
        Assert.AreEqual((PortraitCatalogueStatus.Ready, false), (gameOnly.Status, gameOnly.Catalogue!.HasUserFile), "the user file is optional");
        Write(module, PortraitCatalogue.UserPath, User);
        Assert.IsFalse(Load(module, data).Catalogue!.HasUserFile, "a module's user file is not read either");
        Write(data, PortraitCatalogue.UserPath, User);
        var both = Load(module, data);
        Assert.AreEqual((PortraitCatalogueStatus.Ready, "portrait/HUF_u1.bmp"), (both.Status, both.Catalogue!.Image(1, 32).Path));
        Write(data, PortraitCatalogue.UserPath, "{1}{HUF");
        var broken = Load(module, data);
        Assert.AreEqual((PortraitCatalogueStatus.Ready, false), (broken.Status, broken.Catalogue!.HasUserFile), "a user file that cannot be parsed is no user file");
        Assert.AreEqual((PortraitCatalogueStatus.ParseFailed, PortraitCatalogue.UserPath), (broken.UserRefusal!.Status, broken.UserRefusal.File), "and is reported");
        Assert.IsNull(both.UserRefusal);
        Write(data, PortraitCatalogue.UserPath, User);
        Assert.AreEqual(MesParseStatus.TooLarge, Load(module, data, new() { MaxBytes = 4 }).Parse!.Status);
    }

    private PortraitCatalogueLoad Load(string module, string data, MesReaderOptions? options = null)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return PortraitCatalogue.Load(stack, options);
    }

    private static bool Match(string stem, PortraitSubject subject) => PortraitCatalogue.Matches(Encoding.Latin1.GetBytes(stem), subject);

    private static PortraitCatalogue Catalogue(string game, string? user = null) => PortraitCatalogue.Create(Mes(game), user is null ? null : Mes(user));

    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"line {parsed.Line}");
        return parsed.File!;
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}

/// <summary>ARC-DATA-045 on the installed original (opt-in, <c>DAT_ROOT</c>), against ARC-RES-025 CC-08 and the pre-made
/// portraits. Counts and numbers only; no text is asserted or printed.</summary>
[TestClass]
public sealed class PortraitCatalogueRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_portraits_serve_every_player_race_and_gender()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = PortraitCatalogue.Load(stack);
        Assert.AreEqual(PortraitCatalogueStatus.Ready, loaded.Status, $"{loaded.File} {loaded.Read} {loaded.Parse?.Status}");
        var catalogue = loaded.Catalogue!;

        // 78 portraits 1000..1077, no user file: 4 for each of the twelve player race and gender pairs (with a large image) and
        // 30 for NPCs only (small image only).
        var run = 0;
        while (catalogue.Stem(PortraitCatalogue.FirstGameId + run).Status == PortraitStemStatus.Found) run++;
        Assert.AreEqual((78, 78, false), (catalogue.GameCount, run, catalogue.HasUserFile));
        var ids = Enumerable.Range(PortraitCatalogue.FirstGameId, run).ToArray();
        bool Resolves(PortraitImage image) => image.Status == PortraitImageStatus.Found && stack.Resolve(image.Path!).Status == ContentResolutionStatus.Resolved;
        Assert.IsTrue(ids.All(id => Resolves(catalogue.Image(id, 32))), "every portrait has its small image");
        var large = ids.Where(id => Resolves(catalogue.Image(id, PortraitCatalogue.LargeSize))).ToHashSet();
        Console.WriteLine($"portraits {run}, large images {large.Count}");

        // The pairs the race table offers (CC-02): human, elf, half-elf and half-orc both genders, the others male.
        var pairs = new[] { (0, 1), (0, 0), (1, 1), (2, 1), (2, 0), (3, 1), (3, 0), (4, 1), (5, 1), (6, 1), (6, 0), (7, 1) };
        var offered = new HashSet<int>();
        foreach (var (race, gender) in pairs)
        {
            var pc = new PortraitSubject(race, gender);
            var allowed = ids.Where(id => catalogue.Allows(id, pc)).ToArray();
            Assert.AreEqual(4, allowed.Length, $"race {race} gender {gender}");
            Assert.IsTrue(allowed.All(large.Contains), $"race {race} gender {gender}: a PC portrait has its large image");
            Assert.AreEqual(34, ids.Count(id => catalogue.Allows(id, new(race, gender, npc: true))), $"race {race} gender {gender} as an NPC");
            // The buttons cycle through the same four, both ways.
            var walked = new List<int> { catalogue.First(pc)!.Value };
            while (walked.Count <= run && catalogue.Press(PortraitButton.Next, walked[^1], pc) is var next && next != walked[0]) walked.Add(next);
            CollectionAssert.AreEqual(allowed, walked);
            Assert.AreEqual(allowed[^1], catalogue.Press(PortraitButton.Previous, allowed[0], pc));
            offered.UnionWith(allowed);
        }
        Assert.AreEqual((48, 48), (offered.Count, large.Count), "the large images are the PC portraits'");
        Assert.AreEqual(30, ids.Count(id => !offered.Contains(id) && catalogue.Allows(id, new(0, 1, npc: true))), "the rest are NPC portraits");
        Assert.IsNull(catalogue.First(new(1, 0)), "no dwarf woman portrait (the race table offers none)");

        // The pre-made heroes' portraits (ARC-RES-025, CC-08) are allowed for their race and gender.
        var premade = new[] { (1005, 0, 1), (1017, 5, 1), (1002, 4, 1), (1032, 3, 1), (1027, 7, 1), (1033, 2, 1), (1030, 6, 1), (1020, 1, 1), (1025, 0, 0), (1024, 2, 0), (1031, 6, 0), (1029, 3, 0) };
        foreach (var (id, race, gender) in premade)
            Assert.IsTrue(catalogue.Allows(id, new(race, gender)), $"portrait {id}");
    }
}
