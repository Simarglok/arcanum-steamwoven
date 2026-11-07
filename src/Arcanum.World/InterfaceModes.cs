namespace Arcanum.World;

/// <summary>
/// ARC-DEV-108 (MIG16, TM13, GR35): the original's 21 interface modes (<c>original-scripts-authority.md</c>, mode table of SA39). The
/// numbers are the original's: they are the values pushed on its mode stack (0x551A80). Mode 12 has no code and no pusher.
/// </summary>
public enum InterfaceMode
{
    Game = 0, SpellTarget = 1, SkillTarget = 2, Dialog = 3, Barter = 4, WorldMap = 5, Sleep = 6, Logbook = 7, Inventory = 8,
    Character = 9, Loot = 10, Steal = 11, Unused = 12, QuantityPicker = 13, Schematic = 14, Book = 15, ItemTarget = 16,
    Matchmaker = 17, FollowerOrder = 18, InventoryKind5 = 19, InventoryKind6 = 20,
}

/// <summary>ARC-DEV-108: which cursor family a mode shows (SA41): the game cursor (the viewer picks battle or gauntlet by the hero's
/// combat mode and a called-shot key, <c>AreaCursorArt</c>), the spell cursor, the skill cursor, or the gauntlet.</summary>
public enum InterfaceCursor { Game, Spell, Skill, Gauntlet }

/// <summary>
/// ARC-DEV-108 (MIG16, TM13): the fixed tables of the interface mode machine, read from 1.0.7.4 (SA37–SA45, SA52). Pure functions of
/// the mode; the stack that applies them is <see cref="InterfaceModeStack"/>. Godot-free.
/// </summary>
public static class InterfaceModes
{
    /// <summary>The modes the original defines (0..20).</summary>
    public const int Count = 21;

    /// <summary>True for the 21 defined values.</summary>
    public static bool IsDefined(InterfaceMode mode) => (int)mode is >= 0 and < Count;

    /// <summary>The modes a window opener replaces the top with (SA40): dialog 3, barter 4, world map 5, sleep 6, logbook 7, inventory
    /// 8, character 9, loot 10, steal 11, schematic 14, book 15 and the inventory kinds 19 and 20.</summary>
    public static bool IsWindow(InterfaceMode mode) => mode is InterfaceMode.Dialog or InterfaceMode.Barter or InterfaceMode.WorldMap
        or InterfaceMode.Sleep or InterfaceMode.Logbook or InterfaceMode.Inventory or InterfaceMode.Character or InterfaceMode.Loot
        or InterfaceMode.Steal or InterfaceMode.Schematic or InterfaceMode.Book or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6;

    /// <summary>The target modes, which sit over the inventory when their target mask has its bit (SA40): spell 1, skill 2, item use 16.</summary>
    public static bool IsTarget(InterfaceMode mode) => mode is InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.ItemTarget;

    /// <summary>The modes always pushed over the top without a pop (SA40): quantity picker 13, matchmaker 17, follower order 18.</summary>
    public static bool IsOverlay(InterfaceMode mode) => mode is InterfaceMode.QuantityPicker or InterfaceMode.Matchmaker or InterfaceMode.FollowerOrder;

    /// <summary>The cursor family of a mode (SA41): spell in modes 1 and 16, skill in 2, the game cursor in 0 and 18, the gauntlet in
    /// every window mode (dialog included).</summary>
    public static InterfaceCursor Cursor(InterfaceMode mode) => mode switch
    {
        InterfaceMode.SpellTarget or InterfaceMode.ItemTarget => InterfaceCursor.Spell,
        InterfaceMode.SkillTarget => InterfaceCursor.Skill,
        InterfaceMode.Game or InterfaceMode.FollowerOrder => InterfaceCursor.Game,
        _ => InterfaceCursor.Gauntlet,
    };

    /// <summary>True when the game view acts on the mouse in this mode (SA44): 0, 1, 2, 3, 16 and 18; in every other mode the window
    /// owns the mouse. <paramref name="anotherHasTheTurn"/> (a round runs and the participant whose turn it is is not the local hero)
    /// stops the view in every mode.</summary>
    public static bool GameViewActs(InterfaceMode mode, bool anotherHasTheTurn = false) => !anotherHasTheTurn
        && mode is InterfaceMode.Game or InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.Dialog
            or InterfaceMode.ItemTarget or InterfaceMode.FollowerOrder;

    /// <summary>True when arrow and edge scrolling run in this mode (SA44): 0, 1, 2, 3, 5, 16 and 18. One table (0x5C72B4, read through
    /// 0x551F70) gates both inputs; nothing in the scroll path asks whose turn it is (ARC-DEV-200 reading: scrolling goes on while another
    /// participant has the turn; only the game view's click guard checks that).</summary>
    public static bool ScrollsView(InterfaceMode mode) => mode is InterfaceMode.Game or InterfaceMode.SpellTarget or InterfaceMode.SkillTarget
        or InterfaceMode.Dialog or InterfaceMode.WorldMap or InterfaceMode.ItemTarget or InterfaceMode.FollowerOrder;

