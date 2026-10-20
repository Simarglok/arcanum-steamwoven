using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>
/// ARC-SCRIPT-014: the guard conditions of conditional map transitions (set <see cref="ScrAdmissionSet.S4"/>, ledger SG1) and
/// the teleport requests (ledger TR1, TR2), over one object each as the set's focus kinds give. The run's own loop, values and
/// objects stay in the interpreter; these are the parts of the original's condition and action functions that answer them.
/// </summary>
internal static class ScrGuards
{
    /// <summary>The nine wield locations the wielding condition walks.</summary>
    internal const int FirstWieldLocation = 1000, WieldLocations = 9;

    /// <summary>
    /// One guard over one object: PC quest state equals (6), holds an item named (17), is named and not mind-controlled
    /// (20), wields an item named in 1000..1008 (21), has the reputation (33). <paramref name="first"/> and
    /// <paramref name="second"/> are the clause's value operands after the object, as read.
    /// </summary>
    [OriginalFunction(0x4451B0, OriginalFunctionScope.Partial)]
    internal static bool Holds(int type, ScrObjectRef obj, int first, int second, IScrObjectQueries queries) => type switch
    {
        ScrCatalogue.Condition.PcQuestState => queries.QuestState(obj, first) == second,
        ScrCatalogue.Condition.HasItemNamed => queries.HoldsItemNamed(obj, first),
        ScrCatalogue.Condition.IsNamed => !queries.IsMindControlled(obj) && queries.NameOf(obj) == first,
        ScrCatalogue.Condition.IsWieldingItem => Enumerable.Range(FirstWieldLocation, WieldLocations).Any(location => queries.WieldedItemName(obj, location) == first),
        ScrCatalogue.Condition.HasReputation => queries.HasReputation(obj, first),
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Not a guard condition."),
    };

    /// <summary>
    /// A teleport (44: object, map, x, y) or a fade-and-teleport (102: seconds, sound, movie, object, map, x, y) as the run asks
    /// for it; <paramref name="values"/> are the clause's value operands in order, the object already resolved. A network
    /// game skips the fade-and-teleport; a single-player game never does.
    /// </summary>
    [OriginalFunction(0x447220, OriginalFunctionScope.Partial)]
    internal static ScrTeleport Teleport(int type, ScrObjectRef obj, IReadOnlyList<int> values) => type switch
    {
        ScrCatalogue.Action.Teleport => new(obj, values[0], values[1], values[2]),
        ScrCatalogue.Action.FadeAndTeleport => new(obj, values[3], values[4], values[5], new(values[0], values[1], values[2])),
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Not a teleport action."),
    };
}
