using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-114, ARC_AREA_MAP_SMOKE_ONLY=premade, map mode on the start map with --hero): the AI turns on and the
    // hero stands by the north, then the south group of the twelve pre-made heroes' NPCs. Their heartbeat scripts (1781 + i) run from the
    // AI ticks: the chosen hero's NPC is switched off (not drawn), the eleven others die with no killer, lie fallen and can be looted.
    // The map data's own dead NPCs stay drawn as they are. ARC_PREMADE_SMOKE_SHOT, when set, names a PNG the stage saves of the view.
    private async Task RunAreaPremadeHeroesSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-114 premade smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PREMADE_SMOKE_FAILED");
        }
        Require(_areaSession is { } && _areaSession.QueryHero() is not null && _arguments?.Hero is not null, "needs a session hero (--hero)");
        Require(_combatHost is not null && _readyArea is not null && _conversationContent?.Load.Content is not null, "needs combat values and the game's scripts");
        var session = _areaSession!;
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        // The twelve NPCs whose heartbeat is a pre-made hero's script, by hero index.
        var heroes = session.QueryMapInventory(new(map.RuntimeMap))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity)))
            .Select(h => (h.Holder, h.Row, Script: AreaUseScriptHost.LoadedSlot(h.Row, Arcanum.Scripting.Scr.ScrPoint.Heartbeat)?.Script ?? 0))
            .Where(h => h.Script is >= 1781 and <= 1792).OrderBy(h => h.Script).ToArray();
        Require(heroes.Length == 12, $"twelve pre-made heroes' NPCs on the map: {heroes.Length}");
        var chosen = heroes[_arguments!.Hero!.Index];
        Require(chosen.Script == 1781 + _arguments.Hero.Index, "the chosen hero's NPC carries its script");
        // The NPCs dead in the map data (their seed says so), before any heartbeat.
        var t0Dead = inventory.Seed.Holders.Where(h => h.Kind == SessionHolderKind.Npc && h.Dead).Select(h => inventory.Row(h.Entity)).ToArray();
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var hostMs = 30_000_000L;
            var frames = 0;
            bool Handled((SessionHolderInventory Holder, AreaObjectRecord Row, int Script) h) => h.Holder.Entity == chosen.Holder.Entity
                ? AreaUseScriptHost.SwitchedOff(session, map.RuntimeMap, h.Holder.Entity) : session.QueryNpcKilled(h.Holder.Entity);
            // The north group, then the south group: a heartbeat runs only for an NPC within 30 tiles of the PC (0x4AD420).
            foreach (var group in new[] { heroes.Where(h => h.Row.WorldY!.Value < 82595).ToArray(), heroes.Where(h => h.Row.WorldY!.Value >= 82595).ToArray() })
            {
                var x = (long)group.Average(h => h.Row.WorldX!.Value); var y = (long)group.Average(h => h.Row.WorldY!.Value);
                var stand = AreaSmokeStandable(map, map.Chart.ToChart(new(x, y)), 6);
                AreaSmokePlace(map, stand);
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                Require(AreaAiHostNow() is not null, "the AI host is built");
                TickAreaClock(hostMs);
                for (var f = 0; f < 3000 && !group.All(Handled); f++, frames++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
            }
            GD.Print($"ARC-WORLD-114 premade smoke frames={frames} kills={_heartbeatKills} offs={_heartbeatOffs} t0-dead={t0Dead.Length} chosen={chosen.Script}");
            var missing = heroes.Where(h => !Handled(h)).Select(h => h.Script).ToArray();
            Require(missing.Length == 0, "every pre-made hero's heartbeat ran: left " + string.Join(",", missing));
            Require(heroes.Count(h => session.QueryNpcKilled(h.Holder.Entity)) == 11 && !session.QueryNpcKilled(chosen.Holder.Entity), "the eleven unchosen died, the chosen did not");
            // Drawn: the chosen one is not drawn; each killed one is drawn fallen and is a dead NPC to loot.
            SyncAreaInventory(); SyncAreaCombatBodies();
            Require(_inventoryHidden?.Contains(chosen.Row.Key) == true, "the switched-off NPC is not drawn");
            foreach (var dead in heroes.Where(h => h.Holder.Entity != chosen.Holder.Entity))
            {
                Require(_fallenBodies.ContainsKey(dead.Row.Key), $"script {dead.Script}: drawn fallen");
                Require(_inventoryHost.Target(session, map.RuntimeMap, dead.Row.Key) is { Kind: AreaInventoryTargetKind.DeadNpc }, $"script {dead.Script}: lootable as a dead NPC");
            }
            // The map data's dead NPCs keep their T0 bodies (their stored art is already the corpse's) and are dead NPCs to loot.
            Require(t0Dead.All(r => _areaOverlay!.Value.Bodies.ContainsKey(r.Key) && _inventoryHidden?.Contains(r.Key) != true
                && _inventoryHost.Target(session, map.RuntimeMap, r.Key) is { Kind: AreaInventoryTargetKind.DeadNpc }), "the map data's dead NPCs stay drawn and lootable");
            foreach (var r in t0Dead) GD.Print($"ARC-WORLD-114 premade smoke t0-dead cell=({r.WorldX},{r.WorldY})");
            // Two views when asked: by a killed hero, then by a map data's dead NPC (".t0.png").
            if (System.Environment.GetEnvironmentVariable("ARC_PREMADE_SMOKE_SHOT") is { Length: > 0 } shot)
                foreach (var (focus, path) in new[] { (heroes.First(h => h.Holder.Entity != chosen.Holder.Entity).Row, shot) }
                    .Concat(t0Dead.Take(1).Select(r => (r, System.IO.Path.ChangeExtension(shot, ".t0.png")))))
                {
                    AreaSmokePlace(map, AreaSmokeStandable(map, map.Chart.ToChart(new(focus.WorldX!.Value + 3, focus.WorldY!.Value)), 4));
                    for (var f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    GD.Print($"ARC-WORLD-114 premade smoke shot={image.SavePng(path)} cell=({focus.WorldX},{focus.WorldY})");
                }
            GD.Print($"ARC-WORLD-114 native-smoke premade PASS killed=11 off=1 t0-dead={t0Dead.Length}");
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
