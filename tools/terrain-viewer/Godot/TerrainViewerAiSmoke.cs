using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;
using Arcanum.Simulation.Goals;
using Arcanum.World;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-DEV-129: puts the hero's life back as an AI stage found it. A stage's fights can kill the hero (the follower stage's freed
    /// follower and the NPC do, ARC-WORLD-119), and a plain heal does nothing for a dead one, so the hero stayed dead for the stages after
    /// it (the dialog of the follow smoke refuses a dead hero's window): a dead hero is resurrected first.</summary>
    private static void RestoreHeroForSmoke(GameSessionOwner session, LifeState before)
    {
        if (session.QueryHeroLife()!.Status.IsDead)
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
        var now = session.QueryHeroLife()!.Record.State;
        if (now.HitPointDamage > before.HitPointDamage || now.FatigueDamage > before.FatigueDamage)
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { HitPoints = now.HitPointDamage - before.HitPointDamage, Fatigue = now.FatigueDamage - before.FatigueDamage });
    }

    // Native observation (ARC-WORLD-092, ARC_AREA_MAP_SMOKE_ONLY=ai, map mode with --hero, the Crash Site cave): the AI turns on, the
    // hero stands a few cells from the living monstrous NPC with the fewest hit points, in combat mode, and that NPC is told of the
    // hero's attack (the attack event a hit runs). Its AI tick's pass focuses the hero, the run walks it up tile by tile, it swings
    // and the session resolves its bite on the hero; its body is drawn on its new cell and the status line shows the AI.
    // ARC-DEV-094 (MIG7c): where a walked NPC stands is session state, so the next AI stage of one run would find it where the last left it; each stage
    // picks its NPC by its map data's cell and stands the hero from there, so it starts from the map data's places.
    private static void ForgetNpcPlacesForSmoke(GameSessionOwner session, int map)
    {
        foreach (var place in session.QueryNpcPlaces(new SessionMapId(map)))
            session.ClearMapChange(session.Version, new SessionMapId(map), SessionNpcPlacement.Key(place.Entity));
    }

    // ARC-DEV-118: an earlier stage of one run leaves its victim in a fight with the hero (its danger state is session state), and the main map's
    // residency keeps that NPC awake, so the next stage's NPCs start calm: every NPC in danger is put back to NONE.
    private static void CalmNpcsForSmoke(GameSessionOwner session, IEnumerable<string> npcs)
    {
        foreach (var npc in npcs)
            if (session.QueryDanger(npc).State != DangerState.None)
                session.SetDangerState(session.Version, new SessionDangerRequest { Entity = npc, State = DangerState.None });
    }

    // ARC-DEV-141 (MIG13 S8c): the AI smoke's NPC pushed away from the hero through the knockback host (what an AGoal component 43 asks): the goal run slides it up to four tiles on the animation clock, a block or the
    // line's end ends it, no run is left. Skipped when the map has no AI host or the NPC cannot be pushed.
    // ARC-DEV-285: the sides a push can come from (the tile next to the pushed object, the line's tiles are the pixel line's, so the free side is found by trying): the first that slides the object its four tiles.
    private string PushFromFreeSideForSmoke(GameSessionOwner session, string npc, Action tick, SessionLifeTarget life)
    {
        if (_aiHost is not { } ai || ai.Tile(npc) is not { } at) return "skipped";
        var last = "refused";
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1) })
        {
            if (ai.Tile(npc) is not { } now) break;
            last = RunAreaKnockbackSmoke(session, npc, tick, new CellCoord(now.X + dx, now.Y + dy));
            session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (last.StartsWith("Exhausted", StringComparison.Ordinal)) return last;
        }
        return last;
    }

    private string RunAreaKnockbackSmoke(GameSessionOwner session, string npc, Action tick, CellCoord? source = null)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-DEV-141 knockback smoke: " + why); }
        if (_aiHost is not { } ai || !ai.CanBePushed(npc) || HeroWorldCell() is not { } hero || ai.Tile(npc) is not { } before)
        {
            GD.Print("ARC-DEV-141 native-smoke knockback SKIP (no AI host or the NPC cannot be pushed here)");
            return "skipped";
        }
        var host = KnockbackHost();
        var endsSeen = _knockbackEndCountForSelfTest;
        var started = host.Start(npc, source ?? hero);
        if (!started)
        {
            GD.Print($"ARC-DEV-141 native-smoke knockback SKIP (the start was refused: npc at {before.X},{before.Y}, hero at {hero.X},{hero.Y})");
            return "refused";
        }
        Require(session.QueryGoals().RunsOf(npc).Any(r => r.Bottom.Goal == GoalType.Knockback), "the push is a goal run of the NPC");
        var frames = 0;
        for (; frames < 600 && host.Count > 0; frames++)
        {
            _areaSmokeTime++;
            tick();
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
        }
        Require(host.Count == 0, $"the push ended within {frames} frames");
        Require(!session.QueryGoals().RunsOf(npc).Any(r => r.Bottom.Goal == GoalType.Knockback), "no knockback run is left");
        Require(_knockbackEndCountForSelfTest == endsSeen + 1, "the push was reported once");
        var end = _knockbackEndsForSelfTest[^1];
        // ARC-DEV-294: the push leaves the NPC on its last tile (the end's tile); a hit NPC then reacts (ARC-DEV-264: an Attack goal toward the hero) and may take its first step in the very frame the push ended, so the AI host's
        // tile afterwards is that tile or its neighbour, never further.
        var rest = end.Tile;
        var after = ai.Tile(npc)!.Value;
        Require(end.Target == npc && end.Outcome is KnockbackOutcome.Exhausted or KnockbackOutcome.Blocked, $"the push ran its line or met a block: {end.Outcome}");
        Require(rest == (end.Path.Count > 0 ? end.Path[^1] : before) && AiExecutorRules.Distance(before, rest) <= KnockbackRules.Distance, $"the NPC was left on the last tile it reached: {before.X},{before.Y} -> {rest.X},{rest.Y} (outcome {end.Outcome}, tiles {end.Path.Count}, source {(source ?? hero).X},{(source ?? hero).Y})");
        Require(AiExecutorRules.Distance(rest, after) <= 1, $"the NPC moved on at most one tile on its own goal: {rest.X},{rest.Y} -> {after.X},{after.Y}");
        Require(end.Outcome != KnockbackOutcome.Exhausted || AiExecutorRules.Distance(before, rest) == KnockbackRules.Distance, "a free push goes four tiles");
        GD.Print($"ARC-DEV-141 native-smoke knockback PASS npc={npc} {before.X},{before.Y} -> {rest.X},{rest.Y} outcome={end.Outcome} tiles={end.Path.Count} frames={frames}");
        return $"{end.Outcome} {before.X},{before.Y}->{rest.X},{rest.Y}";
    }

    // ARC-DEV-287 (MIG13 tail T6): the hero pushed through the knockback host from the first free side. The grid actor, the session's place and the AI host's tile agree on the last tile; the view stayed where it was (the
    // original's push calls no view-centring function) until the hero's next step; no tile of the push was left unqueued for its jump point and script; the walk is free again after it.
    private string RunAreaHeroKnockbackSmoke(GameSessionOwner session, Action tick)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-DEV-287 hero knockback smoke: " + why); }
        if (_aiHost is not { } ai || !ai.CanBePushed(AiSchedulerRules.Pc) || _worldActor.Session is not AreaGridSession grid || _camera is null)
        {
            GD.Print("ARC-DEV-287 native-smoke hero-knockback SKIP (no AI host, hero body or camera)");
            return "skipped";
        }
        // The view as the follow left it, settled by a few quiet frames.
        for (var i = 0; i < 3; i++) { _areaSmokeTime++; tick(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
        var view = _camera.Camera.Position;
        var pushed = PushFromFreeSideForSmoke(session, AiSchedulerRules.Pc, tick, SessionLifeTarget.Hero);
        if (!pushed.StartsWith("Exhausted", StringComparison.Ordinal))
        {
            GD.Print("ARC-DEV-287 native-smoke hero-knockback SKIP (no free side: " + pushed + ")");
            return pushed;
        }
        var end = _knockbackEndsForSelfTest[^1];
        Require(end.Target == AiSchedulerRules.Pc && end.Path.Count == KnockbackRules.Distance, $"the hero's push went four tiles: {end.Path.Count}");
        var committed = HeroWorldCell();
        var actor = session.QueryActor();
        Require(committed == end.Path[^1] && new CellCoord(actor.Cell.X, actor.Cell.Y) == end.Path[^1] && ai.Tile(AiSchedulerRules.Pc) == end.Path[^1], $"the grid, the session and the AI host agree on the last tile: {committed}, {actor.Cell.X},{actor.Cell.Y}");
        Require(grid.State.Phase == MovementPhase.Settled && grid.State.Segment is null, "the hero stands settled");
        Require(_camera.Held && _camera.Camera.Position == view, $"the view did not follow the pushed hero: held={_camera.Held} {view} -> {_camera.Camera.Position}");
        // The hero's next step lets the view follow again.
        var released = false;
        foreach (var direction in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1), new Vec2(1, 1), new Vec2(-1, -1), new Vec2(1, -1), new Vec2(-1, 1) })
        {
            for (var frame = 0; frame < 40 && _camera.Held; frame++) { _areaSmokeTime++; tick(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(direction) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            if (!_camera.Held) { released = true; break; }
        }
        for (var frame = 0; frame < 40; frame++) { _areaSmokeTime++; tick(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
        Require(released, "a step of the hero let the view follow again");
        Require(_mapEntered.Count == 0, "every tile the push entered was handled by the map-transfer step");
        GD.Print($"ARC-DEV-287 native-smoke hero-knockback PASS {pushed} tiles={end.Path.Count} view-held-until-step={released}");
        return pushed;
    }

    private async Task RunAreaAiSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-092 ai smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-092 native-smoke ai NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // ARC-DEV-089: the first animal (weakest first) the hero can stand 3..4 cells from, so it walks up before it bites, and see from there.
            var target = candidates[0]; CellCoord? start = null; var targetCell = default(CellCoord);
            foreach (var candidate in candidates.Take(24))
            {
                targetCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
                var body = commands[candidate.Row.Key];
                start = await AreaSmokeFightableStartAsync(map, body, targetCell, 4, 3, false) ?? await AreaSmokeFightableStartAsync(map, body, targetCell, 3, 3, false);
                if (start is not null) { target = candidate; break; }
            }
            if (start is null) { GD.Print($"ARC-WORLD-092 native-smoke ai NO-TARGET map={map.RuntimeMap} (no animal the hero can stand near and see)"); return; }
            var entity = target.Holder.Entity;
            AreaSmokePlace(map, start.Value);
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(session.QueryAiTick(entity) is not null, "the NPC has its AI tick");
            var heroLife = session.QueryHeroLife()!.Record.State.HitPointDamage;
            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero,
                $"the NPC fights the hero: {danger.State} {host.TraceOf(entity)?.Reaction} {host.TraceOf(entity)?.Setter}");

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var frames = 0; var drawnMoved = false; var swing = false;
            // ARC-WORLD-111: its steps are drawn with its own walk or run cycle.
            var stepFrames = new HashSet<AreaFrameKey>(); var steppingFrames = 0;
            // ARC-RENDER-038: its attack is drawn with its body's attack ART, and in combat mode it stands with what it shows.
            _npcAttackFrames.Clear(); var attackFrames = 0; var armedStands = _npcArmedStandDraws;
            // Automated runs drive the clock themselves: a frame is 50 host milliseconds, as the world frame (the game clock runs 8x).
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            for (; frames < 4000 && host.TraceOf(entity)?.Hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                drawnMoved |= host.Movers.Of(entity) is { } mover && AreaMovingCommands()?.Any(c => c.Identity == target.Row.Key && c.MapX == mover.Cell.X && c.MapY == mover.Cell.Y) == true;
                if (host.Movers.Of(entity)?.Stepping(host.NowMs) == true && AreaMovingCommands()?.FirstOrDefault(c => c.Identity == target.Row.Key) is { } stepping)
                {
                    steppingFrames++;
                    if (stepping.Frame.Policy == AreaBodyMoveArt.Policy) stepFrames.Add(stepping.Frame);
                }
                swing |= host.RunOf(entity)?.Top.Goal == GoalType.AttemptAttack;
            }
            var trace = host.TraceOf(entity);
            GD.Print($"ARC-WORLD-092 ai smoke trace danger={trace?.Danger} perform={trace?.Perform} step={trace?.Step} hit={trace?.Hit}");
            Require(trace?.Hit is { } hit && !hit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal), $"the NPC's bite was resolved after {frames} frames: {trace?.Hit}");
            Require(swing, "the attempt-attack goal ran");
            var at = host.Tile(entity)!.Value;
            var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            Require(AiExecutorRules.Distance(at, new(checked((int)hero.X), checked((int)hero.Y))) == 1, "it bit from the cell next to the hero");
            Require(drawnMoved, "its body was drawn on the cells it walked");
            // ARC-DEV-094 (MIG7c): where it walked is session state (its entry's place), and a host built again stands it there: what a load, a map change
            // or a window move gives back.
            var walked = host.Movers.Of(entity) ?? throw new InvalidOperationException("ARC-DEV-094 smoke: the walked NPC has no mover");
            Require(session.QueryNpcPlace(entity) is { } recorded && recorded.World == walked.Cell && recorded.Facing == walked.Facing && recorded.Map.RuntimeMap == host.Scene.RuntimeMap,
                $"the session holds where it stands: {session.QueryNpcPlace(entity)} against {walked.Cell} {walked.Facing}");
            var rebuilt = new AreaAiHost(session, host.Scene, AreaEvents, AreaCombatWorldNow());
            Require(rebuilt.Movers.Of(entity) is { From: null } back && back.Cell == walked.Cell && back.Facing == walked.Facing,
                $"a host built again stands it on that cell: {rebuilt.Movers.Of(entity)?.Cell} against {walked.Cell}");
            GD.Print($"ARC-DEV-094 ai smoke place entity={entity} cell={walked.Cell.X},{walked.Cell.Y} facing={walked.Facing} restored=true");
            Require(stepFrames.Count >= 3, $"its steps were drawn with its move cycle: {stepFrames.Count} distinct frames over {steppingFrames} stepping frames; "
                + string.Join(", ", _bodyMoves.Values.Select(m => $"{m.Status} {m.Detail}")));
            Require(session.QueryCombatMode(SessionCombatant.Critter(entity)), "the attempt put it into combat mode (goal 19 node 5)");
            // ARC-WORLD-099: its attack run ends done, and in combat mode outside a round it starts its fidget (0x4B4320).
            var fidgets = _aiFidgets;
            for (var f = 0; f < 400 && _aiFidgets == fidgets; f++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(_aiFidgets > fidgets, "its done attack run was followed by its fidget");
            Require(_npcAttackFrames.Count - attackFrames >= 3, $"its attack was drawn with its attack ART: {_npcAttackFrames.Count - attackFrames} distinct frames; "
                + string.Join(", ", _armedArts.Values.Select(l => $"{l.Status} {l.Detail}")));
            var shown = host.ShownWeapon(entity);
            Require(shown.Weapon >= 1, $"in combat mode it shows its weapon or bare hands (WA1): {shown}");
            // ARC-WORLD-109: a skill run (pickpocket on itself, which the session refuses: ARC-DEV-246) walks goal 33 into 86 and reaches the viewer.
            var skills = _aiSkills;
            Require(session.QueryLife(target.Build.Combat!.Target).View is { Status.IsActive: true }, "the animal can still act after its bite");
            host.PerformSkill(entity, entity, AiExecutorRules.PickpocketSkill, null);
            Require(host.RunOf(entity)?.Bottom.Goal == GoalType.UseSkill, "the skill starter begins goal 33: " + host.TraceOf(entity)?.Perform);
            for (var f = 0; f < 200 && _aiSkills == skills; f++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(_aiSkills > skills, "the skill attempt reached the viewer: " + host.TraceOf(entity)?.Skill);
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains(" | AI: ", StringComparison.Ordinal), "the status line shows the AI");

            // The hero hits back where the NPC stands now (unless the NPC knocked itself out with a critical miss).
            var struckBack = false;
            if (session.QueryLife(target.Build.Combat!.Target).View is { Status.IsActive: true })
            {
                var moved = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == target.Row.Key);
                Require(moved is not null, "the walked NPC's body is among the moved bodies");
                var point = await HoverPointAsync(this, moved!) ?? throw new InvalidOperationException("ARC-WORLD-092 smoke: walked NPC not visible");
                HoverAtForSelfTest(point);
                var hover = HoverActionForSelfTest;
                Require(hover == AreaHoverAction.Attack, "the hero attacks the walked NPC from where it stands: hover " + hover);
                var before = _lastCritterAttackForSelfTest;
                // The click at the frames' clock (AreaSmokeClick reads whole seconds): the attack starts at once, next to the NPC.
                _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
                Require(_attack is not null, "the click started the hero's attack: " + _attackNotice);
                for (var f = 0; f < 400 && ReferenceEquals(_lastCritterAttackForSelfTest, before); f++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                struckBack = !ReferenceEquals(_lastCritterAttackForSelfTest, before);
                Require(struckBack, "the hero's attack struck the walked NPC: " + _attackNotice);
                CancelAreaAttack(null);
                HoverReleaseForSelfTest();
            }
            GD.Print($"ARC-DEV-117 routes map={map.RuntimeMap} " + string.Join(" ", host.RouteOutcomes.OrderBy(o => o.Key).Select(o => $"{o.Key}={o.Value}")));
            var knockback = RunAreaKnockbackSmoke(session, entity, () => TickAreaClock(hostMs += 50));
            // ARC-DEV-287: the hero is pushed too (goal 43 has no test of the pushed object's kind).
            var heroPush = RunAreaHeroKnockbackSmoke(session, () => TickAreaClock(hostMs += 50));
            // ARC-MAGIC-1230: so is an item lying on the ground (Unseen Force, UO5).
            var itemPush = RunAreaItemKnockbackSmoke(session, () => TickAreaClock(hostMs += 50));
            GD.Print("ARC-WORLD-092 native-smoke ai PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Knockback = knockback, HeroPush = heroPush, ItemPush = itemPush, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
                Frames = frames, Steps = host.Movers.Of(entity) is not null, StepFrames = stepFrames.Count, Hit = trace!.Hit,
                AttackFrames = _npcAttackFrames.Count - attackFrames, ArmedStands = _npcArmedStandDraws - armedStands, Shown = $"{shown.Weapon}/{shown.Shield}", StruckBack = struckBack, Fidgets = _aiFidgets - fidgets, Skills = _aiSkills - skills,
                HeroDamage = session.QueryHeroLife()!.Record.State.HitPointDamage - heroLife,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }

    // Native observation (ARC-WORLD-095, ARC_AREA_MAP_SMOKE_ONLY=ai-turn-based, map mode with --hero, the Crash Site cave): the AI and
    // the turn-based setting on, the hero four cells from the living monstrous NPC with the fewest hit points, which is told of the
    // hero's attack; R enters combat mode and round 1 begins with the NPC. In its own subturns its goal run walks it up tile by tile,
    // paying action points, and bites; it never moves on the hero's subturn, which E ends. The NPC, the hero and the settings are
    // restored for the later stages.
    private async Task RunAreaAiTurnBasedSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-095 ai-turn-based smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_TURN_BASED_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-095 native-smoke ai-turn-based NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var setting = _turnBasedSetting;
        var target = candidates[0];
        var entity = target.Holder.Entity;
        var critter = SessionCombatant.Critter(entity);
        var npcLife = target.Build.Combat!.Target;
        var npcBefore = session.QueryLife(npcLife).View!.Status.HitPoints;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (HeroInCombatMode) ToggleCombatMode();
            _turnBasedSetting = false; _roundLog.Clear();
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                CellCoord cell;
                try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                if (Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y)) >= 3) { start = cell; break; }
            }
            Require(start is not null, "a standable cell a few cells from the NPC");
            AreaSmokePlace(map, start!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero, $"the NPC fights the hero: {danger.State}");
            void Press(Key key, bool pressed) => InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            void Tap(Key key) { Press(key, true); Press(key, false); }
            Tap(Key.Space);
            Require(_turnBasedSetting && session.QueryCombatRound() is null, "Space turned the setting on: " + _turnBasedNotice);
            Tap(Key.R);
            Require(HeroInCombatMode && session.QueryCombatRound() is { Turn: 1 } first && first.Participants.Contains(critter), "R began round 1 with the NPC: " + _combatModeNotice);

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
            TickAreaClock(hostMs);
            var frames = 0; var held = 0; var heroTurns = 0; var offTurn = false; var paid = false; int? lastPoints = null;
            // ARC-RENDER-038: its attack in the round is drawn with its body's attack ART (distinct frames counted from here).
            _npcAttackFrames.Clear(); var attackFrames = 0;
            string? hit = null; var offTurnBefore = host.OffTurnSteps;
            for (; frames < 6000 && hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                hit = host.TraceOf(entity)?.Hit;
                // ARC-DEV-124: the frame step may give the NPC its subturn and pump its run in one frame, so the actor before the frame says nothing about
                // whether a step began off its turn; the host counts those.
                offTurn |= host.OffTurnSteps > offTurnBefore;
                var round = session.QueryCombatRound();
                Require(round is not null, $"the round runs at frame {frames}");
                if (round!.Actor == critter)
                {
                    if (host.RunOf(entity) is not null) held++;
                    paid |= lastPoints is { } points && round.ActionPoints < points;
                    lastPoints = round.ActionPoints;
                }
                else lastPoints = null;
                if (round.Actor.IsHero && HeroIdleForRound())
                {
                    heroTurns++;
                    Tap(Key.E);
                }
            }
            var trace = host.TraceOf(entity);
            GD.Print($"ARC-WORLD-095 ai-turn-based smoke trace perform={trace?.Perform} step={trace?.Step} hit={trace?.Hit} frames={frames} held={held} heroTurns={heroTurns}");
            Require(hit is { } bite && !bite.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal), $"the NPC's bite was resolved in a round: {hit}");
            Require(held >= 2, "its goal run held its subturn over frames");
            Require(paid, "its steps or attack paid the round's action points");
            Require(!offTurn, "it never walked on another participant's subturn");
            Require(_roundLog.Any(e => e.Kind == SessionCombatEventKind.SubturnEnded && e.Combatant == critter), "its subturn ended and passed on");
            Require(trace?.EndCombatScripts > 0, "its subturn end asked its end-combat script (point 16)");
            Require(_npcAttackFrames.Count - attackFrames >= 2, $"its attack was drawn with its attack ART: {_npcAttackFrames.Count - attackFrames} distinct frames; "
                + string.Join(", ", _armedArts.Values.Select(l => $"{l.Status} {l.Detail}")));
            var at = host.Tile(entity)!.Value;
            var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            Require(AiExecutorRules.Distance(at, new(checked((int)hero.X), checked((int)hero.Y))) == 1, "it bit from the cell next to the hero");
            RefreshAreaStatus();
            var hud = GetSelfTestSnapshot().Hud;
            Require(hud.Contains("turn-based: round", StringComparison.Ordinal) && hud.Contains(" | AI: ", StringComparison.Ordinal), "the status line shows the round and the AI");
            GD.Print("ARC-WORLD-095 native-smoke ai-turn-based PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Frames = frames, Held = held, HeroTurns = heroTurns, Hit = hit,
                EndCombatScripts = trace!.EndCombatScripts, AttackFrames = _npcAttackFrames.Count - attackFrames,
                Rounds = _roundLog.Count(e => e.Kind == SessionCombatEventKind.RoundBegan),
                Parity = "NOT_VERIFIED (round hooks read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            if (session.QueryCombatRound() is not null) session.EndCombatRound(session.Version);
            _turnBasedSetting = setting;
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            if (session.QueryLife(npcLife).View?.Status is { } now && (now.HitPoints != npcBefore || now.IsDead))
                session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            RestoreHeroForSmoke(session, heroBefore);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }

    // Native observation (ARC-WORLD-102, ARC_AREA_MAP_SMOKE_ONLY=ai-cast, map mode with --hero, a map with an NPC whose fields store a
    // magick college level: the Crash Site window of Arcanum1-024-fixed): the AI on, the hero a few cells from the highest-level such
    // NPC, which is told of the hero's attack. Its AI pass chooses one of its spells at the danger update (0x4ABC20), perform casts it
    // through the map's cast host (0x4AC250), and its casting goal plays: the start, the play timed by its cast eye candy, the release's
    // BEGIN and the goal's end. ARC-WORLD-105: a missile spell's flight. ARC-WORLD-108: the NPC, given a healing potion and hurt, drinks it
    // at its heal step (marker `ai-item`). The NPC, the hero and the casts' runs are restored for the later stages.
    private async Task RunAreaAiCastSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-102 ai-cast smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_CAST_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity), Fields: _combatHost.Fields(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key)
                && c.Fields?.Critter?.SpellTech.Take(Arcanum.Simulation.Magic.SpellBookRules.Colleges).Any(rank => rank > 0) == true)
            .OrderByDescending(c => c.Build.Combat!.Level).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-102 native-smoke ai-cast NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var target = candidates[0];
        var entity = target.Holder.Entity;
        var npcLife = target.Build.Combat!.Target;
        var npcBefore = session.QueryLife(npcLife).View?.Status.HitPoints;
        var npcFatigueBefore = session.QueryLife(npcLife).View?.Status.Fatigue ?? 0;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        string? potion = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 3..5 cells from the NPC.
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (3, 0), (-3, 0), (0, 3), (0, -3), (5, 0), (-5, 0), (0, 5), (0, -5) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                CellCoord cell;
                try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                if (Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y)) >= 3) { start = cell; break; }
            }
            Require(start is not null, "a standable cell a few cells from the NPC");
            AreaSmokePlace(map, start!.Value);
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            // The spell data loads in the background: the cast host is built once it is there.
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var caster = SpellCasterNow();
            Require(caster is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            var known = Enumerable.Range(0, Arcanum.Simulation.Magic.SpellBookRules.Spells).Where(spell => caster!.AiKnows(entity, spell)).ToArray();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero,
                $"the NPC fights the hero: {danger.State} {host.TraceOf(entity)?.Reaction} {host.TraceOf(entity)?.Setter}");

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var casts = _aiCasts; var releases = _aiCastReleases; var landed = _aiMissileArrivals;
            var frames = 0; var choices = new HashSet<string>();
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            // ARC-WORLD-105: a chosen spell with a missile begins at its missile's arrival instead of a release.
            for (; frames < 4000 && _aiCastReleases == releases && _aiMissileArrivals == landed; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                if (host.TraceOf(entity)?.Spell is { } spell) choices.Add(spell);
            }
            var trace = host.TraceOf(entity);
            GD.Print($"ARC-WORLD-102 ai-cast smoke trace knows={string.Join(",", known)} choices={string.Join(" | ", choices)} perform={trace?.Perform} cast={trace?.Cast}");
            Require(_aiCasts > casts, $"a casting goal played after {frames} frames: {trace?.Cast}");
            Require(_aiCastReleases > releases || _aiMissileArrivals > landed, $"its release (or its missile's arrival) began the run: {trace?.Cast}");
            // The goal's end follows its play (and any missile lands); afterwards no run of the NPC waits for a BEGIN.
            var settled = 0;
            for (; settled < 200 && (host.RunOf(entity)?.Frames.Any(g => g.SpellRun is not null) == true || _spellMissiles.Count > 0); settled++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(!session.QuerySpellRuns().Runs.Any(r => r.Parent?.Id == entity && r.Action == Arcanum.Simulation.Magic.SpellRunAction.Begin
                && (r.Flags & Arcanum.Simulation.Magic.SpellRunFlags.Processed) == 0), "no run of the NPC waits for its BEGIN after its goal");
            Require(settled < 200, "its casting goal ended and no missile flies");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains(" | AI: ", StringComparison.Ordinal), "the status line shows the AI");

            // ARC-WORLD-105: the NPC casts a spell with a missile at the hero (the first the table lists whose run starts; the pass
            // record's choice, as its danger update would make it): the play's action frame launches the missile from the NPC's tile,
            // which flies, is drawn and arrives, its arrival beginning the run.
            var missileSpells = Enumerable.Range(0, Arcanum.Simulation.Magic.SpellBookRules.Spells).Where(s => SpellMissileArt(s) is not null).ToArray();
            Require(missileSpells.Length > 0, "the eye candy table lists spells with a missile");
            // ARC-MAGIC-1232 (DW2): the first missile spell, Disarm (0), now takes the hero's wielded weapon (a drop that draws from the session generator and leaves the weapon on the ground), which
            // would move the stage's later casts and saves. The hero puts its weapon away first, so Disarm finds none (the drop itself is the unit tests'); no weapon, no change from before.
            if (session.QueryEquipment().Worn.Any(item => item.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon))
                Require(session.Unequip(session.Version, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed,
                    "the hero put its weapon away before the NPC's Disarm");
            var missiles = _aiMissiles; var arrivals = _aiMissileArrivals;
            int? missileSpell = null; var missileRun = -1;
            foreach (var candidate in missileSpells)
            {
                host.PerformChoice(entity, new AiPassChoice(AiPassAction.Cast, candidate, 0, null, AiSchedulerRules.Pc));
                if (host.RunOf(entity)?.Bottom.SpellRun is { } started && session.QuerySpellRuns()[started]?.Spell == candidate)
                {
                    missileSpell = candidate; missileRun = started; break;
                }
            }
            Require(missileSpell is not null, $"the NPC casts one of the missile spells {string.Join(",", missileSpells)}: {host.TraceOf(entity)?.Cast}");
            var flewFrom = false; var drawn = false;
            var missileFrames = 0;
            for (; missileFrames < 2000 && (_aiMissiles == missiles || _spellMissiles.Count > 0); missileFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                if (_spellMissiles.FirstOrDefault(m => m.Run == missileRun) is { } flying)
                {
                    flewFrom |= flying.CasterObject == entity;
                    drawn |= _spellMissileBodies.Count > 0;
                }
            }
            var missileTrace = host.TraceOf(entity)?.Cast;
            var missileEnd = _spellMissileEndsForSelfTest.LastOrDefault(e => e.Run == missileRun);
            GD.Print($"ARC-WORLD-105 ai-cast missile spell={missileSpell} run={missileRun} frames={missileFrames} cast={missileTrace} end={missileEnd}");
            Require(_aiMissiles > missiles, $"the play's action frame launched a missile: {missileTrace}");
            Require(flewFrom && drawn, "the missile flew from the NPC and was drawn");
            Require(missileEnd.End is "arrived" or "launched on its target", $"the missile arrived: {missileEnd.End}");
            Require(_aiMissileArrivals > arrivals, "its arrival began the run");
            Require(session.QuerySpellRuns()[missileRun] is not { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin }, "no run waits for its BEGIN");
            // ARC-DEV-241 (S14f, GT183/GT184): a script's free, unresistable cast by the NPC at the hero (the viewer's script cast host, no cost test): the run starts with the Free and Unresistable flags and
            // its casting goal, which the NPC plays as its own cast's; then a cast with no caster (61): a run with no parent that begins by its event, no goal.
            int? scriptSpell = null; var scriptRun = -1; string? scriptAnswer = null;
            foreach (var candidate in missileSpells)
            {
                scriptAnswer = host.ScriptCast(entity, AiSchedulerRules.Pc, candidate, free: true, unresistable: true);
                if (host.RunOf(entity)?.Bottom.SpellRun is { } startedRun && session.QuerySpellRuns()[startedRun]?.Spell == candidate) { scriptSpell = candidate; scriptRun = startedRun; break; }
            }
            Require(scriptSpell is not null, $"a script cast by the NPC starts a run: {scriptAnswer} {host.TraceOf(entity)?.Cast}");
            var scriptFlags = session.QuerySpellRuns()[scriptRun]!.Flags;
            Require((scriptFlags & Arcanum.Simulation.Magic.SpellRunFlags.Free) != 0 && (scriptFlags & Arcanum.Simulation.Magic.SpellRunFlags.Unresistable) != 0,
                $"its run is free and unresistable: {scriptFlags}");
            var scriptFrames = 0;
            for (; scriptFrames < 2000 && (session.QuerySpellRuns()[scriptRun] is { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin } || _spellMissiles.Count > 0); scriptFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(session.QuerySpellRuns()[scriptRun] is not { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin }, $"the scripted cast began its run after {scriptFrames} frames: {host.TraceOf(entity)?.Cast}");
            var bare = host.ScriptCast(null, AiSchedulerRules.Pc, scriptSpell!.Value, free: false, unresistable: false);
            var bareRun = session.QuerySpellRuns().Runs.FirstOrDefault(r => r.Parent is null && r.Spell == scriptSpell && r.Target?.Id == AiSchedulerRules.Pc);
            Require(bareRun is not null, $"a cast with no caster starts a parentless run: {bare}");
            Require(host.RunOf(entity)?.Frames.Any(g => g.SpellRun == bareRun!.Id) != true, "the parentless run has no casting goal");
            var bareFrames = 0;
            for (; bareFrames < 400 && session.QuerySpellRuns()[bareRun!.Id] is { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin }; bareFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            GD.Print($"ARC-DEV-241 ai-cast script spell={scriptSpell} frames={scriptFrames} flags={scriptFlags} bare={bare} bareFrames={bareFrames} bareAction={session.QuerySpellRuns()[bareRun!.Id]?.Action}");
            Require(session.QuerySpellRuns()[bareRun!.Id] is not { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin }, $"the parentless run began by its event after {bareFrames} frames");
            // ARC-DEV-243 (S14g, GT194–GT196): a script's damage (60) and unresistable damage (138) take hit points off the NPC through the cast host's damage step, a script's heal (75) gives them back, and a dead NPC is not healed.
            var hpStart = session.QueryLife(npcLife).View!.Status.HitPoints;
            var dmgAnswer = DamageByScript(entity, 3, 0, false);
            var hpHurt = session.QueryLife(npcLife).View!.Status.HitPoints;
            Require(hpHurt < hpStart && hpStart - hpHurt <= 3, $"a script's damage took hit points off the NPC: {hpStart}->{hpHurt} {dmgAnswer}");
            var unresistAnswer = DamageByScript(entity, 2, 3, true);
            var hpBurnt = session.QueryLife(npcLife).View!.Status.HitPoints;
            Require(hpBurnt == hpHurt - 2, $"a script's unresistable fire damage took 2: {hpHurt}->{hpBurnt} {unresistAnswer}");
            var healAnswer = HealHitPointsByScript(entity, 5);
            var hpBack = session.QueryLife(npcLife).View!.Status.HitPoints;
            Require(hpBack == Math.Min(hpBurnt + 5, session.QueryLife(npcLife).View!.Status.MaxHitPoints), $"a script's heal gave hit points back: {hpBurnt}->{hpBack} {healAnswer}");
            GD.Print($"ARC-DEV-243 ai-cast script-damage hp {hpStart}->{hpHurt}->{hpBurnt}->{hpBack}");
            // ARC-DEV-255 (GT195): the same script damage reaches a door, window, container or scenery of the map: the material's value is lost and the object breaks at 0 hit points
            // (a junk pile and an invulnerable object are skipped). NONE when the map has no such object.
            var damageable = ObjectTargets() is { } objectTargets
                ? objectTargets.Keys.Select(k => objectTargets.Target(session, k, AreaTrapped)).FirstOrDefault(t => t?.Object is { Baseline: { JunkPile: false, Invulnerable: false, Points: > 0 } b }
                    && b.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery)
                : null;
            if (damageable?.Object is { } doomed)
            {
                var before = session.QueryObject(doomed);
                var amount = before.HitPoints + 11;
                var objectAnswer = DamageByScript(doomed.Entity, amount, 0, true);
                var after = session.QueryObject(doomed);
                Require((after.Damage > before.Damage || after.Condition != SessionObjectCondition.Whole) && (after.HitPoints <= 0 || after.Condition != SessionObjectCondition.Whole),
                    $"a script's damage of {amount} broke the {doomed.Baseline.Kind} {doomed.Entity}: {before} -> {after} {objectAnswer}");
                GD.Print($"ARC-DEV-255 ai-cast object-damage kind={doomed.Baseline.Kind} material={doomed.Baseline.Material} {before} -> {after} answer={objectAnswer}");
            }
            else GD.Print("ARC-DEV-255 ai-cast object-damage NONE (no breakable object on the map)");
            // ARC-COMBAT-025 (OD1–OD4, OD12): the same script damage reaches an item lying on the ground of the map: it loses durability, breaks at 0 hit points (BUST script, bust sound) and is
            // deleted by the next break. NONE when no ground item of the map is reachable by the cast map.
            // The item is named by its session entity, as a script names it, which is also its cast id (the seed's entity), through the viewer's script-damage path.
            string? groundEntity = null;
            foreach (var ground in session.QueryMapInventory(new SessionMapId(CurrentAreaMap))?.Ground ?? [])
            {
                if (_spellCastMap?.GroundItemsAt(ground.Cell).Contains(ground.Item.Entity) != true ||
                    _spellCastMap.ObjectCombat(ground.Item.Entity) is not { } answer ||
                    answer.Combatant.Conditions.HasFlag(Arcanum.Simulation.Combat.CombatantConditions.Invulnerable)) continue;
                groundEntity = ground.Item.Entity;
                break;
            }
            if (groundEntity is not null)
            {
                var itemLog = new List<string>();
                for (var hitNumber = 0; hitNumber < 4 && !session.IsItemDestroyed(groundEntity); hitNumber++)
                {
                    var itemAnswer = DamageByScript(groundEntity, 1_000, 0, true);
                    itemLog.Add(itemAnswer);
                    Require(itemAnswer.StartsWith("damaged"), $"a script's damage reached the ground item {groundEntity} (hit {hitNumber}): {itemAnswer}");
                }
                Require(session.IsItemDestroyed(groundEntity), $"two breaks deleted the ground item {groundEntity}: {string.Join(", ", itemLog)}");
                GD.Print($"ARC-COMBAT-025 ai-cast item-damage item={groundEntity} hits={string.Join(", ", itemLog)}");
            }
            else GD.Print("ARC-COMBAT-025 ai-cast item-damage NONE (no ground item on the map that damage reaches)");
            GD.Print("ARC-WORLD-102 native-smoke ai-cast PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Caster = target.Row.PrototypeNumber, Level = target.Build.Combat!.Level, Known = known, Frames = frames,
                Casts = _aiCasts - casts, Releases = _aiCastReleases - releases, Cast = trace?.Cast,
                MissileSpell = missileSpell, MissileFrames = missileFrames, MissileLegs = missileEnd.Legs, MissileSteps = missileEnd.Steps,
                HeroDamage = session.QueryHeroLife()!.Record.State.HitPointDamage - heroBefore.HitPointDamage,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));

            // ARC-WORLD-108: the NPC, given an installed healing potion (food whose slot-0 spell is on the light-healing list) and hurt
            // to a quarter of its hit points, lists it at its heal step (0x4A8940), which the pass runs first, and perform's item-use goal
            // 39 drinks it: the slot-0 record fires on Item_Used at the NPC, which heals, and the potion leaves its list.
            for (var wait = 0; wait < 600 && _barterData is null; wait++) { AdoptBarterData(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var barter = _barterData?.Data;
            Require(barter is not null, "the item prototypes loaded (" + _barterData?.Status + ")");
            var light = caster!.AiLists[AiActionList.HealingLight];
            var potionNumber = Enumerable.Range(6049, 15213 - 6029).FirstOrDefault(n => barter!.Prototype(n) is
                { Object.Type: SessionItemSpells.FoodType, Item: { SpellManaStore: not 0 } item } && light.Contains(item.Spells[0]));
            Require(potionNumber != 0, "an installed healing potion");
            var potionFields = barter!.Prototype(potionNumber)!;
            var given = session.AddItemToNpc(session.Version, entity,
                new SessionItemKind(SessionItemType.Food, potionNumber, potionFields.Object.Name, potionFields.Object.Description), _ => SessionInsertVerdict.Accepted);
            Require(given.Failure == GameSessionFailure.None && given.Item is not null, $"the NPC took the potion: {given.Failure}");
            potion = given.Item;
            // A quarter: at or below 30 % the heal step skips its combat-mode care roll (the NPC fights the hero); its own spells are on
            // no healing list, so the heavier rungs find nothing and the light rung lists the potion.
            var max = session.QueryLife(npcLife).View?.Status.MaxHitPoints ?? 0;
            if (session.QueryLife(npcLife).View?.Status is { } full && full.HitPoints > max / 4)
                session.ApplyDamage(session.Version, npcLife, new LifeDamage { HitPoints = full.HitPoints - max / 4 });
            // ARC-DEV-089: a caster whose own spell is on the heal list (the town's has one) would list that spell alone, as the list builds
            // the spells first and the items only without a spell entry (0x4CC580); at or below half its fatigue in combat mode the
            // items come first, so the NPC is tired down to 40 % (its fatigue is restored with it, after).
            if (session.QueryLife(npcLife).View?.Status is { MaxFatigue: > 0 } rested
                && 100L * rested.Fatigue / rested.MaxFatigue > AiActionLists.ItemsFirstFatigue)
                session.ApplyDamage(session.Version, npcLife, new LifeDamage { Fatigue = rested.Fatigue - rested.MaxFatigue * 40 / 100 });
            var hurt = session.QueryLife(npcLife).View?.Status.HitPoints ?? 0;
            Require(host.Inventory(entity).Contains(potion!) && host.List(entity, AiActionList.HealingLight).Any(e => e.Item == potion),
                "the potion is on the NPC's light-healing list: its list " + string.Join(",", host.List(entity, AiActionList.HealingLight).Select(e => $"{e.Spell}:{e.Item is not null}"))
                + $", its spells on the list {string.Join(",", known.Where(light.Contains))}, fatigue {host.Support(entity).FatiguePercent}%, magick {host.Support(entity).MagickPoints}");
            var uses = _aiItemUses;
            // The pass's heal step and its perform, as the AI pass runs them (heal first; a choice skips the danger update).
            Require(host.Heal(entity), $"the heal step chose: {host.TraceOf(entity)?.Item}");
            host.Perform(entity);
            var itemFrames = 0;
            for (; itemFrames < 2000 && !session.IsItemDestroyed(potion!); itemFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var itemTrace = host.TraceOf(entity)?.Item;
            var healed = session.QueryLife(npcLife).View?.Status.HitPoints ?? 0;
            GD.Print($"ARC-WORLD-108 ai-item potion={potionNumber} item={potion} frames={itemFrames} hp={hurt}->{healed}/{max} trace={itemTrace} perform={host.TraceOf(entity)?.Perform}");
            Require(session.IsItemDestroyed(potion!), $"the NPC drank the potion: {itemTrace}");
            Require(_aiItemUses > uses, "the item use reached the viewer");
            Require(healed > hurt, "the potion healed the NPC");
            GD.Print("ARC-WORLD-108 native-smoke ai-item PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Npc = target.Row.PrototypeNumber, Potion = potionNumber, Frames = itemFrames, Hurt = hurt, Healed = healed, Max = max,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));

            // ARC-DEV-289 (MIG14 S8): the NPC's casting goal saved in the middle of its play (the attempt goal before its action frame), with the spell run it names; the live goal
            // launches its missile, which arrives; F8 loads the save, and the restored goal must be the saved one and launch and land its missile as the live one did. Last, since the
            // load replaces the session and the AI host the stage holds.
            session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000 });
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var savedRun = -1;
            foreach (var candidate in missileSpells)
            {
                host.PerformChoice(entity, new AiPassChoice(AiPassAction.Cast, candidate, 0, null, AiSchedulerRules.Pc));
                if (host.RunOf(entity)?.Bottom.SpellRun is { } started && session.QuerySpellRuns()[started]?.Spell == candidate) { savedRun = started; break; }
            }
            Require(savedRun >= 0, $"the NPC casts a missile spell to save: {host.TraceOf(entity)?.Cast}");
            static bool Playing(GoalRun? run) => run?.Top is { Goal: GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation, Node: 2 };
            for (var wait = 0; wait < 400 && !Playing(host.RunOf(entity)); wait++) Frame();
            Require(Playing(host.RunOf(entity)), $"the casting goal plays before its action frame: {host.RunOf(entity)?.Top.Goal} node {host.RunOf(entity)?.Top.Node}");
            var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-289-ai-").FullName;
            _saveLibraryForSelfTest = new(saves);
            try
            {
                QuickKey(Key.F7);
                Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved during the NPC's cast: " + _quickSaveStatus);
                var castAtSave = host.RunOf(entity)!;
                var captured = session.Capture();
                Require(captured.Goals.Runs.Any(r => r.Handle == castAtSave.Handle) && captured.Spells.Runs.Any(r => r.Id == savedRun),
                    "the snapshot holds the NPC's casting goal and the spell run it plays");
                var spellAtSave = session.QuerySpellRuns()[savedRun];
                bool Settled(GameSessionOwner s) => s.QuerySpellRuns()[savedRun] is not { Action: Arcanum.Simulation.Magic.SpellRunAction.Begin } waiting
                    || (waiting.Flags & Arcanum.Simulation.Magic.SpellRunFlags.Processed) != 0;
                bool Ended() => _spellMissileEndsForSelfTest.Any(e => e.Run == savedRun);
                _spellMissileEndsForSelfTest.Clear();
                for (var wait = 0; wait < 2000 && !(Settled(session) && Ended()); wait++) Frame();
                Require(Settled(session) && Ended(), "the live cast launched its missile, which ended");
                var live = _spellMissileEndsForSelfTest.First(e => e.Run == savedRun);
                var running = _areaSession;
                QuickKey(Key.F8);
                if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
                Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "F8 loaded a new session: " + _quickSaveStatus);
                session = _areaSession!;
                var restoredRun = session.QueryGoals().RunsOf(entity).FirstOrDefault(r => r.Handle == castAtSave.Handle);
                Require(restoredRun is not null && restoredRun.Top.Goal == castAtSave.Top.Goal && restoredRun.Top.Node == castAtSave.Top.Node && restoredRun.Top.SpellRun == savedRun,
                    "after the load the NPC's casting goal is the saved one");
                Require(session.QuerySpellRuns()[savedRun] == spellAtSave, "after the load the spell run is the saved one, field for field");
                _spellMissileEndsForSelfTest.Clear();
                for (var wait = 0; wait < 2000 && !(Settled(session) && Ended()); wait++) Frame();
                Require(Settled(session) && Ended(), "the restored cast launched its missile, which ended");
                var restored = _spellMissileEndsForSelfTest.First(e => e.Run == savedRun);
                Require((restored.Run, restored.End, restored.Legs, restored.Steps) == (live.Run, live.End, live.Legs, live.Steps),
                    $"the restored cast's missile ends as the live one ({restored}, live {live})");
                GD.Print($"ARC-DEV-289 native-smoke saved-npc-cast PASS map={_readyMapName} spell={spellAtSave?.Spell} run={savedRun} goal={castAtSave.Top.Goal} end={live.End} legs={live.Legs} steps={live.Steps}");
            }
            finally
            {
                _saveLibraryForSelfTest = null; _quickKeys.Reset();
                try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            }
        }
        finally
        {
            // ARC-WORLD-108: a potion the NPC did not drink leaves it again.
            if (potion is not null && !session.IsItemDestroyed(potion)) session.RemoveItemFromNpc(session.Version, entity, potion);
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            ClearSpellMissiles("the smoke ended");
            // The casts' runs (a maintained one keeps its slot) are ended and freed, the NPC and the hero healed. ARC-DEV-331: Free alone drops the run without its END, so the effect it
            // wrote (the Entangle spell's flag on the hero) stayed and held the hero in the stages after; ForceEnd (0x457270) runs the END first, then what is left is freed.
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Parent?.Id == entity || (r.Parent is null && r.Target?.Id == AiSchedulerRules.Pc)).ToArray())
            {
                if (session.QuerySpellRuns()[run.Id] is not null) _spellCaster?.Act(new(SessionSpellRunActionKind.ForceEnd, run.Id));
                if (session.QuerySpellRuns()[run.Id] is not null) _spellCaster?.Act(new(SessionSpellRunActionKind.Free, run.Id));
            }
            if (session.QueryLife(npcLife).View?.Status is { } now && (now.HitPoints != npcBefore || now.IsDead || now.Fatigue < npcFatigueBefore))
                session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            RestoreHeroForSmoke(session, heroBefore);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }

    // Native observation (ARC-WORLD-115, ARC_AREA_MAP_SMOKE_ONLY=ai-follower, map mode with --hero, a map with a free living speaker
    // that can join and another living NPC with combat values near it: the Crash Site window of Arcanum1-024-fixed): the AI on, the
    // speaker joins the hero, which stands next to it; the nearest other living NPC is told of the hero's attack. The attack event tells
    // the hero's follower (0x4A9B80): it focuses the hero's victim, the AI host holds it (its own walk follows nothing), walks it up
    // through its own walk and it strikes through the session's NPC hit step. The NPC, the follower and the hero are restored after.
    private async Task RunAreaAiFollowerSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area
            || _conversationHost is not { PartyWired: true }) { GD.Print($"ARC-WORLD-115 native-smoke ai-follower NONE map={_readyMapName} (needs a hero, combat values and the session party)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-115 ai-follower smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_FOLLOWER_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker)
            && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free && AreaLife.Of(session, _combatHost, CurrentAreaMap, t.Entity) is not null
            // ARC-DEV-129: the speaker must heal with the skill and fight (not undead or mechanical, not invulnerable): the original's checks
            // (0x4AE570, 0x4AABE0) refuse those, so a map whose speakers are all such (the unlit mine's) has no follower for this smoke.
            && _combatHost.Fields(CurrentAreaMap, t.Entity) is { Critter: { } speakerCritter } speakerFields
            && AreaAiHost.CanHealAndFight(speakerFields.Object.Flags, speakerCritter.Flags, speakerCritter.Flags2));
        if (template is null) { GD.Print($"ARC-WORLD-115 native-smoke ai-follower NO-FOLLOWER map={map.RuntimeMap}"); return; }
        var home = map.Chart.ToWorld(template.HomeOrigin);
        static long Apart(long ax, long ay, long bx, long by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
        // The nearest other living free NPC with combat values, drawn, that is not a pre-made hero's NPC (their heartbeat scripts
        // 1781–1792 kill the unchosen ones once the AI runs, ARC-WORLD-114).
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && h.Entity != template.Entity && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.WorldX is not null && c.Row.WorldY is not null
                && AreaUseScriptHost.LoadedSlot(c.Row, Arcanum.Scripting.Scr.ScrPoint.Heartbeat)?.Script is not (>= 1781 and <= 1792)
                && session.QueryLife(c.Build.Combat!.Target).View is { Status.IsActive: true })
            .OrderBy(c => Apart(c.Row.WorldX!.Value, c.Row.WorldY!.Value, home.X, home.Y)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        CalmNpcsForSmoke(session, candidates.Select(c => c.Holder.Entity).Append(template.Entity));
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-115 native-smoke ai-follower NO-TARGET map={map.RuntimeMap}"); return; }
        var follower = template.Entity;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var joined = false;
        string? kit = null;
        var target = candidates[0];
        var enemy = target.Holder.Entity;
        var enemyLife = target.Build.Combat!.Target;
        var enemyBefore = session.QueryLife(enemyLife).View!.Status.HitPoints;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var join = session.ForceJoinParty(session.Version, follower);
            Require(join.Disposition != GameSessionDisposition.Rejected, "the speaker joined: " + join.Failure);
            joined = true;
            // ARC-WORLD-116: first, where the hero stands (before it is placed by the fight's NPC, whose AI must not act before the fight), the
            // follower, given an installed healing item (a generic item with the healing flag and no use or destroy script) and out of
            // danger, heals the hero hurt to half through the heal step's heal-skill rung (0x4A8AA0: the companion itself is unhurt, its
            // PC leader is next), skill goal 33 and the attempt's session heal. The placement on the hero's own cell binds the joined
            // companion.
            // ARC-DEV-118: the heal stands at the follower's own home, not where an earlier stage left the hero (beside that stage's victim, which the main map's
            // residency has woken and which fights the hero while the follower heals).
            var standing = AreaSmokeStandable(map, template.HomeOrigin, 1);
            AreaSmokePlace(map, standing);
            long Near(CellCoord cell) => _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == follower) is { } now
                ? Arcanum.Simulation.Party.FollowDistanceRules.Distance(now.Grid.State.CommittedCell, cell) : long.MaxValue;
            for (var f = 0; f < 600 && Near(standing) > 4; f++) AreaSmokeFrame();
            Require(Near(standing) <= 4, "the follower came up to the hero for the heal: " + _areaCompanions?.Status());
            var healer = AreaAiHostNow();
            Require(healer is not null && healer.RunCompanions.Contains(follower) && !healer.Holds(follower), $"the AI host runs the follower: {healer?.RunCompanions.Count}");
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 29_000_000L;
            TickAreaClock(hostMs);
            for (var wait = 0; wait < 600 && _barterData is null; wait++) { AdoptBarterData(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var barter = _barterData?.Data;
            Require(barter is not null, "the item prototypes loaded (" + _barterData?.Status + ")");
            var kitNumber = Enumerable.Range(6049, 15213 - 6049 + 1).FirstOrDefault(n => barter!.Prototype(n) is { } p
                && AreaHealingItems.From("kit", p) is { } healing && !healing.ScriptPoints.Contains(SessionHealingItem.UseScript)
                && !healing.ScriptPoints.Contains(SessionHealingItem.DestroyScript));
            Require(kitNumber != 0, "an installed healing item");
            var kitFields = barter!.Prototype(kitNumber)!;
            var givenKit = session.AddItemToNpc(session.Version, follower,
                new SessionItemKind(SessionItemType.Generic, kitNumber, kitFields.Object.Name, kitFields.Object.Description), _ => SessionInsertVerdict.Accepted);
            Require(givenKit.Failure == GameSessionFailure.None && givenKit.Item is not null, $"the follower took the healing item: {givenKit.Failure}");
            kit = givenKit.Item;
            var heroMax = session.QueryHeroLife()!.Status.MaxHitPoints;
            var hurtResult = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = heroMax / 2 });
            Require(hurtResult.Disposition == GameSessionDisposition.Changed, $"the hero was hurt: {hurtResult.Failure}");
            var heroHurt = session.QueryHeroLife()!.Status.HitPoints;
            // The given item must be the tool: a healing item of the follower's own would lose a use the smoke does not restore.
            var tool = healer!.HealingItem(follower);
            Require(tool == kit, $"the given item is the follower's first healing item: {tool}");
            Require(healer.Heal(follower), $"the heal step chose: {healer.TraceOf(follower)?.Item}");
            Require(healer.TraceOf(follower)?.Item?.StartsWith($"heal {AiSchedulerRules.Pc} {AiHealStep.HealSkill} ", StringComparison.Ordinal) == true,
                $"the heal skill's rung on the hero: {healer.TraceOf(follower)?.Item}");
            var skillBefore = healer.TraceOf(follower)?.Skill;
            healer.Perform(follower);
            Require(healer.RunOf(follower)?.Bottom.Goal == GoalType.UseSkill, $"perform started goal 33: {healer.TraceOf(follower)?.Perform}");
            // Until the attempt's action frame (a later pass may choose the rung again while the hero is still hurt).
            var healFrames = 0;
            for (; healFrames < 2000 && ReferenceEquals(healer.TraceOf(follower)?.Skill, skillBefore); healFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var healTrace = healer.TraceOf(follower)?.Skill;
            var heroHealed = session.QueryHeroLife()!.Status.HitPoints;
            GD.Print($"ARC-WORLD-116 ai-follower heal item={kitNumber} tool={tool} frames={healFrames} hp={heroHurt}->{heroHealed}/{heroMax} trace={healTrace}");
            Require(healTrace?.StartsWith($"heal {AiSchedulerRules.Pc} {GameSessionDisposition.Changed} ", StringComparison.Ordinal) == true,
                $"the session healed with the item: {healTrace}");
            Require(healTrace!.Contains("success=True", StringComparison.Ordinal) ? heroHealed > heroHurt : heroHealed == heroHurt, "a success heals, a failure does not");
            GD.Print("ARC-WORLD-116 native-smoke ai-heal-item PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Follower = template.Speaker.Row.PrototypeNumber, Item = kitNumber, Frames = healFrames, Hurt = heroHurt, Healed = heroHealed, Max = heroMax,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
            if (!session.IsItemDestroyed(kit!))
                Require(session.RemoveItemFromNpc(session.Version, follower, kit!).Disposition == GameSessionDisposition.Changed, "the healing item left the follower");
            kit = null;
            if (session.QueryHeroLife()!.Record.State is { } hurtNow && hurtNow.HitPointDamage > heroBefore.HitPointDamage)
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { HitPoints = hurtNow.HitPointDamage - heroBefore.HitPointDamage });
            // The run's rest ends at the host's clock; then its own walk follows the hero again.
            for (var f = 0; f < 100 && healer.Holds(follower); f++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(!healer.Holds(follower), $"after the heal its own walk follows the hero again: {healer.TraceOf(follower)?.Perform}");
            // The hero next to the NPC, so the NPC fights it where it stands (the placement rebinds the companions, the joined one among
            // them); the follower follows the hero there (beyond 30 tiles it jumps to the hero, OL9) before the fight.
            // ARC-DEV-089: the victim is the nearest NPC the follower is not upset by (0x4ADCC0): a good follower complains instead of
            // fighting when the hero attacks a good NPC (the town's nearest one), so the follower rule has a fight to start only against
            // another (an animal, a hostile). The nearest that passes replaces the first pick, which the smoke has not touched yet.
            var victimIndex = Array.FindIndex(candidates, c => HostilityRules.Upset(healer, follower, c.Holder.Entity, AiSchedulerRules.Pc, session.Rules.Combat) == UpsetReason.None);
            if (victimIndex < 0)
            {
                GD.Print($"ARC-WORLD-115 native-smoke ai-follower NO-TARGET map={map.RuntimeMap} (every NPC with combat values upsets the follower)");
                return;
            }
            target = candidates[victimIndex]; enemy = target.Holder.Entity; enemyLife = target.Build.Combat!.Target;
            enemyBefore = session.QueryLife(enemyLife).View!.Status.HitPoints;
            // ARC-DEV-118: the NPC stands where the AI host has it, which is not its map data's cell once the main map's residency has woken it
            // (the fold's first tick runs the waypoint and standpoint steps, ARC-DEV-117), so the hero is stood next to its live tile.
            var victimTile = healer.Tile(enemy);
            var dataCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            CellCoord standCell;
            try { standCell = AreaSmokeStandable(map, victimTile is { } live ? map.Chart.ToChart(new(live.X, live.Y)) : dataCell, 1); }
            catch (InvalidOperationException) { standCell = AreaSmokeStandable(map, dataCell, 1); } // a live tile beyond the window's chart
            AreaSmokePlace(map, standCell);
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == follower);
            Require(companion is { Standing: CompanionStanding.Following }, "the speaker is a following companion: " + _areaCompanions?.Status());
            var heroAt = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            for (var f = 0; f < 600 && FollowDistance() > 4; f++) AreaSmokeFrame();
            long FollowDistance() => _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == follower) is { } now
                ? Arcanum.Simulation.Party.FollowDistanceRules.Distance(now.Grid.State.CommittedCell, heroAt) : long.MaxValue;
            Require(FollowDistance() <= 4, "the follower came up to the hero: " + _areaCompanions?.Status());
            companion = _areaCompanions!.Companions.Single(c => c.Entity == follower);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null && host.RunCompanions.Contains(follower), $"the AI host runs the follower: {host?.RunCompanions.Count}");
            Require(!host!.Holds(follower), "before the fight its own walk follows the hero");
            var startCell = companion!.Grid.State.CommittedCell;
            GD.Print($"ARC-WORLD-115 ai-follower target={target.Row.PrototypeNumber}@{target.Row.WorldX},{target.Row.WorldY} follower={host.Tile(follower)} hero={host.Tile(AiSchedulerRules.Pc)}");
            var apart = AiExecutorRules.Distance(host.Tile(follower)!.Value, host.Tile(enemy)!.Value);
            Require(apart <= 1 || host.PlanPath(follower, new AiPathRequest(AiPathKind.Attack, enemy, null, 1, false, false)) is not null, "the follower's attack path reaches the NPC");

            host.HeroAttacked(enemy);
            var danger = session.QueryDanger(follower);
            Require(danger.State == DangerState.CombatFocus && danger.Source?.Entity == enemy,
                $"the follower fights the hero's victim: {danger.State} {host.TraceOf(follower)?.Event} {host.TraceOf(follower)?.Reaction} {host.TraceOf(follower)?.Setter}");
            Require(host.Holds(follower), "the fighting follower is held by the AI host");

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var frames = 0; var walked = false; var swing = false;
            hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            for (; frames < 4000 && host.TraceOf(follower)?.Hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                walked |= _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == follower)?.Grid.State.CommittedCell is { } now && now != startCell;
                swing |= host.RunOf(follower)?.Top.Goal == GoalType.AttemptAttack;
            }
            var trace = host.TraceOf(follower);
            GD.Print($"ARC-WORLD-115 ai-follower smoke trace event={trace?.Event} danger={trace?.Danger} perform={trace?.Perform} step={trace?.Step} hit={trace?.Hit}");
            Require(trace?.Hit is { } hit && !hit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal), $"the follower's blow was resolved after {frames} frames: {trace?.Hit}");
            Require(swing, "its attempt-attack goal ran");
            Require(walked || apart <= 1, "it walked up through its own walk");
            var at = host.Tile(follower)!.Value;
            var there = host.Tile(enemy)!.Value;
            Require(AiExecutorRules.Distance(at, there) == 1, $"it struck from the cell next to the NPC: {at} {there}");
            Require(session.QueryCombatMode(SessionCombatant.Critter(follower)), "the attempt put it into combat mode (goal 19 node 5)");

            // ARC-WORLD-117: the viewer's orders through the AI host, at once after the blow (no frame between, so the NPC's own fight does
            // not re-engage the follower): back off stops the fight (FO5) and the order's AI pass runs (FO1); then an attack order on the
            // NPC as the highlighted object locks the follower on it (FO3) and its pass starts the attack, which strikes again.
            if (session.QueryLife(enemyLife).View?.Status is { IsDead: true } or { IsUnconscious: true })
                session.Heal(session.Version, enemyLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            // ARC-DEV-129: out of danger the pass's heal step casts a light heal on the follower or the hero below 90 % hit points (0x4A8AA0, found
            // by a run that failed on its "no run" check when the fight had hurt the follower), so the back-off starts from an unhurt pair: what the
            // order's pass does is then its own, not a heal that depends on the fight's rolls.
            if (_combatHost.Build(session, map.RuntimeMap, follower).Combat?.Target is { } followerHurt)
                session.Heal(session.Version, followerHurt, new LifeHeal { Full = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Resurrect = true }); // hit points only: a poisoning enemy would still open the cure-poison rung (a limit)
            var backOff = IssueAreaOrder("smoke", new(CompanionOrderType.BackOff, CompanionOrderAddressing.All, []))?.Outcomes.SingleOrDefault(o => o.Entity == follower);
            // The host's interrupt comes before the pass, whose gate then finds the follower idle.
            Require(backOff is { Effect: CompanionOrderEffect.Stopped, Stop.Interrupted: true, Pass: { Failure: GameSessionFailure.None, Pass.Gate.Passed: true } }
                && session.QueryDanger(follower).State == DangerState.None && host.RunOf(follower) is null,
                $"back off stopped the fight and ran the pass: {(backOff is null ? "none" : AreaCompanionOrders.Describe(backOff))} run={host.RunOf(follower)?.Bottom.Goal}");
            var ordered = IssueAreaOrder("smoke", new(CompanionOrderType.Attack, CompanionOrderAddressing.All, []) { Target = OrderTarget(target.Row.Key) })
                ?.Outcomes.SingleOrDefault(o => o.Entity == follower);
            Require(ordered is { Effect: CompanionOrderEffect.AttackLocked, Locked: true, Pass.Pass.Performed: true },
                $"the attack order locked the follower and its pass performed: {(ordered is null ? "none" : AreaCompanionOrders.Describe(ordered))}");
            var focus = session.QueryDanger(follower);
            Require(focus.State == DangerState.CombatFocus && focus.Focus == SessionCombatant.Critter(enemy), $"the follower's focus is the ordered target: {focus.State} {focus.Focus?.Entity}");
            Require(host.RunOf(follower) is { Bottom.Goal: GoalType.Attack } orderRun && orderRun.Bottom.Target == enemy, $"the pass started the attack: {host.TraceOf(follower)?.Perform}");
            var hitBefore = host.TraceOf(follower)?.Hit;
            var orderFrames = 0; var offTarget = false;
            for (; orderFrames < 4000 && ReferenceEquals(host.TraceOf(follower)?.Hit, hitBefore); orderFrames++)
            {
                offTarget |= host.RunOf(follower) is { } now && now.Bottom.Target != enemy;
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var orderHit = host.TraceOf(follower)?.Hit;
            Require(!ReferenceEquals(orderHit, hitBefore) && orderHit is not null && !orderHit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal),
                $"the ordered follower struck again after {orderFrames} frames: {orderHit}");
            Require(!offTarget, "its runs aimed at the ordered target only");
            GD.Print("ARC-WORLD-117 native-smoke ai-order PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                BackOff = AreaCompanionOrders.Describe(backOff!), Attack = AreaCompanionOrders.Describe(ordered!), Frames = orderFrames, Hit = orderHit,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));

            // ARC-WORLD-119: the curse through the AI host (the viewer reads no typed order text, so the smoke sends it): the follower
            // leaves (its run ends, FO20), the hero's attack event on it runs in full and asks its reaction, then its pass runs; the
            // host is kept and runs it on as a free NPC.
            var leftRun = host.RunOf(follower) is not null;
            var cursed = IssueAreaOrder("smoke", new(CompanionOrderType.Curse, CompanionOrderAddressing.All, []))?.Outcomes.SingleOrDefault(o => o.Entity == follower);
            Require(cursed is { Effect: CompanionOrderEffect.Cursed, CurseAttack: not null, Pass.Failure: GameSessionFailure.None }
                && !session.QueryCompanion(follower).LedByPc, $"the curse freed the follower and its pass ran: {(cursed is null ? "none" : AreaCompanionOrders.Describe(cursed))}");
            joined = false;
            Require(ReferenceEquals(AreaAiHostNow(), host) && host.RunCompanions.Contains(follower), "the leave kept the host, which runs the free companion");
            // The trace is live: its lines are kept as they stand after the order.
            var (curseEvent, curseReaction, cursePerform) = (host.TraceOf(follower)?.Event, host.TraceOf(follower)?.Reaction, host.TraceOf(follower)?.Perform);
            Require(curseEvent == nameof(AttackEventStop.None) && curseReaction is not null, $"the hero's attack event on it ran and asked its reaction: {curseEvent} {curseReaction}");
            var freed = session.QueryDanger(follower);
            var freeFrames = 0;
            for (; freeFrames < 200; freeFrames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(host.RunCompanions.Contains(follower) && !session.QueryCompanion(follower).LedByPc, "it stays a free companion the host runs");
            // ARC-DEV-285: a companion the host runs is pushed by the knockback as any NPC (goal 43 has no test of the pushed object's kind).
            var companionPush = PushFromFreeSideForSmoke(session, follower, () => TickAreaClock(hostMs += 50), _combatHost.Build(session, map.RuntimeMap, follower).Combat!.Target);
            Require(companionPush.StartsWith("Exhausted", StringComparison.Ordinal), "the free companion was pushed four tiles by the knockback: " + companionPush);
            GD.Print("ARC-WORLD-119 native-smoke ai-free PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Curse = AreaCompanionOrders.Describe(cursed!), Push = companionPush, LeftRun = leftRun, Reaction = curseReaction, Danger = freed.State.ToString(),
                Source = freed.Source is { IsHero: true } ? "hero" : freed.Source?.Entity, Perform = cursePerform, Frames = freeFrames,
                After = host.TraceOf(follower)?.Perform, Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
            RefreshAreaStatus();
            GD.Print("ARC-WORLD-115 native-smoke ai-follower PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Follower = template.Speaker.Row.PrototypeNumber, Target = target.Row.PrototypeNumber, Apart = apart, Walked = walked, Frames = frames,
                Hit = trace!.Hit, Event = trace.Event,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            // ARC-WORLD-116: a healing item the follower did not use up leaves it again.
            if (kit is not null && !session.IsItemDestroyed(kit)) session.RemoveItemFromNpc(session.Version, follower, kit);
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            // Both NPCs leave combat mode, their life and the hero's are restored, and the follower is free again (their danger records
            // stay: the AI is off after this stage, which runs only when named).
            foreach (var npc in new[] { follower, enemy })
                if (session.QueryCombatMode(SessionCombatant.Critter(npc)))
                    session.SetCombatMode(session.Version, SessionCombatant.Critter(npc), false, SessionCombatModeContext.RealTime);
            if (session.QueryLife(enemyLife).View?.Status is { } now && (now.HitPoints != enemyBefore || now.IsDead))
                session.Heal(session.Version, enemyLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (_combatHost.Build(session, map.RuntimeMap, follower).Combat?.Target is { } followerLife)
                session.Heal(session.Version, followerLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            RestoreHeroForSmoke(session, heroBefore);
            if (joined) session.LeaveParty(session.Version, follower, new PartyLeave(Force: true));
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            BindAreaCompanions();
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }
}
