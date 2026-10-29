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
// runs the NPCs' subturn hooks with this host and their runs pay action points. ARC-WORLD-102: an NPC that knows a spell may choose it
// at its danger update and cast it through the map's cast host (the hero's); its casting goal is timed by its cast eye candy (not
// drawn on the NPC yet) and its release's effects reach the spell log, the bodies and the status line. ARC-WORLD-108: an NPC heals
// and fights with the items it holds (potions, scrolls, wands) through the same cast host; the inventory follows a spent item.
// ARC-WORLD-115: the hero's companions here are run too and fight beside it, stepping through their own walk. NOT READY: pickups,
// the attack order's target, NPC ranged attacks, saved NPC positions.
public partial class TerrainViewerApp
{
    private AreaAiHost? _aiHost;
    private (GameSessionOwner Session, int Map, AreaLoadResult Area, string Party, string Companions)? _aiHostStamp;
    // The AI smoke stage turns the AI on in an automated run.
    private bool _aiForSmoke;
    // ARC-WORLD-099: the fidgets the AI host reported (smoke stage ai).
    private int _aiFidgets;
    // ARC-WORLD-109: the NPC skill attempts seen (the ai-skill smoke reads it).
    private int _aiSkills;
    // ARC-WORLD-102: the NPC casts the AI host played and released (smoke stage ai-cast).
    private int _aiCasts, _aiCastReleases, _aiMissiles, _aiMissileArrivals;
    // ARC-WORLD-108: the NPC item uses whose magic branch ran (smoke stage ai-cast).
    private int _aiItemUses;
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
            if (_aiHost is not null) { RetireAiHost(); _aiHostStamp = null; _aiPcOccupancy = null; }
            return null;
        }
        // A follower joining or leaving changes who the host runs; ARC-WORLD-115: so do the companions standing here (a rebuilt group
        // with the same ones keeps the host: their walks are read from the group of the moment).
        var stamp = (session, CurrentAreaMap, area, string.Join(",", session.QueryParty().Followers.Select(f => f.Entity)),
            string.Join(",", _areaCompanions?.Companions.Select(c => c.Entity) ?? []));
        if (_aiHost is not null && Equals(stamp, _aiHostStamp)) return _aiHost;
        RetireAiHost();
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
            // ARC-UI-039: NPCs keep the human timing, not the hero body's.
            Timing = grid.Timing is null ? null : GaitTiming.HumanFixed,
            HeroIdle = HeroIdleForRound,
            // ARC-WORLD-102: the spell answers and the casts go through the map's cast host; its eye candy times an NPC's casting goal.
            Spells = SpellCasterNow,
            CastEyeCandy = NpcCastEyeCandy,
            MissileArt = spell => SpellMissileArt(spell) is not null,
            // ARC-WORLD-109: an NPC's skills for its skill attempts.
            NpcSheet = AreaFollowerSheet,
            // ARC-WORLD-114: the NPCs' first-heartbeat and heartbeat scripts (the Crash Site's unchosen pre-made heroes die by theirs).
            Heartbeat = AiHeartbeat,
            // ARC-WORLD-115: the hero's companions here fight beside it, stepping and turning through their own walk.
            Followers = _areaCompanions is { } group ? new AiFollowers(group, () => _areaCompanions, chart, session) : null,
            // ARC-RENDER-038: an NPC's attack plays its body's attack ART for its animation and the weapon it shows.
            AttackArt = NpcAttackArt,
        };
        host = new AreaAiHost(session, scene, AreaEvents, AreaCombatWorldNow());
        _aiHost = host; _aiHostStamp = stamp; _aiPcOccupancy = null;
        var refused = host.ScheduleTicks();
        GD.Print($"ARC-WORLD-092 ai-host map={mapNumber} npcs={npcs.Count} companions={host.RunCompanions.Count} ticks-refused={refused} table={scene.Parameters is not null}");
        return host;

        static CellCoord World(Arcanum.Data.Traversal.AreaStepCell cell) => new(checked((int)cell.X), checked((int)cell.Y));
    }

    /// <summary>ARC-WORLD-115: the companions of the shown map the AI host runs (those the hero leads, following or waiting), with their
    /// home map data's world cell; their steps and turns go to the group's walk (chart cells).</summary>
    private sealed class AiFollowers(AreaCompanionGroup group, Func<AreaCompanionGroup?> now, AreaChart chart, GameSessionOwner session) : IAreaAiFollowers
    {
        public IReadOnlyList<AreaAiNpc> Npcs { get; } = group.Companions.Where(c => session.QueryCompanion(c.Entity).LedByPc)
            .Select(c => new AreaAiNpc(c.Entity, c.Speaker.Row, World(chart.ToWorld(c.Origin)))).ToArray();

        public bool Step(string entity, CellCoord next, bool running) => now()?.StepTo(entity, chart.ToChart(new(next.X, next.Y)), running) == true;

        public void Face(string entity, CellCoord toward) => now()?.FaceTo(entity, chart.ToChart(new(toward.X, toward.Y)));

        private static CellCoord World(Arcanum.Data.Traversal.AreaStepCell cell) => new(checked((int)cell.X), checked((int)cell.Y));
    }

    // ARC-WORLD-114: the heartbeats whose refusal was logged (one line per NPC, point and reason, as the tick repeats every few seconds).
    private readonly HashSet<string> _heartbeatRefusalsLogged = new(StringComparer.Ordinal);
    // ARC-WORLD-114: the NPCs a heartbeat script killed or switched off (the pre-made smoke counts them).
    private int _heartbeatKills, _heartbeatOffs;

    /// <summary>ARC-WORLD-114: an NPC's first-heartbeat or heartbeat script through the shown map's use-script host, whose toggle state and
    /// kill reach the session; true lets the tick go on. A run that is refused or not applied goes on as without a script.</summary>
    private bool AiHeartbeat(AiScriptPoint point, string npc)
    {
        if (_readyArea is not { } area || _areaSession is not { } session || UseScriptHost(area.Snapshot) is not { } host
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map || !map.TryGetRow(npc, out var row)) return true;
        var scrPoint = point == AiScriptPoint.Heartbeat ? Arcanum.Scripting.Scr.ScrPoint.Heartbeat : Arcanum.Scripting.Scr.ScrPoint.FirstHeartbeat;
        if (host.Slot(new(CurrentAreaMap, npc), scrPoint) is not { Script: > 0 }) return true;
        var ran = host.RunHeartbeat(row, scrPoint, session.Rules.Scripts);
        if (ran.Outcome != AreaScriptedUseOutcome.NoTeleport)
        {
            if (_heartbeatRefusalsLogged.Add($"{npc}|{scrPoint}|{ran.Outcome}|{ran.Run?.Script}"))
                GD.Print($"ARC-WORLD-114 heartbeat-refused npc={npc} point={scrPoint} outcome={ran.Outcome} detail={ran.Detail}");
            return true;
        }
        var states = ran.Run!.Effects.ObjectStates;
        // A skip default (the script's return, or one by rule: a missing script, the end of the script, the line budget) stops the
        // tick's pass; logged once per NPC, point and stop, so an NPC that stays still has a trace.
        if (ran.Run.Outcome != Arcanum.Scripting.Scr.ScrOutcome.RunDefault && states.Count == 0
            && _heartbeatRefusalsLogged.Add($"{npc}|{scrPoint}|skip|{ran.Run.Script}|{ran.Run.Stop}"))
            GD.Print($"ARC-WORLD-114 heartbeat-skip npc={npc} point={scrPoint} script={ran.Run.Script} stop={ran.Run.Stop}");
        if (states.Count > 0)
        {
            _heartbeatKills += states.Count(s => s.Change == Arcanum.Scripting.Scr.ScrObjectStateChange.Kill);
            _heartbeatOffs += states.Count(s => s.Change == Arcanum.Scripting.Scr.ScrObjectStateChange.ToggleOff);
            GD.Print($"ARC-WORLD-114 heartbeat npc={npc} point={scrPoint} script={ran.Run.Script} outcome={ran.Run.Outcome} states="
                + string.Join(",", states.Select(s => $"{s.Change}:{s.Object.Code}")));
        }
        return ran.Run.Outcome == Arcanum.Scripting.Scr.ScrOutcome.RunDefault;
    }

    /// <summary>ARC-WORLD-114: whether a script's kill can be applied: an NPC of the shown map whose combat values read.</summary>
    private bool CanKillByScript(Arcanum.Scripting.Scr.ScrObjectRef obj) => _areaSession is { } session && _combatHost is not null
        && obj.Map == CurrentAreaMap && _combatHost.Build(session, obj.Map, obj.Code).Combat is not null;

    /// <summary>ARC-WORLD-114: a script's kill (58) of an NPC of the shown map: a death with no killer through the session's life domain
    /// (no experience, no killer's notification), drawn with the plain fall; false when it has no combat values or the session refuses.</summary>
    private bool KillByScript(Arcanum.Scripting.Scr.ScrObjectRef obj)
    {
        if (_areaSession is not { } session || _combatHost is null || obj.Map != CurrentAreaMap
            || _combatHost.Build(session, obj.Map, obj.Code).Combat is not { } combat) return false;
        var killed = session.Kill(session.Version, combat.Target);
        GD.Print($"ARC-WORLD-114 script-kill npc={obj.Code} disposition={killed.Disposition} failure={killed.Failure} events={killed.Outcome?.Events}");
        if (killed.Outcome is { } outcome && outcome.Events.HasFlag(LifeEvents.Died)) _deathAnimations[(obj.Map, obj.Code)] = AreaFallenPoses.Animation(outcome.Death);
        return killed.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>ARC-WORLD-102: drops the AI host; one of the current session first ends its runs (<see cref="AreaAiHost.Retire"/>), so a
    /// casting goal's magic run does not outlive it.</summary>
    private void RetireAiHost()
    {
        if (_aiHost is { } host && ReferenceEquals(host.Session, _areaSession)) host.Retire();
        _aiHost = null;
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
                    GD.Print($"ARC-WORLD-092 ai-swing npc={notice.Npc} target={notice.Target} action={notice.Swing.ActionFrameMs} rest={notice.Swing.RestMs}"
                        + $" animation={notice.Play?.Wanted.Animation} weapon={notice.Play?.Wanted.Weapon} shield={notice.Play?.Wanted.Shield}"
                        + $" art={(notice.Play?.Art is { } art ? $"{art.Animation}/{art.Weapon}/{art.Shield} frames={art.Frames} actionFrame={art.ActionFrame}" : "fixed")}");
                    break;
                case AreaAiNoticeKind.Hit:
                    AiHit(notice);
                    redraw = true;
                    break;
                case AreaAiNoticeKind.Cast:
                    _aiCasts++;
                    _aiNotice = $"{AiName(notice.Npc)} casts spell {notice.Spell}";
                    GD.Print($"ARC-WORLD-102 ai-cast npc={notice.Npc} spell={notice.Spell} run={notice.Run} target={notice.Target} plays={notice.CastSwing.Plays}"
                        + $" action={notice.CastSwing.ActionFrameMs} rest={notice.CastSwing.RestMs}");
                    break;
                case AreaAiNoticeKind.Release:
                    AiCastReleased(notice);
                    redraw = true;
                    break;
                case AreaAiNoticeKind.Missile:
                    // ARC-WORLD-105: the NPC's launched missile flies as the hero's (TerrainViewerSpellMissiles.cs).
                    _aiMissiles++;
                    _aiNotice = $"{AiName(notice.Npc)} casts spell {notice.Spell}: a missile";
                    LaunchNpcSpellMissile(notice, now);
                    break;
                case AreaAiNoticeKind.CastEnd:
                    GD.Print($"ARC-WORLD-102 ai-cast-end npc={notice.Npc} run={notice.Run} freed={notice.SpellRun?.Disposition.ToString() ?? "nothing"}");
                    break;
                case AreaAiNoticeKind.ItemUse:
                    // ARC-WORLD-108: an NPC used an item (goal 39 or the chooser's use at once): its spells ran through the cast host.
                    _aiItemUses++;
                    _aiNotice = $"{AiName(notice.Npc)} uses an item";
                    GD.Print($"ARC-WORLD-108 ai-item-use npc={notice.Npc} item={notice.Item} target={notice.Target} outcome={notice.ItemUse?.Outcome}"
                        + $" cast={notice.ItemUse?.Cast} fired={notice.ItemUse?.ItemUsed?.Spells.Count ?? 0} spent={notice.ItemUse?.Spent}");
                    SyncAreaInventory();
                    redraw = true;
                    break;
                case AreaAiNoticeKind.Skill:
                    // ARC-WORLD-109: a skill attempt at its action frame; a heal changes a body's life and the healer's item.
                    _aiSkills++;
                    _aiNotice = $"{AiName(notice.Npc)} uses skill {notice.Skill}" + (notice.Heal is { } heal
                        ? $": {(heal.Check?.Success == true ? $"heals {heal.Heal?.HitPoints}" : heal.Disposition == GameSessionDisposition.Changed ? "fails" : $"refused ({heal.Failure})")}" : "");
                    GD.Print($"ARC-WORLD-109 ai-skill npc={notice.Npc} skill={notice.Skill} target={notice.Target} disposition={notice.Heal?.Disposition.ToString() ?? "none"}"
                        + $" failure={notice.Heal?.Failure} success={notice.Heal?.Check?.Success} hp={notice.Heal?.Heal?.HitPoints} uses={notice.Heal?.UsesLeft}");
                    if (notice.Heal is { Disposition: GameSessionDisposition.Changed }) AiSpellTook();
                    break;
                case AreaAiNoticeKind.Throw:
                    AiThrow(notice);
                    redraw = true;
                    break;
                case AreaAiNoticeKind.Fidget:
                    // ARC-WORLD-099: the fidget after a done run (0x4B4320); NPC ART is not drawn, so only the log shows it.
                    _aiFidgets++;
                    GD.Print($"ARC-WORLD-099 ai-fidget npc={notice.Npc}");
                    break;
            }
        }
        // ARC-WORLD-111: a stepping NPC's cycle advances every frame, and its stand pose is drawn once its step ends.
        if (AiMoversAnimating(host, now) | redraw) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    // An NPC's hit: the status line, a stun's end, a killed NPC's fall, the bodies and the inventory.
    private void AiHit(AreaAiNotice notice)
    {
        var owner = _areaSession!;
        var result = notice.Result!;
        var hero = notice.Target == AiSchedulerRules.Pc;
        var life = hero ? owner.QueryHeroLife() : _combatHost?.Build(owner, CurrentAreaMap, notice.Target!).Combat is { } combat ? owner.QueryLife(combat.Target).View : null;
        _aiNotice = $"{AiName(notice.Npc)} attacks {(hero ? "the hero" : AiName(notice.Target!))}: "
            + AreaCombatHost.Describe(result, life?.Status.HitPoints, life?.Status.MaxHitPoints);
        FloatCritterAttack(result, notice.Npc, hero ? HeroFloater : notice.Target!);
        GD.Print($"ARC-WORLD-092 ai-hit npc={notice.Npc} target={notice.Target} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} recipient={result.Recipient} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} step={result.StepFailure}");
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned))
        {
            var struck = result.Recipient == DamageRecipient.Target ? notice.Target! : notice.Npc;
            var target = struck == AiSchedulerRules.Pc ? SessionLifeTarget.Hero : _combatHost?.Build(owner, CurrentAreaMap, struck).Combat?.Target;
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

    // ARC-WORLD-113: an NPC's throw at its action frame: a struck critter's attack reaches the status line and the bodies as a hit's;
    // the item lies where it landed (the inventory and the ground).
    private void AiThrow(AreaAiNotice notice)
    {
        var thrown = notice.Throw!;
        GD.Print($"ARC-WORLD-113 ai-throw npc={notice.Npc} item={notice.Item} struck={notice.Target ?? "none"} disposition={thrown.Disposition}"
            + $" failure={thrown.Failure} outcome={thrown.Attack?.Resolution?.Outcome} landed={(thrown.Landed is { } at ? $"{at.X},{at.Y}" : "none")}");
        if (notice.Target is not null && notice.Result is not null)
        {
            AiHit(notice);
            return;
        }
        _aiNotice = $"{AiName(notice.Npc)} throws: {(thrown.Landed is null ? $"refused ({thrown.Failure})" : "it lands")}";
        SyncAreaCombatBodies();
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    // ARC-WORLD-102: an NPC's cast released (its BEGIN ran, or the re-check refused it): the cast host's log reaches the status line and
    // the message window (its eye-candy components play on their targets), and its damage the bodies, the inventory and the status line.
    private void AiCastReleased(AreaAiNotice notice)
    {
        if (notice.Released) _aiCastReleases++;
        GD.Print($"ARC-WORLD-102 ai-cast-release npc={notice.Npc} spell={notice.Spell} run={notice.Run} released={notice.Released}"
            + $" disposition={notice.SpellRun?.Disposition} failure={notice.SpellRun?.Failure} hero-hp={_areaSession?.QueryHeroLife()?.Status.HitPoints}");
        FlushSpellLog(notice.Released ? null : $"{AiName(notice.Npc)}'s spell found no target");
        AiSpellTook();
    }

    // An NPC's spell began (at its release, ARC-WORLD-105 also at its missile's arrival): a downed hero stops, and the damage reaches
    // the bodies, the inventory and the status line.
    private void AiSpellTook()
    {
        if (_areaSession?.QueryHeroLife() is { Status.IsActive: false }) { StopWorldRoute("HERO_DOWN"); CancelAreaAttack("the hero is down"); }
        SyncAreaCombatBodies();
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    private string AiName(string entity) =>
        _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row)
            ? $"NPC {row.PrototypeNumber}" : entity;

    /// <summary>The moved NPCs' bodies: a fallen body where it stands, else its T0 body with the frame of its step and facing
    /// (ARC-WORLD-111).</summary>
    private IReadOnlyList<AreaDrawCommand> AiMoverCommands()
    {
        if (_aiHost is not { } host || host.Movers.All.Count == 0 || _areaOverlay is not { } overlay || !ReferenceEquals(overlay.Area, _readyArea)) return [];
        var now = WorldMilliseconds();
        return AnimateAiMovers(host, host.Movers.Commands(key => _fallenBodies.TryGetValue(key, out var fallen) ? fallen : overlay.Bodies.TryGetValue(key, out var body) ? body : null, now), now);
    }

    /// <summary>The hero's overlay with the NPCs the AI moved: their T0 records moved and the cells they hold blocking.</summary>
    // ARC-WORLD-115: the AI's moved NPCs on a companion's overlay, one value per base overlay while the movers do not change (a planner
    // asks at every step it assesses).
    private readonly Dictionary<AreaOccupancy, AreaOccupancy> _aiCompanionOccupancy = new(ReferenceEqualityComparer.Instance);
    private long _aiCompanionOccupancyVersion = -1;

    private AreaOccupancy AiMoversOverlay(AreaOccupancy occupancy)
    {
        if (_aiHost is not { } host || host.Movers.All.Count == 0 || _worldActor.Session is not AreaGridSession grid) return occupancy;
        if (_aiCompanionOccupancyVersion != host.Movers.Version || _aiCompanionOccupancy.Count > 64)
        {
            _aiCompanionOccupancy.Clear();
            _aiCompanionOccupancyVersion = host.Movers.Version;
        }
        if (_aiCompanionOccupancy.TryGetValue(occupancy, out var known)) return known;
        var value = new AreaOccupancy(occupancy.Moved.Concat(host.Movers.Moved),
            occupancy.Occupied.Concat(host.Movers.Held().Select(cell => grid.Chart.ToChart(new(cell.X, cell.Y)))));
        _aiCompanionOccupancy[occupancy] = value;
        return value;
    }

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
