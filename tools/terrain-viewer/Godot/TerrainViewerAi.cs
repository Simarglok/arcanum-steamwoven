using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
// ARC-WORLD-115: the hero's companions here are run too and fight beside it, stepping through their own walk. ARC-WORLD-119: so are
// the free ones (released or cursed), as any leaderless NPC; a leave does not rebuild the host. NOT READY: pickups,
// the attack order's target, NPC ranged attacks, saved NPC positions.
public partial class TerrainViewerApp
{
    private AreaAiHost? _aiHost;
    // ARC-DEV-119 (MIG9 S5c): the stamp carries no area for a map shown as windows (a window move keeps the host); _aiHostArea is the area its scene was built for.
    private (GameSessionOwner Session, int Map, AreaLoadResult? Area, string Party, string Companions)? _aiHostStamp;
    private AreaLoadResult? _aiHostArea;
    // True while a window move publishes the next window of the same session: the unbinding it runs keeps the host (its casting runs end).
    private bool _aiKeepAcrossWindowMove;
    // ARC-DEV-119: how many hosts were built and how often a window move rebound the standing one (the stream smoke's AI observation reads them).
    private int _aiHostBuilds, _aiHostRebinds;
    // The AI smoke stage turns the AI on in an automated run.
    private bool _aiForSmoke;
    // ARC-WORLD-099: the fidgets the AI host reported (smoke stage ai).
    private int _aiFidgets, _heroFidgets;
    // ARC-WORLD-109: the NPC skill attempts seen (the ai-skill smoke reads it).
    private int _aiSkills;
    // ARC-WORLD-102: the NPC casts the AI host played and released (smoke stage ai-cast).
    private int _aiCasts, _aiCastReleases, _aiMissiles, _aiMissileArrivals;
    // ARC-WORLD-108: the NPC item uses whose magic branch ran (smoke stage ai-cast).
    private int _aiItemUses;
    private string? _aiNotice;
    // The line of fire the AI reads, rebuilt once per frame.
    private (ulong Frame, GameSessionOwner? Owner, AreaLineOfFire? Map)? _aiLineOfFire;
    // The hero's overlay with the moved NPCs, while neither changed.
    private (AreaOccupancy? Base, long Version, AreaOccupancy Value)? _aiPcOccupancy;

    // ARC-DEV-118 (MIG9 S5b): the main map's host chart over the loaded sectors, built with the map's sector cache (a window move keeps it).
    private AreaSectorChart? _sectorChart;
    private (GameSessionOwner Session, int Map)? _sectorChartKey;

    // ARC-DEV-116: the waiting requests of NPCs outlive a rebuilt host; they belong to one session and one shown map.
    private (GameSessionOwner Session, int Map, List<(string Npc, int Map, CellCoord Cell)> Queue)? _homecomingQueue;

    private List<(string Npc, int Map, CellCoord Cell)> HomecomingQueue(GameSessionOwner session, int map)
    {
        if (_homecomingQueue is { } held && ReferenceEquals(held.Session, session) && held.Map == map) return held.Queue;
        var queue = new List<(string, int, CellCoord)>();
        _homecomingQueue = (session, map, queue);
        return queue;
    }

    private bool AiEnabled => !AutomatedVisualRun || _aiForSmoke;

    /// <summary>The shown map's AI host, built again when the session, the map or the party changes (its runs and movers are not saved, so a load or a
    /// map change stands every NPC on its map data's cell); null without a hero, combat values or a grid. ARC-DEV-119 (MIG9 S5c): a window move of the
    /// main map does not build it again: the host is the map's, and the scene the new window gave it replaces the old (<see cref="AreaAiHost.Rebind"/>);
    /// a map without a window (a whole map, the cave) is built again when its area is.</summary>
    private AreaAiHost? AreaAiHostNow()
    {
        if (!AiEnabled || _areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map)
        {
            if (_aiHost is not null) { RetireAiHost(); _aiHostStamp = null; _aiPcOccupancy = null; }
            return null;
        }
        // A follower joining or leaving changes who the host runs; ARC-WORLD-115: so do the companions standing here (a rebuilt group
        // with the same ones keeps the host: their walks are read from the group of the moment). ARC-WORLD-119: the host runs the group's
        // companions whether they follow or not, so only the joins and leaves of other NPCs rebuild it; a group companion's leave ends
        // its own run in the host (AreaAiHost.ObserveLeaves), the other NPCs' runs go on.
        var companions = (_areaCompanions?.Companions.Select(c => c.Key) ?? []).Concat(_areaCompanions?.Absent ?? new HashSet<AreaObjectKey>()).ToHashSet();
        // A visitor from another home map has no row here: the group's entities count too.
        var groupEntities = (_areaCompanions?.Companions.Select(c => c.Entity) ?? []).ToHashSet(StringComparer.Ordinal);
        var windowed = area.Snapshot.Window is not null;
        var stamp = (session, CurrentAreaMap, windowed ? null : area,
            string.Join(",", session.QueryParty().Followers.Select(f => f.Entity)
                .Where(f => !groupEntities.Contains(f) && (!map.TryGetRow(f, out var r) || !companions.Contains(r.Key)))),
            string.Join(",", _areaCompanions?.Companions.Select(c => c.Entity) ?? []));
        if (_aiHost is { } kept && Equals(stamp, _aiHostStamp))
        {
            if (ReferenceEquals(area, _aiHostArea)) { SyncCreatedCritters(session, kept); return kept; }
            // ARC-DEV-119: the map's window moved; the host goes on over the scene the new window gives.
            var rebound = BuildAiScene(session, area, grid, map, combat, companions, () => _aiHost);
            kept.Rebind(rebound);
            _aiHostArea = area; _aiPcOccupancy = null; _aiLineOfFire = null; _aiHostRebinds++;
            if (windowed) StartSectorChart(session, CurrentAreaMap, area, grid);
            var late = kept.ScheduleTicks();
            GD.Print($"ARC-DEV-119 ai-host rebound map={CurrentAreaMap} npcs={kept.Scene.Npcs.Count} window-npcs={rebound.Npcs.Count} companions={kept.RunCompanions.Count} ticks-refused={late}");
            SyncCreatedCritters(session, kept);
            return kept;
        }
        RetireAiHost();
        var scene = BuildAiScene(session, area, grid, map, combat, companions, () => _aiHost);
        var mapNumber = CurrentAreaMap;
        var host = new AreaAiHost(session, scene, AreaEvents, AreaCombatWorldNow());
        _aiHost = host; _aiHostStamp = stamp; _aiHostArea = area; _aiPcOccupancy = null; _aiHostBuilds++;
        host.DoorEnds = DoorRunEnded;
        host.MissileEnds = MissileRunEnded;
        host.ProjectileEnds = ProjectileRunEnded;
        host.KnockbackEnds = KnockbackRunEnded;
        host.HeroEnds = HeroRunEnded;
        session.KillNotice = request => { if (request.Map == mapNumber) host.NoticeKill(request.Victim, request.Killer); };
        // ARC-DEV-104 (MIG9 S4): a whole map opens its residency and sector cache once per map open; the arrival pre-cache folds the 3 × 3
        // sectors around the hero, whose NPCs get their first tick there. The delayed ticks below go only to NPCs without one.
        if (AreaResidency.Open(session, area, map, scene.HeroCell()) is { } opened)
            GD.Print($"ARC-DEV-104 residency map={mapNumber} status={opened.Residency.Status} rows={opened.Residency.Rows} sectors={opened.Sectors?.ToString() ?? "-"}"
                + $" extent={opened.Extent.SectorsX}x{opened.Extent.SectorsY} folds={opened.Arrival?.Folds.Count ?? 0}"
                + $" ticks={opened.Arrival?.Folds.Sum(f => f.Npcs.Count - f.Refused.Count) ?? 0} failure={opened.Arrival?.Failure.ToString() ?? "-"}"
                + (opened.Residency.Detail.Length > 0 ? $" detail={opened.Residency.Detail}" : ""));
        if (windowed) StartSectorChart(session, mapNumber, area, grid); else DropSectorChart();
        var refused = host.ScheduleTicks();
        GD.Print($"ARC-WORLD-092 ai-host map={mapNumber} npcs={scene.Npcs.Count} companions={host.RunCompanions.Count} ticks-refused={refused} table={scene.Parameters is not null}");
        SyncCreatedCritters(session, host);
        return host;
    }

