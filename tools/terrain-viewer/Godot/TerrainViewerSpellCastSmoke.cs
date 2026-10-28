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
    // hero's fatigue drops by the spell's cost and the NPC resists or takes damage. A right click closes the cursor. The NPC and the
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
        SessionSpellRecord? Known() => session.QueryHeroSpellBook() is { } book
            ? records.FirstOrDefault(r => Damaging(r) && r.Data.Number % SpellRecord.Ranks < book.Ranks[r.Data.Number / SpellRecord.Ranks]) : null;
        var spell = Known();
        if (spell is null)
        {
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
            var first = records.FirstOrDefault(r => Damaging(r) && r.Data.Number % SpellRecord.Ranks == 0 && book.Ranks[r.Data.Number / SpellRecord.Ranks] == 0);
            Require(first is not null, "a damaging rank-1 college spell");
            // Points are spent only in the open character editor (0x5597C0, CP1), as the sheet's + does; the editor closes again
            // unless it was already open.
            var opened = !session.HeroEditorOpen;
            var editor = session.OpenHeroEditor();
            Require(editor.Failure == GameSessionFailure.None, "the hero editor opens: " + editor.Failure);
            var spent = session.SpendHeroPoint(session.Version, AdvancementTrack.College, first!.Data.Number / SpellRecord.Ranks);
            if (opened) session.CloseHeroEditor();
            Require(spent.Disposition == GameSessionDisposition.Changed, "the hero learns the spell: " + spent.Failure + " " + spent.Change?.Status);
            spell = Known();
        }
        Require(spell is not null, "the hero knows a damaging spell");

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var target = candidates[0];
        var npc = target.Build.Combat!.Target;
        var heroBefore = session.QueryHeroLife()!.Status;
        var npcBefore = session.QueryLife(npc).View!.Status.HitPoints;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (HeroInCombatMode) ToggleCombatMode();
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

            // M opens the pane with the spell; pressing it opens the cursor (or, for a spell cast at once, would cast).
            InventoryKey(Key.M);
            Require(_spellPane is { Shown: true } && _spellPane.ShownForSelfTest.Any(c => c.Spell == spell!.Data.Number), "M shows the pane with the spell: " + _spellNotice);
            _spellPane!.PressForSelfTest(spell!.Data.Number);
            Require(_spellTargeting?.Spell == spell.Data.Number, "the spell opened the targeting cursor: " + _spellNotice);

            var point = await HoverPointAsync(this, commands[target.Row.Key]) ?? throw new InvalidOperationException("ARC-MAGIC-015 smoke: target not visible");
            HoverAtForSelfTest(point);
            var hover = HoverActionForSelfTest;
            Require(hover == AreaHoverAction.Cast && _hoverCursor == Godot.Input.CursorShape.Cross, $"cast hover {hover} cursor {_hoverCursor}");

            var casts = _spellCasts;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            AreaSmokeClick(point);
            Require(_spellCasts == casts + 1, "the click cast: " + _spellNotice);
            Require(_spellGoal is not null, "the casting goal plays: " + _spellNotice);
            var run = _spellGoal!.Run;
            // ARC-MAGIC-019 (SU13): a second click while the goal plays queues a second cast beneath it, which plays once the first ends.
            var queuedStarts = _spellQueuedStarts;
            AreaSmokeClick(point);
            Require(_spellCasts == casts + 2 && _spellQueue.Count == 1 && _spellGoal?.Run == run, "the second click queued a cast: " + _spellNotice);
            var second = _spellQueue[0].Run;
            var frames = 0;
            for (; frames < 800 && _spellGoal is not null; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(_spellGoal is null && _spellQueue.Count == 0, $"both goals ended after {frames} frames");
            Require(_spellQueuedStarts == queuedStarts + 1, "the queued cast's goal started after the first: " + _spellNotice);
            Require(session.QuerySpellRuns()[second] is null, "the queued instant run is freed");
            Require(_spellTargeting is not null, "the cursor stays open after the cast (SU9)");
            var log = _spellCaster!.Log;
            var paid = log.Where(e => e.Kind == SpellCastEventKind.FatiguePaid).Sum(e => e.Value);
            var damaged = log.Where(e => e.Kind == SpellCastEventKind.Damaged && e.Object == target.Holder.Entity).Sum(e => e.Value);
            var resisted = log.Any(e => e.Kind == SpellCastEventKind.Resisted && e.Object == target.Holder.Entity);
            var heroAfter = session.QueryHeroLife()!.Status;
            var npcAfter = session.QueryLife(npc).View!.Status.HitPoints;
            Require(paid > 0 && heroAfter.Fatigue < heroBefore.Fatigue, $"the cast cost fatigue: paid {paid}, {heroBefore.Fatigue} -> {heroAfter.Fatigue}");
            Require(resisted || damaged > 0 && npcAfter == npcBefore - damaged || npcAfter <= 0,
                $"the NPC resists or takes the damage: damaged {damaged}, resisted {resisted}, hp {npcBefore} -> {npcAfter}");
            Require(session.QuerySpellRuns()[run] is null, "the instant run is freed");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("M spells", StringComparison.Ordinal), "the status line shows the spell state");

            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point });
            Require(_spellTargeting is null, "a right click closes the cursor");
            InventoryKey(Key.M);
            Require(_spellPane is { Shown: false }, "M hides the pane");
            HoverReleaseForSelfTest();
            GD.Print("ARC-MAGIC-015 native-smoke cast PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Spell = spell.Data.Number, Cost = spell.Data.Cost, Target = target.Row.PrototypeNumber, Frames = frames, Queued = second, Paid = paid,
                Fatigue = $"{heroBefore.Fatigue}->{heroAfter.Fatigue}", HitPoints = $"{npcBefore}->{npcAfter}", Damaged = damaged, Resisted = resisted,
                Log = string.Join(",", log.Select(e => e.Kind)), Hover = hover.ToString(),
                Parity = "NOT_VERIFIED (spell UI read in the original executable; no original-runtime run; release frame timing is a stand-in)" }));
        }
        finally
        {
            CancelSpellCast(null); _spellPane?.HidePane();
            // The later stages see the map and the hero as they were (the learned spell and the experience stay).
            var npcNow = session.QueryLife(npc).View?.Status;
            if (npcNow is { } now && (now.HitPoints != npcBefore || now.IsDead))
                session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            PublishWorldSelection();
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
