using Arcanum.Presentation.Audio;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Sound;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-040: the effect sounds of the dialog scripts the viewer runs (docs/scripting/scr-dispatcher-runtime.md, sound
// actions). Play sound
// plays its effect once without a position (volume 127, centre), play sound on plays it once on the object's tile (the PC or
// a dialog NPC); scheme requests already went to the session when the run's effects were applied.
public partial class TerrainViewerApp
{
    private int _scriptSoundsPlayed;

    private void TickScriptSounds()
    {
        if (_conversationHost?.Scripts is not { } scripts) return;
        foreach (var sound in scripts.TakeSounds()) PlayScriptSound(sound, scripts);
    }

    private void PlayScriptSound(ScrSound sound, Arcanum.TerrainViewer.Core.AreaScriptHost scripts)
    {
        if (_audio is null) return;
        if (sound.Kind == ScrSoundKind.Effect)
        {
            var (_, _, result) = PlaySfx(sound.Id);
            if (result?.Started == true) _scriptSoundsPlayed++;
            _cueNotice = $"script {sound.Id}: {result?.Status.ToString() ?? "not played"}";
            return;
        }
        if (sound.Kind != ScrSoundKind.OnObject || sound.Object is not { } target) return;
        // A speaker stands where it is now (a companion's live cell), else at its loaded cell.
        var cell = target == scripts.Pc ? ActorCell()
            : scripts.SpeakerOf(target) is { } speaker ? SpeakerCell?.Invoke(speaker) ?? (speaker.WorldX, speaker.WorldY) : ((long X, long Y)?)null;
        if (cell is not { } at) { _cueNotice = $"script {sound.Id}: no position"; return; }
        if (PlayCueAt(new SoundCue(sound.Id), at.X, at.Y, PositionalSize.Large, "script").Result?.Started == true) _scriptSoundsPlayed++;
    }

    /// <summary>ARC-SCRIPT-040 native smoke (any map with dialog scripts loaded): the viewer's player for script sounds plays a
    /// play sound effect without a position and a play sound on the PC on its tile (under the view-centre listener). It does not
    /// run a script: no dialog run the viewer makes reaches a sound action yet.</summary>
    private void RunAreaScriptSoundSmoke()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-SCRIPT-040 script sound smoke: " + why); }
        if (_conversationHost?.Scripts is not { } scripts) { GD.Print("ARC-SCRIPT-040 native-smoke script sounds NONE (no script host)"); return; }
        Require(_audio is not null && ActorCell() is not null, "no backend or actor");
        var played = _scriptSoundsPlayed;
        PlayScriptSound(new(ScrSoundKind.Effect, InterfaceSoundIds[0]), scripts);
        Require(_scriptSoundsPlayed == played + 1, "play sound: " + _cueNotice);
        PlayScriptSound(new(ScrSoundKind.OnObject, 2904, 0, scripts.Pc), scripts);
        Require(_scriptSoundsPlayed == played + 2 && _cueNotice!.Contains("volume 127", StringComparison.Ordinal), "play sound on the PC, under the listener: " + _cueNotice);
        GD.Print($"ARC-SCRIPT-040 native-smoke script sounds PASS effect={InterfaceSoundIds[0]} onPc=2904 ({_cueNotice})");
    }
}
