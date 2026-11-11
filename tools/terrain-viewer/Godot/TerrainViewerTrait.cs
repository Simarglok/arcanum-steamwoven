using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-046 (MA-8 second slice, the host's half of ARC-MAGIC-040; ledger TR3, TR6): what the original does around the Trait component's monster body for the hero's equipment.
// The apply takes every worn item off (the nine slots 1000 to 1008, each unwielded to the inventory); the restore chooses the best items for every slot again (0x4654F0). The cast host tells it
// by the TraitApplied and TraitRestored lines; the work is queued and done at the next frame, because the cast host's log is flushed from inside a cast and the item functions may rebuild that host.
// A map NPC's worn items are the map data's and are not tracked by the session, so for an NPC only the body changes (ARC-MAGIC-046, TerrainViewerTraitBody).
public partial class TerrainViewerApp
{
    // The hero's pending equipment steps in order: true the strip of an apply, false the best items of a restore.
    private readonly Queue<bool> _traitEquipment = new();
    // For the smoke: what the last steps did (the slots taken off, the steps the best-items call made).
    private string _traitEquipmentResult = "none";

    /// <summary>A cast host log entry of the hero's monster body: queues the equipment step the original takes with it.</summary>
    private void TakeTraitEvent(SpellCastEvent entry)
    {
        if (entry.Kind is not (SpellCastEventKind.TraitApplied or SpellCastEventKind.TraitRestored) || entry.Object != SessionSpellCaster.Hero) return;
        if (_traitEquipment.Count >= 64) _traitEquipment.Clear();
        _traitEquipment.Enqueue(entry.Kind == SpellCastEventKind.TraitApplied);
    }

    /// <summary>The frame's equipment steps of the hero's monster body (before the hero's body is chosen, so the drawn body sees the worn items of that frame).</summary>
    private void DrainTraitEquipment()
    {
        while (_traitEquipment.Count > 0)
        {
            var strip = _traitEquipment.Dequeue();
            if (_areaSession is not { } session || _inventoryHost is not { } host || session.QueryHero() is null) { _traitEquipmentResult = "no session or inventory host"; continue; }
            try { if (strip) StripHeroItems(session, host); else ChooseHeroItems(session, host); }
            catch (InvalidOperationException error) { _traitEquipmentResult = "failed: " + error.Message; GD.Print("ARC-MAGIC-046 trait-equipment FAIL " + error.Message); }
        }
        // ARC-MAGIC-046: a transformed NPC's body (or its restore) reaches the canvas at once; the record is already in the session when the cast host's line is flushed.
        // This is cosmetic: a frame that cannot build it logs and goes on, it does not fail the world frame.
        try { if (SyncTraitBodies()) PublishWorldSelection(); }
        catch (InvalidOperationException error) { GD.Print("ARC-MAGIC-046 trait-bodies FAIL " + error.Message); }
    }

    // TR3: every slot, helmet to boots, taken off into the inventory. The original drops an item that stays (a refused take-off) on the holder's tile; the viewer leaves it worn and says so.
    private void StripHeroItems(GameSessionOwner session, AreaInventoryHost host)
    {
        var off = 0; var stayed = new List<string>();
        foreach (var worn in session.QueryEquipment().Worn.ToArray())
        {
            // The triggers first: building them may rebuild the cast host, which can end a spell goal and change the session.
            var triggers = ItemTriggersNow();
            var result = session.Unequip(session.Version, worn.Slot, host.InsertCheck, host.ItemScripts(session), triggers);
            if (result.Disposition == GameSessionDisposition.Changed) off++;
            else stayed.Add($"{worn.Slot}:{result.Failure}/{result.Refusal}");
        }
        _traitEquipmentResult = $"strip off={off} stayed=[{string.Join(",", stayed)}] worn={session.QueryEquipment().Worn.Count}";
        GD.Print($"ARC-MAGIC-046 trait-equipment {_traitEquipmentResult}");
    }

    // TR6: the best items for every slot again (the starting kit's choice); an item with a move script or item spells is not worn (the session's policy, named by its step).
    private void ChooseHeroItems(GameSessionOwner session, AreaInventoryHost host)
    {
        var result = session.EquipBest(session.Version, entity => host.Wearable(session, entity, _areaKitWear), (_, _) => true, host.InsertCheck);
        _traitEquipmentResult = $"best {result.Disposition} {result.Failure} steps={result.Steps.Count} worn={session.QueryEquipment().Worn.Count}";
        GD.Print($"ARC-MAGIC-046 trait-equipment {_traitEquipmentResult} wielded=[{string.Join(",", result.Steps.Where(s => s.Wielded is not null).Select(s => $"{s.Slot}:{s.Wielded}"))}] "
            + $"unsupported=[{string.Join(",", result.Steps.Where(s => s.Unsupported is not null).Select(s => s.Unsupported))}]");
    }
}
