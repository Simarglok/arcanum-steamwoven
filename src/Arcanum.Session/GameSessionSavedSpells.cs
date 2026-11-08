using System.Collections.Immutable;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-210 (MIG14 S4, docs/architecture/saved-runs.md §3.5; schema 73, the <c>spells</c> section): the spell runs a save writes, with the maintain bar and the last
/// charged cost. The original saves the whole run table (PS11) and the maintained spells go on after a load. This build writes the <b>settled</b> runs, those in the
/// Maintain or End action, or in Begin with its BEGIN processed (their effect ticks by the <c>magic.run</c> events, which a save now keeps for them): a run still before its
/// BEGIN (waiting for its casting goal's release frame, the caster's release and missile marks, or the parentless run's BEGIN event) or waiting for a host callback has host state that a
/// load does not rebuild, so it is cut with its events, as the goals that name it are cut (<c>GoalFrame.SpellRun</c>). The viewer refuses a save while a missile flies or the hero casts,
/// so no run waits on one there. The overlays a settled run started (eye candy loops, hover) are goal runs with a viewer host and are cut as well (S5): the restored run ticks to its END
/// without them. A game of an older schema has none.
/// </summary>
public sealed record SessionSpellState(IReadOnlyList<SpellRun> Runs, IReadOnlyList<MaintainSlot> Bar, int LastCost)
{
    public static SessionSpellState Empty { get; } = new([], [.. Enumerable.Repeat(MaintainSlot.Empty, MaintainBarRules.Slots)], 0);

    public bool IsEmpty => Runs.Count == 0 && LastCost == 0 && ObjectFlags.Count == 0;

    /// <summary>ARC-DEV-302 (schema 83): the flags the spell components wrote on critters (<see cref="GameSessionOwner.QueryObjectFlags"/>), ascending by entity.</summary>
    public IReadOnlyList<SessionObjectFlagRecord> ObjectFlags { get; init; } = [];
}

public sealed partial class GameSessionOwner
{
    // The runs a save writes (saved-spells above): the runs whose BEGIN has run and whose effect needs no host state: in the Maintain or End action, or still in Begin
    // with the Processed flag (a timed run with a trigger count stays there until its first tick turns it to Maintain). A Begin run not processed yet awaits its release or its
    // BEGIN event; Callback and EndCallback wait for the host. The restore applies the same test.
    internal static bool SpellRunIsSaved(SpellRun run) =>
        run.Action is SpellRunAction.Maintain or SpellRunAction.End || run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) != 0;

    private HashSet<int> SavedSpellRunSlots() => [.. _spellRuns.Runs.Where(SpellRunIsSaved).Select(run => run.Id)];

    private SessionSpellState CaptureSpells()
    {
        var saved = SavedSpellRunSlots();
        var bar = _maintainBar.Slots.Select(slot => slot.State != MaintainSlotState.Empty && saved.Contains(slot.Run) ? slot : MaintainSlot.Empty).ToArray();
        return new([.. _spellRuns.Runs.Where(SpellRunIsSaved)], bar, _spellLastCost) { ObjectFlags = CaptureObjectFlags() };
    }

    // Shapes the codec cannot know: the run table's slots, the entity strings of the objects a run names, the actions the build saves, and a bar that shows only runs that exist.
    private static GameSessionFailure ImportSpells(SessionSpellState saved, out SpellRunTable table, out MaintainBar bar)
    {
        table = SpellRunTable.Empty;
        bar = MaintainBar.Empty;
        if (saved.LastCost < 0 || saved.Runs.Count > SpellRunTable.Slots) return GameSessionFailure.InvalidSnapshot;
        foreach (var run in saved.Runs)
        {
            if (run.Id < 0 || run.Spell < 0 || !Enum.IsDefined(run.Action) || !SpellRunIsSaved(run)) return GameSessionFailure.InvalidSnapshot;
            foreach (var named in new[] { run.Source, run.Parent, run.Target, run.Other })
                if (named is not null && !SessionValidation.Entity(named.Id)) return GameSessionFailure.InvalidSnapshot;
        }
        var restored = SpellRunTable.Restore(saved.Runs);
        var restoredBar = MaintainBar.Restore(saved.Bar);
        if (restored is null || restoredBar is null) return GameSessionFailure.InvalidSnapshot;
        for (var slot = 0; slot < MaintainBarRules.Slots; slot++)
            if (restoredBar[slot].State != MaintainSlotState.Empty && restored[restoredBar[slot].Run] is null) return GameSessionFailure.InvalidSnapshot;
        table = restored;
        bar = restoredBar;
        return GameSessionFailure.None;
    }

    // The saved events of the runs a save keeps: a magic.run event of a run that is cut is left out (its run is gone, so the event would end nothing).
    private static bool KeepEvent(GameEvent e, HashSet<int> saved) =>
        e.Kind != SessionEventKinds.SpellRun || e.Payload.Values.Length > 1 && saved.Contains((int)e.Payload.Values[1]);
}
