namespace Arcanum.Presentation.Hud;

/// <summary>The edit keys the original's text entry (0x566F00) reads besides characters: Home, the arrows, Insert and Delete. End is not one of them.</summary>
public enum StripEntryKey { Home, Left, Right, Up, Down, Insert, Delete }

/// <summary>
/// ARC-UI-2305, ARC-UI-2306: the text edit of the bottom strip's text entries (the original's entry 0x566F00 on a record: text buffer, maximum length, no character filter):
/// a character from 0x20 up is inserted at the caret (or overwrites with Insert on), refused when the caret is at the typing limit (the maximum less one) or beyond and,
/// inserting, when the text is longer than the limit; Backspace removes the character before the caret, Delete the one at it; Left and Right move the caret by one,
/// Up and Down by 40, only while it stays inside the text; Home goes to 0; Tab and other controls do nothing; there is no selection. Engine-free; the strip mode
/// that owns the entry (the order line, the map note entry) draws it.
/// </summary>
public sealed class StripTextEntry
{
    /// <summary>The caret movement of Up and Down (0x566F00).</summary>
    public const int VerticalStep = 40;

    private string _text = "";

    /// <summary>An entry of up to <paramref name="maxLength"/> characters (the record's maximum).</summary>
    public StripTextEntry(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        MaxLength = maxLength;
    }

    /// <summary>The record's maximum text length; typing at the end stops at <see cref="TypingLimit"/> characters.</summary>
    public int MaxLength { get; }

    /// <summary>The most characters the entry takes by typing (the maximum less one).</summary>
    public int TypingLimit => MaxLength - 1;

    /// <summary>The text.</summary>
    public string Text => _text;

    /// <summary>The caret's position, 0 to the text's length.</summary>
    public int Caret { get; private set; }

    /// <summary>Insert off, characters overwrite (the Insert key toggles it).</summary>
    public bool Overwrite { get; private set; }

    /// <summary>A counter that grows with each change of the state the strip shows, so the host redraws only then.</summary>
    public int Revision { get; private set; }

    /// <summary>Binds the entry to <paramref name="text"/> (0x566E50 and the opener): the caret at the end, insert on. The text is kept as it is, even over the maximum
    /// (a note already stored may be longer; typing then adds nothing).</summary>
    public void Bind(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text; Caret = _text.Length; Overwrite = false; Revision++;
    }

    /// <summary>Puts <paramref name="text"/> in place of the text (a Cancel button's restore, 0x5672D0); the caret stays where it was but not past the new end (the original
    /// leaves it past the end, which a later edit would read out of the text; the viewer keeps it inside).</summary>
    public void Replace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == _text) return;
        _text = text; Caret = Math.Min(Caret, _text.Length); Revision++;
    }

    /// <summary>Empties the text, keeping the entry bound (a Clear button): the caret goes to 0.</summary>
    public void Clear()
    {
        if (_text.Length == 0 && Caret == 0) return;
        _text = ""; Caret = 0; Revision++;
    }

    /// <summary>Unbinds the entry: the text is dropped.</summary>
    public void Reset()
    {
        _text = ""; Caret = 0; Overwrite = false; Revision++;
    }

    /// <summary>A typed character (the entry's character event): Backspace removes the one before the caret; characters below 0x20 do nothing; others are
    /// inserted or overwritten under the length rule. Characters above 0xFF are not in the original's one-byte text and are refused.
    /// Returns whether the text changed.</summary>
    public bool Type(char c)
    {
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
    public bool Press(StripEntryKey key)
    {
        switch (key)
        {
            case StripEntryKey.Home: return MoveTo(0);
            case StripEntryKey.Left: return Caret > 0 && MoveTo(Caret - 1);
            case StripEntryKey.Right: return Caret < _text.Length && MoveTo(Caret + 1);
            case StripEntryKey.Up: return Caret - VerticalStep >= 0 && MoveTo(Caret - VerticalStep);
            case StripEntryKey.Down: return Caret + VerticalStep <= _text.Length && MoveTo(Caret + VerticalStep);
            case StripEntryKey.Insert: Overwrite = !Overwrite; Revision++; return true;
            case StripEntryKey.Delete:
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
