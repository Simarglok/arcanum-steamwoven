using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-043: the dialogue voice in the AREA viewer (docs/audio/dialogue-voice.md). Each NPC line shown plays its voice once
// on the voice channel at the voice channel's volume, the file chosen by the PC's gender with the female → male fallback;
// a line without a voice leaves the previous one playing, picking a reply stops it, closing the dialog does not. A missing
// voice changes nothing in the dialog.
public partial class TerrainViewerApp
{
    private readonly DialogVoicePlayer _dialogVoice = new();
    private AreaConversationStart? _voiceStart;
    private ViewerAudioHandle? _voiceHandle;
    private string? _voiceNotice;
    private int _voicesPlayed;

    private void TickDialogVoice()
    {
        if (_audio is null) return;
        if (!ConversationOpen || _conversationPanel?.Conversation is not { } conversation || _voiceStart is not { } start
            || !ReferenceEquals(start.Conversation, conversation))
        {
            _dialogVoice.Release();
            return;
        }
        var identity = start.VoiceIdentity(conversation.NpcLine);
        foreach (var command in _dialogVoice.Update(conversation, conversation.Turn, identity)) ExecuteVoice(command);
    }

    private void ExecuteVoice(DialogVoiceCommand command)
    {
        if (_voiceHandle is { } playing) { _audio!.Stop(playing); _voiceHandle = null; }
        if (command.Kind != DialogVoiceCommandKind.Play) return;
        if (AreaSoundCatalog() is not { } catalog) { _voiceNotice = "no catalogue"; return; }
        var gender = _conversationHost?.Profile.Pc.Gender == DialogPcGender.Female ? SpeechGender.Female : SpeechGender.Male;
        var speech = catalog.ResolveSpeech(command.Identity, gender);
        if (speech.File is not { } file) { _voiceNotice = $"voice {speech.Folder}/{speech.Line}: {speech.Status}"; return; }
        var payload = catalog.OpenPayload(file);
        var result = _audio!.Play(payload, AudioChannel.Voice, AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Voice));
        _voiceHandle = result.Handle;
        if (result.Started) _voicesPlayed++;
        _voiceNotice = $"voice {speech.Folder}/{speech.Line}: {result.Status}";
        GD.Print($"ARC-UI-043 voice identity={command.Identity} status={speech.Status} play={result.Status}");
    }

    private string VoiceStatus() => _voiceNotice is null ? "" : $" | voice: {_voiceNotice} ({_voicesPlayed} played)";

    /// <summary>ARC-UI-043 native smoke on the original installation (any map): the first installed line voice of the module
    /// plays on the voice channel at the voice volume; a line whose file is missing then stops it and plays nothing.</summary>
    private async Task RunAreaVoiceSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-043 voice smoke: FAIL " + why); throw new InvalidOperationException("ARC-UI-043 voice smoke: " + why); }
        Require(_audio is not null && AreaSoundCatalog() is not null && _soundContent?.Stack is not null, "no backend or catalogue");
        var key = _soundContent!.Value.Stack!.Keys("sound/speech/").FirstOrDefault(k =>
            System.Text.RegularExpressions.Regex.IsMatch(k, @"^sound/speech/[0-9]{5}/v[0-9]+_m\.mp3$"));
        Require(key is not null, "no line voice installed");
        var parts = key!.Split('/');
        var identity = (int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) << 16)
            | int.Parse(parts[3][1..parts[3].IndexOf('_')], System.Globalization.CultureInfo.InvariantCulture);
        var played = _voicesPlayed;
        ExecuteVoice(new(DialogVoiceCommandKind.Play, identity));
        Require(_voicesPlayed == played + 1 && _voiceHandle is { } handle, $"the voice started ({_voiceNotice})");
        Require(Math.Abs(_audio!.Volume(_voiceHandle!.Value)!.Value - AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Voice)) < 0.5, "at the voice channel volume");
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_audio.Audible) Require(_audio.IsPlaying(_voiceHandle.Value), "still playing a few frames later");
        var first = _voiceHandle.Value;
        ExecuteVoice(new(DialogVoiceCommandKind.Play, (0x7FFF << 16) | 0xFFFF));
        Require(_audio.Volume(first) is null && _voiceHandle is null && _voiceNotice!.Contains("Missing", StringComparison.Ordinal), "a missing voice stops the old one and plays nothing: " + _voiceNotice);
        GD.Print($"ARC-UI-043 native-smoke voice PASS identity={identity} channel={AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Voice)} missing=silent");
    }
}
