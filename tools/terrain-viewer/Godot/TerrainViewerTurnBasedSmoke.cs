using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Charges the host made (step, attack, pickup, loot, use) with their cost and outcome, for the smoke.
    private readonly List<(string What, int Cost, AreaHeroCharge Charge)> _chargeLog = [];

    // Native observation (ARC-COMBAT-020, the turn-based round) with --hero, after the cast stage: the hero stands three cells from the
    // living NPC with the fewest hit points. Space turns the setting on (no round out of combat mode); R enters combat mode and round 1
    // begins with the NPC first, whose subturn takes no press and ends at once; on the hero's subturn a one-cell walk costs 2 action
    // points; E ends it and round 2 begins one game second later with the points granted again; next to the NPC a click attacks it and
    // the attack's start charges its cost; a long walk then spends the rest, overexerts and is refused, which passes the turn on (round
    // 3). Space turns the setting off (the round ends, combat mode stays), the game clock flows and the hero's fatigue recovers through
    // the event pump; R leaves combat mode. ARC-COMBAT-022: a right click first, in real time, keeps combat mode while the hero walks,
    // leaves it once the hero stands, never enters it and ends prowling; at the end, on the hero's subturn of a new round, it leaves
    // combat mode and ends the round. ARC-UI-1114: the combat HUD is read along the way (the running lights and the marked NPC on its subturn, the hero's points as lamps, the walk
    // preview and the charge's pending cost, the attack preview, the HUD gone with the round; ARC_AREA_HUD_CAPTURE saves readbacks). The NPC, the hero's fatigue and the setting are restored for the later stages.
    private async Task RunAreaTurnBasedSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-COMBAT-020 native-smoke turn-based SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-020 turn-based smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TURN_BASED_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-COMBAT-020 native-smoke turn-based NO-TARGET map={map.RuntimeMap}"); return; }

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var numLock = _numLockRun; var option = _alwaysRunOption; var setting = _turnBasedSetting; var concealed = HeroConcealed;
        var hudForSelfTest = _combatHudForSelfTest; var hudBarsForSelfTest = _gameBarForSelfTest;
        _worldFocusSimulationForSelfTest = true;
        // ARC-DEV-133: the search places the hero, and the safety gate opens only under focus. The smoke owns the focus from here on, as every
        // smoke does before its first placement, not the window's own, which a run behind another window does not have.
        _worldFocused = true; SelectSimulatedPadForSelfTest();
        // ARC-DEV-089: the first NPC (weakest first) the hero can stand three cells from and next to, and see from there.
        var target = candidates[0]; var found = false;
        foreach (var candidate in candidates.Take(24))
        {
            var candidateCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
            if (await AreaSmokeFightableStartAsync(map, commands[candidate.Row.Key], candidateCell, 3, 2, true) is null) continue;
            target = candidate; found = true; break;
        }
        if (!found)
        {
            GD.Print($"ARC-COMBAT-020 native-smoke turn-based NO-TARGET map={map.RuntimeMap} (no NPC the hero can stand near, next to and see)");
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; RestoreWorldTestTime(testTime); return;
        }
        var npc = target.Build.Combat!.Target;
        var npcBefore = session.QueryLife(npc).View!.Status.HitPoints;
        var fatigueBefore = session.QueryHeroLife()!.Record.State.FatigueDamage;
        var critter = SessionCombatant.Critter(target.Holder.Entity);
        try
        {
            _numLockRun = false; _alwaysRunOption = false;
            if (HeroInCombatMode) ToggleCombatMode();
            _turnBasedSetting = false; _chargeLog.Clear(); _roundLog.Clear();
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // ARC-UI-1333: "next to" is Chebyshev distance 1; the helper's standable cell can be one farther, where the preview is rightly WalkToAttack (+1 step).
            CellCoord? Near(int range) => AreaSmokeNearTarget(map, targetCell, range, maxDistance: range);
            var start = AreaSmokeNearTarget(map, targetCell, 3, 2) ?? AreaSmokeStandable(map, targetCell, 2);
            AreaSmokePlace(map, start);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Press(Key key, bool pressed) => InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            void Tap(Key key) { Press(key, true); Press(key, false); }
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            // MIG12 S8 (ARC-DEV-127): the turn-based event is on the session's real list, which only the frame step moves, and an automated run has
            // no live flow: a frame that finds the event pending (a round runs, so game time stands still: no admitted game time) ticks the clock
            // with a literal stamp, 50 ms of real time a frame. Frames without a round tick nothing, as before. ARC-DEV-148: so does a frame that finds the hero's goal 53 on the table
            // (the table steps the turn on the animation clock, 30 ms a step).
            var roundHostMs = 29_000_000L;
            void Frame()
            {
                _areaSmokeTime++;
                if (session.QueryRealStep(SessionRealStep.TurnBasedStep) is not null || ShownHeroRunsHost?.TurnRun is not null || ShownHeroRunsHost?.WalkRun is not null || ShownHeroRunsHost?.AttackRun is not null) TickAreaClock(roundHostMs += ShownHeroRunsHost?.WalkRun is not null || ShownHeroRunsHost?.AttackRun is not null ? 100 : 50); // a frame is 0.1 s of the hero's walk
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            void FramesUntil(Func<bool> done, int limit = 400) { for (var i = 0; i < limit && !done(); i++) Frame(); }
            bool HeroTurn() => session.QueryCombatRound() is { Actor.IsHero: true };
            SessionCombatRound Round() => session.QueryCombatRound() ?? throw new InvalidOperationException("ARC-COMBAT-020 smoke: no round");
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);

            // ARC-COMBAT-022: in real time a right click keeps combat mode while the hero walks, leaves it once the hero stands, never
            // enters it, and out of combat mode ends prowling. The hero goes back to its start for the round.
            // The placement restarts the smoke clock at whole units; the frames here count tenths.
            void PlaceAtStart()
            {
                AreaSmokePlace(map, start);
                _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
                PublishWorldSelection();
                Frame();
            }
            void RightClick(Vector2 at)
            {
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at });
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
            }
            var startScreen = Screen(start);
            Tap(Key.R);
            Require(HeroInCombatMode && session.QueryCombatRound() is null, "R entered combat mode in real time: " + _combatModeNotice);
            var walkGoal = RunGoal(map, start);
            Require(walkGoal is not null, "a cell to walk to");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(walkGoal!.Value), GlobalPosition = Screen(walkGoal.Value) });
            Require(HeroWalking, "the hero walks: " + _worldRouteClick);
            // ARC-DEV-146 (GT14): the walk is a run of the table (goal 3 or 4, owner pc, priority 2); the same click again keeps it, the right release ends it below.
            var walkRun = ShownHeroRunsHost?.WalkRun;
            Require(walkRun is not null && walkRun.Bottom.Tile == WorldTile((AreaGridSession)_worldActor.Session!, walkGoal.Value) && ShownHeroRunsHost!.HighestPriority == 2,
                "the hero's walk is a run on the table: " + walkRun?.Bottom);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(walkGoal.Value), GlobalPosition = Screen(walkGoal.Value) });
            Require(ShownHeroRunsHost!.WalkRun?.Handle == walkRun!.Handle, "the same click keeps the same run");
            // The hero is mid-step when the release comes.
            FramesUntil(() => _worldActor.Session!.State.Phase != MovementPhase.Settled, 30);
            Require(_worldActor.Session!.State.Phase != MovementPhase.Settled && HeroWalking, "the hero is mid-step on its route");
            var turnsBefore = _heroTurnStarts;
            // A pointer well to the side of the walk (rotation 3 or 5, not the walking facing), so the release has a turn to start.
            var walking = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var sideFacing = _worldActor.State.Facing == 3 ? 5 : 3;
            RightClick(Screen(sideFacing == 3 ? new CellCoord(walking.X, walking.Y + 5) : new CellCoord(walking.X + 5, walking.Y)));
            Require(HeroInCombatMode, "a right click while the hero walks keeps combat mode");
            // ARC-DEV-145 (RR5): the same release interrupted the walk at level 3.
            Require(!HeroWalking, "the right release interrupted the hero's walk: " + _worldRouteDiagnostic);
            Require(ShownHeroRunsHost!.WalkRun is null, "the release's level-3 interrupt ended the walk's run on the table");
            FramesUntil(() => !HeroWalking && _worldActor.Session!.State.Phase == MovementPhase.Settled);
            // The turn the release asked for waits for the hero to stand and is then drawn (it must not be lost to the step in flight).
            FramesUntil(() => !HeroTurning, 20);
            Require(_heroTurnStarts > turnsBefore && _heroTurnTarget == sideFacing, $"the release of a walking hero started a turn toward {sideFacing}: starts {_heroTurnStarts - turnsBefore}, target {_heroTurnTarget}");
            Require(_worldActor.TurnedFacing?.Octant == _heroTurnTarget && _worldActor.State.Facing == _heroTurnTarget,
                    $"the walking hero turned toward the pointer after it stood: facing {_worldActor.State.Facing}, target {_heroTurnTarget}, turned {_worldActor.TurnedFacing}");
            GD.Print($"ARC-DEV-145 walking-release turns={_heroTurnStarts - turnsBefore} facing={_worldActor.State.Facing}");
            // The standing hero's release leaves combat mode and then turns the hero toward the pointer (goal 53, 30 ms a step): a tile in another direction.
            var standing = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var facingBefore = _worldActor.State.Facing;
            var (pointerCell, wanted) = facingBefore == 3 ? (new CellCoord(standing.X + 3, standing.Y), 5) : (new CellCoord(standing.X, standing.Y + 3), 3);
            RightClick(Screen(pointerCell));
            Require(!HeroInCombatMode, "a right click left combat mode: " + _combatModeNotice);
            Require(HeroTurning || _worldActor.State.Facing == wanted, $"the release started the hero's turn: facing {facingBefore} -> {_worldActor.State.Facing}, wanted {wanted}");
            FramesUntil(() => !HeroTurning, 20);
            Require(_worldActor.State.Facing == wanted && !HeroTurning, $"the hero turned toward the pointer (rotation {wanted}): facing {_worldActor.State.Facing}");
            Require(ShownHeroRunsHost!.TurnSteps > 0, $"ARC-DEV-148: the table's goal 53 took the turn's steps: {ShownHeroRunsHost!.TurnSteps}");
            GD.Print($"ARC-DEV-145 right-release-turn from={facingBefore} to={_worldActor.State.Facing} table-steps={ShownHeroRunsHost!.TurnSteps}");
            RightClick(startScreen);
            Require(!HeroInCombatMode, "a right click does not enter combat mode");
            // A turn under way is a run that is no overlay (goal 53): the hero is animating until it ends, so the release waits for it (RR3, RR4).
            FramesUntil(() => !HeroTurning, 20);
            Require(session.ToggleHeroConcealment(session.Version).Disposition == GameSessionDisposition.Changed && HeroConcealed, "the hero prowls");
            RightClick(startScreen);
            Require(!HeroConcealed, "a right click ended prowling: " + _prowlNotice);
            PlaceAtStart();

            // Space out of combat mode: the setting only.
            Tap(Key.Space);
            Require(_turnBasedSetting && session.QueryCombatRound() is null, "Space turned the setting on without a round: " + _turnBasedNotice);
            // ARC-UI-1114: the combat HUD (action-point lamps, turn-order list) is drawn while a round runs; this stage reads it.
            _combatHudForSelfTest = true;
            // ARC-UI-1888: ARC_AREA_HUD_BARS=1 draws the interface bars of the game's art (the lamp panel then draws the game's own art over them; the readbacks make the graphics composite).
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_HUD_BARS") == "1") _gameBarForSelfTest = true;
            // ARC_AREA_HUD_CAPTURE=<ignored path prefix> saves readbacks of the HUD for the graphics composite (never committed).
            var hudCapture = System.Environment.GetEnvironmentVariable("ARC_AREA_HUD_CAPTURE");
            async Task HudShot(string name)
            {
                if (string.IsNullOrWhiteSpace(hudCapture)) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng($"{hudCapture}-{name}.png") == Error.Ok, "capture saved");
            }
            ActionPointLamp[] HudLamps() { RefreshCombatHud(); return _combatHud?.LampsForSelfTest ?? throw new InvalidOperationException("ARC-UI-1114 smoke: the combat HUD is not drawn"); }
            // R: combat mode and round 1, the NPC first (the party goes last).
            Tap(Key.R);
            Require(HeroInCombatMode && session.QueryCombatRound() is { Turn: 1 } first && first.Participants.Contains(critter), "R began round 1 with the NPC: " + _combatModeNotice);
            var clock = session.QueryClockSnapshot().GameTimestampMs;
            var far = RunGoal(map, start);
            Require(far is not null, "a far cell to walk to");
            var ignored = false;
            if (!Round().Actor.IsHero)
            {
                // CH5: on another participant's subturn three lights run along the lamps, and the list marks that participant, not the hero.
                var cue = HudLamps();
                Require(cue.Count(l => l == ActionPointLamp.Left) == ActionPointRules.CueLights && cue.Count(l => l != ActionPointLamp.Off) == ActionPointRules.CueLights,
                    "ARC-UI-1114: three running lights on the NPC's subturn: " + string.Join(",", cue));
                Require(_combatHud!.ListShownForSelfTest && _combatHud.RowTextsForSelfTest.Count(t => t.StartsWith("> ", StringComparison.Ordinal)) == 1
                    && !_combatHud.RowTextsForSelfTest.Contains("> Hero"), "ARC-UI-1114: the list marks the NPC: " + string.Join("|", _combatHud.RowTextsForSelfTest));
                await HudShot("npc-turn");
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far!.Value), GlobalPosition = Screen(far.Value) });
                ignored = !HeroWalking && _worldRouteClick.Contains("another participant", StringComparison.Ordinal);
                Require(ignored, "a press on the NPC's subturn is ignored: " + _worldRouteClick);
            }
            FramesUntil(HeroTurn, 20);
            Require(HeroTurn(), "the NPC's subturn passed to the hero");
            var grant = Round().ActionPoints;
            Require(grant >= TurnBasedRules.MinActionPoints, $"the hero's grant {grant}");
            // ARC-UI-1114 (CH2, CH4): on the hero's subturn the lamps show its points (no pending cost yet), the list marks the hero.
            var lit = Math.Min(grant, ActionPointRules.LampCount);
            var points = HudLamps();
            Require(points.Count(l => l == ActionPointLamp.Left) == lit && points.Count(l => l != ActionPointLamp.Off) == lit, $"ARC-UI-1114: {lit} lamps for {grant} points: " + string.Join(",", points));
            var rowTexts = _combatHud!.RowTextsForSelfTest;
            Require(!session.Rules.Interface.TurnOrderListHidden && _combatHud.ListShownForSelfTest && rowTexts.Count(t => t.StartsWith("> ", StringComparison.Ordinal)) == 1 && rowTexts.Contains("> Hero")
                && rowTexts[0] == $"Round {Round().Turn}", "ARC-UI-1114: the list marks the hero: " + string.Join("|", rowTexts));
            // The hover preview of a walk one cell away: the check stores 2 as the pending cost, so the lamps show the points left and the cost.
            HoverIndex();
            var stepAhead = Enum.GetValues<NeighborDirection>().Select(d => (Direction: d, Cell: RoutePlanner.Neighbor(((AreaGridSession)_worldActor.Session!).State.CommittedCell, d)))
                .First(n => new AreaRouteSource(map.Steps, map.Chart).Assess(((AreaGridSession)_worldActor.Session!).State.CommittedCell, n.Direction) == RouteStepVerdict.Open && n.Cell != targetCell).Cell;
            // The pointer over the cell goes through the viewer's own hover (the hook of ShowAreaHover), not a direct call.
            HoverAtForSelfTest(Screen(stepAhead));
            Require(_hoverAction == AreaHoverAction.Walk, $"ARC-UI-1114: the hover over the next cell is a walk: {_hoverAction}");
            Require(_apPending == TurnBasedRules.WalkStepCost, $"ARC-UI-1114: the walk preview stored {_apPending}");
            var preview = HudLamps();
            // Worked by hand: grant points, cost 2 → grant − 2 green lamps, then 2 orange, the rest off (the 20-lamp cap aside).
            var greenLamps = Math.Min(grant - 2, ActionPointRules.LampCount); var orangeLamps = Math.Min(2, ActionPointRules.LampCount - greenLamps);
            Require(preview.Take(greenLamps).All(l => l == ActionPointLamp.Left) && preview.Skip(greenLamps).Take(orangeLamps).All(l => l == ActionPointLamp.Cost)
                && preview.Skip(greenLamps + orangeLamps).All(l => l == ActionPointLamp.Off), "ARC-UI-1114: lamps preview the walk's cost: " + string.Join(",", preview));
            // The drawn lamps, not only the computed ones: green first, then orange, inside the window.
            Require(_combatHud!.LampColorForSelfTest(0) == new Color(0f, 1f, 0f) && (orangeLamps == 0 || _combatHud.LampColorForSelfTest(greenLamps) == new Color(1f, .5f, 0f)),
                "ARC-UI-1114: the drawn lamps' colours");
            var panelRect = _combatHud.PlacementForSelfTest.Panel;
            Require(new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size).Encloses(panelRect) && _combatHud.FirstLampRectForSelfTest.Size.X > 0, $"ARC-UI-1114: the lamp panel is inside the window: {panelRect}");
            await HudShot("hero-preview");
            // ARC-UI-1888: the original's art panel (synthetic art here: an automated run does not read the game's): the panel, the lamps as the arts of their shares at the
            // table's places (a lit lamp only), the end-turn button, all in one scale; the plain controls hide.
            Texture2D Block(int width, int height, Color colour) { var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8); image.Fill(colour); return ImageTexture.CreateFromImage(image); }
            var realArt = AreaCombatLampLayout.ArtNumbers.All(number => _gameBarPanel?.ArtFrame(number) is not null);
            // ARC_AREA_HUD_ART=plain draws the plain controls (the "before" frame of the graphics composite) and checks none of the art panel.
            var plain = System.Environment.GetEnvironmentVariable("ARC_AREA_HUD_ART") == "plain";
            if (plain) _combatHudArtForSelfTest = _ => null;
            else if (!realArt) _combatHudArtForSelfTest = number => number switch
            {
                AreaCombatLampLayout.PanelArt => [Block(AreaCombatLampLayout.PanelWidth, AreaCombatLampLayout.PanelHeight, new Color(.2f, .2f, .22f))],
                AreaCombatLampLayout.EndTurnArt => [.. Enumerable.Range(0, 3).Select(i => Block(AreaCombatLampLayout.EndTurnWidth, AreaCombatLampLayout.EndTurnHeight, new Color(.1f * (i + 1), .5f, .2f)))],
                _ => [Block(AreaCombatLampLayout.LampWidth, AreaCombatLampLayout.LampHeight, Colors.White)],
            };
            _combatHudStamp = null; RefreshCombatHud();
            GD.Print($"ARC-UI-1888 lamp panel art {(plain ? "plain controls" : realArt ? "from the game" : "synthetic")}");
            if (plain) { Require(!_combatHud.ArtShownForSelfTest && !_combatHud.EndTurnShownForSelfTest, "ARC-UI-1888: no art, no art panel"); await HudShot("hero-preview-plain"); }
            else {
            Require(_combatHud.ArtShownForSelfTest && _combatHud.EndTurnShownForSelfTest, "ARC-UI-1888: the art panel is drawn");
            // A control's global rectangle holds its own (unscaled) size and its position in window pixels: the bars' whole factor scales the positions, not the sizes.
            var factor = AreaGameBarPanel.ScreenFactor(GetViewport().GetVisibleRect().Size);
            var origin = _combatHud.PanelArtRectForSelfTest.Position;
            Require(_combatHud.PanelArtRectForSelfTest.Size.IsEqualApprox(new Vector2(AreaCombatLampLayout.PanelWidth, AreaCombatLampLayout.PanelHeight)), $"ARC-UI-1888: the panel keeps the original's size: {_combatHud.PanelArtRectForSelfTest}");
            var shareArt = _combatHud.LampsForSelfTest!.Select(l => AreaCombatLampLayout.LampArt(l) ?? -1).ToArray();
            for (var i = 0; i < ActionPointRules.LampCount; i++)
            {
                Require(_combatHud.LampArtNumberForSelfTest(i) == shareArt[i], $"ARC-UI-1888: lamp {i} draws art {shareArt[i]}, drew {_combatHud.LampArtNumberForSelfTest(i)}");
                if (shareArt[i] < 0) continue;
                var rect = _combatHud.LampArtRectForSelfTest(i);
                Require(rect.Position.IsEqualApprox(origin + new Vector2(AreaCombatLampLayout.LampX[i], AreaCombatLampLayout.LampY) * factor)
                    && rect.Size.IsEqualApprox(new Vector2(AreaCombatLampLayout.LampWidth, AreaCombatLampLayout.LampHeight)), $"ARC-UI-1888: lamp {i} at the table's place: {rect}");
            }
            var button = _combatHud.EndTurnRectForSelfTest;
            Require(button.Position.IsEqualApprox(origin + new Vector2(AreaCombatLampLayout.EndTurnX, AreaCombatLampLayout.EndTurnY) * factor)
                && button.Size.IsEqualApprox(new Vector2(AreaCombatLampLayout.EndTurnWidth, AreaCombatLampLayout.EndTurnHeight)) && _combatHud.EndTurnFrameForSelfTest == 0, $"ARC-UI-1888: the end-turn button at its place: {button}");
            await HudShot("hero-preview-art");
            // ARC-UI-1888, ARC-UI-1821: with ARC_AREA_WINDOW_ART=legacy|hd|generated (and ARC_HD_PACK for the last two) the panel's arts are checked in that interface mode: in legacy and hd all five are
            // plain frames; in generated the panel (294) and the end-turn button (297) are generated chrome at the bars' device scale (again in a 1600 x 1200 window), the three lamps (295, 296, 315) stay pictures.
            if (realArt && WindowArtSmokeRequested)
            {
                IReadOnlyList<int> checkedArts = System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_ART") == "generated" ? [AreaCombatLampLayout.PanelArt, AreaCombatLampLayout.EndTurnArt] : AreaCombatLampLayout.ArtNumbers;
                await WindowArtSmokeAsync("combat-lamps", () => checkedArts.Select(number => _gameBarPanel!.ArtFrame(number)).OfType<HdInterfaceTexture>().ToArray(),
                    checkedArts, BarDeviceScale, () => _combatHud.ArtShownForSelfTest, new Vector2I(1600, 1200));
            }
            }
            HoverReleaseForSelfTest(); // the pointer leaves: the pending cost stays (an action that runs no check leaves it, CH4)
            Require(_apPending == TurnBasedRules.WalkStepCost, $"ARC-UI-1114: the pending cost stays after the pointer leaves: {_apPending}");
            // The "before" frame: the same scene without the HUD, as it was drawn before this change.
            _combatHudForSelfTest = false; RefreshCombatHud();
            await HudShot("before");
            _combatHudForSelfTest = true; RefreshCombatHud();

            // One walk step.
            var grid = (AreaGridSession)_worldActor.Session!;
            var step = Enum.GetValues<NeighborDirection>().Select(d => (Direction: d, Cell: RoutePlanner.Neighbor(grid.State.CommittedCell, d)))
                .First(n => new AreaRouteSource(map.Steps, map.Chart).Assess(grid.State.CommittedCell, n.Direction) == RouteStepVerdict.Open && n.Cell != targetCell).Cell;
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(step), GlobalPosition = Screen(step) });
            FramesUntil(() => !HeroWalking && _worldActor.Session!.State.Phase == MovementPhase.Settled);
            Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == step, "the hero walked one cell");
            Require(HeroTurn() && Round().ActionPoints == grant - TurnBasedRules.WalkStepCost, $"the step cost 2: {grant} -> {Round().ActionPoints}");
            // CH4: the charge stores its cost too and does not clear it: the lamps show the points left against the same cost.
            var afterStep = HudLamps(); var leftLamps = Math.Min(grant - 2 * TurnBasedRules.WalkStepCost, ActionPointRules.LampCount);
            Require(_apPending == TurnBasedRules.WalkStepCost && afterStep.Count(l => l == ActionPointLamp.Left) == leftLamps
                && afterStep.Count(l => l == ActionPointLamp.Cost) == Math.Min(2, ActionPointRules.LampCount - leftLamps),
                $"ARC-UI-1114: the lamps after the step: pending {_apPending}, " + string.Join(",", afterStep));

            // The end-turn button of the art panel (CT5) ends the subturn as E does, round 2 one game second later, the grant again (E itself is the AI smoke's).
            if (plain) Tap(Key.E); else Require(EndTurnClickForSelfTest(), "ARC-UI-1888: the end-turn button is drawn");
            FramesUntil(HeroTurn, 20);
            Require(Round().Turn == 2 && HeroTurn() && Round().ActionPoints == grant, $"the end-turn button passed the turn: round {Round().Turn}, AP {Round().ActionPoints}");
            Require(session.QueryClockSnapshot().GameTimestampMs == clock + TurnBasedRules.RoundGameTimeMs, "one game second per round");
            // CH4: a subturn start runs the check with cost 0, so the new subturn shows all its points and no cost.
            var fresh = HudLamps();
            Require(_apPending == 0 && fresh.Count(l => l == ActionPointLamp.Cost) == 0 && fresh.Count(l => l == ActionPointLamp.Left) == Math.Min(grant, ActionPointRules.LampCount),
                $"ARC-UI-1114: the new subturn cleared the pending cost: {_apPending}, " + string.Join(",", fresh));

            // Next to the NPC a click attacks it; the start charges the attack's cost.
            var adjacent = Near(1);
            Require(adjacent is not null, "a cell next to the NPC");
            AreaSmokePlace(map, adjacent!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            var point = await HoverPointAsync(this, commands[target.Row.Key]) ?? throw new InvalidOperationException("ARC-COMBAT-020 smoke: NPC not visible");
            var cost = TurnBasedRules.AttackCost(session.QueryHeroAttack()!.WeaponSpeed);
            // ARC-UI-1114: the attack preview next to the NPC is the attack's cost alone (CH4, 0x4B78D0: no approach at one cell).
            HoverAtForSelfTest(point);
            Require(_hoverAction == AreaHoverAction.Attack && _apPending == cost, $"ARC-UI-1114: the attack preview stored {_apPending} for {_hoverAction}, expected {cost}");
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            FramesUntil(() => _attack is null, 400);
            HoverReleaseForSelfTest();
            var attack = _chargeLog.LastOrDefault(c => c.What == "attack");
            Require(attack.What == "attack" && attack.Cost == cost && AreaTurnBased.Proceeds(attack.Charge), $"the attack charged {cost}: {attack}");
            var npcAfter = session.QueryLife(npc).View!.Status.HitPoints;

            // A long walk spends the rest, overexerts and is refused: the turn passes on to round 3.
            if (!HeroTurn()) FramesUntil(HeroTurn, 20);
            var turn = Round().Turn;
            // With odd points left the walk's last step overexerts: a pickup's 1 point makes even points odd first.
            if (Round().ActionPoints % 2 == 0) Require(ChargeHeroAction(TurnBasedRules.PickUpCost, "pickup") && HeroTurn(), "a pickup's point");
            var fatigue = session.QueryHeroLife()!.Record.State.FatigueDamage;
            grid = (AreaGridSession)_worldActor.Session!;
            far = RunGoal(map, grid.State.CommittedCell);
            Require(far is not null, "a far cell from the NPC");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far!.Value), GlobalPosition = Screen(far.Value) });
            FramesUntil(() => !HeroWalking && _worldActor.Session!.State.Phase == MovementPhase.Settled);
            var refused = _chargeLog.LastOrDefault(c => c.What == "step");
            Require(refused.Charge == AreaHeroCharge.Refused && _worldRoute?.StopReason == "ROUTE_STOPPED_BY_TURN_BASED", $"the walk was refused: {refused} {DescribeWorldRoute(_worldRoute!)}");
            FramesUntil(HeroTurn, 20);
            Require(Round().Turn == turn + 1 && HeroTurn(), $"the refusal passed the turn on: round {Round().Turn}");
            var overexerted = _chargeLog.Count(c => c.Charge == AreaHeroCharge.Overexerted);
            var fatigueAfterWalk = session.QueryHeroLife()!.Record.State.FatigueDamage;
            Require(overexerted == 1 && fatigueAfterWalk > fatigue, $"one overexertion took fatigue: {overexerted}, {fatigue} -> {fatigueAfterWalk}");

            // Space off: the round ends, combat mode stays; the clock flows and the fatigue recovers.
            Tap(Key.Space);
            Require(!_turnBasedSetting && session.QueryCombatRound() is null && HeroInCombatMode, "Space ended the round: " + _turnBasedNotice);
            RefreshCombatHud();
            Require(_combatHud is { ShownForSelfTest: false } && _apPending == 0, "ARC-UI-1114: the HUD is hidden and the pending cost cleared once the round ends");
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage;
            Require(damage > 0 && _areaAmbient is not null, $"fatigue to recover ({damage}) and the clock driver ({_areaAmbient is not null})");
            Require(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroFatigue), "the fatigue recovery is on the queue");
            var host = roundHostMs += 1_000_000;
            TickAreaClock(host);
            for (var i = 0; i < 4000 && session.QueryHeroLife()!.Record.State.FatigueDamage >= damage; i++) TickAreaClock(roundHostMs = host += 250);
            Require(session.QueryHeroLife()!.Record.State.FatigueDamage < damage, $"the event pump recovered fatigue: {damage} -> {session.QueryHeroLife()!.Record.State.FatigueDamage}");
            var recovered = $"{damage}->{session.QueryHeroLife()!.Record.State.FatigueDamage}";
            Tap(Key.R);
            Require(!HeroInCombatMode, "R left combat mode");
            // ARC-COMBAT-022: on the hero's own subturn a right click leaves combat mode, which ends the round.
            PlaceAtStart();
            Tap(Key.Space);
            Tap(Key.R);
            FramesUntil(HeroTurn, 20);
            Require(HeroTurn() && HeroIdleForRound(), "a new round reached the idle hero's subturn: " + _combatModeNotice);
            RightClick(startScreen);
            Require(!HeroInCombatMode && session.QueryCombatRound() is null, "a right click left combat mode and ended the round: " + _combatModeNotice);
            Tap(Key.Space);
            RefreshAreaStatus();
            GD.Print("ARC-COMBAT-020 native-smoke turn-based PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Grant = grant, AttackCost = cost, Attack = attack.Charge.ToString(),
                HitPoints = $"{npcBefore}->{npcAfter}", Steps = _chargeLog.Count(c => c.What == "step"), Hud = "lamps+list", Overexerted = overexerted, Ignored = ignored,
                Rounds = _roundLog.Count(e => e.Kind == SessionCombatEventKind.RoundBegan), Fatigue = $"{fatigue}->{fatigueAfterWalk}", Recovered = recovered,
                Parity = "NOT_VERIFIED (charges and keys read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            if (session.QueryCombatRound() is not null) session.EndCombatRound(session.Version);
            _turnBasedSetting = setting;
            _combatHudForSelfTest = hudForSelfTest; _combatHudArtForSelfTest = null; _gameBarForSelfTest = hudBarsForSelfTest; RefreshCombatHud();
            if (HeroConcealed != concealed) session.ToggleHeroConcealment(session.Version);
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            var npcNow = session.QueryLife(npc).View?.Status;
            if (npcNow is { } now && (now.HitPoints != npcBefore || now.IsDead))
                session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage - fatigueBefore;
            if (damage > 0) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = damage });
            PublishWorldSelection();
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
