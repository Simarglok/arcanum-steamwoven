using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioLab.Core;

public sealed record MixerPreferences(int Version, BusLevel Master, BusLevel Music, BusLevel Voice, BusLevel SFX, BusLevel Ambient)
{
    [JsonIgnore] public bool Valid => Version == 1 && Enum.GetValues<AudioBus>().All(b => float.IsFinite(Level(b).Volume) && Level(b).Volume is >= 0 and <= 1);
    public BusLevel Level(AudioBus bus) => bus switch
    {
        AudioBus.Master => Master, AudioBus.Music => Music, AudioBus.Voice => Voice,
        AudioBus.SFX => SFX, AudioBus.Ambient => Ambient, _ => throw new ArgumentOutOfRangeException(nameof(bus))
    };
    public static MixerPreferences Defaults { get; } = new(1, new(.5f, false), new(1, false), new(1, false), new(1, false), new(1, false));
}
public static class PreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    // Required fields are checked independently of record constructor defaulting.
    public static AudioResult Decode(string json, out MixerPreferences preferences)
    {
        preferences = MixerPreferences.Defaults;
        if (json.Length > 4096) return new(AudioError.PreferencesInvalid);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Count() != 6) return new(AudioError.PreferencesInvalid);
            foreach (string name in new[] { "Version", "Master", "Music", "Voice", "SFX", "Ambient" })
                if (!doc.RootElement.TryGetProperty(name, out _)) return new(AudioError.PreferencesInvalid);
            foreach (string name in new[] { "Master", "Music", "Voice", "SFX", "Ambient" })
            {
                var level = doc.RootElement.GetProperty(name);
                if (level.ValueKind != JsonValueKind.Object || level.EnumerateObject().Count() != 2) return new(AudioError.PreferencesInvalid);
                if (!level.TryGetProperty("Volume", out _) || !level.TryGetProperty("Muted", out _)) return new(AudioError.PreferencesInvalid);
            }
            var parsed = JsonSerializer.Deserialize<MixerPreferences>(json, Options);
            if (parsed is null || !parsed.Valid) return new(AudioError.PreferencesInvalid);
            preferences = parsed; return new(AudioError.None);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        { return new(AudioError.PreferencesInvalid); }
    }
    public static AudioResult Load(string path, out MixerPreferences preferences)
    {
        preferences = MixerPreferences.Defaults;
        try
        {
            if (!File.Exists(path)) return new(AudioError.PreferencesMissing);
            if (new FileInfo(path).Length > 4096) return new(AudioError.PreferencesInvalid);
            return Decode(File.ReadAllText(path), out preferences);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(AudioError.PreferencesIo); }
    }
    public static AudioResult Save(string path, MixerPreferences preferences)
    {
        if (preferences is null || !preferences.Valid) return new(AudioError.PreferencesInvalid);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
            File.Move(temporary, path, true); return new(AudioError.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(AudioError.PreferencesIo); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
