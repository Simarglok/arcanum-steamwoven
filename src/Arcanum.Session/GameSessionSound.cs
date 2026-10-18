using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// The saved music and ambient state (ARC-WORLD-040; the original's sound save, docs/research/audio-evidence.md SN18): the
/// scheme in each of the two slots (0 = empty), whether a finished transition song still has to restore the pair it
/// replaced and that pair, and whether combat music plays with the world pair it will restore. What plays from these
/// identities, and when within their hours, is the player's (ARC-UI-041).
/// </summary>
public sealed record SessionSoundState(int Slot0, int Slot1, bool RestorePending, int Restore0, int Restore1,
    bool CombatMusic, int PreCombat0, int PreCombat1)
{
    public static SessionSoundState Empty { get; } = new(0, 0, false, 0, 0, false, 0, 0);
}

/// <summary>The live sound view: the saved state, the movie lock depth and a counter of combat-music starts (neither is
/// saved, as in the original; a player starts the combat stinger when the counter moves, so a load never replays it).</summary>
public sealed record SessionSoundView(SessionSoundState State, int LockDepth, long CombatStarts);

/// <summary>What the session needs to know of a scheme id from the module's catalogue: whether loading it fills its slot
/// (an id without a list base, such as "No Sound", leaves the slot empty) and whether it starts a transition song.</summary>
public interface ISessionSoundSchemes
{
    bool Loads(int schemeId);
    bool StartsTransition(int schemeId);
}

/// <summary>A map's default music and ambient pair (its map info's <c>SoundScheme</c> line).</summary>
public readonly record struct SessionMapSound(int Music, int Ambient);

/// <summary>A sound call's disposition and the version after it.</summary>
public sealed record SessionSoundResult(GameSessionDisposition Disposition, GameSessionFailure Failure, GameSessionVersion Version);

/// <summary>
/// The sound rule settings of owner decision D8 (docs/world/rule-modes.md). Each switch is a bug of the original game
/// confirmed in its executable (audio study).
/// </summary>
public sealed record SoundRuleProfile
{
    public required string Name { get; init; }

    /// <summary>
    /// Ledger SN18: a save made while combat music plays stores empty slots; on load the combat flag defers that empty pair
    /// over the saved pre-combat pair, so combat stays silent and leaving it restores silence, as the retail executable does
    /// (save 0x41B1D0, load 0x41B0F0). Steamwoven keeps the saved pre-combat pair.
    /// </summary>
    public bool CombatSaveLosesSchemes { get; init; }

    /// <summary>
    /// Ledger SN14: a scheme sound's hour window is inclusive and never wraps, so a reversed window (20 installed night
    /// entries such as <c>19-0</c>) never plays, as the retail executable does (0x41BAF0). Steamwoven runs it past midnight.
    /// The scheme player reads it (ARC-UI-041).
    /// </summary>
    public bool HourWindowsNeverWrap { get; init; }

    public static readonly SoundRuleProfile Steamwoven = new() { Name = "steamwoven" };
    public static readonly SoundRuleProfile Original = new() { Name = "original", CombatSaveLosesSchemes = true, HourWindowsNeverWrap = true };
    public static IReadOnlyList<SoundRuleProfile> All { get; } = new[] { Steamwoven, Original };
    public static SoundRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// The original's scheme slots (audio study SN10–SN12, SN15, SN17–SN20, checked in the 1.0.7.4 executable), as identities
/// only: requests, slot filling and stopping, the transition restore, combat music and the movie lock. Fades, songs and
/// ambient chances are the player's.
/// </summary>
internal sealed class SoundDomain
{
    private readonly int[] _slots = new int[2];
    private readonly int[] _restore = new int[2];
    private readonly int[] _preCombat = new int[2];
    private readonly int[] _preLock = new int[2];
    private bool _restorePending;
    private bool _combat;

    internal int LockDepth { get; private set; }
    internal long CombatStarts { get; private set; }

    internal SoundDomain Clone()
    {
        var copy = new SoundDomain { _restorePending = _restorePending, _combat = _combat, LockDepth = LockDepth, CombatStarts = CombatStarts };
        _slots.CopyTo(copy._slots, 0); _restore.CopyTo(copy._restore, 0); _preCombat.CopyTo(copy._preCombat, 0); _preLock.CopyTo(copy._preLock, 0);
        return copy;
    }

    internal bool SameAs(SoundDomain other) => Capture() == other.Capture() && LockDepth == other.LockDepth && CombatStarts == other.CombatStarts
        && _preLock.SequenceEqual(other._preLock);

    internal SessionSoundState Capture() => new(_slots[0], _slots[1], _restorePending, _restore[0], _restore[1], _combat, _preCombat[0], _preCombat[1]);

