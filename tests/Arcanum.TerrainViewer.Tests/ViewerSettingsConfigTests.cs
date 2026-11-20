using Arcanum.Presentation.Steamwoven;
using Arcanum.Input;
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
        Floats = 0, FloatSpeed = 9, CombatTaunts = false, AutoSwitchWeapons = true, FollowerSkills = false, ViolenceFilter = true, Brightness = 5, TextDuration = 3, HdInterface = true, GeneratedInterface = true, HdPackSource = HdPackSourceMode.Custom, HdPackPath = "/Volumes/Packs/HD pack",
        Pad = PadScheme.Pointer, Touch = TouchScheme.Off, Prompts = PromptStyle.SteamDeck,
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
        Assert.AreEqual(ViewerSettingsConfig.CombatTaunts, Setting(AreaOption.CombatTaunts));
        Assert.AreEqual(ViewerSettingsConfig.ViolenceFilter, Setting(AreaOption.ViolenceFilter));
        Assert.AreEqual(ViewerSettingsConfig.Brightness, Setting(AreaOption.Brightness));
        Assert.AreEqual(ViewerSettingsConfig.TextDuration, Setting(AreaOption.TextDuration));
        Assert.AreEqual(ViewerSettingsConfig.EffectsVolume, Setting(AreaOption.Effects));
        Assert.AreEqual(ViewerSettingsConfig.VoiceVolume, Setting(AreaOption.Voice));
        Assert.AreEqual(ViewerSettingsConfig.MusicVolume, Setting(AreaOption.Music));
    }

    [TestMethod]
    public void The_removed_interface_layout_line_is_ignored_and_kept()
    {
        // ARC-UI-2926: the column is the only layout; an old file's `interface layout` line (column, full-width, column-thin) is not read and not written, and it no longer makes the game group present.
        Assert.AreEqual(AreaGameSettings.Defaults, ViewerSettingsConfig.ReadGame(Lines("interface layout=full-width\n")));
        Assert.AreEqual(AreaGameSettings.Defaults, ViewerSettingsConfig.ReadGame(Lines("interface layout=Column-Thin\n")));
        Assert.IsFalse(ViewerSettingsConfig.HasGame(Lines("interface layout=column\n")), "the line no longer belongs to the game group");
        Assert.IsFalse(ViewerSettingsConfig.GameChanges(AreaGameSettings.Defaults).Any(change => change.Name == ViewerSettingsConfig.InterfaceLayoutLine), "the line is not written");
    }

    [TestMethod]
    public void The_game_settings_round_trip_as_lines_and_a_missing_file_reads_the_defaults()
    {
        var text = ArcanumConfig.Format(ViewerSettingsConfig.GameChanges(Chosen).Select(change => new ArcanumConfigEntry(change.Name, change.Value!)));
        Assert.AreEqual("difficulty=2\nturn-based=1\nauto attack=0\ntext floaters=0\nfloat speed=9\ncombat taunts=0\nauto switch=1\nfollower skills=0\nviolence filter=1\nbrightness=5\ntext duration=3\nhd=1\ngenerated interface=1\nhd pack=custom\nhd pack path=/Volumes/Packs/HD pack\npad scheme=pointer\ntouch scheme=off\nprompt style=steamdeck\n", text);
        var read = Lines(text);
        Assert.AreEqual(Chosen, ViewerSettingsConfig.ReadGame(read));
        Assert.IsTrue(ViewerSettingsConfig.HasGame(read));
        Assert.AreEqual(AreaGameSettings.Defaults, ViewerSettingsConfig.ReadGame(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing)));
        Assert.IsFalse(ViewerSettingsConfig.HasGame(Lines("music volume=3\n")));
    }

    [TestMethod]
    public void A_bad_value_takes_that_settings_default_and_leaves_the_others()
    {
        var read = Lines("difficulty=7\nturn-based=yes\nauto attack=0\ntext floaters=-1\nfloat speed=11\ncombat taunts=2\nviolence filter=maybe\nbrightness=10\nauto switch=2\nfollower skills=2\nhd interface=1\ngenerated interface=1\nhd pack=elsewhere\nhd pack path=/x\n");
        var settings = ViewerSettingsConfig.ReadGame(read);
        var defaults = AreaGameSettings.Defaults;
        Assert.AreEqual((defaults.Difficulty, defaults.TurnBased, false, defaults.Floats, defaults.FloatSpeed),
            (settings.Difficulty, settings.TurnBased, settings.AutoAttack, settings.Floats, settings.FloatSpeed));
        Assert.IsTrue(settings.CombatTaunts, "an out-of-range combat taunts line reads as the default, on");
        Assert.AreEqual((false, 0), (settings.ViolenceFilter, settings.Brightness), "ARC-UI-1737: a bad violence filter or a brightness of 10 (the factor 2.0 the original refuses) reads as the default, off and 0");
        Assert.IsFalse(settings.AutoSwitchWeapons, "an out-of-range auto switch line reads as the default, off (ARC-COMBAT-1720)");
        Assert.IsTrue(settings.FollowerSkills, "an out-of-range follower skills line reads as the default, on (ARC-COMBAT-1777)");
        Assert.AreEqual((true, true, defaults.HdPackSource, "/x"), (settings.HdInterface, settings.GeneratedInterface, settings.HdPackSource, settings.HdPackPath));
        Assert.IsFalse(ViewerSettingsConfig.ReadGame(Lines("generated interface=1\n")).GeneratedInterface, "the generated interface is on only with the HD interface");
        Assert.AreEqual(HdPackSourceMode.GameFolder, ViewerSettingsConfig.ReadGame(Lines("HD PACK=Game\n")).HdPackSource);
    }

    [TestMethod]
    public void The_controller_scheme_lines_default_to_off_and_a_bad_word_takes_that_lines_default_alone()
    {
        var defaults = ViewerSettingsConfig.ReadGame(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing));
        Assert.AreEqual((PadScheme.Off, TouchScheme.Off, PromptStyle.Auto), (defaults.Pad, defaults.Touch, defaults.Prompts));
        var read = ViewerSettingsConfig.ReadGame(Lines("pad scheme=Direct\ntouch scheme=1\nprompt style=gamecube\ndifficulty=2\n"));
        Assert.AreEqual((PadScheme.Direct, TouchScheme.Off, PromptStyle.Auto, 2), (read.Pad, read.Touch, read.Prompts, read.Difficulty), "words are case-insensitive, a number or an unknown word is the default");
        Assert.AreEqual(PadScheme.Off, ViewerSettingsConfig.ReadGame(Lines("pad scheme=off,direct\n")).Pad, "no list of values");
        Assert.IsTrue(ViewerSettingsConfig.HasGame(Lines("pad scheme=off\n")), "a controller line alone is a saved game setting");
        // An out-of-range value in a record never reaches the file: it is written as the default.
        var bad = ViewerSettingsConfig.GameChanges(AreaGameSettings.Defaults with { Pad = (PadScheme)9, Prompts = (PromptStyle)9 });
        Assert.AreEqual("off", bad.Single(change => change.Name == ViewerSettingsConfig.PadSchemeLine).Value);
        Assert.AreEqual("auto", bad.Single(change => change.Name == ViewerSettingsConfig.PromptStyleLine).Value);
        // Saved through the one file at once and read back after a restart.
        var path = Path.Combine(_root, "user", ArcanumConfig.FileName);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.GameChanges(AreaGameSettings.Defaults with { Pad = PadScheme.Direct, Prompts = PromptStyle.Nintendo })));
        var saved = ViewerSettingsConfig.ReadGame(ArcanumConfig.Read(path));
        Assert.AreEqual((PadScheme.Direct, TouchScheme.Off, PromptStyle.Nintendo), (saved.Pad, saved.Touch, saved.Prompts));
        Assert.AreEqual(ControlSchemeSettings.CurrentVersion, saved.Controls.Version);
        // A file with both a pad scheme and the touch scheme on is C alone (the one Controls line shows C; the pad scheme beside it is dropped).
        var both = ViewerSettingsConfig.ReadGame(Lines("pad scheme=direct\ntouch scheme=touchpad\n"));
        Assert.AreEqual((PadScheme.Off, TouchScheme.TouchPad), (both.Controls.Pad, both.Controls.Touch));
        Assert.IsTrue(saved.Controls.OriginalActive, "the original mouse and keyboard stay live whatever the lines hold");
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

        // ARC-UI-2174: one HD value, the `hd` line (0 or 1); an unknown value reads Off.
        Assert.IsTrue(ViewerSettingsConfig.ReadHd(Lines("hd=1\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("hd=0\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("hd=sepia\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("")));
        Assert.IsTrue(ViewerSettingsConfig.HasHd(Lines("hd=0\n")));
        Assert.IsFalse(ViewerSettingsConfig.HasHd(Lines("visual profile=hd\n")));
        Assert.AreEqual("1", ViewerSettingsConfig.HdChanges(true).Single().Value);
        Assert.AreEqual("hd", ViewerSettingsConfig.HdChanges(false).Single().Name);

        // The migration of an older file: with no `hd` line the earlier `hd interface` line is the value (On only when it was 1); `visual profile` is ignored, so HD graphics alone never turns HD on;
        // an `hd` line wins over the earlier ones; the old lines are neither read nor changed.
        Assert.IsTrue(ViewerSettingsConfig.ReadHd(Lines("hd interface=1\n")));
        Assert.IsTrue(ViewerSettingsConfig.ReadHd(Lines("hd interface=1\nvisual profile=legacy\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("hd interface=0\nvisual profile=hd\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("visual profile=hd\n")));
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("hd=0\nhd interface=1\nvisual profile=hd\n")), "the hd line wins");
        Assert.IsTrue(ViewerSettingsConfig.ReadHd(Lines("hd=1\nhd interface=0\n")));
        Assert.IsTrue(ViewerSettingsConfig.HasHd(Lines("hd interface=0\n")), "an old file with only hd interface holds a saved value");
        Assert.IsTrue(ViewerSettingsConfig.ReadGame(Lines("hd interface=1\ngenerated interface=1\n")) is { HdInterface: true, GeneratedInterface: true });
        Assert.IsTrue(ViewerSettingsConfig.ReadGame(Lines("visual profile=hd\ngenerated interface=1\n")) is { HdInterface: false, GeneratedInterface: false }, "generated is honoured only while HD is On");
        Assert.IsFalse(ViewerSettingsConfig.GameChanges(Chosen).Any(change => change.Name == ViewerSettingsConfig.HdInterface), "the old line is never written again");
        var oldPath = Path.Combine(_root, "old", ArcanumConfig.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "visual profile=hd\nhd interface=1\nrelit look=1\n");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(oldPath, ViewerSettingsConfig.GameChanges(ViewerSettingsConfig.ReadGame(ArcanumConfig.Read(oldPath)))));
        var migrated = File.ReadAllText(oldPath);
        Assert.IsTrue(migrated.Contains("hd=1", StringComparison.Ordinal) && migrated.Contains("visual profile=hd", StringComparison.Ordinal) && migrated.Contains("hd interface=1", StringComparison.Ordinal)
            && migrated.Contains("relit look=1", StringComparison.Ordinal), "the first save writes hd and leaves the old lines in place: " + migrated);

        // ARC-RENDER-2076: the relit look is a part of HD, not a setting; the `relit look` line of an older file is ignored.
        var old = Lines("relit look=1\nvisual profile=legacy\ndifficulty=2\n");
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(old));
        Assert.AreEqual(ViewerSettingsConfig.ReadGame(Lines("difficulty=2\n")).Difficulty, ViewerSettingsConfig.ReadGame(old).Difficulty);
        Assert.IsFalse(ViewerSettingsConfig.ReadHd(Lines("relit look=1\n")), "the old line alone never turns HD on");
    }

    [TestMethod]
    public void Every_group_and_the_launcher_live_in_one_file_and_a_save_changes_its_own_lines_only()
    {
        var path = Path.Combine(_root, "user", ArcanumConfig.FileName);
        var launcher = LauncherSettings.Defaults with { ShowAtStart = true, Rules = RuleMode.Original };
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.GameChanges(Chosen)));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AudioChanges(new AudioVolumes(1, 2, 3))));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AlwaysRunChanges(true)));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.HdChanges(true)));
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Save(path, launcher));
        CollectionAssert.AreEqual(new[] { ArcanumConfig.FileName }, Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray(), "one file, no other");

        void Check(AreaGameSettings game, AudioVolumes audio, bool run, bool hd, LauncherSettings stored)
        {
            var read = ArcanumConfig.Read(path);
            Assert.AreEqual(game, ViewerSettingsConfig.ReadGame(read));
            Assert.AreEqual(audio, ViewerSettingsConfig.ReadAudio(read));
            Assert.AreEqual(run, ViewerSettingsConfig.ReadAlwaysRun(read));
            Assert.AreEqual(hd, ViewerSettingsConfig.ReadHd(read));
            Assert.AreEqual((stored.ShowAtStart, stored.Rules), (LauncherSettingsFile.Load(path).Settings.ShowAtStart, LauncherSettingsFile.Load(path).Settings.Rules));
        }
        Check(Chosen, new AudioVolumes(1, 2, 3), true, true, launcher);

        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.AudioChanges(new AudioVolumes(9, 9, 9))));
        Check(Chosen, new AudioVolumes(9, 9, 9), true, true, launcher);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, ViewerSettingsConfig.GameChanges(AreaGameSettings.Defaults)));
        Check(AreaGameSettings.Defaults, new AudioVolumes(9, 9, 9), true, false, launcher);
        Assert.AreEqual(LauncherSettingsSaveStatus.Saved, LauncherSettingsFile.Reset(path));
        Check(AreaGameSettings.Defaults, new AudioVolumes(9, 9, 9), true, false, LauncherSettings.Defaults);
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
        // The old file knew no controller lines and no combat taunts line (ARC-AI-1527) and no violence filter or brightness line (ARC-UI-1737): they come in at their defaults (Off, Off, Auto; taunts on; filter off; brightness 0).
        Assert.AreEqual(Chosen with { Pad = PadScheme.Off, Touch = TouchScheme.Off, Prompts = PromptStyle.Auto, CombatTaunts = true, ViolenceFilter = false, Brightness = 0, TextDuration = 6, AutoSwitchWeapons = false, FollowerSkills = true }, ViewerSettingsConfig.ReadGame(read));
        Assert.AreEqual(new AudioVolumes(4, 0, 10), ViewerSettingsConfig.ReadAudio(read));
        Assert.IsTrue(ViewerSettingsConfig.ReadAlwaysRun(read));
        Assert.IsTrue(ViewerSettingsConfig.ReadHd(read), "the game file hdInterface is the HD value");
        var stored = LauncherSettingsFile.Load(path);
        Assert.AreEqual((LauncherSettingsReadStatus.Read, true, RuleMode.Original, true), (stored.Status, stored.Settings.ShowAtStart, stored.Settings.Rules, stored.Settings.DebugMode));
    }

    [TestMethod]
    public void An_old_file_that_was_invalid_or_absent_adds_no_line_and_a_newer_launcher_object_is_kept()
    {
        Assert.AreEqual(0, ViewerSettingsMigration.Import(new LegacySettingsTexts(null, null, null, null, null)).Count);
        Assert.IsFalse(new LegacySettingsTexts(null, null, null, null, null).Any);
        var broken = new LegacySettingsTexts("{}", "not json", "maybe", "{\"schema\":1,\"profile\":\"Ultra\"}", "garbage");
        Assert.IsTrue(broken.Any);
        Assert.AreEqual(0, ViewerSettingsMigration.Import(broken).Count, "an invalid file keeps its settings at their defaults");
        Assert.AreEqual(0, ViewerSettingsMigration.Import(new LegacySettingsTexts(null, null, null, VisualProfileCodec.Serialize(VisualProfile.Hd), null)).Count, "ARC-UI-2174: the old visual profile file is not imported");

        const string newer = "{\n  \"schema\": 2,\n  \"gameRoot\": \"/x\"\n}";
        var kept = ViewerSettingsMigration.Import(new LegacySettingsTexts(null, null, null, null, newer)).Single();
        Assert.AreEqual(LauncherSettingsFile.Line, kept.Name);
        Assert.AreEqual("{\"schema\":2,\"gameRoot\":\"/x\"}", kept.Value);
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [kept]));
        Assert.AreEqual(LauncherSettingsSaveStatus.RefusedOtherSchema, LauncherSettingsFile.Save(path, LauncherSettings.Defaults), "still a newer build's, still not overwritten");
    }

    [TestMethod]
    public void The_violence_filter_and_the_brightness_are_off_and_zero_by_default_and_each_is_its_own_line()
    {
        var defaults = ViewerSettingsConfig.ReadGame(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing));
        Assert.AreEqual((false, 0), (defaults.ViolenceFilter, defaults.Brightness), "OP5: both register 0");
        var read = ViewerSettingsConfig.ReadGame(Lines("brightness=9\n"));
        Assert.AreEqual((false, 9), (read.ViolenceFilter, read.Brightness), "a line holds only its own setting");
        Assert.IsTrue(ViewerSettingsConfig.HasGame(Lines("violence filter=0\n")), "either line counts as a saved game setting");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => defaults with { Brightness = 10 });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => defaults with { Brightness = -1 });
        var values = new AreaGameOptions(1, 0, true, false, 5, 5, 5, ViolenceFilter: true, Brightness: 7);
        Assert.AreEqual((1, 7), (values.Value(AreaOption.ViolenceFilter), values.Value(AreaOption.Brightness)));
    }

    [TestMethod]
    public void The_text_duration_is_six_by_default_reads_its_own_line_and_is_written_back()
    {
        var defaults = ViewerSettingsConfig.ReadGame(ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing));
        Assert.AreEqual(6, defaults.TextDuration, "OP5: the original registers 6");
        Assert.AreEqual(10, ViewerSettingsConfig.ReadGame(Lines("text duration=10\n")).TextDuration);
        Assert.AreEqual(6, ViewerSettingsConfig.ReadGame(Lines("text duration=11\n")).TextDuration, "an out-of-range line reads as the default");
        Assert.IsTrue(ViewerSettingsConfig.HasGame(Lines("text duration=3\n")));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => defaults with { TextDuration = 11 });
        Assert.IsTrue(ViewerSettingsConfig.GameChanges(defaults with { TextDuration = 3 }).Any(change => change.Name == "text duration" && change.Value == "3"));
    }
}
