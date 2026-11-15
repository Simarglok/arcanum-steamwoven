using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-1925 (ledger IW35, IW36, IW45): the cross-holder moves of the hero's own doll in the loot and steal windows. The holder's item released on a slot of the hero's doll
    /// (IW35): the wear test first (PROJECT: the item's wear slot is the released one, the occupant rules are the session's), then in the steal window the
    /// hero's helper, a steal attempt that refuses an occupied slot (the attempt does not wear the item, it lands in the pack), in the loot window the holder's item is taken and worn
    /// (the buy-and-move executor; PROJECT: through the pack, so a full pack refuses a free slot too, where the original ignores the room test for a free slot), an occupant of the slot going on the cursor
    /// as the own window's swap does.
    /// </summary>
    private void TakeWearFromHolder(AreaInventoryDrop drop)
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder || _areaSession is not { } session) return;
        var (item, slot) = (drop.Held.Entity, drop.Slot!.Value);
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        string Back(string why) => $"{name} went back to its place ({why})";
        var kind = InventoryItemKind(session, item);
        if (kind is not { } itemKind || kind is { Type: SessionItemType.Gold or SessionItemType.Ammo })
        { RenderHeroDollMove(holder, Back(kind is null ? "no facts" : "a stack is NOT READY here")); return; }
        if (_inventoryHost.HolderItemWearable(session, item, itemKind) is not { } wearable || !AreaInventoryHost.WearSlots(wearable).Contains(slot))
        { GD.Print($"ARC-UI-1925 take-wear item={item} slot={slot} refused by the wear test"); RenderHeroDollMove(holder, Back("cannot be worn there")); return; }
        // The wear rules (two-handed weapon against a shield, race and gender of an armour) refuse before anything moves, as the original's wear test does.
        if (session.QueryWearRefusal(wearable, slot) is var refusal and not EquipmentRefusal.None)
        { GD.Print($"ARC-UI-1925 take-wear item={item} slot={slot} refused by the wear rules: {refusal}"); RenderHeroDollMove(holder, Back($"cannot be worn: {refusal}")); return; }
        var occupied = session.QueryEquipment().Worn.Any(entry => entry.Slot == slot);
        if (holder == _pickPocketVictim)
        {
            // Mode 3: an occupied slot does nothing (the steal onto it is refused); an empty one is one steal attempt.
            if (occupied) { RenderHeroDollMove(holder, Back("the slot is taken")); return; }
            GD.Print($"ARC-UI-1925 take-wear steal item={item} slot={slot}");
            PickPocketAttempt(PickPocketDirection.Steal, item);
            return;
        }
        var looted = _inventoryHost.Loot(session, holder, item);
        GD.Print($"ARC-UI-1925 take-wear loot item={item} slot={slot} disposition={looted.Disposition} failure={looted.Failure}{InsertScriptLog(looted)}");
        if (!AreaInventoryHost.Moved(looted)) { RenderHeroDollMove(holder, $"not taken: {AreaInventoryHost.NotMoved(looted)}"); return; }
        PlayTakenItemSound(item);
        var (result, message) = WearHeroItem(session, item, slot, name);
        SyncAreaInventory();
        _inventoryStatus = $"took {name}{AreaInventoryHost.Printed(looted)}; {message}";
        _inventoryPanel.Render(InventoryView(holder), _inventoryStatus);
        if (result is { Disposition: GameSessionDisposition.Changed, Displaced: { } displaced }) _inventoryPanel.Hold(new(displaced, null, 0));
        RefreshReadyStatus();
    }

    /// <summary>The wear move of a held-by-the-hero item into a slot through the session, as the own window's release makes it (IW10..IW13); the result and its message.</summary>
    private (EquipmentMutationResult? Result, string Message) WearHeroItem(GameSessionOwner session, string item, EquipmentSlot slot, string name)
    {
        if (_inventoryHost!.Wearable(session, item, _areaKitWear) is not { } wearable) return (null, $"not worn: {name} has no wear values here");
        var result = session.Equip(session.Version, item, slot, wearable, (_, _) => true, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), ItemTriggersNow());
        GD.Print($"ARC-UI-1925 wear item={item} slot={slot} disposition={result.Disposition} failure={result.Failure} refusal={result.Refusal} displaced={result.Displaced ?? "-"}");
        return (result, AreaInventoryHost.WearText(result, name, slot, false, entity => _inventoryHost.ItemName(session, entity, _hoverNames?.Catalog)));
    }

    /// <summary>
    /// ARC-UI-1925 (the loot window, mode 2; IW35, IW36): the hero's item, worn or in its pack, released on the holder's grid (or right-released) goes into the holder: the move executor removes it with the
    /// full unwear first (the session's move runs the wield-off and remove-item scripts and the unwear spells), then the holder's insert. PROJECT: the item goes to the holder's first free place, not the released cell; a stack
    /// (ammunition, gold) and a junk pile are NOT READY here and go back.
    /// </summary>
    private void GiveToHolder(AreaInventoryDrop drop)
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder || holder == _pickPocketVictim || _areaSession is not { } session) return;
        var item = drop.Held.Entity;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        var kind = InventoryItemKind(session, item);
        if (kind is null or { Type: SessionItemType.Gold or SessionItemType.Ammo } || SessionJunkPile.Number(holder) is not null)
        { RenderHeroDollMove(holder, $"{name} went back to its place ({(kind is null ? "no facts" : "a stack or a pile is NOT READY here")})"); return; }
        var triggers = ItemTriggersNow();
        var scripts = _inventoryHost.ItemScripts(session);
        var result = session.GiveItemToNpc(session.Version, holder, item, AreaInventoryHost.DiagnosticInsertCheck, triggers, scripts);
        if (result.Failure == GameSessionFailure.UnknownHolder) result = session.GiveItemToContainer(session.Version, holder, item, AreaInventoryHost.DiagnosticInsertCheck, triggers, scripts);
        GD.Print($"ARC-UI-1925 give item={item} holder={holder} from={drop.Held.Worn?.ToString() ?? "grid"} disposition={result.Disposition} failure={result.Failure}{InsertScriptLog(result)}");
        var message = AreaInventoryHost.Moved(result) ? $"put {name} into the holder{AreaInventoryHost.Printed(result)}" : $"not moved: {AreaInventoryHost.NotMoved(result)}";
        SyncAreaInventory();
        RenderHeroDollMove(holder, message);
    }

    private void RenderHeroDollMove(string holder, string message)
    {
        _inventoryStatus = message;
        _inventoryPanel!.Render(InventoryView(holder), message);
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-UI-1925 (IW35, IW45): the barter window's wear moves on the hero's doll. The hero's item released on another slot of its doll is worn there as the own window's
    /// release does (the wear test, then the move; the occupant goes on the cursor), and the merchant's item released on a slot is bought and then worn (the buy path: the price and the gold test first, then the move into
    /// the slot; PROJECT: the buy puts the item in the pack first, the wear follows; a taken slot puts the item back, as the original puts back the other party's item over an occupied slot).
    /// </summary>
    private void BarterWearDrop(AreaInventoryDrop drop)
    {
        if (!BarterOpen || _barter is null || !BarterActive) return;
        // The opening shop's session is not the area's: its wear moves are not built (the item goes back).
        if (_areaSession is not { } session || !ReferenceEquals(_barterSession, session)) { SyncAreaBarter(force: true); return; }
        var (item, slot) = (drop.Held.Entity, drop.Slot!.Value);
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        if (drop.Kind == AreaInventoryDropKind.BuyWear)
        {
            var kind = InventoryItemKind(session, item);
            var wearable = kind is { } itemKind ? _inventoryHost.HolderItemWearable(session, item, itemKind) : null;
            var occupied = session.QueryEquipment().Worn.Any(entry => entry.Slot == slot);
            var refusal = wearable is not null && AreaInventoryHost.WearSlots(wearable).Contains(slot) ? session.QueryWearRefusal(wearable, slot) : EquipmentRefusal.NotWieldable;
            if (kind is null or { Type: SessionItemType.Gold or SessionItemType.Ammo } || wearable is null || refusal != EquipmentRefusal.None || occupied)
            {
                GD.Print($"ARC-UI-1925 barter buy-wear item={item} slot={slot} put back (wearable={wearable is not null} refusal={refusal} occupied={occupied})");
                SyncAreaBarter(force: true);
                return;
            }
            if (!Trade(item, sell: false)) return;
            var (bought, text) = WearHeroItem(session, item, slot, name);
            _barterMessage = $"{_barterMessage}; {text}";
            SyncAreaBarter(force: true);
            if (bought is { Disposition: GameSessionDisposition.Changed, Displaced: { } displaced }) _barterPanel!.Hold(new(displaced, null, 0));
            return;
        }
        if (drop.Kind == AreaInventoryDropKind.SellWear)
        {
            // ARC-UI-2138 (IW35, BW5): the hero's item sold onto a slot of the merchant's doll, which the merchant wears. The wear test and a filled slot put it back silently.
            var sellable = _inventoryHost.Wearable(session, item, _areaKitWear);
            if (sellable is null) { GD.Print($"ARC-UI-2138 barter sell-wear item={item} slot={slot} put back (no wear facts)"); SyncAreaBarter(force: true); return; }
            var trade = _barter.SellOnSlot(item, slot, sellable);
            GD.Print($"ARC-UI-2138 barter sell-wear item={item} slot={slot} moved={trade.Moved} total={trade.Total} failure={trade.Result.Failure} text={trade.Text}");
            if (!string.IsNullOrEmpty(trade.Text)) _barterMessage = trade.Text;
            if (trade.Moved) PlayBarterTradeSound();
            SyncAreaBarter(force: true);
            return;
        }
        var (result, message) = WearHeroItem(session, item, slot, name);
        _barterMessage = message;
        SyncAreaBarter(force: true);
        if (result is { Disposition: GameSessionDisposition.Changed, Displaced: { } occupant }) _barterPanel!.Hold(new(occupant, null, 0));
    }
}

