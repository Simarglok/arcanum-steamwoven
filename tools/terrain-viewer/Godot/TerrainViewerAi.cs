using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-092 (NPC AI slice I10, first fight): the shown map's AI host in the AREA view. With a hero, every NPC of the shown map
// that is not a companion gets its AI tick (0x4AD6E0) when the map's host is built; the event pump fires the ticks through the host
// (the scheduler's pass: gate, danger update, perform), and each frame the host steps the goal runs on the world clock. An NPC the
// hero hits reacts (the attack event); a fighting NPC walks up tile by tile, swings and bites through the session's NPC hit step. NPC
// bodies are their T0 frames moved with the walker (no walk or attack ART yet). Automated runs (self-test, native smokes) keep the AI
// off unless their stage turns it on, so the other stages stay as they were. In a turn-based round (ARC-WORLD-095) the round host
// runs the NPCs' subturn hooks with this host and their runs pay action points. NOT READY: casting, items, throws, pickups, followers
// in combat, NPC ranged attacks, saved NPC positions.
public partial class TerrainViewerApp
{
    private AreaAiHost? _aiHost;
    private (GameSessionOwner Session, int Map, AreaLoadResult Area, string Party)? _aiHostStamp;
    // The AI smoke stage turns the AI on in an automated run.
    private bool _aiForSmoke;
    // ARC-WORLD-099: the fidgets the AI host reported (smoke stage ai).
    private int _aiFidgets;
    private string? _aiNotice;
    // The line of fire the AI reads, rebuilt once per frame.
    private (ulong Frame, AreaLineOfFire? Map)? _aiLineOfFire;
    // The hero's overlay with the moved NPCs, while neither changed.
    private (AreaOccupancy? Base, long Version, AreaOccupancy Value)? _aiPcOccupancy;

    private bool AiEnabled => !AutomatedVisualRun || _aiForSmoke;

    /// <summary>The shown map's AI host, built again when the session, the map or the loaded area changes (its runs and movers are
    /// not saved, so a load or a map change stands every NPC on its map data's cell); null without a hero, combat values or a grid.</summary>
    private AreaAiHost? AreaAiHostNow()
    {
        if (!AiEnabled || _areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map)
        {
            if (_aiHost is not null) { _aiHost = null; _aiHostStamp = null; _aiPcOccupancy = null; }
            return null;
        }
        // A follower joining or leaving changes who the host runs.
        var stamp = (session, CurrentAreaMap, area, string.Join(",", session.QueryParty().Followers.Select(f => f.Entity)));
        if (_aiHost is not null && Equals(stamp, _aiHostStamp)) return _aiHost;
        var companions = (_areaCompanions?.Companions.Select(c => c.Key) ?? []).Concat(_areaCompanions?.Absent ?? new HashSet<AreaObjectKey>()).ToHashSet();
        var npcs = new List<AreaAiNpc>();
        foreach (var holder in map.Seed.Holders)
        {
            if (holder.Kind != SessionHolderKind.Npc || !map.TryGetRow(holder.Entity, out var row) || companions.Contains(row.Key)
                || session.QueryCompanion(holder.Entity).LedByPc || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            npcs.Add(new(holder.Entity, row, new(checked((int)x), checked((int)y))));
        }
        var chart = grid.Chart;
        var mapNumber = CurrentAreaMap;
        AreaAiHost? host = null;
        var scene = new AreaAiScene
        {
            RuntimeMap = mapNumber, Chart = chart, Steps = grid.Snapshot, Npcs = npcs, Combat = combat,
            HeroCell = () => _worldActor.Session is AreaGridSession hero ? World(hero.Chart.ToWorld(hero.State.CommittedCell)) : null,
            Companions = () => _areaCompanions is { } group
                ? group.Companions.Select(c => (c.Key, World(chart.ToWorld(c.Grid.State.CommittedCell)))).ToArray() : [],
            Removed = AreaRemovedObjects,
            LineOfFire = () => AiLineOfFire(host),
            DropMap = AreaDropMapNow,
            HeroWorn = () => AreaCombatHost.HeroWorn(session, _areaKitDurability),
            HeroCombatMode = CombatModeContext,
            Parameters = _companionData?.Data?.Table ?? (_arguments is { } arguments ? MapCompanionData(arguments.ModulePath)?.Table : null),
            Timing = grid.Timing,
            HeroIdle = HeroIdleForRound,
        };
        host = new AreaAiHost(session, scene, AreaEvents, AreaCombatWorldNow());
        _aiHost = host; _aiHostStamp = stamp; _aiPcOccupancy = null;
        var refused = host.ScheduleTicks();
        GD.Print($"ARC-WORLD-092 ai-host map={mapNumber} npcs={npcs.Count} ticks-refused={refused} table={scene.Parameters is not null}");
        return host;

        static CellCoord World(Arcanum.Data.Traversal.AreaStepCell cell) => new(checked((int)cell.X), checked((int)cell.Y));
    }

    // The shown map's line of fire with the moved NPCs on their cells, rebuilt once per frame.
    private AreaLineOfFire? AiLineOfFire(AreaAiHost? host)
    {
        var frame = Engine.GetProcessFrames();
        if (_aiLineOfFire is { } cached && cached.Frame == frame) return cached.Map;
        var map = AreaLineOfFireNow();
        _aiLineOfFire = (frame, map);
        return map;
    }

    /// <summary>Per frame, after the hero's attack: the due goal runs step, and what they did reaches the status line, the bodies and
    /// the drawing.</summary>
    private void ProcessAreaAi()
    {
        if (AreaAiHostNow() is not { } host) return;
        var now = WorldMilliseconds();
        var steps = host.Pump(now);
        // ARC-WORLD-095: in a round the runs' charges and subturn ends reach the round log and the status line's action points.
        var roundEvents = host.DrainRoundEvents();
        LogRound(roundEvents);
        if (roundEvents.Count > 0 || (steps > 0 && InTurnBasedRound)) RefreshAreaStatus();
        var redraw = steps > 0;
        foreach (var notice in host.DrainNotices())
        {
            switch (notice.Kind)
            {
                case AreaAiNoticeKind.Swing:
                    GD.Print($"ARC-WORLD-092 ai-swing npc={notice.Npc} target={notice.Target} action={notice.Swing.ActionFrameMs} rest={notice.Swing.RestMs}");
                    break;
                case AreaAiNoticeKind.Hit:
                    AiHit(notice);
                    redraw = true;
                    break;
                case AreaAiNoticeKind.Fidget:
                    // ARC-WORLD-099: the fidget after a done run (0x4B4320); NPC ART is not drawn, so only the log shows it.
                    _aiFidgets++;
                    GD.Print($"ARC-WORLD-099 ai-fidget npc={notice.Npc}");
                    break;
            }
        }
        if (redraw || host.Movers.All.Any(m => m.Progress(now) < 1)) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    // An NPC's hit: the status line, a stun's end, a killed NPC's fall, the bodies and the inventory.
    private void AiHit(AreaAiNotice notice)
    {
        var owner = _areaSession!;
        var result = notice.Result!;
        var hero = notice.Target == AiSchedulerRules.Pc;
        var life = hero ? owner.QueryHeroLife() : _combatHost?.Build(CurrentAreaMap, notice.Target!).Combat is { } combat ? owner.QueryLife(combat.Target).View : null;
        _aiNotice = $"{AiName(notice.Npc)} attacks {(hero ? "the hero" : AiName(notice.Target!))}: "
            + AreaCombatHost.Describe(result, life?.Status.HitPoints, life?.Status.MaxHitPoints);
        FloatCritterAttack(result, notice.Npc, hero ? HeroFloater : notice.Target!);
        GD.Print($"ARC-WORLD-092 ai-hit npc={notice.Npc} target={notice.Target} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} recipient={result.Recipient} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} step={result.StepFailure}");
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned))
        {
            var struck = result.Recipient == DamageRecipient.Target ? notice.Target! : notice.Npc;
            var target = struck == AiSchedulerRules.Pc ? SessionLifeTarget.Hero : _combatHost?.Build(CurrentAreaMap, struck).Combat?.Target;
            if (target is not null) EndStun(owner, target, struck);
        }
        // A hero knocked down or out stops its walk and attack (its fall is not drawn yet).
        if (hero && owner.QueryHeroLife() is { Status.IsActive: false }) { StopWorldRoute("HERO_DOWN"); CancelAreaAttack("the hero is down"); }
        if (!hero && result.Recipient == DamageRecipient.Target && result.Life is { } dealt && dealt.Events.HasFlag(LifeEvents.Died))
            _deathAnimations[(CurrentAreaMap, notice.Target!)] = AreaFallenPoses.Animation(dealt.Death);
        SyncAreaCombatBodies();
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    private string AiName(string entity) =>
        _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row)
            ? $"NPC {row.PrototypeNumber}" : entity;

