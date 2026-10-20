using Arcanum.Presentation.Audio;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-042 native smoke on the original installation: the hero's tile has a footstep class and a footstep cue
    /// plays one of the original's footstep ids on the effects channel; a source far to the right (beyond the large maximum
    /// radius, past the maximum pan distance to the right) starts silent and hard right; moving the listener onto it raises it
    /// to the full volume scaled by the channel / 127. Prints the event sounds the earlier stages played.</summary>
    private async Task RunAreaCueSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-042 cue smoke: FAIL " + why); throw new InvalidOperationException("ARC-UI-042 cue smoke: " + why); }
        Require(_audio is not null && AreaSoundCatalog() is not null && _worldActor.Session is AreaGridSession, "no backend, catalogue or actor");
        var grid = (AreaGridSession)_worldActor.Session!;
        var (prototypes, tiles) = CueContent();
        Require(prototypes is not null && tiles is not null, $"cue content {_cueContent?.Detail}");
        var earlier = string.Join(",", _cuePlays.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}"));
        var cell = ActorCell()!.Value;
        var tile = TileSoundAt(cell.X, cell.Y);
        Require(tile is >= 0 and <= 5, $"the hero's tile has a footstep class ({tile})");
        try
        {
            StopSceneSound();
            var step = PlayHeroFootstep(grid);
            Require(step is { Id: >= 2900 and < 2940, Result.Started: true }, $"footstep {step?.Id} {step?.Result?.Status} ({_cueNotice})");
            // Far enough to the screen's right to be beyond the large maximum radius and the maximum pan distance.
            var positional = AreaSoundCatalog()!.Positional;
            var tilesAway = Math.Max(positional.Large.MaxRadius, positional.MaxPanDistance) / 40 + 2;
            var (farX, farY) = (cell.X - tilesAway, cell.Y);
            var far = PlayCueAt(new SoundCue(step!.Value.Id), farX, farY, PositionalSize.Large, "smoke");
            Require(far.Result is { Started: true, Handle: not null }, $"far source {far.Result?.Status}");
            var handle = far.Result!.Value.Handle!.Value;
            Require(_audio!.Volume(handle) is < 0.5, $"beyond the maximum radius it starts silent ({_audio.Volume(handle)}; {_cueNotice})");
            Require(_cueNotice!.EndsWith("balance 127", StringComparison.Ordinal), "hard right: " + _cueNotice);
            MoveSoundListener(PositionalSoundRules.Point(farX, farY), AreaSoundCatalog()!);
            var expected = AudioVolumeRules.AdjustedVolume(AudioVolumeRules.MaxVolume, AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects));
            Require(_audio.Volume(handle) is { } moved && Math.Abs(moved - expected) < 0.5, $"the listener on the source: {_audio.Volume(handle)} != {expected}");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"ARC-UI-042 native-smoke cues PASS tile={tile} footstep={step.Value.Id} far={tilesAway} tiles silent,right recalculated={expected} earlier=\"{earlier}\" content=\"{_cueContent?.Detail}\"");
        }
        finally { StopSceneSound(); }
    }
}
