using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-AI-1514 adds a check: a calm bystander the witness rule (0x4A9F10) sends fleeing from the hero at the hit must reach the setter with FLEE. ARC-DEV-1577: the bystanders are every NPC of the map the host runs (animals and humans),
    // and the victim is the animal with the most bystanders the witness rule does not make protectors (a human, an animal of another faction) within the noise radius, so the flight shows on a map that has one; the stage prints NONE when the map has none.
    // ARC-DEV-264 (B08, smoke plan SM06) native observation (stage "ai-react", only when named, map mode with --hero, AI on, the Crash Site): the hero casts an aggressive single-target damage spell at a calm animal from
    // the longest distance the screen and the spell's Range allow. The pass rule is the executable's (read on 2026-11-08, docs/research/npc-ai-hostility.md section 7.7): the aggressive spell's notice (0x453EE0) runs the attack event
    // on the target before the damage, with no test of the distance, so the animal takes COMBAT_FOCUS on the hero at the hit; it then acts at once (the damage step's AI pass, 0x4B52FC) or at its next tick, at most 5 s of game
    // time later (10 s after a sector fold's wake-up; the stage allows 10 s as a margin and prints when the reaction came). Beyond 30 tiles of the player the original's tick does nothing, so an animal hit from there stands still: that is the original's, and this stage (a hit within the screen)
    // does not test it. The spell and the animal are named by the data, not by this stage.
    private async Task RunAreaAiReactSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-DEV-264 native-smoke ai-react SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-264 ai-react smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_REACT_SMOKE_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_spellRecords is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var records = _spellRecords!;
        // The weakest aggressive single-target damage spell with a missile (so that a wolf survives the hit), in the data's order of damage then number.
        var spell = records.Where(r => r is not null && r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && r.Data.Enabled && ((int)r.Data.Info & (int)SpellInfo.Aggressive) != 0
                && r.Areas is { Length: > 0 } && (r.Areas[0].Targets & 0x4) != 0 && (r.Areas[0].Targets & 0x10) == 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets) && SpellMissileArt(r.Data.Number) is not null
                && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0))
            .OrderBy(r => r.Of(SpellRunAction.Begin).Where(c => c.Type == SpellComponentType.Damage).Max(c => c.Damage!.Maximum)).ThenBy(r => r.Data.Number).FirstOrDefault();
        if (spell is null) { GD.Print("ARC-DEV-264 native-smoke ai-react NONE (no aggressive single-target damage spell with a missile in the data)"); return; }
        var spellRange = spell.Data.Range;

        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        // The animals with the most hit points first: the hit must leave one standing.
        var animals = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core && c.Row.WorldX is not null && c.Row.WorldY is not null)
            .ToArray();
        // ARC-DEV-1577: the bystanders the hit's noise can reach (every NPC of the map but the victim) are ranked per victim by the data: the NPCs that are not animals of the victim's kind come first
        // (a human or an animal of another faction does not protect the victim, so the witness rule may send it fleeing; the rule itself decides at the hit).
        var noiseHalf = AwarenessRules.NoiseRadius(Loudness.Normal);
        var bystanders = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead)
            .Select(h => (h.Entity, Row: inventory.Row(h.Entity), Monstrous: _combatHost.Build(session, map.RuntimeMap, h.Entity).Combat is { Monstrous: true }))
            .Where(b => b.Row.Scope == AreaScope.Core && b.Row.WorldX is not null && b.Row.WorldY is not null).ToArray();
        int NotAnimalNear(string victim, (long X, long Y) at) => bystanders.Count(b => b.Entity != victim && !b.Monstrous
            && Math.Max(Math.Abs(b.Row.WorldX!.Value - at.X), Math.Abs(b.Row.WorldY!.Value - at.Y)) <= noiseHalf);
        animals = animals.OrderByDescending(c => NotAnimalNear(c.Holder.Entity, (c.Row.WorldX!.Value, c.Row.WorldY!.Value)))
            .ThenByDescending(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (animals.Length == 0) { GD.Print($"ARC-DEV-264 native-smoke ai-react NONE map={map.RuntimeMap} (no animal on the map)"); return; }
        CalmNpcsForSmoke(session, animals.Select(a => a.Holder.Entity));

        // The wielded kit weapon is lent the spell (the pre-made hero cannot learn its college), as the cast smokes do.
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is not { Item.Weapon: not null } wielded
            || session.QueryHeroKit().All(k => k.Entity != wielded.Entity))
        { GD.Print("ARC-DEV-264 native-smoke ai-react NONE (the hero wields no kit weapon to lend the spell)"); return; }

        var heroBefore = session.QueryHeroLife()!.Record.State;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var targetIndex = -1; var scaled = false;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // The longest distance (the hero stands that many cells off the animal, orthogonally) the spell reaches, the screen shows the animal from and a cell allows.
            CellCoord? start = null; var distance = 0; var targetCell = default(CellCoord);
            foreach (var wanted in new[] { 16, 14, 12, 10, 8, 6, 4 }.Where(d => d <= Math.Max(spellRange, 4)))
            {
                for (var c = 0; c < Math.Min(animals.Length, 12) && start is null; c++)
                {
                    targetCell = map.Chart.ToChart(new(animals[c].Row.WorldX!.Value, animals[c].Row.WorldY!.Value));
                    start = await AreaSmokeFightableStartAsync(map, commands[animals[c].Row.Key], targetCell, wanted, wanted, false);
                    if (start is not null) { targetIndex = c; distance = wanted; }
                }
                if (start is not null) break;
            }
            if (start is null) { GD.Print($"ARC-DEV-264 native-smoke ai-react NONE map={map.RuntimeMap} (no animal the hero can stand 4 or more cells from and see; spell range {spellRange})"); return; }
            var chosen = animals[targetIndex];
            var entity = chosen.Holder.Entity;
            AreaSmokePlace(map, start.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(session.QueryAiTick(entity) is not null, "the animal has its AI tick");
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            session.Heal(session.Version, chosen.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            Require(session.QueryDanger(entity).State == DangerState.None, "the animal starts calm: " + session.QueryDanger(entity).State);

            _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            CellCoord? lastTile = null;
            CellCoord TileOf()
            {
                if (host!.Tile(entity) is { } tile) return (lastTile = tile).Value;
                Require(lastTile is not null, "the AI host has no tile for the animal");
                return lastTile!.Value;
            }
            static int Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            CellCoord HeroTile() { var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell); return new(checked((int)hero.X), checked((int)hero.Y)); }

            // The cast through the weapon's pane, the cursor and the click at the animal's drawn body.
            _lentItemSpellsForSelfTest = new(wielded.Entity, SessionSpellCaster.Hero, 14, [spell.Data.Number, 10000, 10000, 10000, 10000], -1, 1, ItemSpellPaneRules.IdentifiedFlag); _weaponPaneCache = null;
            InventoryKey(Key.M);
            Require(_spellPane?.WeaponShownForSelfTest is { ButtonShown: true, Open: false } shut && shut.Item == wielded.Entity, "M shows the weapon button: " + _spellPane?.WeaponShownForSelfTest);
            _spellPane!.PressWeaponForSelfTest();
            Require(_spellPane.WeaponShownForSelfTest is { Open: true } pane && pane.Buttons.Any(b => b.Slot == 0 && b.Spell == spell.Data.Number && b.Activates),
                $"the weapon's pane holds the lent spell: {string.Join(",", _spellPane.WeaponShownForSelfTest?.Buttons.Select(b => $"{b.Slot}:{b.Spell}:{b.Activates}") ?? [])}");
            _spellPane.PressItemForSelfTest(0);
            Require(_spellTargeting is { Item: { } source } && source == wielded.Entity && _spellTargeting.Spell == spell.Data.Number, "the slot opened the cursor with the weapon as the source: " + _spellNotice);
            var body = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == chosen.Row.Key) ?? commands[chosen.Row.Key];
            var point = await HoverPointAsync(this, body) ?? throw new InvalidOperationException("ARC-DEV-264 smoke: the animal is not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.Cast, $"the cast hover {HoverActionForSelfTest}");
            var casts = _spellCasts;
            var logStart = _spellCaster!.Log.Count;
            Require(session.QueryDanger(entity).State == DangerState.None, "the animal is still calm just before the click (the focus at the hit is the notice's): " + session.QueryDanger(entity).State);
            var firstTile = TileOf(); var castDistance = Distance(firstTile, HeroTile());
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(_spellCasts == casts + 1, "the click cast: " + _spellNotice);

            // Until the damage lands (the first Damaged or Resisted event on the animal).
            var hitFrame = -1;
            // ARC-AI-1514 (VBG3, 0x4A9F10): the other NPCs the witness rule sends fleeing from the hero for this hit (the rule read on each frame before the hit, as the noise is raised at the hit itself).
            var fleeing = new Dictionary<string, string?>(StringComparer.Ordinal);
            var noiseRadius = noiseHalf;
            for (var frame = 0; frame < 1200 && hitFrame < 0; frame++)
            {
                fleeing.Clear();
                // Only the NPCs this host runs and that stand within the noise square of the hero or of the victim (the host's own witness filter).
                if (host!.Tile(entity) is { } victimTile)
                    foreach (var other in bystanders.Select(b => b.Entity).Where(e => e != entity))
                        if (host.Tile(other) is { } there && (Distance(there, victimTile) <= noiseRadius || Distance(there, HeroTile()) <= noiseRadius)
                            && session.QueryDanger(other).State == DangerState.None
                            && Arcanum.Simulation.Ai.HostilityRules.Witness(host, other, AiSchedulerRules.Pc, entity, Loudness.Normal).Response == WitnessResponse.FleeFromAttacker)
                            fleeing[other] = host.TraceOf(other)?.Setter;
                Frame();
                if (_spellCaster.Log.Skip(logStart).Any(e => e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted && e.Object == entity)) hitFrame = frame;
            }
            Require(hitFrame >= 0, $"the spell's damage landed within 1200 frames; log {string.Join(",", _spellCaster.Log.Skip(logStart).Select(e => e.Kind))}");
            var hitLog = _spellCaster.Log.Skip(logStart).Where(e => e.Object == entity && e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted).ToArray();
            if (hitLog.Any(e => e.Kind == SpellCastEventKind.Resisted)) { GD.Print($"ARC-DEV-264 native-smoke ai-react NONE map={map.RuntimeMap} (the animal resisted the spell: a resisted spell tells the AI nothing, 0x4510F0)"); return; }
            var life = session.QueryLife(chosen.Build.Combat!.Target).View;
            if (life is not { Status.IsActive: true }) { GD.Print($"ARC-DEV-264 native-smoke ai-react NONE map={map.RuntimeMap} (the spell left the animal not active, {life?.Status}: nothing to react)"); return; }

            // At the hit: the animal's danger state (the notice runs the attack event before the damage).
            var danger = session.QueryDanger(entity);
            var atHit = TileOf(); var heroCell = HeroTile();
            var focus = danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero;
            // ARC-DEV-292: the damage step's own AI pass (0x4B52FC) ran at the hit, before any tick: the trace holds it, and the pass is the one that started the animal's run when its gate let it.
            var struckPass = host!.TraceOf(entity)?.Struck; var runAtHit = host.RunOf(entity)?.Top.Goal;
            // Then, up to ten seconds of game time (the next tick is at most five seconds away, ten after a fold's wake-up): the animal leaves its place toward the hero or attacks.
            var gameStart = session.QueryClockSnapshot().GameTimestampMs;
            long Elapsed() => session.QueryClockSnapshot().GameTimestampMs - gameStart;
            var reacted = false; long reactedAfterMs = -1; var swung = false;
            for (var frame = 0; frame < 6000 && Elapsed() < 10_000 && !reacted; frame++)
            {
                Frame();
                var now = TileOf();
                swung |= host!.RunOf(entity)?.Top.Goal == GoalType.AttemptAttack || host.TraceOf(entity)?.Hit is not null;
                if (swung || Distance(now, heroCell) + 2 <= Distance(atHit, heroCell)) { reacted = true; reactedAfterMs = Elapsed(); }
            }
            var after = TileOf();
            // The bystanders the rule named just before the hit asked the setter for FLEE from the hero at the notice (the setter's own rules then decide; its call is what the trace holds).
            if (fleeing.Count == 0)
            { GD.Print($"ARC-DEV-264 native-smoke ai-react NONE map={map.RuntimeMap} (no NPC the witness rule sends fleeing from the hero stood within {noiseRadius} tiles of the hit: every bystander protects the victim, is a guard or is already in danger)"); return; }
            var witnessFlights = fleeing.Where(w => host.TraceOf(w.Key)?.Setter is { } now && now != w.Value && now.StartsWith($"{DangerState.Flee} {AiSchedulerRules.Pc} ", StringComparison.Ordinal)).Select(w => w.Key).ToArray();
            string Name(string id) => $"{id[^6..]}{(bystanders.First(b => b.Entity == id).Monstrous ? "(animal)" : "(human)")}";
            Require(witnessFlights.Length == fleeing.Count, $"the witness rule sent {fleeing.Count} bystander(s) fleeing from the hero but only {witnessFlights.Length} reached the setter with FLEE: {string.Join(",", fleeing.Keys.Select(w => Name(w) + "=" + host.TraceOf(w)?.Setter))}");
            GD.Print($"ARC-AI-1514 ai-react witnesses: {fleeing.Count} bystander(s) in the witness rule's flight, {witnessFlights.Length} asked the setter for FLEE from the hero: {string.Join(",", witnessFlights.Select(Name))}");
            GD.Print($"ARC-DEV-264 ai-react spell={spell.Data.Number} range={spellRange} distance={castDistance} (asked {distance}) hitFrame={hitFrame} danger={danger.State}/{danger.Focus}"
                + $" struck={struckPass} runAtHit={runAtHit} event={host!.TraceOf(entity)?.Event} reaction={host.TraceOf(entity)?.Reaction} tile {atHit.X},{atHit.Y}->{after.X},{after.Y} hero {heroCell.X},{heroCell.Y} reacted={reacted} afterMs={reactedAfterMs}");
            Require(focus, $"the animal took COMBAT_FOCUS on the hero at the hit of an aggressive spell from {castDistance} cells (the original's notice, 0x453EE0, runs the attack event on the target whatever the distance): it is {danger.State} on {danger.Focus}");
            Require(struckPass is not null, "the damage step ran the animal's AI pass at once (0x4B52FC): the trace holds no immediate pass");
            Require(reacted, $"the animal, in focus on the hero, came two or more cells toward the hero or attacked within ten seconds of game time (the original acts at the hit or at its next tick, at most five seconds away): it stood at {after.X},{after.Y}");
            GD.Print("ARC-DEV-264 native-smoke ai-react PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Spell = spell.Data.Number, SpellRange = spellRange, Distance = castDistance, HitFrame = hitFrame, Danger = danger.State.ToString(), WitnessFlights = witnessFlights.Length, Swung = swung, ReactedAfterMs = reactedAfterMs, StruckPass = struckPass, RunAtHit = runAtHit?.ToString(),
                Beyond30Tiles = "NOT_TESTED (the screen shows no animal 31 or more tiles off; the original's tick does nothing there)",
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            // The weapon's pane stays open after the cast unless toggled shut, and the next stage that opens it (cast-multi) would find it open.
            if (_spellPane?.WeaponShownForSelfTest is { Open: true }) _spellPane.PressWeaponForSelfTest();
            _lentItemSpellsForSelfTest = null; _weaponPaneCache = null;
            CancelSpellCast(null); _spellPane?.HidePane(); ClearSpellMissiles("the smoke ended"); _worldActor.TurnedFacing = null;
            HoverReleaseForSelfTest();
            if (targetIndex >= 0) session.Heal(session.Version, animals[targetIndex].Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            CalmNpcsForSmoke(session, animals.Select(a => a.Holder.Entity));
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