    internal static bool Valid(SessionSoundState state) =>
        state.Slot0 >= 0 && state.Slot1 >= 0 && state.Restore0 >= 0 && state.Restore1 >= 0 && state.PreCombat0 >= 0 && state.PreCombat1 >= 0
        && (state.Slot0 == 0 || state.Slot0 != state.Slot1) && !(state.CombatMusic && (state.Slot0 != 0 || state.Slot1 != 0));

    /// <summary>The original's load. The map loads first and opens the saved map, which plays its default pair; then the
    /// sound load sets the saved restore and combat state and requests the saved slots. While combat music is flagged that
    /// request only replaces the restore target (the original's bug); Steamwoven keeps the saved pre-combat pair and leaves
    /// the slots empty, as they were when the game was saved.</summary>
    [OriginalFunction(0x41B0F0, OriginalFunctionScope.Partial)]
    internal static SoundDomain Import(SessionSoundState saved, SoundRuleProfile rules, SessionMapSound? mapDefault)
    {
        var domain = new SoundDomain();
        domain.EnterMap(mapDefault, null);
        domain._restorePending = saved.RestorePending; domain._combat = saved.CombatMusic;
        (domain._restore[0], domain._restore[1]) = (saved.Restore0, saved.Restore1);
        (domain._preCombat[0], domain._preCombat[1]) = (saved.PreCombat0, saved.PreCombat1);
        if (saved.CombatMusic && !rules.CombatSaveLosesSchemes) domain._slots[0] = domain._slots[1] = 0;
        else domain.Play(saved.Slot0, saved.Slot1, null);
        return domain;
    }

    /// <summary>A scheme request (the original's scheme call): dropped while locked; an ambient equal to the music becomes
    /// 0; while combat music plays it only becomes the pair restored afterwards; otherwise ids already in a slot keep
    /// playing, a slot no longer requested is stopped and the others load. 0 leaves a slot empty.</summary>
    [OriginalFunction(0x41BD50, OriginalFunctionScope.Full)]
    internal bool Play(int music, int ambient, ISessionSoundSchemes? schemes)
    {
        if (LockDepth > 0) return false;
        var before = Capture();
        if (music == ambient) ambient = 0;
        if (_combat) { (_preCombat[0], _preCombat[1]) = (music, ambient); return Capture() != before; }
        var musicSlot = SlotOf(music); var ambientSlot = SlotOf(ambient);
        if (musicSlot < 0 && ambientSlot < 0) { Stop(0); Stop(1); Load(music, schemes); Load(ambient, schemes); }
        else if (musicSlot >= 0 && ambientSlot < 0) { Stop(1 - musicSlot); Load(ambient, schemes); }
        else if (musicSlot < 0) { Stop(1 - ambientSlot); Load(music, schemes); }
        return Capture() != before;
    }

    private int SlotOf(int id) => id == 0 ? -1 : _slots[0] == id ? 0 : _slots[1] == id ? 1 : -1;

    // The slot half of the original's scheme load (0x41BE20); its parsing half is the catalogue's (registry row).
    private void Load(int id, ISessionSoundSchemes? schemes)
    {
        if (id == 0) return;
        var slot = _slots[0] == 0 ? 0 : _slots[1] == 0 ? 1 : -1;
        if (slot < 0 || !(schemes?.Loads(id) ?? true)) return;
        _slots[slot] = id;
        if (schemes?.StartsTransition(id) == true) _restorePending = true;
    }

    [OriginalFunction(0x41BA20, OriginalFunctionScope.Partial)]
    private void Stop(int slot)
    {
        if (!_restorePending) _restore[slot] = _slots[slot];
        _slots[slot] = 0;
    }

    /// <summary>The original's stop of every sound: both slots stop and a pending restore is dropped.</summary>
    [OriginalFunction(0x41C340, OriginalFunctionScope.Partial)]
    internal void StopAll()
    {
        Stop(0); Stop(1);
        _restorePending = false;
    }

    /// <summary>A map opens: every sound stops, combat music and the lock are reset, then the map's default pair plays when
    /// its map info has one.</summary>
    [OriginalFunction(0x41B060, OriginalFunctionScope.Partial)]
    internal void EnterMap(SessionMapSound? mapDefault, ISessionSoundSchemes? schemes)
    {
        StopAll();
        _combat = false; LockDepth = 0;
        if (mapDefault is { } pair) Play(pair.Music, pair.Ambient, schemes);
    }

    /// <summary>An NPC entered combat mode: unless combat music already plays, the slots are saved as the restore target
    /// and stopped, and combat music starts.</summary>
    [OriginalFunction(0x41C4D0, OriginalFunctionScope.Partial)]
    internal void StartCombatMusic()
    {
        if (_combat) return;
        (_preCombat[0], _preCombat[1]) = (_slots[0], _slots[1]);
        Stop(0); Stop(1);
        _combat = true;
        CombatStarts++;
    }

