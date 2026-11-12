using Arcanum.Simulation.Items;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SKILLS-1156 (ledger TT5): a new made item of <paramref name="kind"/> for the hero, as the disarm of a trap creates its device at the
    /// disarmer's location and gives it to the disarmer. The item is <c>made:n</c> with its prototype's weight facts
    /// (<see cref="AttachItemWeights"/>); it enters the hero's list when the hero can take it (the host's insert check, the weight, Technophobia and a
    /// free grid place, <see cref="HeroInsertVerdict"/>), else it stays where it was created, on the hero's tile (the answer's
    /// <see cref="InventoryMutationResult.Verdict"/> is then the refusal and the disposition still Changed, as a pick-up refused for room). Ammunition,
    /// gold, keys and key rings are not created here. One revision. Refused before any effect: no hero (<see cref="GameSessionFailure.InvalidItem"/>),
    /// the version, an item type the lists do not admit (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), no free made
    /// number (<see cref="GameSessionFailure.BudgetExceeded"/>; a pack at its item limit counts as no room and leaves the item on the tile), a tile the ground cannot hold when the item must lie there
    /// (<see cref="GameSessionFailure.DropTileUnfit"/>), the revision.
    /// </summary>
    public InventoryMutationResult CreateItemForHero(GameSessionVersion expected, SessionItemKind kind, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (_hero is null || !Enum.IsDefined(kind.Type)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (kind.Type == SessionItemType.Ammo || !InventoryDomain.Admitted(kind.Type) || kind.Type == SessionItemType.Gold) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        var weight = CreatedWeight(kind.Prototype);
        var verdict = HeroInsertVerdict(insertCheck(new SessionInventoryItem(entity, InventoryDomain.MadeFacts(kind) with { Weight = weight.Weight, MagicWeight = weight.MagicWeight })),
            weight.Complexity, new((int)kind.Type, weight.Weight, weight.MagicWeight, weight.Complexity), room: NewItemRoom(entity, kind));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.SeedGeneration != seeds) return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict == SessionInsertVerdict.Accepted && _inventory.Held >= _limits.MaxPcItems) verdict = SessionInsertVerdict.NoRoom;
        if (verdict == SessionInsertVerdict.Accepted) CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, weight, weight.Complexity));
        else
        {
            var (map, cell) = (_actor.Map, _actor.Cell);
            if (!DropCellFits(cell)) return RejectItem(GameSessionFailure.DropTileUnfit, verdict);
            CreateMadeItem(new(entity, kind, SessionCreatePlace.Ground, weight, weight.Complexity, Map: map, Cell: cell, Source: SessionPlacedSource.Made));
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, verdict, Version);
    }
}
