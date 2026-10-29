using Arcanum.Data.Characters;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The new-character window's art buttons in its table's order (descriptor 0x5C4E28, button table 0x5C4C48; ledger MW27).</summary>
public enum AreaNewCharacterArtButton
{
    Accept, Cancel, PortraitPrevious, PortraitNext, RacePrevious, RaceNext, BackgroundPrevious, BackgroundNext, GenderPrevious, GenderNext,
}

/// <summary>Which text the description field shows (the hover flag 0x64C458 of window 12, MW28): none, the gender's or the race's.</summary>
public enum AreaNewCharacterHover { None = 0, Gender = 1, Race = 2 }

/// <summary>
/// ARC-UI-068: the original look of the new-character window (window 12), read from its descriptor 0x5C4E28, opening 0x5446F0, redraw
/// 0x5447B0, pointer callback 0x5456B0 and the character draw 0x5448E0 it shares with the pre-made browser (ledger MW27–MW29,
/// <see cref="AreaPremadeBrowserArtLayout"/> for the shared fields). The background 765 `CreateCharacterBase` (800 × 400) is the window at
/// (0, 41) between the strips (MW1). Ten art buttons: Accept 321 and Cancel 323 in the bottom strip, the portrait's 283 / 284 beside it
/// and the gender's, race's and background's 757 `MPCycleLeftButton` / 758 `MPCycleRightButton` beside their fields; an art-less button
/// over the name field. The pointer over the gender's or the race's band shows that one's text in the description field. Coordinates
/// here are the 800 × 600 screen's. Godot-free.
/// </summary>
public static class AreaNewCharacterArtLayout
{
    public const int BackgroundArt = 765, CycleLeftArt = 757, CycleRightArt = 758;
    /// <summary>MainMenu.mes: the gender texts 742 + gender (MW19).</summary>
    public const int GenderTextBase = 742;
    /// <summary>The pointer's y less this is the band's y (0x5456CD: 43, while the window starts at 41, MW1).</summary>
    public const int HoverOffset = 43;

    public static IReadOnlyList<int> ArtNumbers { get; } =
    [
        BackgroundArt, AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AreaPremadeBrowserArtLayout.StartArt,
        AreaPremadeBrowserArtLayout.BackArt, AreaPremadeBrowserArtLayout.PreviousArt, AreaPremadeBrowserArtLayout.NextArt, CycleLeftArt, CycleRightArt,
        AreaPremadeBrowserArtLayout.TextFontArt, AreaPremadeBrowserArtLayout.NameFontArt,
    ];

    /// <summary>The art-less button over the name field (0x5C50A0 made by 0x5461A0 at the screen's (45, 224), 230 × 20: the name's
    /// rectangle 0x5C4EE0 two larger).</summary>
    public static (int X, int Y, int Width, int Height) NameButton { get; } = (45, 224, 230, 20);

    /// <summary>The gender's and the race's bands (0x5C4F00, 0x5C4F20) as the pointer callback places them on the screen (y + 43).</summary>
    public static (int X, int Y, int Width, int Height) GenderBand { get; } = (22, 231 + HoverOffset, 276, 39);
    public static (int X, int Y, int Width, int Height) RaceBand { get; } = (22, 281 + HoverOffset, 276, 40);

