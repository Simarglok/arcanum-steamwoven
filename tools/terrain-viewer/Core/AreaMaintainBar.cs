using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Magic;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What a maintain-bar slot shows: a closed slot (its SplSlt art with its plugged art over it, taking no pointer), an open
/// empty slot (its SplSlt art), or a slot holding a run (the run's spell icon as the button's art).</summary>
public enum AreaMaintainSlotKind { Closed, Empty, Run }

/// <param name="Art">The interface art the slot's button shows: the SplSlt art, or the spell's icon (null when the spell has none).</param>
/// <param name="Spell">The run's spell for a <see cref="AreaMaintainSlotKind.Run"/> slot; −1 otherwise.</param>
public sealed record AreaMaintainSlotLook(AreaMaintainSlotKind Kind, int? Art, int Spell, int Run);

/// <summary>
/// ARC-MAGIC-026: the original's maintain bar on the top strip (ledger MB1–MB9, 0x5533C0 and 0x553670): five push buttons at
/// (281 + 50 i, 3) whose art is SplSlt1–5 (188–192) while empty and the run's spell icon (0x458AE0 → 0x4B1570) while a run holds the
/// slot; a slot at or beyond the PC's intelligence / 4 is closed, its button disabled and its plugged art SP_Plugged1–5 (628–632)
/// drawn one pixel up and left of the button. Godot-free.
/// </summary>
public static class AreaMaintainBarLayout
{
    public const int FirstX = 281, Step = 50, Y = 3;
    public const int FirstSlotArt = 188, FirstPluggedArt = 628;

    public static int SlotX(int slot) => FirstX + Step * slot;
    public static int SlotArt(int slot) => FirstSlotArt + slot;
    public static int PluggedArt(int slot) => FirstPluggedArt + slot;

    /// <summary>Where a closed slot's plugged art is drawn: one pixel up and left of its button (0x5533C0).</summary>
    public static (int X, int Y) PluggedPlace(int slot) => (SlotX(slot) - 1, Y - 1);

    /// <summary>The slot and plugged arts the bar reads with the bars' art.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [.. Enumerable.Range(0, MaintainBarRules.Slots).Select(SlotArt), .. Enumerable.Range(0, MaintainBarRules.Slots).Select(PluggedArt)];

    /// <summary>The five slots' looks: a slot holding a run (shown or stopping) shows its spell's icon; an empty slot its SplSlt art,
    /// closed at or beyond <paramref name="open"/> (0x553620/0x5533C0: the plugged art is drawn only for an empty slot).</summary>
    public static IReadOnlyList<AreaMaintainSlotLook> Looks(MaintainBar bar, int open, Func<int, int?> spellOfRun)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(spellOfRun);
        return [.. Enumerable.Range(0, MaintainBarRules.Slots).Select(i =>
        {
            var slot = bar[i];
            if (slot.State != MaintainSlotState.Empty && spellOfRun(slot.Run) is { } spell)
                return new AreaMaintainSlotLook(AreaMaintainSlotKind.Run, HotkeyBarRules.SpellIcon(spell), spell, slot.Run);
            return new AreaMaintainSlotLook(i < open ? AreaMaintainSlotKind.Empty : AreaMaintainSlotKind.Closed, SlotArt(i), -1, -1);
        })];
    }

    /// <summary>
    /// The slots a drop of the open count closes while they hold a run (0x57C4B0 → 0x553910 → 0x553670): a slot turning closed whose
    /// button shows a spell icon is clicked (0x57C370), so its run is stopped. Only when the count changed from a known one.
    /// </summary>
    public static IReadOnlyList<int> ClosingRuns(MaintainBar bar, int? before, int open)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (before is not { } was || open >= was) return [];
        return [.. Enumerable.Range(open, MaintainBarRules.Slots - open).Where(i => bar[i].State == MaintainSlotState.Shown)];
    }
}
