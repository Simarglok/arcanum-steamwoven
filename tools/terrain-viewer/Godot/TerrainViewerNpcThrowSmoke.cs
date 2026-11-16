using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-1762: the NPC throws the viewer launched (shot, thrown item, the check's decided hit, thrower, the cells it flew between), for the npc-throw stage.
    private readonly List<(long Shot, string Item, bool Hit, string Thrower, CellCoord From, CellCoord To, bool InFlight, CellCoord? Lying)> _npcThrowLaunchesForSelfTest = [];

    // ARC-DEV-1762 native observation (stage "npc-throw", only when named, map mode with --hero): a free NPC of the map whose wielded weapon is a seed item (the AI host throws no item the session added) is lent the
    // installed throwing dagger's own fields (prototype 6072, the "default throws" weapon with a projectile art; no NPC of the installed town wields one), and the hero stands four to six cells from it with a clear line;
    // the NPC is told of the hero's attack and fights. The attack starter throws the dagger (goal 37, ARC-COMBAT-1717) and the stage requires the whole chain through the real projectile host: the flight launches (the
    // dagger leaves the NPC's hand and lies hidden on the thrower's cell while it flies), the check the session decided at the throw plays out (a hit strikes the hero through the AI host and its damage lands, a miss
    // scatters and hurts nobody), the dagger lands where the flight ends (placed there, drawn again, no decay) and nothing is left in flight or in the session. NONE on a map with no such NPC. ARC-DEV-1962: every throw is judged, and the next thrower throws (a thrown seed item is no longer its NPC's, so one NPC cannot throw it twice) until one throw is decided a hit, so the hit leg runs too.
    private async Task RunAreaNpcThrowSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _inventoryHost is null
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-DEV-1762 native-smoke npc-throw NONE map={_readyMapName} (no hero, combat values or attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-1762 npc-throw smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_THROW_SMOKE_FAILED");
        }
        var runtime = CurrentAreaMap;
        if (_inventoryHost.Map(runtime) is not { } inventory) { GD.Print($"ARC-DEV-1762 native-smoke npc-throw NONE map={_readyMapName} (no inventory)"); return; }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        // The prototype of the installed throwing dagger (6072): its combat fields are what the NPC's wielded weapon is lent; it must be the "default throws" weapon with a projectile art.
        const int DaggerPrototype = 6072;
        for (var i = 0; i < 600 && CombatPrototype(DaggerPrototype) is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (CombatPrototype(DaggerPrototype) is not { Item: not null, Weapon: { } daggerWeapon } daggerFields || !daggerWeapon.Flags.HasFlag(CombatWeaponFlags.DefaultThrows) || daggerWeapon.MissileArt == CombatWeaponFields.NoMissileArt)
        { GD.Print($"ARC-DEV-1762 native-smoke npc-throw NONE map={_readyMapName} (prototype {DaggerPrototype} is not loaded as a throwing weapon with a projectile art)"); return; }
        // The wielders: free, living NPCs of the map with a row on the screen's plan whose worn weapon is a seed item (no item the session added to the NPC, which the AI host does not throw) with no no-drop flag and no
        // scripts. The installed town has no NPC that wields the dagger itself (the census below prints it), so the stage lends such a weapon the dagger's own fields, as the ranged stage lends the hero's.
        var holders = session.QueryMapInventory(new(runtime))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free).ToArray();
        var throwers = new List<(string Entity, AreaObjectRecord Row, AreaCombatTarget Target, string Dagger, int Prototype)>();
        var wielders = 0; var natural = 0;
        foreach (var holder in holders)
        {
            if (inventory.Row(holder.Entity) is not { Scope: AreaScope.Core } row || !commands.ContainsKey(row.Key) || _combatHost.Worn(session, runtime, holder.Entity) is not { } worn) continue;
            foreach (var item in worn)
            {
                if (item.Slot != EquipmentSlot.Weapon || item.Wear.NoDrop || item.Fields.Object.ScriptPoints.Count != 0 || session.QueryNpcInventory(holder.Entity).Any(made => made.Entity == item.Entity)
                    || _combatHost.Target(session, runtime, row.Key) is not { Build.Combat: not null } target || session.QueryLife(target.Build.Combat!.Target).View is not { } life || life.Status.IsDead) continue;
                wielders++;
                if (item.Fields.Weapon is { } weapon && weapon.Flags.HasFlag(CombatWeaponFlags.DefaultThrows)) natural++;
                throwers.Add((holder.Entity, row, target, item.Entity, inventory.Row(item.Entity)?.PrototypeNumber ?? -1));
            }
        }
        GD.Print($"ARC-DEV-1762 npc-throw census map={_readyMapName} freeNpcs={holders.Length} wielders={wielders} wieldingDefaultThrows={natural}");
        if (throwers.Count == 0) { GD.Print($"ARC-DEV-1762 native-smoke npc-throw NONE map={_readyMapName} (no free NPC wields a seed weapon among {holders.Length} free NPCs)"); return; }
        ForgetNpcPlacesForSmoke(session, runtime);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var heroRecord = session.QueryHeroLife()!.Record.State;
        var hostMs = 30_000_000L; string? passLine = null;
        // ARC-DEV-1962: one throw of one thrower, judged by the assertions below; true when the session decided it a hit (the hit leg then ran). A thrown dagger is no longer the NPC's seed item (an item an NPC picks up again is
        // one the session added, which the AI host does not throw), so a miss is followed by the next thrower, each lent the dagger's fields afresh, until a throw is decided a hit.
        async Task<bool?> ThrowOnceAsync((string Entity, AreaObjectRecord Row, AreaCombatTarget Target, string Dagger, int Prototype) thrower, int index)
        {
            var at = map.Chart.ToChart(new(thrower.Row.WorldX!.Value, thrower.Row.WorldY!.Value));
            if (await AreaSmokeFightableStartAsync(map, commands[thrower.Row.Key], at, 6, 4, false) is not { } start) return null;
            var entity = thrower.Entity; var dagger = thrower.Dagger;
            // The dagger is a seed item of the map (it has a row): lying on the ground it is drawn under its row's key, and the session lists it on the map's ground.
            var bodyKey = inventory.Row(dagger)!.Key;
            CellCoord? Lying() => session.QueryMapInventory(new(runtime))!.Ground.FirstOrDefault(ground => ground.Item.Entity == dagger)?.Cell;
            var scaled = false;
            try
            {
                _combatHost.LendItemFieldsForSmoke(dagger, daggerFields);
                AreaSmokePlace(map, start);
                if (!HeroInCombatMode) ToggleCombatMode();
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                PublishWorldSelection();
                var host = AreaAiHostNow();
                Require(host is not null && session.QueryAiTick(entity) is not null, $"throw {index}: the AI host is built and the thrower has its AI tick");
                Require(host!.ThrowableWeapon(entity) == dagger, $"throw {index}: the AI host answers the lent {dagger} as the thrower's throwable weapon ({host.ThrowableWeapon(entity)})");
                session.Heal(session.Version, thrower.Target.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                host.HeroAttacked(entity);
                _npcThrowLaunchesForSelfTest.Clear(); _npcShotStrikesForSelfTest.Clear(); _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _shotEndItemEventsForSelfTest.Clear();
                _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                TickAreaClock(hostMs);
                void Frame()
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                var frames = 0;
                for (; frames < 6000 && _npcThrowLaunchesForSelfTest.Count == 0; frames++) Frame();
                Require(_npcThrowLaunchesForSelfTest.Count > 0, $"throw {index}: the NPC threw its dagger after {frames} frames: {host.TraceOf(entity)?.Throw} {host.TraceOf(entity)?.Perform} danger={session.QueryDanger(entity).State}");
                var launch = _npcThrowLaunchesForSelfTest[0];
                Require(launch.Thrower == entity && launch.Item == dagger, $"throw {index}: the launched throw is the thrower's dagger ({launch.Thrower} {launch.Item})");
                var heroAtThrow = session.QueryHeroLife()!.Status.HitPoints;
                // The dagger left the NPC's hand at the throw and lies hidden on the thrower's cell while it flies (the shot holds it in both NPC stores).
                Require(_combatHost.WornItems(session, runtime, entity)?.ContainsKey(EquipmentSlot.Weapon) != true, $"throw {index}: the dagger left the thrower's hand");
                // The dagger lies hidden on the thrower's cell while it flies: the session holds it in flight on that cell (the ground bodies skip an item in flight) as of the launch itself, even when the flight ends in the same frame.
                Require(launch.InFlight && launch.Lying == launch.From, $"throw {index}: it lies hidden on the thrower's cell {launch.From.X},{launch.From.Y} while it flies (in flight {launch.InFlight}, lying {launch.Lying?.X},{launch.Lying?.Y})");
                for (var f = 0; f < 3000 && (_projectiles.Count > 0 || session.QueryShotsInFlight().Count > 0 && _thrownLandingsForSelfTest.Count == 0); f++) Frame();
                Require(_projectiles.Count == 0 && _thrownLandingsForSelfTest.Count > 0, $"throw {index}: the flight ended and the dagger landed ({_projectiles.Count} in flight, {_thrownLandingsForSelfTest.Count} landings)");
                Require(!session.QueryShotsInFlight().Any(), $"throw {index}: no shot is left in the session ({session.QueryShotsInFlight().Count})");
                var landing = _thrownLandingsForSelfTest.FirstOrDefault(l => l.Item == dagger);
                Require(landing.Item == dagger, $"throw {index}: the dagger's own landing was recorded");
                Require(landing.Disposition != GameSessionDisposition.Rejected, $"throw {index}: the landing was accepted ({landing.Disposition})");
                var end = _projectileEndsForSelfTest.FirstOrDefault(e => e.Shot.Id == launch.Shot);
                var strike = _npcShotStrikesForSelfTest.FirstOrDefault(s => s.Shot == launch.Shot);
                var lying = Lying();
                SyncAreaGroundBodies();
                var drawn = _groundBodies.ContainsKey(bodyKey);
                GD.Print($"ARC-DEV-1762 npc-throw flight throw={index} shot={launch.Shot} hit={launch.Hit} from={launch.From.X},{launch.From.Y} to={launch.To.X},{launch.To.Y} strike={end.Strike} struck={end.Struck ?? "none"} strikes={_npcShotStrikesForSelfTest.Count}"
                    + $" landed={landing.Tile.X},{landing.Tile.Y} lying={lying?.X},{lying?.Y} drawn={drawn} itemEvents={_shotEndItemEventsForSelfTest.Count}");
                Require(lying == landing.Tile && !session.QueryItemInFlight(dagger) && session.QueryItemDecays().All(d => d.Entity != dagger),
                    $"throw {index}: the dagger lies where the flight ended without decay ({lying?.X},{lying?.Y} vs {landing.Tile.X},{landing.Tile.Y})");
                Require(drawn, $"throw {index}: the dagger is drawn again where it landed");
                // ARC-COMBAT-2425 (ledger TW10, TW11): an NPC's throw holds the hero's tile as a location and no object, and the line of fire never names the critter on the target tile, so nothing is struck: no roll,
                // the dagger flies unchecked to the hero's tile and lands there, and the hero is not hurt.
                var lost = heroAtThrow - session.QueryHeroLife()!.Status.HitPoints;
                Require(!launch.Hit && end.Struck is null && strike.Struck is null && end.Strike != ProjectileStrikeKind.Target,
                    $"throw {index}: the hero on the target tile is not struck ({end.Strike} {end.Struck ?? "none"} {strike.Struck ?? "none"} hit {launch.Hit})");
                Require(landing.Tile == launch.To && lost == 0, $"throw {index}: the dagger landed on the hero's tile and hurt nobody (landed {landing.Tile.X},{landing.Tile.Y} aimed {launch.To.X},{launch.To.Y}, lost {lost})");
                GD.Print($"ARC-DEV-1962 npc-throw tile leg throw={index} struck=none heroLost={lost} hp={heroAtThrow}->{session.QueryHeroLife()!.Status.HitPoints}"
                    + $" landed={landing.Tile.X},{landing.Tile.Y} drawn={drawn}");
                passLine = "ARC-DEV-1762 native-smoke npc-throw PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                    RuntimeMap = runtime, Thrower = thrower.Row.PrototypeNumber, Weapon = DaggerPrototype, Wielded = thrower.Prototype, Throws = index, Skipped = index - 1, Hit = launch.Hit, Strike = end.Strike.ToString(), Struck = end.Struck ?? "none",
                    Damage = strike.Damage, HeroLost = lost, Landed = $"{landing.Tile.X},{landing.Tile.Y}", Drawn = drawn, ItemEvents = _shotEndItemEventsForSelfTest.Count,
                    Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" });
                return true;
            }
            finally
            {
                try { _combatHost.LendItemFieldsForSmoke(dagger, null); }
                finally
                {
                    try { RestoreHeroForSmoke(session, heroRecord); }
                    finally
                    {
                        RetireAiHost(); _aiHostStamp = null;
                        if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
                    }
                }
            }
        }
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var tried = 0; var decided = false;
            foreach (var candidate in throwers.Take(12))
            {
                if (await ThrowOnceAsync(candidate, tried + 1) is not { } hit) continue;
                tried++;
                if (hit) { decided = true; break; }
            }
            if (tried == 0) { GD.Print($"ARC-DEV-1762 native-smoke npc-throw NONE map={_readyMapName} (no thrower the hero can stand 4..6 cells from and see among {throwers.Count})"); return; }
            Require(decided, $"no throw of {tried} (one per thrower) landed on the hero's tile without striking it");
            GD.Print(passLine!);
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            PublishWorldSelection();
        }
    }
}
