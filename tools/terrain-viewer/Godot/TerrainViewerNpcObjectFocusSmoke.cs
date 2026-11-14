using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-AI-1757: the strikes an NPC landed on an object through the AI host (shooter, struck object's entity, damage), for the npc-object stage.
    private readonly List<(string Shooter, string? Struck, int? Damage)> _npcObjectStrikesForSelfTest = [];

    // ARC-AI-1757 (the viewer leg of ARC-AI-1636, VB2-2 gap d) native observation (stage "npc-object", only when named, map mode with --hero, AI on, the town): a free NPC of the map is put into COMBAT_FOCUS on a scenery or container of the
    // shown map by the attack event of that object (the event 0x4A9650 an object attacker gives, as a trap's shooter does, AreaAiHost.SpellAttacked), with the real map rows, the cast map's object facts and the session's object attack; no
    // invented rows. The pair is the scenery or container with the fewest hit points that stands 2..9 cells from an NPC the hero can stand 4..6 cells from and see. The stage lets the host fight: the NPC walks to the object and the
    // session's object attack (no skill roll) lands damage on it; then the fight goes on until the object is busted (or, when the NPC's hits do not break it within the budget, the rest of its hit points is dealt by a script's
    // damage, printed), and the NPC drops the focus the break made invalid (the danger update's busted test, 0x43F630, AreaObjectTargets.IsBusted) and no attack goal on the object is left. NONE on a map with no such pair.
    private async Task RunAreaNpcObjectFocusSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _inventoryHost is null
            || ObjectTargets() is not { } targets || SpellCasterNow() is null || _spellCastMap is null || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-AI-1757 native-smoke npc-object NONE map={_readyMapName} (no hero, combat values, object targets or cast map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-AI-1757 npc-object smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_OBJECT_SMOKE_FAILED");
        }
        var runtime = CurrentAreaMap;
        if (_inventoryHost.Map(runtime) is not { } inventory) { GD.Print($"ARC-AI-1757 native-smoke npc-object NONE map={_readyMapName} (no inventory)"); return; }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        static int Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        CellCoord ChartCell(AreaObjectRecord row) => map.Chart.ToChart(new(row.WorldX!.Value, row.WorldY!.Value));

        // The NPCs: free, living ones of the map with a row on the screen's plan and combat values.
        var npcs = session.QueryMapInventory(new(runtime))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (h.Entity, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, runtime, h.Entity)))
            .Where(c => c.Row is { Scope: AreaScope.Core, WorldX: not null, WorldY: not null } && commands.ContainsKey(c.Row.Key)
                && c.Build.Combat is { } combat && session.QueryLife(combat.Target).View is { } life && !life.Status.IsDead)
            .Select(c => (c.Entity, Row: c.Row!, Cell: ChartCell(c.Row!), Life: c.Build.Combat!.Target)).ToArray();
        // The objects: a scenery or container the session can damage whole, that the cast map describes to the AI host as an object that is no critter and is not busted.
        var objects = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && commands.ContainsKey(row.Key))
            .Select(row => (Row: row, Target: targets.Target(session, row.Key, AreaTrapped), Cast: _spellCastMap.IdOf(row.Key)))
            .Where(c => c.Cast is not null && c.Target is { Refusal: null, Object: { } o } && c.Target.Kind is DamageableObjectKind.Scenery or DamageableObjectKind.Container
                && !o.Baseline.Invulnerable && !o.Baseline.Busted && !o.Baseline.JunkPile && AiObjectFacts(c.Cast!) is { Busted: false, Destroyed: false, Off: false, Invulnerable: false })
            .Select(c => (c.Row, Target: c.Target!, Cast: c.Cast!, State: session.QueryObject(c.Target!.Object!), Cell: ChartCell(c.Row))).Where(c => c.State.HitPoints > 0).ToArray();
        if (npcs.Length == 0 || objects.Length == 0)
        { GD.Print($"ARC-AI-1757 native-smoke npc-object NONE map={_readyMapName} (free NPCs {npcs.Length}, scenery or containers {objects.Length})"); return; }
        var pairs = npcs.SelectMany(n => objects.Select(o => (Npc: n, Object: o, Distance: Distance(n.Cell, o.Cell)))).Where(p => p.Distance is >= 2 and <= 9)
            .OrderBy(p => p.Object.State.HitPoints).ThenBy(p => p.Distance).ThenBy(p => p.Npc.Entity, StringComparer.Ordinal).ThenBy(p => p.Object.Cast, StringComparer.Ordinal).Take(40).ToArray();
        ForgetNpcPlacesForSmoke(session, runtime);
        CalmNpcsForSmoke(session, npcs.Select(n => n.Entity));
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var scaled = false;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var skipped = 0; var found = -1;
            for (var i = 0; i < pairs.Length && found < 0; i++)
            {
                if (await AreaSmokeFightableStartAsync(map, commands[pairs[i].Npc.Row.Key], pairs[i].Npc.Cell, 6, 4, false) is null) skipped++;
                else found = i;
            }
            if (found < 0) { GD.Print($"ARC-AI-1757 native-smoke npc-object NONE map={_readyMapName} (no NPC and object 2..9 cells apart with a start the hero can stand 4..6 cells from the NPC and see; {pairs.Length} pairs, {skipped} without a start)"); return; }
            var npc = pairs[found].Npc; var obj = pairs[found].Object;
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            RequestSectorChart(); // the placement moved the hero through the session's cache without asking the chart
            Require(SettleSectorChartForSmoke(), "the sector chart's background read did not finish: " + SectorChartState());
            Require(session.QueryAiTick(npc.Entity) is not null, "the NPC has its AI tick");
            session.Heal(session.Version, npc.Life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            Require(session.QueryDanger(npc.Entity).State == DangerState.None, "the NPC starts calm: " + session.QueryDanger(npc.Entity).State);
            var objectTarget = obj.Target.Object!;
            var before = session.QueryObject(objectTarget);

            _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            string Trace() => $"event={host!.TraceOf(npc.Entity)?.Event} perform={host.TraceOf(npc.Entity)?.Perform} step={host.TraceOf(npc.Entity)?.Step} hit={host.TraceOf(npc.Entity)?.Hit} object={host.TraceOf(obj.Cast)?.Event}";
            bool InFocus() => session.QueryDanger(npc.Entity) is { State: DangerState.CombatFocus, Focus.Entity: { } focus } && focus == obj.Cast;

            // The attack event of the object (what a trap's shooter or a scripted object attack runs): the NPC takes COMBAT_FOCUS on it.
            host!.SpellAttacked(obj.Cast, npc.Entity);
            Require(InFocus(), $"the attack event of the object {obj.Cast} gave the NPC {npc.Entity} COMBAT_FOCUS on it: {session.QueryDanger(npc.Entity).State}/{session.QueryDanger(npc.Entity).Focus} {Trace()}");
            Require(!host.Gone(obj.Cast) && !host.DeadCritter(obj.Cast), "the host reads the object as standing, never as a dead critter");

            // The fight: until the object takes damage.
            _npcObjectStrikesForSelfTest.Clear();
            var frames = 0;
            bool Struck() => _npcObjectStrikesForSelfTest.Any(s => s.Shooter == npc.Entity && s.Struck == obj.Target.Entity && s.Damage is > 0);
            for (; frames < 2400 && !Struck(); frames++) Frame();
            var first = session.QueryObject(objectTarget);
            var firstHitTrace = Trace();
            Require(Struck(), $"the NPC fought the object and the session's object attack damaged it within {frames} frames ({before.Damage}/{before.HitPoints}->{first.Damage}/{first.HitPoints}): {firstHitTrace} danger={session.QueryDanger(npc.Entity).State}");
            var strikeDamage = _npcObjectStrikesForSelfTest.First(s => s.Shooter == npc.Entity && s.Struck == obj.Target.Entity && s.Damage is > 0).Damage!.Value;
            var heroHp = session.QueryHeroLife()!.Status.HitPoints;
            GD.Print($"ARC-AI-1757 npc-object fight npc={npc.Entity} object={obj.Cast} kind={obj.Target.Kind} cells {npc.Cell.X},{npc.Cell.Y}->{obj.Cell.X},{obj.Cell.Y} first strike after {frames} frames damage {strikeDamage} on {before.HitPoints} hit points {firstHitTrace}");

            // The fight goes on until the object is busted (the busted test the host reads); the NPC's hits may not break a sturdy container within the budget, then a script's damage deals the rest and the stage says so.
            var forced = false;
            bool Busted() => session.QueryObject(objectTarget).Condition != SessionObjectCondition.Whole || session.QueryObject(objectTarget).HitPoints <= 0 || targets.IsBusted(session, obj.Row.Key, AreaTrapped) || host.Gone(obj.Cast);
            var bustFrames = 0;
            for (; bustFrames < 6000 && !Busted() && InFocus(); bustFrames++) Frame();
            if (!Busted() && InFocus())
            {
                forced = true;
                var left = session.QueryObject(objectTarget).HitPoints;
                DamageByScript(obj.Target.Entity, Math.Max(left, 1) + 1, 0, true);
            }
            var busted = Busted();
            var struckByNpc = session.QueryObject(objectTarget).Damage;
            Require(busted, $"the object is busted after the fight (damage {struckByNpc}, condition {session.QueryObject(objectTarget).Condition}): {Trace()}");

            // The break makes the focus invalid: the danger update drops it within the NPC's next tick (at most five seconds of game time; the stage allows twenty).
            var dropFrames = 0;
            for (; dropFrames < 400 && InFocus(); dropFrames++) Frame();
            var danger = session.QueryDanger(npc.Entity);
            var goalOnObject = host.RunOf(npc.Entity) is { } run && run.Bottom.Target == obj.Cast;
            GD.Print($"ARC-AI-1757 npc-object busted forced={forced} bustFrames={bustFrames} dropFrames={dropFrames} danger={danger.State}/{danger.Focus} goalOnObject={goalOnObject} {Trace()}");
            Require(!InFocus(), $"the NPC dropped the focus on the busted object within {dropFrames} frames: {danger.State}/{danger.Focus} {Trace()}");
            Require(!goalOnObject, "the NPC is no longer on an attack goal on the busted object");
            Require(session.QueryHeroLife()!.Status.HitPoints >= heroHp, "the hero was not struck by the NPC's fight with the object");
            GD.Print("ARC-AI-1757 native-smoke npc-object PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                RuntimeMap = runtime, Npc = npc.Row.PrototypeNumber, Object = obj.Row.PrototypeNumber, Kind = obj.Target.Kind.ToString(), Distance = Distance(npc.Cell, obj.Cell), FirstStrikeDamage = strikeDamage, ObjectHitPoints = before.HitPoints,
                FramesToFirstDamage = frames, ForcedBreak = forced, FramesToBreak = bustFrames, FramesToDrop = dropFrames, DangerAfter = danger.State.ToString(),
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            RestoreHeroForSmoke(session, heroBefore);
            CalmNpcsForSmoke(session, npcs.Select(n => n.Entity));
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