    /// <summary>A button's art and its top-left on the screen (table 0x5C4C48, 48 bytes each; flag 1 for Accept and Cancel: the bottom
    /// strip's; flag 0 for the arrows: the screen's).</summary>
    public static (int Art, int X, int Y) Button(AreaNewCharacterArtButton button) => button switch
    {
        AreaNewCharacterArtButton.Accept => (AreaPremadeBrowserArtLayout.StartArt, 675, AreaPremadeBrowserArtLayout.BottomStripTop + 55),
        AreaNewCharacterArtButton.Cancel => (AreaPremadeBrowserArtLayout.BackArt, 69, AreaPremadeBrowserArtLayout.BottomStripTop + 55),
        AreaNewCharacterArtButton.PortraitPrevious => (AreaPremadeBrowserArtLayout.PreviousArt, 43, 118),
        AreaNewCharacterArtButton.PortraitNext => (AreaPremadeBrowserArtLayout.NextArt, 244, 118),
        AreaNewCharacterArtButton.RacePrevious => (CycleLeftArt, 42, 331),
        AreaNewCharacterArtButton.RaceNext => (CycleRightArt, 255, 332),
        AreaNewCharacterArtButton.BackgroundPrevious => (CycleLeftArt, 42, 381),
        AreaNewCharacterArtButton.BackgroundNext => (CycleRightArt, 255, 382),
        AreaNewCharacterArtButton.GenderPrevious => (CycleLeftArt, 42, 281),
        AreaNewCharacterArtButton.GenderNext => (CycleRightArt, 255, 282),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The button's hint message in MainMenu.mes (7003 Accept, 7002 Cancel), or null (the arrows carry none, -1).</summary>
    public static int? Hint(AreaNewCharacterArtButton button) => button switch
    {
        AreaNewCharacterArtButton.Accept => AreaPremadeBrowserArtLayout.StartHint,
        AreaNewCharacterArtButton.Cancel => AreaPremadeBrowserArtLayout.BackHint,
        _ => null,
    };

    /// <summary>The window's press of an arrow (ledger NW1), or null for Accept and Cancel.</summary>
    public static AreaNewCharacterButton? Arrow(AreaNewCharacterArtButton button) => button switch
    {
        AreaNewCharacterArtButton.PortraitPrevious => AreaNewCharacterButton.PortraitPrevious,
        AreaNewCharacterArtButton.PortraitNext => AreaNewCharacterButton.PortraitNext,
        AreaNewCharacterArtButton.RacePrevious => AreaNewCharacterButton.RacePrevious,
        AreaNewCharacterArtButton.RaceNext => AreaNewCharacterButton.RaceNext,
        AreaNewCharacterArtButton.BackgroundPrevious => AreaNewCharacterButton.BackgroundPrevious,
        AreaNewCharacterArtButton.BackgroundNext => AreaNewCharacterButton.BackgroundNext,
        AreaNewCharacterArtButton.GenderPrevious => AreaNewCharacterButton.GenderPrevious,
        AreaNewCharacterArtButton.GenderNext => AreaNewCharacterButton.GenderNext,
        _ => null,
    };

    /// <summary>PROJECT: the buttons the controller's focus row lights (<see cref="AreaMainMenu.CreationRows"/>: gender, race,
    /// background, portrait, the name field, Accept, Cancel); the original has no focus.</summary>
    public static IReadOnlyList<AreaNewCharacterArtButton> Focused(int row) => row switch
    {
        0 => [AreaNewCharacterArtButton.GenderPrevious, AreaNewCharacterArtButton.GenderNext],
        1 => [AreaNewCharacterArtButton.RacePrevious, AreaNewCharacterArtButton.RaceNext],
        2 => [AreaNewCharacterArtButton.BackgroundPrevious, AreaNewCharacterArtButton.BackgroundNext],
        3 => [AreaNewCharacterArtButton.PortraitPrevious, AreaNewCharacterArtButton.PortraitNext],
        5 => [AreaNewCharacterArtButton.Accept],
        6 => [AreaNewCharacterArtButton.Cancel],
        _ => [],
    };

    /// <summary>The pointer callback 0x5456B0: the gender's band first, then the race's; elsewhere none (the background's band
    /// 0x5C4F40 is not tested).</summary>
    public static AreaNewCharacterHover Hover(double x, double y) =>
        AreaPremadeBrowserArtLayout.Contains(GenderBand, x, y) ? AreaNewCharacterHover.Gender
        : AreaPremadeBrowserArtLayout.Contains(RaceBand, x, y) ? AreaNewCharacterHover.Race : AreaNewCharacterHover.None;

    /// <summary>
    /// The name line the redraw 0x5447B0 draws in the name's field (<see cref="AreaPremadeBrowserArtLayout.Name"/>) and its x: while the
    /// name is typed (0x544100), the text with a closing "|", centred when narrower than the field and otherwise without its first
    /// characters until it fits (0x5418A0 with flag 2, at least one character kept); else the text, " " when empty, centred as
    /// 0x542DF0 does.
    /// </summary>
    public static (int X, string Text) NameLine(AreaArtFont font, string name, bool typing)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(name);
        var field = AreaPremadeBrowserArtLayout.Name;
        var text = typing ? name + "|" : name.Length == 0 ? " " : name;
        var width = font.Measure(text).Width;
        if (!typing || width < field.Width) return (AreaPremadeBrowserArtLayout.Centred(field, width), text);
        while (text.Length > 1 && font.Measure(text).Width > field.Width) text = text[1..];
        return (field.X, text);
    }

    /// <summary>The description field's title, text and text field for <paramref name="hover"/> (the character draw 0x544D15): the
    /// gender's text without a title in the race text's field (0x5C5090) over the gender's band; else the story in the whole field when
    /// the description number is above 1000 and the pointer is over neither band; else "Race" and the race's text.</summary>
    public static AreaCharacterDescription Description(AreaNewCharacterView view, AreaNewCharacterHover hover)
    {
        ArgumentNullException.ThrowIfNull(view);
        return hover switch
        {
            AreaNewCharacterHover.Gender => new(null, view.GenderText, AreaPremadeBrowserArtLayout.RaceText),
            AreaNewCharacterHover.None when view.Character.DescriptionTitle is null => AreaCharacterDescription.Of(view.Character),
            _ => new(view.RaceTitle, view.RaceText, AreaPremadeBrowserArtLayout.RaceText),
        };
    }
}

/// <summary>ARC-UI-068: what the character draw's description field shows (MW19): a title or none, its text and the text's field.</summary>
public sealed record AreaCharacterDescription(string? Title, string Text, (int X, int Y, int Width, int Height) Field)
{
    /// <summary>A character view's own description: the story in the whole field, or "Race" and the race's text.</summary>
    public static AreaCharacterDescription Of(AreaPremadeBrowserView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(view.DescriptionTitle, view.Description,
            view.DescriptionTitle is null ? AreaPremadeBrowserArtLayout.Description : AreaPremadeBrowserArtLayout.RaceText);
    }
}

/// <summary>
/// ARC-UI-068: what the new-character window draws: <see cref="Character"/> is the shared character draw's (its name is the name
/// field's text, its description the one shown with the pointer over neither band), <see cref="Typing"/> whether the name is typed,
/// and the gender's and race's texts the bands show.
/// </summary>
public sealed record AreaNewCharacterView(AreaPremadeBrowserView Character, bool Typing, string GenderText, string RaceTitle, string RaceText);