    /// <summary>True when the scroll moves the isometric game view (ARC-DEV-200): the modes of <see cref="ScrollsView"/> but the world map.
    /// Entering the world map (0x551DC0) sets a scroll hook (0x563770) that moves the map's own offsets, so the view under it stays; the
    /// dialog (mode 3) sets no hook, so the view scrolls under it.</summary>
    public static bool ScrollsGameView(InterfaceMode mode) => ScrollsView(mode) && mode != InterfaceMode.WorldMap;

    /// <summary>True when F1–F6 give the follower orders at the mouse tile and Home is handled (SA44): modes 0, 1, 2 and 18. The release
    /// of the number row picks a hotkey slot in every mode. Nothing runs while another participant has the turn.</summary>
    public static bool FollowerOrderKeys(InterfaceMode mode, bool anotherHasTheTurn = false) => !anotherHasTheTurn
        && mode is InterfaceMode.Game or InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.FollowerOrder;

    /// <summary>True for the window modes that bring the interface bar back when F10 had hidden it, and hide it again on leave
    /// (the note under the mode table): 4, 5, 7–11, 14, 15, 19 and 20. Not modelled: modes 19 and 20 re-hide it even when going to 13, which the
    /// others do not (the next mode is not an argument here; the host reads the step).</summary>
    public static bool RestoresHiddenBar(InterfaceMode mode) => mode is InterfaceMode.Barter or InterfaceMode.WorldMap or InterfaceMode.Logbook
        or InterfaceMode.Inventory or InterfaceMode.Character or InterfaceMode.Loot or InterfaceMode.Steal or InterfaceMode.Schematic
        or InterfaceMode.Book or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6;

    /// <summary>
    /// True when leaving <paramref name="from"/> for <paramref name="next"/> closes its window (the leave column of the mode table): the
    /// world map, sleep, logbook, character, schematic and book windows always; the inventory (8) unless going to 1, 2, 13 or 16;
    /// loot (10) unless going to 1, 2 or 13; barter, steal and the inventory kinds (4, 11, 19, 20) unless going to 13. The rows say only what
    /// keeps the window; that every other destination closes it is an inference for loot (10), barter and steal.
    /// </summary>
    public static bool LeaveClosesWindow(InterfaceMode from, InterfaceMode next) => from switch
    {
        InterfaceMode.WorldMap or InterfaceMode.Sleep or InterfaceMode.Logbook or InterfaceMode.Character or InterfaceMode.Schematic or InterfaceMode.Book => true,
        InterfaceMode.Inventory => next is not (InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.QuantityPicker or InterfaceMode.ItemTarget),
        InterfaceMode.Loot => next is not (InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.QuantityPicker),
        InterfaceMode.Barter or InterfaceMode.Steal or InterfaceMode.InventoryKind5 or InterfaceMode.InventoryKind6 => next != InterfaceMode.QuantityPicker,
        _ => false,
    };

    /// <summary>
    /// True when the leave case puts a held item back (0x575770). Read in 1.0.7.4 (ARC-DEV-110; the first reading named only mode 16): every
    /// leave case sets the flag that asks for it except the quantity picker (13), the matchmaker (17) and the unused 12; the item target
    /// (16) calls the put-back itself; the own inventory (8) asks for it only while its window stays, going to 1, 2, 13 or 16, and not
    /// when it closes (so an item lifted off the inventory onto the game cursor stays held when the window closes).
    /// </summary>
    public static bool LeavePutsHeldItemBack(InterfaceMode from, InterfaceMode next) => from switch
    {
        InterfaceMode.Unused or InterfaceMode.QuantityPicker or InterfaceMode.Matchmaker => false,
        InterfaceMode.Inventory => next is InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.QuantityPicker or InterfaceMode.ItemTarget,
        _ => true,
    };

    /// <summary>True for the modes whose leave case makes the function run once more as a pop (SA38): the spell and skill target modes.
    /// The mode below them, the inventory when they were opened over it, goes too.</summary>
    public static bool LeavePopsAgain(InterfaceMode from) => from is InterfaceMode.SpellTarget or InterfaceMode.SkillTarget;

    /// <summary>
    /// True when the opener of <paramref name="mode"/> refuses for this hero (SA52). A dead hero is refused by the logbook, character,
    /// book, schematic, world-map, sleep, dialog and inventory openers and by the starts of the spell, skill and item-use target modes;
    /// a dead or unconscious one by the spell, skill, item-use, world-map, sleep and dialog openers. The follower order, matchmaker and
    /// quantity move test nothing of the kind, nor do the other windows' openers. Inferences: only mode 8 stands for the inventory opener (0x572510 opens
    /// kinds 0..6, modes 8, 4, 10, 11, 19, 20; the row names "inventory"); the 4-action-point check at the start of a target mode (SA52) is not modelled.
    /// </summary>
    public static bool OpenerRefuses(InterfaceMode mode, bool heroDead, bool heroUnconscious)
    {
        if (heroDead && mode is InterfaceMode.Logbook or InterfaceMode.Character or InterfaceMode.Book or InterfaceMode.Schematic
            or InterfaceMode.WorldMap or InterfaceMode.Sleep or InterfaceMode.Dialog or InterfaceMode.Inventory
            or InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.ItemTarget) return true;
        return heroUnconscious && mode is InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.ItemTarget
            or InterfaceMode.WorldMap or InterfaceMode.Sleep or InterfaceMode.Dialog;
    }
}
