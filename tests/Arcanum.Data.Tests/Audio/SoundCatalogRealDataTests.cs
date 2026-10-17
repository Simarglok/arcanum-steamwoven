using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Audio;

/// <summary>
/// ARC-DATA-040 opt-in check of the installed original (DAT_ROOT = the game folder, module Arcanum): the catalogue
/// reproduces the census of docs/research/audio-evidence.md for the whole module. Only counts are compared, never
/// original text or audio.
/// </summary>
[TestClass]
public sealed class SoundCatalogRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_catalogue_covers_every_installed_sound_id_scheme_map_sector_and_voice()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var result = SoundCatalog.Open(stack);
        Assert.AreEqual(SoundCatalogStatus.Ready, result.Status, result.File);
        var catalog = result.Catalog!;

        Assert.AreEqual(0, catalog.Issues.Count);
        Assert.AreEqual(6, catalog.Tables.Count);
        Assert.IsTrue(catalog.Tables.All(t => !ContentPhase.IsModule(t.Candidate.Source.Role)));
        Assert.AreEqual(ContentSourceRole.ModuleArchive, catalog.UserTable!.Candidate.Source.Role);
        Assert.IsTrue(catalog.Positional.FromFile);

        var ids = catalog.Tables.Append(catalog.UserTable).SelectMany(t => t.Messages.Messages.Select(m => m.Number)).Distinct().ToArray();
        var sfx = ids.Select(catalog.ResolveSfx).ToArray();
        Assert.AreEqual(834, sfx.Count(s => s.Status == SfxStatus.Named));
        Assert.AreEqual(805, sfx.Count(s => s.Status == SfxStatus.EmptyRecord));
        Assert.AreEqual(27, sfx.Count(s => s.Status == SfxStatus.Named && !s.Playable));
        Assert.AreEqual(ids.Length, sfx.Length, "no id held by two tables");

        var schemes = catalog.SchemeIds.Select(catalog.Scheme).ToArray();
        Assert.AreEqual(60, schemes.Length);
        Assert.AreEqual(58, schemes.Count(s => s.Status == SoundSchemeStatus.Loaded));
        Assert.AreEqual((1, 1), (schemes.Count(s => s.Status == SoundSchemeStatus.None), schemes.Count(s => s.Status == SoundSchemeStatus.NoListBase)));
        Assert.AreEqual((26, 30), (schemes.Count(s => s.IsMusicName), schemes.Count(s => s.IsAmbientName)));
        var entries = schemes.SelectMany(s => s.Entries).ToArray();
        Assert.AreEqual(155, entries.Length, "153 records with text and 2 empty records, which also take a slot");
        Assert.IsTrue(schemes.All(s => s.Entries.Count <= SoundSchemeParser.MaxEntries));
        Assert.AreEqual(85, entries.Count(e => e.Kind == SoundSchemeEntryKind.LoopSong));
        Assert.AreEqual(70, entries.Count(e => e.Kind == SoundSchemeEntryKind.Ambient));
        Assert.AreEqual(20, entries.Count(e => e.HasHourWindow && e.HourWindowReversed));
        Assert.AreEqual(3, entries.Count(e => e.File.Status == SoundFileStatus.Missing));
        Assert.AreEqual(2, entries.Count(e => e.File.Status == SoundFileStatus.InvalidPath));

        var catalogue = WorldCatalog.Open(stack);
        int mapDefaults = 0, pairs = 0, sectorsWithRecord = 0;
        foreach (var map in catalogue.Maps)
        {
            if (map.Info?.MusicScheme is { } music)
            {
                mapDefaults++;
                AssertInIndex(catalog, music, map.Name); AssertInIndex(catalog, map.Info.AmbientScheme ?? 0, map.Name);
            }
            foreach (var coordinate in map.SectorFiles)
            {
                var sector = catalogue.ReadSector(map, coordinate);
                Assert.AreEqual(WorldSectorStatus.Read, sector.Status, $"{map.Name} {coordinate}");
                if (SoundCatalog.SectorSound(sector.Sector!) is not { } record) continue;
                sectorsWithRecord++;
                Assert.AreEqual(0u, record.Flags);
                if (!record.AppliesOnEntry) continue;
                pairs++;
                AssertInIndex(catalog, record.MusicScheme, map.Name); AssertInIndex(catalog, record.AmbientScheme, map.Name);
            }
        }
        Assert.AreEqual((74, 1654, 410), (mapDefaults, sectorsWithRecord, pairs));

        var speech = stack.Keys("sound/speech/").Where(k => !k.StartsWith("sound/speech/slide/", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(2630, speech.Length);
        foreach (var key in speech)
        {
            var name = key.Split('/');
            var (folder, file) = (int.Parse(name[2]), name[3]);
            var line = int.Parse(file[1..file.IndexOf('_')]);
            var gender = file[file.IndexOf('_') + 1] == 'm' ? SpeechGender.Male : SpeechGender.Female;
            var voice = catalog.ResolveSpeech(SoundCatalog.SpeechIdentity(folder, line.ToString()), gender);
            Assert.AreEqual((SpeechStatus.Found, key), (voice.Status, voice.File!.Key));
        }

        foreach (var music in Enumerable.Range(1, SoundCatalog.CombatStingerCount).Select(SoundCatalog.CombatStingerName)
            .Concat([SoundCatalog.CombatLoopName, SoundCatalog.WorldMapTravelMusicName, SoundCatalog.SlideMusicName]))
            Assert.AreEqual((SoundFileStatus.Found, ContentSourceRole.ModuleDirectory), (catalog.ResolveSoundFile(music).Status, catalog.ResolveSoundFile(music).Candidate!.Source.Role), music);
    }

    private static void AssertInIndex(SoundCatalog catalog, int id, string map) =>
        Assert.AreNotEqual(SoundSchemeStatus.NotInIndex, catalog.Scheme(id).Status, $"{map}: scheme {id}");
}