    // ARC-WORLD-1162: the beds of the shown map with the world cell each stands on (map mode only: the cave's transitions hold none).
    private IReadOnlyList<(AreaObjectKey Key, CellCoord Cell)> PlacedBeds() =>
        _mapTransfers is not { } transfers ? []
            : [.. transfers.Beds.Select(key => (Key: key, Row: transfers.Object(key))).Where(bed => bed.Row.WorldX is not null && bed.Row.WorldY is not null)
                .Select(bed => (bed.Key, new CellCoord(checked((int)bed.Row.WorldX!.Value), checked((int)bed.Row.WorldY!.Value))))];

    /// <summary>The scene the shown window gives the AI host: its chart and evidence, the rows its map data holds (the companions and the NPCs the party leads
    /// left out) and the viewer's live providers. <paramref name="host"/> is the host the scene belongs to, for the line of fire's frame cache.</summary>
    private AreaAiScene BuildAiScene(GameSessionOwner session, AreaLoadResult area, AreaGridSession grid, AreaInventoryMap map, AreaCombatHost combat,
        IReadOnlySet<AreaObjectKey> companions, Func<AreaAiHost?> host)
    {
        var npcs = new List<AreaAiNpc>();
        foreach (var holder in map.Seed.Holders)
        {
            if (holder.Kind != SessionHolderKind.Npc || !map.TryGetRow(holder.Entity, out var row) || companions.Contains(row.Key)
                || session.QueryCompanion(holder.Entity).LedByPc || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            npcs.Add(new(holder.Entity, row, new(checked((int)x), checked((int)y))));
        }
        var chart = grid.Chart;
        var mapNumber = CurrentAreaMap;
        return new AreaAiScene
        {
            RuntimeMap = mapNumber, Chart = chart, Steps = grid.Snapshot, Npcs = npcs, Combat = combat,
            // ARC-DEV-137 (MIG13 S6): the critter-state goals time the fall, the get-up and the dying animation by the NPC's ART.
            AnimationClip = NpcAnimationClip,
            // ARC-DEV-118 (MIG9 S5b): a window of the main map reads the sectors its session's cache has loaded, not the window.
            LoadedSteps = area.Snapshot.Window is not null ? () => _sectorChart?.Steps : null,
            // ARC-DEV-209 (MIG14 S3, GT49): the walkers' opener goals run over the shown map's door host; the closed portals a step crosses are read from its T0 assessment.
            DoorWorld = () => _areaDoors,
            Passages = () => _areaPortals is { } passages && _areaSession is { } shown ? passages.Passages(shown) : null,
            ClosedPortalsOf = step => _areaPortals is { } portals && _areaSession is { } shown ? [.. portals.CrossedClosed(shown, step).Select(p => p.Entity)] : [],
            SectorsTouched = area.Snapshot.Window is not null ? RequestSectorChart : null,
            HeroCell = () => _worldActor.Session is AreaGridSession hero ? World(hero.Chart.ToWorld(hero.State.CommittedCell)) : null,
            Companions = () => _areaCompanions is { } group
                ? group.Companions.Select(c => (c.Entity, c.Key, World(chart.ToWorld(c.Grid.State.CommittedCell)))).ToArray() : [],
            Removed = AreaRemovedObjects,
            LineOfFire = () => AiLineOfFire(host()),
            DropMap = AreaDropMapNow,
            // ARC-WORLD-1157 (SZ6): a generator's off-screen option reads the game window and the generator's drawn rectangle.
            Screen = () => new RestockScreen(this),
            HeroWorn = () => AreaCombatHost.HeroWorn(session, _areaKitDurability),
            HeroCombatMode = CombatModeContext,
            // ARC-DEV-287 (MIG13 tail T6): the hero is pushed by the knockback through its grid actor.
            HeroPush = new ViewerHeroPush(this),
            // ARC-DEV-126 (MIG12 S7): a dialog is no pause, so the NPC the hero talks with is the AI gate's conversation partner and stands still (TL: the gate's Conversation stop).
            ConversationPartner = ConversationPartnerEntity,
            ConversationEnded = npc => _conversationEndPending = npc,
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
            // ARC-MAGIC-1189 (TT10, SP2): an NPC's or a companion's step checks the next tile for a trap it spots with its own roll.
            Traps = AreaTrapsNow,
            // ARC-WORLD-114: the NPCs' first-heartbeat and heartbeat scripts (the Crash Site's unchosen pre-made heroes die by theirs).
            Heartbeat = AiHeartbeat,
            CatchingThief = AiCatchingThief,
            // ARC-DEV-258 (MIG13 S14n): an NPC's thrown grenade flies as a projectile.
            LaunchThrown = AiLaunchThrown,
            // ARC-DEV-282: an NPC's arrow or bullet flies as a projectile and strikes through the AI host.
            LaunchShot = AiLaunchShot,
            // ARC-COMBAT-1140 (RA4): a door, container, scenery, trap or wall that stops the line of fire takes the shot: its target, its flight and the follow-up of an object hit.
            ObjectTarget = row => _areaSession is { } objectOwner && !IsAreaCompanion(row.Key) ? ObjectTargets()?.Target(objectOwner, row.Key, AreaTrapped) : null,
            LaunchShotAtObject = AiLaunchShotAtObject,
            ObjectStruck = NpcObjectHit,
            // ARC-DEV-261 (GT198): the victims' taking-damage scripts (point 0x17) of the NPCs' attacks, throws and knockback bumps.
            TakingDamage = TakingDamageGoesOn,
            // ARC-WORLD-115: the hero's companions here fight beside it, stepping and turning through their own walk (ARC-WORLD-119: the
            // free ones act as any NPC through the same walk).
            Followers = _areaCompanions is { } followerGroup ? new AiFollowers(followerGroup, () => _areaCompanions, chart) : null,
            // ARC-RENDER-038: an NPC's attack plays its body's attack ART for its animation and the weapon it shows.
            AttackArt = NpcAttackArt,
            AlertedSound = AiAlertedSound,
            // ARC-DEV-116 (MIG11 S5): the maps an NPC's change of place can name.
            MapListed = MapListedNow,
            Homecomings = HomecomingQueue(session, mapNumber),
            // ARC-WORLD-1162 (SWB17–SWB21): the shown map's beds and who lies in them, the hero's rest's occupancy too.
            Beds = new AreaAiBeds(_bedOccupancy, PlacedBeds),
        };

        static CellCoord World(Arcanum.Data.Traversal.AreaStepCell cell) => new(checked((int)cell.X), checked((int)cell.Y));
    }

    /// <summary>ARC-DEV-126: the entity of the NPC the hero is talking or bartering with, or null. The original's dialog is suspended under a barter with the partner kept (GT139), and here the barter holds the merchant, so the merchant is a partner too.</summary>
    private string? ConversationPartnerEntity()
    {
        if (ConversationOpen && _conversationSpeaker is { } speaker) return AreaConversationState.EntityId(speaker.Row);
        return BarterOpen && _barter is { } barter ? barter.Merchant.Entity : null;
    }

    /// <summary>ARC-WORLD-115: the companions of the shown map the AI host runs (ARC-WORLD-119: every one standing here, following,
    /// waiting or free), with their home map data's world cell; their steps and turns go to the group's walk (chart cells).</summary>
    private sealed class AiFollowers(AreaCompanionGroup group, Func<AreaCompanionGroup?> now, AreaChart chart) : IAreaAiFollowers
    {
        public IReadOnlyList<AreaAiNpc> Npcs { get; } = group.Companions
            .Select(c => new AreaAiNpc(c.Entity, c.Speaker.Row, World(chart.ToWorld(c.Origin)))).ToArray();

        public bool Step(string entity, CellCoord next, bool running) => now()?.StepTo(entity, chart.ToChart(new(next.X, next.Y)), running) == true;

        public void Face(string entity, CellCoord toward) => now()?.FaceTo(entity, chart.ToChart(new(toward.X, toward.Y)));

        public bool Jump(string entity, CellCoord cell) => now()?.JumpTo(entity, chart.ToChart(new(cell.X, cell.Y))) == true;

        private static CellCoord World(Arcanum.Data.Traversal.AreaStepCell cell) => new(checked((int)cell.X), checked((int)cell.Y));
    }

    /// <summary>
    /// ARC-DEV-246 (MIG13 S14l, GT208): the victim's catching-thief script (point 11) of a noticed NPC pick pocket, the NPC thief as the triggerer, through the shown map's use-script host; true when the consequences go on (run default, or no script), false when the script's answer stopped them or the viewer could not finish the run (Not ready, as the hero's).
    /// </summary>
    private bool AiCatchingThief(string victim, string thief)
    {
        if (victim == Arcanum.Simulation.Ai.AiSchedulerRules.Pc || _readyArea is not { } area || _areaSession is not { } session || UseScriptHost(area.Snapshot) is not { } host
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map || !map.TryGetRow(victim, out var row)
            || host.Slot(new(CurrentAreaMap, victim), AreaUseScriptHost.CatchingThiefPoint) is not { Script: > 0 }) return true;
        var ran = Awarded(host, host.RunCatchingThief(row, session.Rules.Scripts, new(CurrentAreaMap, thief)));
        GD.Print($"ARC-DEV-246 catching-thief victim={victim} thief={thief} outcome={ran.Outcome} run={ran.Run?.Outcome} detail={ran.Detail}");
        // As the hero's thief (AreaPickPocket.Caught): a script that ran and skipped the default stops the consequences, and so does one the viewer cannot finish (Not ready, nothing applied).
        return ran.Outcome == AreaScriptedUseOutcome.NoTeleport && ran.Run?.Outcome != Arcanum.Scripting.Scr.ScrOutcome.SkipDefault;
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
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map) return true;
        // ARC-DEV-371: a critter created at run time runs its prototype's heartbeat too (its row is the session's, the map holds none).
        if ((map.TryGetRow(npc, out var held) ? held : CreatedCrittersRow(npc)) is not { } row) return true;
        var scrPoint = point == AiScriptPoint.Heartbeat ? Arcanum.Scripting.Scr.ScrPoint.Heartbeat : Arcanum.Scripting.Scr.ScrPoint.FirstHeartbeat;
        if (host.Slot(new(CurrentAreaMap, npc), scrPoint) is not { Script: > 0 }) return true;
        var ran = host.RunHeartbeat(row, scrPoint, session.Rules.Scripts);
        if (ran.Outcome != AreaScriptedUseOutcome.NoTeleport)
        {
            if (_heartbeatRefusalsLogged.Add($"{npc}|{scrPoint}|{ran.Outcome}|{ran.Run?.Script}"))
                GD.Print($"ARC-WORLD-114 heartbeat-refused npc={npc} point={scrPoint} outcome={ran.Outcome} detail={ran.Detail}");
            return true;
        }
        // ARC-SCRIPT-047: a heartbeat that called the NPC's dialog point with the PC (1100's opening talk) asks for the conversation; it
        // opens once the event pump that ran this tick returned (OpenHeartbeatConversation).
        if (ran.Dialog is { } talk)
        {
            _heartbeatDialog ??= talk;
            GD.Print($"ARC-SCRIPT-047 heartbeat-talk npc={npc} point={scrPoint} script={ran.Run!.Script} dialog={talk.Script} line={talk.Line} node={talk.Node}");
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

    // ARC-SCRIPT-047: the conversation a heartbeat asked for, opened after the event pump that ran the tick (kept until it can open).
    private Arcanum.Scripting.Scr.ScrDialogStart? _heartbeatDialog;

    /// <summary>ARC-SCRIPT-047: opens the conversation a heartbeat script asked for (the NPC's talk with the PC, 1100) after the event
    /// pump that ran the tick. The run is applied already (the heartbeat removed itself), so a talk that cannot open yet (a conversation
    /// or a window is open, the hero is under attack) stays pending and opens once it can: a project choice, the original's behaviour in
    /// those states is not read (audit U5). One for another map than the shown one, or left when the AI host retires, is dropped.</summary>
    private void OpenHeartbeatConversation()
    {
        if (_heartbeatDialog is not { } talk) return;
        if (talk.Npc?.Map != CurrentAreaMap)
        {
            _heartbeatDialog = null;
            GD.Print($"ARC-SCRIPT-047 heartbeat-talk dropped: the NPC {talk.Npc?.Code} is not on the shown map");
            return;
        }
        if (ConversationOpen || WorldModal || !LeaveCombatModeForDialog()) return;
        _heartbeatDialog = null;
        BeginScriptedConversation(talk);
    }

    /// <summary>ARC-SCRIPT-047: an NPC's live cell for a script's vicinity: where the AI walked it, a companion's committed cell; null for
    /// an NPC that stands where its map data puts it.</summary>
    // ARC-DEV-328: a companion carried here is named by its home map and stands where the group holds it (the original keeps one object wherever it stands).
    private CellCoord? ScriptCritterCell(Arcanum.Scripting.Scr.ScrObjectRef obj)
    {
        if (obj.Map != CurrentAreaMap && _areaCompanions?.Companions.Any(c => c.Entity == obj.Code && c.Template.Home.RuntimeMap == obj.Map) != true) return null;
        if (obj.Map == CurrentAreaMap && _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) && ai.Movers.IsMoved(obj.Code) && ai.Movers.Of(obj.Code) is { } mover)
            return new(checked((int)mover.Cell.X), checked((int)mover.Cell.Y));
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Entity == obj.Code && c.Template.Home.RuntimeMap == obj.Map) is { } companion && _worldActor.Session is AreaGridSession grid)
        {
            var cell = grid.Chart.ToWorld(companion.Grid.State.CommittedCell);
            return new(checked((int)cell.X), checked((int)cell.Y));
        }
        return null;
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

