using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Presentation.Audio;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-040: sound output, the three original channel volumes and the original's sound catalogue for the area's module
// (docs/audio/viewer-audio.md). The catalogue is opened once per module archive through the same content root as the
// script content (any map of any module); the palette's sound test plays the original's fixed interface sounds by id,
// music and ambient play through TerrainViewerAudioScheme (ARC-UI-041), and later tasks add event, voice and interface sounds.
public partial class TerrainViewerApp
{
    /// <summary>The original's fixed interface sound ids (docs/research/audio-evidence.md SN31), in the order the test plays.</summary>
    internal static IReadOnlyList<int> InterfaceSoundIds { get; } = Array.AsReadOnly(new[] { 3008, 3009, 3010, 3011, 3002, 3003, 3004, 3005, 3006, 3007, 3027, 3028 });
    private ViewerAudioBackend? _audio;
    private AudioVolumesStore? _audioStore;
    private AudioVolumes _audioVolumes = AudioVolumes.Defaults;
    private string _audioOrigin = "default";
    private (string Module, ContentStack? Stack, SoundCatalog? Catalog, string Detail)? _soundContent;
    private string? _audioNotice;
    private int _soundTestCursor = -1;

    private void ConfigureAudio()
    {
        _audioStore = new AudioVolumesStore(AutomatedVisualRun);
        _audioVolumes = _audioStore.Load();
        _audioOrigin = !_audioStore.Persistent ? "default (automated run)" : _audioStore.HasSaved ? "saved setting" : "default (nothing saved yet)";
        _audio = ViewerAudioBackend.Create(this);
        GD.Print($"ARC-UI-040 audio driver={_audio.Driver} audible={_audio.Audible} volumes={_audioVolumes.Effects}/{_audioVolumes.Voice}/{_audioVolumes.Music} from={_audioOrigin}");
    }

    /// <summary>The sound catalogue of the module archive the area was read from, opened once and kept; null with a detail
    /// when the module is not in a game folder or the catalogue is refused.</summary>
    private SoundCatalog? SoundCatalogFor(string module)
    {
        if (_soundContent is { } cached && cached.Module == module) return cached.Catalog;
        ReleaseSoundContent();
        var plan = AreaContentRoot.Discover(module);
        ContentStack? stack = null; SoundCatalog? catalog = null; string detail;
        if (plan.Status != AreaContentRootStatus.Ready) detail = $"{plan.Status}: {plan.Detail}";
        else
        {
            var opened = ContentStack.Open(plan.Plan!);
            if (opened.Status != ContentStackStatus.Ready) detail = $"stack {opened.Status}";
            else
            {
                stack = opened.Stack!;
                var result = SoundCatalog.Open(stack);
                catalog = result.Catalog;
                detail = result.Status == SoundCatalogStatus.Ready
                    ? $"{catalog!.Tables.Count} tables{(catalog.UserTable is null ? "" : " + user table")}, {catalog.SchemeIds.Count} schemes, {catalog.Issues.Count} issues"
                    : $"refused {result.Status}";
                if (catalog is null) { stack.Dispose(); stack = null; }
            }
        }
        GD.Print($"ARC-UI-040 sound catalogue {detail}");
        _soundContent = (module, stack, catalog, detail);
        return catalog;
    }

    private void ReleaseSoundContent()
    {
        _soundContent?.Stack?.Dispose();
        _soundContent = null;
        _soundWorld = null; _schemeCache.Clear(); _sectorSoundCache.Clear(); _ambientPayloads.Clear();
        ReleaseCueContent();
    }

    /// <summary>The original's non-positional effect (SN6: the plain play call uses volume 127 and the centre pan), scaled by
    /// the effects channel at start.</summary>
    private (SfxResolution? Sfx, SoundPayload? Payload, ViewerAudioResult? Result) PlaySfx(int id)
    {
        if (_audio is null || _arguments?.ModulePath is not { } module || SoundCatalogFor(module) is not { } catalog) return (null, null, null);
        var sfx = catalog.ResolveSfx(id);
        if (!sfx.Playable) return (sfx, null, null);
        var payload = catalog.OpenPayload(sfx.File!);
        var volume = AudioVolumeRules.StartVolume(AudioVolumeRules.MaxVolume, AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects));
        return (sfx, payload, _audio.Play(payload, AudioChannel.Effects, volume));
    }

    /// <summary>Palette: plays the next of the original's interface sounds and reports what happened.</summary>
    private void PaletteSoundTest()
    {
        _soundTestCursor = (_soundTestCursor + 1) % InterfaceSoundIds.Count;
        var id = InterfaceSoundIds[_soundTestCursor];
        var (sfx, payload, result) = PlaySfx(id);
        _audioNotice = _audio is null ? $"sound {id}: audio off" : sfx is null ? $"sound {id}: no catalogue ({_soundContent?.Detail ?? "no module"})"
            : !sfx.Playable ? $"sound {id}: {sfx.Status}{(sfx.File is { } f ? $" {f.Status}" : "")}"
            : payload!.Status != SoundPayloadStatus.Admitted ? $"sound {id}: {payload.Status}"
            : $"sound {id}: {result!.Value.Status}";
        GD.Print($"ARC-UI-040 sound test {_audioNotice}");
        BindAudioPalette(); RefreshReadyStatus();
    }

    private void BindAudioPalette()
    {
        if (_worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel)) return;
        _worldPanel.SetSoundStatus($"Sound: {AudioSummary()}.");
        _worldPanel.BindVolumes(_audioVolumes);
    }

    private string AudioSummary() => _audio is null ? "off"
        : $"{(_audio.Audible ? "device " + _audio.Driver : "no output device (" + _audio.Driver + ")")}; effects {_audioVolumes.Effects}, voice {_audioVolumes.Voice}, music {_audioVolumes.Music} ({_audioOrigin})"
          + (_soundContent is { } content ? $"; catalogue {content.Detail}" : "") + (_audioNotice is null ? "" : "; " + _audioNotice);

    private string AudioStatus() => (_audioNotice is null ? "" : $" | sound: {_audioNotice}") + CueStatus() + VoiceStatus();

    private void TickAudio()
    {
        _audio?.Tick();
        // ARC-UI-026: under the death screen the scene's sounds stand still (only the slides' music and voice play).
        if (_readyMode == TerrainViewerMode.Area && !HeroDeathCovering) { TickSceneSound(); TickPositionalSound(); TickDialogVoice(); TickScriptSounds(); TickInterfaceSounds(); }
    }

    private void DisposeAudio()
    {
        _audio?.Dispose(); _audio = null;
        ReleaseSoundContent();
    }
}
