using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-092, ARC_AREA_MAP_SMOKE_ONLY=ai, map mode with --hero, the Crash Site cave): the AI turns on, the
    // hero stands a few cells from the living monstrous NPC with the fewest hit points, in combat mode, and that NPC is told of the
    // hero's attack (the attack event a hit runs). Its AI tick's pass focuses the hero, the run walks it up tile by tile, it swings
    // and the session resolves its bite on the hero; its body is drawn on its new cell and the status line shows the AI.
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
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-092 native-smoke ai NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var target = candidates[0];
            var entity = target.Holder.Entity;
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 3..4 cells from the NPC, so it walks up before it bites.
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
            // Automated runs drive the clock themselves: a frame is 50 host milliseconds, as the world frame (the game clock runs 8x).
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            for (; frames < 4000 && host.TraceOf(entity)?.Hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                drawnMoved |= host.Movers.Of(entity) is { } mover && AreaMovingCommands()?.Any(c => c.Identity == target.Row.Key && c.MapX == mover.Cell.X && c.MapY == mover.Cell.Y) == true;
                if (host.Movers.Of(entity)?.Stepping(WorldMilliseconds()) == true && AreaMovingCommands()?.FirstOrDefault(c => c.Identity == target.Row.Key) is { } stepping)
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
            Require(stepFrames.Count >= 2, $"its steps were drawn with its move cycle: {stepFrames.Count} distinct frames over {steppingFrames} stepping frames; "
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
            GD.Print("ARC-WORLD-092 native-smoke ai PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
                Frames = frames, Steps = host.Movers.Of(entity) is not null, StepFrames = stepFrames.Count, Hit = trace!.Hit, StruckBack = struckBack, Fidgets = _aiFidgets - fidgets, Skills = _aiSkills - skills,
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
            var at = host.Tile(entity)!.Value;
            var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            Require(AiExecutorRules.Distance(at, new(checked((int)hero.X), checked((int)hero.Y))) == 1, "it bit from the cell next to the hero");
            RefreshAreaStatus();
            var hud = GetSelfTestSnapshot().Hud;
            Require(hud.Contains("turn-based: round", StringComparison.Ordinal) && hud.Contains(" | AI: ", StringComparison.Ordinal), "the status line shows the round and the AI");
            GD.Print("ARC-WORLD-095 native-smoke ai-turn-based PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Frames = frames, Held = held, HeroTurns = heroTurns, Hit = hit,
                EndCombatScripts = trace!.EndCombatScripts,
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
    // BEGIN and the goal's end. The NPC, the hero and the casts' runs are restored for the later stages.
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
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-102 native-smoke ai-cast NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var target = candidates[0];
        var entity = target.Holder.Entity;
        var npcLife = target.Build.Combat!.Target;
        var npcBefore = session.QueryLife(npcLife).View?.Status.HitPoints;
        var heroBefore = session.QueryHeroLife()!.Record.State;
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
        }
        finally
        {
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            ClearSpellMissiles("the smoke ended");
            // The casts' runs (a maintained one keeps its slot) are freed, the NPC and the hero healed.
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Parent?.Id == entity).ToArray())
                _spellCaster?.Act(new(SessionSpellRunActionKind.Free, run.Id));
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
    }
}
