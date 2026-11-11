using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-017: the spells' eye candy in the AREA view (ledger EC rows). The casting goal plays the cast entry (6·spell) on the hero
// and is timed by it (CastEyeCandyAnimation: the release at the timing part's action frame, the end one pace after the step
// that finds its last frame);
// the release plays the secondary cast entry (6·spell + 4) on the hero and the EyeCandy components of the run play or remove their
// entries on their targets, each as an overlay goal run of its own (goals 45-48, 58, 59, 75, 76, ARC-DEV-143, AreaEyeCandyHost). Each part (foreground, background, underlay) is drawn as an
// extra-art body at its object's tile: the background and the underlay right before the object, the foreground right after it
// (the object's own draw order), additive when translucent, at the art id's scale. A play needs a free slot of each list it places
// in (7 foreground, 7 background, 4 underlay per object, 0x4CCF70). Not ready: lights, sounds, tinting, turning a multi-rotation eye
// candy to its object's facing, the object's scale and offsets, the eye-candy callbacks into the spell run (the host counts them), the AGoal hover's raised body (the host keeps its offset).
public partial class TerrainViewerApp
{
    /// <summary>One cast eye candy shown on an object (the casting goal's own animation): the entry (table index), its art id (frame 0), its parts and its timing. The eye candy of a goal run of its own is
    /// <see cref="ShownOverlay"/> (ARC-DEV-143).</summary>
    private sealed class ShownEyeCandy
    {
        public required int Sequence { get; init; }
        public required string Object { get; init; }
        public required int Index { get; init; }
        public required int Run { get; init; }
        public required uint Art { get; init; }
        public required EyeCandyParts Parts { get; init; }
        public required CastEyeCandyAnimation Cast { get; init; }
        public bool Shown => !Cast.Ended;
    }

    /// <summary>One part set the draw places: where it came from (a cast's sequence or a run's handle), its owner, entry, art, parts and frames.</summary>
    private readonly record struct EyeCandyDraw(string Label, string Object, int Index, uint Art, EyeCandyParts Parts, int Frame, int BackgroundFrame);

    private readonly List<ShownEyeCandy> _eyeCandies = [];
    private Dictionary<AreaObjectKey, AreaDrawCommand> _eyeCandyBodies = [];
    private int _eyeCandySequence;
    // Smoke seams: the entries drawn, and the cast animations' release and end times after their start.
    private readonly HashSet<int> _eyeCandyDrawnForSelfTest = [];
    private readonly List<(int Index, long ReleaseMs, long EndMs, bool Immediate)> _castTimingsForSelfTest = [];

    private SpellEyeCandyTable? EyeCandyTable => _spellData?.EyeCandy;
    private const int ForegroundSlots = 7, BackgroundSlots = 7, UnderlaySlots = 4;

