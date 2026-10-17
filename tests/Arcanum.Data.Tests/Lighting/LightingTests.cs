using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// Independently authored synthetic rule files and literal expectations (ARC-RENDER-010).
// Expected hourly values are hand-computed below; no production helper computes them.
[TestClass]
public sealed class LightingTests
{
    private static readonly string Sha = new('a', 64);

    private static string Rows(Func<int, string> row) => string.Concat(Enumerable.Range(0, 24).Select(h => $"{{{h}}}{{{row(h)}}}\n"));
    private static LightSchemeTable Parse(string text, int id = 1)
    {
        Assert.AreEqual(LightingFailure.None, LightSchemeParser.TryParse(id, Encoding.ASCII.GetBytes(text), out var table));
        return table!;
    }
    private static LightingFailure Fails(string text) => LightSchemeParser.TryParse(1, Encoding.ASCII.GetBytes(text), out _);

    [TestMethod]
    public void Explicit_rows_are_outdoor_then_indoor_per_hour()
    {
        var table = Parse(Rows(h => $"{h}, {h + 1}, {h + 2}, {200 + h}, {201 + h}, {202 + h}"), id: 7);
        Assert.AreEqual(7, table.SchemeId);
        Assert.AreEqual(24, table.Hours.Count);
        Assert.AreEqual(new AmbientPair(new(5, 6, 7), new(205, 206, 207)), table.At(5));
        Assert.AreEqual(new AmbientPair(new(23, 24, 25), new(223, 224, 225)), table.At(23));
        Assert.IsTrue(table.ExplicitHours.All(value => value));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.At(24));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.At(-1));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AmbientPair>)table.Hours)[0] = default);
        Assert.AreEqual(64, table.PayloadSha256.Length);
    }

    [TestMethod]
    public void Interior_gaps_use_their_own_anchors_and_truncated_steps()
    {
        // Anchors: h0=0, h4=10 (step trunc(10/4)=2), h7=0 (step trunc(-10/3)=-3), h23=255 (step trunc(255/16)=15).
        var text = Rows(h => h switch
        {
            0 => "0,0,0,0,0,0", 4 => "10,10,10,0,0,0", 7 => "0,0,0,9,9,9", 23 => "255,255,255,9,9,9", _ => "",
        });
        var table = Parse(text);
        int[] outdoor = [0, 2, 4, 6, 10, 7, 4, 0, 15, 30, 45, 60, 75, 90, 105, 120, 135, 150, 165, 180, 195, 210, 225, 255];
        int[] indoor = [0, 0, 0, 0, 0, 3, 6, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9];
        for (var hour = 0; hour < 24; hour++)
        {
            Assert.AreEqual(outdoor[hour], table.At(hour).Outdoor.R, $"outdoor {hour}");
            Assert.AreEqual(outdoor[hour], table.At(hour).Outdoor.B, $"outdoor {hour}");
            Assert.AreEqual(indoor[hour], table.At(hour).Indoor.G, $"indoor {hour}");
        }
        Assert.AreEqual(4, table.ExplicitHours.Count(value => value));
    }

    [TestMethod]
    public void Scheme_files_are_read_with_the_original_mes_grammar()
    {
        // ARC-DATA-027: text outside braces (comments, hour notes, anything else) is ignored as by the original.
        var text = "//\n// header\n//\n\n" + string.Concat(Enumerable.Range(0, 24).Select(h =>
            $"{{{h}}}{{{(h is 0 or 23 ? " 1, 2,3 ,4,5,6 " : "")}}}\t{(h % 12 == 0 ? 12 : h % 12)}{(h < 12 ? "AM" : "PM")} // note\r\n"));
        Assert.AreEqual(new AmbientPair(new(1, 2, 3), new(4, 5, 6)), Parse(text).At(11));
        Assert.AreEqual(LightingFailure.None, Fails(text.Replace("{5}{}\t5AM", "{5}{}\t13PM")));
        Assert.AreEqual(LightingFailure.None, Fails(text + "junk } text"));
        // The shapes the original only warns about, truncates or stops on stay refused (docs/formats/mes-reader.md).
        Assert.AreEqual(LightingFailure.MesGrammar, Fails(text + "{5}{}"), "duplicate number");
        Assert.AreEqual(LightingFailure.MesGrammar, Fails(text + "{x}{}"), "not a number");
        Assert.AreEqual(LightingFailure.MesGrammar, Fails(text + "{30}{unterminated"));
        Assert.AreEqual(LightingFailure.MesGrammar, Fails(text + "{30}{a{b}"), "brace in a field");
        Assert.AreEqual(LightingFailure.MesGrammar, Fails(text + "\r{30}{1}"), "lone carriage return");
        Assert.AreEqual(LightingFailure.LimitExceeded, Fails(text + "{30}{" + new string('a', 2000) + "}"), "text over 1,999 bytes");
        Assert.AreEqual(LightingFailure.LimitExceeded, LightSchemeParser.TryParse(1, new byte[(1 << 20) + 1], out _));
        // A multi-line row is kept as the original reads it, and is then not six channels.
        Assert.AreEqual(LightingFailure.ChannelCount, Fails(text.Replace("{11}{}", "{11}{1,2,3\n4,5,6}")));
    }

    [TestMethod]
    public void Missing_edge_and_malformed_rows_fail_without_a_guessed_palette()
    {
        var full = Rows(_ => "1,2,3,4,5,6");
        Assert.AreEqual(LightingFailure.MissingHour, Fails(full.Replace("{12}{1,2,3,4,5,6}\n", "")));
        Assert.AreEqual(LightingFailure.EdgeHourEmpty, Fails(full.Replace("{0}{1,2,3,4,5,6}", "{0}{}")));
        Assert.AreEqual(LightingFailure.EdgeHourEmpty, Fails(full.Replace("{23}{1,2,3,4,5,6}", "{23}{ }")));
        Assert.AreEqual(LightingFailure.ChannelCount, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,4,5}")));
        Assert.AreEqual(LightingFailure.ChannelCount, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,4,5,6,7}")));
        Assert.AreEqual(LightingFailure.ChannelRange, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,4,5,256}")));
        Assert.AreEqual(LightingFailure.ChannelRange, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,-1,5,6}")));
        Assert.AreEqual(LightingFailure.ChannelRange, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,x,5,6}")));
        Assert.AreEqual(LightingFailure.ChannelRange, Fails(full.Replace("{9}{1,2,3,4,5,6}", "{9}{1,2,3,,5,6}")));
    }

    [TestMethod]
    public void Map_default_is_one_positive_LightScheme_line()
    {
        static LightingFailure Map(string text, out int value) => AreaLightingReader.MapDefault(Encoding.ASCII.GetBytes(text), out value);
        Assert.AreEqual(LightingFailure.None, Map("LightScheme:3\r\nSoundScheme:1,40\r\n", out var scheme)); Assert.AreEqual(3, scheme);
        Assert.AreEqual(LightingFailure.None, Map("SoundScheme:1\n lightscheme : 12 \n", out scheme)); Assert.AreEqual(12, scheme);
        Assert.AreEqual(LightingFailure.MapDefaultMissing, Map("SoundScheme:1,40\n", out _));
        Assert.AreEqual(LightingFailure.MapDefaultMalformed, Map("LightScheme:2\nLightScheme:2\n", out _));
        Assert.AreEqual(LightingFailure.MapDefaultMalformed, Map("LightScheme:0\n", out _));
        Assert.AreEqual(LightingFailure.MapDefaultMalformed, Map("LightScheme:-1\n", out _));
        Assert.AreEqual(LightingFailure.MapDefaultMalformed, Map("LightScheme:two\n", out _));
    }

    private static AreaLightingSource Source(IDictionary<long, int> sectors, int mapDefault = 2)
    {
        var schemes = new Dictionary<int, LightSchemeTable>
        {
            [2] = Parse(Rows(h => $"{h},0,0,0,{h},0"), id: 2),
            [5] = Parse(Rows(h => $"0,{h},0,{h},0,0"), id: 5),
        };
        return new AreaLightingSource(Sha, 1, mapDefault, Sha, sectors, schemes, new StartupClockDeclaration(1885, 15, Sha));
    }

    [TestMethod]
    public void Sector_sentinel_selects_map_default_and_hourly_selection_changes_only_on_hour_or_scheme()
    {
        var source = Source(new Dictionary<long, int> { [10] = 0, [11] = 5, [12] = 2 });
        Assert.AreEqual(LightingFailure.None, source.TryResolveScheme(10, out var id, out var fromDefault)); Assert.AreEqual((2, true), (id, fromDefault));
        Assert.AreEqual(LightingFailure.None, source.TryResolveScheme(11, out id, out fromDefault)); Assert.AreEqual((5, false), (id, fromDefault));
        Assert.AreEqual(LightingFailure.None, source.TryResolveScheme(12, out id, out fromDefault)); Assert.AreEqual((2, false), (id, fromDefault));
        Assert.AreEqual(LightingFailure.SectorNotAdmitted, source.TryResolveScheme(13, out _, out _));

        var state = new AmbientLightingState(source);
        Assert.IsNull(state.Current);
        Assert.AreEqual(LightingFailure.None, state.Apply(15, 10, out var changed)); Assert.IsTrue(changed);
        Assert.AreEqual(new AmbientSelection(2, true, 10, 15, new(new(15, 0, 0), new(0, 15, 0))), state.Current);
        Assert.AreEqual(1, state.Revision);
        Assert.AreEqual(LightingFailure.None, state.Apply(15, 10, out changed)); Assert.IsFalse(changed); Assert.AreEqual(1, state.Revision);
        // Same scheme in another sector: no visible change, no new revision.
        Assert.AreEqual(LightingFailure.None, state.Apply(15, 12, out changed)); Assert.IsFalse(changed); Assert.AreEqual(1, state.Revision);
        Assert.AreEqual(12, state.Current!.Value.Sector);
        Assert.AreEqual(LightingFailure.None, state.Apply(15, 11, out changed)); Assert.IsTrue(changed); Assert.AreEqual(2, state.Revision);
        Assert.AreEqual(new AmbientPair(new(0, 15, 0), new(15, 0, 0)), state.Current!.Value.Pair);
        Assert.AreEqual(LightingFailure.None, state.Apply(0, 11, out changed)); Assert.IsTrue(changed); Assert.AreEqual(3, state.Revision);
        var before = state.Current;
        Assert.AreEqual(LightingFailure.SectorNotAdmitted, state.Apply(1, 99, out changed)); Assert.IsFalse(changed);
        Assert.AreEqual(LightingFailure.InvalidHour, state.Apply(24, 11, out changed)); Assert.IsFalse(changed);
        Assert.AreEqual(LightingFailure.InvalidHour, state.Apply(-1, 11, out _));
        Assert.AreEqual(before, state.Current); Assert.AreEqual(3, state.Revision);
    }

    [TestMethod]
    public void Source_rejects_unresolvable_or_inconsistent_inputs()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Source(new Dictionary<long, int> { [10] = 3 }));
        Assert.ThrowsExactly<ArgumentException>(() => Source(new Dictionary<long, int> { [10] = -1 }));
        Assert.ThrowsExactly<ArgumentException>(() => Source(new Dictionary<long, int> { [10] = 0 }, mapDefault: 0));
        Assert.ThrowsExactly<ArgumentException>(() => Source(new Dictionary<long, int>()));
        var table = Parse(Rows(_ => "1,1,1,1,1,1"), id: 2);
        Assert.ThrowsExactly<ArgumentException>(() => new AreaLightingSource(Sha, 1, 2, Sha, new Dictionary<long, int> { [1] = 0 },
            new Dictionary<int, LightSchemeTable> { [3] = table }, new StartupClockDeclaration(1885, 15, Sha)));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaLightingSource("A" + Sha[1..], 1, 2, Sha, new Dictionary<long, int> { [1] = 0 },
            new Dictionary<int, LightSchemeTable> { [2] = table }, new StartupClockDeclaration(1885, 15, Sha)));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaLightingSource(Sha, 1, 2, Sha, new Dictionary<long, int> { [1] = 0 },
            new Dictionary<int, LightSchemeTable> { [2] = table }, new StartupClockDeclaration(1885, 24, Sha)));
    }

    // ---- Reader over a synthetic module archive and a strict-shaped snapshot ----

    private const long CoreA = 86570436012, CoreB = 86570436013, Context = 86637544876;
    private const string GameInit = "//\n// Game Info\n//\n{0}{Fixture}\t// name\n{4}{1885}\t\t// year\n{5}{15}\t\t// hour\n{10}{2}\n";
    private const string MapList = "// maps\n\t// comment\n{1}{Test-Map, 92958,82592, Type: START_MAP, WorldMap: 0}\n{2}{Other, 1, 2}\n";
    private const string Catalogue = "{0}{<map default>}\n{2}{Lighting Two}\n{5}{Lighting Five}\n";

    private sealed record Fixture(byte[] Module, AreaSnapshot Snapshot);

    private static Fixture Build(Dictionary<string, byte[]>? replace = null, int schemeB = 5, int placeholderB = 0xAA0003,
        Func<AreaSectorRecord, AreaSectorRecord>? edit = null, (string Name, byte[] Bytes)[]? extra = null, bool strict = true)
    {
        var files = new List<(string Name, byte[] Bytes)>
        {
            ($"maps/Test-Map/{CoreA}.sec", SectorBytes(0, 0xAA0003)),
            ($"maps/Test-Map/{CoreB}.sec", SectorBytes(schemeB, placeholderB)),
            ($"maps/Test-Map/{Context}.sec", SectorBytes(-9, 0xAA0003)),
            ("rules/gameinit.mes", Encoding.ASCII.GetBytes(GameInit)),
            ("rules/maplist.mes", Encoding.ASCII.GetBytes(MapList)),
            ("maps/Test-Map/mapinfo.txt", Encoding.ASCII.GetBytes("LightScheme:2\r\nSoundScheme:1,40\r\n")),
            ("rules/lighting schemes.mes", Encoding.ASCII.GetBytes(Catalogue)),
            ("Rules/Lighting Two.mes", Encoding.ASCII.GetBytes(Rows(h => $"{h},{h},{h},1,1,1"))),
            ("rules/lighting five.mes", Encoding.ASCII.GetBytes(Rows(h => $"5,5,5,{h},{h},{h}"))),
        };
        if (replace is not null)
            for (var i = 0; i < files.Count; i++)
                if (replace.TryGetValue(files[i].Name, out var bytes)) files[i] = (files[i].Name, bytes);
        files.RemoveAll(file => file.Bytes.Length == 0);
        if (extra is not null) files.AddRange(extra);
        // Sorted as an original archive index (lower-case, '/' and '\\' alike), which the content stack requires.
        var module = Dat(files.OrderBy(file => file.Name.ToLowerInvariant().Replace('\\', '/'), StringComparer.Ordinal).ToArray());
        using var archive = DatArchive.Open(new MemoryStream(module));
        var key = new AreaInputKey(AreaSourceRole.Module);
        var sectors = new List<AreaSectorRecord>();
        foreach (var (packed, scope) in new[] { (CoreA, AreaScope.Core), (CoreB, AreaScope.Core), (Context, AreaScope.Context) })
        {
            var entry = archive.Entries.Single(e => Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).StartsWith($"maps/Test-Map/{packed}.sec", StringComparison.Ordinal));
            var payload = archive.ReadEntry(entry);
            var identity = new AreaEntryIdentity(key, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset, Hash(entry.RawNameBytes.ToArray()));
            var record = new AreaSectorRecord(new(Hash(module), 5051, packed), AreaSectorCoordinate.Unpack(packed), scope, true, entry.Ordinal, 2, 0, 1,
                0xAA0003, 0, [], [], identity, Hash(payload));
            sectors.Add(edit is null ? record : edit(record));
        }
        var plan = new AreaSourcePlan(1, 5051, new(1452, 1290), 2, "maps/test-map", [new AreaInputIdentity(key, module.Length, Hash(module))], []);
        var counts = new AreaReconciliation(3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var snapshot = new AreaSnapshot(new object(), 1, plan, sectors, [], [], [], counts, []);
        if (strict) snapshot.MarkStrictlyPublished();
        return new(module, snapshot);
    }

    private static AreaLightingReadResult Read(Fixture fixture)
    {
        using var archive = DatArchive.Open(new MemoryStream(fixture.Module));
        return AreaLightingReader.Read(archive, fixture.Snapshot);
    }

    [TestMethod]
    public void Reader_binds_core_sector_words_map_default_schemes_and_startup()
    {
        var fixture = Build();
        var result = Read(fixture);
        Assert.AreEqual(LightingFailure.None, result.Failure);
        var source = result.Source!;
        Assert.AreEqual(Hash(fixture.Module), source.ModuleSha256);
        Assert.AreEqual(1, source.CatalogueMap);
        Assert.AreEqual(2, source.MapDefaultScheme);
        CollectionAssert.AreEqual(new[] { CoreA, CoreB }, source.SectorSchemes.Keys.ToArray(), "context sectors are not admitted");
        Assert.AreEqual(0, source.SectorSchemes[CoreA]); Assert.AreEqual(5, source.SectorSchemes[CoreB]);
        CollectionAssert.AreEqual(new[] { 2, 5 }, source.Schemes.Keys.ToArray());
        Assert.AreEqual(new AmbientPair(new(9, 9, 9), new(1, 1, 1)), source.Schemes[2].At(9));
        Assert.AreEqual(new AmbientPair(new(5, 5, 5), new(9, 9, 9)), source.Schemes[5].At(9));
        Assert.AreEqual(new StartupClockDeclaration(1885, 15, Hash(Encoding.ASCII.GetBytes(GameInit))), source.Startup);
        Assert.AreEqual(Hash(Encoding.ASCII.GetBytes(Rows(h => $"{h},{h},{h},1,1,1"))), source.Schemes[2].PayloadSha256);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<long, int>)source.SectorSchemes)[CoreA] = 5);
    }

    [TestMethod]
    public void Reader_fails_closed_on_missing_ambiguous_or_unbound_inputs()
    {
        static Dictionary<string, byte[]> With(string name, string text) => new() { [name] = Encoding.ASCII.GetBytes(text) };
        static Dictionary<string, byte[]> Without(string name) => new() { [name] = [] };
        Assert.AreEqual(LightingFailure.MissingResource, Read(Build(Without("rules/lighting five.mes"))).Failure);
        Assert.AreEqual(LightingFailure.MissingResource, Read(Build(Without("rules/gameinit.mes"))).Failure);
        Assert.AreEqual(LightingFailure.MissingResource, Read(Build(Without("maps/Test-Map/mapinfo.txt"))).Failure);
        Assert.AreEqual(LightingFailure.AmbiguousResource, Read(Build(extra: [("RULES\\LIGHTING FIVE.MES", Encoding.ASCII.GetBytes(Rows(_ => "1,1,1,1,1,1")))])).Failure);
        Assert.AreEqual(LightingFailure.SchemeNotCatalogued, Read(Build(With("rules/lighting schemes.mes", "{2}{Lighting Two}"))).Failure);
        Assert.AreEqual(LightingFailure.UnsafeResourceName, Read(Build(With("rules/lighting schemes.mes", "{2}{Lighting Two}{5}{../Lighting Five}"))).Failure);
        Assert.AreEqual(LightingFailure.UnsafeResourceName, Read(Build(With("rules/maplist.mes", "{1}{maps/Test-Map, 1, 2}"))).Failure);
        Assert.AreEqual(LightingFailure.MapNotCatalogued, Read(Build(With("rules/maplist.mes", "{2}{Test-Map, 1, 2}"))).Failure);
        Assert.AreEqual(LightingFailure.MapDefaultMissing, Read(Build(With("maps/Test-Map/mapinfo.txt", "SoundScheme:1"))).Failure);
        Assert.AreEqual(LightingFailure.StartupMalformed, Read(Build(With("rules/gameinit.mes", "{4}{1885}{5}{24}"))).Failure);
        Assert.AreEqual(LightingFailure.StartupMalformed, Read(Build(With("rules/gameinit.mes", "{4}{1885}"))).Failure);
        Assert.AreEqual(LightingFailure.EdgeHourEmpty, Read(Build(With("rules/lighting five.mes", Rows(h => h == 0 ? "" : "1,1,1,1,1,1")))).Failure);
        Assert.AreEqual(LightingFailure.SectorSchemeNegative, Read(Build(schemeB: -1)).Failure);
        Assert.AreEqual(LightingFailure.SectorMetadataAbsent, Read(Build(placeholderB: 0xAA0002)).Failure);
        Assert.AreEqual(LightingFailure.SectorIdentityMismatch, Read(Build(edit: r => r.Scope == AreaScope.Core ? r with { PayloadSha256 = Sha } : r)).Failure);
        Assert.AreEqual(LightingFailure.SectorIdentityMismatch, Read(Build(edit: r => r.Entry is { } e && r.Scope == AreaScope.Core ? r with { Entry = e with { Offset = e.Offset + 1 } } : r)).Failure);
        Assert.AreEqual(LightingFailure.SectorIdentityMismatch, Read(Build(edit: r => r.Entry is { } e && r.Scope == AreaScope.Core ? r with { Entry = e with { Ordinal = 999 } } : r)).Failure);
        Assert.AreEqual(LightingFailure.SectorIdentityMismatch, Read(Build(edit: r => r.Entry is { } e && r.Scope == AreaScope.Core ? r with { Entry = e with { NameSha256 = Sha } } : r)).Failure);
        Assert.AreEqual(LightingFailure.SectorNotAdmitted, Read(Build(edit: r => r with { Scope = AreaScope.Context })).Failure);
        Assert.AreEqual(LightingFailure.SnapshotNotStrict, Read(Build(strict: false)).Failure);
        // A catalogue row naming another map directory must not lend its default to these sectors.
        Assert.AreEqual(LightingFailure.MapDirectoryMismatch, Read(Build(With("rules/maplist.mes", "{1}{Other, 1, 2}"),
            extra: [("maps/Other/mapinfo.txt", Encoding.ASCII.GetBytes("LightScheme:5"))])).Failure);
    }

    // ---- ARC-DATA-027: the same reader over a content stack ----

    /// <summary>A synthetic installation: tig.dat, the fixture's module archive as <c>modules/Mod.dat</c>, then the given loose
    /// files (relative to the game folder) and base archives.</summary>
    private static AreaLightingReadResult ReadStack(Fixture fixture, (string Path, byte[] Bytes)[]? loose = null, (string Name, byte[] Bytes)[]? arcanum1 = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-light-stack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "modules"));
            File.WriteAllBytes(Path.Combine(root, "tig.dat"), Dat());
            File.WriteAllBytes(Path.Combine(root, "modules", "Mod.dat"), fixture.Module);
            if (arcanum1 is not null) File.WriteAllBytes(Path.Combine(root, "arcanum1.dat"), Dat(arcanum1));
            foreach (var (relative, bytes) in loose ?? [])
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            var discovery = Arcanum.Data.Content.ContentMountPlan.Discover(root, "Mod");
            Assert.AreEqual(Arcanum.Data.Content.ContentMountStatus.Ready, discovery.Status, discovery.Detail);
            var opened = Arcanum.Data.Content.ContentStack.Open(discovery.Plan!);
            Assert.AreEqual(Arcanum.Data.Content.ContentStackStatus.Ready, opened.Status, opened.Status + " " + opened.Detail);
            using var stack = opened.Stack!;
            return AreaLightingReader.Read(stack, fixture.Snapshot);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Stack_reader_reads_what_the_module_archive_holds_and_follows_overrides()
    {
        var fixture = Build();
        var archive = Read(fixture).Source!;
        var stacked = ReadStack(fixture);
        Assert.AreEqual(LightingFailure.None, stacked.Failure);
        var source = stacked.Source!;
        Assert.AreEqual((archive.ModuleSha256, archive.MapDefaultScheme, archive.MapInfoSha256, archive.Startup), (source.ModuleSha256, source.MapDefaultScheme, source.MapInfoSha256, source.Startup));
        CollectionAssert.AreEqual(archive.SectorSchemes.ToArray(), source.SectorSchemes.ToArray());
        CollectionAssert.AreEqual(archive.Schemes.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray(), source.Schemes.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray());

        // Every lighting file is read after the module is mounted: a module-folder copy wins over the archive.
        var five = Encoding.ASCII.GetBytes(Rows(h => $"7,7,7,{h},{h},{h}"));
        var overridden = ReadStack(fixture, [("modules/Mod/rules/lighting five.mes", five), ("modules/Mod/maps/test-map/mapinfo.txt", "LightScheme:5\n"u8.ToArray())]);
        Assert.AreEqual(LightingFailure.None, overridden.Failure);
        Assert.AreEqual((5, Hash(five)), (overridden.Source!.MapDefaultScheme, overridden.Source.Schemes[5].PayloadSha256));
        CollectionAssert.AreEqual(new[] { 5 }, overridden.Source.Schemes.Keys.ToArray(), "both sectors now use scheme 5");

        // A file only a base archive holds is read; a file nothing holds is missing.
        var moved = Build(new() { ["rules/lighting five.mes"] = [] });
        Assert.AreEqual(LightingFailure.MissingResource, ReadStack(moved).Failure);
        Assert.AreEqual(LightingFailure.None, ReadStack(moved, arcanum1: [("rules/lighting five.mes", five)]).Failure);

        // A sector the stack resolves to another source is not the one the snapshot read.
        Assert.AreEqual(LightingFailure.SectorIdentityMismatch, ReadStack(fixture, [($"modules/Mod/maps/test-map/{CoreB}.sec", SectorBytes(5, 0xAA0003))]).Failure);
        Assert.AreEqual(LightingFailure.MapDirectoryMismatch, ReadStack(Build(new() { ["rules/maplist.mes"] = Encoding.ASCII.GetBytes("{1}{Other, 1, 2}") },
            extra: [("maps/Other/mapinfo.txt", Encoding.ASCII.GetBytes("LightScheme:5"))])).Failure);
    }

    // Selected original module (opt-in): numeric identities from the ARC-RES-018/RES-020 research
    // receipts, never colour values. Requires the ordinary ARC_AREA_* inputs.
    [TestMethod, TestCategory("RealData")]
    public void Original_crash_site_core_resolves_map_default_scheme_one_at_startup_fifteen()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module)) Assert.Inconclusive("Opt-in original inputs are required.");
        using var owner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var snapshot = owner.LoadStrict();
        using var archive = DatArchive.Open(module);
        var result = AreaLightingReader.Read(archive, snapshot);
        Assert.AreEqual(LightingFailure.None, result.Failure);
        var source = result.Source!;
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", source.ModuleSha256);
        Assert.AreEqual(5000, source.CatalogueMap);
        Assert.AreEqual(1, source.MapDefaultScheme);
        Assert.AreEqual(9, source.SectorSchemes.Count);
        Assert.IsTrue(source.SectorSchemes.Values.All(raw => raw == 0), "all core sectors use the map-default sentinel");
        CollectionAssert.AreEqual(new[] { 1 }, source.Schemes.Keys.ToArray());
        Assert.AreEqual("ede40699a2e250d37b3748d255f7535561633c0bd8ec3de6229a157bedf07a0d", source.Schemes[1].PayloadSha256);
        Assert.AreEqual(9, source.Schemes[1].ExplicitHours.Count(value => value));
        Assert.AreEqual(new StartupClockDeclaration(1885, 15, "e098286005ba4c4a16701cd6cbcf74eca2bc8937d9f45c7d9ef6331000c7d670"), source.Startup);
        Assert.IsTrue(source.Schemes[1].Hours.Any(pair => pair.Outdoor != pair.Indoor), "distinct indoor/outdoor channels");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Explicit opt-in input is missing.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] SectorBytes(int scheme, int placeholder)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0);
        for (var i = 0; i < 4096; i++) writer.Write(0x100u);
        writer.Write(1); writer.Write(placeholder);
        writer.Write(0);
        if (placeholder >= 0xAA0002) writer.Write(new byte[12]);
        if (placeholder >= 0xAA0003) { writer.Write(2); writer.Write(0); writer.Write(scheme); writer.Write(new byte[12]); }
        writer.Write(0);
        return stream.ToArray();
    }

    private static byte[] Dat(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>();
        foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Bytes); }
        using var index = new MemoryStream(); U32(index, checked((uint)entries.Length));
        foreach (var (entry, i) in entries.Select((entry, i) => (entry, i)))
        {
            var name = Encoding.ASCII.GetBytes(entry.Name + "\0");
            U32(index, checked((uint)name.Length)); index.Write(name); index.Write(new byte[4]); U32(index, 1);
            U32(index, checked((uint)entry.Bytes.Length)); U32(index, checked((uint)entry.Bytes.Length)); U32(index, offsets[i]);
        }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8);
        U32(output, checked((uint)entries.Sum(e => e.Name.Length + 1))); U32(output, checked((uint)(index.Length + 28)));
        return output.ToArray();
    }
    private static void U32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
}
