using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Godot;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-COMBAT-024 native observation (only when named, last: the hero stays dead; from a window map's opening window, with --hero), the owner's report
    // replayed: the hero takes an item behind the window's centre, the window moves on twice in the other direction, the second
    // time published while the hero is dead (a window load that began before the death), and the second window no longer reaches the
    // item's sector. Its seed then does not list the taken item; the session still takes it (a window seed), so the map keeps its
    // inventory and combat: an NPC killed before the hero is still drawn fallen, and the dead hero keeps the body (its armour) it wore.
    private async Task RunAreaDeadHeroWindowSmokeAsync()
    {
        if (_mapWindowCentre is not { } centre || _areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession)
        { GD.Print($"ARC-COMBAT-024 native-smoke dead-window NONE map={_readyMapName} (needs a window map and a hero)"); return; }
        // Its items and NPCs are those around the map's opening window (the Crash Site's on the main map); the stream stage moves it.
        if (_mapWindowMoves > 0) { GD.Print($"ARC-COMBAT-024 native-smoke dead-window NONE map={_readyMapName} (the window moved {_mapWindowMoves} times: run the stage on its own)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try { await DeadHeroWindowAsync(centre, session); }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; }
    }

    private async Task DeadHeroWindowAsync(AreaSectorCoordinate centre, GameSessionOwner session)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-024 dead-window smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DEAD_WINDOW_SMOKE_FAILED");
        }
        Require(_combatHost is not null && _inventoryHost is not null && _readyArea is not null, "combat and inventory are bound: " + _inventoryStatus);
        var runtimeMap = _readyArea!.Snapshot.RuntimeMap;
        AreaInventoryMap Shown() => _inventoryHost!.Maps.First(m => m.RuntimeMap == runtimeMap);
        AreaSectorCoordinate? SectorOf(string entity) => Shown().Row(entity) is { WorldX: { } x, WorldY: { } y } ? new(x >> 6, y >> 6) : null;
        AreaMapRuntime Runtime() { var grid = (AreaGridSession)_worldActor.Session!; return new(_readyArea!.Snapshot, grid.Snapshot, grid.Chart); }
        // A window loads around the sector the hero stands in.
        static bool In(AreaStepCell cell, AreaSectorCoordinate sector) => new AreaSectorCoordinate(cell.X >> 6, cell.Y >> 6) == sector;

        // The hero stands on a cell near another (in the shown window), the frames settle.
        async Task<AreaStepCell> StandNear(AreaStepCell near)
        {
            var runtime = Runtime();
            AreaSmokePlace(runtime, AreaSmokeStandable(runtime, runtime.Chart.ToChart(near), 3));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var grid = (AreaGridSession)_worldActor.Session!;
            return grid.Chart.ToWorld(grid.State.CommittedCell);
        }
        // The window around a sector, published with the session carried.
        async Task Publish(AreaStepCell actor, AreaSectorCoordinate sector)
        {
            var moves = _mapWindowMoves;
            Require(_mapHandoff is null && !StreamHeld, $"no load or hold keeps the window: {_mapHandoff?.Kind} held={StreamHeld}");
            BeginMapStream((AreaGridSession)_worldActor.Session!, actor, sector);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (MapStreamInFlight)
            {
                Require(clock.Elapsed.TotalSeconds < 120, "the window published within 120 s: " + _mapStreamStatus);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(_mapWindowMoves == moves + 1 && ReferenceEquals(_areaSession, session) && _mapWindowCentre == sector,
                $"the window around {sector.X},{sector.Y} published with the session carried: {_mapStreamStatus}");
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // An item one sector behind the centre (the first direction with one the hero can take: on the ground, or in a container's or
        // a dead NPC's list), taken by the hero.
        var shown = session.QueryMapInventory(new(runtimeMap))!;
        var items = shown.Ground.Select(g => (Holder: (string?)null, Item: g.Item.Entity, At: SectorOf(g.Item.Entity)))
            .Concat(shown.Holders.Where(h => h.Kind == SessionHolderKind.Container || h.Dead)
                .SelectMany(h => h.Items.Select(i => (Holder: (string?)h.Entity, Item: i.Entity, At: SectorOf(h.Entity))))).ToList();
        (long X, long Y) step = default; string? taken = null; AreaSectorCoordinate behind = centre;
        foreach (var (dx, dy) in new (long, long)[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
        {
            if (centre.X + 2 * dx < 0 || centre.Y + 2 * dy < 0 || centre.X - dx < 0 || centre.Y - dy < 0) continue;
            var at = new AreaSectorCoordinate(centre.X - dx, centre.Y - dy);
            foreach (var (holder, item, _) in items.Where(i => i.At == at))
            {
                var took = holder is null ? session.PickUpItem(session.Version, item, _ => SessionInsertVerdict.Accepted)
                    : session.LootItem(session.Version, holder, item, _ => SessionInsertVerdict.Accepted);
                if (took.Disposition != GameSessionDisposition.Changed) continue;
                (taken, step, behind) = (item, (dx, dy), at);
                break;
            }
            if (taken is not null) break;
        }
        Require(taken is not null, "an item the hero can take one sector from the centre " + _mapWindowCentre);

        // First move: one sector ahead (the item's sector is still in the window's ring).
        var first = new AreaSectorCoordinate(centre.X + step.X, centre.Y + step.Y);
        var actor = await StandNear(new(first.OriginX + 32, first.OriginY + 32));
        Require(In(actor, first), $"the hero stands in the first window's centre sector: {actor}");
        await Publish(actor, first);
        Require(_inventoryHost is not null && Shown().Seed.Items.Any(item => item.Entity == taken), "the first window still lists the taken item: " + _inventoryStatus);

        // A living NPC with combat values, drawn, not a companion, in the second window's centre sector; killed and drawn fallen.
        var second = new AreaSectorCoordinate(centre.X + 2 * step.X, centre.Y + 2 * step.Y);
        AreaFrames();
        string? victim = null; AreaObjectKey victimKey = default; SessionLifeTarget? victimTarget = null;
        foreach (var holder in Shown().Seed.Holders)
        {
            if (holder.Kind != SessionHolderKind.Npc || holder.Dead || SectorOf(holder.Entity) != second) continue;
            var row = Shown().Row(holder.Entity);
            if (IsAreaCompanion(row.Key) || !_areaOverlay!.Value.Bodies.ContainsKey(row.Key)) continue;
            if (_combatHost!.Build(session, runtimeMap, holder.Entity).Combat is not { } combat || session.QueryLife(combat.Target).View is not { Status.IsDead: false }) continue;
            (victim, victimKey, victimTarget) = (holder.Entity, row.Key, combat.Target);
            break;
        }
        Require(victim is not null, "a living NPC with combat values drawn in the second window's centre sector");
        var killed = session.Kill(session.Version, victimTarget!);
        Require(killed.Outcome is { } outcome && outcome.Events.HasFlag(LifeEvents.Died), $"the NPC died: {killed.Disposition} {killed.Failure}");
        _deathAnimations[(runtimeMap, victim!)] = AreaFallenPoses.Animation(killed.Outcome!.Death);
        var near = Shown().Row(victim!);
        actor = await StandNear(new(near.WorldX!.Value, near.WorldY!.Value));
        Require(In(actor, second), $"the hero stands in the second window's centre sector: {actor}");
        PublishWorldSelection();
        Require(_fallenBodies.ContainsKey(victimKey), $"the killed NPC {victim} is drawn fallen");

        // The hero's body (out of combat mode: its armour, no weapon), then its death.
        Require(!HeroInCombatMode, "the hero is out of combat mode");
        SyncHeroBody();
        var body = WantedHeroBody();
        var idle = _worldActor.Profile.Idle.Selector;
        Require(session.Kill(session.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(session.QueryHeroLife()?.Status.IsDead == true, "the hero is dead");

        // Second move, published while the hero is dead: the item's sector is out of the window, so its seed does not list the item.
        await Publish(actor, second);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var afterBody = WantedHeroBody();
        var afterIdle = _worldActor.Profile.Idle.Selector;
        GD.Print($"ARC-COMBAT-024 dead-window after inventory={_inventoryHost is not null} combat={_combatHost is not null} fallen={_fallenBodies.Count}"
            + $" body={body.Label}/{body.Armor}->{afterBody.Label}/{afterBody.Armor} idle=0x{idle:x8}->0x{afterIdle:x8} status={_inventoryStatus}");
        Require(_inventoryHost is not null && _combatHost is not null, "the second window's inventory and combat are bound: " + _inventoryStatus);
        Require(Shown().Seed.Window && Shown().Seed.Items.All(item => item.Entity != taken), "the second window's seed does not list the taken item");
        Require(session.QueryPcInventory().Any(item => item.Entity == taken), "the hero still holds the taken item");
        Require(_fallenBodies.ContainsKey(victimKey), $"the killed NPC {victim} is still drawn fallen");
        Require(afterBody == body && afterIdle == idle, $"the dead hero keeps its body: {body} 0x{idle:x8} -> {afterBody} 0x{afterIdle:x8}");
        GD.Print("ARC-COMBAT-024 native-smoke dead-window PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, Windows = $"{centre.X},{centre.Y} -> {first.X},{first.Y} -> {second.X},{second.Y}", TakenFrom = $"{behind.X},{behind.Y}",
            Taken = taken, Npc = victim, Body = body.Label, body.Armor, Idle = $"0x{idle:x8}",
            Parity = "NOT_VERIFIED (the session's seed bookkeeping and the viewer's drawing; no original-runtime run)" }));
    }
}
