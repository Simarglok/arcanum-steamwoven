using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-TECHNOLOGY-007: the charge-cell facts of the hero as payer (it pays a cell-powered spell from its one power-cell
    /// stack), or null without a hero. The stack is the host's (the session holds no ammunition yet; consumption in session state
    /// waits for session ammunition).
    /// </summary>
    public CellPayer? QueryHeroCellPayer(CellStack? cells)
    {
        ThrowIfDisposed();
        return _hero is null ? null : new CellPayer(SpellPayerKind.Critter, CellOwnerKind.Critter, cells);
    }

    /// <summary>
    /// The charge-cell facts of an item the hero holds as payer: its complexity and the hero's magick/tech aptitude, the cells
    /// from the hero's power-cell stack (the item's parent). Null without a hero.
    /// </summary>
    public CellPayer? QueryHeroItemCellPayer(int itemComplexity, CellStack? cells)
    {
        ThrowIfDisposed();
        return _hero is null ? null : new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, cells, itemComplexity, HeroAptitude());
    }

    /// <summary>
    /// The engine's charge-cell port for a payer under the session's technology rules (TC1 in <see cref="GameRules.Technology"/>):
    /// each request returns the original's answer and keeps the plan (<see cref="ChargeCellSource.LastPlan"/>). Asking changes no
    /// session state; an applied plan is the host's to write until the session holds ammunition.
    /// </summary>
    public ChargeCellSource CellSource(CellPayer payer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(payer);
        return new ChargeCellSource(payer, _rules.Technology);
    }
}
