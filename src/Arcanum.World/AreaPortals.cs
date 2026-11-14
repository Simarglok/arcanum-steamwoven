using System.Diagnostics.CodeAnalysis;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Locks;

namespace Arcanum.World;

/// <summary>One door or window of the shown map: its object, session entity, T0 lock facts, ART facts and frames.</summary>
public sealed record AreaPortal(AreaObjectKey Key, string Entity, AreaStepCell Cell, SessionOpenableBaseline Baseline, PortalArt Art, int FramesPerSecond,
    bool UnlockScript, IReadOnlyList<AreaVisualRecord> Frames)
{
    /// <summary>ARC-SKILLS-006: the lock's difficulty (portal field 47, the record's slot 37), bounded to the session's range.</summary>
    public int LockDifficulty { get; init; }

    /// <summary>ARC-SCRIPT-1778 (ledger AD1): the art id a script's change art number wrote (frame 0, the frame bits of the id are not kept: the session's frame is the open state); null while the portal has the art its map data gives.
    /// <see cref="Art"/>, <see cref="FramesPerSecond"/> and <see cref="Frames"/> are then those of that art.</summary>
    public uint? ArtId { get; init; }

    /// <summary>The visual of one frame of the portal's ART, or null when the ART has no such frame.</summary>
    public AreaVisualRecord? Visual(int frame) => Frames.FirstOrDefault(v => (int)((v.RawArt >> 14) & 31) == frame);

    /// <summary>The frame its open steps end on: 0 when it cannot open (one frame).</summary>
    public int OpenFrame => PortalFrameRules.Toggled(Art, 0);

    /// <summary>One animation step: 1000 / the ART's frame rate in ms, 100 ms when the rate is not positive (ledger LK11).</summary>
    public long StepMs => FramesPerSecond > 0 ? Math.Max(1, 1000 / FramesPerSecond) : 100;
}

/// <summary>What a use of a door or window did (ARC-WORLD-051).</summary>
public enum AreaPortalUse { Opened, Closed, Locked, Jammed, MagicallyHeld, ScriptGuarded, CannotOpen }

/// <summary>
/// ARC-WORLD-051: doors and windows of the shown map at runtime. Their state lives in the session (ARC-WORLD-050): a step
/// admits a portal by its current state (<see cref="Passages"/>), a step that crosses a closed portal it may pass opens it,
/// and a use toggles it after the player's open attempt. The frames come from the map's own ART (the area owner decodes
/// every frame of a portal ART of several frames).
/// </summary>
public sealed class AreaPortals
{
    private const int FlagsSlot = 36, DifficultySlot = 37, KeySlot = 38;
    /// <summary>ARC-SKILLS-006: the range of the use-item-with-skill goal that picks a lock (1, ledger PL3).</summary>
    public const int PickRange = 1;
    private readonly Dictionary<AreaObjectKey, AreaPortal> _portals;
    // ARC-SCRIPT-1778: the portals as the map's data gives them (a portal a script gave another art returns to its entry here), and the portals by session entity.
    private readonly Dictionary<AreaObjectKey, AreaPortal> _original;
    private readonly Dictionary<string, AreaObjectKey> _byEntity;

    private AreaPortals(SessionMapId map, Dictionary<AreaObjectKey, AreaPortal> portals)
    {
        Map = map; _portals = portals; _original = new(portals); _byEntity = portals.ToDictionary(p => p.Value.Entity, p => p.Key, StringComparer.Ordinal);
    }

    /// <summary>The portals of a map made from already-built facts (a synthetic map of a test or a tool); <see cref="From"/> reads them from the map's data.</summary>
    public static AreaPortals Of(SessionMapId map, IEnumerable<AreaPortal> portals)
    {
        ArgumentNullException.ThrowIfNull(portals);
        return new(map, portals.ToDictionary(p => p.Key));
    }

    public SessionMapId Map { get; }
    public IReadOnlyCollection<AreaPortal> All => _portals.Values;

    public bool TryGet(AreaObjectKey key, [MaybeNullWhen(false)] out AreaPortal portal) => _portals.TryGetValue(key, out portal);

