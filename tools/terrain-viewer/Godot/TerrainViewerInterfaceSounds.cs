using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-044: the original's interface sounds on the AREA viewer's windows and messages, and its three volume settings in the
// palette (docs/audio/interface-sounds.md; checked in the 1.0.7.4 executable, docs/research/audio-evidence.md SN7–SN9, SN31).
// Interface sounds are not positional and play at the plain volume through the effects channel.
public partial class TerrainViewerApp
{
    public const int BookOpenSound = 3008, BookCloseSound = 3009, BookPageSound = 3010, BookSwitchSound = 3011, LevelUpSound = 3005, QuestCompleteSound = 3028;
    private (GameSessionOwner Session, long Revision, IReadOnlySet<int> Completed)? _interfaceWatch;
    private int _interfaceSoundsPlayed;

    /// <summary>Plays an interface sound; true when it started.</summary>
    private bool PlayInterfaceSound(int id, string what)
    {
        if (_audio is null) return false;
        var (_, _, result) = PlaySfx(id);
        if (result?.Started == true) _interfaceSoundsPlayed++;
        _cueNotice = $"{what} {id}: {result?.Status.ToString() ?? "not played"}";
        return result?.Started == true;
    }

    /// <summary>Per frame: a quest that became completed for the PC plays the quest sound (checked once per session revision; the
    /// original plays it on every set to completed, which the session does not report). The level-up sound plays with each level-up
    /// message since ARC-UI-046 (<see cref="ShowLevelUps"/>).</summary>
    private void TickInterfaceSounds()
    {
        if (_areaSession is not { } session) { _interfaceWatch = null; return; }
        if (_interfaceWatch is { } seen && ReferenceEquals(seen.Session, session) && seen.Revision == session.Version.Revision) return;
        IReadOnlySet<int> completed = session.QueryStory().Quests.Where(q => q.Effective == SessionQuestState.Completed).Select(q => q.Quest).ToHashSet();
        if (_interfaceWatch is { } before && ReferenceEquals(before.Session, session))
        {
            if (completed.Any(q => !before.Completed.Contains(q))) PlayInterfaceSound(QuestCompleteSound, "quest completed");
        }
        _interfaceWatch = (session, session.Version.Revision, completed);
    }

    /// <summary>A volume setting changed in the palette (SN8): it is saved, every live sound of its channel takes the bare
    /// channel volume, and an effects change also moves the listener to the PC and recalculates the positional sounds (SN7).</summary>
    private void PaletteVolumeChanged(AudioChannel channel, int setting)
    {
        if (setting is < 0 or > AudioVolumes.Max) return;
        _audioVolumes = _audioVolumes.With(channel, setting);
        var saved = _audioStore?.Save(_audioVolumes) == true;
        _audioOrigin = saved ? "saved setting" : _audioStore?.Persistent == true ? "not saved" : "this run only";
        var volume = AudioVolumeRules.ChannelVolume(_audioVolumes, channel);
        _audio?.FlattenChannel(channel, volume);
        if (channel == AudioChannel.Effects && ActorCell() is { } pc && AreaSoundCatalog() is { } catalog)
            MoveSoundListener(PositionalSoundRules.Point(pc.X, pc.Y), catalog);
        GD.Print($"ARC-UI-044 volume {channel}={setting} channel={volume} saved={saved}");
        BindAudioPalette(); RefreshReadyStatus();
    }

    /// <summary>An item taken in the inventory window: its pickup sound, not positional (the original plays it when the item is
    /// picked up in its inventory window; the viewer's take is one click).</summary>
    private void PlayTakenItemSound(string item)
    {
        if (_audio is null || EntitySounds(item) is not { } fields
            || Arcanum.Simulation.Sound.SoundCueRules.Item(Arcanum.Simulation.Sound.ItemSoundAction.Pickup, SessionSoundCues.Item(fields)) is not { } cue) return;
        PlayInterfaceSound(cue.Id, "item taken");
    }