    /// <summary>The moved NPCs' bodies: a fallen body where it stands, else its T0 body.</summary>
    private IReadOnlyList<AreaDrawCommand> AiMoverCommands()
    {
        if (_aiHost is not { } host || host.Movers.All.Count == 0 || _areaOverlay is not { } overlay || !ReferenceEquals(overlay.Area, _readyArea)) return [];
        var now = WorldMilliseconds();
        return host.Movers.Commands(key => _fallenBodies.TryGetValue(key, out var fallen) ? fallen : overlay.Bodies.TryGetValue(key, out var body) ? body : null, now);
    }

    /// <summary>The hero's overlay with the NPCs the AI moved: their T0 records moved and the cells they hold blocking.</summary>
    private AreaOccupancy? WithAiMovers(AreaOccupancy? occupancy)
    {
        if (_aiHost is not { } host || host.Movers.All.Count == 0 || _worldActor.Session is not AreaGridSession grid) return occupancy;
        if (_aiPcOccupancy is { } known && ReferenceEquals(known.Base, occupancy) && known.Version == host.Movers.Version) return known.Value;
        var moved = (occupancy?.Moved ?? new HashSet<AreaObjectKey>()).Concat(host.Movers.Moved);
        var held = (occupancy?.Occupied ?? new HashSet<CellCoord>()).Concat(host.Movers.Held().Select(cell => grid.Chart.ToChart(new(cell.X, cell.Y))));
        var value = new AreaOccupancy(moved, held);
        _aiPcOccupancy = (occupancy, host.Movers.Version, value);
        return value;
    }

    /// <summary>The overlay of the AI: each NPC with a run or a danger state, its target and the run's last reason.</summary>
    private string AiStatus()
    {
        if (_aiHost is not { } host || _areaSession is not { } session) return "";
        var shown = new List<string>();
        foreach (var npc in host.Scene.Npcs)
        {
            var danger = session.QueryDanger(npc.Entity);
            var run = host.RunOf(npc.Entity);
            if (danger.State == DangerState.None && run is null) continue;
            var target = danger.Source is { } source ? source.IsHero ? "hero" : AiName(source.Entity!) : "-";
            shown.Add($"{AiName(npc.Entity)} {danger.State}→{target} {run?.Top.Goal.ToString() ?? "idle"} {host.TraceOf(npc.Entity)?.Reason}");
            if (shown.Count == 3) break;
        }
        return shown.Count == 0 && _aiNotice is null ? "" : $" | AI: {(shown.Count == 0 ? "calm" : string.Join("; ", shown))}{(_aiNotice is null ? "" : $" ({_aiNotice})")}";
    }
}
