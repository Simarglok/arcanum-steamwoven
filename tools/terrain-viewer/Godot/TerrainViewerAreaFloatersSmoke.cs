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

            // Level 1: a plain hit floats no number; level 0 draws nothing.
            _gameSettings = _gameSettings with { Floats = FloaterRules.Messages };
            var count = layer.Field.Count;
            FloatCritterAttack(result, HeroFloater, entity);
            var added = layer.Field.Count - count;
            Require(added == CombatFloaters.Of(AreaCombatFloaters.Of(result, FloaterSide.Pc, FloaterSide.Other)!, FloaterRules.Messages, texts.Text).Count
                && added < expected.Length, $"level 1 drops the numbers ({added} of {expected.Length})");
            layer.Level = FloaterRules.Never;
            Require(layer.DrawnForSelfTest().Count == 0, "level 0 draws nothing");
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
