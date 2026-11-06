using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    // hero's fatigue are restored for the later stages; the learned spell, the experience, the level and (ARC-UI-016) the kit weapon's spent spell charge stay. A map without a living
    // NPC that has combat values reports that and passes nothing.
    // ARC-UI-031: before the first cast the cast host knows every item the map's NPCs hold (that reads as an item) as its NPC's, placed
    // at its holder with the inventory flag; the first cast's own life steps fire through the host's item triggers (recorded for the
    // cast), every NPC item they walk known as its holder's, and a damage dealt with worn items on either side walks at least one.
    // ARC-UI-016: then the weapon's spell pane. No kit weapon carries castable spells, so the stage lends the hero's wielded kit weapon
    // the spell facts of the first weapon prototype (in number order, from the data) that has a castable critter spell needing a
    // target that need not be damaged, marked identified (the session does not model identification yet): M shows the weapon button, it opens the weapon's pane,
    // the slot opens the cursor with the weapon as the source, a click casts at the NPC and the weapon pays a charge instead of the
    // hero's fatigue; unwielding the weapon returns the pane to the spell list. A hero without a weapon reports that and skips it.
    // ARC-DEV-139 (MIG13 S8): a missile is a goal run on the session's animation clock, which a native smoke does not tick by itself: the missile loops drive the clock as the AI and portal smokes do (a frame is 50
    // host milliseconds).
    private long _spellSmokeHostMs;

    // A native smoke does not tick the animation clock; the runs on it (a spell missile, since ARC-DEV-140 a projectile) need it.
    private void SpellSmokeTick()
    {
        if (_spellSmokeHostMs == 0) { _spellSmokeHostMs = 80_000_000L; TickAreaClock(_spellSmokeHostMs); }
        TickAreaClock(_spellSmokeHostMs += 50);
    }

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
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
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
        // ARC-DEV-133: the search places the hero, and the safety gate opens only under focus. The smoke owns the focus from here on, as every
        // smoke does before its first placement, not the window's own, which a run behind another window does not have.
        _worldFocused = true; SelectSimulatedPadForSelfTest();
        // ARC-DEV-089: the first NPC (weakest first) the hero can stand three cells from and see, with the hero standing there.
        var target = candidates[0]; CellCoord? castStart = null;
        foreach (var candidate in candidates.Take(24))
        {
            var candidateCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
            if (await AreaSmokeFightableStartAsync(map, commands[candidate.Row.Key], candidateCell, 3, 2, false) is not { } found) continue;
            target = candidate; castStart = found; break;
        }
        if (castStart is null)
        {
            GD.Print($"ARC-MAGIC-015 native-smoke cast NO-TARGET map={map.RuntimeMap} (no NPC the hero can stand near and see)");
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; RestoreWorldTestTime(testTime); return;
        }
        var npc = target.Build.Combat!.Target;
        var npcStart = session.QueryLife(npc).View!.Status.HitPoints;
        var scaled = false;
        var lastPressed = -1;

        // One cast at the NPC through the pane, the cursor and the click, played to its end (and its missile's arrival); the NPC and
        // the hero's fatigue are restored first and the hero turned back, so a second cast finds them as the first did.
        // ARC-MAGIC-019 (SU13): a spell without a missile is clicked twice, the second cast queued beneath the first.
        async Task<object> CastOnce(SessionSpellRecord spell)
        {
            lastPressed = spell.Data.Number;
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
            // ARC-DEV-110 (MIG16 S3): the cursor is interface mode 1 on the stack, over the base, and the cursor mode reads it.
            Require(_interfaceWindows.Stack.Current == InterfaceMode.SpellTarget && _interfaceWindows.Stack.Depth == 2 && TargetsMatchStack && !AnyWindowOpen
                && CursorMode == AreaCursorMode.SpellTarget, $"the targeting cursor is mode 1 on the stack: {StackText()}");
            // ARC-UI-056: the targeting cursor shows spellcur.art, stepping its frames at its rate (CU1, CU5).
            SyncCursorArt(); SetCursorClockForSelfTest(1_000, restart: true); SyncCursorArt();
            Require(CursorShownForSelfTest == (AreaCursorArt.Spell, 0), $"the spell cursor's first frame: {CursorShownForSelfTest}");
            SetCursorClockForSelfTest(1_100); SyncCursorArt();
            Require(CursorShownForSelfTest == (AreaCursorArt.Spell, 1), $"the spell cursor's second frame after 100 ms: {CursorShownForSelfTest}");
            SetCursorClockForSelfTest(null);

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
            // The targeting cursor stays open after a cast (SU9), and so does its art.
            SyncCursorArt();
            Require(_spellTargeting is not null && CursorShownForSelfTest?.Art == AreaCursorArt.Spell, $"the spell cursor stays after the cast: {CursorShownForSelfTest}");
            Require(TargetModeOpen(InterfaceMode.SpellTarget) && TargetsMatchStack, $"mode 1 stays on the stack after the cast: {StackText()}");
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
                SpellSmokeTick();
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
                if (EyeCandyTable?.At(added.Value) is { Art: { } } shown && SpellEyeCandyRules.Plays((shown.Flags & SpellEyeCandyFlags.Animates) != 0, PartsOf(shown.Flags)))
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
            Require(_spellTargeting is null && !TargetModeOpen(InterfaceMode.SpellTarget) && _interfaceWindows.Stack.Depth == 1 && TargetsMatchStack,
                $"a right click closes the cursor and leaves mode 1: {StackText()}");
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

        // ARC-UI-016: the weapon's spell pane, cast at the NPC through it with the weapon as the source.
        async Task<object?> WeaponPaneCastAsync()
        {
            if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is not { Item.Weapon: not null } wielded
                || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
            { GD.Print("ARC-UI-016 native-smoke item-pane SKIP (the hero wields no kit weapon)"); return null; }
            bool Castable(int value) => value is >= 0 and < SpellRecord.Count && records[value] is { } r && !r.Data.IsTechnology && r.Data.ItemTriggers == 0
                && r.Data.Enabled && r.Areas.Length > 0 && (r.Areas[0].Targets & 0x40) != 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets)
                && !SpellUiRules.TargetsInventory(r.Areas[0].Targets) && (r.Areas[0].Targets & SpellUiRules.DamagedBit) == 0;
            var lent = LentWeaponSpells(wielded.Entity, Castable);
            Require(lent is not null, "a weapon prototype with a castable critter spell: " + _lentItemSpellsDetail);
            _lentItemSpellsForSelfTest = lent; _weaponPaneCache = null;
            try
            {
                var slot = Enumerable.Range(0, lent!.Spells.Count).First(i => Castable(lent.Spells[i]));
                var spellNumber = lent.Spells[slot];
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
                if (session.QueryLife(npc).View?.Status is { } was && (was.HitPoints != npcStart || was.IsDead))
                    session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                var fatigue = session.QueryHeroLife()!.Status.Fatigue;
                var charges = SpellCasterNow()!.Item(wielded.Entity)!.Charges;
                InventoryKey(Key.M);
                Require(_spellPane?.WeaponShownForSelfTest is { ButtonShown: true, Open: false } shut && shut.Item == wielded.Entity,
                    "M shows the weapon button: " + _spellPane?.WeaponShownForSelfTest);
                _spellPane!.PressWeaponForSelfTest();
                var pane = _spellPane.WeaponShownForSelfTest;
                Require(pane is { Open: true } && pane.Buttons.Any(b => b.Slot == slot && b.Spell == spellNumber && b.Activates),
                    $"the weapon button opens the weapon's pane with slot {slot}: {pane?.Gauge} [{string.Join(",", pane?.Buttons.Select(b => $"{b.Slot}:{b.Spell}:{b.Activates}") ?? [])}]");
                _spellPane.PressItemForSelfTest(slot);
                Require(_spellTargeting is { Item: { } source } && source == wielded.Entity && _spellTargeting.Spell == spellNumber,
                    "the slot opened the cursor with the weapon as the source: " + _spellNotice);
                var point = await HoverPointAsync(this, commands[target.Row.Key]) ?? throw new InvalidOperationException("ARC-UI-016 smoke: target not visible");
                HoverAtForSelfTest(point);
                var logStart = _spellCaster!.Log.Count;
                var casts = _spellCasts;
                AreaSmokeClick(point);
                Require(_spellCasts == casts + 1 && _spellGoal is not null, "the click cast from the weapon: " + _spellNotice);
                var frames = 0;
                for (; frames < 800 && (_spellGoal is not null || _spellMissiles.Count > 0); frames++)
                {
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_spellGoal is null && _spellMissiles.Count == 0, $"the weapon's cast ended after {frames} frames");
                var log = _spellCaster!.Log.Skip(logStart).ToArray();
                var left = SpellCasterNow()!.Item(wielded.Entity)!.Charges;
                Require(charges < 0 ? left == charges : left == charges - 1, $"the weapon paid a charge: {charges} -> {left}");
                Require(charges < 0 || log.Any(e => e.Kind == SpellCastEventKind.ChargeSpent && e.Object == wielded.Entity), "the log shows the weapon's charge");
                Require(log.All(e => e.Kind != SpellCastEventKind.FatiguePaid) && session.QueryHeroLife()!.Status.Fatigue == fatigue,
                    "the hero paid no fatigue for the weapon's spell");
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point });
                Require(_spellTargeting is null, "a right click closes the weapon's cursor");
                // ARC-UI-022: the weapon's spell on entry 1 (key 2): the wielded weapon goes straight to the activation (0x57C080).
                Require(session.AssignHotkey(session.Version, 1, HotkeyEntry.ForItemSpell(wielded.Entity, spellNumber)).Disposition == GameSessionDisposition.Changed,
                    "the weapon's spell is put on entry 1");
                HotkeyKey(Key.Key2);
                Require(_spellTargeting is { Item: { } hotkeySource } && hotkeySource == wielded.Entity && _spellTargeting.Spell == spellNumber,
                    "the key 2 opened the weapon's cursor: " + _hotkeyNotice);
                CancelSpellTargeting(null);
                Require(session.ClearHotkey(session.Version, 1).Disposition == GameSessionDisposition.Changed, "entry 1 emptied");
                // Unwielding the weapon returns the bar to the spell list (0x5570D0), wielding it again shows the button (0x556EF0).
                // The weapon's run still has it as the source (a run with a duration): its moves need the trigger host (ARC-MAGIC-020).
                var host = new SessionItemTriggers(SpellCasterNow()!, CombatWorldOrNull() ?? NoSpellCombat.Instance);
                var off = session.Unequip(session.Version, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted, triggers: host);
                Require(off.Disposition == GameSessionDisposition.Changed, $"the weapon is taken off: {off.Failure} {off.Refusal}");
                ProcessAreaSpells();
                Require(_itemPaneItem is null && _spellPane.WeaponShownForSelfTest is null, "the pane left the weapon's spells when it was taken off");
                Require(session.Equip(session.Version, wielded.Entity, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, new(kit.Kind, [], false, wielded.Item),
                    (_, _) => true, _ => SessionInsertVerdict.Accepted, triggers: host).Disposition == GameSessionDisposition.Changed, "the weapon is wielded again");
                ProcessAreaSpells();
                Require(_spellPane.WeaponShownForSelfTest is { ButtonShown: true, Open: false }, "the weapon button is back, the pane on the spell list");
                InventoryKey(Key.M);
                Require(_spellPane is { Shown: false }, "M hides the pane");
                HoverReleaseForSelfTest();
                GD.Print($"ARC-UI-016 native-smoke item-pane PASS weapon={wielded.Entity} lent={_lentItemSpellsDetail} slot={slot} spell={spellNumber} "
                    + $"charges={charges}->{left} frames={frames} log={string.Join(",", log.Select(e => e.Kind))}");
                return new { Lent = _lentItemSpellsDetail, Slot = slot, Spell = spellNumber, Charges = $"{charges}->{left}", Frames = frames };
            }
            finally
            {
                // The later stages wear and take off the kit weapon without a trigger host: no run keeps it as its source (its saved
                // spent charge stays, and without the lent facts it shows no weapon button).
                if (SpellCasterNow() is { } caster)
                    foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Source?.Id == wielded.Entity).Select(r => r.Id).ToArray())
                    {
                        caster.Act(new(SessionSpellRunActionKind.Free, run));
                        // Its component eye candy (drawn on the NPC until the run ends) goes with it.
                        _eyeCandies.RemoveAll(e => e.Run == run);
                        _eyeCandyHost?.EndTagged(run);
                    }
                Require(session.QuerySpellRuns().Runs.All(r => r.Source?.Id != wielded.Entity), "no run keeps the weapon as its source");
                _lentItemSpellsForSelfTest = null; _itemPaneItem = null; _weaponPaneCache = null;
            }
        }

        try
        {
            if (HeroInCombatMode) ToggleCombatMode();
            // The hero stands where the search saw the NPC from (three cells off) for both casts (placed once: the smoke's clock then runs on).
            AreaSmokePlace(map, castStart!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            if (SpellMissileArt(spell!.Data.Number) is not null)
                GD.Print($"ARC-MAGIC-019 cast smoke: spell {spell.Data.Number} has a missile, so the queued second cast is not checked here");
            Require(SpellCasterNow() is { ItemTriggers: not null }, "the map's cast host is built with its item triggers");
            var caster = _spellCaster!;
            var lookup = NpcItemLookupCheck(session, caster, map.RuntimeMap);
            var recorder = new RecordingItemTriggers(caster.ItemTriggers!, caster);
            caster.ItemTriggers = recorder;
            var heroWorn = session.QueryEquipment().Worn.Count;
            var npcWorn = _spellCastMap!.WornItems(target.Holder.Entity).Count;
            var triggersLog = caster.Log.Count;
            object first;
            try { first = await CastOnce(spell!); }
            finally { caster.ItemTriggers = recorder.Inner; }
            Require(ReferenceEquals(_spellCaster, caster), "the cast host was not rebuilt during the first cast");
            var damage = caster.Log.Skip(triggersLog).Where(e => e.Kind == SpellCastEventKind.Damaged && e.Object == target.Holder.Entity).Sum(e => e.Value);
            var npcCalls = recorder.Calls.Where(c => c.Holder != SessionSpellCaster.Hero).ToArray();
            Require(npcCalls.All(c => c.Holder == target.Holder.Entity && c.Known == c.Holder),
                "every NPC item the cast's damage step walked is the target's and known as its: " + string.Join(";", npcCalls.Select(c => $"{c.Item}@{c.Holder}={c.Known}")));
            Require(damage <= 0 || heroWorn + npcWorn == 0 || recorder.Calls.Count > 0,
                $"a damage of {damage} with {heroWorn} hero and {npcWorn} NPC worn items walked them through the item triggers");
            GD.Print($"ARC-UI-031 native-smoke item-triggers PASS map={map.RuntimeMap} {lookup} damage={damage} hero-worn={heroWorn} npc-worn={npcWorn} "
                + $"calls={recorder.Calls.Count} npc-calls={npcCalls.Length} started={recorder.Calls.Sum(c => c.Started)}");
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
            var weaponCast = await WeaponPaneCastAsync();
            // ARC-UI-022: the pane's last press is the latest recent action; Ctrl+1 puts it on entry 0, the keys 1 and A activate it, Ctrl+Shift+1
            // empties the entry and an empty entry does nothing.
            var latest = session.QueryHotkeys().Recent[0];
            Require(latest.Kind == HotkeyKind.Spell && latest.Index == lastPressed, $"the pane's press is the latest recent action: {latest} (pressed {lastPressed})");
            CancelSpellTargeting(null);
            HotkeyKey(Key.Key1, ctrl: true);
            Require(session.QueryHotkeys().Entries[0] == latest, "Ctrl+1 put the latest recent action on entry 0: " + _hotkeyNotice);
            var activations = _hotkeyActivations;
            HotkeyKey(Key.Key1);
            Require(_spellTargeting?.Spell == latest.Index && _hotkeyActivations == activations + 1, "the key 1 opened the spell's cursor: " + _hotkeyNotice);
            CancelSpellTargeting(null);
            HotkeyKey(Key.A);
            Require(_spellTargeting?.Spell == latest.Index && _hotkeyActivations == activations + 2, "A opened the latest action's cursor: " + _hotkeyNotice);
            CancelSpellTargeting(null);
            HotkeyKey(Key.Key1, ctrl: true, shift: true);
            Require(session.QueryHotkeys().Entries[0].IsEmpty, "Ctrl+Shift+1 emptied entry 0: " + _hotkeyNotice);
            HotkeyKey(Key.Key1);
            Require(_spellTargeting is null && _hotkeyActivations == activations + 2, "an empty entry does nothing: " + _hotkeyNotice);
            GD.Print($"ARC-UI-022 native-smoke hotkeys PASS spell={latest.Index} weapon-entry={(weaponCast is null ? "skipped" : "activated")} activations={_hotkeyActivations - activations}");
            // ARC-MAGIC-026: the top strip's maintain bar shows a maintained spell and a click on its slot ends it.
            await RunAreaMaintainBarSmokeAsync(session, target.Holder.Entity);
            // ARC-UI-027: the bar's art, its clicks and its right-button drag.
            await RunAreaHotkeyBarSmokeAsync(session, latest.Index);
            GD.Print("ARC-MAGIC-015 native-smoke cast PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Cast = first, MissileCast = second, WeaponCast = weaponCast,
                Parity = "NOT_VERIFIED (spell UI, eye-candy timing and the missile read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelSpellCast(null); _spellPane?.HidePane(); _lentItemSpellsForSelfTest = null; _itemPaneItem = null; SetCursorClockForSelfTest(null);
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

    // ARC-UI-031 smoke: every item an NPC of the map holds that reads as an item is the cast host's as its NPC's, at the NPC's location
    // with the inventory flag (when the NPC is a map object); throws with the first that is not.
    private string NpcItemLookupCheck(GameSessionOwner session, SessionSpellCaster caster, int runtimeMap)
    {
        int holders = 0, items = 0, placed = 0;
        foreach (var holder in session.QueryMapInventory(new(runtimeMap))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc))
        {
            holders++;
            var at = caster.Facts(holder.Entity)?.Location;
            foreach (var item in holder.Items.Where(i => _combatHost!.Fields(runtimeMap, i.Entity)?.Item is not null && !session.IsItemDestroyed(i.Entity)))
            {
                items++;
                if (caster.Item(item.Entity)?.Holder is var known && known != holder.Entity)
                {
                    GD.Print($"ARC-UI-031 cast smoke FAIL: item {item.Entity} of {holder.Entity} is known as {known ?? "nothing"}");
                    throw new InvalidOperationException("AREA_CAST_SMOKE_FAILED");
                }
                if (at is not { } location) continue;
                var facts = caster.Facts(item.Entity);
                if (facts?.Location != location || (facts.ObjectFlags & SpellTargetObject.Inventory) == 0)
                {
                    GD.Print($"ARC-UI-031 cast smoke FAIL: item {item.Entity} of {holder.Entity} stands at {facts?.Location} flags {facts?.ObjectFlags}, its holder at {location}");
                    throw new InvalidOperationException("AREA_CAST_SMOKE_FAILED");
                }
                placed++;
            }
        }
        return $"npcs={holders} npc-items={items} placed={placed}";
    }

    // ARC-UI-031 smoke: the cast host's item triggers with each executor call recorded (its item, holder, the holder the cast host knows
    // the item as, and the runs it started).
    private sealed class RecordingItemTriggers(ISessionItemTriggers inner, SessionSpellCaster caster) : ISessionItemTriggers
    {
        public ISessionItemTriggers Inner { get; } = inner;
        public List<(string Item, string Holder, string? Known, int Started)> Calls { get; } = [];
        public bool Supports(string item) => Inner.Supports(item);
        public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default, string? runOther = null)
        {
            var known = caster.Item(item)?.Holder;
            var result = Inner.Fire(eventMask, item, holder, other, tile, runOther);
            Calls.Add((item, holder, known, result.Spells.Count(spell => spell.Outcome == SessionItemSpellOutcome.Started)));
            return result;
        }
        public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder) => Inner.Withdraw(eventMask, item, holder);
        public SessionItemTriggerResult Remove(string item, string holder) => Inner.Remove(item, holder);
    }

    // ARC-UI-022 smoke: a key down and up through the WORLD keyboard route, with Ctrl or Shift held.
    private void HotkeyKey(Key key, bool ctrl = false, bool shift = false)
    {
        var unicode = key == Key.A ? 'a' : 0;
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, CtrlPressed = ctrl, ShiftPressed = shift, Pressed = true });
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, CtrlPressed = ctrl, ShiftPressed = shift, Pressed = false });
    }

    // ARC-UI-016 smoke: spell facts lent to the hero's wielded weapon (see the cast smoke) and where they came from.
    private SessionItemSpells? _lentItemSpellsForSelfTest;
    private string _lentItemSpellsDetail = "none";

    /// <summary>The spell facts of the first weapon prototype (in number order) with a positive complexity, charges and a slot
    /// <paramref name="castable"/> accepts, under <paramref name="entity"/> and marked identified; null when none reads.</summary>
    private SessionItemSpells? LentWeaponSpells(string entity, Func<int, bool> castable)
    {
        if (_arguments?.ModulePath is not { } module) { _lentItemSpellsDetail = "no module"; return null; }
        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) { _lentItemSpellsDetail = "content root " + found.Status; return null; }
        var opened = Arcanum.Data.Content.ContentStack.Open(found.Plan!);
        if (opened.Stack is not { } stack) { _lentItemSpellsDetail = "stack " + opened.Status; return null; }
        using (stack)
        {
            var prototypes = Arcanum.Data.Content.ContentPrototypes.Open(stack);
            foreach (var number in prototypes.Numbers.Order())
            {
                if (Arcanum.Data.Combat.CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is not { Item: { } item, Weapon: not null } fields
                    || item.MagickTechComplexity <= 0 || item.SpellManaStore == 0 || !item.Spells.Any(castable)) continue;
                _lentItemSpellsDetail = $"prototype {number}";
                return SessionItemSpells.FromCombatFields(entity, SessionSpellCaster.Hero, fields)! with { Flags = item.Flags | ItemSpellPaneRules.IdentifiedFlag };
            }
        }
        _lentItemSpellsDetail = "no weapon prototype";
        return null;
    }
}
