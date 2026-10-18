namespace Arcanum.Presentation.Audio;

/// <summary>What a scheme sound is (the catalogue's entry kind, docs/audio/sound-catalog.md#scheme-entries).</summary>
public enum SchemeSoundKind { Ambient, LoopSong, AnchorSong, TransitionSong }

/// <summary>One scheme sound as the player needs it: raw 0–127 volume and balance, inclusive hours, per-mille frequency
/// and the virtual path of its file (null when the catalogue found none; it is still "played" and simply makes no sound).</summary>
public sealed record SchemeSound(int ListNumber, SchemeSoundKind Kind, string? File, int Frequency, int HourStart, int HourEnd,
    int VolumeMin, int VolumeMax, int BalanceLeft, int BalanceRight);

/// <summary>The sounds of the scheme in one slot, in list order.</summary>
public sealed record SchemeSounds(int Id, IReadOnlyList<SchemeSound> Sounds);

/// <summary>What the player hands the host: the session's slots (null = empty), whether a transition still restores,
/// and the combat music flag with its start counter.</summary>
public sealed record SchemeInput(SchemeSounds? Slot0, SchemeSounds? Slot1, bool RestorePending, bool CombatMusic, long CombatStarts);

public enum SchemeCommandKind
{
    /// <summary>Start <see cref="SchemeCommand.Sound"/> as a song under <see cref="SchemeCommand.Voice"/> (looped or once) at <see cref="SchemeCommand.Volume"/>.</summary>
    StartSong,
    /// <summary>Fade the voice out over <see cref="SchemeCommand.FadeMs"/> and release it.</summary>
    FadeOut,
    /// <summary>Play the ambient sound once at the drawn volume and balance (fire and forget).</summary>
    PlayAmbient,
    /// <summary>A transition song ended while a restore is pending: tell the session.</summary>
    TransitionFinished,
    /// <summary>Start combat music: stinger <see cref="SchemeCommand.Stinger"/> once and the combat loop, together.</summary>
    StartCombat,
    /// <summary>Stop combat music at once.</summary>
    StopCombat,
}

/// <param name="Voice">The player's token for a song voice; the host maps it to its own handle.</param>
public sealed record SchemeCommand(SchemeCommandKind Kind, long Voice = 0, SchemeSound? Sound = null, bool Loop = false, int Volume = 0,
    int Balance = 64, int FadeMs = 0, int Stinger = 0);

/// <summary>The audible draws, kept apart from the game's generator (docs/research/audio-evidence.md, randomness).</summary>
public interface IAudioRandom
{
    /// <summary>A uniform integer in <paramref name="min"/>..<paramref name="max"/>, both inclusive.</summary>
    int Next(int min, int max);
}

/// <summary>An <see cref="IAudioRandom"/> over <see cref="System.Random"/>.</summary>
public sealed class SystemAudioRandom(Random random) : IAudioRandom
{
    public int Next(int min, int max) => random.Next(min, max + 1);
}

/// <summary>
/// ARC-UI-041: plays the session's two scheme slots as the original does (docs/research/audio-evidence.md SN12–SN17, checked
/// in the 1.0.7.4 executable), as commands for a host. A slot that changes fades its loop songs (25 ms) and leaves its
/// one-shots playing; a new scheme starts its anchor and transition songs at once. Every tick, which runs once more than
/// 250 ms have passed, walks each slot in list order: an ambient sound inside its hour window fires when a draw 0–999 is
/// below its frequency, at a volume and balance drawn below their maximum, and then the rest of that slot waits for the
/// next tick; a loop song starts inside its window and fades outside it; a transition song that ended reports the restore.
/// Hour windows are inclusive; a reversed one never plays unless <see cref="WrapReversedWindows"/> (the Steamwoven
/// setting) lets it run past midnight. Combat music starts its stinger and loop together when the session's start counter
/// moves and stops at once when the flag clears. Engine-free; time comes from the host.
/// </summary>
public sealed class SchemePlayer
{
    public const int TickMs = 250;
    public const int SchemeFadeMs = 25;
    public const int StingerCount = 6;

    private sealed class Slot
    {
        public SchemeSounds? Scheme;
        public readonly Dictionary<int, long> Voices = [];
    }

    private readonly Slot[] _slots = [new(), new()];
    private long _nextVoice;
    private long? _lastTick;
    private long _combatStarts;
    private bool _combatPlaying;

    /// <param name="wrapReversedWindows">The Steamwoven setting of ledger SN14: a reversed hour window runs past midnight.</param>
    public SchemePlayer(bool wrapReversedWindows, long combatStarts = 0)
    {
        WrapReversedWindows = wrapReversedWindows;
        _combatStarts = combatStarts;
    }

    public bool WrapReversedWindows { get; set; }

    /// <summary>Song voices the player still tracks (loops and one-shots it has not released).</summary>
    public int TrackedVoices => _slots.Sum(slot => slot.Voices.Count);

