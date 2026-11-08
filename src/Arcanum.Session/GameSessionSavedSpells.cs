using System.Collections.Immutable;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-210 (MIG14 S4, docs/architecture/saved-runs.md §3.5; schema 73, the <c>spells</c> section): the spell runs a save writes, with the maintain bar and the last
/// charged cost. The original saves the whole run table (PS11) and the maintained spells go on after a load. S4 wrote the <b>settled</b> runs, those in the
/// Maintain or End action, or in Begin with its BEGIN processed (their effect ticks by the <c>magic.run</c> events, which a save keeps for them). ARC-DEV-289 (S8, schema 82): a run
/// still before its BEGIN is written too when something saved waits to begin it: an NPC's casting goal that names it (before or after its release frame), the missile flying to release
/// it, or its pending BEGIN event (a run cast with no parent); the caster's release and missile marks are rebuilt from the saved missiles (<see cref="SessionSpellCaster"/>). A run
/// waiting for a host callback (Callback, EndCallback) is still cut with its events and the goals that name it, and so is a hero's run before its BEGIN (the hero's casting goal is
/// the viewer's until S9). The overlays a settled run started (eye candy loops, hover) are goal runs with a viewer host and are cut as well (S5): the restored run ticks to its END
/// without them. A game of an older schema has none.
/// </summary>
public sealed record SessionSpellState(IReadOnlyList<SpellRun> Runs, IReadOnlyList<MaintainSlot> Bar, int LastCost)
{
    public static SessionSpellState Empty { get; } = new([], [.. Enumerable.Repeat(MaintainSlot.Empty, MaintainBarRules.Slots)], 0);

    public bool IsEmpty => Runs.Count == 0 && LastCost == 0;
}

public sealed partial class GameSessionOwner
{
    // The settled runs (saved-spells above): the runs whose BEGIN has run and whose effect needs no host state: in the Maintain or End action, or still in Begin
    // with the Processed flag (a timed run with a trigger count stays there until its first tick turns it to Maintain). Callback and EndCallback wait for the host.
    internal static bool SpellRunIsSaved(SpellRun run) =>
        run.Action is SpellRunAction.Maintain or SpellRunAction.End || run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) != 0;

    // ARC-DEV-289: a run before its BEGIN (its casting goal's release, its missile's arrival or its BEGIN event begins it); saved with what waits to begin it (SavedCasts).
    internal static bool SpellRunAwaitsBegin(SpellRun run) => run.Action == SpellRunAction.Begin && (run.Flags & SpellRunFlags.Processed) == 0;

    private HashSet<int> SavedSpellRunSlots() => SavedCasts().Spells;

    private SessionSpellState CaptureSpells()
    {
        var saved = SavedSpellRunSlots();
        var bar = _maintainBar.Slots.Select(slot => slot.State != MaintainSlotState.Empty && saved.Contains(slot.Run) ? slot : MaintainSlot.Empty).ToArray();
        return new([.. _spellRuns.Runs.Where(run => saved.Contains(run.Id))], bar, _spellLastCost);
    }

    // Shapes the codec cannot know: the run table's slots, the entity strings of the objects a run names, the actions the build saves, and a bar that shows only runs that exist.
    // ARC-DEV-289: a run before its BEGIN is checked against what waits to begin it once the goals, flights and events are in (CheckCastRuns).
    private static GameSessionFailure ImportSpells(SessionSpellState saved, out SpellRunTable table, out MaintainBar bar)
    {
        table = SpellRunTable.Empty;
        bar = MaintainBar.Empty;
        if (saved.LastCost < 0 || saved.Runs.Count > SpellRunTable.Slots) return GameSessionFailure.InvalidSnapshot;
        foreach (var run in saved.Runs)
        {
            if (run.Id < 0 || run.Spell < 0 || !Enum.IsDefined(run.Action) || !SpellRunIsSaved(run) && !SpellRunAwaitsBegin(run)) return GameSessionFailure.InvalidSnapshot;
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
