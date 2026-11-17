using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-041: the pool prototype's current art (4048, read when the content stack opens; null when it does not read), the
    // recorded deaths (critter, death time) already seen on the shown map and session, and the world time each pool this view left
    // started its frames at (a pool the view did not see left shows its last frame).
    private uint? _bloodPoolArt;
    private object? _bloodPoolDeathStamp, _bloodPoolDeathBaseline;
    private HashSet<(string Critter, long DeathMs)> _bloodPoolDeaths = [];
    private readonly Dictionary<string, long> _bloodPoolStarts = new(StringComparer.Ordinal);
    // ARC-DEV-163: the pools this view saw left (a start is dropped from _bloodPoolStarts when the pool has played to its last frame); the smoke asks it.
    private readonly HashSet<string> _bloodPoolSeen = new(StringComparer.Ordinal);
    private (int Frames, int FramesPerSecond)? _bloodPoolTiming;
    private HashSet<AreaObjectKey> _bloodPoolKeys = [];
    private const int OffsetXSlot = 2, OffsetYSlot = 3;

    /// <summary>
    /// ARC-RENDER-041: a death the view sees recorded on the shown map leaves a pool of blood (0x42FED0 → 0x432D90, ledger BP1–BP7):
    /// an NPC of the map's seed lies at the cell its body is drawn on (a companion's or an AI walker's cell, with no pixel offset; else its
    /// map cell and offsets), the hero at its committed cell. The deaths recorded when the map and session were first seen leave none
    /// (they were before this view, or a save that predates pools). The NPC's flags come from its map data and the hero's are none; the session lays the
    /// flags it wrote on the critter (stoned, undead, elemental) over them when it leaves the pool (ARC-COMBAT-1928). As the original (0x42FED0 runs when the dying goal has played its
    /// frames), a death leaves its pool when its dying animation has played (ARC-RENDER-040, <see cref="DyingPending"/>; the hero's is
    /// its fall). A refusal or failure is said once.
    /// </summary>
    private void LeaveBloodPoolsForNewDeaths()
    {
        if (_readyArea is not { } area || _areaSession is not { } session || _inventoryHost is null || !AttackMapActive) return;
        // ARC-RENDER-040: a death the fallen bodies have not seen yet starts its dying animation first.
        var fallenBefore = _fallenBodies;
        SyncAreaCombatBodies();
        if (!ReferenceEquals(fallenBefore, _fallenBodies)) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
        var now = WorldMilliseconds();
        var stamp = (area, session, session.Version.Revision, CurrentAreaMap, DyingPendingStamp(CurrentAreaMap, now));
        if (Equals(stamp, _bloodPoolDeathStamp)) return;
        _bloodPoolDeathStamp = stamp;
        var deaths = new List<(string Critter, long DeathMs, Func<(CellCoord Cell, int OffsetX, int OffsetY, BloodPoolFacts Facts)?> Where)>();
        if (session.QueryLife(SessionLifeTarget.Hero).View is { Record.DeathTimeMs: { } heroDeath })
            deaths.Add((SessionEventKinds.PlayerEntity, heroDeath, HeroPoolPlace));
        if (_inventoryHost.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is { } map)
            foreach (var holder in map.Seed.Holders)
                if (holder.Kind == SessionHolderKind.Npc && session.QueryLifeRecord(holder.Entity) is { DeathTimeMs: { } death })
                    deaths.Add((holder.Entity, death, () => NpcPoolPlace(map, holder.Entity)));
        var baseline = (area, session, CurrentAreaMap);
        if (!Equals(baseline, _bloodPoolDeathBaseline))
        {
            _bloodPoolDeathBaseline = baseline;
            _bloodPoolDeaths = deaths.Select(d => (d.Critter, d.DeathMs)).ToHashSet();
            _bloodPoolStarts.Clear(); _bloodPoolSeen.Clear();
            return;
        }
        foreach (var (critter, deathMs, where) in deaths)
        {
            if (_bloodPoolDeaths.Contains((critter, deathMs)) || DyingPending(CurrentAreaMap, critter, now)) continue;
            _bloodPoolDeaths.Add((critter, deathMs));
            if (where() is not var (cell, offsetX, offsetY, facts))
            {
                GD.Print($"ARC-RENDER-041 blood-pool critter={critter} not left: no cell");
                continue;
            }
            var left = session.LeaveBloodPool(session.Version, critter, new SessionMapId(CurrentAreaMap), cell, offsetX, offsetY, facts);
            if (left.Disposition == GameSessionDisposition.Changed && left.Pool is { } pool) { _bloodPoolStarts[pool.Entity] = WorldMilliseconds(); _bloodPoolSeen.Add(pool.Entity); }
            GD.Print($"ARC-RENDER-041 blood-pool critter={critter} disposition={left.Disposition} failure={left.Failure} refusal={left.Refusal}"
                + (left.Pool is { } p ? $" entity={p.Entity} cell={p.Cell.X},{p.Cell.Y} offset={p.OffsetX},{p.OffsetY}" : ""));
        }
    }

    // Where the hero lies: its committed cell, no pixel offset, no critter flags (null without the hero's grid).
    private (CellCoord Cell, int OffsetX, int OffsetY, BloodPoolFacts Facts)? HeroPoolPlace()
    {
        if (_worldActor.Session is not AreaGridSession hero) return null;
        var world = hero.Chart.ToWorld(hero.State.CommittedCell);
        var cell = new CellCoord(checked((int)world.X), checked((int)world.Y));
        return (cell, 0, 0, new BloodPoolFacts(0, 0, 0, TileBlocks(cell), _gameSettings.ViolenceFilter));
    }

    // Where an NPC of the shown map lies and what its map data says (null when its row or fields do not read).
    private (CellCoord Cell, int OffsetX, int OffsetY, BloodPoolFacts Facts)? NpcPoolPlace(AreaInventoryMap map, string entity)
    {
        if (!map.TryGetRow(entity, out var row) || row.WorldX is not { } x || row.WorldY is not { } y || _combatHost?.Fields(map.RuntimeMap, entity) is not { } fields) return null;
        var moved = AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == row.Key) ?? AiMoverCommands().FirstOrDefault(c => c.Identity == row.Key);
        var cell = moved is not null ? new CellCoord(moved.MapX, moved.MapY) : new CellCoord(checked((int)x), checked((int)y));
        var offsetX = moved is null ? (int)(Number(row, OffsetXSlot) ?? 0) : 0;
        var offsetY = moved is null ? (int)(Number(row, OffsetYSlot) ?? 0) : 0;
        return (cell, offsetX, offsetY, new BloodPoolFacts(fields.Critter?.Flags ?? 0, fields.Critter?.Flags2 ?? 0, fields.Object.SpellFlags, TileBlocks(cell), _gameSettings.ViolenceFilter));
    }

    // 0x4D7110 on the step evidence: a tile it settles as not open blocks; one it cannot speak for does not (the critter stood there).
    private bool TileBlocks(CellCoord cell) => _worldActor.Session is AreaGridSession grid &&
        grid.Snapshot.Terrain(new AreaStepCell(cell.X, cell.Y)) is { Coverage: CardinalTraversalCoverage.Supported, Disposition: not CardinalTraversalDisposition.Open };

    /// <summary>The frame of each pool still playing its frames, for the ground bodies' stamp (null when none plays); a pool that has
    /// reached its last frame stops playing.</summary>
    private string? BloodPoolFramesStamp()
    {
        if (_bloodPoolStarts.Count == 0) return null;
        if (_bloodPoolTiming is not { } timing) return $"unread:{_bloodPoolStarts.Count}";
        var now = WorldMilliseconds();
        int FrameOf(long start) => BloodPoolRules.Frame(now - start, timing.Frames, timing.FramesPerSecond);
        foreach (var (entity, start) in _bloodPoolStarts.ToArray())
            if (FrameOf(start) >= timing.Frames - 1) _bloodPoolStarts.Remove(entity);
        return _bloodPoolStarts.Count == 0 ? null
            : string.Join('|', _bloodPoolStarts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{FrameOf(p.Value)}"));
    }

    /// <summary>ARC-RENDER-041: each pool of blood lying on the shown map, drawn flat at its cell with its critter's pixel offsets from the
    /// pool prototype's art: the frame <see cref="BloodPoolRules.Frame"/> gives since the view left it, else its last frame. It is not
    /// hovered or picked (the prototype is clicked through).</summary>
    private void AddBloodPoolBodies(AreaLoadResult area, Dictionary<AreaObjectKey, AreaDrawCommand> bodies, Func<uint, string, AreaExtraFrame?> frameOf)
    {
        _bloodPoolKeys = [];
        if (_areaSession is not { } session) return;
        var pools = session.QueryBloodPools(new SessionMapId(CurrentAreaMap));
        if (pools.Count == 0) return;
        if (ExtraArt() is null || _bloodPoolArt is not { } art || frameOf(art, "blood-pool") is not { } first)
        {
            if (_groundReported.Add("nopool:")) GD.Print("ARC-RENDER-041 blood-pool not drawn: no pool art");
            return;
        }
        var frames = Math.Clamp(first.FramesPerGroup, 1, 32);
        _bloodPoolTiming = (frames, first.FramesPerSecond);
        var now = WorldMilliseconds();
        foreach (var pool in pools)
        {
            var index = _bloodPoolStarts.TryGetValue(pool.Entity, out var start)
                ? BloodPoolRules.Frame(now - start, frames, first.FramesPerSecond) : frames - 1;
            if (frameOf(WithSceneryFrame(art, index), "blood-pool") is not { } frame) continue;
            var key = PlacedKey(area.Snapshot.RuntimeMap, pool.Entity);
            _bloodPoolKeys.Add(key);
            var command = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, pool.Cell.X, pool.Cell.Y, true);
            var at = command.Destination;
            bodies[key] = command with { Destination = new(checked(at.X + pool.OffsetX), checked(at.Y + pool.OffsetY), at.Width, at.Height) };
        }
    }

    // A scenery art id at another frame (bits 14..18, AreaObjectArt).
    private static uint WithSceneryFrame(uint aid, int frame) => (aid & ~(31u << 14)) | ((uint)Math.Clamp(frame, 0, 31) << 14);

    /// <summary>ARC-RENDER-041 smoke: the NPC the stage just killed leaves its pool of blood (unless the rules refuse it, which the
    /// log names) at its cell, drawn flat and not picked; the pool plays its frames, then shows its last frame; the save holds it.</summary>
    private async Task RequireBloodPoolAsync(GameSessionOwner session, int runtimeMap, string npc)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RENDER-041 blood-pool smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_BLOOD_POOL_SMOKE_FAILED");
        }
        SyncAreaInventory();
        var pool = session.QueryBloodPools(new SessionMapId(runtimeMap)).FirstOrDefault(p => p.Critter == npc);
        var facts = NpcPoolPlace(_inventoryHost!.Maps.First(m => m.RuntimeMap == runtimeMap), npc);
        Require(facts is not null, "the dead NPC's place and flags read");
        var refusal = BloodPoolRules.Leaves(facts!.Value.Facts);
        GD.Print($"ARC-RENDER-041 smoke blood-pool npc={npc} refusal={refusal} pool={pool?.Entity} cell={pool?.Cell.X},{pool?.Cell.Y}");
        if (refusal != BloodPoolRefusal.None) { Require(pool is null, "a refused death leaves no pool"); return; }
        Require(pool is not null && pool.Cell == facts.Value.Cell && pool.DeathTimeMs == session.QueryLifeRecord(npc)!.Value.DeathTimeMs, "the death left its pool at the body's cell");
        var key = PlacedKey(runtimeMap, pool!.Entity);
        Require(_groundBodies.TryGetValue(key, out var drawn) && drawn.Phase == 3 && !_groundOnly.ContainsKey(key), "the pool is drawn flat and not picked");
        // ARC-DEV-163: the view saw the pool left and started its frames. A target that lay knocked out when it was killed has no dying animation to wait for, so its pool is left at the next sync and the stage's loops after the kill
        // may have played it to its last frame already: the start is asked of the record of what the view saw, not of the pools that still play.
        Require(_bloodPoolTiming is { Frames: > 1 } && _bloodPoolSeen.Contains(pool.Entity), "the pool plays its frames: " + _bloodPoolTiming);
        var last = ExtraArt()!.Load(WithSceneryFrame(_bloodPoolArt!.Value, _bloodPoolTiming!.Value.Frames - 1)).Frame!.Key;
        // Wait by the world clock, not by frames (a background window's frames may come far faster than the art's rate): the pool plays on
        // it (ARC-DEV-125). A smoke that drives the clock by a literal test time moves it 50 ms a frame; otherwise it follows the host.
        var deadline = WorldMilliseconds() + 3000;
        var waited = 0;
        while (_bloodPoolStarts.ContainsKey(pool.Entity) && WorldMilliseconds() < deadline)
        {
            waited++;
            if (_worldTestTime is { } driven) _worldTestTime = new SimulationTime(driven.Value + ExactScalar.Create(1, 20));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory();
        }
        Require(!_bloodPoolStarts.ContainsKey(pool.Entity) && _groundBodies[key].Frame == last, $"the pool keeps its last frame after {waited} frames");
        Require(GameSessionSnapshotCodec.Encode(session.Capture()).Contains($"{{\"entity\":\"{pool.Entity}\",\"critter\":\"{npc}\"", StringComparison.Ordinal), "the save holds the pool");
        // A repeated sync leaves no second pool.
        _bloodPoolDeathStamp = null; SyncAreaInventory();
        Require(session.QueryBloodPools(new SessionMapId(runtimeMap)).Count(p => p.Critter == npc) == 1, "one pool per death");
    }
}
