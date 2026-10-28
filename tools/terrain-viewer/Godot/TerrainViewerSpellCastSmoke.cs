using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-MAGIC-015, the first cast) with --hero, after the attack stage: the hero learns the first damaging rank-1
    // college spell in its character editor (an experience award for the next level buys the point when it has none, with its
    // auto-level scheme suspended), stands three cells from the living NPC with the fewest hit points, opens the spell pane with M,
    // presses the spell, hovers the NPC (the cast cursor), clicks it, and the casting goal plays its start, release frame and end; the
    // hero's fatigue drops by the spell's cost and the NPC resists or takes damage. Since ARC-MAGIC-017 the goal is timed by the spell's
    // cast eye candy, drawn on the hero: the release comes at its action frame as the rules time it, within one frame of the smoke's
    // clock, and an EyeCandy component the run logs is drawn on its target when its entry plays. ARC-MAGIC-018: the goal's start turns the
    // hero toward the NPC; a spell with a missile launches it at the release frame and its BEGIN comes when it arrives on the NPC's
    // tile, after one leg of the line's steps; when the first spell has no missile the hero also casts (learning it if needed) the first
    // critter spell that has one (its damage, if any, is not asserted). ARC-MAGIC-019: a spell without a missile is clicked twice
    // while its goal plays, the second cast queued beneath the first and played once it ends. A right click closes the cursor. The NPC and the
    // hero's fatigue are restored for the later stages; the learned spell, the experience and the level stay. A map without a living
    // NPC that has combat values reports that and passes nothing.
    private async Task RunAreaCastSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-MAGIC-015 native-smoke cast SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-015 cast smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CAST_SMOKE_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_spellRecords is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var records = _spellRecords!;
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-MAGIC-015 native-smoke cast NO-TARGET map={map.RuntimeMap}"); return; }

        // College spells that damage a critter at BEGIN, need a target and cost fatigue (from the data: no spell is named).
        bool Damaging(SessionSpellRecord r) => r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && r.Data.Enabled
            && r.Areas.Length > 0 && (r.Areas[0].Targets & 0x40) != 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets)
            && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0);
        SessionSpellRecord? Known(Func<SessionSpellRecord, bool> wanted) => session.QueryHeroSpellBook() is { } book
            ? records.FirstOrDefault(r => wanted(r) && r.Data.Number % SpellRecord.Ranks < book.Ranks[r.Data.Number / SpellRecord.Ranks]) : null;
        // The hero knows a spell <paramref name="wanted"/> picks, or learns the first rank-1 one it does not know yet.
        SessionSpellRecord? Learn(Func<SessionSpellRecord, bool> wanted)
        {
            if (Known(wanted) is { } known) return known;
            if (session.QueryHeroExperience() is { UnspentPoints: not > 0 } before)
            {
                // The award must write a level whose points stay unspent: a hero's auto-level scheme (ARC-WORLD-036) would spend them,
                // so the hero has none while the award lands and gets its scheme back after. A third more covers the Hard difficulty's
                // quarter off (ExperienceRules.AdjustForDifficulty).
                var scheme = session.QueryHero()!.Advancement.AutoLevelScheme;
                if (scheme != 0) session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, 0);
                var award = session.AwardHeroExperience(session.Version, Math.Max(2000, (before.ToNextLevel ?? 0) * 4 / 3 + 1));
                if (scheme != 0)
                    Require(session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, scheme).Disposition == GameSessionDisposition.Changed,
                        "the hero gets its auto-level scheme back");
                var after = session.QueryHeroExperience();
                Require(after?.UnspentPoints > 0, $"an experience award buys a point: {award.Disposition} {award.Failure}, scheme {scheme}, "
                    + $"level {before.Level} -> {after?.Level}, experience {before.Experience} -> {after?.Experience}, to next {before.ToNextLevel}");
            }
            var book = session.QueryHeroSpellBook()!;
            var first = records.FirstOrDefault(r => wanted(r) && r.Data.Number % SpellRecord.Ranks == 0 && book.Ranks[r.Data.Number / SpellRecord.Ranks] == 0);
            Require(first is not null, "a rank-1 college spell to learn");
            // Points are spent only in the open character editor (0x5597C0, CP1), as the sheet's + does; the editor closes again
            // unless it was already open.
            var opened = !session.HeroEditorOpen;
            var editor = session.OpenHeroEditor();
            Require(editor.Failure == GameSessionFailure.None, "the hero editor opens: " + editor.Failure);
            var spent = session.SpendHeroPoint(session.Version, AdvancementTrack.College, first!.Data.Number / SpellRecord.Ranks);
            if (opened) session.CloseHeroEditor();
            Require(spent.Disposition == GameSessionDisposition.Changed, "the hero learns the spell: " + spent.Failure + " " + spent.Change?.Status);
            return Known(wanted);
        }
        var spell = Learn(Damaging);
        Require(spell is not null, "the hero knows a damaging spell");

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var target = candidates[0];
        var npc = target.Build.Combat!.Target;
        var npcStart = session.QueryLife(npc).View!.Status.HitPoints;
        var scaled = false;

        // One cast at the NPC through the pane, the cursor and the click, played to its end (and its missile's arrival); the NPC and
        // the hero's fatigue are restored first and the hero turned back, so a second cast finds them as the first did.
        // ARC-MAGIC-019 (SU13): a spell without a missile is clicked twice, the second cast queued beneath the first.
        async Task<object> CastOnce(SessionSpellRecord spell)
        {
            var queue = SpellMissileArt(spell.Data.Number) is null;
            if (session.QueryLife(npc).View?.Status is { } was && (was.HitPoints != npcStart || was.IsDead))
                session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            var heroBefore = session.QueryHeroLife()!.Status;
            var npcBefore = session.QueryLife(npc).View!.Status.HitPoints;
            _worldActor.TurnedFacing = null;

            // M opens the pane with the spell; pressing it opens the cursor (or, for a spell cast at once, would cast).
            InventoryKey(Key.M);
            Require(_spellPane is { Shown: true } && _spellPane.ShownForSelfTest.Any(c => c.Spell == spell.Data.Number), "M shows the pane with the spell: " + _spellNotice);
            _spellPane!.PressForSelfTest(spell.Data.Number);
            Require(_spellTargeting?.Spell == spell.Data.Number, "the spell opened the targeting cursor: " + _spellNotice);

            var point = await HoverPointAsync(this, commands[target.Row.Key]) ?? throw new InvalidOperationException("ARC-MAGIC-015 smoke: target not visible");
            HoverAtForSelfTest(point);
            var hover = HoverActionForSelfTest;
            Require(hover == AreaHoverAction.Cast && _hoverCursor == Godot.Input.CursorShape.Cross, $"cast hover {hover} cursor {_hoverCursor}");

            var casts = _spellCasts;
            var timings = _castTimingsForSelfTest.Count; _eyeCandyDrawnForSelfTest.Clear();
            var logStart = _spellCaster!.Log.Count;
            if (!scaled) { _areaSmokeTime *= 20; scaled = true; }
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            AreaSmokeClick(point);
            Require(_spellCasts == casts + 1, "the click cast: " + _spellNotice);
            Require(_spellGoal is not null, "the casting goal plays: " + _spellNotice);
            var run = _spellGoal!.Run;
            // ARC-MAGIC-018: the facing turn toward the NPC (0x42C850).
            var heroCell = HeroWorldCell()!.Value;
            var npcCell = new CellCoord(checked((int)target.Row.WorldX!.Value), checked((int)target.Row.WorldY!.Value));
            var rotation = LineOfFireRules.Rotation(heroCell, npcCell);
            Require(_worldActor.TurnedFacing?.Octant == DiagnosticGridActor.Octant((NeighborDirection)rotation) && _worldActor.State.Facing == _worldActor.TurnedFacing?.Octant,
                $"the hero turned toward the NPC: rotation {rotation}, turned {_worldActor.TurnedFacing}, drawn {_worldActor.State.Facing}");
            var missileArt = SpellMissileArt(spell.Data.Number);
            var missileEnds = _spellMissileEndsForSelfTest.Count;
            _spellMissilesDrawnForSelfTest.Clear();
            // ARC-MAGIC-019 (SU13): a second click while the goal plays queues a second cast beneath it, which plays once the first ends.
            var queuedStarts = _spellQueuedStarts; var failedAtStart = _spellTargetFailedAtStart;
            int? second = null;
            if (queue)
            {
                AreaSmokeClick(point);
                Require(_spellCasts == casts + 2 && _spellQueue.Count == 1 && _spellGoal?.Run == run, "the second click queued a cast: " + _spellNotice);
                second = _spellQueue[0].Run;
            }
            var frames = 0;
            var revived = false;
            for (; frames < 800 && _spellGoal is not null; frames++)
            {
                // The first cast may kill the NPC (the one with the fewest hit points): it is raised before the queued goal's first
                // frame, so the queued cast plays at a living target.
                if (_spellGoal is { Animation: null } queuedGoal && queuedGoal.Run == second && session.QueryLife(npc).View?.Status is { IsActive: false })
                {
                    session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                    PublishWorldSelection();
                    revived = true;
                }
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(_spellGoal is null && _spellQueue.Count == 0, $"the goals ended after {frames} frames");
            if (second is { } queued)
            {
                Require(_spellQueuedStarts == queuedStarts + 1, "the queued cast's goal started after the first: " + _spellNotice);
                Require(session.QuerySpellRuns()[queued] is null, "the queued instant run is freed");
            }
            // ARC-MAGIC-018: a missile flies on after the goal until it arrives.
            var flying = 0;
            for (; flying < 400 && _spellMissiles.Count > 0; flying++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(_spellMissiles.Count == 0, $"the missile arrived within {flying} frames");
            string missileNote = "none";
            if (missileArt is not null)
            {
                Require(_spellMissileEndsForSelfTest.Count > missileEnds || _spellMissileEndsForSelfTest.Count == 16, "the spell launched a missile");
                var end = _spellMissileEndsForSelfTest[^1];
                var leg = SpellMissileRules.Leg(heroCell, npcCell).Flight;
                Require(end.Run == run && end.End == "arrived" && end.Legs == 1 && end.Steps == (leg?.Samples.Count ?? 0),
                    $"the missile flew one leg of {leg?.Samples.Count} steps to the NPC and arrived: {end}");
                var faced = ProjectileRules.FacedArt(missileArt.Value, ProjectileRules.Direction(heroCell, npcCell));
                Require(faced is { } f && _spellMissilesDrawnForSelfTest.Contains(f), $"the missile was drawn faced toward the NPC: 0x{faced:X8}");
                missileNote = $"{end.Steps} steps, {flying} frames after the goal";
            }
            else Require(_spellMissileEndsForSelfTest.Count == missileEnds, "a spell without a missile art launches none");
            // ARC-MAGIC-017: the cast entry's eye candy timed the goal and was drawn on the hero.
            var castEntry = SpellEyeCandyRules.EntryIndex(spell.Data.Number, SpellEyeCandyRules.Cast);
            var goals = queue ? 2 : 1;
            var untimed = _spellTargetFailedAtStart - failedAtStart;
            Require(_castTimingsForSelfTest.Count == Math.Min(timings + goals, 16) && untimed == 0, $"the goals recorded their eye-candy timing ({untimed} ended before it)");
            var timing = _castTimingsForSelfTest[^1];
            var entry = EyeCandyTable?.At(castEntry);
            Require(entry?.Art is { } && !timing.Immediate, $"the spell's cast eye candy plays: {_spellData?.EyeCandyDetail}, entry art {entry?.Art}");
            Require(_eyeCandyDrawnForSelfTest.Contains(castEntry), "the cast eye candy was drawn on the hero");
            var parts = PartsOf(entry!.Flags);
            var (clip, back, pace) = EyeCandyClips(entry.Art!.Value, parts);
            var expected = new CastEyeCandyAnimation(clip, back, parts.TimesRelease, 0, pace);
            long? expectedRelease = null;
            // ARC-MAGIC-018: a launch waits one pace before the goal's next step (0x4246E0).
            expected.Advance(long.MaxValue / 4, () => { expectedRelease = expected.DueMs; expected.PaceAfterRelease = missileArt is not null; return true; });
            var expectedEnd = expected.DueMs;
            Require(expectedRelease is { } due ? timing.ReleaseMs >= due && timing.ReleaseMs < due + 50 : timing.ReleaseMs == timing.EndMs,
                $"the release came at the eye candy's action frame: {timing.ReleaseMs} ms, expected {expectedRelease} ms (clip {clip})");
            Require(timing.EndMs >= expectedEnd && timing.EndMs < expectedEnd + 50, $"the goal ended after the eye candy: {timing.EndMs} ms, expected {expectedEnd} ms");
            var log = _spellCaster!.Log.Skip(logStart).ToArray();
            foreach (var added in log.Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded))
                if (EyeCandyTable?.At(added.Value) is { Art: { } } shown && EyeCandyAnimation.Plays((shown.Flags & SpellEyeCandyFlags.Animates) != 0, PartsOf(shown.Flags)))
                    Require(_eyeCandyDrawnForSelfTest.Contains(added.Value), $"the component's eye candy {added.Value} was drawn on {added.Object}");
            Require(_spellTargeting is not null, "the cursor stays open after the cast (SU9)");
            var paid = log.Where(e => e.Kind == SpellCastEventKind.FatiguePaid).Sum(e => e.Value);
            var damaged = log.Where(e => e.Kind == SpellCastEventKind.Damaged && e.Object == target.Holder.Entity).Sum(e => e.Value);
            var resisted = log.Any(e => e.Kind == SpellCastEventKind.Resisted && e.Object == target.Holder.Entity);
            var heroAfter = session.QueryHeroLife()!.Status;
            var npcAfter = session.QueryLife(npc).View!.Status.HitPoints;
            Require(paid > 0 && heroAfter.Fatigue < heroBefore.Fatigue, $"the cast cost fatigue: paid {paid}, {heroBefore.Fatigue} -> {heroAfter.Fatigue}");
            Require(log.Count(e => e.Kind == SpellCastEventKind.FatiguePaid) == goals, "each cast paid at its own BEGIN (SU13)");
            Require(!Damaging(spell) || resisted || damaged > 0 && (revived || npcAfter == npcBefore - damaged) || npcAfter <= 0,
                $"the NPC resists or takes the damage: damaged {damaged}, resisted {resisted}, hp {npcBefore} -> {npcAfter}");
            Require(session.QuerySpellRuns()[run] is null, "the instant run is freed");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("M spells", StringComparison.Ordinal), "the status line shows the spell state");

            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point });
            Require(_spellTargeting is null, "a right click closes the cursor");
            InventoryKey(Key.M);
            Require(_spellPane is { Shown: false }, "M hides the pane");
            HoverReleaseForSelfTest();
            return new {
                Spell = spell.Data.Number, Cost = spell.Data.Cost, Frames = frames, Queued = second, Revived = revived, Paid = paid,
                Fatigue = $"{heroBefore.Fatigue}->{heroAfter.Fatigue}", HitPoints = $"{npcBefore}->{npcAfter}", Damaged = damaged, Resisted = resisted,
                Log = string.Join(",", log.Select(e => e.Kind)), Hover = hover.ToString(),
                EyeCandy = $"release {timing.ReleaseMs} end {timing.EndMs} drawn {string.Join("/", _eyeCandyDrawnForSelfTest.Order())}",
                Turn = rotation, Missile = missileNote };
        }

        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (HeroInCombatMode) ToggleCombatMode();
            // The hero stands three cells from the NPC for both casts (placed once: the smoke's clock then runs on).
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 0), (-2, 0), (0, 2), (0, -2) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                try { start = AreaSmokeStandable(map, near, 1); break; } catch (InvalidOperationException) { }
            }
            AreaSmokePlace(map, start ?? AreaSmokeStandable(map, targetCell, 1));
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            if (SpellMissileArt(spell!.Data.Number) is not null)
                GD.Print($"ARC-MAGIC-019 cast smoke: spell {spell.Data.Number} has a missile, so the queued second cast is not checked here");
            var first = await CastOnce(spell!);
            // ARC-MAGIC-018: a hero whose damaging spell has no missile also casts one that has (learned when it knows none).
            object? second = null;
            if (SpellMissileArt(spell!.Data.Number) is null)
            {
                // The installed ones learnable at rank 1 target critters but do no damage (a missile spell is not chosen by name).
                var missile = Learn(r => r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && r.Data.Enabled && r.Areas.Length > 0
                    && (r.Areas[0].Targets & 0x40) != 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets) && SpellMissileArt(r.Data.Number) is not null);
                Require(missile is not null, "the hero knows a critter spell with a missile");
                second = await CastOnce(missile!);
            }
            GD.Print("ARC-MAGIC-015 native-smoke cast PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Cast = first, MissileCast = second,
                Parity = "NOT_VERIFIED (spell UI, eye-candy timing and the missile read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelSpellCast(null); _spellPane?.HidePane();
            // ARC-MAGIC-018: a missile still flying is lost; the later stages see the hero unturned.
            ClearSpellMissiles("the smoke ended"); _worldActor.TurnedFacing = null;
            // The later stages see the map and the hero as they were (the learned spells and the experience stay).
            var npcNow = session.QueryLife(npc).View?.Status;
            if (npcNow is { } now && (now.HitPoints != npcStart || now.IsDead))
                session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            PublishWorldSelection();
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