    /// <summary>ARC-DEV-233 (S14b, GT162): a script's heal fatigue of an NPC of the shown map: the fatigue damage falls by <paramref name="amount"/> through the session's life step, which starts the
    /// get-up of a critter that comes round; false when it has no combat values or the session refuses.</summary>
    private bool HealFatigueByScript(Arcanum.Scripting.Scr.ScrObjectRef obj, int amount)
    {
        if (_areaSession is not { } session || _combatHost is null || obj.Map != CurrentAreaMap
            || _combatHost.Build(session, obj.Map, obj.Code).Combat is not { } combat) return false;
        var healed = session.Heal(session.Version, combat.Target, new LifeHeal { Fatigue = amount });
        GD.Print($"ARC-DEV-233 script-heal-fatigue npc={obj.Code} amount={amount} disposition={healed.Disposition} failure={healed.Failure} events={healed.Outcome?.Events}");
        return healed.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>ARC-DEV-243 (S14g, GT194–GT195): a script's damage (60) or unresistable damage (138) to the PC (the scheduler id) or an NPC of the shown map, through the cast host's damage step (no attacker; resistance
    /// unless unresistable) and the session's life step; an NPC it kills falls with the death's pose, as a script's kill does. The text says what happened.</summary>
    private string DamageByScript(string victim, int amount, int type, bool unresistable)
    {
        if (SpellCasterNow() is not { } caster || _areaSession is null) return "no cast host";
        var id = victim == Arcanum.Simulation.Ai.AiSchedulerRules.Pc ? SessionSpellCaster.Hero : victim;
        // ARC-DEV-255: a door, window, container, scenery or trap is named by its session entity; the cast map knows it by its row.
        if (id != SessionSpellCaster.Hero && _spellCastMap is { } castMap && ObjectTargets()?.KeyOfEntity(victim) is { } objectKey && castMap.IdOf(objectKey) is { } objectId) id = objectId;
        var hit = caster.ApplyScriptDamage(id, amount, type, unresistable);
        GD.Print($"ARC-DEV-243 script-damage victim={victim} amount={amount} type={type} unresistable={unresistable} applied={hit.Applied} hp={hit.HitPointDamage} events={hit.Outcome?.Events}");
        LogObjectHits();
        LogItemHits();
        if (hit.Outcome is { } outcome && outcome.Events.HasFlag(LifeEvents.Died) && id != SessionSpellCaster.Hero)
            _deathAnimations[(CurrentAreaMap, id)] = AreaFallenPoses.Animation(outcome.Death);
        // A hero knocked down or out stops its walk and attack, as an NPC's hit does (its fall is not drawn yet).
        if (id == SessionSpellCaster.Hero && _areaSession.QueryHeroLife() is { Status.IsActive: false }) { if (!HeroOnlyHeld(_areaSession)) StopWorldRoute("HERO_DOWN"); CancelAreaAttack("the hero is down"); }
        var aftermath = hit.Applied && hit.Outcome is not null ? ScriptDamageAftermath(_areaSession, id, hit) : "";
        SyncAreaCombatBodies();
        RefreshAreaStatus();
        return hit.Applied ? $"damaged {hit.HitPointDamage}{aftermath}" : hit.Vetoed ? "cancelled by the victim's taking-damage script" : "nothing applied";
    }

    /// <summary>
    /// ARC-DEV-245 (MIG13 S14i, GT195): what a script's damage does to a critter beyond its hit points: fire damage above 0 after resistance (<see cref="AttackDamage.DamagesArmor"/>) wears the torso armour by 10 (the session's armour wear
    /// with the critter's worn items; an NPC's broken item is no longer worn), and a follower of the PC that falls from 20 % or more to below it floats its near-death line toward its leader (<see cref="FollowerRules.NearDeathOfHit"/>:
    /// lines 5500–5599, shown as the log and the answer's text like the other loyalty lines, none is spoken yet). The text says what happened ("" for nothing).
    /// </summary>
    private string ScriptDamageAftermath(GameSessionOwner session, string id, SessionScriptHit hit)
    {
        var hero = id == SessionSpellCaster.Hero;
        var text = "";
        if (hit.Step is { DamagesArmor: true })
        {
            var worn = hero ? AreaCombatHost.HeroWorn(session, _areaKitDurability) : _combatHost?.WornItems(session, CurrentAreaMap, id);
            SessionLifeTarget? target = hero ? SessionLifeTarget.Hero : _combatHost?.Build(session, CurrentAreaMap, id).Combat?.Target;
            if (worn is not null && target is { } life)
            {
                var wear = session.ApplyArmorWear(session.Version, new SessionWearSide { Life = life, Worn = worn });
                var step = wear.Steps.FirstOrDefault();
                GD.Print($"ARC-DEV-245 script-armour-wear victim={id} disposition={wear.Disposition} failure={wear.Failure} item={step?.Entity} skip={step?.Skip} break={step?.Item?.Break}");
                if (step?.Item is { } done) text += done.Break is Arcanum.Simulation.Items.ItemBreakStep.Broken or Arcanum.Simulation.Items.ItemBreakStep.Deleted ? ", the armour breaks" : ", the armour wears";
            }
        }
        if (!hero && hit.Outcome is { } outcome && session.QueryCompanion(id).LedByPc
            && _combatHost?.Build(session, CurrentAreaMap, id).Combat is { } combat && session.QueryLife(combat.Target).View is { } view)
        {
            var after = view.Status.HitPoints;
            var near = FollowerRules.NearDeathOfHit(id, true, after + outcome.HitPointDamage, after, view.Status.MaxHitPoints, outcome.Events.HasFlag(LifeEvents.Died));
            if (near is { } comment)
            {
                GD.Print($"ARC-DEV-245 script-near-death follower={comment.Speaker} lines={comment.Lines.First}-{comment.Lines.Last}");
                text += $", near-death lines {comment.Lines.First}-{comment.Lines.Last}";
            }
        }
        return text;
    }

    // ARC-DEV-261: the id a script's object handle holds for a cast's object id: an NPC's is the same entity, a door, container, scenery or trap is known to the cast by its row and to the script by its session entity.
    private string ScriptCodeOfCast(string castId) =>
        castId == SessionSpellCaster.Hero || _spellCastMap?.Row(castId) is not { } row ? castId : ObjectTargets()?.EntityOfKey(row.Key) ?? castId;

    /// <summary>
    /// ARC-DEV-254 (MIG13 S14j, GT198–GT203): the victim's taking-damage script (point 0x17) asked by a damage the cast host applies: true when the damage goes on (no script, a script that ran default, or one the viewer cannot
    /// apply), false when the script skipped the default and cancelled it. The PC has no script here (no attachment data), so its damage always goes on.
    /// </summary>
    private bool TakingDamageGoesOn(string victim, string? attacker)
    {
        if (victim == SessionSpellCaster.Hero || _areaSession is not { } owner || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return true;
        var ran = Awarded(host, host.RunTakingDamage(ScriptCodeOfCast(victim), attacker is null ? null : new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, ScriptCodeOfCast(attacker)), owner.Rules.Scripts));
        var vetoed = AreaUseScriptHost.TakingDamageVetoes(ran);
        if (ran.Run is not null || ran.Detail.StartsWith("nested", StringComparison.Ordinal))
            GD.Print($"ARC-DEV-254 taking-damage victim={victim} attacker={attacker ?? "-"} outcome={ran.Outcome} run={ran.Run?.Outcome} stop={ran.Run?.Stop} vetoed={vetoed} detail={ran.Detail}");
        return !vetoed;
    }

    /// <summary>
    /// ARC-MAGIC-048 (E4-c, CN6): the Use component's call (0x452ED0 → 0x441980, point 1): the target's own use script, with the target as the triggerer. True when a script ran. The PC has no script here (no attachment data), an
    /// object that is not a row of the shown map has none; the target is also the extra object (the three object pairs of 0x452ED0); the run's awards, float lines and created objects are taken as for any other script run.
    /// </summary>
    private bool UseScriptOfCast(string target)
    {
        if (target == SessionSpellCaster.Hero || _areaSession is not { } owner || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return false;
        var code = ScriptCodeOfCast(target);
        var ran = Awarded(host, host.RunUseScript(code, new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, code), new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, code), owner.Rules.Scripts));
        // True when a script ran and its effects applied (a refused or rejected run is not one).
        var applied = ran.Run is not null && ran.Outcome == AreaScriptedUseOutcome.NoTeleport;
        if (ran.Run is not null) GD.Print($"ARC-MAGIC-048 use-component target={target} outcome={ran.Outcome} run={ran.Run.Outcome} stop={ran.Run.Stop} detail={ran.Detail}");
        return applied;
    }

    /// <summary>ARC-MAGIC-031: hands the shown session the ground under the hero (<see cref="GameSessionOwner.HeroGround"/>: the sector's aptitude adjustment and the tile facts the mage backgrounds read) from the shown
    /// area's snapshot, the hero's committed cell and the module's tile-name table; called wherever the viewer takes a session, with <see cref="AttachAreaDyingScript"/>.</summary>
    private void AttachAreaHeroGround()
    {
        if (_areaSession is not { } session) return;
        // The hero's cell is chart-relative and the snapshot's are world cells, so the chart goes with it. The shown area counts only while it is the session's map (the session is attached a moment before
        // the area is shown on a map change).
        var ground = new AreaHeroGround(() => _readyArea is { } area && area.Snapshot.RuntimeMap == session.QueryActor().Map.RuntimeMap ? area.Snapshot : null,
            () => _worldActor.Session is AreaGridSession grid ? (grid.Chart, grid.State.CommittedCell) : null, () => CueContent().Tiles);
        session.HeroGround = ground.Read;
    }

    /// <summary>ARC-DEV-312: hands the shown session the dying-script question (<see cref="GameSessionOwner.DyingScript"/>); called wherever the viewer takes a session (created, carried over a map change, loaded).</summary>
    private void AttachAreaDyingScript()
    {
        if (_areaSession is not { } session) return;
        session.DyingScript = DyingGoesOn;
        // ARC-COMBAT-1141 (GT210): the script kill and the poison tick ask the NPC's taking-damage script too (no attacker); a victim that is not on the shown map has none here.
        session.TakingDamageScript = request => request.Map != CurrentAreaMap || TakingDamageGoesOn(request.Victim, request.Attacker);
        // ARC-WORLD-1301 (VPG6): a container with flag 0x200 that breaks before it was opened generates its goods through the same restock host as the loot window's first open (not ready until the barter data has loaded: it then spills the stored items).
        session.GenerateOnceWorld = () => AreaEvents;
    }

    /// <summary>
    /// ARC-DEV-312 (DY1–DY3, MIG13 slice 4): the dying script (point 12) of an NPC whose life step has just killed it, asked by the session before the death processing, with the killer as the triggerer, through the shown map's
    /// use-script host: true when the death goes on (no script, a script that ran default, or one the viewer cannot apply), false when the script skipped the default and stopped the whole processing. A death on another map
    /// than the shown one has no script here. The PC has no script (the session asks for NPCs only).
    /// </summary>
    private bool DyingGoesOn(SessionDyingRequest request)
    {
        if (_areaSession is not { } owner || request.Map != CurrentAreaMap || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return true;
        Arcanum.Scripting.Scr.ScrObjectRef? killer = request.Killer is null ? null : new(CurrentAreaMap, ScriptCodeOfCast(request.Killer));
        var ran = Awarded(host, host.RunDying(request.Victim, killer, owner.Rules.Scripts));
        // GT34 step 3: a victim the script destroyed returns before any death processing, as skip default does.
        var stops = AreaUseScriptHost.DyingStops(ran) || AreaUseScriptHost.Destroyed(owner, CurrentAreaMap, request.Victim);
        if (ran.Run is not null)
            GD.Print($"ARC-DEV-312 dying victim={request.Victim} killer={request.Killer ?? "-"} outcome={ran.Outcome} run={ran.Run.Outcome} stop={ran.Run.Stop} stops-death={stops} detail={ran.Detail}");
        return !stops;
    }

    /// <summary>ARC-DEV-243 (S14g, GT196): a script's heal of hit points (75) of the PC or an NPC of the shown map by the session's heal (no resurrection: a dead critter does nothing); an amount of 0 or less does nothing.</summary>
    private string HealHitPointsByScript(string victim, int amount)
    {
        if (_areaSession is not { } session) return "no session";
        if (amount <= 0) return "nothing to heal";
        SessionLifeTarget? target = victim == Arcanum.Simulation.Ai.AiSchedulerRules.Pc ? SessionLifeTarget.Hero
            : _combatHost?.Build(session, CurrentAreaMap, victim).Combat?.Target;
        if (target is not { } who) return "no combat values";
        var healed = session.Heal(session.Version, who, new LifeHeal { HitPoints = amount });
        GD.Print($"ARC-DEV-243 script-heal-hp victim={victim} amount={amount} disposition={healed.Disposition} failure={healed.Failure} events={healed.Outcome?.Events}");
        RefreshAreaStatus();
        return healed.Disposition == GameSessionDisposition.Rejected ? $"not healed: {healed.Failure}"
            : healed.Outcome is { } done && done.Events.HasFlag(LifeEvents.AlreadyDead) ? "nothing healed: dead" : "healed";
    }

    /// <summary>ARC-WORLD-102: drops the AI host; one of the current session first ends its runs (<see cref="AreaAiHost.Retire"/>), so a
    /// casting goal's magic run does not outlive it.</summary>
    private void RetireAiHost()
    {
        if (_aiHost is { } host && ReferenceEquals(host.Session, _areaSession)) { host.Retire(); _areaSession!.KillNotice = null; }
        _aiHost = null; _aiHostArea = null;
        _heartbeatDialog = null;
        _npcTilePending.Clear();
    }

    // ARC-DEV-118 (MIG9 S5b, K1 §3.5): the chart over the loaded sectors the host of the main map's window reads; created with the map's sector cache and kept
    // across window moves, dropped with the cache. The window's own evidence is the free source of its sectors; any other sector is read on a task.
    private void StartSectorChart(GameSessionOwner session, int mapNumber, AreaLoadResult area, AreaGridSession grid)
    {
        if (session.QuerySectors(new SessionMapId(mapNumber)) is null) { DropSectorChart(); return; }
        if (_sectorChart is null || _sectorChartKey != (session, mapNumber))
        {
            DropSectorChart();
            var root = _arguments?.GameRoot; var name = _readyMapName; var data = session.Rules.Data;
            _sectorChart = new AreaSectorChart(() => root is null || name is null ? null : ContentSectorTerrain.Open(root, name, out _, data));
            _sectorChartKey = (session, mapNumber);
        }
        _sectorChart.UseWindow(area.Snapshot, grid.Snapshot);
        RequestSectorChart();
    }

    private void RequestSectorChart()
    {
        if (_sectorChart is { } chart && _sectorChartKey == (_areaSession, CurrentAreaMap) && _areaSession?.QuerySectors(new SessionMapId(CurrentAreaMap)) is { } state) chart.Request(state.Slots);
    }

    private void DropSectorChart() { _sectorChart?.Dispose(); _sectorChart = null; _sectorChartKey = null; }

    // ARC-DEV-104 (MIG9 S4, RL11): each frame the hero's view square touches its sectors in the session's cache (an age bump, or a load and a
    // fold); a map without a cache touches nothing. ARC-DEV-118: the main map has one since S5b, and its chart follows the cache.
    private void TouchAreaView()
    {
        if (_areaSession is not { } session || _worldActor.Session is not AreaGridSession hero) return;
        var map = new SessionMapId(CurrentAreaMap);
        if (session.QuerySectors(map) is null) { DropSectorChart(); return; }
        var cell = hero.Chart.ToWorld(hero.State.CommittedCell);
        var touch = session.TouchSectors(session.Version, map, AreaResidency.ViewCells(new(checked((int)cell.X), checked((int)cell.Y))));
        foreach (var fold in touch.Folds)
            GD.Print($"ARC-DEV-104 fold map={CurrentAreaMap} sector={fold.Sector.X},{fold.Sector.Y} marked={fold.Marked} npcs={fold.Npcs.Count}"
                + $" refused={fold.Refused.Count} evicted={(fold.Evicted is { } e ? $"{e.X},{e.Y}" : "-")}");
        RequestSectorChart();
        if (_sectorChart is { } chart) while (chart.Log.TryDequeue(out var line)) GD.Print(line);
    }

    // The shown map's line of fire with the moved NPCs on their cells, rebuilt once per frame. ARC-DEV-319: and per session: a quick load inside one process frame (a smoke's) must not hand the loaded session's AI the
    // map whose open-door test reads the replaced (disposed) session.
    private AreaLineOfFire? AiLineOfFire(AreaAiHost? host)
    {
        var frame = Engine.GetProcessFrames();
        if (_aiLineOfFire is { } cached && cached.Frame == frame && ReferenceEquals(cached.Owner, _areaSession)) return cached.Map;
        var map = AreaLineOfFireNow();
        _aiLineOfFire = (frame, _areaSession, map);
        return map;
    }

    // ARC-DEV-124 (MIG12 S5): the AI host steps its goal runs inside the frame step, as one of its participants (AreaAiFrameParticipant, registered with every
    // ambient driver, StartAreaAmbient): on the frame's world milliseconds, not while paused; the shown map's sectors are touched first.
    private AreaAiFrameParticipant? _aiFrame;

    // The host the ping (before the frame opens) built or kept; no pump while a transfer is in flight (its frames are the transfer's: ProcessAreaAi does not
    // run in them, so a notice a pump made would wait for the next one, which a retired host never gives).
    private AreaAiHost? AiFrameHost() =>
        AreaTransferPending || _aiHost is not { } host || !ReferenceEquals(host.Session, _areaSession) ? null : host;

    private AreaAiFrameParticipant AiFrameParticipant => _aiFrame ??= new(AiFrameHost);

    // ARC-DEV-134 (MIG13 S4): the frame step's goal-step host, over the same host as the participant.
    private AreaAiGoalSteps? _aiGoalSteps;
    private AreaAiGoalSteps AiGoalSteps => _aiGoalSteps ??= new(AiFrameHost);

    /// <summary>Per frame, after the hero's attack: what the goal runs did in this frame's step (the frame step pumped them, <see cref="AreaAiFrameParticipant"/>)
    /// reaches the status line, the bodies and the drawing.</summary>
    private void ProcessAreaAi()
    {
        if (AreaAiHostNow() is not { } host) return;
        // The view's sectors are touched in every frame the world runs, whether or not the frame step did (a smoke's frames call it only when they say so).
        TouchAreaView();
        // ARC-DEV-371: the critters a heartbeat or another run created since the last frame are notified (a run that went through Awarded was noted at once).
        if (_useScriptHost is { } scripted) NoteScriptCreations(scripted.Host.TakeCreations());
        // ARC-DEV-371: the encounter creatures a tick destroyed are destroyed in the session here, from the frame, never from inside a tick.
        if (_areaSession is { } scriptedSession) { DrainEncounterDestroys(scriptedSession, host); TakeCreatedInventories(scriptedSession); }
        var steps = AiFrameParticipant.TakeSteps();
        // ARC-WORLD-095: in a round the runs' charges and subturn ends reach the round log and the status line's action points.
        var roundEvents = host.DrainRoundEvents();
        LogRound(roundEvents);
        if (roundEvents.Count > 0 || (steps > 0 && InTurnBasedRound)) RefreshAreaStatus();
        var redraw = steps > 0;
        // ARC-DEV-328, ARC-DEV-351: the objects the open of this map notified (the followers that arrived with the actor and a former follower sent home while the map was closed) run the
        // script of the tile they were written on, in entity order; one that is gone or dead by the time it is read runs nothing (RunNpcTilePending).
        if (_arrivalTilePending.Count > 0 && _mapTransfers is not null)
        {
            foreach (var (entity, homeMap, cell) in _arrivalTilePending) _npcTilePending.Enqueue((entity, homeMap, cell));
            _arrivalTilePending.Clear();
        }
        // ARC-DEV-206: tile scripts that waited for an open conversation.
        if (_npcTilePending.Count > 0 && RunNpcTilePending()) redraw = true;
        foreach (var notice in host.DrainNotices())
        {
            switch (notice.Kind)
            {
                case AreaAiNoticeKind.Swing:
                    if (notice.AttackBark) PlayNpcAttackBark(host, notice.Npc);
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
                    LaunchNpcSpellMissile(notice, WorldMilliseconds()); // the missile and its eye candy run on the world clock (ARC-DEV-125), not the AI host's
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
                case AreaAiNoticeKind.Entered:
                    redraw |= AiEntered(notice);
                    break;
                case AreaAiNoticeKind.Fidget:
                    // ARC-WORLD-099: the fidget after a done run (0x4B4320); NPC ART is not drawn, so only the log shows it.
                    // ARC-DEV-272: the hero's fidget (after every done run of its in combat mode) is counted apart and not logged, a held stick ends a walk about four times a second.
                    if (notice.Npc == Arcanum.Simulation.Ai.AiSchedulerRules.Pc) { _heroFidgets++; break; }
                    _aiFidgets++;
                    PlayNpcFidgetBark(host, notice.Npc);
                    GD.Print($"ARC-WORLD-099 ai-fidget npc={notice.Npc}");
                    break;
                case AreaAiNoticeKind.Sound:
                    // ARC-RENDER-1126: the combat approach goal's alerted bark (0x4296D0), the id the setter stored (AreaAiScene.AlertedSound).
                    PlayNpcSoundId(host, notice.Npc, notice.Sound, "alerted bark");
                    break;
            }
        }
        // ARC-WORLD-111: a stepping NPC's cycle advances every frame, and its stand pose is drawn once its step ends.
        if (AiMoversAnimating(host, host.NowMs) | redraw) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    // ARC-DEV-206 (B25, GR12): the shown map's tile script of the cell an AI-host mover was placed on runs with the NPC as the triggerer (the original runs a tile's scripts for every PC or NPC
    // that moved, ledger TR3, RL5, RL6), as a following companion's does (RunCompanionTileScripts). Only for an NPC of the shown window's rows (a refused run, as for a row the window no longer holds,
    // is not logged). A run that asked for a conversation opens it with the dialog's NPC; while one is open the notices wait (in order) and run when it closes. An NPC gone or dead by the time its
    // notice is read runs nothing. True when a run changed the world (a line printed, a dialog, an outcome other than "no teleport" that was not refused), so the bodies are drawn again.
    private readonly Queue<(string Npc, int? HomeMap, Arcanum.Data.Traversal.AreaStepCell Cell)> _npcTilePending = new();
    // The tile scripts AI-host movers ran, for the npc-tile smoke (npc, cell, outcome, detail).
    private readonly List<(string Npc, Arcanum.Data.Traversal.AreaStepCell Cell, AreaScriptedUseOutcome Outcome, string? Detail)> _npcTileRuns = [];
    private static readonly bool NpcTileLogAll = System.Environment.GetEnvironmentVariable("ARC_DEV_206_LOG_ALL") == "1";

    /// <summary>ARC-DEV-328: the home map of a companion carried here (null for an NPC of the shown map, or one the group does not hold).</summary>
    private int? CompanionHomeMap(string entity) =>
        _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is { } companion && companion.Template.Home.RuntimeMap != CurrentAreaMap ? companion.Template.Home.RuntimeMap : null;

    private bool AiEntered(AreaAiNotice notice)
    {
        if (notice.Cell is { } cell && _mapTransfers is not null)
        {
            if (_npcTilePending.Count >= 256) _npcTilePending.Dequeue();
            _npcTilePending.Enqueue((notice.Npc, CompanionHomeMap(notice.Npc), new(cell.X, cell.Y)));
        }
        return RunNpcTilePending();
    }

    private bool RunNpcTilePending()
    {
        var changed = false;
        // A tile script that creates a critter onto a tile with the same script would queue another notice for each run: at most 64 runs a frame, the rest wait for the next one.
        var budget = 64;
        while (_npcTilePending.Count > 0 && !ConversationOpen && budget-- > 0)
        {
            var (npc, homeMap, at) = _npcTilePending.Dequeue();
            // A companion carried here is the group's (its record may be unresolved for the host, which then reads as dead): its death is the session's.
            var carried = homeMap is not null;
            if (_mapTransfers is not { } transfers || AreaAiHostNow() is not { } host || (carried ? _areaSession?.QueryNpcDead(npc) == true : host.Gone(npc) || host.DeadCritter(npc))) continue;
            if (transfers.EnterByCritter(at, npc, homeMap) is not { } ran) continue;
            _npcTileRuns.Add((npc, at, ran.Outcome, ran.Detail));
            if (_npcTileRuns.Count > 64) _npcTileRuns.RemoveAt(0);
            var refused = ran.Outcome is AreaScriptedUseOutcome.Refused or AreaScriptedUseOutcome.ContentUnavailable;
            var did = !refused && (ran.Outcome != AreaScriptedUseOutcome.NoTeleport || ran.Dialog is not null || ran.Lines.Count > 0);
            if (did || NpcTileLogAll)
                GD.Print("ARC-DEV-206 npc-tile " + System.Text.Json.JsonSerializer.Serialize(new { Npc = npc, Cell = new { at.X, at.Y },
                    Outcome = ran.Outcome.ToString(), ran.Detail, Dialog = ran.Dialog?.Script, Node = ran.Dialog?.Node,
                    Lines = ran.Run is { } run ? string.Join(' ', run.Trace.Select(t => $"{t.Line}:c{t.Condition}{(t.Held ? '+' : '-')}a{t.Action}")) : null }));
            if (ran.Dialog is { } dialog) BeginScriptedConversation(dialog);
            changed |= did;
        }
        return changed;
    }

    // An NPC's hit: the status line, a stun's end, a killed NPC's fall, the bodies and the inventory.
    private void AiHit(AreaAiNotice notice)
    {
        var owner = _areaSession!;
        var result = notice.Result!;
        var hero = notice.Target == AiSchedulerRules.Pc;
        var life = hero ? owner.QueryHeroLife() : _combatHost?.Build(owner, notice.Target!).Combat is { } combat ? owner.QueryLife(combat.Target).View : null;
        _aiNotice = $"{AiName(notice.Npc)} attacks {(hero ? "the hero" : AiName(notice.Target!))}: "
            + AreaCombatHost.Describe(result, life?.Status.HitPoints, life?.Status.MaxHitPoints);
        FloatCritterAttack(result, notice.Npc, hero ? HeroFloater : notice.Target!);
        GD.Print($"ARC-WORLD-092 ai-hit npc={notice.Npc} target={notice.Target} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} recipient={result.Recipient} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} step={result.StepFailure}");
        // ARC-DEV-137: a stunned NPC's stun is its goal 57, which the AI host steps; only the hero's stun still ends at once (S13).
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned)
            && (result.Recipient == DamageRecipient.Target ? notice.Target! : notice.Npc) == AiSchedulerRules.Pc)
            HeroStunned(owner);
        // A hero knocked down or out stops its walk and attack (its fall is not drawn yet).
        if (hero && owner.QueryHeroLife() is { Status.IsActive: false }) { if (!HeroOnlyHeld(owner)) StopWorldRoute("HERO_DOWN"); CancelAreaAttack("the hero is down"); }
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
        // ARC-DEV-302: a hero only held (paralysed, stoned) keeps its walk run (it waits at the step start and goes on when the hold ends, GT148); its attack ends.
        if (_areaSession?.QueryHeroLife() is { Status.IsActive: false }) { if (!HeroOnlyHeld(_areaSession)) StopWorldRoute("HERO_DOWN"); CancelAreaAttack("the hero is down"); }
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
        // The movers' steps and attack plays run on the host's clock (the frame step's world milliseconds).
        var now = host.NowMs;
        return AnimateAiMovers(host, host.Movers.Commands(key => _fallenBodies.TryGetValue(key, out var fallen) ? fallen : NpcT0Body(key), now), now);
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
