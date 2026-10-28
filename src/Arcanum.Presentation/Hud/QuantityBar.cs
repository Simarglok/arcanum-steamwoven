namespace Arcanum.Presentation.Hud;

/// <summary>What a press on the quantity window changes (ledger QW3, QW4): the All button, the up and down arrows, a digit of the
/// numeric keypad, and Backspace or Delete, which erase the last digit.</summary>
public enum QuantityAction { All, Up, Down, Digit, Erase }

/// <summary>
/// ARC-UI-029: the original's quantity window (interface mode 13, shown as bottom-bar mode 9; checked in the 1.0.7.4 executable,
/// ledger QW1–QW6). A move of a gold or ammunition stack to another holder asks how many units go, unless the stack holds one
/// unit, the move is a loot window's put into the looted holder, or it is the loot window's Take all (<see cref="Ask"/>). The
/// window starts at 0 with the stack's units as the maximum; All sets the maximum, the arrows step by one within 0..maximum, a
/// keypad digit appends (clamped to the maximum), Backspace or Delete drop the last digit. Accept moves the count shown (0 moves
/// nothing); Cancel moves nothing. Engine-free; the host draws the window and makes the move.
/// </summary>
public readonly record struct QuantityBar(int Count, int Maximum)
{
    /// <summary>The digits the original's counter draws (0x553960).</summary>
    public const int Digits = 6;
    /// <summary>The item types of the original's stacks (field 0x14F): ammunition and gold.</summary>
    public const int AmmoType = 6, GoldType = 8;

    /// <summary>
    /// Whether a move of an item of <paramref name="itemType"/> with <paramref name="units"/> to another holder asks for the count
    /// (the move 0x5788C0, QW1): null when it asks, otherwise the count it moves at once: 1 for an item that is not gold or
    /// ammunition and for a stack of one unit, and the whole stack for a put into the looted holder of a loot window
    /// (<paramref name="intoLootedHolder"/>) or a Take all (<paramref name="takeAll"/>). Every other stack asks: a drop, a take
    /// from a looted holder, a barter in either direction.
    /// </summary>
    public static int? Ask(int itemType, int units, bool intoLootedHolder, bool takeAll)
    {
        if (itemType is not (AmmoType or GoldType) || units == 1) return 1;
        return intoLootedHolder || takeAll ? units : null;
    }

    /// <summary>The window as it opens over a stack of <paramref name="maximum"/> units (bar mode 9's open, 0x551210): count 0.</summary>
    [OriginalFunction(0x551210, OriginalFunctionScope.Partial)]
    public static QuantityBar Open(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return new(0, maximum);
    }

    /// <summary>
    /// One press (the in-game handler 0x54B5D0, QW3, QW4): All sets the maximum; Up adds one below the maximum, Down takes one above
    /// 0; a <paramref name="digit"/> makes count × 10 + digit, clamped to the maximum; Erase divides by 10. The original's
    /// multiplication is 32-bit and wraps above 214 748 364 units (no installed stack comes near); here it is clamped.
    /// </summary>
    [OriginalFunction(0x54B5D0, OriginalFunctionScope.Partial)]
    public QuantityBar Apply(QuantityAction action, int digit = 0)
    {
        if (action == QuantityAction.Digit) ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)digit, 9u, nameof(digit));
        return action switch
        {
            QuantityAction.All => this with { Count = Maximum },
            QuantityAction.Up => Count < Maximum ? this with { Count = Count + 1 } : this,
            QuantityAction.Down => Count > 0 ? this with { Count = Count - 1 } : this,
            QuantityAction.Digit => this with { Count = (int)Math.Min((long)Count * 10 + digit, Maximum) },
            QuantityAction.Erase => this with { Count = Count / 10 },
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }
}
