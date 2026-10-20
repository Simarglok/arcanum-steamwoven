namespace Arcanum.Simulation.Magic;

/// <summary>What a cost formula needs to know about the caster (any critter, or another object): whether it is a critter
/// (PC or NPC), its race stat and the college it mastered.</summary>
/// <param name="IsCritter">The object type is PC or NPC.</param>
/// <param name="Race">The effective race stat; <see cref="SpellCostRules.DwarfRace"/> doubles costs.</param>
/// <param name="MasteredCollege">The mastered college 0..15, or −1.</param>
public readonly record struct SpellCaster(bool IsCritter, int Race, int MasteredCollege);

/// <summary>
/// ARC-MAGIC-003: the original's spell cost formulas (contract docs/world/spell-cost-runtime.md, checked in the 1.0.7.4
/// executable, ledger "Spell cost and payment"). There are three, and they disagree for odd costs and mastered colleges
/// (MG2, the original's behaviour, no switch):
/// <list type="bullet">
/// <item><see cref="RunCost"/>, charged at BEGIN: mastery halves rounding up (only for spells 0..79 of the mastered college),
/// the cheat flag makes it 1, a dwarf PC or NPC pays double;</item>
/// <item><see cref="CastCost"/>, shown by the spell UI and used by the AI: double for a dwarf (any object), then mastery halves
/// rounding toward zero (no 0..79 range test);</item>
/// <item><see cref="CheckCost"/>, the UI's and the casting animation's affordability check: no mastery (MG1, MG2).</item>
/// </list>
/// Arithmetic is the original's 32-bit integer arithmetic (a doubling wraps). The callers pass the record's values.
/// </summary>
public static class SpellCostRules
{
    public const int DwarfRace = 1, CollegeSpells = 80, Ranks = 5;

    /// <summary>The character points one spell rank costs to learn and refunds when unlearned (learning, unlearning, auto-level
    /// and the spell UI ask it): 1 for every spell.</summary>
    [OriginalFunction(0x4B1650, OriginalFunctionScope.Full)]
    public static int RankPointCost(int spell) => 1;

    /// <summary>The cost a cast is charged at BEGIN: <paramref name="cost"/> (the record's <c>Cost:</c>); for a spell 0..79 of the
    /// caster's mastered college halved toward zero and raised by 1 when <paramref name="cost"/> is odd; 1 under the cheat
    /// flag; doubled for a dwarf PC or NPC.</summary>
    [OriginalFunction(0x4507D0, OriginalFunctionScope.Partial)]
    public static int RunCost(int cost, int spell, SpellCaster caster, bool cheat)
    {
        if (spell is >= 0 and < CollegeSpells && caster.MasteredCollege == spell / Ranks)
            cost = cost / 2 + ((cost & 1) != 0 ? 1 : 0);
        if (cheat) cost = 1;
        return Dwarf(caster) ? unchecked(cost * 2) : cost;
    }

    /// <summary>The cost the affordability check of the spell UI tests: <paramref name="cost"/>, 1 under the cheat flag,
    /// doubled for a dwarf PC or NPC; no mastery.</summary>
    [OriginalFunction(0x4508A0, OriginalFunctionScope.Partial)]
    public static int CheckCost(int cost, SpellCaster caster, bool cheat)
    {
        if (cheat) cost = 1;
        return Dwarf(caster) ? unchecked(cost * 2) : cost;
    }

    /// <summary>The cost the casting animation's check tests before the animation (MG1): the cast spell's <c>Cost:</c> for a
    /// BEGIN, its maintenance cost otherwise; 1 under the cheat flag; doubled for a dwarf PC or NPC (the run's source, which
    /// pays); no mastery. A free run or a run without a source is not tested (<see cref="PreAnimationChecked"/>).</summary>
    [OriginalFunction(0x450940, OriginalFunctionScope.Partial)]
    public static int PreAnimationCost(bool begin, int cost, int maintainCost, SpellCaster source, bool cheat) =>
        CheckCost(begin ? cost : maintainCost, source, cheat);

    /// <summary>Whether the casting animation's check tests a run at all: not for a free run, nor without a source.</summary>
    public static bool PreAnimationChecked(bool freeRun, bool hasSource) => !freeRun && hasSource;

    /// <summary>The cost the spell UI shows and the AI weighs: <paramref name="cost"/> doubled for a dwarf (any object with
    /// that race), then halved toward zero when the spell's college (spell / 5, for any spell number) is the mastered one.</summary>
    [OriginalFunction(0x4B1660, OriginalFunctionScope.Partial)]
    public static int CastCost(int cost, int spell, SpellCaster caster)
    {
        if (caster.Race == DwarfRace) cost = unchecked(cost * 2);
        return caster.MasteredCollege == spell / Ranks ? cost / 2 : cost;
    }

    /// <summary>The maintenance the spell UI and the AI show: the maintenance cost doubled for a dwarf (any object), and the
    /// period doubled when the spell's college (spell / 5) is the mastered one; no halving of the cost.</summary>
    [OriginalFunction(0x4B16C0, OriginalFunctionScope.Full)]
    public static (int Cost, int Period) MaintainCost(int maintainCost, int period, int spell, SpellCaster caster) =>
        (caster.Race == DwarfRace ? unchecked(maintainCost * 2) : maintainCost,
         caster.MasteredCollege == spell / Ranks ? unchecked(period << 1) : period);

    /// <summary>Whether a BEGIN pays (the stage's BEGIN branch, 0x452F20): not a free run, a critter parent, not a technology
    /// record and not <c>ChargeBeginCost: 0</c>. The run's source pays (<see cref="RunCost"/>).</summary>
    public static bool BeginPays(bool freeRun, bool critterParent, bool technology, bool skipsBeginCost) =>
        !freeRun && critterParent && !technology && !skipsBeginCost;

    /// <summary>
    /// One recharge of a worn mana item (the recharge event, 0x459680): <c>2 × max(1, elapsed / 120)</c> points, at most
    /// <paramref name="remaining"/>, where <paramref name="elapsedGameSeconds"/> is the game time since the drain; the rest
    /// is rescheduled.
    /// </summary>
    [OriginalFunction(0x459680, OriginalFunctionScope.Partial)]
    public static (int Restored, int Remaining) Recharge(int elapsedGameSeconds, int remaining)
    {
        var restored = 2 * Math.Max(1, elapsedGameSeconds / 120);
        if (restored > remaining) restored = remaining;
        return (restored, remaining - restored);
    }

    private static bool Dwarf(SpellCaster caster) => caster.IsCritter && caster.Race == DwarfRace;
}
