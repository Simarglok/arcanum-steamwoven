using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-044 (technology audit E4-c; the executable read CN1–CN6, ledger spell-runs): the control components that steer a target's component list, as the component table
/// (0x5B0BA8) has them: NoOp (type 0, 0x452F00, returns), Terminate (type 19, 0x4529D0: sets the loop index to the list's count, so the target's remaining components do not run),
/// ChargeNBranch (type 5, 0x451850: pays <c>Cost:</c> through the payment 0x450420 for the run's source and branches when the payment fails) and TestNBranch (type 20, 0x4529F0: tests one field of
/// the target critter against <c>TestVal:</c> and branches when the test fails). A branch sets the loop index to <c>Branch:</c> (−1: the count) and the loop goes on after it, so a
/// jump to <c>n</c> runs component <c>n + 1</c> next; <see cref="SpellRunSystem"/> already carries the jump. The list is the one of the current target; the jump does not leave it.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The TestNBranch field of the target's hit-point damage (<c>HP_Damage</c>, object field 29), the one test field modelled; the other three (<c>art_num</c> 1,
    /// <c>Teleport_Tile</c> 242, <c>obj_f_critter_fleeing_from</c> 230) are not ready.</summary>
    public const int HitPointDamageField = 29;

    [OriginalFunction(0x4529D0, OriginalFunctionScope.Full)]
    private static SpellComponentOutcome TerminateComponent() => new(JumpTo: SpellComponentOutcome.EndOfList);

    // A failed branch goes to Branch: (a value of −1, and any negative one, ends the list; the original indexes the list with a smaller negative value, not a data case).
    private static SpellComponentOutcome Branch(int branch) => new(JumpTo: branch < 0 ? SpellComponentOutcome.EndOfList : branch);

    // 0x451850: with no run source (handle 0) the component does nothing. The payment is the run's one (0x450420 with the record's cells flag, apply on, the component's cost and
    // the run's spell), so a cell record spends cells through the cell source whatever else the source has, and a spent item charge is saved as a BEGIN's is. A source the host
    // cannot make a payer of (a critter with no life state: the original would charge its fatigue) leaves the component not ready.
    [OriginalFunction(0x451850, OriginalFunctionScope.Full)]
    private SpellComponentOutcome? ChargeBranchComponent(SpellRun run, SessionSpellComponent component)
    {
        if (component.ChargeBranch is not { } data) return null;
        if (run.Source is not { } source) return SpellComponentOutcome.None;
        if (Payer(source) is not { } payer) return null;
        var payment = SpellPaymentRules.Pay(payer, data.Cost, true, run.Spell, Record(run.Spell).Payment, Cells);
        Pay(run.Id, source.Id, payment);
        return payment.Succeeded ? SpellComponentOutcome.None : Branch(data.Branch);
    }

    // 0x4529F0: only with a current target object that is a PC or NPC (the cached target type 0xF or 0x10); any other target does nothing. The tested value is the field's
    // (0x406CA0) and the comparison index (0 equal, 1 greater, 2 greater or equal, 3 less, 4 less or equal) says what passes; an index above 4 logs an error in the original and fails.
    // A field the host does not read is not ready (null), so the target is logged and its list goes on.
    [OriginalFunction(0x4529F0, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? TestBranchComponent(SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.TestBranch is not { } data) return null;
        if (target.Object is not { IsCritter: true } obj) return SpellComponentOutcome.None;
        if (data.Field != HitPointDamageField) return null;
        if (Life(obj.Id) is not { } life) return null;
        var field = life.Status.MaxHitPoints - life.Status.HitPoints;
        var passes = data.Comparison switch
        {
            0 => field == data.Value,
            1 => data.Value < field,
            2 => data.Value <= field,
            3 => field < data.Value,
            4 => field <= data.Value,
            _ => false,
        };
        return passes ? SpellComponentOutcome.None : Branch(data.Branch);
    }
}
