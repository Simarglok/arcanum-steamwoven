using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-365 (T-g, ledger IA12): the spell path of the hero's use of an item on a tile (0x462FC0), the tile twin of
/// <see cref="SessionItemTriggers.UseItem"/> (0x462CC0). The use on a tile runs no scroll test and no use script (they belong to the object use, IA12), so the host
/// asks neither; it is taken when the item's charges are not 0 or it has the magical flag. In order: the slot-0 cast (0x4F27F0, 0x4CB7A0, 0x4605E0 → the activation
/// callback 0x57CE30): nothing for an empty slot or a record with item triggers; otherwise the activation (0x57BC70, <see cref="SpellUiRules.Activate"/> with the
/// holder as caster) casts at the holder's own tile when the record's BEGIN area asks for no target, else the cast goes to the tile that was clicked (0x57C110,
/// <see cref="SessionSpellCaster.CastAt"/> with the item as source) and its casting goal plays at once with its end; then the item used executor (0x4CBF70) with
/// <b>no target</b> (the object use passes its target), and for a food or scroll the spend. It lives in its own type so the item use of the cast host stays as it is.
/// </summary>
public sealed class SessionItemTileUse(SessionSpellCaster caster, ISessionCombatWorld combat)
{
    private readonly SessionSpellCaster _caster = caster ?? throw new ArgumentNullException(nameof(caster));
    private readonly ISessionCombatWorld _combat = combat ?? throw new ArgumentNullException(nameof(combat));

    public SessionItemUseResult UseItemOnTile(string item, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_caster.Item(item) is not { } spells) return new(SessionItemUseOutcome.Unknown);
        if (spells.Charges == 0 && !spells.Magical) return new(SessionItemUseOutcome.NoMagic);
        var (cast, attempt, goal) = CastSlotZero(spells, tile);
        var used = Played([], _caster.StartItemSpells(ItemSpellTriggerRules.ItemUsed, item, spells.Holder, null));
        var spent = spells.Type is SessionItemSpells.FoodType or SessionItemSpells.ScrollType && _caster.SpendUsedItem(item);
        return new(SessionItemUseOutcome.Used, cast, attempt, goal, used, spent);
    }

    private (SessionItemUseCast, SpellCastAttempt?, SpellCastGoalStart?) CastSlotZero(SessionItemSpells item, SpellLocation tile)
    {
        if (item.Spells.Count == 0 || item.Spells[0] is var value && (value == ItemSpellTriggerRules.NoSpell || value is < 0 or >= SpellPaymentRules.Spells))
            return (SessionItemUseCast.NoSpell, null, null);
        var spell = value;
        if (_caster.SpellRecord(spell) is not { } record) return (SessionItemUseCast.NoSpell, null, null);
        if (record.Data.ItemTriggers != 0) return (SessionItemUseCast.Triggered, null, null);
        // The activation casts nothing for a holder that is not a PC (NU2); the hero is the only user of the cursor.
        if (item.Holder != SessionSpellCaster.Hero) return (SessionItemUseCast.Nothing, null, null);
        var begin = record.Areas.Length > 0 ? record.Areas[0].Targets : 0;
        var life = _caster.HeroLife;
        var active = life is { IsDead: false, IsUnconscious: false };
        if (active && record.Data.Enabled && SpellUiRules.TargetsInventory(begin)) return (SessionItemUseCast.InventoryTarget, null, null);
        var activation = SpellUiRules.Activate(active, record.Data.Enabled, begin, record.Resist.Aggressive, altHeld: false, shiftHeld: false, actionPointsAllow: true);
        SessionItemUseCast cast;
        SpellCastAttempt attempt;
        switch (activation.Kind)
        {
            case SpellActivationKind.Nothing: return (SessionItemUseCast.Nothing, null, null);
            case SpellActivationKind.CastAtCaster:
                cast = SessionItemUseCast.AtCaster;
                attempt = _caster.CastAt(SessionSpellCaster.Hero, spell, null, _caster.Facts(SessionSpellCaster.Hero)?.Location ?? SpellLocation.None, item.Entity);
                break;
            default:
                cast = SessionItemUseCast.AtTarget;
                attempt = _caster.CastAt(SessionSpellCaster.Hero, spell, null, tile, item.Entity);
                break;
        }
        return (cast, attempt, attempt.RunId is { } run ? Play(run, castGoal: true) : null);
    }

    private SessionItemTriggerResult Played(IReadOnlyList<int> stopped, IReadOnlyList<SessionItemSpellStart> spells) =>
        new(stopped, spells, spells.Select(spell => spell.Run is { } run ? Play(run, spell.Firing.CastGoal) : null).ToArray());

    private SpellCastGoalStart Play(int run, bool castGoal)
    {
        var start = _caster.StartGoal(run, _combat);
        if (start.Outcome == SpellCastGoalOutcome.Started) _caster.Release(run);
        if (castGoal) _caster.EndGoal(run, refused: start.Outcome != SpellCastGoalOutcome.Started);
        else if (start.Outcome != SpellCastGoalOutcome.Started) _caster.Act(new(SessionSpellRunActionKind.Free, run));
        return start;
    }
}
