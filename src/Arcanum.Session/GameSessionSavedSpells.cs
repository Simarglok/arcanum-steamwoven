using System.Collections.Immutable;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-210 (MIG14 S4, docs/architecture/saved-runs.md §3.5; schema 73, the <c>spells</c> section): the spell runs a save writes, with the maintain bar and the last
/// charged cost. The original saves the whole run table (PS11) and the maintained spells go on after a load. This build writes the <b>settled</b> runs, those in the
/// Maintain or End action (their effect ticks by the <c>magic.run</c> events, which a save now keeps for them): a run still before its BEGIN, or waiting for an animation's
/// callback or a missile's arrival, has host state (a casting goal, the caster's release and missile marks, an eye candy) that a load does not rebuild, so it is cut with its
/// events, as the goals that name it are cut (<c>GoalFrame.SpellRun</c>). The viewer refuses a save while a missile flies or the hero casts, so no run waits on one there.
/// A game of an older schema has none.
/// </summary>
public sealed record SessionSpellState(IReadOnlyList<SpellRun> Runs, IReadOnlyList<MaintainSlot> Bar, int LastCost)
{
    public static SessionSpellState Empty { get; } = new([], [.. Enumerable.Repeat(MaintainSlot.Empty, MaintainBarRules.Slots)], 0);

    public bool IsEmpty => Runs.Count == 0 && LastCost == 0;
}

public sealed partial class GameSessionOwner
{
    // The runs a save writes (saved-spells above): the actions whose effect needs no host state. The restore applies the same test.
    internal static bool SpellRunIsSaved(SpellRun run) => run.Action is SpellRunAction.Maintain or SpellRunAction.End;

    private HashSet<int> SavedSpellRunSlots() => [.. _spellRuns.Runs.Where(SpellRunIsSaved).Select(run => run.Id)];

    private SessionSpellState CaptureSpells()
    {
        var saved = SavedSpellRunSlots();
        var bar = _maintainBar.Slots.Select(slot => slot.State != MaintainSlotState.Empty && saved.Contains(slot.Run) ? slot : MaintainSlot.Empty).ToArray();
        return new([.. _spellRuns.Runs.Where(SpellRunIsSaved)], bar, _spellLastCost);
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
    private bool KeepEvent(GameEvent e) =>
        e.Kind != SessionEventKinds.SpellRun || e.Payload.Values.Length > 1 && SavedSpellRunSlots().Contains((int)e.Payload.Values[1]);
}
