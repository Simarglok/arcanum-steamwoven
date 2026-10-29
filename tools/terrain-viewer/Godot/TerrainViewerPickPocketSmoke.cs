using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-SKILLS-010, ARC_AREA_MAP_SMOKE_ONLY=pickpocket, map mode with --hero): the AI turns on (the start reads the
    // hero's sight and the victim's view through its host), the hero stands next to a living free NPC that holds a plain item, the palette's
    // Pick pocket arms the skill and the start opens the window in steal mode. A fated theft moves the item to the hero, a fated plant moves
    // it back, then plain thefts run until one is noticed or ten are done; a noticed failure closes the window after its consequences. A critter
    // the flags refuse floats the refusal. NONE without a living NPC with such an item that the hero sees from a cell next to it.
    // ARC-UI-065: with the game's art the steal window shows the loot frame without Take all (IW30), the theft is a press on the victim's
    // grid released on the hero's, the plant the reverse (IW35), and the first plain theft a right release on the victim's item (IW36);
    // ARC_AREA_STEAL_CAPTURE=PATH.png saves a readback of the window.
    private async Task RunAreaPickPocketSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is not { } combat || _readyArea is not { } area ||
            _inventoryHost?.Map(CurrentAreaMap) is not { } inventory)
        { GD.Print($"ARC-SKILLS-010 native-smoke pickpocket NONE map={_readyMapName} (no hero, combat values or inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SKILLS-010 pickpocket smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PICK_POCKET_SMOKE_FAILED");
        }
        if (InventoryOpen) EndInventory();
        var map = AreaRuntimes()[CurrentAreaMap];
        var mapId = new SessionMapId(map.RuntimeMap);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true;
            Require(AreaAiHostNow() is not null, "the AI host is up");
            bool Plain(SessionInventoryItem item) => item.Facts.Kind.Type is not (SessionItemType.Gold or SessionItemType.Ammo or SessionItemType.Key or SessionItemType.KeyRing)
                && !item.Facts.Worn && item.Facts.ScriptPoints.Count == 0;
            var holders = session.QueryMapInventory(mapId)!.Holders
                .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free && inventory.TryGetRow(h.Entity, out var row) &&
                    row.Scope == AreaScope.Core && row.WorldX is not null && combat.Fields(map.RuntimeMap, h.Entity) is { Critter: not null }
                    // ARC-WORLD-114: an NPC a script switched off is not drawn and cannot be pressed.
                    && !AreaRemovedObjects().Contains(row.Key))
                .OrderBy(h => h.Entity, StringComparer.Ordinal).ToArray();
            var refused = holders.FirstOrDefault(h => combat.Fields(map.RuntimeMap, h.Entity) is { Critter: { } c } && PickPocketRules.NotAllowed(c.Flags, c.Flags2));
            var candidates = holders.Where(h => h.Items.Any(Plain) && combat.Fields(map.RuntimeMap, h.Entity) is { Critter: { } c } && !PickPocketRules.NotAllowed(c.Flags, c.Flags2)).ToArray();
            string? victim = null; var tried = new List<string>();
            foreach (var holder in candidates.Take(12))
            {
                var row = inventory.Row(holder.Entity);
                var npcCell = map.Chart.ToChart(new(row.WorldX!.Value, row.WorldY!.Value));
                CellCoord? next = null;
                foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
                {
                    var near = new CellCoord(npcCell.X + dx, npcCell.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    CellCoord cell;
                    try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                    if (Math.Max(Math.Abs(cell.X - npcCell.X), Math.Abs(cell.Y - npcCell.Y)) == 1) { next = cell; break; }
                }
                if (next is not { } stand) { tried.Add($"{holder.Entity[^6..]}:no-cell"); continue; }
                AreaSmokePlace(map, stand);
                for (var i = 0; i < 3; i++) AreaSmokeFrame();
                ArmPickPocket();
                Require(_pickPocketArmed, "the palette's Pick pocket arms the skill: " + _inventoryStatus);
                _pickPocketArmed = false;
                StartPickPocket(holder.Entity);
                if (InventoryOpen && _inventoryPanel!.Holder == holder.Entity) { victim = holder.Entity; break; }
                tried.Add($"{holder.Entity[^6..]}:{_lastPickPocketStart?.Open?.ToString() ?? "none"}/{_lastPickPocketStart?.Detail}");
            }
            if (victim is null)
            {
                GD.Print($"ARC-SKILLS-010 native-smoke pickpocket NONE map={_readyMapName} (candidates={candidates.Length}; {string.Join("; ", tried)})");
                return;
            }
            var view = _inventoryPanel!.ViewForSelfTest!;
            Require(view.Steal && view.Holder == victim, "the window is in steal mode on the victim");
            var current = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == victim).Items;
            var item = view.Loot.Select(line => line.Entity).First(entity => current.Any(i => i.Entity == entity && Plain(i)));
            bool HeroHolds(string entity) => session.QueryPcInventory().Any(i => i.Entity == entity) || session.QueryMadeItems().Any(i => i.Entity == entity) ||
                session.QueryHeroKit().Any(i => i.Entity == entity);
            bool VictimHolds(string entity) => session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == victim).Items.Any(i => i.Entity == entity);
            void Fated()
            {
                Require(session.GrantHeroFatePoint(session.Version).Disposition == GameSessionDisposition.Changed, "a fate point");
                Require(session.ArmHeroFate(session.Version, FateKind.PickPocket).Disposition == GameSessionDisposition.Changed, "the pick-pocket fate armed");
            }
            var panel = _inventoryPanel;
            var grid = panel.GridAreaForSelfTest;
            var art = panel.ArtMode && grid.LootLook;
            Vector2? VictimPoint(string entity) =>
                panel.ViewForSelfTest!.LootGrid.FirstOrDefault(i => i.Entity == entity) is { } shown && AreaLootArtLayout.ItemRect(shown, grid.LootScroll) is { } r
                    ? new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2) : null;
            if (art)
            {
                Require(grid.Mode == AreaInventoryMode.Steal && grid.LookMode == AreaInventoryMode.Steal, "the frame draws the steal window (mode 3)");
                Require(!AreaLootArtLayout.HitTest(AreaLootArtLayout.TakeAll.X + 1, AreaLootArtLayout.TakeAll.Y + 1, 0, (0, 0)).TakeAll &&
                    !panel.TakeAllForSelfTest.Visible, "no Take all in the steal window");
                Require(view.HolderImage is { } image && grid.Look!.Art.ContainsKey(image), $"the victim's image {view.HolderImage?.ToString() ?? "none"} reads");
                Require(panel.RoundViewShown, "the steal window's round view shows the game view");
                if (System.Environment.GetEnvironmentVariable("ARC_AREA_STEAL_CAPTURE") is { Length: > 0 } capture)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = GetViewport().GetTexture().GetImage();
                    if (shot.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_STEAL_CAPTURE_FAILED");
                }
            }
            // The fated theft: a drag from the victim's grid to a free cell of the hero's in the original look, else the Steal button's take.
            var dragged = "-";
            // A drop is queued deferred: wait a few frames for its attempt (a capture's post-draw await can skip one flush).
            async Task Attempted()
            {
                for (var frame = 0; frame < 3 && _lastPickPocket is null; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            async Task Steal(string entity)
            {
                if (art && VictimPoint(entity) is { } from)
                {
                    var free = Enumerable.Range(0, SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(panel.ViewForSelfTest!.Grid, cell!.Value) is null);
                    Require(free is not null, "a free cell of the hero's grid for the theft");
                    grid.PressAt(from);
                    Require(grid.Held is { Partner: true } held && held.Entity == entity, "the press on the victim's grid holds its item");
                    grid.ReleaseAt(AreaInventoryGridArea.CellCentre(free!.Value));
                    await Attempted();
                    dragged = "steal";
                }
                else TakeFromBody(entity);
            }

            Fated();
            var alignment = session.QueryHeroPartySide()!.Alignment;
            _lastPickPocket = null;
            await Steal(item);
            Require(_lastPickPocket is { Result: { Disposition: GameSessionDisposition.Changed, Moved: 1, FateUsed: true, Noticed: false } }, "a fated theft: " + _inventoryStatus);
            Require(HeroHolds(item) && !VictimHolds(item) && InventoryOpen, "the item moved to the hero and the window stays: " + _inventoryStatus);
            var stolenAlignment = _lastPickPocket!.Value.Result.Alignment;
            Fated();
            _lastPickPocket = null;
            // The fated plant: a drag from the hero's grid to a free place of the victim's shown rows in the original look, else Plant.
            var placed = panel.ViewForSelfTest!.Grid.FirstOrDefault(i => i.Entity == item);
            var spot = Enumerable.Range(grid.LootScroll * AreaLootArtLayout.Columns, AreaLootArtLayout.ShownRows * AreaLootArtLayout.Columns)
                .Cast<int?>().FirstOrDefault(location => AreaLootArtLayout.ItemAt(panel.ViewForSelfTest!.LootGrid, location!.Value) is null);
            if (art && placed is not null && spot is { } location)
            {
                grid.PressAt(AreaInventoryGridArea.CellCentre(placed.Slot));
                Require(grid.Held is { Partner: false } held && held.Entity == item, "the press on the hero's grid holds the stolen item");
                grid.ReleaseAt(new(AreaLootArtLayout.GridX + (location % AreaLootArtLayout.Columns + .5f) * AreaLootArtLayout.CellPixels,
                    AreaLootArtLayout.GridY + (location / AreaLootArtLayout.Columns - grid.LootScroll + .5f) * AreaLootArtLayout.CellPixels));
                await Attempted();
                dragged += "+plant";
            }
            else _inventoryPanel.EmitPlantForSelfTest(item);
            Require(_lastPickPocket is { Result: { Moved: 1, FateUsed: true, Alignment: null } }, "a fated plant: " + _inventoryStatus);
            Require(!HeroHolds(item) && VictimHolds(item), "the item moved back to the victim: " + _inventoryStatus);

            int attempts = 0, successes = 0;
            SessionPickPocketResult? failed = null;
            AreaPickPocketCaught? caught = null;
            while (attempts < 10 && InventoryOpen)
            {
                var steal = session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == victim).Items.FirstOrDefault(Plain)?.Entity;
                if (steal is null) break;
                _lastPickPocket = null;
                // ARC-UI-065: the first plain theft is a right release on the victim's item in the original look (IW36).
                if (art && attempts == 0 && VictimPoint(steal) is { } point)
                {
                    grid.RightReleaseAt(point);
                    await Attempted();
                    dragged += "+right";
                }
                else TakeFromBody(steal);
                Require(_lastPickPocket is not null, "the attempt ran: " + _inventoryStatus);
                attempts++;
                var (result, consequences) = _lastPickPocket!.Value;
                Require(result.Disposition == GameSessionDisposition.Changed, $"an attempt runs: {result.Failure}");
                if (result.Moved > 0) { successes++; Require(HeroHolds(steal), "a successful theft moved the item"); continue; }
                failed = result; caught = consequences;
                Require(VictimHolds(steal), "a failure moves nothing");
                Require(result.Noticed == (consequences is not null) && result.Noticed == !InventoryOpen, "a noticed failure ran its consequences and closed the window");
                if (result.Noticed) break;
            }
            if (InventoryOpen) EndInventory();
            Require(_pickPocketVictim is null, "closing the window ends steal mode");

            var refusal = "NONE";
            if (refused is not null)
            {
                StartPickPocket(refused.Entity);
                Require(_lastPickPocketStart?.Open == PickPocketOpen.NotAllowed && !InventoryOpen, "a critter the flags refuse floats the refusal: " + _inventoryStatus);
                refusal = _inventoryStatus;
            }
            var training = session.QueryHeroSkills()![Skill.PickPocket].Training;
            GD.Print("ARC-SKILLS-010 native-smoke pickpocket PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Map = _readyMapName, Candidates = candidates.Length, Victim = victim[^6..], Training = training.ToString(),
                Look = art ? "original" : "diagnostic", Dragged = dragged,
                Alignment = $"{alignment}->{stolenAlignment?.ToString() ?? "same"}", Attempts = attempts, Successes = successes,
                Failure = failed is null ? "none" : $"critical={failed.Check!.Critical} die={failed.PerceptionRoll?.ToString() ?? "-"} noticed={failed.Noticed}",
                Caught = caught?.Detail ?? "-", HitBy = session.QueryDanger(victim).HitBy?.IsHero, Refusal = refusal,
                Lines = _gameBarContent?.Texts.GameSkillLineCount ?? 0,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)",
            }));
        }
        catch (Exception exception) when (exception is not InvalidOperationException { Message: "AREA_PICK_POCKET_SMOKE_FAILED" })
        {
            GD.Print("ARC-SKILLS-010 pickpocket smoke FAIL: " + exception);
            throw;
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            _pickPocketArmed = false; _pickPocketAfterRoute = null;
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