    /// <summary>ARC-SCRIPT-1778: the portal of a session entity, as it is now (a script may have given it another art), or null.</summary>
    public AreaPortal? ByEntity(string entity) => entity is not null && _byEntity.TryGetValue(entity, out var key) ? _portals[key] : null;

    /// <summary>
    /// ARC-SCRIPT-1778 (ledger AD1): gives the portal the art a script's change art number wrote: <paramref name="artId"/> (null: the art its map data gives, which returns the original entry), with
    /// the facts of that art read by the caller (<paramref name="art"/>: door or window, rotation and frame count; <paramref name="framesPerSecond"/>; <paramref name="frames"/>: every frame of its group, in frame order).
    /// What reads the portal afterwards (the open steps, the passage rule, the draw, a script's toggle) reads the new art's frame count. False when the portal is not of this map or already has this art.
    /// </summary>
    public bool Retarget(AreaObjectKey key, uint? artId, PortalArt art, int framesPerSecond, IReadOnlyList<AreaVisualRecord> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!_portals.TryGetValue(key, out var current) || current.ArtId == artId) return false;
        if (artId is null) { _portals[key] = _original[key]; return true; }
        _portals[key] = _original[key] with { ArtId = artId, Art = art, FramesPerSecond = framesPerSecond, Frames = frames };
        return true;
    }

    /// <summary>The drawn doors and windows of a plan: every core portal body with its lock fields and ART. A map without a
    /// runtime number (not in the module's map list) has no session state, so its portals stay at T0 (none listed).</summary>
    public static AreaPortals From(AreaSnapshot snapshot, AreaScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(plan);
        if (snapshot.RuntimeMap < 1) return new(new(snapshot.RuntimeMap), []);
        var visuals = snapshot.Visuals.Where(v => v.Key.Layer == AreaLayerKind.Body && v.Key.Object is not null && v.RawArt >> 28 == 3)
            .ToDictionary(v => v.Key.Object!.Value);
        var drawn = plan.Commands.Where(c => c.Identity is not null && c.ArtClass == 3).Select(c => c.Identity!.Value).ToHashSet();
        var portals = new Dictionary<AreaObjectKey, AreaPortal>();
        foreach (var row in snapshot.Objects)
        {
            if (row.Type != 1 || !drawn.Contains(row.Key) || !visuals.TryGetValue(row.Key, out var body) || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            long Field(int slot) => row.EffectiveFields.SingleOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0;
            var raw = body.RawArt;
            var frames = plan.PortalFrames.TryGetValue(row.Key, out var all) ? all : [body];
            var art = new PortalArt(((raw >> 10) & 1) != 0, (int)((raw >> 11) & 7), Math.Max(1, body.FrameCount));
            // A busted ART (bit 9) stands for the busted bit the original sets with it (ledger LK12).
            var flags = unchecked((int)Field(FlagsSlot)) | ((raw & 0x200) != 0 ? LockRules.BustedBit : 0);
            var baseline = new SessionOpenableBaseline(OpenableKind.Portal, flags, unchecked((int)Field(KeySlot)), (int)((raw >> 14) & 31));
            var script = AreaStepInspector.HasUnlockScript(row);
            portals.Add(row.Key, new(row.Key, AreaUseScriptHost.EntityId(row), new(x, y), baseline, art, body.FramesPerSecond, script, frames)
            {
                LockDifficulty = (int)Math.Clamp(Field(DifficultySlot), -GameSessionOwner.MaxLockDifficulty, GameSessionOwner.MaxLockDifficulty),
            });
        }
        return new(new(snapshot.RuntimeMap), portals);
    }

    /// <summary>The portal's recorded frame (the session's, else its T0 frame).</summary>
    public int Frame(GameSessionOwner session, AreaPortal portal) => session.QueryOpenable(Openable(portal), portal.Baseline).Frame;

    /// <summary>
    /// The runtime passage of every portal, read from the session at each lookup: open (any frame but 0, or busted) passes;
    /// an unlock script leaves the T0 rule; otherwise the player's open attempt decides (ledger LK7): it opens, a lock
    /// passes only a step leaving a building (unless always locked), jammed and held block.
    /// </summary>
    public IReadOnlyDictionary<AreaObjectKey, AreaPortalPassage> Passages(GameSessionOwner session) => new LivePassages(this, session);

    /// <summary>The closed portals a started step crosses and may pass, opened to their last frame in the session (one
    /// revision each); the caller animates them. A portal that cannot open (one frame) is not listed.</summary>
    public IReadOnlyList<AreaPortal> OpenCrossed(GameSessionOwner session, AreaStepAssessment step)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(step);
        var opened = new List<AreaPortal>();
        if (step.Disposition != CardinalTraversalDisposition.Open) return opened;
        foreach (var key in step.Portals)
        {
            if (!_portals.TryGetValue(key, out var portal) || Frame(session, portal) != 0 || portal.OpenFrame == 0) continue;
            if (session.SetOpenableFrame(session.Version, Openable(portal), portal.Baseline, portal.OpenFrame).Disposition == GameSessionDisposition.Changed)
                opened.Add(portal);
        }
        return opened;
    }

    /// <summary>ARC-DEV-138 (MIG13 S7): the closed portals a started step crosses and may pass, in the order the step lists them; nothing is written. The caller starts each
    /// portal's own run (<see cref="AreaDoorHost.Open"/>), whose steps open it in the session. A portal that cannot open (one frame) is not listed.</summary>
    public IReadOnlyList<AreaPortal> CrossedClosed(GameSessionOwner session, AreaStepAssessment step)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(step);
        var closed = new List<AreaPortal>();
        if (step.Disposition != CardinalTraversalDisposition.Open) return closed;
        foreach (var key in step.Portals)
            if (_portals.TryGetValue(key, out var portal) && Frame(session, portal) == 0 && portal.OpenFrame != 0) closed.Add(portal);
        return closed;
    }

    /// <summary>ARC-SKILLS-006: the hero picks the portal's lock with <paramref name="lockpick"/> (null: no lockpicks), at the light
    /// <paramref name="light"/> of the portal (full light when the host has none). The session's answer; nothing opens. In a turn-based
    /// round (ARC-COMBAT-020) <paramref name="world"/> is its world, and the pick charges its action points.</summary>
    public SessionPickLockResult Pick(GameSessionOwner session, AreaPortal portal, SessionLockpick? lockpick, int light = SessionPickLockTarget.FullLight,
        ISessionCombatWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(portal);
        return session.PickLock(session.Version, PickTarget(portal, light), lockpick, world);
    }

    /// <summary>ARC-COMBAT-1776: the session's pick-locks target of the portal at the light <paramref name="light"/> (the hero's <see cref="Pick"/> and an NPC's <see cref="GameSessionOwner.PickLockByNpc"/> read the same).</summary>
    public SessionPickLockTarget PickTarget(AreaPortal portal, int light = SessionPickLockTarget.FullLight)
    {
        ArgumentNullException.ThrowIfNull(portal);
        return new(Openable(portal), portal.Baseline, portal.LockDifficulty, light);
    }

    /// <summary>
    /// ARC-DEV-138 (MIG13 S7): what a use of the portal by the player will do, decided and not written (FR-SCENE-014): an open portal is to close (<see cref="AreaPortalUse.Closed"/>;
    /// only a magically held door refuses, GT41), a closed one is to open when the player's open attempt succeeds (<see cref="AreaPortalUse.Opened"/>), else the attempt's
    /// refusal. The portal's own run does the change (<see cref="AreaDoorHost"/>).
    /// </summary>
    public AreaPortalUse Decide(GameSessionOwner session, AreaPortal portal)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(portal);
        var openable = Openable(portal);
        var state = session.QueryOpenable(openable, portal.Baseline);
        if (state.Frame != 0)
            return (state.Flags & LockRules.MagicallyHeldBit) != 0 ? AreaPortalUse.MagicallyHeld : AreaPortalUse.Closed;
        return OpenRefusal(session, portal) ?? (portal.OpenFrame == 0 ? AreaPortalUse.CannotOpen : AreaPortalUse.Opened);
    }

    /// <summary>The open attempt's refusal for the player (ledger LK7), null when the portal opens: a busted portal opens before the unlock script is asked.</summary>
    public AreaPortalUse? OpenRefusal(GameSessionOwner session, AreaPortal portal)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(portal);
        var openable = Openable(portal);
        if (portal.UnlockScript && (session.QueryOpenable(openable, portal.Baseline).Flags & LockRules.BustedBit) == 0) return AreaPortalUse.ScriptGuarded;
        return session.AttemptOpen(openable, portal.Baseline, scriptRefused: false) switch
        {
            OpenAttempt.Opens => null,
            OpenAttempt.Locked => AreaPortalUse.Locked,
            OpenAttempt.Jammed => AreaPortalUse.Jammed,
            OpenAttempt.MagicallyHeld => AreaPortalUse.MagicallyHeld,
            _ => AreaPortalUse.ScriptGuarded,
        };
    }

    /// <summary>A use of the portal by the player (FR-SCENE-014), written at once: <see cref="Decide"/>, then the final frame in one revision. The viewer does not call it since
    /// ARC-DEV-138 (the portal's own run steps the frames); it stays for a caller that wants the whole change now.</summary>
    public AreaPortalUse Use(GameSessionOwner session, AreaPortal portal)
    {
        var decided = Decide(session, portal);
        if (decided is not (AreaPortalUse.Opened or AreaPortalUse.Closed)) return decided;
        var target = decided == AreaPortalUse.Opened ? portal.OpenFrame : PortalFrameRules.Closed;
        return session.SetOpenableFrame(session.Version, Openable(portal), portal.Baseline, target).Disposition == GameSessionDisposition.Rejected ? AreaPortalUse.CannotOpen : decided;
    }

    public SessionOpenable Openable(AreaPortal portal) => new(Map, portal.Entity);

    private AreaPortalPassage Passage(GameSessionOwner session, AreaPortal portal)
    {
        var state = session.QueryOpenable(Openable(portal), portal.Baseline);
        // ARC-COMBAT-008: a door an attack destroyed is gone.
        if (state.Open || (state.Flags & LockRules.BustedBit) != 0 ||
            session.QueryObjectCondition(Map, portal.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Portal) == SessionObjectCondition.Destroyed)
            return AreaPortalPassage.Open;
        if (portal.UnlockScript) return AreaPortalPassage.Unresolved;
        return session.AttemptOpen(Openable(portal), portal.Baseline, scriptRefused: false) switch
        {
            OpenAttempt.Opens => AreaPortalPassage.Open,
            OpenAttempt.Locked => (state.Flags & LockRules.AlwaysLockedBit) != 0 ? AreaPortalPassage.Blocked : AreaPortalPassage.LeavingOnly,
            OpenAttempt.Jammed or OpenAttempt.MagicallyHeld => AreaPortalPassage.Blocked,
            _ => AreaPortalPassage.Unresolved,
        };
    }

    private sealed class LivePassages(AreaPortals owner, GameSessionOwner session) : IReadOnlyDictionary<AreaObjectKey, AreaPortalPassage>
    {
        public AreaPortalPassage this[AreaObjectKey key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        public IEnumerable<AreaObjectKey> Keys => owner._portals.Keys;
        public IEnumerable<AreaPortalPassage> Values => Keys.Select(key => this[key]);
        public int Count => owner._portals.Count;
        public bool ContainsKey(AreaObjectKey key) => owner._portals.ContainsKey(key);
        public bool TryGetValue(AreaObjectKey key, out AreaPortalPassage value)
        {
            if (!owner._portals.TryGetValue(key, out var portal)) { value = default; return false; }
            value = owner.Passage(session, portal); return true;
        }
        public IEnumerator<KeyValuePair<AreaObjectKey, AreaPortalPassage>> GetEnumerator() =>
            Keys.Select(key => new KeyValuePair<AreaObjectKey, AreaPortalPassage>(key, this[key])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
