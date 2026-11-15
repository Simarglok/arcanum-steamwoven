namespace Arcanum.Presentation.Hud;

/// <summary>The edit keys the original's text entry (0x566F00) reads besides characters: Home, the arrows, Insert and Delete. End is not one of them.</summary>
public enum OrderLineKey { Home, Left, Right, Up, Down, Insert, Delete }

/// <summary>
/// ARC-UI-2305: the original's typed order line, bar mode 7 of the bottom strip (ledger BM0, BM5; art 291 <c>Broadcast_Rot</c>): a text
/// entry of up to 125 characters (record 0x5CAC28, no character filter) drawn into a rectangle of the strip, opened by Enter (0x5718A0),
/// accepted by Enter (0x571950) and closed by any bar mode change (0x571910). The text edit follows 0x566F00 (read for this task):
/// a character from 0x20 up is inserted at the caret (or overwrites with Insert on), refused when the caret is at 124 or beyond and,
/// inserting, when the text is longer than 124; Backspace removes the character before the caret, Delete the one at it; Left and Right
/// move the caret by one, Up and Down by 40, only while it stays inside the text; Home goes to 0; Tab and other controls do nothing; there is
/// no selection. The caret mark is drawn after the end of the text whatever the caret position (0x5521B0), and an empty text is drawn as one space.
/// Engine-free; the host draws it and sends the accepted text through the follower orders.
/// </summary>
public sealed class OrderLine
{
    /// <summary>The bar mode the line is (table 0x5C6548 entry 7).</summary>
    public const int BarMode = 7;
    /// <summary>The mode's background (art 291 <c>Broadcast_Rot</c>), drawn at the place every strip pane uses (196, 492).</summary>
    public const int BackgroundArt = 291, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The art the line needs read with the bar's arts.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt];
    /// <summary>The text rectangle (table 0x5C6D60 row 7): left, top, width, height on the 800 × 600 screen.</summary>
    public const int TextX = 220, TextY = 503, TextWidth = 350, TextHeight = 82;
    /// <summary>The record's maximum text length; typing at the end stops at <see cref="TypingLimit"/> characters (0x566F00).</summary>
    public const int MaxLength = 125, TypingLimit = 124;
    /// <summary>The caret movement of Up and Down (0x566F00).</summary>
    public const int VerticalStep = 40;
    /// <summary>The caret mark (string 0x5C286C) and its offset from the end of the text (0x5521B0: x + 3 + text width).</summary>
    public const string CaretMark = "*";
    public const int CaretOffset = 3;
    /// <summary>The text colour (0x5717F0: red 175, green 120, blue 120; the values F1, the look F0) of the font the original calls resource 10.</summary>
    public static readonly (byte R, byte G, byte B) TextColour = (175, 120, 120);

    private string _text = "";

    /// <summary>Whether the line is open (the entry is bound, 0x566EF0).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The typed text.</summary>
    public string Text => _text;

    /// <summary>The caret's position, 0 to the text's length.</summary>
    public int Caret { get; private set; }

    /// <summary>Insert off, characters overwrite (the Insert key toggles it).</summary>
    public bool Overwrite { get; private set; }

    /// <summary>What the strip draws for the text: the text, or one space for an empty one (the change callback 0x571990).</summary>
    public string Shown => _text.Length == 0 ? " " : _text;

    /// <summary>A counter that grows with each change of the state the strip shows, so the host redraws only then.</summary>
    public int Revision { get; private set; }

    /// <summary>Opens the line (0x5718A0, the not-open branch; the closing toggle belongs to other callers than Enter): empty text, the caret at 0, insert on.</summary>
    [OriginalFunction(0x5718A0, OriginalFunctionScope.Partial)]
    public void Open()
    {
        _text = ""; Caret = 0; Overwrite = false; IsOpen = true; Revision++;
    }

    /// <summary>Closes the line and drops the text (0x571910; it runs on every bar mode change). Returns whether it was open.</summary>
    [OriginalFunction(0x571910, OriginalFunctionScope.Partial)]
    public bool Close()
    {
        var was = IsOpen;
        _text = ""; Caret = 0; IsOpen = false;
        if (was) Revision++;
        return was;
    }

    /// <summary>
    /// Enter in the open line (0x571950): a non-empty text is handed back for the order, then the line closes whatever it held (an empty text
    /// sends nothing). Null when the line is not open or empty.
    /// </summary>
    [OriginalFunction(0x571950, OriginalFunctionScope.Partial)]
    public string? Accept()
    {
        if (!IsOpen) return null;
        var text = _text.Length == 0 ? null : _text;
        Close();
        return text;
    }

    /// <summary>A typed character (the entry's character event): Backspace removes the one before the caret; characters below 0x20 do nothing; others are
    /// inserted or overwritten under the length rule. Characters above 0xFF are not in the original's one-byte text and are refused.
    /// Returns whether the text changed.</summary>
    public bool Type(char c)
    {
        if (!IsOpen) return false;
        if (c == '\b')
        {
            if (Caret == 0) return false;
            _text = _text.Remove(Caret - 1, 1); Caret--; Revision++;
            return true;
        }
        if (c < ' ' || c > 0xFF) return false;
        if (Caret >= TypingLimit) return false;
        if (!Overwrite)
        {
            if (_text.Length > TypingLimit) return false;
            _text = _text.Insert(Caret, c.ToString());
        }
        else _text = Caret < _text.Length ? _text.Remove(Caret, 1).Insert(Caret, c.ToString()) : _text + c;
        Caret++; Revision++;
        return true;
    }

    /// <summary>An edit key (the entry's key event). Returns whether anything changed.</summary>
    public bool Press(OrderLineKey key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case OrderLineKey.Home: return MoveTo(0);
            case OrderLineKey.Left: return Caret > 0 && MoveTo(Caret - 1);
            case OrderLineKey.Right: return Caret < _text.Length && MoveTo(Caret + 1);
            case OrderLineKey.Up: return Caret - VerticalStep >= 0 && MoveTo(Caret - VerticalStep);
            case OrderLineKey.Down: return Caret + VerticalStep <= _text.Length && MoveTo(Caret + VerticalStep);
            case OrderLineKey.Insert: Overwrite = !Overwrite; Revision++; return true;
            case OrderLineKey.Delete:
                if (Caret >= _text.Length) return false;
                _text = _text.Remove(Caret, 1); Revision++;
                return true;
            default: return false;
        }
    }

    private bool MoveTo(int caret)
    {
        if (caret == Caret) return false;
        Caret = caret; Revision++;
        return true;
    }
}
