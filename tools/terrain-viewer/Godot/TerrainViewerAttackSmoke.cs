using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-005) with --hero, after the combat-mode stage, on the exterior or a whole map: in combat mode
    // the hero clicks the living NPC with the fewest hit points from a few cells away, walks within reach, plays its attack
    // ART, and the auto-attack repeats until the NPC dies; the session then holds its death, the experience it paid and makes it
    // lootable, its body is drawn fallen, and a click on the corpse in combat mode without Alt walks to the tile behind it (ARC-INPUT-1937). A map without a
    // living NPC that has combat values reports that and passes nothing.
    private async Task RunAreaAttackSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-005 attack smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_ATTACK_SMOKE_FAILED");
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
        var refused = session.QueryMapInventory(new(map.RuntimeMap))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc)
            .Select(h => _combatHost.Build(session, map.RuntimeMap, h.Entity).Failure).Where(f => f != SessionCritterCombatFailure.None)
            .GroupBy(f => f).ToDictionary(g => g.Key.ToString(), g => g.Count());
        if (candidates.Length == 0)
        {
            GD.Print("ARC-COMBAT-005 native-smoke attack NO-TARGET " + System.Text.Json.JsonSerializer.Serialize(new { map.RuntimeMap, Refused = refused }));
            return;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // ARC-DEV-089: the first NPC (weakest first) the hero can stand 2..3 cells from and see from there.
            var target = candidates[0]; var found = false;
            foreach (var candidate in candidates.Take(24))
            {
                var candidateCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
                if (await AreaSmokeFightableStartAsync(map, commands[candidate.Row.Key], candidateCell, 3, 2, false) is null) continue;
                target = candidate; found = true; break;
            }
            if (!found) { GD.Print($"ARC-COMBAT-005 native-smoke attack NO-TARGET map={map.RuntimeMap} (no NPC the hero can stand near and see)"); return; }
            var key = target.Row.Key;
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 2..4 cells from the target, so the click walks within reach first.
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3), (2, 0), (-2, 0), (0, 2), (0, -2) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                CellCoord cell;
                try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                var d = Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y));
                if (d >= 2) { start = cell; break; }
            }
            AreaSmokePlace(map, start ?? AreaSmokeStandable(map, targetCell, 1));
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var point = await HoverPointAsync(this, commands[key]) ?? throw new InvalidOperationException("ARC-COMBAT-005 smoke: target not visible");
            HoverAtForSelfTest(point);
            var hover = HoverActionForSelfTest;
            Require(hover is AreaHoverAction.Attack or AreaHoverAction.WalkToAttack, "hover " + hover);
            Require(Godot.Input.GetCurrentCursorShape() == Godot.Input.CursorShape.Drag || _hoverCursor == Godot.Input.CursorShape.Drag, "attack cursor");

            var experience = session.QueryHeroExperience()?.Experience;
            var draws = session.QueryRandom().Draws;
            var attackSounds = _cuePlays.GetValueOrDefault("attack");
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            AreaSmokeClick(point);
            Require(_attack is not null, "the click started an attack: " + _attackNotice);
            AttackSettleForSelfTest();
            var walked = AttackApproaching;
            var attackFrames = 0; var frames = 0; var maxAttacks = 0;
            for (; frames < 20000 && !session.QueryNpcKilled(target.Holder.Entity); frames++)
            {
                _areaSmokeTime++; SmokeTickHeroRuns();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                if (_worldActor.State.Action == ActorDiagnosticAction.Attack) attackFrames++;
                maxAttacks = Math.Max(maxAttacks, _attack?.Attacks ?? 0);
                if (_attack is null && !session.QueryNpcKilled(target.Holder.Entity)) break;
            }
            Require(session.QueryNpcKilled(target.Holder.Entity), $"killed after {frames} frames, attack {_attack is not null}: {_attackNotice}");
            Require(attackFrames > 0, "the attack ART was drawn");
            // ARC-RENDER-037: in combat mode the hero attacks and stands with its wielded weapon (bare hands without one, WA1).
            // A wielded weapon whose use art reads shows its own part (above 1); bare hands show 1.
            var wielded = session.QueryEquipment().Worn.FirstOrDefault(item => item.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon);
            var readUse = wielded is null ? null : _inventoryHost!.Wearable(session, wielded.Entity, _areaKitWear)?.UseArt;
            var armedBody = ArmedBody(session, session.QueryEquipment().Worn, HeroBody.HumanFemale);
            Require(armedBody is not null, "the wielded weapon has a weapon part");
            var armed = armedBody!.Value.Weapon;
            Require((readUse is { } use && Arcanum.Simulation.Equipment.BodyArmorArtRules.ItemArmor(use) > 0 ? armed > 1 : armed == 1) && WantedHeroBody().Weapon == armed && Arcanum.Simulation.Equipment.WeaponArtRules.Weapon(_worldActor.Profile.Attack!.Selector) == armed
                && Arcanum.Simulation.Equipment.WeaponArtRules.Weapon(_worldActor.Profile.Idle.Selector) == armed,
                $"the attack and stand ARTs show weapon {armed}: attack 0x{_worldActor.Profile.Attack!.Selector:x8} idle 0x{_worldActor.Profile.Idle.Selector:x8} ({_heroBodyStatus})");
            Require(session.QueryRandom().Draws > draws, "the attacks rolled");
            // ARC-UI-042: every attack plays its result on the hero (a miss always has 4010, an unarmed hit the material sounds).
            Require(_audio is null || _cuePlays.GetValueOrDefault("attack") > attackSounds, "the attacks played their sounds: " + _cueNotice);
            // The killing attack plays its ART to the last frame (a weapon's frames after the action frame may outlast 4 frames:
            // the staff's 4 at 83 ms, ARC-RENDER-037), then the auto attack does not repeat.
            for (var i = 0; i < 40 && (i < 4 || _attack is not null); i++) { _areaSmokeTime++; SmokeTickHeroRuns(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            Require(_attack is null && _worldActor.State.Action != ActorDiagnosticAction.Attack, "the attack stops with the death");
            Require(session.QueryMapInventory(new(map.RuntimeMap))!.Holders.Single(h => h.Entity == target.Holder.Entity).Dead, "the corpse can be looted");
            Require(experience is null || session.QueryHeroExperience()!.Experience > experience, "the kill paid experience");
            // ARC-COMBAT-1720: the auto switch host path (0x4B5580 → 0x4B83E0): a wear that broke the weapon asks the session for the best weapon against the struck NPC. The weapon is not really
            // broken here (the pre-made hero holds one weapon), so the same weapon comes back: nothing with the setting off, the weapon with it on.
            if (wielded is not null)
            {
                var wearStep = new SessionWearStep(SessionWearStepKind.AttackerWeapon, DamageRecipient.Attacker, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, wielded.Entity, 1, SessionWearSkip.None,
                    new Arcanum.Session.ItemDurabilityResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, Arcanum.Simulation.Items.ItemBreakStep.Broken, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, session.Version), null);
                var brokeWear = new SessionAttackWearResult(GameSessionDisposition.Changed, GameSessionFailure.None, [wearStep], GameSessionFailure.None, session.Version);
                var savedSettings = _gameSettings;
                try
                {
                    Require(HeroWeaponWornOut(brokeWear, target.Row, false) is null, "auto switch off: nothing is wielded after a broken weapon");
                    _gameSettings = _gameSettings with { AutoSwitchWeapons = true };
                    Require(HeroWeaponWornOut(brokeWear, target.Row, false) == wielded.Entity, "auto switch on: the best weapon is wielded after a broken weapon");
                    Require(session.QueryEquipment().Worn.Any(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && w.Entity == wielded.Entity), "the hero wields it");
                    // ARC-COMBAT-1738: the no-ammunition case (0x4B3170) and the struck hero's lost weapon (0x4B4390) reach the same re-wield.
                    Require(HeroOutOfAmmunition(AttackRefusal.None, target.Row, false) is null, "a served attack calls no re-wield");
                    Require(HeroOutOfAmmunition(AttackRefusal.OutOfAmmunition, target.Row, false) == wielded.Entity, "auto switch on: no ammunition wields the best weapon");
                    var heroStruck = new SessionAttackResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, DamageRecipient.Target, null, null, null, GameSessionFailure.None, session.Version)
                        { Criticals = [new SessionCriticalStep(SessionCriticalStepKind.DropWeapon, DamageRecipient.Target, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, wielded.Entity, SessionCriticalStepOutcome.Dropped, null)] };
                    Require(HeroWeaponLostToStrike(heroStruck, target.Row, false) == wielded.Entity, "auto switch on: a struck hero's dropped weapon wields the best weapon");
                    _gameSettings = _gameSettings with { AutoSwitchWeapons = false };
                    Require(HeroOutOfAmmunition(AttackRefusal.OutOfAmmunition, target.Row, false) is null && HeroWeaponLostToStrike(heroStruck, target.Row, false) is null, "auto switch off: the two cases change nothing");
                }
                finally { _gameSettings = savedSettings; }
                GD.Print("ARC-COMBAT-1720 native-smoke auto-switch PASS weapon=" + wielded.Entity);
            }
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("killed", StringComparison.Ordinal), "the status line names the kill");
            Require(_fallenBodies.ContainsKey(key), "the body is drawn fallen");
            // ARC-RENDER-040: the dying animation plays, then the pool of blood comes.
            RequireDyingAnimation(session, map.RuntimeMap, target.Holder.Entity, key);
            await RequireBloodPoolAsync(session, map.RuntimeMap, target.Holder.Entity);

            // ARC-INPUT-1937 (0x551F80): a corpse in combat mode without Alt is clicked through: the press is the ground's (the hero walks to the tile under the pointer) and the hover shows
            // the walk, not the corpse; with Alt the corpse is picked and looted.
            point = await HoverPointAsync(this, _fallenBodies[key]) ?? throw new InvalidOperationException("ARC-COMBAT-005 smoke: corpse not visible");
            Require(AreaObjectAtScreen(point) is { } picked && picked == key, "the corpse is picked where it is drawn (Alt held)");
            Require(AreaObjectAtScreen(point, spared: PressPickSpared(false)) != key, "the pick without Alt sees through the corpse");
            HoverAtForSelfTest(point);
            var corpseHover = HoverActionForSelfTest;
            Require(corpseHover is AreaHoverAction.Walk or AreaHoverAction.NoRoute && HoverKeyForSelfTest != key, "corpse hover without Alt is the ground's: " + corpseHover);
            AreaSmokeClick(point);
            Require(_attack is null && !InventoryOpen && _worldRouteClick.Contains("goal set", StringComparison.Ordinal) && _attackNotice?.Contains("corpse", StringComparison.Ordinal) != true,
                "a corpse click without Alt walks to the tile behind it: " + _worldRouteClick);
            CancelAreaAttack(null);
            HeroRequests.Apply(new Arcanum.World.StopRequest(null));
            _worldRouteClick = "none";
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point, AltPressed = true });
            Require(_attack is null && !_worldRouteClick.Contains("goal set", StringComparison.Ordinal) && _inventoryStatus.Contains("goal 81 started", StringComparison.Ordinal),
                "a corpse click with Alt is the loot (goal 81), not a walk: " + _worldRouteClick + " / " + _inventoryStatus);
            CancelInventoryApproach("smoke: the loot click is shown");
            HeroRequests.Apply(new Arcanum.World.StopRequest(null));
            ToggleCombatMode();
            HoverReleaseForSelfTest();
            // Out of combat mode the hero shows no weapon again (WA1, WA3).
            SyncHeroBody();
            Require(WantedHeroBody().Weapon == 0 && Arcanum.Simulation.Equipment.WeaponArtRules.Weapon(_worldActor.Profile.Idle.Selector) == 0,
                $"out of combat mode the stand shows no weapon: idle 0x{_worldActor.Profile.Idle.Selector:x8}");
            // The later stages see the map as it was: the NPC is brought back (experience stays).
            var raised = session.Heal(session.Version, target.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            Require(raised.Disposition != GameSessionDisposition.Rejected && !session.QueryNpcKilled(target.Holder.Entity), "the NPC is brought back: " + raised.Failure);
            PublishWorldSelection();
            Require(!_fallenBodies.ContainsKey(key), "the body stands again");
            // ARC-RENDER-039: an NPC knocked out lies in its fall (animation 7), so the hero's attack on it is the low attack (WA4), drawn
            // with the body's low attack ART (bare hands) or the attack ART the repair gives a weapon (MA1); the NPC is woken after.
            var knocked = session.ApplyDamage(session.Version, target.Build.Combat!.Target, new LifeDamage { Fatigue = 1000 });
            Require(knocked.Disposition != GameSessionDisposition.Rejected, "the NPC takes the fatigue: " + knocked.Failure);
            var lowAction = ActorDiagnosticAction.Attack;
            var lowFrames = 0;
            // An NPC that never goes unconscious (the undead) skips the check.
            if (session.QueryLife(target.Build.Combat!.Target).View?.Status.IsUnconscious != true)
                GD.Print("ARC-RENDER-039 native-smoke low attack SKIP (the NPC does not go unconscious)");
            else
            {
                if (!HeroInCombatMode) ToggleCombatMode();
                SyncHeroBody();
                // The armed body's: its own low attack ART, or the attack ART for a weapon the repair turns (MA1).
                lowAction = _worldActor.Profile.LowAttack is null ? ActorDiagnosticAction.Attack : ActorDiagnosticAction.LowAttack;
                PublishWorldSelection();
                Require(_fallenBodies.ContainsKey(key), "the knocked-out body is drawn fallen");
                // ARC-RENDER-040: the fall plays from the knockout (the earlier death's show went when the NPC stood).
                Require(_lyingShows.TryGetValue((map.RuntimeMap, target.Holder.Entity), out var fall) && fall.StartMs is not null && fall.EndsAtMs is null
                    && fall.Load.Pose!.Animation == AreaFallenPoses.FallDown, "the knockout plays the fall: " + fall);
                point = await HoverPointAsync(this, _fallenBodies[key]) ?? throw new InvalidOperationException("ARC-RENDER-039 smoke: knocked-out body not visible");
                HoverAtForSelfTest(point);
                AreaSmokeClick(point);
                Require(_attack is not null, "the click on the knocked-out NPC started an attack: " + _attackNotice);
                for (var i = 0; i < 4000 && lowFrames == 0 && _attack is not null; i++)
                {
                    _areaSmokeTime++; SmokeTickHeroRuns();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                    if (_attack?.Player is not null && _attack.Animation == Arcanum.Simulation.Equipment.WeaponArtRules.LowAttackAnimation
                        && _worldActor.State.Action == lowAction && _attack.Action == lowAction) lowFrames++;
                }
                Require(lowFrames > 0, $"the low attack ({lowAction}) was drawn: {_attackNotice}");
                CancelAreaAttack(null);
                HoverReleaseForSelfTest();
                ToggleCombatMode();
                Require(!HeroInCombatMode, "the hero leaves combat mode after the low attack");
                SyncHeroBody();
            }
            var woken = session.Heal(session.Version, target.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            Require(woken.Disposition != GameSessionDisposition.Rejected && session.QueryLife(target.Build.Combat!.Target).View?.Status.IsActive == true,
                "the NPC is woken: " + woken.Failure);
            PublishWorldSelection();
            Require(!_fallenBodies.ContainsKey(key), "the body stands again after the low attack");
            GD.Print("ARC-COMBAT-005 native-smoke attack PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
                Walked = walked, Frames = frames, AttackFrames = attackFrames, Weapon = armed, LowAttack = lowFrames > 0 ? lowAction.ToString() : "SKIP", Attacks = maxAttacks, Hover = hover.ToString(), CorpseHover = corpseHover.ToString(),
                Experience = session.QueryHeroExperience()?.Experience - experience, Refused = refused,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
            // ARC-DEV-317: last, as it replaces the session (F8): nothing after it reads the stage's own session.
            await SavedAttackAsync(map, target.Holder.Entity, target.Row, Require);
        }
        finally
        {
            CancelAreaAttack(null); _attackAimForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // ARC-DEV-317 (MIG14 S9b): the hero's attack saved in its swing. The hero stands next to the NPC and swings; F7 between the swing's start and its hit; the live hit lands; F8; the loaded run's next step is the
    // hit at the saved time, with the same roll (the random stream is saved), so the NPC's hit points end as the live hit left them, and no second swing was paid.
    private async Task SavedAttackAsync(AreaMapRuntime map, string entity, AreaObjectRecord row, Action<bool, string> require)
    {
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-317-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            AreaSmokePlace(map, AreaSmokeStandable(map, map.Chart.ToChart(new(row.WorldX!.Value, row.WorldY!.Value)), 1));
            if (!HeroInCombatMode) ToggleCombatMode();
            var session = _areaSession!;
            require(_combatHost!.Target(session, entity) is { Build.Combat: not null }, "the NPC is an attack target again");
            var npc = _combatHost.Target(session, entity)!;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            CancelAreaAttack(null);
            _attack = new(npc, npc.Build.Combat!, null);
            StartAttackGoal(_attack);
            AttackSettleForSelfTest();
            bool Swinging(GameSessionOwner owner) => owner.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Any(r => r.Top is { Goal: Arcanum.Simulation.Goals.GoalType.AttemptAttack, Node: 2 });
            require(Swinging(session), $"the hero swings from where it stands: {_attackNotice}");
            int Hp(GameSessionOwner owner) => owner.QueryLife(npc.Build.Combat!.Target).View!.Status.HitPoints;
            var before = Hp(session);
            QuickKey(Key.F7);
            require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved the swinging hero: " + _quickSaveStatus);
            require(session.Capture().Goals.Runs.Any(r => r.Owner == AreaHeroRunsHost.Hero && r.Top.Goal == Arcanum.Simulation.Goals.GoalType.AttemptAttack), "the save holds the hero's attack");
            for (var i = 0; i < 200 && Swinging(session); i++) { _areaSmokeTime++; SmokeTickHeroRuns(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            require(!Swinging(session), "the live swing reached its hit");
            var live = Hp(session);
            var liveDraws = session.QueryRandom().Draws;
            CancelAreaAttack(null);
            QuickKey(Key.F8);
            require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded the swinging hero: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            var loaded = _areaSession!;
            require(!ReferenceEquals(loaded, session) && Swinging(loaded), "the loaded session holds the swing");
            require(Hp(loaded) == before, "the load brought the NPC back to its hit points at the save");
            var draws = loaded.QueryRandom().Draws;
            for (var i = 0; i < 200 && Swinging(loaded); i++) { _areaSmokeTime++; SmokeTickHeroRuns(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            require(!Swinging(loaded), "the loaded swing reached its hit");
            require(Hp(loaded) == live, $"the loaded hit left the NPC as the live one did: live {live}, loaded {Hp(loaded)} (before {before})");
            require(loaded.QueryRandom().Draws == liveDraws, $"the loaded hit rolled what the live one did: live {liveDraws} draws, loaded {loaded.QueryRandom().Draws}");
            GD.Print("ARC-DEV-317 native-smoke saved-attack PASS " + System.Text.Json.JsonSerializer.Serialize(new { Npc = entity, Before = before, Live = live, Loaded = Hp(loaded), Draws = loaded.QueryRandom().Draws - draws }));

            // The held-item throw (goal 37) saved in its swing: an item of the pack on the cursor, thrown two cells away; F7 between the swing's start and the throw; the live item lands; F8; the loaded run throws it at
            // the saved time and it lands on the same cell, and the item left the pack once.
            void Frame() { _areaSmokeTime++; SpellSmokeTick(); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            var worn = loaded.QueryEquipment().Worn.Select(w => w.Entity).ToHashSet(StringComparer.Ordinal);
            var refusals = new List<string>();
            // The pre-made hero's pack may be empty: a worn item is lifted off the body as the inventory's drag would (the weapon last).
            var weapon = loaded.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon)?.Entity;
            var item = loaded.QueryPcInventory().Select(i => i.Entity).Where(e => !worn.Contains(e)).Concat(worn.Where(e => e != weapon)).Concat(weapon is null ? [] : [weapon])
                .Where(e => e != SessionHeroGrid.GoldEntity)
                // A map item of the pack (oid:) may carry a get or insert-item script, which the session's ground pickup of a placed item does not run (ItemScriptUnsupported): the pickup steps below take a kit or made item first.
                .OrderBy(e => e.StartsWith("oid:", StringComparison.Ordinal))
                .FirstOrDefault(e => HoldItemOnGameCursor(e, "smoke") is not { } why || Refuse(e, why));
            bool Refuse(string e, string why) { refusals.Add($"{e}: {why}"); return false; }
            require(item is not null, $"an item of the pack goes on the cursor (pack {loaded.QueryPcInventory().Count}, worn {worn.Count}): {string.Join(" | ", refusals.Take(4))}");
            var wornSlot = loaded.QueryEquipment().Worn.FirstOrDefault(w => w.Entity == item)?.Slot;
            var wearable = wornSlot is null ? null : _inventoryHost!.Wearable(loaded, item!, _areaKitWear);
            var grid = (AreaGridSession)_worldActor.Session!;
            var heroAt = grid.Chart.ToWorld(grid.State.CommittedCell);
            var aim = new CellCoord(checked((int)heroAt.X) + 2, checked((int)heroAt.Y));
            require(ThrowGameCursorItem(aim), $"the throw started ({HeldThrowNoticeForSelfTest})");
            bool Throwing(GameSessionOwner owner) => owner.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Any(r => r.Top is { Goal: Arcanum.Simulation.Goals.GoalType.ThrowItem, Node: 2 });
            for (var i = 0; i < 200 && !Throwing(loaded) && HeroRunsHost() is { ThrowRun: not null }; i++) Frame();
            require(Throwing(loaded), "the throw's swing began");
            QuickKey(Key.F7);
            require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved the throwing hero: " + _quickSaveStatus);
            for (var i = 0; i < 1500 && (HeroRunsHost() is { ThrowRun: not null } || _projectiles.Count > 0); i++) Frame();
            CellCoord? Landed(GameSessionOwner owner) => owner.QueryPlacedItems().FirstOrDefault(p => p.Entity == item)?.Cell;
            var liveLanding = Landed(loaded);
            require(liveLanding is not null, "the live throw landed the item");
            QuickKey(Key.F8);
            require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded the throwing hero: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            var thrownSession = _areaSession!;
            require(!ReferenceEquals(thrownSession, loaded) && Throwing(thrownSession) && Landed(thrownSession) is null, "the loaded session holds the swing and the item is still the hero's");
            for (var i = 0; i < 1500 && (HeroRunsHost() is { ThrowRun: not null } || _projectiles.Count > 0); i++) Frame();
            require(Landed(thrownSession) == liveLanding, $"the loaded throw landed the item where the live one did: live {liveLanding}, loaded {Landed(thrownSession)}");
            require(thrownSession.QueryPcInventory().All(i => i.Entity != item) && thrownSession.QueryEquipment().Worn.All(w => w.Entity != item), "the item left the hero once");
            GD.Print("ARC-DEV-317 native-smoke saved-throw PASS " + System.Text.Json.JsonSerializer.Serialize(new { Item = item, Landed = $"{liveLanding}" }));
            // ARC-DEV-319 (MIG14 S9d): the item goes back to the hero through the pickup goal 15 saved at its start: the click, F7, the live pickup, F8 with the use strings forgotten (as a load in a new
            // viewer has none), the loaded run finds the item again by its string, walks onto its cell and takes it once.
            SyncAreaInventory();
            // An item with a row on this map (a pack item of the hero's own record) lies under its row's key, drawn in the ground bodies; one without a row is picked under a placed key.
            var lying = _groundOnly.FirstOrDefault(g => g.Value.Entity == item).Key;
            if (lying == default && _inventoryHost!.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } itemMap && itemMap.TryGetRow(item!, out var itemRow)
                && _groundBodies.ContainsKey(itemRow.Key)) lying = itemRow.Key;
            require(lying != default, "the thrown item lies on the ground as a placed item");
            CancelAreaAttack(null);
            var click = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(lying)));
            require(click.Click == Arcanum.Simulation.Goals.HeroClickGoal.Pickup && click.Outcome == HeroRequestOutcome.Done, $"the click on the item started the pickup ({click.Click} {click.Outcome})");
            QuickKey(Key.F7);
            require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved the hero going to pick up: " + _quickSaveStatus);
            require(thrownSession.Capture().Goals.Runs.Any(r => r.Owner == AreaHeroRunsHost.Hero && r.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.PickupItem), "the save holds the hero's pickup");
            // The item was taken when it left the ground (a kit item goes back into the hero's grid, which the pack query does not list).
            bool Held(GameSessionOwner owner) => owner.QueryPlacedItems().All(p => p.Entity != item);
            for (var i = 0; i < 400 && !Held(thrownSession); i++) Frame();
            require(Held(thrownSession), "the live pickup took the item");
            _useTargets.Clear();
            QuickKey(Key.F8);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            var pickupSession = _areaSession!;
            require(!ReferenceEquals(pickupSession, thrownSession) && !Held(pickupSession) && pickupSession.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Any(r => r.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.PickupItem),
                "the loaded session holds the pickup and the item lies on the ground");
            for (var i = 0; i < 400 && !Held(pickupSession); i++) Frame();
            require(Held(pickupSession) && !pickupSession.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Any(r => r.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.PickupItem), $"the loaded pickup took the item ({_inventoryStatus})");
            thrownSession = pickupSession;
            GD.Print($"ARC-DEV-319 native-smoke saved-pickup PASS item={item}");
            // A pickup that put the item back on the body (an empty slot it fits) leaves nothing to wear again.
            if (wornSlot is { } slot && wearable is not null && thrownSession.QueryEquipment().Worn.All(w => w.Entity != item))
                require(thrownSession.Equip(thrownSession.Version, item!, slot, wearable, (_, _) => true, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, "and worn again");
            SyncAreaInventory();
        }
        finally
        {
            CancelAreaAttack(null);
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
        }
    }
}