    /// <summary>The local PC left combat mode: combat music stops and the latest requested world pair plays.</summary>
    [OriginalFunction(0x41C5A0, OriginalFunctionScope.Partial)]
    internal void StopCombatMusic(ISessionSoundSchemes? schemes)
    {
        if (!_combat) return;
        _combat = false;
        Play(_preCombat[0], _preCombat[1], schemes);
    }

    /// <summary>A transition song finished (the player reports it): the pair it replaced is requested again.</summary>
    internal void FinishTransition(ISessionSoundSchemes? schemes)
    {
        if (!_restorePending) return;
        var (music, ambient) = (_restore[0], _restore[1]);
        Play(music, ambient, schemes);
        _restorePending = false;
    }

    [OriginalFunction(0x41C610, OriginalFunctionScope.Partial)]
    internal void Lock()
    {
        if (LockDepth == 0)
        {
            (_preLock[0], _preLock[1]) = (_slots[0], _slots[1]);
            Stop(0); Stop(1);
        }
        LockDepth++;
    }

    [OriginalFunction(0x41C660, OriginalFunctionScope.Partial)]
    internal void Unlock(ISessionSoundSchemes? schemes)
    {
        if (LockDepth == 0) return;
        if (--LockDepth == 0) Play(_preLock[0], _preLock[1], schemes);
    }
}

public sealed partial class GameSessionOwner
{
    private SoundDomain _sound = new();
    private ISessionSoundSchemes? _soundSchemes;

    /// <summary>The scheme facts of the module's catalogue, attached by the host after it opens the catalogue (not saved).
    /// Without them every non-zero id fills its slot and none starts a transition.</summary>
    public void AttachSoundSchemes(ISessionSoundSchemes? schemes) { ThrowIfDisposed(); _soundSchemes = schemes; }

    public SessionSoundView QuerySound() { ThrowIfDisposed(); return new(_sound.Capture(), _sound.LockDepth, _sound.CombatStarts); }

    /// <summary>A map opened in play (a transition or a reload of the area, not a game load: <see cref="Restore"/> takes the
    /// loaded map's default through <see cref="GameSessionRestoreOptions.SoundMapDefault"/>): every sound stops and the map's
    /// default pair (null when its map info has no <c>SoundScheme</c> line) plays.</summary>
    public SessionSoundResult EnterMapSound(GameSessionVersion expected, SessionMapSound? mapDefault) =>
        SoundChange(expected, mapDefault is { } pair && (pair.Music < 0 || pair.Ambient < 0), sound => sound.EnterMap(mapDefault, _soundSchemes));

    /// <summary>The local PC was placed in another sector: the sector's pair plays unless both ids are 0.</summary>
    public SessionSoundResult EnterSectorSound(GameSessionVersion expected, int music, int ambient) =>
        SoundChange(expected, music < 0 || ambient < 0, sound => { if (music != 0 || ambient != 0) sound.Play(music, ambient, _soundSchemes); });

    /// <summary>A script's scheme request (<c>play_sound_scheme</c>), unchanged.</summary>
    public SessionSoundResult PlaySoundScheme(GameSessionVersion expected, int music, int ambient) =>
        SoundChange(expected, music < 0 || ambient < 0, sound => sound.Play(music, ambient, _soundSchemes));

    /// <summary>The player reports that a transition song ended.</summary>
    public SessionSoundResult FinishSoundTransition(GameSessionVersion expected) =>
        SoundChange(expected, false, sound => sound.FinishTransition(_soundSchemes));

    /// <summary>A movie starts (the lock nests): the slots stop and every scheme request is dropped until the last unlock.</summary>
    public SessionSoundResult LockSound(GameSessionVersion expected) => SoundChange(expected, false, sound => sound.Lock());

    public SessionSoundResult UnlockSound(GameSessionVersion expected) => SoundChange(expected, false, sound => sound.Unlock(_soundSchemes));

    // The change runs on a copy: NoChange when the copy equals the state, a revision only for a real change.
    private SessionSoundResult SoundChange(GameSessionVersion expected, bool invalid, Action<SoundDomain> change)
    {
        ThrowIfDisposed();
        if (invalid) return new(GameSessionDisposition.Rejected, GameSessionFailure.InvalidSound, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        var next = _sound.Clone();
        change(next);
        if (next.SameAs(_sound)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        _sound = next;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    private static GameSessionFailure ImportSound(SessionSoundState? saved, SoundRuleProfile rules, SessionMapSound? mapDefault, out SoundDomain sound)
    {
        sound = new();
        if (saved is null) return GameSessionFailure.InvalidSnapshot;
        if (!SoundDomain.Valid(saved) || mapDefault is { } pair && (pair.Music < 0 || pair.Ambient < 0)) return GameSessionFailure.InvalidSound;
        sound = SoundDomain.Import(saved, rules, mapDefault);
        return GameSessionFailure.None;
    }
}
