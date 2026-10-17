using System.Text;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Audio;

/// <summary>ARC-DATA-040: the sound catalogue on synthetic installations (no original data). Expected values are written
/// from the behaviour recorded in docs/research/audio-evidence.md, not computed by the implementation.</summary>
[TestClass]
public sealed class SoundCatalogTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-sound-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static byte[] Mes(params (int Number, string Text)[] records) =>
        Encoding.ASCII.GetBytes(string.Concat(records.Select(r => $"{{{r.Number}}}{{{r.Text}}}\r\n")));

    /// <summary>A minimal installation: two tables (index numbers 2 and 1 written in that order), schemes, parameters.</summary>
    private void Base(string? positional = "{1}{150}")
    {
        Write("data/sound/snd_00index.mes", Mes((2, "snd_b.mes"), (1, "snd_a.mes")));
        Write("data/sound/snd_a.mes", Mes((10, "a10.wav"), (11, ""), (12, "nofile.wav")));
        Write("data/sound/snd_b.mes", Mes((10, "b10.wav"), (11, "b11.wav"), (20, "b20.wav")));
        Write("data/sound/SchemeIndex.mes", Mes((0, "<<NONE>>"), (1, "M.Test #100"), (2, "A.Test #200"), (3, "<<No Sound>>")));
        Write("data/sound/SchemeList.mes", Mes((100, "music\\Song.mp3 /loop /vol:50 /time:5-19"), (200, "#10 /freq:20 /time:19-0")));
        if (positional is not null) Write("data/sound/soundparams.mes", Encoding.ASCII.GetBytes(positional));
        Write("data/sound/a10.wav", [1]);
        Write("data/sound/b11.wav", [1]);
        Write("data/sound/b20.wav", [1]);
        Write("modules/Mod/sound/music/Song.mp3", [1]);
    }

    [TestMethod]
    public void Tables_are_searched_in_index_number_order_and_the_user_table_last()
    {
        Base();
        Write("modules/Mod/sound/snd_user.mes", Mes((10, "user10.wav"), (30, "user30.wav")));
        Write("modules/Mod/sound/user30.wav", [1]);
        using var stack = Stack();
        var catalog = Open(stack);

        CollectionAssert.AreEqual(new[] { "snd_a.mes", "snd_b.mes" }, catalog.Tables.Select(t => t.Name).ToArray());
        Assert.AreEqual(ContentSourceRole.ModuleDirectory, catalog.UserTable!.Candidate.Source.Role);

        var first = catalog.ResolveSfx(10);
        Assert.AreEqual((SfxStatus.Named, "snd_a.mes", "sound/a10.wav", true), (first.Status, first.Table!.Name, first.File!.Key, first.Playable));
        var empty = catalog.ResolveSfx(11);
        Assert.AreEqual((SfxStatus.EmptyRecord, "snd_a.mes", false), (empty.Status, empty.Table!.Name, empty.Playable), "an empty record in the first table wins");
        var missing = catalog.ResolveSfx(12);
        Assert.AreEqual((SfxStatus.Named, SoundFileStatus.Missing, false), (missing.Status, missing.File!.Status, missing.Playable));
        Assert.AreEqual("snd_b.mes", catalog.ResolveSfx(20).Table!.Name);
        var user = catalog.ResolveSfx(30);
        Assert.AreEqual((SfxStatus.Named, true, true), (user.Status, user.Table!.IsUserTable, user.Playable), "user table only after every index table missed");
        Assert.AreEqual(SfxStatus.UnknownId, catalog.ResolveSfx(31).Status);
        Assert.AreEqual(0, catalog.Issues.Count);
    }

    [TestMethod]
    public void Init_files_come_from_the_base_sources_and_user_table_and_sounds_from_the_winner()
    {
        Base();
        Write("modules/Mod/sound/snd_00index.mes", Mes((1, "snd_m.mes")));
        Write("modules/Mod/sound/snd_a.mes", Mes((10, "module10.wav")));
        Write("modules/Mod/sound/SchemeIndex.mes", Mes((1, "M.Module #900")));
        Write("modules/Mod/sound/a10.wav", [2]);
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(("sound/snd_user.mes", Mes((40, "dat40.wav")))));
        using var stack = Stack();
        var catalog = Open(stack);

        Assert.AreEqual(ContentSourceRole.DataDirectory, catalog.Tables[0].Candidate.Source.Role, "the module copy of a table is never read");
        Assert.AreEqual("sound/a10.wav", catalog.ResolveSfx(10).File!.Key);
        Assert.AreEqual(ContentSourceRole.ModuleDirectory, catalog.ResolveSfx(10).File!.Candidate!.Source.Role, "the file itself is opened while the game runs");
        Assert.AreEqual(100, catalog.Scheme(1).ListBase, "SchemeIndex from the base sources");
        Assert.AreEqual(ContentSourceRole.ModuleArchive, catalog.UserTable!.Candidate.Source.Role);
        Assert.AreEqual(SfxStatus.Named, catalog.ResolveSfx(40).Status);
    }

    [TestMethod]
    public void Missing_or_malformed_init_files_refuse_the_catalogue_with_a_typed_status()
    {
        using (var stack = Stack()) Assert.AreEqual((SoundCatalogStatus.MissingFile, SoundCatalog.IndexPath), Status(stack));

        Base();
        File.Delete(Path.Combine(_root, "data", "sound", "snd_b.mes"));
        using (var stack = Stack()) Assert.AreEqual((SoundCatalogStatus.MissingFile, "sound/snd_b.mes"), Status(stack));

        Write("data/sound/snd_b.mes", Encoding.ASCII.GetBytes("{1}{x}\r\n{1}{y}\r\n"));
        using (var stack = Stack())
        {
            var result = SoundCatalog.Open(stack);
            Assert.AreEqual((SoundCatalogStatus.Malformed, "sound/snd_b.mes", MesParseStatus.DuplicateNumber), (result.Status, result.File, result.Mes));
        }

        Write("data/sound/snd_b.mes", Mes((20, "b20.wav")));
        Write("data/sound/snd_00index.mes", Mes((1, "")));
        using (var stack = Stack()) Assert.AreEqual((SoundCatalogStatus.InvalidIndex, SoundCatalog.IndexPath), Status(stack));
        Write("data/sound/snd_00index.mes", Mes());
        using (var stack = Stack()) Assert.AreEqual((SoundCatalogStatus.InvalidIndex, SoundCatalog.IndexPath), Status(stack), "the original refuses an empty index");

        Write("data/sound/snd_00index.mes", Mes((1, "snd_a.mes")));
        File.Delete(Path.Combine(_root, "data", "sound", "SchemeList.mes"));
        using (var stack = Stack()) Assert.AreEqual((SoundCatalogStatus.MissingFile, SoundCatalog.SchemeListPath), Status(stack));
    }

    [TestMethod]
    public void An_index_entry_numbered_zero_is_skipped_and_reported()
    {
        Base();
        Write("data/sound/snd_00index.mes", Mes((0, "snd_b.mes"), (1, "snd_a.mes")));
        using var stack = Stack();
        var catalog = Open(stack);
        CollectionAssert.AreEqual(new[] { "snd_a.mes" }, catalog.Tables.Select(t => t.Name).ToArray());
        Assert.AreEqual(SoundIssueCode.IndexEntryZeroSkipped, catalog.Issues.Single(i => i.Code != SoundIssueCode.UserTableMissing).Code);
    }

    [TestMethod]
    public void Missing_user_table_and_parameters_are_issues_not_refusals()
    {
        Base(positional: null);
        using var stack = Stack();
        var catalog = Open(stack);
        CollectionAssert.AreEquivalent(new[] { SoundIssueCode.UserTableMissing, SoundIssueCode.PositionalParametersMissing }, catalog.Issues.Select(i => i.Code).ToArray());
        Assert.IsNull(catalog.UserTable);
        Assert.AreSame(SoundPositionalProfile.WithoutFile, catalog.Positional);
        Assert.AreEqual((0L, 0L, 0), (catalog.Positional.Small.MinRadius, catalog.Positional.Small.MaxRadius, catalog.Positional.Small.MaxVolumePercent), "silent without the file");
        Assert.AreEqual((150L, 800L, 100), (catalog.Positional.Large.MinRadius, catalog.Positional.Large.MaxRadius, catalog.Positional.Large.MaxVolumePercent));
    }

    [TestMethod]
    public void Positional_keys_follow_the_original_and_key_12_is_the_small_volume()
    {
        Base(positional: "{2}{900}{3}{100}{12}{33}{22}{66}{31}{2000}{11}{40x}");
        using var stack = Stack();
        var p = Open(stack).Positional;
        Assert.IsTrue(p.FromFile);
        Assert.AreEqual(new SoundSizeParameters(50, 40, 33), p.Small, "key 11 '40x' reads as 40 like atoi; key 12 is the small volume");
        Assert.AreEqual(new SoundSizeParameters(50, 400, 66), p.Medium, "key 22 sets the medium volume only");
        Assert.AreEqual(new SoundSizeParameters(150, 900, 100), p.Large);
        Assert.AreEqual(new SoundSizeParameters(50, 2000, 100), p.ExtraLarge);
        Assert.AreEqual((100L, 400L), (p.MinPanDistance, p.MaxPanDistance));
        Assert.AreEqual(22, p.Large.RangeTiles);
        Assert.AreEqual(1, p.Small.RangeTiles);
        Assert.AreSame(p.Medium, p[SoundSourceSize.Medium]);
    }

    [TestMethod]
    public void Scheme_lookup_statuses()
    {
        Base();
        Write("data/sound/SchemeIndex.mes", Mes((1, "M.Test #100"), (3, "<<No Sound>>"), (4, "A.Bad #0"), (5, "A.Big #300"), (6, "A.Ok #400")));
        Write("data/sound/SchemeList.mes", Mes(Enumerable.Range(300, 26).Select(n => (n, "x.wav")).Concat(Enumerable.Range(400, 25).Select(n => (n, "x.wav")))
            .Append((100, "music\\song.mp3 /loop")).ToArray()));
        using var stack = Stack();
        var catalog = Open(stack);
        Assert.AreEqual(SoundSchemeStatus.None, catalog.Scheme(0).Status);
        Assert.AreEqual(SoundSchemeStatus.NotInIndex, catalog.Scheme(2).Status);
        var silent = catalog.Scheme(3);
        Assert.AreEqual((SoundSchemeStatus.NoListBase, "<<No Sound>>", 0), (silent.Status, silent.Name, silent.Entries.Count));
        Assert.AreEqual(SoundSchemeStatus.Malformed, catalog.Scheme(4).Status);
        Assert.AreEqual(SoundSchemeStatus.TooManyEntries, catalog.Scheme(5).Status, "the 26th record would crash the original");
        Assert.AreEqual((SoundSchemeStatus.Loaded, 25), (catalog.Scheme(6).Status, catalog.Scheme(6).Entries.Count));
        Assert.IsTrue(catalog.Scheme(1).IsMusicName);
        CollectionAssert.AreEqual(new[] { 1, 3, 4, 5, 6 }, catalog.SchemeIds.ToArray());
    }

    [TestMethod]
    public void Scheme_records_parse_with_the_original_grammar()
    {
        Base();
        Write("data/sound/SchemeIndex.mes", Mes((1, "M.All #100")));
        Write("data/sound/SchemeList.mes", Mes(
            (100, "MUSIC\\Song.MP3 /LOOP /vol:50 /time:5-19"),
            (101, "music\\song.mp3 /over /loop /time:3-4"),
            (102, "music\\song.mp3 /anchor"),
            (104, "b20.wav /freq:20 /time:19-0 /bal:10,30 /vol:60-40"),
            (105, "b20.wav /scatter:30"),
            (106, "b20.wav /vol:200 /bal:150-200 /time:-1"),
            (107, "#11 /freq:1000"),
            (108, "#12"),
            (109, "#99"),
            (110, ""),
            (199, "b20.wav"),
            (200, "b20.wav")));
        using var stack = Stack();
        var scheme = Open(stack).Scheme(1);
        Assert.AreEqual((SoundSchemeStatus.Loaded, 100), (scheme.Status, scheme.ListBase));
        var e = scheme.Entries.ToDictionary(x => x.ListNumber);
        CollectionAssert.AreEqual(new[] { 100, 101, 102, 104, 105, 106, 107, 108, 109, 110, 199 }, e.Keys.ToArray(), "gaps skipped, 100 numbers scanned, 200 outside");

        Assert.AreEqual((SoundSchemeEntryKind.LoopSong, "music\\song.mp3", 5, 19, 63, 63), (e[100].Kind, e[100].Resource, e[100].HourStart, e[100].HourEnd, e[100].VolumeMin, e[100].VolumeMax));
        Assert.AreEqual((SoundFileStatus.Found, ContentSourceRole.ModuleDirectory), (e[100].File.Status, e[100].File.Candidate!.Source.Role));
        Assert.AreEqual((SoundSchemeEntryKind.TransitionSong, 0, 23), (e[101].Kind, e[101].HourStart, e[101].HourEnd), "/over wins and /time is not read");
        Assert.AreEqual(SoundSchemeEntryKind.AnchorSong, e[102].Kind);

        var ambient = e[104];
        Assert.AreEqual((SoundSchemeEntryKind.Ambient, 20, 19, 0, true), (ambient.Kind, ambient.Frequency, ambient.HourStart, ambient.HourEnd, ambient.HourWindowReversed));
        Assert.AreEqual((12, 38), (ambient.BalanceLeft, ambient.BalanceRight), "127 × 10 / 100 and 127 × 30 / 100, truncated");
        Assert.AreEqual((76, 76), (ambient.VolumeMin, ambient.VolumeMax), "a max below its min is raised to it");
        Assert.AreEqual((5, 0, 23, 127, 127, 63, 63), (e[199].Frequency, e[199].HourStart, e[199].HourEnd, e[199].VolumeMin, e[199].VolumeMax, e[199].BalanceLeft, e[199].BalanceRight), "defaults");

        Assert.AreEqual((88, 127, 44, 82), (e[105].VolumeMin, e[105].VolumeMax, e[105].BalanceLeft, e[105].BalanceRight), "scatter 30: volume 70–100, balance 35–65");
        Assert.AreEqual((127, 127, 127, 127), (e[106].VolumeMin, e[106].VolumeMax, e[106].BalanceLeft, e[106].BalanceRight), "clamped to 127, balance-left too");
        Assert.AreEqual((-1, 1), (e[106].HourStart, e[106].HourEnd), "a leading minus doubles as the separator");

        Assert.AreEqual((11, SfxStatus.EmptyRecord, SoundFileStatus.Missing, 1000), (e[107].SfxId, e[107].SfxStatus, e[107].File.Status, e[107].Frequency));
        Assert.AreEqual((SfxStatus.Named, "sound/nofile.wav", SoundFileStatus.Missing), (e[108].SfxStatus, e[108].File.Key, e[108].File.Status));
        Assert.AreEqual(SfxStatus.UnknownId, e[109].SfxStatus);
        Assert.AreEqual((SoundSchemeEntryKind.Ambient, SoundFileStatus.InvalidPath), (e[110].Kind, e[110].File.Status), "an empty record still takes a slot");
    }

    [TestMethod]
    public void Values_atoi_cannot_read_and_overlong_resources_make_the_scheme_malformed()
    {
        Base();
        Write("data/sound/SchemeIndex.mes", Mes((1, "A.Big #100"), (2, "A.Long #200")));
        Write("data/sound/SchemeList.mes", Mes((100, "b20.wav /freq:99999999999"), (200, new string('a', 300) + ".wav")));
        using var stack = Stack();
        var catalog = Open(stack);
        StringAssert.Contains(catalog.Scheme(1).Detail, "record 100");
        Assert.AreEqual(SoundSchemeStatus.Malformed, catalog.Scheme(1).Status);
        Assert.AreEqual(SoundSchemeStatus.Malformed, catalog.Scheme(2).Status);
    }

    [TestMethod]
    public void Speech_follows_the_pc_gender_with_a_female_to_male_fallback()
    {
        Base();
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(
            ("sound/speech/00042/v17_m.mp3", [1]), ("sound/speech/00042/v18_f.mp3", [1]), ("sound/speech/00042/v18_m.mp3", [1]), ("sound/speech/00042/v19_f.mp3", [1])));
        using var stack = Stack();
        var catalog = Open(stack);
        var identity = SoundCatalog.SpeechIdentity(42, "17");
        Assert.AreEqual(0x002A0011, identity);

        var fallback = catalog.ResolveSpeech(identity, SpeechGender.Female);
        Assert.AreEqual((SpeechStatus.Found, 42, 17, 2, "sound/speech/00042/v17_m.mp3"), (fallback.Status, fallback.Folder, fallback.Line, fallback.Candidates.Count, fallback.File!.Key));
        Assert.AreEqual("sound/speech/00042/v18_f.mp3", catalog.ResolveSpeech(SoundCatalog.SpeechIdentity(42, "18"), SpeechGender.Female).File!.Key);
        var male = catalog.ResolveSpeech(SoundCatalog.SpeechIdentity(42, "19"), SpeechGender.Male);
        Assert.AreEqual((SpeechStatus.Missing, 1), (male.Status, male.Candidates.Count), "a male PC never falls back to the female file");
        var none = catalog.ResolveSpeech(-1, SpeechGender.Male);
        Assert.AreEqual((SpeechStatus.NoVoice, 0), (none.Status, none.Candidates.Count));

        Assert.AreEqual(-1, SoundCatalog.SpeechIdentity(0, "17"));
        Assert.AreEqual(-1, SoundCatalog.SpeechIdentity(42, null));
        Assert.AreEqual(0x7FFF0000 | 0xFFFF, SoundCatalog.SpeechIdentity(0xFFFF, "131071"), "script masked to 15 bits, line to 16");
        Assert.AreEqual(42 << 16, SoundCatalog.SpeechIdentity(42, "x"), "atoi of a non-number is 0");
    }

    [TestMethod]
    public void Fixed_music_names_and_path_limits()
    {
        Base();
        using var stack = Stack();
        var catalog = Open(stack);
        Assert.AreEqual("music/combat 6.mp3", SoundCatalog.CombatStingerName(6));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SoundCatalog.CombatStingerName(7));
        Assert.AreEqual(SoundFileStatus.Missing, catalog.ResolveSoundFile(SoundCatalog.CombatLoopName).Status);
        Assert.AreEqual(SoundFileStatus.InvalidPath, catalog.ResolveSoundFile("../x.wav").Status);
        Assert.AreEqual(SoundFileStatus.InvalidPath, catalog.ResolveFile("sound/" + new string('a', 260)).Status);
        Assert.AreEqual(SoundFileStatus.InvalidPath, catalog.ResolveSoundFile("café.wav").Status);
        Assert.IsFalse(new SectorSoundRecord(0, 0, 0).AppliesOnEntry);
        Assert.IsTrue(new SectorSoundRecord(0, 0, 29).AppliesOnEntry);
    }

    private (SoundCatalogStatus, string?) Status(ContentStack stack)
    {
        var result = SoundCatalog.Open(stack);
        return (result.Status, result.File);
    }

    private static SoundCatalog Open(ContentStack stack)
    {
        var result = SoundCatalog.Open(stack);
        Assert.AreEqual(SoundCatalogStatus.Ready, result.Status, result.File);
        return result.Catalog!;
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
