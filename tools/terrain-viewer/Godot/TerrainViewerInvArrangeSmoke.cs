using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-339, bug B54; ARC_AREA_MAP_SMOKE_ONLY=inv-arrange, map mode with --hero): the own inventory window in the
    // game's art draws the arrange button (art 340, 763, 197); with one of the hero's items first moved to the grid's last free cell, a press and
    // release on the button (nothing held) plays 3000 and 3001 and lays the grid out row by row (the tallest, widest item at cell 0, every item
    // inside the grid, no shared cell, the same items, one revision); the next press lays it out column by column; the third press gives the
    // first layout again (the layout depends on the list order and the sizes only, and the flag alternates). NONE without a hero, with fewer than
    // two grid items, or without the window's art.
    private async Task RunAreaInvArrangeSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost?.Map(CurrentAreaMap) is null)
        { GD.Print($"ARC-DEV-339 native-smoke inv-arrange NONE map={_readyMapName} (no hero or inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-339 inv-arrange smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_INV_ARRANGE_SMOKE_FAILED");
        }
        if (InventoryOpen) EndInventory();
        // A hero's kit may be one item: copies of its first kit item are laid on the ground next to the hero and picked up until the grid holds four.
        var kitItem = session.QueryHeroKit().FirstOrDefault();
        for (var made = 0; kitItem.Entity is not null && session.QueryHeroGrid().Items.Count < 4 && made < 4; made++)
        {
            var drop = AreaDropMapNow();
            if (drop is null) break;
            var created = session.CreateGroundItem(session.Version, kitItem.Kind, new SessionMapId(AreaRuntimes()[CurrentAreaMap].RuntimeMap), session.QueryActor().Cell, drop, DropOwner.PcSide);
            if (created.Disposition != GameSessionDisposition.Changed || created.Item is not { } entity || !AreaInventoryHost.Moved(_inventoryHost.PickUp(session, entity))) break;
        }
        var before = session.QueryHeroGrid().Items.ToArray();
        if (session.QueryHeroGrid().Unplaced.Count > 0) { GD.Print($"ARC-DEV-339 native-smoke inv-arrange NONE map={_readyMapName} (an item waits outside the grid)"); return; }
        if (before.Length < 2) { GD.Print($"ARC-DEV-339 native-smoke inv-arrange NONE map={_readyMapName} (the hero's grid holds {before.Length} item(s))"); return; }
        // One item of the grid goes to the last free cell that takes it, so the window is not already arranged.
        var scattered = false;
        foreach (var item in before)
        {
            if (_inventoryHost.GridMoveItem(session, item.Entity, _areaKitGridMove) is not { } facts) continue;
            for (var slot = SessionHeroGrid.Slots - 1; slot >= 0 && !scattered; slot--)
                scattered = session.MoveHeroItem(session.Version, item.Entity, slot, facts, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session)).Disposition
                    == GameSessionDisposition.Changed;
            if (scattered) break;
        }
        Require(scattered, "an item of the grid moves to a far cell");
        OpenInventory();
        if (!InventoryOpen || _inventoryPanel is not { ArtMode: true } panel || panel.GridAreaForSelfTest is not { LootLook: false, Look: not null } grid)
        { GD.Print($"ARC-DEV-339 native-smoke inv-arrange NONE map={_readyMapName} (the window's art did not read)"); if (InventoryOpen) EndInventory(); return; }
        Require(grid.ArrangeShown, "the arrange button's art (340) is drawn");
        var art = grid.Look!.Art[AreaInventoryArtLayout.ArrangeArt];
        Require((art.GetWidth(), art.GetHeight()) == (28, 27), $"the arrange art is 28 x 27, was {art.GetWidth()} x {art.GetHeight()}");
        var centre = new Vector2(AreaInventoryArtLayout.ArrangeButton.X + art.GetWidth() / 2f, AreaInventoryArtLayout.ArrangeButton.Y + art.GetHeight() / 2f);
        Require(grid.ArrangeAt(centre) && !grid.ArrangeAt(new Vector2(AreaInventoryArtLayout.ArrangeButton.X - 1, centre.Y)) && !grid.ArrangeAt(Vector2.Zero), "the button's rectangle is the art's");
        var sounds = new List<int>();
        panel.SoundRequested += sounds.Add;
        async Task<(IReadOnlyList<SessionHeroGridItem> Items, long Revision)> Press()
        {
            var flag = _arrangeLayout; var revision = session.Version.Revision; sounds.Clear();
            grid.PressAt(centre); grid.ReleaseAt(centre);
            for (var frame = 0; frame < 6 && _arrangeLayout == flag; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_arrangeLayout != flag, "the release reaches the app (the layout flag flips)");
            Require(sounds.SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]), $"the press plays 3000 and the release 3001, was [{string.Join(',', sounds)}]");
            return (session.QueryHeroGrid().Items.ToArray(), session.Version.Revision - revision);
        }
        void Check(IReadOnlyList<SessionHeroGridItem> items, string layout)
        {
            Require(items.Select(item => item.Entity).Order().SequenceEqual(before.Select(item => item.Entity).Order()), $"{layout}: the same items are in the grid");
            _ = InventoryGridRules.Occupied(items.Select(item => new InventoryGridItem(item.Slot, item.Width, item.Height)), SessionHeroGrid.Rows); // throws outside the grid
            var cells = items.Sum(item => item.Width * item.Height);
            var marked = InventoryGridRules.Occupied(items.Select(item => new InventoryGridItem(item.Slot, item.Width, item.Height)), SessionHeroGrid.Rows).Count(cell => cell);
            Require(marked == cells, $"{layout}: no two items share a cell ({marked} marked of {cells})");
            var first = items.OrderBy(item => item.Slot).First();
            var largest = items.OrderByDescending(item => item.Height).ThenByDescending(item => item.Width).First();
            Require(first.Slot == 0 && (first.Height, first.Width) == (largest.Height, largest.Width), $"{layout}: the tallest, widest item lies at cell 0");
        }
        var scatterSlot = session.QueryHeroGrid().Items.ToDictionary(item => item.Entity, item => item.Slot);
        var (rows, rowRevision) = await Press();
        Check(rows, "rows"); Require(rowRevision == 1, $"the first press is one revision, was {rowRevision}");
        Require(rows.Any(item => scatterSlot[item.Entity] != item.Slot), "the first press moved something");
        var (columns, _) = await Press();
        Check(columns, "columns");
        var (rowsAgain, _) = await Press();
        Check(rowsAgain, "rows again");
        Require(rowsAgain.OrderBy(item => item.Entity, StringComparer.Ordinal).SequenceEqual(rows.OrderBy(item => item.Entity, StringComparer.Ordinal)), "the third press gives the first layout again");
        panel.SoundRequested -= sounds.Add;
        EndInventory();
        GD.Print($"ARC-DEV-339 native-smoke inv-arrange PASS map={_readyMapName} items={rows.Count} rows=[{string.Join(' ', rows.OrderBy(i => i.Slot).Select(i => $"{i.Slot}:{i.Width}x{i.Height}"))}] " +
            $"columns=[{string.Join(' ', columns.OrderBy(i => i.Slot).Select(i => $"{i.Slot}:{i.Width}x{i.Height}"))}]");
    }
}
