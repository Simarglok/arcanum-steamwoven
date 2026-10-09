using System.Buffers.Binary;
using AudioLab.Core;
using AudioLab.Synthetic;

int checks = 0;
void Check(bool condition, string name)
{
    checks++; if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}
void Error(AudioResult result, AudioError expected, string name) => Check(result.Error == expected, name + " => " + expected);
var data = SyntheticWave.Create();
Check(data.Length == 8044, "literal WAV length");
Check(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(40)) == 8000, "literal PCM byte length");
Check(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(22)) == 1, "literal mono");
Check(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24)) == 16000, "literal sample rate");
for (int i = 0; i < 4000; i++) Check(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(44 + 2 * i)) == (i % 40 < 20 ? 4096 : -4096), "wave sample " + i);
Check(PcmWaveProfile.Accept(data), "WAV admission");
foreach (int offset in new[] { 0, 4, 8, 16, 20, 22, 24, 28, 32, 34, 36, 40 })
{
    var corrupt = (byte[])data.Clone(); corrupt[offset] = 255; Check(!PcmWaveProfile.Accept(corrupt), "bad WAV metadata " + offset);
}
Check(!PcmWaveProfile.Accept(data.AsSpan(0, 40)), "truncated WAV admission");
EncodedAudio.Create(new byte[4], MediaKind.Wav, 4, out var tiny);
var cmd = new PlayCommand(tiny!, AudioBus.Music, true);
Error(EncodedAudio.Create(new byte[5], MediaKind.Wav, 4, out _), AudioError.BufferLimit, "factory beyond4");
Error(EncodedAudio.Create([], MediaKind.Wav, 4, out _), AudioError.InvalidMedia, "empty factory");
Error(EncodedAudio.Create([1], (MediaKind)88, 4, out _), AudioError.InvalidMedia, "invalid media kind");
var mutable = new byte[] { 1, 2, 3, 4 };
EncodedAudio.Create(mutable, MediaKind.Wav, 4, out var immutable); mutable[0] = 99;
var copy = immutable!.CopyForDecoder(); copy[1] = 99;
Check(immutable.CopyForDecoder().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "immutable command owns bytes");
using var p = new AudioPolicy(new(2, 4, 8));
Error(p.Reserve(cmd, true), AudioError.Cancelled, "pre-cancel no reservation");
Check(p.ActivePlayers == 0 && p.RetainedBytes == 0, "cancel releases");
Error(p.Reserve(cmd with { Bus = AudioBus.Master }), AudioError.InvalidCommand, "Master not playable");
Error(p.Reserve(cmd with { Bus = (AudioBus)88 }), AudioError.InvalidCommand, "unknown bus");
var first = p.Reserve(cmd); Error(first, AudioError.None, "literal at4-byte buffer");
Error(p.RetainDecoded(first.Handle, 4), AudioError.None, "literal at8 total bytes");
Check(p.RetainedBytes == 8 && p.CacheBytes == 0, "retained8 zero cache");
Error(p.RetainDecoded(first.Handle, 1), AudioError.TotalByteLimit, "beyond total8");
Error(p.RetainDecoded(first.Handle, 5), AudioError.BufferLimit, "decoded beyond4");
Error(p.Reserve(cmd), AudioError.TotalByteLimit, "reserve beyond total8");
Error(p.Stop(first.Handle), AudioError.None, "stop releases");
Error(p.Stop(first.Handle), AudioError.AlreadyStopped, "double stop");
Check(p.RetainedBytes == 0, "stop retained zero");
var second = p.Reserve(cmd);
Error(p.Stop(first.Handle), AudioError.StaleHandle, "reused slot stale generation");
using var foreign = new AudioPolicy(new(2, 4, 8));
Error(foreign.Stop(second.Handle), AudioError.ForeignHandle, "foreign owner");
Error(p.Stop(second.Handle with { Slot = 200 }), AudioError.StaleHandle, "invalid slot");
var third = p.Reserve(cmd); Check(p.ActivePlayers == 2 && p.RetainedBytes == 8, "literal at2 concurrent players");
Error(p.Reserve(cmd), AudioError.PlayerLimit, "beyond2 players");
Error(p.Pause(second.Handle, true), AudioError.None, "pause");
Error(p.Fade(second.Handle, 0, 2), AudioError.None, "fade2 real seconds");
Error(p.Tick(.5), AudioError.None, "independent real tick");
p.State(second.Handle, out var state); Check(state.Paused && state.Gain == .75f, "fade continues paused literal.75");
p.Tick(1.5); p.State(second.Handle, out state); Check(state.Gain == 0, "fade target exact0");
Error(p.Fade(second.Handle, float.NaN, 1), AudioError.InvalidCommand, "NaN fade");
Error(p.Fade(second.Handle, 1, 61), AudioError.InvalidCommand, "fade duration beyond60");
Error(p.Tick(-1), AudioError.InvalidCommand, "negative time");
Error(p.Tick(double.PositiveInfinity), AudioError.InvalidCommand, "infinite time");
Error(p.SetLevel(AudioBus.Voice, new(.25f, true)), AudioError.None, "Voice independent volume mute");
Check(p.Level(AudioBus.Music) == new BusLevel(1, false) && p.Level(AudioBus.Master) == new BusLevel(.5f, false), "unrelated volume unchanged");
p.SetLevel(AudioBus.Voice, new(.75f, true)); Check(p.Level(AudioBus.Voice) == new BusLevel(.75f, true), "volume preserves explicit mute");
Error(p.SetLevel(AudioBus.Music, new(1.01f, false)), AudioError.InvalidCommand, "volume beyond1");
Error(p.SetLevel(AudioBus.Music, new(-.01f, false)), AudioError.InvalidCommand, "volume below0");
Error(p.SetLevel(AudioBus.Music, new(float.NaN, false)), AudioError.InvalidCommand, "NaN volume");
Error(Task.Run(() => p.Stop(second.Handle)).GetAwaiter().GetResult(), AudioError.WrongThread, "background mutation rejects");
p.Dispose(); Check(p.ActivePlayers == 0 && p.RetainedBytes == 0, "dispose releases all");
Error(p.Reserve(cmd), AudioError.Disposed, "disposed play");
Error(p.Stop(second.Handle), AudioError.Disposed, "disposed stop"); p.Dispose();
const string validJson = "{\"Version\":1,\"Master\":{\"Volume\":0.5,\"Muted\":false},\"Music\":{\"Volume\":0.25,\"Muted\":true},\"Voice\":{\"Volume\":0.75,\"Muted\":false},\"SFX\":{\"Volume\":0,\"Muted\":false},\"Ambient\":{\"Volume\":1,\"Muted\":true}}";
Error(PreferencesStore.Decode(validJson, out var prefs), AudioError.None, "literal v1 prefs");
Check(prefs.Music == new BusLevel(.25f, true) && prefs.SFX == new BusLevel(0, false), "literal prefs values");
foreach (var json in new[] { "{", "null", "{}", validJson.Replace("\"Version\":1", "\"Version\":2"), validJson.Replace("0.25", "1.1"), validJson.Replace("\"Muted\":true", "\"Nope\":true"), validJson.Replace("\"Ambient\"", "\"Other\""), validJson.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"), validJson.Replace("\"Volume\":0.25", "\"Volume\":0.25,\"Volume\":0.25") })
{
    Error(PreferencesStore.Decode(json, out var defaults), AudioError.PreferencesInvalid, "corrupt typed prefs");
    Check(defaults == MixerPreferences.Defaults, "corrupt deterministic defaults");
}
using var restore = new AudioPolicy(new(1, 4, 8));
Error(restore.ApplyPreferences(prefs), AudioError.None, "apply persisted prefs");
Check(restore.Level(AudioBus.Music) == new BusLevel(.25f, true) && restore.Level(AudioBus.Voice) == new BusLevel(.75f, false), "restored independence");
for (int i = 0; i < 1000; i++)
{
    var r = restore.Reserve(cmd); Error(r, AudioError.None, "cycle reserve " + i); restore.Stop(r.Handle);
}
Check(restore.ActivePlayers == 0 && restore.RetainedBytes == 0, "1000 lifecycle cycles no retention");
Console.WriteLine($"AUDIO_CORE_PASS checks={checks}");
