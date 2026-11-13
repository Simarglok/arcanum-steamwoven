using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-1544: smoke only, set while the stage draws as the viewer did before the change (the weapon letter of the map art), to capture the "before" image.
    private bool _weaponArtOldDrawing;

    /// <summary>
    /// ARC-RENDER-1544 native observation (stage "weapon-art", only when named, with --hero, on a map window with the AI host): a companion (the first free speaker that joins) and an NPC the AI never moved (the
    /// first living NPC with a human critter body that stands still) each get a weapon item, are put into combat mode and re-wielded; their body is then drawn with the stand ART of the weapon part
    /// <see cref="AreaAiHost.ShownWeapon"/> gives (0x465020), not the weapon letter of the map art. ARC_AREA_WEAPON_CAPTURE=PREFIX saves the window before (the old drawing) and after as
    /// PREFIX-companion-before.png, -companion-after.png, -npc-before.png and -npc-after.png (a local, ignored path) and prints the screen point of the body.
    /// </summary>
    private async Task RunAreaWeaponArtStageAsync()
    {
        // The AI host is built while the smoke flag is set (as the other AI stages' preamble).
        var aiBefore = _aiForSmoke;
        _aiForSmoke = true;
        for (var wait = 0; wait < 1800 && (AreaAiHostNow() is null || _barterData is null); wait++) { AdoptBarterData(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || AreaAiHostNow() is not { } host || _barterData?.Data is not { } barter
            || _worldActor.Session is not AreaGridSession grid)
        { _aiForSmoke = aiBefore; GD.Print($"ARC-RENDER-1544 native-smoke weapon-art SKIP (needs --hero, an area session, the AI host and the item prototypes: session={_areaSession is not null} hero={_areaSession?.QueryHero() is not null} ready={_readyArea is not null} host={AreaAiHostNow() is not null} barter={_barterData?.Data is not null} grid={_worldActor.Session is AreaGridSession})"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RENDER-1544 weapon-art smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_WEAPON_ART_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_WEAPON_CAPTURE");
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++)
            {
                TickAreaClock(_talkSmokeHostMs += 100);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncAreaInventory();
            }
        }
        // The weapon prototypes by weapon part (the use-art subtype of 0x465020): the parts above bare hands.
        var weapons = Enumerable.Range(6049, 15213 - 6049 + 1)
            .Select(n => (Number: n, Fields: barter.Prototype(n)))
            .Where(w => w.Fields is { Object.Type: (int)SessionItemType.Weapon, Item.UseArtFragment: { } use } && WeaponArtRules.WeaponPart(true, use) > WeaponArtRules.Unarmed)
            .GroupBy(w => WeaponArtRules.WeaponPart(true, w.Fields!.Item!.UseArtFragment))
            .OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Take(10).ToArray());
        Require(weapons.Count > 0, "the installation has weapon prototypes");
        var results = new List<string>();
        // What the stage gave and which NPCs it put into combat mode, undone in the finally block whatever fails.
        var given = new List<(string Entity, string Item)>(); var fought = new HashSet<string>();

        // The AI's pass takes a calm NPC out of combat mode again, so the case puts it back before each frame it draws.
        async Task Hold(string entity, int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                TickAreaClock(_talkSmokeHostMs += 100);
                if (!session.QueryCombatMode(SessionCombatant.Critter(entity))) host.EnterCombatMode(entity);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncAreaInventory();
            }
        }
        // One body: finds a weapon part whose stand ART the body has and an item an NPC re-wields as it, then draws the body as before the change and as now.
        async Task<bool> Case(string label, string entity, AreaVisualRecord visual, Func<AreaDrawCommand?> drawn, Func<Task<Vector2?>> screen)
        {
            var tried = new List<string>();
            foreach (var (part, protos) in weapons)
            {
                var stand = ArmedArt(area, visual, entity, AreaBodyMoveArt.Stand, part, 0);
                if (stand.Art is null || stand.Weapon != part) { tried.Add($"{part}:art={stand.Status}/{stand.Weapon}"); continue; }
                foreach (var (number, fields) in protos)
                {
                    var gift = session.AddItemToNpc(session.Version, entity, new SessionItemKind(SessionItemType.Weapon, number, fields!.Object.Name, fields.Object.Description), _ => SessionInsertVerdict.Accepted);
                    if (gift.Item is not { } item) { tried.Add($"{part}/{number}:given={gift.Failure}"); continue; }
                    given.Add((entity, item)); fought.Add(entity);
                    var entered = host.EnterCombatMode(entity);
                    host.ReWield(entity, allSlots: true);
                    await Hold(entity, 4);
                    if (host.ShownWeapon(entity).Weapon != part) { tried.Add($"{part}/{number}:shown={host.ShownWeapon(entity)} combat={entered?.Disposition}/{entered?.Failure}/{entered?.Refusal}/{session.QueryCombatMode(SessionCombatant.Critter(entity))}"); session.RemoveItemFromNpc(session.Version, entity, item); given.Remove((entity, item)); continue; }
                    _weaponArtOldDrawing = true; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); await Hold(entity, 3);
                    var old = drawn();
                    var oldPoint = await screen();
                    await Hold(entity, 2); await Shot($"{label}-before");
                    _weaponArtOldDrawing = false; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); await Hold(entity, 3);
                    var now = drawn();
                    var point = await screen();
                    await Hold(entity, 2); await Shot($"{label}-after");
                    Require(now is not null && now.Frame.Policy == AreaBodyMoveArt.Policy, $"{label}: drawn with the stand ART of the weapon part {part} ({now?.Frame.Policy})");
                    Require(_armedArts.Values.Any(l => l.Animation == AreaBodyMoveArt.Stand && l.Weapon == part && l.Art is not null), $"{label}: the stand ART of weapon part {part} was read");
                    Require(old is null || old.Frame != now!.Frame, $"{label}: not the map art's frame {old?.Frame}");
                    GD.Print($"ARC-RENDER-1544 weapon-art case={label} entity={entity} weapon-part={part} prototype={number} shown={host.ShownWeapon(entity)} before-point={oldPoint} after-point={point} before-frame={old?.Frame} after-frame={now!.Frame}");
                    results.Add($"{label}:{part}");
                    session.RemoveItemFromNpc(session.Version, entity, item); given.Remove((entity, item));
                    return true;
                }
            }
            GD.Print($"ARC-RENDER-1544 weapon-art case={label} NONE (no weapon part both the body's ARTs and an item the NPC re-wields give): {string.Join(" ", tried.Take(30))}");
            return false;
        }
        async Task Shot(string name)
        {
            if (capture is not { Length: > 0 }) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_WEAPON_ART_CAPTURE_FAILED " + name);
        }
        string? joined = null;
        try
        {
            // 1. An NPC the AI never moved: a living hosted NPC with a human critter body that stands at its map data's cell, the hero beside it.
            var unmoved = host.Scene.Npcs.Where(n => n.Row.WorldX is not null && !session.QueryNpcDead(n.Entity) && host.Movers.Of(n.Entity) is null
                    && NpcT0Body(n.Row.Key) is { } c && TryBodyVisual(c.Visual, out var v) && v.RawArt >> 28 == 2).Take(16).ToArray();
            foreach (var npc in unmoved)
            {
                var cell = LiveWorldCell(npc.Row);
                // The hero stands two cells off so that the NPC is in the clear part of the window, below the diagnostic text.
                var placed = false;
                foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2), (-2, -2), (2, 2), (-2, 2), (2, -2), (-3, 0), (3, 0), (0, -3), (0, 3) })
                {
                    try { AreaSmokePlace(map, AreaSmokeStandable(map, new(checked((int)(cell.X - grid.Chart.OriginX)) + dx, checked((int)(cell.Y - grid.Chart.OriginY)) + dy), 1)); }
                    catch (InvalidOperationException) { continue; }
                    PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                    await Settle(2);
                    if (NpcT0Body(npc.Row.Key) is { } probe && await HoverPointAsync(this, probe) is { Y: > 330 }) { placed = true; break; }
                }
                if (!placed) continue;
                CalmNpcsForSmoke(session, [npc.Entity]);
                await Settle(10);
                if (session.QueryNpcDead(npc.Entity) || host.Movers.Of(npc.Entity) is not null || NpcT0Body(npc.Row.Key) is not { } t0 || !TryBodyVisual(t0.Visual, out var visual)) continue;
                var key = npc.Row.Key;
                AreaDrawCommand? Drawn() => AreaMovingCommands()?.FirstOrDefault(c => c.Identity == key);
                if (await Case("npc", npc.Entity, visual, Drawn, () => HoverPointAsync(this, AreaMovingCommands()?.FirstOrDefault(c => c.Identity == key) ?? t0))) break;
                host.LeaveCombatMode(npc.Entity);
            }

            // 2. A companion: the first free speaker with a human critter body joins the hero, which stands next to its home.
            var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker) && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free
                && t.Visuals.Values.Any(v => v.RawArt >> 28 == 2));
            if (template is null) GD.Print("ARC-RENDER-1544 weapon-art case=companion NONE (no free speaker with a human critter body)");
            else
            {
                Require(session.ForceJoinParty(session.Version, template.Entity).Disposition != GameSessionDisposition.Rejected, "the speaker joined");
                joined = template.Entity;
                var standing = AreaSmokeStandable(map, template.HomeOrigin, 1);
                AreaSmokePlace(map, standing);
                long Near() => _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity) is { } c
                    ? Arcanum.Simulation.Party.FollowDistanceRules.Distance(c.Grid.State.CommittedCell, standing) : long.MaxValue;
                for (var f = 0; f < 600 && Near() > 3; f++) AreaSmokeFrame();
                Require(Near() <= 3, "the companion came up to the hero: " + _areaCompanions?.Status());
                PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                await Settle(10);
                AreaDrawCommand? Drawn() => AreaCompanionCommands()?.FirstOrDefault(c => c.Identity == template.Key);
                if (Drawn() is { } first && template.Visuals.TryGetValue(first.Visual, out var visual))
                    await Case("companion", template.Entity, visual, Drawn, () => HoverPointAsync(this, Drawn()!));
                else GD.Print("ARC-RENDER-1544 weapon-art case=companion NONE (the companion is not drawn)");
                host.LeaveCombatMode(template.Entity);
            }
            Require(results.Count > 0, "a companion or an unmoved NPC showed a weapon (no case ran)");
            Require(template is null || results.Any(r => r.StartsWith("companion:", StringComparison.Ordinal)), "the companion showed a weapon");
            GD.Print("ARC-RENDER-1544 native-smoke weapon-art PASS " + string.Join(",", results));
        }
        finally
        {
            _weaponArtOldDrawing = false;
            foreach (var (entity, item) in given) session.RemoveItemFromNpc(session.Version, entity, item);
            foreach (var entity in fought) host.LeaveCombatMode(entity);
            if (joined is not null) session.LeaveParty(session.Version, joined, new PartyLeave(Force: true));
            _aiForSmoke = aiBefore; ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            BindAreaCompanions(); PublishWorldSelection();
        }
    }
}