public partial class TerrainViewerApp
{
    private static Vector2 CentreOf((float X, float Y, float Width, float Height) rect) => new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

    /// <summary>
    /// ARC-UI-1925 smoke step of <c>loot-worn</c> (the loot window on a body, mode 2; the original look, the hero's doll shown): <paramref name="item"/>, which the hero holds in its pack, is worn on an empty slot it fits, then the worn
    /// item released on the holder's grid goes into the holder (the hero's slot is empty again), then the holder's grid item pressed and released on the hero's slot is taken and worn again; the run ends with the item taken off into
    /// the pack so the hero is as it was found. A named "not driven" line when the item fits no empty slot of the hero.
    /// </summary>
    private async Task<string> HeroDollLootMovesStepAsync(AreaInventoryGridArea grid, AreaInventoryPanel panel, GameSessionOwner session, string body, SessionMapId mapId, string item)
    {
        void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-1925 loot hero doll moves FAIL: " + why); throw new InvalidOperationException("ARC-UI-1925 loot hero doll moves"); }
        var heroWorn = session.QueryEquipment().Worn.Select(entry => entry.Slot).ToHashSet();
        var slots = _inventoryHost!.Wearable(session, item, _areaKitWear) is { } wearable ? AreaInventoryHost.WearSlots(wearable) : [];
        if (slots.Cast<EquipmentSlot?>().FirstOrDefault(candidate => !heroWorn.Contains(candidate!.Value)) is not { } slot) return $"not driven ({item} fits no empty slot of the hero)";
        async Task Frames(Func<bool> done) { for (var frame = 0; frame < 8 && !done(); frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        bool Wears() => session.QueryEquipment().Worn.Any(entry => entry.Slot == slot && entry.Entity == item);
        bool BodyHolds() => session.QueryMapInventory(mapId)!.Holders.Single(h => h.Entity == body).Items.Any(entry => entry.Entity == item);
        var (_, wearText) = WearHeroItem(session, item, slot, item);
        Require(Wears(), $"the hero wears {item} on {slot}: {wearText}");
        panel.Render(InventoryView(body), wearText);
        grid.SetBodyDoll(false); grid.SetHeroDoll(true);
        grid.PressAt(CentreOf(AreaHeroDollToggle.SlotRects[slot]));
        Require(grid.Held is { Partner: false } worn && worn.Entity == item && worn.Worn == slot, "a press on the hero's slot lifts the worn item");
        grid.ReleaseAt(new(AreaLootArtLayout.GridX + AreaLootArtLayout.CellPixels / 2f, AreaLootArtLayout.GridY + AreaLootArtLayout.CellPixels / 2f));
        await Frames(BodyHolds);
        string line;
        if (!BodyHolds())
        {
            // PROJECT: the give was refused (a script the session does not run, the insert check): the item stays worn and goes to the pack again.
            line = $"worn on {slot}; the give to the holder was refused ({_inventoryStatus})";
        }
        else
        {
            Require(!Wears(), "the hero's slot is empty after the give");
            var shown = panel.ViewForSelfTest!.LootGrid.FirstOrDefault(entry => entry.Entity == item);
            Require(shown is not null && AreaLootArtLayout.ItemRect(shown, grid.LootScroll) is not null, "the given item is among the holder's shown rows");
            grid.PressAt(CentreOf(AreaLootArtLayout.ItemRect(shown!, grid.LootScroll)!.Value));
            Require(grid.Held is { Partner: true } held && held.Entity == item, "a press on the holder's grid item holds it");
            grid.ReleaseAt(CentreOf(AreaHeroDollToggle.SlotRects[slot]));
            await Frames(Wears);
            Require(Wears() && !BodyHolds(), $"the holder's {item} released on the hero's {slot} slot is taken and worn");
            line = $"worn on {slot}, given to the holder, taken and worn again";
        }
        grid.SetHeroDoll(false);
        if (Wears())
        {
            var off = session.Unequip(session.Version, slot, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), ItemTriggersNow());
            Require(off.Disposition != GameSessionDisposition.Rejected, $"the item is taken off again: {off.Failure}");
            panel.Render(InventoryView(body), "taken off");
        }
        return line;
    }

    /// <summary>
    /// ARC-UI-1925 smoke step of <c>pickpocket</c> (the steal window, mode 3, the original look; a fate point armed by <paramref name="fated"/> before each certain attempt): the hero's worn item released on the victim's grid is one plant
    /// attempt (the plant helper allows a worn item), then stolen back and worn again; the victim's item released on an occupied slot of the hero's doll does nothing (the steal onto an occupied slot is refused). Named lines for what the
    /// hero and the victim do not offer.
    /// </summary>
    private async Task<string> HeroDollStealMovesStepAsync(AreaInventoryGridArea grid, AreaInventoryPanel panel, GameSessionOwner session, string victim, SessionMapId mapId,
        Action fated, Func<Task> attempted, Func<string, bool> heroHolds, Func<string, bool> victimHolds)
    {
        void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-1925 steal hero doll moves FAIL: " + why); throw new InvalidOperationException("ARC-UI-1925 steal hero doll moves"); }
        var parts = new List<string>();
        var wornNow = session.QueryEquipment().Worn.FirstOrDefault();
        if (wornNow?.Entity is null) parts.Add("plant of a worn item not driven (the hero wears nothing)");
        else
        {
            var (item, slot) = (wornNow.Entity, wornNow.Slot);
            var spot = Enumerable.Range(grid.LootScroll * AreaLootArtLayout.Columns, AreaLootArtLayout.ShownRows * AreaLootArtLayout.Columns)
                .Cast<int?>().FirstOrDefault(location => AreaLootArtLayout.ItemAt(panel.ViewForSelfTest!.LootGrid, location!.Value) is null);
            Require(spot is not null, "a free place of the victim's shown rows");
            grid.SetHeroDoll(true);
            fated();
            _lastPickPocket = null;
            grid.PressAt(CentreOf(AreaHeroDollToggle.SlotRects[slot]));
            Require(grid.Held is { Partner: false } held && held.Entity == item && held.Worn == slot, "a press on the hero's slot lifts the worn item");
            var location = spot!.Value;
            grid.ReleaseAt(new(AreaLootArtLayout.GridX + (location % AreaLootArtLayout.Columns + .5f) * AreaLootArtLayout.CellPixels,
                AreaLootArtLayout.GridY + (location / AreaLootArtLayout.Columns - grid.LootScroll + .5f) * AreaLootArtLayout.CellPixels));
            await attempted();
            grid.SetHeroDoll(false);
            if (_lastPickPocket is { Result.Moved: 1 })
            {
                Require(victimHolds(item) && session.QueryEquipment().Worn.All(entry => entry.Entity != item), "the planted worn item is the victim's and off the hero");
                // Steal it back (a drag onto the hero's grid) and wear it again.
                fated();
                _lastPickPocket = null;
                var shown = panel.ViewForSelfTest!.LootGrid.Single(entry => entry.Entity == item);
                var free = Enumerable.Range(0, SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(panel.ViewForSelfTest!.Grid, cell!.Value) is null);
                Require(free is not null && AreaLootArtLayout.ItemRect(shown, grid.LootScroll) is not null, "a free cell and the planted item shown");
                grid.PressAt(CentreOf(AreaLootArtLayout.ItemRect(shown, grid.LootScroll)!.Value));
                grid.ReleaseAt(AreaInventoryGridArea.CellCentre(free!.Value));
                await attempted();
                Require(heroHolds(item), "the planted item was stolen back");
                var (_, text) = WearHeroItem(session, item, slot, item);
                Require(session.QueryEquipment().Worn.Any(entry => entry.Slot == slot && entry.Entity == item), "the item is worn again: " + text);
                parts.Add($"worn {slot} planted and stolen back");
            }
            else parts.Add($"worn {slot} plant attempted, not moved ({_lastPickPocket?.Result.Failure.ToString() ?? "no result"})");
        }
        // A victim's item on an occupied slot of the hero's doll: nothing happens.
        var occupied = session.QueryEquipment().Worn.Select(entry => entry.Slot).ToHashSet();
        var offer = panel.ViewForSelfTest!.LootGrid.Where(entry => entry.Kind.Type is not (SessionItemType.Gold or SessionItemType.Ammo))
            .Select(entry => (Entry: entry, Slot: _inventoryHost!.HolderItemWearable(session, entry.Entity, entry.Kind) is { } wearable
                ? AreaInventoryHost.WearSlots(wearable).Cast<EquipmentSlot?>().FirstOrDefault(slot => occupied.Contains(slot!.Value)) : null))
            .FirstOrDefault(entry => entry.Slot is not null && AreaLootArtLayout.ItemRect(entry.Entry, grid.LootScroll) is not null);
        if (offer.Entry is null) parts.Add("steal onto an occupied slot not driven (no shown victim item fits an occupied slot)");
        else
        {
            grid.SetHeroDoll(true);
            _lastPickPocket = null;
            grid.PressAt(CentreOf(AreaLootArtLayout.ItemRect(offer.Entry, grid.LootScroll)!.Value));
            Require(grid.Held is { Partner: true }, "a press on the victim's item holds it");
            grid.ReleaseAt(CentreOf(AreaHeroDollToggle.SlotRects[offer.Slot!.Value]));
            for (var frame = 0; frame < 8; frame++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            grid.SetHeroDoll(false);
            Require(_lastPickPocket is null && !heroHolds(offer.Entry.Entity) && victimHolds(offer.Entry.Entity), "a steal onto an occupied slot did nothing");
            parts.Add($"steal onto occupied {offer.Slot} refused");
        }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// ARC-UI-1925 smoke step of <c>barter</c> (the live window, the original look, the hero's doll shown): a ware that fits an empty slot of the hero is pressed on the merchant's grid and released on that
    /// slot: it is bought and worn at once (the buy path with a slot); the worn item right-released on the doll is sold (the executor unwears it), so the hero ends without it. A named "not driven" line when no ware fits.
    /// </summary>
    private async Task<string> BarterHeroDollMovesStepAsync(GameSessionOwner session)
    {
        void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-1925 barter hero doll moves FAIL: " + why); throw new InvalidOperationException("ARC-UI-1925 barter hero doll moves"); }
        var panel = _barterPanel!;
        var area = panel.GridAreaForSelfTest;
        if (!area.HeroTogglesShown || panel.ArtView is not { } view) return "not driven (the original look or the toggle art does not read)";
        var worn = session.QueryEquipment().Worn.Select(entry => entry.Slot).ToHashSet();
        var gold = session.QueryGold();
        var pick = view.LootGrid.Where(ware => AreaBarterArtLayout.ItemRect(ware, area.LootScroll) is not null && ware.Kind.Type is not (SessionItemType.Gold or SessionItemType.Ammo))
            .Select(ware => (Ware: ware, Price: panel.View!.Wares.FirstOrDefault(line => line.Entity == ware.Entity)?.Quote.Price ?? int.MaxValue,
                Slot: _inventoryHost!.HolderItemWearable(session, ware.Entity, ware.Kind) is { } wearable
                    ? AreaInventoryHost.WearSlots(wearable).Cast<EquipmentSlot?>().FirstOrDefault(slot => !worn.Contains(slot!.Value)) : null))
            .FirstOrDefault(entry => entry.Slot is not null && entry.Price is > 0 and < int.MaxValue);
        if (pick.Slot is not { } target)
        {
            var detail = string.Join(", ", view.LootGrid.Select(ware => $"{ware.Kind.Type} shown={AreaBarterArtLayout.ItemRect(ware, area.LootScroll) is not null} " +
                $"price={panel.View!.Wares.FirstOrDefault(line => line.Entity == ware.Entity)?.Quote.Price.ToString() ?? "-"} " +
                $"slots={(_inventoryHost!.HolderItemWearable(session, ware.Entity, ware.Kind) is { } wearable ? string.Join("/", AreaInventoryHost.WearSlots(wearable)) : "no facts")}"));
            return $"not driven (no shown ware fits an empty slot of the hero; gold {gold}, worn {string.Join("/", worn)}; wares {detail})";
        }
        var item = pick.Ware.Entity;
        // The smoke hero may be poor: it is given what the ware costs (one revision), then the window reads it.
        if (pick.Price > gold)
        {
            Require(session.GiveGoldToHero(session.Version, pick.Price - gold).Disposition == GameSessionDisposition.Changed, "the gold for the ware");
            SyncAreaBarter(force: true);
            gold = session.QueryGold();
        }
        var slotPoint = CentreOf(AreaHeroDollToggle.SlotRects[target]);
        area.SetHeroDoll(true);
        area.PressAt(CentreOf(AreaBarterArtLayout.ItemRect(pick.Ware, area.LootScroll)!.Value));
        Require(area.Held is { Partner: true } held && held.Entity == item, "a press on the ware holds it");
        area.ReleaseAt(slotPoint);
        for (var frame = 0; frame < 8 && !session.QueryEquipment().Worn.Any(entry => entry.Slot == target); frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(session.QueryEquipment().Worn.Any(entry => entry.Slot == target && entry.Entity == item) && session.QueryGold() == gold - pick.Price,
            $"the ware released on the empty {target} slot was bought for {pick.Price} and worn: {_barterMessage}");
        // Sell it back: a right release on the worn item.
        area.RightReleaseAt(slotPoint);
        for (var frame = 0; frame < 8 && session.QueryEquipment().Worn.Any(entry => entry.Entity == item); frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sold = session.QueryEquipment().Worn.All(entry => entry.Entity != item);
        area.SetHeroDoll(false);
        if (!sold)
        {
            // PROJECT: the sale was refused (no price, the merchant's coins); the smoke takes it off so the hero keeps its slots.
            var off = session.Unequip(session.Version, target, _inventoryHost!.InsertCheck, _inventoryHost.ItemScripts(session), ItemTriggersNow());
            Require(off.Disposition != GameSessionDisposition.Rejected, "the unsold item is taken off: " + off.Failure);
        }
        return $"{item} bought for {pick.Price} and worn on {target}; {(sold ? "sold back by a right release on the worn item" : "the sale was refused, taken off")}";
    }
}