    /// <summary>Whether the object has a free slot in every list the parts go to (0x4CCF70: a full list places nothing): the cast eye candies and the overlay runs it shows count.</summary>
    private bool EyeCandySlotsFree(string obj, EyeCandyParts parts)
    {
        int Count(Func<EyeCandyParts, bool> part) => _eyeCandies.Count(e => e.Object == obj && e.Shown && part(e.Parts)) + (ShownEyeCandyHost?.Shown().Count(e => e.Owner == obj && part(e.Parts)) ?? 0);
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
    private CastEyeCandyAnimation StartCastEyeCandy(int run, int spell, long now, out CastEyeCandyAnimation probe)
    {
        var entry = EyeCandyTable?.Entry(spell, SpellEyeCandyTable.Cast) ?? SpellEyeCandyEntry.Empty;
        var parts = PartsOf(entry.Flags);
        EyeCandyClip? timing = null, background = null, pace = null;
        if (entry.Art is { } art && parts.Any && EyeCandySlotsFree(SessionSpellCaster.Hero, parts)) (timing, background, pace) = EyeCandyClips(art, parts);
        var animation = new CastEyeCandyAnimation(timing, background, parts.TimesRelease, now, pace);
        // ARC-DEV-183: a second animation of the same clips from 0 ms, for the dry run that gives the table its waits.
        probe = new CastEyeCandyAnimation(timing, background, parts.TimesRelease, 0, pace);
        if (!animation.Immediate && entry.Art is { } shown)
            _eyeCandies.Add(new() { Sequence = ++_eyeCandySequence, Object = SessionSpellCaster.Hero, Index = SpellEyeCandyRules.EntryIndex(spell, SpellEyeCandyRules.Cast),
                Run = run, Art = shown, Parts = parts, Cast = animation });
        GD.Print($"ARC-MAGIC-017 cast-eye-candy run={run} spell={spell} entry={SpellEyeCandyRules.EntryIndex(spell, SpellEyeCandyRules.Cast)}"
            + $" art={(entry.Art is { } a ? $"0x{a:X8}" : "none")} flags={entry.Flags} timing={timing?.ToString() ?? "none"} immediate={animation.Immediate}");
        return animation;
    }

    /// <summary>ARC-WORLD-102: an NPC's casting goal's eye candy (the cast entry on the NPC, its own slots), started at 0 ms for the AI
    /// host's timing of the goal; null when none plays (no table, art, part or free slot). It is not drawn on the NPC yet.</summary>
    private CastEyeCandyAnimation? NpcCastEyeCandy(string npc, int spell)
    {
        var entry = EyeCandyTable?.Entry(spell, SpellEyeCandyTable.Cast) ?? SpellEyeCandyEntry.Empty;
        var parts = PartsOf(entry.Flags);
        if (entry.Art is not { } art || !parts.Any || !EyeCandySlotsFree(npc, parts)) return null;
        var (timing, background, pace) = EyeCandyClips(art, parts);
        return new CastEyeCandyAnimation(timing, background, parts.TimesRelease, 0, pace);
    }

    // ARC-DEV-143: the log entries already taken (by reference); a replaced cast host's entries can be flushed twice and an add must not start two runs from one entry.
    private readonly HashSet<SpellCastEvent> _eyeCandyTaken = new(ReferenceEqualityComparer.Instance);
    private AreaEyeCandyHost? _eyeCandyHost;
    private readonly Dictionary<(uint Art, EyeCandyParts Parts), EyeCandyFacts?> _eyeCandyFacts = [];

    /// <summary>The overlay host as far as it belongs to the shown session; null when there is none or when it still belongs to a replaced session (a quick load, a carried session: its session is disposed, so reading its goals throws). Read-only users go through this; <see cref="EyeCandyHostNow"/> replaces the stale host.</summary>
    private AreaEyeCandyHost? ShownEyeCandyHost => _eyeCandyHost is { } host && ReferenceEquals(host.Session, _areaSession) ? host : null;

    /// <summary>The overlay host of the shown session: a new session (a reload, a carried session) gives up the old host's runs first.</summary>
    private AreaEyeCandyHost? EyeCandyHostNow()
    {
        if (_areaSession is not { } session) return null;
        if (_eyeCandyHost is { } host && ReferenceEquals(host.Session, session)) return host;
        _eyeCandyHost?.LoseAll();
        _eyeCandyFacts.Clear();
        return _eyeCandyHost = new(session, index => EyeCandyTable?.At(index), EyeCandyFactsOf, null, owner => _eyeCandies.Where(e => e.Object == owner && e.Shown).Select(e => e.Parts), FireBurnerFor(session));
    }

    /// <summary>The ART facts of an entry's parts for the goals' nodes (the header reads, cached): the timing part's frames and action frame, the background that animates beside a foreground, the pace; null when the art does not read or its rate is 0.</summary>
    private EyeCandyFacts? EyeCandyFactsOf(uint art, EyeCandyParts parts)
    {
        if (_eyeCandyFacts.TryGetValue((art, parts), out var known)) return known;
        var (timing, background, pace) = EyeCandyClips(art, parts);
        EyeCandyFacts? facts = timing is { Frames: > 0 } clip && pace?.PaceMs is { } ms
            ? new(clip.Frames, clip.ActionFrame, ms, background?.Frames ?? 0, parts.TimesRelease, parts.Foreground && !parts.Underlay) : null;
        return _eyeCandyFacts[(art, parts)] = facts;
    }

    /// <summary>An eye candy played by a goal run of its own (ag_eye_candy and its kin): the release's secondary cast entry or an EyeCandy component's, with the component's play flags. Only an animating entry with a foreground or underlay part plays
    /// (0x4CD49D); the run is the add 0x4CCF70 (<see cref="AreaEyeCandyHost.Play"/>).</summary>
    private void PlayEyeCandy(string obj, int index, int run, uint play = 0)
    {
        if (EyeCandyHostNow() is not { } host) return;
        var started = host.Play(obj, index, play, run);
        if (started) GD.Print($"ARC-MAGIC-017 eye-candy-play object={obj} entry={index} run={run} play=0x{play:X} goal={AiExecutorRules.EyeCandyGoalOf(play)}");
        // ARC-DEV-352 (EC11): a callback-flagged add the run waits for that could not play after all calls back at once.
        else EyeCandyNotPlayed(index, run, play);
    }

    /// <summary>An EyeCandy component's Remove (0x4CD940): the entry's eye candy off the object, this run's only unless no_id.</summary>
    private void RemoveEyeCandy(string obj, int index, int run, bool anyRun)
    {
        var removed = ShownEyeCandyHost?.Remove(obj, index, run, anyRun) ?? 0;
        if (removed > 0) GD.Print($"ARC-MAGIC-017 eye-candy-remove object={obj} entry={index} run={run} removed={removed}");
    }

    /// <summary>The cast host's eye-candy entries (EyeCandy components) as eye candy on their objects, and its AGoal components for the floating goal 44 as hovers.</summary>
    private void TakeEyeCandyEvent(SpellCastEvent entry)
    {
        if (entry.Object is not { } obj || entry.Kind is not (SpellCastEventKind.EyeCandyAdded or SpellCastEventKind.EyeCandyRemoved or SpellCastEventKind.Hover or SpellCastEventKind.HoverEnded)) return;
        if (_eyeCandyTaken.Count >= 256) _eyeCandyTaken.Clear();
        if (!_eyeCandyTaken.Add(entry)) return;
        switch (entry.Kind)
        {
            case SpellCastEventKind.EyeCandyAdded: PlayEyeCandy(obj, entry.Value, entry.Run, unchecked((uint)entry.Detail)); break;
            case SpellCastEventKind.EyeCandyRemoved: RemoveEyeCandy(obj, entry.Value, entry.Run, (entry.Detail & 0x100) != 0); break;
            case SpellCastEventKind.Hover:
                GD.Print($"ARC-DEV-143 hover-start object={obj} run={entry.Run} started={EyeCandyHostNow()?.StartHover(obj)}");
                break;
            default:
                GD.Print($"ARC-DEV-143 hover-end object={obj} run={entry.Run} ended={EyeCandyHostNow()?.EndHover(obj)}");
                break;
        }
    }

    /// <summary>The cast eye candies and the overlay runs go (a map change, a replaced session); a hovering object keeps its offset.</summary>
    private void ClearEyeCandy(string why)
    {
        var overlays = ShownEyeCandyHost?.Shown().Count() ?? 0;
        _eyeCandyHost?.LoseAll();
        if (_eyeCandies.Count == 0 && _eyeCandyBodies.Count == 0 && overlays == 0) return;
        GD.Print($"ARC-MAGIC-017 eye-candy-clear count={_eyeCandies.Count + overlays} why={why}");
        _eyeCandies.Clear(); _eyeCandyBodies = [];
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>Per frame: the cast eye candies' own timing steps with their goal; the overlay runs step on the animation clock inside the frame step. Here the ended cast ones go and the parts of both are drawn.</summary>
    private void ProcessAreaEyeCandy()
    {
        var overlays = ShownEyeCandyHost?.Shown().Any() ?? false;
        if (_eyeCandies.Count == 0 && !overlays) { if (_eyeCandyBodies.Count > 0) { _eyeCandyBodies = []; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); } return; }
        if (!SpellMapActive) { ClearEyeCandy("the map changed"); return; }
        // A quick load replaced the session: its eye candy goes with it.
        if (_spellCaster is null || !ReferenceEquals(_spellCaster.Owner, _areaSession)) { ClearEyeCandy("the session changed"); return; }
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
            var draws = _eyeCandies.Select(c => new EyeCandyDraw($"c{c.Sequence}", c.Object, c.Index, c.Art, c.Parts, c.Cast.Frame, c.Cast.BackgroundFrame))
                .Concat((ShownEyeCandyHost?.Shown() ?? []).Select(o => new EyeCandyDraw($"r{o.Handle.Slot}.{o.Handle.RunId}", o.Owner, o.Eye.Entry, o.Eye.Art, o.Parts, o.Eye.Frame, o.Eye.BackgroundFrame)));
            foreach (var shown in draws)
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
                        $"{shown.Label}:{(int)variant}", 0);
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
        // ARC-DEV-352: a summoned object (the flash) at its cell.
        if (CreatedCell(owner, obj) is { } created) return created;
        return _spellCastMap?.Row(obj) is { } row ? LiveCell(owner, row) : null;
    }
}
