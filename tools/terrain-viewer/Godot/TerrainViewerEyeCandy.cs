using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-017: the spells' eye candy in the AREA view (ledger EC rows). The casting goal plays the cast entry (6·spell) on the hero
// and is timed by it (CastEyeCandyAnimation: the release at the timing part's action frame, the end one pace after the step
// that finds its last frame);
// the release plays the secondary cast entry (6·spell + 4) on the hero and the EyeCandy components of the run play or remove their
// entries on their targets, each as its own animation (EyeCandyAnimation). Each part (foreground, background, underlay) is drawn as an
// extra-art body at its object's tile: the background and the underlay right before the object, the foreground right after it
// (the object's own draw order), additive when translucent, at the art id's scale. A play needs a free slot of each list it places
// in (7 foreground, 7 background, 4 underlay per object, 0x4CCF70). Not ready: lights, sounds, tinting, turning a multi-rotation eye
// candy to its object's facing, the object's scale and offsets, the eye-candy callbacks.
public partial class TerrainViewerApp
{
    /// <summary>One eye candy shown on an object: the entry (table index), its art id (frame 0), its parts and its animation.</summary>
    private sealed class ShownEyeCandy
    {
        public required int Sequence { get; init; }
        public required string Object { get; init; }
        public required int Index { get; init; }
        public required int Run { get; init; }
        public required uint Art { get; init; }
        public required EyeCandyParts Parts { get; init; }
        public CastEyeCandyAnimation? Cast { get; init; }
        public EyeCandyAnimation? Play { get; init; }
        public int Frame => Cast?.Frame ?? Play?.Frame ?? 0;
        public int BackgroundFrame => Cast?.BackgroundFrame ?? Play?.BackgroundFrame ?? 0;
        public bool Shown => Cast is { } cast ? !cast.Ended : Play?.Shown == true;
    }

    private readonly List<ShownEyeCandy> _eyeCandies = [];
    private Dictionary<AreaObjectKey, AreaDrawCommand> _eyeCandyBodies = [];
    private int _eyeCandySequence;
    // Smoke seams: the entries drawn, and the cast animations' release and end times after their start.
    private readonly HashSet<int> _eyeCandyDrawnForSelfTest = [];
    private readonly List<(int Index, long ReleaseMs, long EndMs, bool Immediate)> _castTimingsForSelfTest = [];

    private SpellEyeCandyTable? EyeCandyTable => _spellData?.EyeCandy;
    private const int ForegroundSlots = 7, BackgroundSlots = 7, UnderlaySlots = 4;

    /// <summary>Whether the object has a free slot in every list the parts go to (0x4CCF70: a full list places nothing).</summary>
    private bool EyeCandySlotsFree(string obj, EyeCandyParts parts)
    {
        int Count(Func<EyeCandyParts, bool> part) => _eyeCandies.Count(e => e.Object == obj && e.Shown && part(e.Parts));
        return (!parts.Foreground || Count(p => p.Foreground) < ForegroundSlots) && (!parts.Background || Count(p => p.Background) < BackgroundSlots)
            && (!parts.Underlay || Count(p => p.Underlay) < UnderlaySlots);
    }

    private static EyeCandyParts PartsOf(SpellEyeCandyFlags flags) =>
        new((flags & SpellEyeCandyFlags.Foreground) != 0, (flags & SpellEyeCandyFlags.Background) != 0, (flags & SpellEyeCandyFlags.Underlay) != 0);

