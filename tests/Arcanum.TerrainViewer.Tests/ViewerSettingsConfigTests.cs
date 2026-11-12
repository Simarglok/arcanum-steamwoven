using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.Session.Launcher;
using Arcanum.Session.Settings;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1130: the viewer's player settings as lines of the one settings file (<c>arcanum.cfg</c>) and the move of the old files into it.</summary>
[TestClass]
public sealed class ViewerSettingsConfigTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-viewer-cfg-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static ArcanumConfigRead Lines(string text) => ArcanumConfig.Parse(text);

    private static readonly AreaGameSettings Chosen = new(2, true, false)
    {
        Floats = 0, FloatSpeed = 9, HdInterface = true, GeneratedInterface = true, HdPackSource = HdPackSourceMode.Custom, HdPackPath = "/Volumes/Packs/HD pack",
    };

    [TestMethod]
    public void The_original_settings_keep_the_originals_names()
    {
        string Setting(AreaOption option) => AreaOptionTable.Of(option).Setting;
        Assert.AreEqual(ViewerSettingsConfig.Difficulty, Setting(AreaOption.Difficulty));
        Assert.AreEqual(ViewerSettingsConfig.TurnBased, Setting(AreaOption.DefaultCombatMode));
        Assert.AreEqual(ViewerSettingsConfig.AutoAttack, Setting(AreaOption.AutoAttack));
        Assert.AreEqual(ViewerSettingsConfig.AlwaysRun, Setting(AreaOption.AlwaysRun));
        Assert.AreEqual(ViewerSettingsConfig.TextFloaters, Setting(AreaOption.Floats));
        Assert.AreEqual(ViewerSettingsConfig.FloatSpeed, Setting(AreaOption.FloatSpeed));
        Assert.AreEqual(ViewerSettingsConfig.EffectsVolume, Setting(AreaOption.Effects));
        Assert.AreEqual(ViewerSettingsConfig.VoiceVolume, Setting(AreaOption.Voice));
        Assert.AreEqual(ViewerSettingsConfig.MusicVolume, Setting(AreaOption.Music));
    }

    [TestMethod]
    public void The_game_settings_round_trip_as_lines_and_a_missing_file_reads_the_defaults()
    {
        var text = ArcanumConfig.Format(ViewerSettingsConfig.GameChanges(Chosen).Select(change => new ArcanumConfigEntry(change.Name, change.Value!)));
        Assert.AreEqual("difficulty=2\nturn-based=1\nauto attack=0\ntext floaters=0\nfloat speed=9\nhd interface=1\ngenerated interface=1\nhd pack=custom\nhd pack path=/Volumes/Packs/HD pack\n", text);
        var read = Lines(text);
        Assert.AreEqual(Chosen, ViewerSettingsConfig.ReadGame(read));
        Assert.IsTrue(ViewerSettingsConfig.HasGame(read));
        Assert.AreEqual(AreaGameSettings.Defaults, ViewerSettingsConfig.ReadGame(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing)));
        Assert.IsFalse(ViewerSettingsConfig.HasGame(Lines("music volume=3\n")));
    }

    [TestMethod]
    public void A_bad_value_takes_that_settings_default_and_leaves_the_others()
    {
        var read = Lines("difficulty=7\nturn-based=yes\nauto attack=0\ntext floaters=-1\nfloat speed=11\nhd interface=1\ngenerated interface=1\nhd pack=elsewhere\nhd pack path=/x\n");
        var settings = ViewerSettingsConfig.ReadGame(read);
        var defaults = AreaGameSettings.Defaults;
        Assert.AreEqual((defaults.Difficulty, defaults.TurnBased, false, defaults.Floats, defaults.FloatSpeed),
            (settings.Difficulty, settings.TurnBased, settings.AutoAttack, settings.Floats, settings.FloatSpeed));
        Assert.AreEqual((true, true, defaults.HdPackSource, "/x"), (settings.HdInterface, settings.GeneratedInterface, settings.HdPackSource, settings.HdPackPath));
        Assert.IsFalse(ViewerSettingsConfig.ReadGame(Lines("generated interface=1\n")).GeneratedInterface, "the generated interface is on only with the HD interface");
        Assert.AreEqual(HdPackSourceMode.GameFolder, ViewerSettingsConfig.ReadGame(Lines("HD PACK=Game\n")).HdPackSource);
    }

    [TestMethod]
    public void A_folder_path_the_file_cannot_hold_is_not_stored()
    {
        foreach (var path in new[] { " /x", "/x ", "/a\nb", new string('p', AreaGameSettingsCodec.MaxPath + 1) })
        {
            var changes = ViewerSettingsConfig.GameChanges(Chosen with { HdPackPath = path });
            Assert.IsNull(changes.Single(change => change.Name == ViewerSettingsConfig.HdPackPath).Value, path);
        }
        Assert.IsNull(ViewerSettingsConfig.GameChanges(Chosen with { HdPackPath = null }).Single(change => change.Name == ViewerSettingsConfig.HdPackPath).Value);
    }

    [TestMethod]
    public void The_volumes_the_always_run_flag_and_the_profile_are_lines_of_their_own()
    {
        var volumes = new AudioVolumes(2, 9, 0);
        var read = Lines(ArcanumConfig.Format(ViewerSettingsConfig.AudioChanges(volumes).Select(change => new ArcanumConfigEntry(change.Name, change.Value!))));
        Assert.AreEqual(volumes, ViewerSettingsConfig.ReadAudio(read));
        Assert.AreEqual(new AudioVolumes(5, 3, 5), ViewerSettingsConfig.ReadAudio(Lines("voice volume=3\nmusic volume=11\nEffects Volume=x\n")), "each volume is read alone");
        Assert.AreEqual(AudioVolumes.Defaults, ViewerSettingsConfig.ReadAudio(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing)));
        Assert.IsTrue(ViewerSettingsConfig.HasAudio(Lines("voice volume=3\n")));
        Assert.IsFalse(ViewerSettingsConfig.HasAudio(Lines("always run=1\n")));

        Assert.IsTrue(ViewerSettingsConfig.ReadAlwaysRun(Lines("always run=1\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadAlwaysRun(Lines("always run=on\n")), "the original's 0 and 1 only");
        Assert.AreEqual("1", ViewerSettingsConfig.AlwaysRunChanges(true).Single().Value);

        Assert.AreEqual(VisualProfile.Hd, ViewerSettingsConfig.ReadVisualProfile(Lines("visual profile=hd\n")));
        Assert.AreEqual(VisualProfile.Legacy, ViewerSettingsConfig.ReadVisualProfile(Lines("visual profile=sepia\n")));
        Assert.AreEqual(VisualProfile.Legacy, ViewerSettingsConfig.ReadVisualProfile(Lines("")));
        Assert.IsTrue(ViewerSettingsConfig.HasVisualProfile(Lines("visual profile=legacy\n")));
        Assert.AreEqual("hd", ViewerSettingsConfig.VisualProfileChanges(VisualProfile.Hd).Single().Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ViewerSettingsConfig.VisualProfileChanges((VisualProfile)9));
    }

    [TestMethod]
    public void Every_group_and_the_launcher_live_in_one_file_and_a_save_changes_its_own_lines_only()
    {
        var path = Path.Combine(_root, "user", ArcanumConfig.FileName);
        var launcher = LauncherSettings.Defaults with { ShowAtStart = true, Rules = RuleMode.Original };
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.GameChanges(Chosen)));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AudioChanges(new AudioVolumes(1, 2, 3))));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AlwaysRunChanges(true)));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.VisualProfileChanges(VisualProfile.Hd)));
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, launcher));
        CollectionAssert.AreEqual(new[] { ArcanumConfig.FileName }, Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray(), "one file, no other");

        void Check(AreaGameSettings game, AudioVolumes audio, bool run, VisualProfile profile, LauncherSettings stored)
        {
            var read = ArcanumConfig.Read(path);
            Assert.AreEqual(game, ViewerSettingsConfig.ReadGame(read));
            Assert.AreEqual(audio, ViewerSettingsConfig.ReadAudio(read));
            Assert.AreEqual(run, ViewerSettingsConfig.ReadAlwaysRun(read));
            Assert.AreEqual(profile, ViewerSettingsConfig.ReadVisualProfile(read));
            Assert.AreEqual((stored.ShowAtStart, stored.Rules), (LauncherSettingsFile.Load(path).Settings.ShowAtStart, LauncherSettingsFile.Load(path).Settings.Rules));
        }
        Check(Chosen, new AudioVolumes(1, 2, 3), true, VisualProfile.Hd, launcher);

        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AudioChanges(new AudioVolumes(9, 9, 9))));
        Check(Chosen, new AudioVolumes(9, 9, 9), true, VisualProfile.Hd, launcher);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.GameChanges(AreaGameSettings.Defaults)));
        Check(AreaGameSettings.Defaults, new AudioVolumes(9, 9, 9), true, VisualProfile.Hd, launcher);
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Reset(path));
        Check(AreaGameSettings.Defaults, new AudioVolumes(9, 9, 9), true, VisualProfile.Hd, LauncherSettings.Defaults);
        Assert.IsFalse(File.ReadAllText(path).Contains("hd pack path", StringComparison.Ordinal), "a group with no folder holds no folder line");
    }

    [TestMethod]
    public void The_old_files_move_into_the_file_value_for_value()
    {
        var launcher = LauncherSettings.Defaults with { ShowAtStart = true, Rules = RuleMode.Original, DebugMode = true };
        var legacy = new LegacySettingsTexts(AreaGameSettingsCodec.Serialize(Chosen), AudioVolumesCodec.Serialize(new AudioVolumes(4, 0, 10)), "on",
            VisualProfileCodec.Serialize(VisualProfile.Hd), LauncherSettingsCodec.Serialize(launcher));
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsMigration.Import(legacy)));
        var read = ArcanumConfig.Read(path);
        Assert.AreEqual(Chosen, ViewerSettingsConfig.ReadGame(read));
        Assert.AreEqual(new AudioVolumes(4, 0, 10), ViewerSettingsConfig.ReadAudio(read));
        Assert.IsTrue(ViewerSettingsConfig.ReadAlwaysRun(read));
        Assert.AreEqual(VisualProfile.Hd, ViewerSettingsConfig.ReadVisualProfile(read));
        var stored = LauncherSettingsFile.Load(path);
        Assert.AreEqual((LauncherSettingsReadStatus.Read, true, RuleMode.Original, true), (stored.Status, stored.Settings.ShowAtStart, stored.Settings.Rules, stored.Settings.DebugMode));
    }

    [TestMethod]
    public void An_old_file_that_was_invalid_or_absent_adds_no_line_and_a_newer_launcher_object_is_kept()
    {
        Assert.AreEqual(0, ViewerSettingsMigration.Import(new LegacySettingsTexts(null, null, null, null, null)).Count);
        Assert.IsFalse(new LegacySettingsTexts(null, null, null, null, null).Any);
        var broken = new LegacySettingsTexts("{}", "not json", "maybe", null, "garbage");
        Assert.IsTrue(broken.Any);
        Assert.AreEqual(0, ViewerSettingsMigration.Import(broken).Count, "an invalid file keeps its settings at their defaults");

        const string newer = "{\n  \"schema\": 2,\n  \"gameRoot\": \"/x\"\n}";
        var kept = ViewerSettingsMigration.Import(new LegacySettingsTexts(null, null, null, null, newer)).Single();
        Assert.AreEqual(LauncherSettingsFile.Line, kept.Name);
        Assert.AreEqual("{\"schema\":2,\"gameRoot\":\"/x\"}", kept.Value);
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [kept]));
        Assert.AreEqual(LauncherSettingsSaveStatus.RefusedOtherSchema, LauncherSettingsFile.Save(path, LauncherSettings.Defaults), "still a newer build's, still not overwritten");
    }
}
