using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-021 native observation (map mode with --hero, the "floaters" stage alone): at "text floaters" 2 the hero, standing next to
// the living NPC with the most hit points, makes a critical hit on it (a granted critical-hit fate): its floaters carry the texts
// of the player's mes\combat.mes, the critical-hit message first, stacked one line height apart from 4 line heights, the damage line
// in white (an NPC of nobody's party); then the NPC attacks the hero until one attack lands or is dodged: a hit floats the damage in
// red over the hero, a dodge white. Stepped by the smoke's clock, every line rises by the speed + 1 and its drawn opacity is the
// fade's; at level 1 a hit makes no number. The stage leaves the NPC hurt. ARC_AREA_FLOATERS_CAPTURE saves in-app readbacks.
public partial class TerrainViewerApp
{
    private async Task RunAreaFloatersSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } host || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-UI-021 native-smoke floaters NONE map={_readyMapName} (needs a hero and combat values)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-021 floaters smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_FLOATERS_SMOKE_FAILED");
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_FLOATERS_CAPTURE");
        async Task Capture(string name)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
        }
        var mapId = new SessionMapId(CurrentAreaMap);
        var candidates = session.QueryMapInventory(mapId)?.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (h.Entity, Combat: host.Build(session, CurrentAreaMap, h.Entity).Combat))
            .Where(c => c.Combat is not null && session.QueryLife(c.Combat.Target).View?.Status is { IsDead: false })
            .OrderByDescending(c => LifeRules.MaxHitPoints(c.Combat!.Facts, 0)).ThenBy(c => c.Entity, StringComparer.Ordinal).ToArray() ?? [];
        if (candidates.Length == 0) { GD.Print($"ARC-UI-021 native-smoke floaters NONE map={_readyMapName} (no living NPC with combat values)"); return; }
        var (entity, combat) = candidates[0];
        var settings = _gameSettings;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _floaterClockForSelfTest = true;
        try
        {
            _worldFocused = true;
            _gameSettings = settings with { Floats = FloaterRules.Numbers, FloatSpeed = FloaterRules.DefaultSpeed };
            var texts = CombatTexts();
            Require(texts.FromGame && texts.GameCount == AreaCombatTexts.Count, "the texts are the player's mes\\combat.mes: " + texts.Detail);
            var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
            var row = _inventoryHost!.Maps.Single(m => m.RuntimeMap == CurrentAreaMap).Row(entity);
            var npcCell = LiveWorldCell(row);
            AreaSmokePlace(map, AreaSmokeStandable(map, new(checked((int)(npcCell.X - grid.Chart.OriginX)), checked((int)(npcCell.Y - grid.Chart.OriginY))), 2));
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            for (var i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // The hero's critical hit on the NPC.
            Require(session.GrantHeroFatePoint(session.Version).Disposition == GameSessionDisposition.Changed, "a fate point granted");
            Require(session.ArmHeroFate(session.Version, FateKind.CriticalHit).Disposition == GameSessionDisposition.Changed, "the critical-hit fate armed");
            var result = session.AttackCritter(session.Version, new(combat!)
            {
                HeroWorn = AreaCombatHost.HeroWorn(session, _areaKitDurability), TargetWorn = host.WornItems(session, CurrentAreaMap, entity),
                TargetCell = new(checked((int)npcCell.X), checked((int)npcCell.Y)), DropMap = AreaDropMapNow(),
            });
            Require(result.Disposition == GameSessionDisposition.Changed && result.Resolution?.Outcome == AttackOutcome.CriticalHit,
                $"a critical hit ({result.Disposition} {result.Failure} {result.Resolution?.Outcome})");
            if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned)) EndStun(session, combat!.Target, entity);
            // A clean field: lines and a clock of earlier stages would move these.
            if (_floaters is { } earlier) { earlier.Field.Clear(); _floaterCells.Clear(); }
            FloatCritterAttack(result, HeroFloater, entity);
            var layer = _floaters;
            Require(layer is not null, "the floater layer exists");
            var h = layer!.LineHeight;
            var lines = layer.Field.Lines(entity).ToArray();
            var expected = CombatFloaters.Of(AreaCombatFloaters.Of(result, FloaterSide.Pc, FloaterSide.Other)!, FloaterRules.Numbers, texts.Text)
                .Where(f => f.Over == FloaterOver.Struck).ToArray();
            Require(lines.Select(l => (l.Text, l.Color)).SequenceEqual(expected.Select(f => (f.Text, f.Color))),
                $"the NPC's lines {string.Join("|", lines.Select(l => l.Text))} are the rules' {string.Join("|", expected.Select(f => f.Text))}");
            Require(lines[0].Text == texts.Text(CombatFloaters.CriticalHitLine) && lines[0].Color == FloaterColor.White, "the critical hit message first, in white");
            var damage = result.Life!.HitPointDamage.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + texts.Text(CombatFloaters.HitPointsLine);
            Require(lines.Any(l => l.Text.StartsWith(damage, StringComparison.Ordinal) && l.Color == FloaterColor.White), $"the damage line '{damage}' in white");
            Require(lines.Select((l, i) => l.Offset == (FloaterRules.StartLines + i) * h).All(ok => ok), $"stacked from 4h one height apart (h={h}): "
                + string.Join(",", lines.Select(l => l.Offset)));

            // The smoke's clock: every 50 ms the lines rise 3 px; the drawn opacity is the fade's.
            var before = lines.Select(l => l.Offset).ToArray();
            for (var step = 0; step < 12; step++) layer.Field.Advance(100_000 + step * FloaterRules.StepMs, FloaterRules.DefaultSpeed);
            Require(layer.Field.Lines(entity).Select(l => l.Offset).SequenceEqual(before.Select(o => o - 12 * FloaterRules.Step(FloaterRules.DefaultSpeed))), "12 steps of 3 px");
            layer.Field.Advance(100_000 + 12 * FloaterRules.StepMs - 1, FloaterRules.DefaultSpeed);
            Require(layer.Field.Lines(entity)[0].Offset == before[0] - 36, "no step before 50 ms");
            layer.Level = _gameSettings.Floats;
            var drawn = layer.DrawnForSelfTest().Where(d => d.Owner == entity).ToArray();
            var visible = layer.Field.Lines(entity).Where(l => FloaterRules.Alpha(l.Offset, h) > 0).ToArray();
            Require(drawn.Length > 0 && drawn.Select(d => d.Alpha).SequenceEqual(visible.Select(l => FloaterRules.Alpha(l.Offset, h))),
                "the drawn lines carry the fade's opacity: " + string.Join(",", drawn.Select(d => d.Alpha)));
            Require(drawn.Zip(visible).All(pair => (int)pair.First.Position.Y - (int)drawn[0].Position.Y == pair.Second.Offset - visible[0].Offset),
                "each line stands at its offset in the box");
            var view = GetViewport().GetVisibleRect();
            Require(drawn.All(d => view.HasPoint(d.Position)), "the NPC's lines are on screen: " + string.Join(",", drawn.Select(d => d.Position)));
            layer.Redraw();
            await Capture("critical");

            // The NPC attacks the hero until a hit lands or is dodged: red damage over the hero, or a white dodge.
            var heroLines = 0;
            SessionAttackResult? landed = null; string refusedAttack = "";
            for (var tries = 0; tries < 40 && landed is null; tries++)
            {
                var attack = session.AttackByNpc(session.Version, new SessionNpcAttack(combat!) { TargetWorn = AreaCombatHost.HeroWorn(session, _areaKitDurability) });
                if (attack.Disposition == GameSessionDisposition.Rejected) { refusedAttack = $"rejected try={tries} failure={attack.Failure} npcLife={session.QueryLife(combat!.Target).View?.Status} hero={session.QueryHeroLife()?.Status}"; break; }
                refusedAttack = $"last try={tries} disposition={attack.Disposition} failure={attack.Failure} outcome={attack.Resolution?.Outcome} cause={attack.Resolution?.MissCause} hero={session.QueryHeroLife()?.Status} npc={session.QueryLife(combat!.Target).View?.Status}";
                if (attack.Resolution?.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit
                    || attack.Resolution is { Outcome: AttackOutcome.Miss, MissCause: AttackMissCause.Dodged }) landed = attack;
            }
            Require(landed is not null, "an NPC attack on the hero landed or was dodged " + refusedAttack);
            if (landed!.Life is { } stun && stun.Events.HasFlag(LifeEvents.Stunned)) HeroStunned(session);
            FloatCritterAttack(landed, entity, HeroFloater);
            var hero = layer.Field.Lines(HeroFloater);
            heroLines = hero.Count;
            if (landed.Resolution!.Outcome == AttackOutcome.Miss)
                Require(hero.Count == 1 && hero[0].Text == texts.Text(CombatFloaters.DodgeLine) && hero[0].Color == FloaterColor.White, "a white dodge over the hero");
            else
            {
                var heroExpected = CombatFloaters.Of(AreaCombatFloaters.Of(landed, FloaterSide.Other, FloaterSide.Pc)!, FloaterRules.Numbers, texts.Text)
                    .Where(f => f.Over == FloaterOver.Struck).ToArray();
                Require(hero.Select(l => (l.Text, l.Color)).SequenceEqual(heroExpected.Select(f => (f.Text, f.Color))) && hero.Count > 0
                    && hero.Where(l => l.Color != FloaterColor.Green).All(l => l.Color == FloaterColor.Red), "the hero's lines in red: "
                    + string.Join("|", hero.Select(l => $"{l.Color}:{l.Text}")));
            }
            for (var step = 0; step < 8; step++) layer.Field.Advance(200_000 + step * FloaterRules.StepMs, FloaterRules.DefaultSpeed);
            layer.Redraw();
            await Capture("hero");

            // ARC-DEV-296 (B42): damage the cast host applies that is no weapon swing (here a script's damage on the NPC) floats the packet's lines over it.
            // The spell data loads in the background: the cast host is built once it is there.
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var caster = SpellCasterNow();
            Require(caster is not null, "the cast host exists");
            caster!.TakeDamageSteps(); caster.TakeExperienceAwards();
            var linesBefore = layer.Field.Lines(entity).Count;
            var scripted = caster.ApplyScriptDamage(entity, 2, 0, unresistable: true);
            Require(scripted.Applied && scripted.Step is not null, $"the script's damage applied ({scripted.Applied} {scripted.Vetoed})");
            TickCasterDamage();
            var spellDamage = scripted.Step!.HitPointDamage.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + texts.Text(CombatFloaters.HitPointsLine);
            var spellLines = layer.Field.Lines(entity).Skip(linesBefore).ToArray();
            Require(spellLines.Any(l => l.Text.StartsWith(spellDamage, StringComparison.Ordinal) && l.Color == FloaterColor.White), $"the script's damage line '{spellDamage}' in white: "
                + string.Join("|", spellLines.Select(l => $"{l.Color}:{l.Text}")));
            Require(caster.TakeDamageSteps().Count == 0, "the tick took the damage steps");
            GD.Print($"ARC-DEV-296 floaters spell-damage npc={entity} lines={string.Join("|", spellLines.Select(l => l.Text))}");

            // ARC-COMBAT-1138 (CH12, CH13): the same script damage on a door, window, container or scenery floats "HP n" in white over the object (n = what it lost after the
            // material). NONE when the map has no such object with enough hit points to take the hit without breaking.
            // The first object whose line stands on screen is taken, so the capture shows it; else any one.
            var objectView = GetViewport().GetVisibleRect();
            var damageables = ObjectTargets() is { } objectTargets
                ? objectTargets.Keys.Select(k => objectTargets.Target(session, k, AreaTrapped)).Where(t => t?.Object is { Baseline: { JunkPile: false, Invulnerable: false, Points: > 0 } b } o
                    && b.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery
                    && session.QueryObject(o) is { Condition: SessionObjectCondition.Whole } state && state.HitPoints > 40).ToArray()
                : [];
            var damageable = damageables.FirstOrDefault(t =>
            {
                var castId = ObjectTargets()!.KeyOfEntity(t!.Object!.Entity) is { } key ? _spellCastMap?.IdOf(key) : null;
                if (castId is null || _spellCastMap?.Row(castId) is not { WorldX: not null, WorldY: not null } candidate) return false;
                _floaterCells[castId] = LiveWorldCell(candidate);
                return FloaterAnchor(castId) is { } point && objectView.HasPoint(point);
            }) ?? damageables.FirstOrDefault();
            if (damageable?.Object is { } doomed && ObjectTargets()!.KeyOfEntity(doomed.Entity) is { } doomedKey && _spellCastMap?.IdOf(doomedKey) is { } doomedId)
            {
                var objectBefore = session.QueryObject(doomed);
                if (FloaterCell(doomedId) is null && _spellCastMap?.Row(doomedId) is { WorldX: not null, WorldY: not null } doomedRow) _floaterCells[doomedId] = LiveWorldCell(doomedRow);
                // The diagnostic HUD covers the top of the window: hidden for the two captures only.
                if (!string.IsNullOrWhiteSpace(capture)) _hud!.Visible = false;
                try
                {
                    await Capture("object-before");
                    var objectAnswer = DamageByScript(doomed.Entity, 25, 0, true);
                    var objectAfter = session.QueryObject(doomed);
                    var lost = objectAfter.Damage - objectBefore.Damage;
                    Require(lost > 0, $"the script's damage reached the {doomed.Baseline.Kind} {doomed.Entity}: {objectBefore} -> {objectAfter} {objectAnswer}");
                    var objectLines = layer.Field.Lines(doomedId);
                    Require(objectLines.Count == 1 && objectLines[0].Color == FloaterColor.White
                        && objectLines[0].Text == texts.Text(CombatFloaters.HitPointsLine) + " " + lost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        $"the object's damage line 'HP {lost}' in white: " + string.Join("|", objectLines.Select(l => $"{l.Color}:{l.Text}")));
                    for (var step = 0; step < 8; step++) layer.Field.Advance(400_000 + step * FloaterRules.StepMs, FloaterRules.DefaultSpeed);
                    layer.Redraw();
                    await Capture("object-after");
                    GD.Print($"ARC-COMBAT-1138 floaters object-damage anchor={FloaterAnchor(doomedId)} kind={doomed.Baseline.Kind} id={doomedId} lost={lost} line={objectLines[0].Text}");
                }
                finally { _hud!.Visible = true; }
            }
            else GD.Print("ARC-COMBAT-1138 floaters object-damage NONE (no unbroken object with hit points to spare on the map)");

            // Level 1: a plain hit floats no number; level 0 draws nothing.
            _gameSettings = _gameSettings with { Floats = FloaterRules.Messages };
            var count = layer.Field.Count;
            FloatCritterAttack(result, HeroFloater, entity);
            var added = layer.Field.Count - count;
            Require(added == CombatFloaters.Of(AreaCombatFloaters.Of(result, FloaterSide.Pc, FloaterSide.Other)!, FloaterRules.Messages, texts.Text).Count
                && added < expected.Length, $"level 1 drops the numbers ({added} of {expected.Length})");
            layer.Level = FloaterRules.Never;
            Require(layer.DrawnForSelfTest().Count == 0, "level 0 draws nothing");
            // ARC-UI-1997 (TB1–TB22): text bubbles over objects live the Text Duration setting on the real clock; the line changes the live ones' life at once.
            BubbleClockForSelfTest = 1000; layer.Bubbles.Clear();
            _gameSettings = _gameSettings with { TextDuration = 6 };
            const string heroSays = "A long line the hero says out loud so that it wraps onto several lines of the bubble";
            // The capture's "before" is the old look of a script's line: a white floater rising over the speaker (the bubbles replace it).
            if (!string.IsNullOrWhiteSpace(capture))
            {
                layer.Level = FloaterRules.Numbers; layer.Field.Clear();
                layer.Field.Add(HeroFloater, "Hello", FloaterColor.White); layer.Field.Add(entity, "Hello", FloaterColor.White);
                for (var step = 0; step < 6; step++) layer.Field.Advance(step * 100L, FloaterRules.DefaultSpeed);
                layer.Redraw();
                await Capture("bubble-before");
                layer.Field.Clear(); layer.Redraw();
            }
            Require(Bubble(HeroFloater, heroSays) && Bubble(entity, "Hello"), "bubbles over the hero and the NPC were made");
            Require(layer.Bubbles.Of(HeroFloater) is { Color: BubbleColor.Green, LifeMs: 6000, Persistent: false } && layer.Bubbles.Of(entity) is { Color: BubbleColor.White, LifeMs: 6000 },
                "the PC's bubble is green, the NPC's white, both live the default 6 s");
            var bubbles = layer.LayOutBubbles();
            Require(bubbles.Count == 2 && bubbles.Any(b => b.Owner == HeroFloater), $"both bubbles draw ({bubbles.Count} bubbles)");
            var heroBubble = bubbles.First(b => b.Owner == HeroFloater);
            Require(heroBubble.Lines.Count > 1 && heroBubble.Rect.Size.X / layer.BubbleFactor <= TextBubbleRules.BoxSize + 1, $"the long line wrapped to {heroBubble.Lines.Count} lines within 200 px");
            if (!string.IsNullOrWhiteSpace(capture)) { layer.Redraw(); await Capture("bubble-after"); }
            // ARC-UI-2278: a bubble is laid out in the original's pixels and drawn at the screen's whole factor (as the bars and the dialog band): the same text, box and
            // lines at factor 2 are twice the size and stand on the 2 px grid; the layer's own factor is the screen's.
            Require(layer.BubbleFactor == AreaGameBarPanel.ScreenFactor(GetViewport().GetVisibleRect().Size), $"the bubble factor {layer.BubbleFactor} is the screen's whole factor");
            layer.BubbleFactorOverride = 1; var oneX = layer.LayOutBubbles().First(b => b.Owner == HeroFloater);
            layer.BubbleFactorOverride = 2; var twoX = layer.LayOutBubbles().First(b => b.Owner == HeroFloater);
            layer.BubbleFactorOverride = null;
            Require(twoX.Lines.Count == oneX.Lines.Count && twoX.Rect.Size == oneX.Rect.Size * 2
                && twoX.Lines.All(l => l.Position.X % 2 == 0 && l.Position.Y % 2 == 0 && twoX.Rect.Encloses(new Rect2(l.Position, new Vector2(1, 1)))),
                $"at factor 2 the bubble is twice the size on the 2 px grid: {oneX.Rect} -> {twoX.Rect}");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                // The capture at the owner's 2560 x 1440: the old look (factor 1) and the new one (the screen's whole factor, 2 there).
                var windowBefore = DisplayServer.WindowGetSize();
                await SetSmokeWindowSizeAsync(new Vector2I(2560, 1440));
                await NextFrameAsync();
                GD.Print($"ARC-UI-2278 capture window={GetViewport().GetVisibleRect().Size} factor={layer.BubbleFactor}");
                layer.BubbleFactorOverride = 1; layer.Redraw(); await Capture("bubble-factor1");
                layer.BubbleFactorOverride = null; layer.Redraw(); await Capture("bubble-factor-screen");
                await SetSmokeWindowSizeAsync(windowBefore);
            }
            BubbleClockForSelfTest = 1000 + 6000; TickFloaters();
            Require(layer.Bubbles.Count == 2, "a bubble lives its whole life (age 6000 ms is not above 6000)");
            BubbleClockForSelfTest = 1000 + 6001; TickFloaters();
            Require(layer.Bubbles.Count == 0, "and goes just after it");
            // ARC-UI-2055 (TB26, TB28): a speaker is never turned to itself; an NPC speaker with the hero as listener goes through the AI host's turn.
            Require(!FaceListenerOf(entity, entity) && !FaceListenerOf(HeroFloater, HeroFloater), "a speaker that is its own listener turns nothing");
            Require(Bubble(entity, "Again", listener: HeroFloater), "a new bubble with the hero as its listener");
            GD.Print($"ARC-UI-2055 float speaker turn entity={entity} aiHost={AreaAiHostNow() is not null}");
            _gameSettings = _gameSettings with { TextDuration = 2 }; TickFloaters();
            Require(layer.Bubbles.Of(entity)?.LifeMs == 2000, $"the Text Duration line changed the live bubble's life at once: {layer.Bubbles.Of(entity)?.LifeMs}");
            BubbleClockForSelfTest = 1000 + 6001 + 2001; TickFloaters();
            Require(layer.Bubbles.Count == 0, "the shortened bubble is gone after 2 s");
            // ARC-UI-2034 (TB16): the off flag and the teardown of an object take its bubble with them (0x43D0E0, 0x43CFF0 → 0x4D62B0); the hero's stays; a bubble made over an
            // object already gone stays (only the change removes); a script's fade (0x67) clears all (the hosts' counters, unit tests). The NPC's flags are put back afterwards.
            BubbleClockForSelfTest = 20_000; layer.Bubbles.Clear();
            Require(Bubble(entity, "Going dark") && Bubble(HeroFloater, "Staying"), "bubbles made over the NPC and the hero");
            TickFloaters();
            Require(layer.Bubbles.Count == 2, "a standing NPC keeps its bubble");
            Require(session.WriteObjectFlag(session.Version, entity, SessionObjectFlagField.Object, AreaUseScriptHost.OffObjectFlagBit, true, dataHas: false).Disposition == GameSessionDisposition.Changed, "the off flag written");
            TickFloaters();
            Require(layer.Bubbles.Of(entity) is null && layer.Bubbles.Of(HeroFloater) is not null, "the off flag removed the NPC's bubble and not the hero's");
            Require(Bubble(entity, "Already off"), "a bubble over an object that is off");
            TickFloaters();
            Require(layer.Bubbles.Of(entity) is not null, "it stays: only the change removes");
            Require(session.WriteObjectFlag(session.Version, entity, SessionObjectFlagField.Object, AreaUseScriptHost.OffObjectFlagBit, false, dataHas: false).Disposition == GameSessionDisposition.Changed, "the off flag cleared");
            TickFloaters();
            Require(Bubble(entity, "Torn down"), "a bubble over the standing NPC");
            TickFloaters();
            var destroyKey = MapChangeKey.ForEntity(entity, AreaUseScriptHost.DestroyedProperty);
            Require(session.ApplyChanges(session.Version, null, [new(mapId, destroyKey, 1)]).Disposition == GameSessionDisposition.Changed, "the NPC destroyed by a script");
            TickFloaters();
            Require(layer.Bubbles.Of(entity) is null && layer.Bubbles.Of(HeroFloater) is not null, "the teardown removed the NPC's bubble");
            Require(session.ApplyChanges(session.Version, null, [new(mapId, destroyKey, null)]).Disposition != GameSessionDisposition.Rejected, "the destroyed mark put back");
            TickFloaters();
            Require(!BubbleOwnerGone(entity), "the NPC stands again: neither off nor destroyed");
            layer.Bubbles.Clear();
            GD.Print($"ARC-UI-2034 bubbles removed by off flag and teardown PASS npc={entity}");
            // ARC-UI-2048 (TB29, TB30): a dialog speaker with no cell on the shown map still gets its bubble (the original checks no location), which draws nothing, never expires while it is persistent,
            // ages by time after the end, and goes with the scene; a final line shown with the default life and restarted by the close lives one life from the close.
            {
                const string ghost = "oid:no-cell-speaker";
                BubbleClockForSelfTest = 30_000; layer.Bubbles.Clear();
                Require(FloaterCell(ghost) is null && Bubble(ghost, "From elsewhere", dialogLine: true, persistent: true), "a speaker with no cell gets its dialog bubble");
                Require(layer.Bubbles.Of(ghost) is { Persistent: true } && layer.LayOutBubbles().All(b => b.Owner != ghost), "it is created and draws nothing");
                BubbleClockForSelfTest = 30_000 + 60_000; TickFloaters();
                Require(layer.Bubbles.Of(ghost) is not null, "a persistent bubble over no cell never expires");
                ExpireDialogLineBubbleOf(ghost);
                Require(layer.Bubbles.Of(ghost) is { Persistent: false, LifeMs: 2000 }, "the end gives it the default life from now");
                BubbleClockForSelfTest += 2000; TickFloaters();
                Require(layer.Bubbles.Of(ghost) is not null, "still alive at the end of its life");
                BubbleClockForSelfTest += 1; TickFloaters();
                Require(layer.Bubbles.Of(ghost) is null, "and gone just after it, undrawn the whole time");
                _gameSettings = _gameSettings with { TextDuration = 6 };
                BubbleClockForSelfTest = 100_000; Require(Bubble(entity, "Last words", dialogLine: true), "a final line shown with the default life");
                BubbleClockForSelfTest = 104_000; ExpireDialogLineBubbleOf(entity);
                BubbleClockForSelfTest = 104_000 + 6000; TickFloaters();
                Require(layer.Bubbles.Of(entity) is not null, "the final line's bubble lives one life from the close (past one life from the show)");
                BubbleClockForSelfTest += 1; TickFloaters();
                Require(layer.Bubbles.Of(entity) is null, "and goes one life after the close");
                Require(Bubble(ghost, "Scene change", dialogLine: true, persistent: true), "a bubble before the scene changes");
                _floaterScene = null; Require(Bubble(entity, "New scene", dialogLine: true) && layer.Bubbles.Of(ghost) is null, "a new scene drops every old bubble (the map-close clear-all)");
                layer.Bubbles.Clear(); _gameSettings = _gameSettings with { TextDuration = 2 };
                GD.Print($"ARC-UI-2048 dialog line bubble off the shown map and final-line life PASS npc={entity}");
            }
            GD.Print($"ARC-UI-1997 bubbles over objects PASS heroSlot={heroBubble.Slot} lines={heroBubble.Lines.Count} rect={heroBubble.Rect}");
            GD.Print($"ARC-UI-021 native-smoke floaters PASS map={_readyMapName} npc={entity} h={h} npcLines={lines.Length} heroLines={heroLines}"
                + $" hero={landed.Resolution!.Outcome} texts={texts.Detail} first='{lines[0].Text}' Parity=NOT_VERIFIED");
        }
        finally
        {
            _gameSettings = settings; _floaterClockForSelfTest = false;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            if (_floaters is { } floaters) { floaters.Field.Clear(); floaters.Level = settings.Floats; floaters.Redraw(); }
        }
    }
}