    /// <summary>Forgets everything after the host stopped every sound (a map opening); a combat start counter seen at that
    /// moment is not a new start.</summary>
    public void Reset(long combatStarts)
    {
        foreach (var slot in _slots) { slot.Scheme = null; slot.Voices.Clear(); }
        _lastTick = null; _combatStarts = combatStarts; _combatPlaying = false;
    }

    /// <summary>One frame: slot changes and combat at once, scheme sounds on the 250 ms tick.</summary>
    /// <param name="nowMs">Monotonic real time in milliseconds.</param>
    /// <param name="hour">The game hour 0–23.</param>
    /// <param name="playing">Whether the host still plays a song voice.</param>
    public IReadOnlyList<SchemeCommand> Update(SchemeInput input, long nowMs, int hour, Func<long, bool> playing, IAudioRandom random)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(playing); ArgumentNullException.ThrowIfNull(random);
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        var commands = new List<SchemeCommand>();
        Rebind(_slots[0], input.Slot0, commands);
        Rebind(_slots[1], input.Slot1, commands);

        if (input.CombatStarts != _combatStarts)
        {
            _combatStarts = input.CombatStarts;
            if (input.CombatMusic) { commands.Add(new(SchemeCommandKind.StartCombat, Stinger: random.Next(1, StingerCount))); _combatPlaying = true; }
        }
        if (!input.CombatMusic && _combatPlaying) { commands.Add(new(SchemeCommandKind.StopCombat)); _combatPlaying = false; }

        if (_lastTick is null || nowMs - _lastTick.Value > TickMs)
        {
            _lastTick = nowMs;
            foreach (var slot in _slots) Tick(slot, hour, input.RestorePending, playing, random, commands);
        }
        return commands;
    }

    // A changed slot: its loops fade, its one-shots keep playing untracked; the new scheme starts its once-songs.
    private void Rebind(Slot slot, SchemeSounds? scheme, List<SchemeCommand> commands)
    {
        if (slot.Scheme?.Id == scheme?.Id) { slot.Scheme = scheme; return; }
        if (slot.Scheme is { } old)
            foreach (var sound in old.Sounds)
                if (sound.Kind == SchemeSoundKind.LoopSong && slot.Voices.TryGetValue(sound.ListNumber, out var voice))
                    commands.Add(new(SchemeCommandKind.FadeOut, voice, sound, FadeMs: SchemeFadeMs));
        slot.Voices.Clear();
        slot.Scheme = scheme;
        if (scheme is null) return;
        foreach (var sound in scheme.Sounds)
            if (sound.Kind is SchemeSoundKind.AnchorSong or SchemeSoundKind.TransitionSong)
                commands.Add(Start(slot, sound, loop: false));
    }

    [OriginalFunction(0x41BAF0, OriginalFunctionScope.Partial)]
    private void Tick(Slot slot, int hour, bool restorePending, Func<long, bool> playing, IAudioRandom random, List<SchemeCommand> commands)
    {
        if (slot.Scheme is not { } scheme) return;
        foreach (var sound in scheme.Sounds)
        {
            switch (sound.Kind)
            {
                case SchemeSoundKind.Ambient:
                    if (!InWindow(sound, hour) || random.Next(0, 999) >= sound.Frequency) break;
                    commands.Add(new(SchemeCommandKind.PlayAmbient, Sound: sound,
                        Volume: Draw(sound.VolumeMin, sound.VolumeMax, random), Balance: Draw(sound.BalanceLeft, sound.BalanceRight, random)));
                    return; // the original leaves the slot for this tick once an ambient fired
                case SchemeSoundKind.TransitionSong:
                    if (slot.Voices.TryGetValue(sound.ListNumber, out var transition) && !playing(transition) && restorePending)
                    {
                        slot.Voices.Remove(sound.ListNumber);
                        commands.Add(new(SchemeCommandKind.TransitionFinished, transition, sound));
                    }
                    break;
                case SchemeSoundKind.LoopSong:
                    var tracked = slot.Voices.TryGetValue(sound.ListNumber, out var loop);
                    if (!InWindow(sound, hour))
                    {
                        if (tracked) { slot.Voices.Remove(sound.ListNumber); commands.Add(new(SchemeCommandKind.FadeOut, loop, sound, FadeMs: SchemeFadeMs)); }
                    }
                    else if (!tracked) commands.Add(Start(slot, sound, loop: true));
                    break;
            }
        }
    }

    private SchemeCommand Start(Slot slot, SchemeSound sound, bool loop)
    {
        var voice = ++_nextVoice;
        slot.Voices[sound.ListNumber] = voice;
        return new(SchemeCommandKind.StartSong, voice, sound, loop, sound.VolumeMin);
    }

    /// <summary>The original's draw: the minimum plus a draw below the width, so the maximum is never reached.</summary>
    private static int Draw(int min, int max, IAudioRandom random) => max > min ? min + random.Next(0, max - min - 1) : min;

    public bool InWindow(SchemeSound sound, int hour) => sound.HourStart <= sound.HourEnd
        ? hour >= sound.HourStart && hour <= sound.HourEnd
        : WrapReversedWindows && (hour >= sound.HourStart || hour <= sound.HourEnd);
}
