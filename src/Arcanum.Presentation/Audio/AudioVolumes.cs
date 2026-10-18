using System.Globalization;
using System.Text.Json;

namespace Arcanum.Presentation.Audio;

/// <summary>The original's three sound channels (docs/research/audio-evidence.md SN8–SN9). Ambient scheme sounds play on
/// <see cref="Effects"/>, scheme songs and fixed music on <see cref="Music"/>, dialogue and slide voice on <see cref="Voice"/>.</summary>
public enum AudioChannel { Effects, Voice, Music }

/// <summary>
/// ARC-UI-040: the user's channel volumes, as the original's options screen holds them: three settings 0–10, default 5
/// (SN8–SN9, checked in the 1.0.7.4 executable). They are user preferences, never part of a game save.
/// </summary>
public sealed record AudioVolumes
{
    public const int Max = 10;
    public const int Default = 5;

    public AudioVolumes(int effects, int voice, int music)
    {
        if (effects is < 0 or > Max) throw new ArgumentOutOfRangeException(nameof(effects));
        if (voice is < 0 or > Max) throw new ArgumentOutOfRangeException(nameof(voice));
        if (music is < 0 or > Max) throw new ArgumentOutOfRangeException(nameof(music));
        (Effects, Voice, Music) = (effects, voice, music);
    }

    public int Effects { get; }
    public int Voice { get; }
    public int Music { get; }

    public static AudioVolumes Defaults { get; } = new(Default, Default, Default);

    public int this[AudioChannel channel] => channel switch
    {
        AudioChannel.Effects => Effects,
        AudioChannel.Voice => Voice,
        AudioChannel.Music => Music,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public AudioVolumes With(AudioChannel channel, int setting) => channel switch
    {
        AudioChannel.Effects => new(setting, Voice, Music),
        AudioChannel.Voice => new(Effects, setting, Music),
        AudioChannel.Music => new(Effects, Voice, setting),
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}

/// <summary>
/// ARC-UI-040: the original's volume arithmetic (docs/research/audio-evidence.md SN6, SN8, SN16), in its 0–127 sound
/// volume scale. A channel's volume is <c>setting × 127 / 10</c>, and for effects and music then <c>× 80 / 100</c>
/// (integer steps), so effects and music reach 101 at setting 10 and voice 127. A new effect starts at
/// <c>volume × channel / 100</c>, a later recalculation uses <c>volume × channel / 127</c> (the original's two scales), and
/// a channel change sets every live sound of that channel to the bare channel volume. How the player turns a 0–127 volume
/// into amplitude (<see cref="Amplitude"/>) is a Steamwoven choice, not checked against the original's mixer.
/// </summary>
public static class AudioVolumeRules
{
    public const int MaxVolume = 127;

    /// <summary>The channel's volume (0–127) for a setting 0–10.</summary>
    public static int ChannelVolume(AudioChannel channel, int setting)
    {
        if (setting is < 0 or > AudioVolumes.Max) throw new ArgumentOutOfRangeException(nameof(setting));
        return channel switch
        {
            AudioChannel.Effects => EffectsVolume(setting),
            AudioChannel.Voice => VoiceVolume(setting),
            AudioChannel.Music => MusicVolume(setting),
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
    }

    [OriginalFunction(0x41C8B0, OriginalFunctionScope.Partial)]
    private static int EffectsVolume(int setting) => setting * MaxVolume / AudioVolumes.Max * 80 / 100;

    [OriginalFunction(0x41C930, OriginalFunctionScope.Partial)]
    private static int VoiceVolume(int setting) => setting * MaxVolume / AudioVolumes.Max;

    [OriginalFunction(0x41C980, OriginalFunctionScope.Partial)]
    private static int MusicVolume(int setting) => setting * MaxVolume / AudioVolumes.Max * 80 / 100;

    public static int ChannelVolume(AudioVolumes volumes, AudioChannel channel) => ChannelVolume(channel, volumes[channel]);

    /// <summary>The volume an effect or scheme sound starts at: its own volume (0–127) times the channel volume / 100.</summary>
    [OriginalFunction(0x41B2E0, OriginalFunctionScope.Partial)]
    public static int StartVolume(int volume, int channelVolume) => volume * channelVolume / 100;

    /// <summary>The volume after a recalculation (a listener move, a source move): its own volume times the channel / 127.</summary>
    [OriginalFunction(0x41C9D0, OriginalFunctionScope.Partial)]
    public static int AdjustedVolume(int volume, int channelVolume) => volume * channelVolume / MaxVolume;

    /// <summary>Linear amplitude 0–1 for a volume, clamped to 0–127: a Steamwoven choice (NOT_VERIFIED against the
    /// original's mixer curve).</summary>
    public static double Amplitude(int volume) => Math.Clamp(volume, 0, MaxVolume) / (double)MaxVolume;
}

/// <summary>ARC-UI-040: the preference file's text, <c>{"version":1,"effects":E,"voice":V,"music":M}</c>. Anything else
/// (another version, a missing, extra or out-of-range value) reads as null; the caller then uses the defaults.</summary>
public static class AudioVolumesCodec
{
    public const int Version = 1;

    public static string Serialize(AudioVolumes volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"version\":{Version},\"effects\":{volumes.Effects},\"voice\":{volumes.Voice},\"music\":{volumes.Music}}}");
    }

    public static AudioVolumes? Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4) return null;
            if (!Int(root, "version", out var version) || version != Version) return null;
            if (!Int(root, "effects", out var effects) || !Int(root, "voice", out var voice) || !Int(root, "music", out var music)) return null;
            if (effects is < 0 or > AudioVolumes.Max || voice is < 0 or > AudioVolumes.Max || music is < 0 or > AudioVolumes.Max) return null;
            return new(effects, voice, music);
        }
        catch (JsonException) { return null; }
    }

    private static bool Int(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