    /// <summary>The clip of an eye-candy part (its ART's frames, action frame and rate); null when the ART does not read.</summary>
    private EyeCandyClip? EyeCandyClipOf(uint art, EyeCandyVariant variant)
    {
        if (ExtraArt() is not { } extra) return null;
        var aid = EyeCandyArt.WithVariant(art, variant);
        var load = extra.Load(aid);
        if (load.Frame is not { } frame)
        {
            if (_groundReported.Add($"eye-candy:{aid}")) GD.Print($"ARC-MAGIC-017 eye-candy-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
            return null;
        }
        return new(frame.FramesPerGroup, frame.ActionFrame, frame.FramesPerSecond);
    }

    // The timing part (0x431340): the underlay, else the foreground, else the background; the background animates beside a foreground.
    // The pace is the rate of the part the play built last (0x4315B0): the underlay, else the background, else the foreground.
    private (EyeCandyClip? Timing, EyeCandyClip? Background, EyeCandyClip? Pace) EyeCandyClips(uint art, EyeCandyParts parts)
    {
        var timing = parts.Underlay ? EyeCandyClipOf(art, EyeCandyVariant.Underlay)
            : parts.Foreground ? EyeCandyClipOf(art, EyeCandyVariant.Foreground)
            : parts.Background ? EyeCandyClipOf(art, EyeCandyVariant.Background) : null;
        var back = parts.Background ? EyeCandyClipOf(art, EyeCandyVariant.Background) : null;
        var background = parts.AnimatesBackground && parts.AnimatesForeground ? back : null;
        return (timing, background, parts.Underlay ? timing : back ?? timing);
    }

    /// <summary>The casting goal's eye candy (goal 27, 0x429F00): the cast entry on the hero, which times the release and the end. A
    /// spell without one (no art, no part, an ART that does not read, no table) releases at once.</summary>
    private CastEyeCandyAnimation StartCastEyeCandy(int run, int spell, long now)
    {
        var entry = EyeCandyTable?.Entry(spell, SpellEyeCandyTable.Cast) ?? SpellEyeCandyEntry.Empty;
        var parts = PartsOf(entry.Flags);
        EyeCandyClip? timing = null, background = null, pace = null;
        if (entry.Art is { } art && parts.Any && EyeCandySlotsFree(SessionSpellCaster.Hero, parts)) (timing, background, pace) = EyeCandyClips(art, parts);
        var animation = new CastEyeCandyAnimation(timing, background, parts.TimesRelease, now, pace);
        if (!animation.Immediate && entry.Art is { } shown)
            _eyeCandies.Add(new() { Sequence = ++_eyeCandySequence, Object = SessionSpellCaster.Hero, Index = SpellEyeCandyRules.EntryIndex(spell, SpellEyeCandyRules.Cast),
                Run = run, Art = shown, Parts = parts, Cast = animation });
        GD.Print($"ARC-MAGIC-017 cast-eye-candy run={run} spell={spell} entry={SpellEyeCandyRules.EntryIndex(spell, SpellEyeCandyRules.Cast)}"
            + $" art={(entry.Art is { } a ? $"0x{a:X8}" : "none")} flags={entry.Flags} timing={timing?.ToString() ?? "none"} immediate={animation.Immediate}");
        return animation;
    }

    /// <summary>An eye candy played by a goal of its own (ag_eye_candy): the release's secondary cast entry or an EyeCandy component's.
    /// Only an animating entry with a foreground or underlay part plays (0x4CD49D).</summary>
    private void PlayEyeCandy(string obj, int index, int run, long now)
    {
        if (EyeCandyTable?.At(index) is not { Art: { } art } entry) return;
        var parts = PartsOf(entry.Flags);
        if (!EyeCandyAnimation.Plays((entry.Flags & SpellEyeCandyFlags.Animates) != 0, parts) || !EyeCandySlotsFree(obj, parts)) return;
        var (timing, background, pace) = EyeCandyClips(art, parts);
        if (timing is not { Frames: > 0 } clip || pace?.PaceMs is null) return;
        var forever = (entry.Flags & SpellEyeCandyFlags.Forever) != 0;
        _eyeCandies.Add(new() { Sequence = ++_eyeCandySequence, Object = obj, Index = index, Run = run, Art = art, Parts = parts,
            Play = new EyeCandyAnimation(clip, background, forever, now, pace) });
        GD.Print($"ARC-MAGIC-017 eye-candy-play object={obj} entry={index} run={run} art=0x{art:X8} flags={entry.Flags} clip={clip}");
    }

    /// <summary>An EyeCandy component's Remove (0x4CD940): the entry's eye candy off the object, this run's only unless no_id.</summary>
    private void RemoveEyeCandy(string obj, int index, int run, bool anyRun)
    {
        var removed = _eyeCandies.RemoveAll(e => e.Object == obj && e.Index == index && e.Cast is null && (anyRun || e.Run == run));
        if (removed > 0) GD.Print($"ARC-MAGIC-017 eye-candy-remove object={obj} entry={index} run={run} removed={removed}");
    }

    /// <summary>The cast host's eye-candy entries (EyeCandy components) as eye candy on their objects.</summary>
    private void TakeEyeCandyEvent(SpellCastEvent entry)
    {
        if (entry.Object is not { } obj) return;
        if (entry.Kind == SpellCastEventKind.EyeCandyAdded) PlayEyeCandy(obj, entry.Value, entry.Run, WorldMilliseconds());
        else if (entry.Kind == SpellCastEventKind.EyeCandyRemoved) RemoveEyeCandy(obj, entry.Value, entry.Run, (entry.Detail & 0x100) != 0);
    }

    private void ClearEyeCandy(string why)
    {
        if (_eyeCandies.Count == 0 && _eyeCandyBodies.Count == 0) return;
        GD.Print($"ARC-MAGIC-017 eye-candy-clear count={_eyeCandies.Count} why={why}");
        _eyeCandies.Clear(); _eyeCandyBodies = [];
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>Per frame: the played eye candies step on the world clock (the casting goal's own one steps with the goal), the ended
    /// ones go (a forever entry's loops until removed), and the parts are drawn.</summary>
    private void ProcessAreaEyeCandy()
    {
        if (_eyeCandies.Count == 0) { if (_eyeCandyBodies.Count > 0) { _eyeCandyBodies = []; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); } return; }
        if (!SpellMapActive) { ClearEyeCandy("the map changed"); return; }
        // A quick load replaced the session: its eye candy goes with it.
        if (_spellCaster is null || !ReferenceEquals(_spellCaster.Owner, _areaSession)) { ClearEyeCandy("the session changed"); return; }
        var now = WorldMilliseconds();
        foreach (var shown in _eyeCandies) shown.Play?.Advance(now);
        _eyeCandies.RemoveAll(e => !e.Shown);
        SyncEyeCandyBodies();
    }

    /// <summary>The draw commands of the shown parts: at the object's tile (the hero's committed cell, an NPC's live cell), the
    /// background and the underlay ordered right before the object and the foreground right after it (0x43B390's lists: the
    /// object's own place in the upright order — an NPC's T0 body, the standing hero's cell — one rank below or above); the
    /// foreground and background at the art id's draw scale (0x5A548C; the object's own scale is taken as 100 %), the underlay
    /// unscaled; translucent parts additive and unlit.</summary>
    private void SyncEyeCandyBodies()
    {
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        if (_readyArea is { } area && _areaSession is { } owner && ExtraArt() is { } extra)
        {
            var frames = (AreaFrameOverlay)AreaFrames();
            foreach (var shown in _eyeCandies)
            {
                if (EyeCandyCell(owner, shown.Object) is not { } cell) continue;
                var order = EyeCandyOrder(area, shown.Object, cell);
                foreach (var (variant, placed, rank) in new[] { (EyeCandyVariant.Underlay, shown.Parts.Underlay, -2), (EyeCandyVariant.Background, shown.Parts.Background, -1),
                    (EyeCandyVariant.Foreground, shown.Parts.Foreground, 1) })
                {
                    if (!placed) continue;
                    // Animated parts follow the goal; a foreground beside an underlay stays at frame 0 (only the underlay animates).
                    var frame = variant == EyeCandyVariant.Underlay || variant == EyeCandyVariant.Foreground && shown.Parts.AnimatesForeground
                        ? shown.Frame : variant == EyeCandyVariant.Background && shown.Parts.AnimatesBackground
                            ? (shown.Parts.AnimatesForeground ? shown.BackgroundFrame : shown.Frame) : 0;
                    var aid = EyeCandyArt.WithFrame(EyeCandyArt.WithVariant(shown.Art, variant), Math.Clamp(frame, 0, 127));
                    var load = extra.Load(aid);
                    if (load.Frame is not { } art)
                    {
                        if (_groundReported.Add($"eye-candy:{aid}")) GD.Print($"ARC-MAGIC-017 eye-candy-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
                        continue;
                    }
                    frames.Add(art.Key, art.Frame);
                    _extraVisuals[art.Visual.Key] = art.Visual;
                    var key = new AreaObjectKey("eye-candy", area.Snapshot.RuntimeMap, AreaSourceKind.Mobile, null, 0, 0,
                        $"{shown.Sequence}:{(int)variant}", 0);
                    var command = AreaScenePlan.Place(area.Plan.Chart, key, art.Visual, cell.X, cell.Y, flat: false);
                    var percent = variant == EyeCandyVariant.Underlay ? 100 : EyeCandyArt.DrawScales[EyeCandyArt.Scale(aid)];
                    var translucent = EyeCandyArt.Translucent(aid);
                    var at = command.Destination;
                    var anchorX = at.X + art.Visual.HotX; var anchorY = at.Y + art.Visual.HotY;
                    var destination = percent == 100 ? at : new Arcanum.Data.Terrain.TerrainRect(anchorX - art.Visual.HotX * percent / 100,
                        anchorY - art.Visual.HotY * percent / 100, Math.Max(1, art.Visual.Width * percent / 100), Math.Max(1, art.Visual.Height * percent / 100));
                    bodies[key] = command with { Destination = destination, MapX = order.MapX, MapY = order.MapY, SecondRank = order.Second,
                        FirstRank = order.First + rank, Additive = translucent, Unlit = translucent };
                    _eyeCandyDrawnForSelfTest.Add(shown.Index);
                }
            }
        }
        if (bodies.Count == 0 && _eyeCandyBodies.Count == 0) return;
        _eyeCandyBodies = bodies;
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>The object's place in the upright order: an NPC's T0 body (its own offset ranks) while it stands at its map cell,
    /// else the cell with the ranks of no offset (the hero stands still on its cell while it casts).</summary>
    private (int MapX, int MapY, int First, int Second) EyeCandyOrder(AreaLoadResult area, string obj, CellCoord cell)
    {
        if (obj != SessionSpellCaster.Hero && _spellCastMap?.Row(obj) is { } row && row.WorldX == cell.X && row.WorldY == cell.Y
            && area.Plan.Commands.FirstOrDefault(c => c.Identity == row.Key && c.Phase == 4) is { } body)
            return (body.MapX, body.MapY, body.FirstRank, body.SecondRank);
        var ranks = AreaScenePlan.SameCellRanks(0, 0);
        return (cell.X, cell.Y, ranks.First, ranks.Second);
    }

    /// <summary>Where an eye candy's object stands: the hero at its committed cell, an NPC or companion at its live cell.</summary>
    private CellCoord? EyeCandyCell(GameSessionOwner owner, string obj)
    {
        if (obj == SessionSpellCaster.Hero) return HeroWorldCell();
        return _spellCastMap?.Row(obj) is { } row ? LiveCell(owner, row) : null;
    }
}
