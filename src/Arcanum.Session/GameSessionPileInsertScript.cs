using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2855 (ledger NT1, AM4, audit TQ19): the insert-item script (point 21) of the pile a gold or ammunition move takes from. Contract: docs/world/inventory-runtime.md, docs/research/ledger/items.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    // What the insert-item script of a moved pile came to: Failure is the refusal of the move (None: go on), Runs the prepared runs to commit with the move (null: the pile has no insert-item script), Run the script's answer (ignored by the original).
    private readonly record struct PileInsert(GameSessionFailure Failure, ItemScriptRuns? Runs, ScrRunResult? Run, Action? Deliver = null)
    {
        internal static readonly PileInsert None = new(GameSessionFailure.None, null, null);

        /// <summary>The result of the committed move: the script's run, its prints and its runs, as the item moves report them.</summary>
        internal InventoryMutationResult Result(InventoryMutationResult moved)
        {
            if (Runs is null) return moved;
            // ARC-SCRIPT-2900: the runs of the standing host (the caller gave none) send their lines to the host's output, once the move is applied.
            Deliver?.Invoke();
            return moved with { InsertScript = Run, Scripts = Runs.Runs, Messages = Deliver is null ? Runs.Messages : [] };
        }

        /// <summary>The refusal that keeps the runs made (also on a refusal, as the item moves do).</summary>
        internal InventoryMutationResult Refused(InventoryMutationResult refusal) => Runs is null ? refusal : refusal with { InsertScript = Run, Scripts = Runs.Runs };
    }

    /// <summary>
    /// ARC-SCRIPT-2855 (ledger NT1, AM4, the gold and ammunition-unit moves of the original): a move of coins or ammunition units that has a destination fires the insert-item event (21) on the source pile first, with the destination as the triggerer and the
    /// pile as the attachee, before the pile is changed; the answer cannot refuse. The pile's script is the one its seed row lists, or for a made or kit pile its slot or prototype array; the host answers for the shown map's rows, so a pile with the point on another map's row, or without a
    /// <paramref name="scripts"/> host, is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), as is a run the session cannot apply (the move is refused whole, never made with the script skipped). The runs are made and prepared here and the
    /// caller commits them with the move (<see cref="PileInsert.Result"/>). A pile without the point is no run at all.
    /// </summary>
    private PileInsert RunPileInsert(ISessionItemScriptHost? scripts, string pile, string triggerer, bool npcTriggerer, string? removedFrom = null) => RunPileInserts(scripts, [pile], triggerer, npcTriggerer, removedFrom);

    /// <summary>The same for several piles one move takes from, in order (the barter window's opening takes every gold pile of the merchant's container): one run set, so a later run reads what the earlier ones did; the answer kept is the last run's.</summary>
    private PileInsert RunPileInserts(ISessionItemScriptHost? scripts, IReadOnlyList<string> piles, string triggerer, bool npcTriggerer, string? removedFrom = null)
    {
        bool Has(InventoryDomain.Seed seed, string pile, int point) => seed.Items.TryGetValue(pile, out var row) && row.Facts.HasScript(point);
        // A made or kit pile has no seed row: its script is the slot it was written or the array of its prototype (the session's slots, InheritedSlot).
        bool MadeHas(string pile, int point) => _inventory.ScriptSlots.ContainsKey((pile, point)) || InheritedSlot(pile, point) is not null;
        bool Scripted(string pile, int point) => _inventory.Seeds.Values.Any(seed => Has(seed, pile, point)) || MadeHas(pile, point);
        // ARC-SCRIPT-2879 (ledger IE3, TQ20; exe read 0x464830, 0x4658E0 → 0x43CCA0 → 0x467860): a pile the move empties (removedFrom: the holder it leaves, the triggerer of the removal) is destroyed, and the removal runs its remove-item script (28) with
        // the holder as the triggerer, after the insert-item event (the destroy script, point 2, comes between and is not run). The same run set, so the removal reads what the insert-item run did.
        var scripted = piles.Where(pile => Scripted(pile, SessionScriptPoint.InsertItem) || removedFrom is not null && Scripted(pile, SessionScriptPoint.RemoveItem)).ToArray();
        if (scripted.Length == 0) return PileInsert.None;
        // ARC-SCRIPT-2900: a call given no host runs through the standing one (null: refused, as before).
        scripts = ResolveItemScripts(scripts, out var standing);
        bool Shown(string pile, int point) => !Scripted(pile, point) || MadeHas(pile, point) || _inventory.Seeds.TryGetValue(_actor.Map, out var shown) && Has(shown, pile, point);
        if (scripts is null || scripted.Any(pile => !Shown(pile, SessionScriptPoint.InsertItem) || removedFrom is not null && !Shown(pile, SessionScriptPoint.RemoveItem))) return new(GameSessionFailure.ItemScriptUnsupported, null, null);
        var (revision, map, seeds, flagVersion) = (_revision, _actor.Map, _inventory.SeedGeneration, _flags.Version);
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, scripted.ToDictionary(pile => pile, _ => 0, StringComparer.Ordinal)) { NpcTriggerer = npcTriggerer };
        ScrRunResult? last = null;
        foreach (var pile in scripted)
        {
            if (Scripted(pile, SessionScriptPoint.InsertItem))
            {
                runs.NpcTriggerer = npcTriggerer;
                last = runs.Run(SessionScriptPoint.InsertItem, pile, triggerer, null) ?? last;
                if (runs.Failure != GameSessionFailure.None) break;
            }
            if (removedFrom is not null && Scripted(pile, SessionScriptPoint.RemoveItem))
            {
                runs.NpcTriggerer = KnownNpc(removedFrom);
                runs.Run(SessionScriptPoint.RemoveItem, pile, removedFrom, null);
                if (runs.Failure != GameSessionFailure.None) break;
            }
        }
        ThrowIfDisposed();
        // A host callback changed nothing the runs rest on: the revision, the attached seeds, the shown map and the flags.
        if (_revision != revision || _actor.Map != map || _inventory.SeedGeneration != seeds || _flags.Version != flagVersion) return new(GameSessionFailure.StaleVersion, runs, last);
        if (runs.Failure == GameSessionFailure.None) runs.Prepare();
        return new(runs.Failure, runs, last, standing ? () => DeliverStanding(runs) : null);
    }
}
