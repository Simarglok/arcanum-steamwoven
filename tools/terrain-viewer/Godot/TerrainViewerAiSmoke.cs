using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
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
                swing |= host.RunOf(entity)?.Top.Goal == AiGoalType.AttemptAttack;
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
            // ARC-WORLD-109: a skill run (pickpocket on itself, whose attempt is not ready) walks goal 33 into 86 and reaches the viewer.
            var skills = _aiSkills;
            Require(session.QueryLife(target.Build.Combat!.Target).View is { Status.IsActive: true }, "the animal can still act after its bite");
            host.PerformSkill(entity, entity, AiExecutorRules.PickpocketSkill, null);
            Require(host.RunOf(entity)?.Bottom.Goal == AiGoalType.UseSkill, "the skill starter begins goal 33: " + host.TraceOf(entity)?.Perform);
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
            GD.Print("ARC-WORLD-092 native-smoke ai PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
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
            string? hit = null;
            for (; frames < 6000 && hit is null; frames++)
            {
                var before = session.QueryCombatRound()?.Actor;
                var cell = host.Tile(entity);
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                hit = host.TraceOf(entity)?.Hit;
                offTurn |= before != critter && host.Tile(entity) != cell;
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
            var heroNow = session.QueryHeroLife()!.Record.State;
            if (heroNow.HitPointDamage > heroBefore.HitPointDamage || heroNow.FatigueDamage > heroBefore.FatigueDamage)
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { HitPoints = heroNow.HitPointDamage - heroBefore.HitPointDamage, Fatigue = heroNow.FatigueDamage - heroBefore.FatigueDamage });
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
        }
        finally
        {
            // ARC-WORLD-108: a potion the NPC did not drink leaves it again.
            if (potion is not null && !session.IsItemDestroyed(potion)) session.RemoveItemFromNpc(session.Version, entity, potion);
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            ClearSpellMissiles("the smoke ended");
            // The casts' runs (a maintained one keeps its slot) are freed, the NPC and the hero healed.
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Parent?.Id == entity).ToArray())
                _spellCaster?.Act(new(SessionSpellRunActionKind.Free, run.Id));
            if (session.QueryLife(npcLife).View?.Status is { } now && (now.HitPoints != npcBefore || now.IsDead || now.Fatigue < npcFatigueBefore))
                session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            var heroNow = session.QueryHeroLife()!.Record.State;
            if (heroNow.HitPointDamage > heroBefore.HitPointDamage || heroNow.FatigueDamage > heroBefore.FatigueDamage)
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { HitPoints = heroNow.HitPointDamage - heroBefore.HitPointDamage, Fatigue = heroNow.FatigueDamage - heroBefore.FatigueDamage });
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
            && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free && AreaLife.Of(session, _combatHost, CurrentAreaMap, t.Entity) is not null);
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
            Require(healer.RunOf(follower)?.Bottom.Goal == AiGoalType.UseSkill, $"perform started goal 33: {healer.TraceOf(follower)?.Perform}");
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
                swing |= host.RunOf(follower)?.Top.Goal == AiGoalType.AttemptAttack;
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
            Require(host.RunOf(follower) is { Bottom.Goal: AiGoalType.Attack } orderRun && orderRun.Bottom.Target == enemy, $"the pass started the attack: {host.TraceOf(follower)?.Perform}");
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
            GD.Print("ARC-WORLD-119 native-smoke ai-free PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Curse = AreaCompanionOrders.Describe(cursed!), LeftRun = leftRun, Reaction = curseReaction, Danger = freed.State.ToString(),
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
            var heroNow = session.QueryHeroLife()!.Record.State;
            if (heroNow.HitPointDamage > heroBefore.HitPointDamage || heroNow.FatigueDamage > heroBefore.FatigueDamage)
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { HitPoints = heroNow.HitPointDamage - heroBefore.HitPointDamage, Fatigue = heroNow.FatigueDamage - heroBefore.FatigueDamage });
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
