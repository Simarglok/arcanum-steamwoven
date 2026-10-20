using System.Diagnostics.CodeAnalysis;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Locks;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One door or window of the shown map: its object, session entity, T0 lock facts, ART facts and frames.</summary>
public sealed record AreaPortal(AreaObjectKey Key, string Entity, AreaStepCell Cell, SessionOpenableBaseline Baseline, PortalArt Art, int FramesPerSecond,
    bool UnlockScript, IReadOnlyList<AreaVisualRecord> Frames)
{
    /// <summary>The visual of one frame of the portal's ART, or null when the ART has no such frame.</summary>
    public AreaVisualRecord? Visual(int frame) => Frames.FirstOrDefault(v => (int)((v.RawArt >> 14) & 31) == frame);

    /// <summary>The frame its open steps end on: 0 when it cannot open (one frame).</summary>
    public int OpenFrame => PortalFrameRules.Toggled(Art, 0);

    /// <summary>One animation step: 1000 / the ART's frame rate in ms, 100 ms when the rate is not positive (ledger LK11).</summary>
    public long StepMs => FramesPerSecond > 0 ? 1000 / FramesPerSecond : 100;
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
    private const int FlagsSlot = 36, KeySlot = 38, ScriptsSlot = 31, UnlockScriptPoint = 3;
    private readonly Dictionary<AreaObjectKey, AreaPortal> _portals;

    private AreaPortals(SessionMapId map, Dictionary<AreaObjectKey, AreaPortal> portals) { Map = map; _portals = portals; }

    public SessionMapId Map { get; }
    public IReadOnlyCollection<AreaPortal> All => _portals.Values;

    public bool TryGet(AreaObjectKey key, [MaybeNullWhen(false)] out AreaPortal portal) => _portals.TryGetValue(key, out portal);

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
            var script = row.EffectiveFields.SingleOrDefault(f => f.Slot == ScriptsSlot)?.Elements.Any(e => e.Key == UnlockScriptPoint && e.Words.Count == 3 && e.Words[2] != 0) == true;
            portals.Add(row.Key, new(row.Key, AreaUseScriptHost.EntityId(row), new(x, y), baseline, art, body.FramesPerSecond, script, frames));
        }
        return new(new(snapshot.RuntimeMap), portals);
    }

    /// <summary>The portal's recorded frame (the session's, else its T0 frame).</summary>
    public int Frame(GameSessionOwner session, AreaPortal portal) => session.QueryOpenable(Openable(portal), portal.Baseline).Frame;

    /// <summary>
    /// The runtime passage of every portal, read from the session at each lookup: open (any frame but 0, or busted) passes;
    /// an unlock script leaves the T0 rule; otherwise the player's open attempt decides (ledger LK7): it opens, a lock
    /// passes only a step leaving a building (unless always locked: the session holds no keys), jammed and held block.
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

    /// <summary>A use of the portal by the player (FR-SCENE-014): an open portal closes; a closed one opens when the
    /// player's open attempt succeeds, else the attempt's refusal is returned and nothing changes.</summary>
    public AreaPortalUse Use(GameSessionOwner session, AreaPortal portal)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(portal);
        var openable = Openable(portal);
        var frame = Frame(session, portal);
        if (frame != 0)
            return session.SetOpenableFrame(session.Version, openable, portal.Baseline, PortalFrameRules.Closed).Disposition == GameSessionDisposition.Rejected
                ? AreaPortalUse.CannotOpen : AreaPortalUse.Closed;
        if (portal.UnlockScript) return AreaPortalUse.ScriptGuarded;
        switch (session.AttemptOpen(openable, portal.Baseline, scriptRefused: false))
        {
            case OpenAttempt.Locked: return AreaPortalUse.Locked;
            case OpenAttempt.Jammed: return AreaPortalUse.Jammed;
            case OpenAttempt.MagicallyHeld: return AreaPortalUse.MagicallyHeld;
            case OpenAttempt.ScriptRefused: return AreaPortalUse.ScriptGuarded;
        }
        if (portal.OpenFrame == 0) return AreaPortalUse.CannotOpen;
        return session.SetOpenableFrame(session.Version, openable, portal.Baseline, portal.OpenFrame).Disposition == GameSessionDisposition.Rejected
            ? AreaPortalUse.CannotOpen : AreaPortalUse.Opened;
    }

    public SessionOpenable Openable(AreaPortal portal) => new(Map, portal.Entity);

    private AreaPortalPassage Passage(GameSessionOwner session, AreaPortal portal)
    {
        var state = session.QueryOpenable(Openable(portal), portal.Baseline);
        if (state.Open || (state.Flags & LockRules.BustedBit) != 0) return AreaPortalPassage.Open;
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