    /// <summary>ARC-UI-044 native smoke on the original installation: the logbook's book sounds and the level-up and quest
    /// sounds start; an effects volume change flattens a live effect to the new channel volume and moves the listener to the PC,
    /// recalculating a positional sound; the setting is then restored.</summary>
    private async Task RunAreaInterfaceSoundSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-044 interface sound smoke: FAIL " + why); throw new InvalidOperationException("ARC-UI-044 interface sound smoke: " + why); }
        Require(_audio is not null && AreaSoundCatalog() is not null && ActorCell() is { } && _worldActor.Session is AreaGridSession, "no backend, catalogue or actor");
        var played = _interfaceSoundsPlayed;
        var logbook = "none";
        if (LogbookActive && !LogbookOpen)
        {
            // The production hooks: open (3011, 3010, 3008), a tab press (3010), the player's close (3009).
            var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
            _worldFocusSimulationForSelfTest = true; _worldFocused = true;
            try
            {
                OpenLogbook();
                Require(LogbookOpen && _interfaceSoundsPlayed == played + 3, "the logbook opened with its three sounds: " + _cueNotice);
                SelectLogbookTab(_logbookTab == AreaLogbookTab.Rumors ? AreaLogbookTab.Quests : AreaLogbookTab.Rumors);
                CloseLogbookByPlayer();
                Require(!LogbookOpen && _interfaceSoundsPlayed == played + 5, "a tab and the close played theirs: " + _cueNotice);
                logbook = "open,tab,close";
            }
            finally { _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; }
            played = _interfaceSoundsPlayed;
        }
        foreach (var (id, what) in new[] { (LevelUpSound, "level up"), (QuestCompleteSound, "quest") }) PlayInterfaceSound(id, what);
        Require(_interfaceSoundsPlayed == played + 2, "the level and quest sounds started: " + _cueNotice);
        var before = _audioVolumes;
        try
        {
            StopSceneSound();
            var (_, _, plain) = PlaySfx(BookOpenSound);
            var plainHandle = plain?.Handle ?? throw new InvalidOperationException("ARC-UI-044 interface sound smoke: no plain effect");
            var cell = ActorCell()!.Value;
            var far = PlayCueAt(new Arcanum.Simulation.Sound.SoundCue(2904), cell.X - 3, cell.Y, PositionalSize.Large, "smoke").Result?.Handle;
            Require(far is not null, "a positional effect three tiles from the PC");
            var setting = before.Effects == 3 ? 4 : 3;
            // Through the palette's own control when it exists, else the handler it calls.
            if (_worldPanel?.FindChildren("Effectsvolume", "SpinBox", true, false).OfType<SpinBox>().SingleOrDefault() is { } control) { _worldPanel.BindVolumes(_audioVolumes); control.Value = setting; }
            else PaletteVolumeChanged(AudioChannel.Effects, setting);
            var channel = AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects);
            Require(_audioVolumes.Effects == setting, "the setting changed");
            Require(Math.Abs(_audio!.Volume(plainHandle)!.Value - channel) < 0.5, $"a plain effect takes the bare channel volume {channel}");
            Require(_soundListener == PositionalSoundRules.Point(cell.X, cell.Y), "the listener moved to the PC");
            var level = PositionalSoundRules.Level(PositionalSoundRules.Point(cell.X - 3, cell.Y), _soundListener!.Value,
                Positional(AreaSoundCatalog()!.Positional), PositionalSize.Large);
            Require(Math.Abs(_audio.Volume(far!.Value)!.Value - AudioVolumeRules.AdjustedVolume(level.Volume, channel)) < 0.5,
                $"the positional effect is recalculated from the PC ({_audio.Volume(far.Value)} vs {AudioVolumeRules.AdjustedVolume(level.Volume, channel)})");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"ARC-UI-044 native-smoke interface PASS logbook={logbook} effects={setting} channel={channel} listener=pc recalculated={AudioVolumeRules.AdjustedVolume(level.Volume, channel)}");
        }
        finally
        {
            PaletteVolumeChanged(AudioChannel.Effects, before.Effects);
            StopSceneSound();
        }
    }
}
