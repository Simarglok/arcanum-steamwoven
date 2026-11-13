using Arcanum.Data.Hd;
using Arcanum.Presentation.Hud;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-140: a surface the generated interface chrome is painted with (our own colours and noise, chosen by eye; not
/// measured from any game art).</summary>
/// <remarks>ARC-UI-1195: <see cref="Slate"/> is the character sheet's pages. ARC-UI-1196: <see cref="Cloth"/> is a book cover's maroon weave, <see cref="Parchment"/> a page (cream with soft mottling and the shade of the fold at the logbook's spine,
/// so everything that sits on a page matches it) and <see cref="PageEdge"/> the striped edge of a stack of pages. ARC-UI-1199: <see cref="Paper"/> is the parchment of a loose sheet (the schematic window's blueprint
/// page): the same cream and mottling with no fold.</remarks>
public enum ChromeMaterial { Wood, WoodDark, Iron, Dark, Face, Pit, Slate, Cloth, Parchment, PageEdge, Paper }

/// <summary>The edges of a frame that get a rim (a panel's edge facing the play view).</summary>
[Flags]
public enum ChromeSides { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8, All = Top | Bottom | Left | Right }

/// <summary>The one rounded corner a plate may have.</summary>
public enum ChromeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>One part of a recipe, in Legacy pixels of the frame.</summary>
public abstract record ChromePart;

/// <summary>A raised plate of a material with a one-unit bevel, optionally with one rounded corner.</summary>
/// <remarks>ARC-UI-1196: <paramref name="Bevel"/> false paints the plain material with no lit and dark edge (a page or a stack of pages, which meet their neighbours).</remarks>
public sealed record ChromePlate(int X, int Y, int Width, int Height, ChromeMaterial Material, int CornerRadius = 0, ChromeCorner Corner = ChromeCorner.None, bool Bevel = true) : ChromePart;

/// <summary>A sunk panel: a one-unit sunk brass bevel, a one-unit ink line, then the fill.</summary>
public sealed record ChromeSunk(int X, int Y, int Width, int Height, ChromeMaterial Fill) : ChromePart;

/// <summary>A brass rivet (a vector disc with radial light).</summary>
public sealed record ChromeRivet(double X, double Y, double Radius) : ChromePart;

/// <summary>A picture region: its pixels come from the art (the HD redraw when the pack has one, else the Legacy frame).</summary>
/// <remarks>ARC-UI-1194: <paramref name="Round"/> copies only the circle (ellipse) inscribed in the rectangle, anti-aliased, so a round picture (the barter window's
/// medallion) does not bring the square of the old wood around it.</remarks>
public sealed record ChromeKeep(int X, int Y, int Width, int Height, bool Round = false) : ChromePart;

/// <summary>ARC-UI-148: the lines between the cells of a grid (an inventory grid): one-unit lines on the last pixel of every cell but the last
/// of each row and column, in <paramref name="Columns"/> × <paramref name="Rows"/> cells of <paramref name="Cell"/> Legacy pixels from (X, Y).</summary>
/// <remarks>ARC-UI-1194: <paramref name="Closed"/> also draws the line on the last pixel of the last column and row (a grid whose pit goes on past the last cell:
/// the loot and barter windows' scroll bar column, the bottom edge).</remarks>
public sealed record ChromeGrid(int X, int Y, int Columns, int Rows, int Cell, bool Closed = false) : ChromePart;

/// <summary>ARC-UI-1194: the round view of a window (the live game view's circle, <see cref="AreaInventoryHole"/>): a brass ring of the round hole's own style
/// (<see cref="InterfaceChromeRenderer"/>'s socket ring) around the circle at (<paramref name="CenterX"/>, <paramref name="CenterY"/>) of <paramref name="Radius"/>,
/// painted on the opaque pixels of the frame, and the square rim a square hole would get is not drawn. The lens arts (231-233) fill the circle's box with the same ring.
/// <paramref name="Shape"/> (the default) also gives the circle's edge (the pixels within a unit and a half inside it) an anti-aliased alpha instead of the Legacy stairs;
/// deeper inside the Legacy alpha is untouched. A frame whose box behind the live view is opaque (the inventory's paper doll) passes false and keeps its pixels.</summary>
public sealed record ChromeRoundView(double CenterX, double CenterY, double Radius, bool Shape = true) : ChromePart;

/// <summary>ARC-UI-1196: a picture's printed or drawn marks (a glyph, an arrow) on the generated ground under it. In the rectangle the Legacy pixels are compared with the
/// rectangle's median colour; where they are darker or lighter than the median by more than a tenth the difference is multiplied onto the generated colour, so the marks
/// keep their own shape and anti-aliasing and the Legacy ground's grain does not carry over.</summary>
public sealed record ChromeGlyph(int X, int Y, int Width, int Height) : ChromePart;

/// <summary>ARC-UI-1199: the ruled lines of a printed sheet (the schematic window's blueprint page) inside a rectangle: a one-unit line in the rectangle's coordinates at
/// <paramref name="GridX"/> + k × <paramref name="Step"/> and another set of horizontal ones at <paramref name="GridY"/> + k × <paramref name="Step"/>, each <c>max(1, round(scale))</c> device
/// pixels wide (the device-pixel grid rule), faintly orange. A separately painted art on the sheet (a page button) passes the lines' phase in its own coordinates, so they continue.</summary>
public sealed record ChromeRuling(int X, int Y, int Width, int Height, int Step, int GridX, int GridY) : ChromePart;

/// <summary>ARC-UI-1199: a hole of the Legacy frame that takes no rim (the generated rim every hole gets): what is drawn over it (a tab, a lens) carries its own edge. The hole whose box holds
/// the centre of this rectangle is the one.</summary>
public sealed record ChromeBare(int X, int Y, int Width, int Height) : ChromePart;

/// <summary>ARC-UI-1817: the coloured marks of a picture on the generated ground under it (the follower panel's arrow buttons: a red or green triangle on an iron plate). In the
/// rectangle every Legacy pixel whose colour is far from grey (the spread of its three channels is at least 24) keeps its own colour, so the marks keep their shape and their dither, and the grey
/// grain of the Legacy plate does not carry over. A pixel is kept whole (no one-unit halo): a picture with anti-aliased edges would need one.</summary>
public sealed record ChromeMarks(int X, int Y, int Width, int Height) : ChromePart;

/// <summary>ARC-UI-1196: what is drawn around the holes of a panel's Legacy frame (the transparent regions the live view or the page shows through).</summary>
public enum ChromeHoles
{
    /// <summary>The brass rim of the bars and the inventory (the default).</summary>
    Brass,
    /// <summary>Nothing: the hole is the live game view's (the logbook's lens) and its neighbourhood is plain.</summary>
    None,
    /// <summary>A cover's edge: a darker and a lighter line of the cloth itself along the hole.</summary>
    Cloth,
}

public enum ChromeKind
{
    /// <summary>A panel or strip: base material, parts, rims around its holes, rims on <see cref="ChromeRecipe.Rim"/>.</summary>
    Panel,
    /// <summary>A button: generated face and rim following its silhouette (raised, sunk on the down frame), the glyph kept from the art.</summary>
    Button,
    /// <summary>ARC-UI-1196: a logbook tab: the ground it sits on (the recipe's parts) and the paper bookmark the Legacy frame draws (its outline read from the frame), with the Legacy glyph on it.</summary>
    Tab,
}

/// <summary>How to generate the chrome of every frame of one interface art file. The Legacy frame still decides the size, the
/// silhouette (alpha), the holes and hit tests; only the drawn pixels change. A button's <paramref name="DownFrame"/> draws pressed
/// and its <paramref name="HoverFrame"/> lit (the bar's arts: idle 0, down 1, hover 2).</summary>
/// <remarks>ARC-UI-1196: <paramref name="Holes"/> says what the holes of the Legacy frame get; <paramref name="OriginX"/> and <paramref name="OriginY"/> are where the art's corner lies in the
/// window's larger picture (the logbook's book), in Legacy pixels: the materials are sampled at that offset, so a tab or a page button painted on its own continues the page or the
/// stack it lies on without a seam. ARC-UI-1784: <paramref name="KeepFace"/> (a button only) keeps the art's whole picture inside the generated rim (a ball whose colour tells the college
/// apart), where a plain button keeps only the glyph on a generated brass face. ARC-UI-1199: <paramref name="TabPaper"/> is the material of a tab's paper bookmark.</remarks>
public sealed record ChromeRecipe(string File, ChromeKind Kind, ChromeMaterial Base, ChromeSides Rim, IReadOnlyList<ChromePart> Parts, int DownFrame = 1, int HoverFrame = 2,
    ChromeHoles Holes = ChromeHoles.Brass, int OriginX = 0, int OriginY = 0, bool KeepFace = false, ChromeMaterial TabPaper = ChromeMaterial.Parchment)
{
    public string NameSha256 { get; } = HdInterfaceKey.NameSha256(File);
}

/// <summary>ARC-UI-140: the recipes of the generated interface (slice S1: both HUD strips and the bar buttons). Coordinates are Legacy
/// pixels of each frame (layout facts of the original windows, as in the ledgers); everything drawn is ours.</summary>
public static class InterfaceChromeRecipes
{
    /// <summary>ARC-UI-1784: the file names of the sixteen college tab arts of the sheet's spells page (<c>interface.mes</c> numbers 11, 44, 38, 92, 50, 86, 56, 98, 62, 68, 74, 104, 110, 80, 116, 122,
    /// the order of <see cref="AreaCharacterSheetSpellsLayout.CollegeArt"/>).</summary>
    public static readonly IReadOnlyList<string> CollegeTabFiles =
    [
        "s_conveyance.art", "s_divination.art", "s_air.art", "s_earth.art", "s_fire.art", "s_water.art", "s_forc.art", "s_mental.art",
        "s_meta.art", "s_morph.art", "s_nature.art", "s_evilnecro.art", "s_goodnecro.art", "s_phantasm.art", "s_summoning.art", "s_temporal.art",
    ];

    /// <summary>ARC-UI-1196: the logbook's frames (the layout of <see cref="AreaLogbookArtLayout"/>; coordinates in Legacy pixels of each art, the book's own for the book).
    /// The side panel is plain wood (its square hole is the live view of the lens, whose ring the lens art 198 draws). The book is the table, a cover, the stacks of pages and the
    /// two pages with the fold between them; the page art 261 is the cover's cloth frame, its hole shows the pages. A tab is its ground, a paper tab and the glyph; a page button is the page
    /// under it and the arrow.</summary>
    private static IEnumerable<ChromeRecipe> LogbookRecipes()
    {
        yield return new ChromeRecipe("logbooks_side.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, [], Holes: ChromeHoles.None);
        yield return new ChromeRecipe("book_main.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None,
        [
            new ChromePlate(7, 23, 561, 355, ChromeMaterial.Cloth),
            new ChromePlate(21, 35, 23, 331, ChromeMaterial.PageEdge, Bevel: false),
            new ChromePlate(530, 35, 28, 331, ChromeMaterial.PageEdge, Bevel: false),
            new ChromePlate(44, 35, 242, 331, ChromeMaterial.Parchment, Bevel: false),
            new ChromePlate(286, 35, 244, 331, ChromeMaterial.Parchment, Bevel: false),
            new ChromePlate(262, 21, 62, 9, ChromeMaterial.Iron), new ChromePlate(262, 371, 62, 9, ChromeMaterial.Iron),
        ]);
        yield return new ChromeRecipe("book_quests.art", ChromeKind.Panel, ChromeMaterial.Cloth, ChromeSides.None, [new ChromePlate(0, 0, 561, 355, ChromeMaterial.Cloth)], Holes: ChromeHoles.Cloth);
        var tabs = new[] { "tab_note.art", "tab_quest.art", "tab_rep.art", "tab_blesscurse.art", "tab_egoinjure.art", "tab_backgrnd.art", "tab_keys.art" };
        for (var i = 0; i < tabs.Length; i++)
            yield return new ChromeRecipe(tabs[i], ChromeKind.Tab, ChromeMaterial.Wood, ChromeSides.None,
                [new ChromePlate(0, 0, 27, 41, ChromeMaterial.PageEdge, Bevel: false), new ChromePlate(27, 0, 11, 41, ChromeMaterial.WoodDark, Bevel: false)],
                OriginX: AreaLogbookArtLayout.Tabs[i].X - AreaLogbookArtLayout.BookCorner.X, OriginY: AreaLogbookArtLayout.Tabs[i].Y - AreaLogbookArtLayout.BookCorner.Y);
        yield return new ChromeRecipe("pageturn_l.art", ChromeKind.Panel, ChromeMaterial.Parchment, ChromeSides.None, [new ChromeGlyph(0, 0, 18, 22)], Holes: ChromeHoles.None,
            OriginX: AreaLogbookArtLayout.PreviousCorner.X - AreaLogbookArtLayout.BookCorner.X, OriginY: AreaLogbookArtLayout.PreviousCorner.Y - AreaLogbookArtLayout.BookCorner.Y);
        yield return new ChromeRecipe("pageturn_r.art", ChromeKind.Panel, ChromeMaterial.Parchment, ChromeSides.None, [new ChromeGlyph(0, 0, 18, 22)], Holes: ChromeHoles.None,
            OriginX: AreaLogbookArtLayout.NextCorner.X - AreaLogbookArtLayout.BookCorner.X, OriginY: AreaLogbookArtLayout.NextCorner.Y - AreaLogbookArtLayout.BookCorner.Y);
    }

    /// <summary>ARC-UI-1200: the frames of the message window and its two other panes and of the Fate and Sleep windows (Legacy pixels of each art; the layouts are those of
    /// <see cref="AreaMessageLayout"/>, <c>SpellPaneRules</c>, <c>SkillPaneRules</c>, <see cref="AreaFateLayout"/> and <see cref="AreaSleepLayout"/>). All of them are <see cref="ChromeMaterial.Slate"/> with a
    /// rim on every side, like the character sheet's pages: the bottom strip's mode panes (<c>DialogueWindow</c> 354, the message text area; <c>spell_ro</c> 8; <c>Skill_Rot</c> 274; 410 × 107 at (196, 492))
    /// are a sunk text plate (with a rivet at each end where the original has a brass plaque, the spell and skill panes'), the spell pane's five wells and the skill pane's four are sunk pits under the icon
    /// buttons, and the spell pane's sixteen domes under the college buttons stay pictures (a college the hero has no rank in shows its dome). The Fate (<c>Fate_Base</c> 292) and Sleep (<c>Sleep_Base</c> 565)
    /// windows have a sunk bar behind every second row of their lines and buttons; the twelve button holes of the Fate window stay holes (the buttons are drawn over them). The push button
    /// <c>FateBut</c> 293 of both windows is the generic button.</summary>
    private static IEnumerable<ChromeRecipe> PaneRecipes()
    {
        yield return new ChromeRecipe("DialogueWindow.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, [new ChromeSunk(8, 6, 394, 95, ChromeMaterial.Pit)], Holes: ChromeHoles.None);
        List<ChromePart> spell = [];
        for (var college = 0; college < SpellPaneRules.Colleges; college++) spell.Add(new ChromeKeep(SpellPaneRules.CollegeButtonX[college] - SpellPaneRules.BackgroundX + 1, SpellPaneRules.CollegeButtonY - SpellPaneRules.BackgroundY + 1, 23, 23, true)); // a dome lies one pixel in from its 24 x 24 button's corner
        foreach (var x in SpellPaneRules.SpellButtonX) spell.Add(new ChromeSunk(x - SpellPaneRules.BackgroundX - 2, SpellPaneRules.SpellButtonY - SpellPaneRules.BackgroundY - 2, 36, 36, ChromeMaterial.Pit)); // a 32 x 32 icon button in a 36 x 36 well
        spell.AddRange([new ChromeSunk(10, 80, 391, 22, ChromeMaterial.Pit), new ChromeRivet(6.5, 91, 2.5), new ChromeRivet(404.5, 91, 2.5)]);
        yield return new ChromeRecipe("spell_ro.art", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, spell, Holes: ChromeHoles.None);
        List<ChromePart> skill = [];
        foreach (var x in SkillPaneRules.ButtonX) skill.Add(new ChromeSunk(x - SkillPaneRules.BackgroundX - 6, SkillPaneRules.ButtonY - SkillPaneRules.BackgroundY - 6, 43, 44, ChromeMaterial.Pit)); // a 32 x 32 button in a 43 x 44 well
        skill.AddRange([new ChromeSunk(10, 73, 391, 22, ChromeMaterial.Pit), new ChromeRivet(6.5, 84, 2.5), new ChromeRivet(404.5, 84, 2.5)]);
        yield return new ChromeRecipe("Skill_Rot.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, skill, Holes: ChromeHoles.None);
        List<ChromePart> fate = [];
        for (var bar = 0; bar < 6; bar++) fate.Add(new ChromeSunk(6, 19 + 36 * bar, 238, 18, ChromeMaterial.Pit));
        yield return new ChromeRecipe("Fate_Base.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, fate, Holes: ChromeHoles.None);
        List<ChromePart> sleep = [];
        for (var bar = 0; bar < 4; bar++) sleep.Add(new ChromeSunk(6, 19 + 36 * bar, 216, 18, ChromeMaterial.Pit));
        yield return new ChromeRecipe("Sleep_Base.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, sleep, Holes: ChromeHoles.None);
        yield return new ChromeRecipe("FateBut.ART", ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, []);
    }

    // ARC-UI-1199: the schematic window's frames (Legacy pixels of each art; the layout of AreaSchematicWindowLayout). The base (365, 800 x 400) is wood with a blueprint page (a ruled sheet of paper
    // 201..706 across, 13..379 down, ruled every 32 units from x 227 and y 22), the round view's square hole (the lens art 231 draws its ring over it), the record's picture hole (a brass rim), the two component
    // slots' boxes (sunk, filled with paper under the found or missing tint), the channel of the assemble button with the sockets of its two lamps, the two list buttons' sockets, and eight tab slots
    // that take no rim (a tab is a bookmark that sticks out of the page's edge). The two gears with a mortarboard and a scroll (the learned and found buttons' labels) stay pictures. The tabs
    // (27 x 40) are their ground (the wood and the paper's first units) and the paper bookmark of their own Legacy frame; a page button is the sheet under it and the arrow.
    private const int PageX = 201, PageY = 13, PageWidth = 506, PageHeight = 367, RuleStep = 32, RuleX = 227, RuleY = 22;

    private static ChromeRuling PageRuling(int x, int y, int width, int height) =>
        new(0, 0, width, height, RuleStep, (((RuleX - x) % RuleStep) + RuleStep) % RuleStep, (((RuleY - y) % RuleStep) + RuleStep) % RuleStep);

    private static IEnumerable<ChromeRecipe> SchematicRecipes()
    {
        var parts = new List<ChromePart>
        {
            new ChromePlate(PageX, PageY, PageWidth, PageHeight, ChromeMaterial.Paper, Bevel: false),
            new ChromeRuling(PageX, PageY, PageWidth, PageHeight, RuleStep, RuleX - PageX, RuleY - PageY),
            new ChromeSunk(577, 179, 103, 71, ChromeMaterial.Paper), new ChromeSunk(577, 275, 103, 71, ChromeMaterial.Paper),
            new ChromeSunk(39, 334, 126, 30, ChromeMaterial.Pit),
            new ChromeBare(AreaSchematicWindowLayout.RoundView.X, AreaSchematicWindowLayout.RoundView.Y, AreaSchematicWindowLayout.RoundView.Width, AreaSchematicWindowLayout.RoundView.Height),
            new ChromeKeep(72, 144, 90, 70, true), new ChromeKeep(71, 231, 96, 72, true),
        };
        for (var d = 0; d < AreaSchematicWindowLayout.Disciplines; d++)
        {
            var (x, y) = AreaSchematicWindowLayout.DisciplineTab(d);
            parts.Add(new ChromeBare(x, y, 27, 40));
        }
        yield return new ChromeRecipe("Schematic_Base.ART", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, parts);
        var tabs = new[] { "Anatomical_Tab.ART", "Chemistry_Tab.ART", "Electrical_Tab.ART", "Explosives_Tab.ART", "GunSmithy_Tab.ART", "Mechanical_Tab.ART", "Smithy_Tab.ART", "Theraputics_Tab.ART" };
        for (var d = 0; d < tabs.Length; d++)
        {
            var (x, y) = AreaSchematicWindowLayout.DisciplineTab(d);
            yield return new ChromeRecipe(tabs[d], ChromeKind.Tab, ChromeMaterial.Wood, ChromeSides.None,
                [new ChromePlate(0, 0, 27, 40, ChromeMaterial.WoodDark, Bevel: false), new ChromePlate(0, 0, 12, 40, ChromeMaterial.Paper, Bevel: false)], OriginX: x, OriginY: y, TabPaper: ChromeMaterial.Paper);
        }
        var (px, py) = AreaSchematicWindowLayout.Previous;
        var (nx, ny) = AreaSchematicWindowLayout.Next;
        yield return new ChromeRecipe("Schm_LArrow.ART", ChromeKind.Panel, ChromeMaterial.Paper, ChromeSides.None, [PageRuling(px, py, 18, 22), new ChromeGlyph(0, 0, 18, 22)], Holes: ChromeHoles.None, OriginX: px, OriginY: py);
        yield return new ChromeRecipe("Schm_RArrow.ART", ChromeKind.Panel, ChromeMaterial.Paper, ChromeSides.None, [PageRuling(nx, ny, 18, 22), new ChromeGlyph(0, 0, 18, 22)], Holes: ChromeHoles.None, OriginX: nx, OriginY: ny);
    }

    /// <summary>ARC-UI-1817: the follower panel's frames (arts 501–503, 506–509, 826; Legacy pixels of each art; the layout of <see cref="AreaFollowerFrameLayout"/> and
    /// <see cref="AreaFollowerPanelRules"/>). The portrait frame (503, 40 × 49) is an iron plate whose 32 × 32 hole at (4, 4) is the portrait (no rim: the original's frame has none) and whose lower part
    /// holds the two bar channels (a black pit with an iron divider; the bars 504, 505 and 616 are drawn over it and stay pictures). The show/hide strip (501 and 502, 67 × 13) is an iron plate with a rivet at each end and
    /// the Legacy arrow kept (<see cref="ChromeMarks"/>); the scroll buttons (506 to 509, 33 × 13 and 34 × 13, their end looks too) are iron plates with the Legacy arrow kept. The order menu (826, 136 × 148) is a slate slab with the four dark
    /// bands under its odd rows (the rows' words are drawn over it).</summary>
    private static IEnumerable<ChromeRecipe> FollowerRecipes()
    {
        yield return new ChromeRecipe("FollowerFrame.ART", ChromeKind.Panel, ChromeMaterial.Iron, ChromeSides.None,
        [
            new ChromePlate(0, 0, AreaFollowerFrameLayout.Width, AreaFollowerFrameLayout.Height, ChromeMaterial.Iron),
            new ChromePlate(3, 3, 34, 34, ChromeMaterial.Pit, Bevel: false), // the one-unit black line around the portrait hole (the hole itself stays clear)
            new ChromePlate(2, 38, 36, 10, ChromeMaterial.Pit, Bevel: false),
            new ChromePlate(3, 42, 34, 2, ChromeMaterial.Iron, Bevel: false),
        ], Holes: ChromeHoles.None);
        foreach (var file in new[] { "FollowerArrow_DWN.ART", "FollowerArrow_UP.ART" })
            yield return new ChromeRecipe(file, ChromeKind.Panel, ChromeMaterial.Iron, ChromeSides.None,
                [new ChromePlate(0, 0, AreaFollowerPanelRules.ToggleRect.Width, AreaFollowerPanelRules.ToggleRect.Height, ChromeMaterial.Iron),
                    new ChromeRivet(5.5, 6.5, 2.7), new ChromeRivet(59.5, 6.5, 2.7), new ChromeMarks(24, 2, 19, 10)], Holes: ChromeHoles.None);
        foreach (var (file, width) in new[] { ("Follow_Scroll_up.ART", AreaFollowerPanelRules.UpRect.Width), ("Follow_Scroll_up_OFF.ART", AreaFollowerPanelRules.UpRect.Width),
            ("Follow_Scroll_dwn.ART", AreaFollowerPanelRules.DownRect.Width), ("Follow_Scroll_dwn_OFF.ART", AreaFollowerPanelRules.DownRect.Width) })
            yield return new ChromeRecipe(file, ChromeKind.Panel, ChromeMaterial.Iron, ChromeSides.None,
                [new ChromePlate(0, 0, width, AreaFollowerPanelRules.UpRect.Height, ChromeMaterial.Iron), new ChromeMarks(4, 2, width - 8, 9)], Holes: ChromeHoles.None);
        var menu = new List<ChromePart> { new ChromePlate(0, 0, MenuWidth, MenuHeight, ChromeMaterial.Slate) };
        for (var k = 0; k < 4; k++) menu.Add(new ChromePlate(6, 20 + 36 * k, 124, 16, ChromeMaterial.Dark, Bevel: false));
        yield return new ChromeRecipe("FollwerComandMenu.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.None, menu, Holes: ChromeHoles.None);
    }

    /// <summary>ARC-UI-1817: the order menu's art size (826, 136 × 148 Legacy pixels).</summary>
    public const int MenuWidth = 136, MenuHeight = 148;

    // ARC-UI-1819: the common dialog box (822 `DialogueBox`, 325 x 136, opaque stone) and its two round buttons (823 `Lil_Accept`, 824 `Lil_Cancel`, 28 x 29, three frames: idle, down, lit). The box is a
    // `Slate` panel with a rim on all sides, like the sheet's pages: a sunk black pit for the text (the original's brass line with curled corners round its black field, measured at 12..312 across and
    // 6..82 down; the curls are not drawn, as in S5) holds the white art-font lines, which are drawn text. The buttons are the generic button recipe (generated brass face and rim following the round
    // silhouette, sunk on the down frame, lit on the lit frame) with the glyph kept: the green check and the red cross are the state of the answer, so they stay pictures.
    private static IEnumerable<ChromeRecipe> DialogRecipes()
    {
        yield return new ChromeRecipe("DialogueBox.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, [new ChromeSunk(12, 6, 301, 77, ChromeMaterial.Pit)]);
        foreach (var file in new[] { "Lil_Accept.ART", "Lil_Cancel.ART" })
            yield return new ChromeRecipe(file, ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, []);
    }

    public static IReadOnlyList<ChromeRecipe> All { get; } = Build();

    private static readonly Dictionary<string, ChromeRecipe> ByName = All.ToDictionary(recipe => recipe.NameSha256, StringComparer.Ordinal);

    public static ChromeRecipe? For(HdSpriteQuery? query) => query is null ? null : ByName.GetValueOrDefault(query.NameSha256);

    private static ChromeRecipe[] Build()
    {
        var top = new ChromeRecipe("inttop.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.Bottom,
        [
            new ChromePlate(150, 0, 112, 41, ChromeMaterial.Iron, 30, ChromeCorner.BottomRight),
            new ChromePlate(532, 0, 268, 41, ChromeMaterial.Iron, 30, ChromeCorner.BottomLeft),
            new ChromeSunk(189, 16, 27, 14, ChromeMaterial.Dark),
            new ChromeRivet(239.5, 4.5, 2.5), new ChromeRivet(219.5, 33.5, 2.5), new ChromeRivet(556.5, 4.5, 2.5), new ChromeRivet(572.5, 34.5, 2.5),
            new ChromeKeep(186, 1, 32, 11),
            new ChromeKeep(576, 1, 24, 38),
        ]);
        List<ChromePart> bottomParts =
        [
            new ChromeSunk(190, 47, 420, 112, ChromeMaterial.Dark),
            new ChromeSunk(57, 66, 100, 27, ChromeMaterial.Dark),
            new ChromeSunk(64, 102, 41, 41, ChromeMaterial.WoodDark),
            new ChromeSunk(111, 102, 38, 41, ChromeMaterial.WoodDark),
        ];
        for (var k = 0; k < 5; k++)
        {
            bottomParts.Add(new ChromeSunk(197 + 39 * k, 2, 35, 35, ChromeMaterial.WoodDark));
            bottomParts.Add(new ChromeSunk(416 + 39 * k, 2, 35, 35, ChromeMaterial.WoodDark));
        }
        bottomParts.AddRange(
        [
            new ChromeKeep(4, 0, 52, 159), new ChromeKeep(740, 0, 60, 159),
            new ChromeKeep(152, 0, 46, 52), new ChromeKeep(604, 0, 48, 48),
            new ChromeKeep(163, 55, 30, 92), new ChromeKeep(607, 55, 36, 95),
            new ChromeKeep(205, 37, 392, 11), new ChromeKeep(387, 0, 30, 40),
        ]);
        var bottom = new ChromeRecipe("intbotom.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.Top, bottomParts);
        // ARC-UI-148: the inventory window's two frames (Legacy pixels of each art).
        List<ChromePart> inventoryParts =
        [
            new ChromeSunk(8, 6, 324, 388, ChromeMaterial.Pit),
            new ChromeGrid(10, 8, 10, 12, 32),
            new ChromeKeep(332, 60, 38, 170),
            new ChromeKeep(332, 192, 78, 32),
        ];
        for (var k = 0; k < 5; k++) inventoryParts.Add(new ChromeSunk(369, 71 + 23 * k, 61, 17, ChromeMaterial.Pit));
        var inventory = new ChromeRecipe("inventor.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, inventoryParts);
        var doll = new ChromeRecipe("pdoll.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None,
        [
            new ChromeSunk(124, 25, 223, 55, ChromeMaterial.Pit),
            new ChromeSunk(18, 100, 330, 310, ChromeMaterial.Pit), // runs off the frame's bottom: the original's box has no bottom edge between its corners
            new ChromeGrid(23, 107, 10, 9, 32),
            new ChromeKeep(11, 9, 89, 89), // the opaque box behind the live round view
            new ChromeRoundView(55.5, 53.5, 44.5, false),
            new ChromeKeep(10, 303, 112, 97),
            new ChromeKeep(244, 303, 114, 97),
        ]);
        // ARC-UI-1194: the loot, barter and steal windows' frames (Legacy pixels of each art). The grids' last column and row carry a line too (their pit goes on
        // for the scroll bar's chain, which is kept). The curly flourishes of the original's boxes are not drawn (as in the inventory).
        List<ChromePart> lootParts = [new ChromeRoundView(60.5, 61.5, 44.5), new ChromeSunk(181, 31, 68, 68, ChromeMaterial.Pit)];
        foreach (var x in new[] { 171.0, 258.0 })
            foreach (var y in new[] { 45.5, 64.5, 82.5 }) lootParts.Add(new ChromeRivet(x, y, 2.5));
        lootParts.AddRange([new ChromeSunk(6, 134, 342, 260, ChromeMaterial.Pit), new ChromeGrid(8, 136, 10, 8, 32, true), new ChromeKeep(332, 136, 12, 256)]);
        var loot = new ChromeRecipe("loot.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, lootParts);
        List<ChromePart> barterParts =
        [
            new ChromeSunk(181, 31, 68, 68, ChromeMaterial.Pit), new ChromeSunk(82, 125, 266, 36, ChromeMaterial.Pit),
            new ChromeSunk(6, 166, 343, 228, ChromeMaterial.Pit), new ChromeGrid(8, 168, 10, 7, 32, true), new ChromeKeep(332, 168, 12, 224),
            new ChromeRivet(147.5, 112, 2.5), new ChromeRivet(282.5, 112, 2.5),
        ];
        var barter = new ChromeRecipe("barter.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, [new ChromeKeep(12, 13, 98, 95, true), .. barterParts]);
        var follower = new ChromeRecipe("barter_follower.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None,
            [new ChromeRoundView(60.5, 61.5, 44.5), new ChromeKeep(145, 45, 40, 40, true), new ChromeKeep(247, 45, 40, 40, true), .. barterParts]);
        var lootDoll = new ChromeRecipe("loot_pd.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None,
        [
            new ChromeSunk(70, 46, 215, 194, ChromeMaterial.Pit),
            new ChromeGrid(73, 49, 10, 9, 21),
            new ChromeKeep(62, 174, 82, 70),
            new ChromeKeep(212, 174, 80, 70),
        ]);
        // The three lenses (231 barter, 232 loot, 233 own window) are the round view's ring around the live game view's circle.
        var lenses = new[] { "lns_bart.art", "lns_loot.art", "lns_papr.art" }.Select(file => new ChromeRecipe(file, ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, [new ChromeRoundView(44.5, 44.5, 44.5)]));
        string[] buttons = ["char_but.art", "log_but.art", "invn_but.art", "tmap_but.art", "lilgrnbut.art",
            "combat_button.art", "skills_button.art", "spells_button.art", "schematics_button.art",
            // ARC-UI-1195: the character sheet's buttons (the + and − of the stats, skills, technology and spells pages, the page tabs, the skill group and discipline buttons).
            "char_minus.art", "char_plus.art", "SP_Minus.ART", "SP_Plus.ART", "SkilAddBut.ART", "SkilMinusBut.ART", "SpellTech_Add.ART", "SpellTech_Minus.ART",
            "char_Common_Skills.art", "char_Tech_Skills.art", "char_Spells_Skills.art", "char_Schem_Skills.ART",
            "Combat_But.ART", "Thieving_But.ART", "Social_But.ART", "Technological_But.ART",
            "Anatomical_But.ART", "Chemistry_But.ART", "Electrical_But.ART", "Explosives_But.ART", "GunSmithy_But.ART", "Mechanical_But.ART", "Smithy_But.ART", "Therapeutics_But.ART",
            // ARC-UI-1199: the schematic window's two list buttons (art 6) and its assemble button (357).
            "bgrn_but.art", "CombineButt.ART"];
        return [top, bottom, inventory, doll, loot, barter, follower, lootDoll, .. lenses, .. SheetPanels(), .. MapPanels(), .. LogbookRecipes(), .. PaneRecipes(), .. SchematicRecipes(), .. FollowerRecipes(), .. DialogRecipes(), .. buttons.Select(file => new ChromeRecipe(file, ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, [])),
            // ARC-UI-1784: the spells page's sixteen college tabs (24 x 24 balls, three frames): a generated rim around the ball's own picture.
            .. CollegeTabFiles.Select(file => new ChromeRecipe(file, ChromeKind.Button, ChromeMaterial.Face, ChromeSides.None, [], KeepFace: true))];
    }

    // ARC-UI-1197: the world map and town map window (138 `mapmain`, 800 x 400, wood). The map view's hole gets the brass rim every hole gets; the two readout boxes (680, 157 and
    // 680, 189) are sunk black pits the window writes its numbers in. Kept pictures: the portrait's brass ring (the lens art 198 is drawn over its hole), the compass rose,
    // the red route sketch and the Travel button's brass ring, and the brass pill under the view's bottom edge (the cover 217 and the compass button 139 are drawn on it). The holes of
    // the zoom and whole-map buttons are covered by those buttons. The map picture itself, the pieces the windows do not draw (140, 199, 219, 220) are not part of it.
    private static ChromeRecipe[] MapPanels()
    {
        List<ChromePart> window =
        [
            new ChromeSunk(680, 157, 94, 22, ChromeMaterial.Pit), new ChromeSunk(680, 189, 94, 22, ChromeMaterial.Pit),
            new ChromeRoundView(69.5, 68.5, 44.5),
            new ChromeKeep(19, 19, 100, 100, true),
            new ChromeKeep(704, 51, 47, 47, true),
            new ChromeKeep(716, 26, 23, 23, true), new ChromeKeep(683, 63, 23, 23, true), new ChromeKeep(749, 63, 23, 23, true), new ChromeKeep(717, 97, 23, 23, true),
            new ChromeKeep(725, 13, 5, 15), new ChromeKeep(725, 119, 5, 33), new ChromeKeep(666, 72, 19, 5), new ChromeKeep(771, 72, 20, 5),
            new ChromeSunk(673, 266, 110, 41, ChromeMaterial.WoodDark), new ChromeKeep(675, 268, 106, 38),
            new ChromeKeep(691, 307, 70, 69, true),
            new ChromeKeep(295, 341, 206, 51),
        ];
        return [new ChromeRecipe("mapmain.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, window)];
    }

    // ARC-UI-1195: the character sheet's frames (Legacy pixels of each art). Its window (22) is wood with the sunk name box, the info box and the page pit; the stat ovals are
    // sunk boxes with a rivet at each end and the labels baked into the art (ST, CN, ..) stay pictures; the lamp sockets, the close ring, the alignment gauge, the health and
    // fatigue discs and the experience ruler are painted illustrations that stay pictures. The three pages (235 x 325) are iron with a rim.
    private static ChromeRecipe[] SheetPanels()
    {
        List<ChromePart> window =
        [
            new ChromeSunk(119, 9, 360, 87, ChromeMaterial.Pit),
            new ChromeSunk(750, 18, 39, 38, ChromeMaterial.Pit),
            new ChromeSunk(497, 59, 244, 332, ChromeMaterial.Pit),
            new ChromeSunk(24, 278, 452, 113, ChromeMaterial.Pit),
            new ChromeKeep(308, 294, 3, 84),
            new ChromeKeep(8, 6, 97, 97, true),
            new ChromeKeep(6, 131, 100, 100, true),
            new ChromeKeep(411, 127, 76, 56, true), new ChromeKeep(411, 197, 76, 56, true),
            new ChromeKeep(747, 60, 46, 330),
        ];
        foreach (var x in new[] { 524, 592, 660 }) window.Add(new ChromeKeep(x, 5, 52, 52, true));
        for (var row = 0; row < 4; row++)
        {
            var y = 40 * row;
            window.Add(new ChromeSunk(172, 111 + y, 70, 31, ChromeMaterial.Pit)); window.Add(new ChromeSunk(315, 111 + y, 70, 31, ChromeMaterial.Pit));
            window.Add(new ChromeKeep(127, 111 + y, 40, 30)); window.Add(new ChromeKeep(259, 111 + y, 54, 30));
            foreach (var x in new[] { 178.5, 235.5, 321.5, 378.5 }) window.Add(new ChromeRivet(x, 126.5 + y, 3.5));
        }
        List<ChromePart> skills = [];
        foreach (var x in new[] { 19, 74, 129, 183 }) skills.Add(new ChromeKeep(x, 16, 36, 35, true));
        for (var row = 0; row < 4; row++) skills.Add(new ChromeKeep(10, 61 + 66 * row, 215, 64));
        List<ChromePart> disciplines = [new ChromeKeep(12, 63, 213, 29), new ChromeKeep(10, 93, 24, 230)];
        List<ChromePart> spells = [];
        foreach (var x in new[] { 12, 37, 62, 87, 112, 137, 161, 186 }) spells.Add(new ChromeKeep(x, 14, 26, 26, true));
        foreach (var x in new[] { 24, 49, 74, 99, 123, 148, 173, 198 }) spells.Add(new ChromeKeep(x, 39, 26, 26, true));
        foreach (var y in new[] { 82, 130, 177, 225, 272 }) spells.Add(new ChromeSunk(16, y - 2, 36, 36, ChromeMaterial.Pit));
        return
        [
            new ChromeRecipe("char_maint.art", ChromeKind.Panel, ChromeMaterial.Wood, ChromeSides.None, window),
            new ChromeRecipe("Skills_Window.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, skills),
            new ChromeRecipe("Disciplines_Window.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, disciplines),
            new ChromeRecipe("char_spells.art", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, spells),
            // ARC-UI-1784: the schemes page (567, the auto-level tab): a sunk plaque for the scheme's name with a rivet at each end, the list's pit and the scroll bar's track
            // (the bar's arrows and thumb are their own arts, drawn over the track).
            new ChromeRecipe("Scheme_Rot.ART", ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All,
            [
                new ChromeSunk(4, 13, 227, 33, ChromeMaterial.Pit), new ChromeRivet(13.5, 29.5, 3.5), new ChromeRivet(221.5, 29.5, 3.5),
                new ChromeSunk(12, 57, 194, 258, ChromeMaterial.Pit), new ChromeSunk(206, 57, 20, 258, ChromeMaterial.Pit),
            ]),
        ];
    }
}

/// <summary>ARC-UI-140: paints a recipe at a whole device scale (ARC-UI-141; <see cref="Scale"/> = 4 by default) × the Legacy frame. Godot-free and deterministic: the same inputs give the
/// same bytes. Alpha is the Legacy alpha upscaled (round holes get an anti-aliased circular edge), so the silhouette, holes and hit tests
/// are the original's.</summary>
public static class InterfaceChromeRenderer
{
    /// <summary>The HD pack's scale (the redraws are 4× the Legacy frame) and the default render scale.</summary>
    public const int Scale = 4;
    public const int MaxSide = 1024;
    /// <summary>The largest device scale rendered (an 8K screen at 800 × 600 units fits 7).</summary>
    public const int MaxScale = 8;

    private readonly record struct Rgb(float R, float G, float B)
    {
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public Rgb Times(float k) => new(R * k, G * k, B * k);
    }

    private static readonly Rgb Ink = new(20, 14, 8), BrassLight = new(214, 178, 96), Brass = new(160, 120, 52), BrassDark = new(96, 70, 30);
    private static readonly Rgb WoodLowDefault = new(58, 37, 20), WoodHighDefault = new(112, 76, 43), IronLow = new(44, 46, 50), IronHigh = new(96, 98, 104);
    private static readonly Rgb SlateLow = new(20, 22, 26), SlateHigh = new(66, 68, 74);
    private static readonly Rgb PitFill = new(3, 3, 5), GridLine = new(48, 54, 64);
    private static readonly Rgb DarkFill = new(8, 8, 14), FaceHigh = new(82, 74, 38), FaceLow = new(34, 30, 14), White = new(255, 255, 255);

    private sealed class Canvas
    {
        public readonly int W, H, LegacyW, LegacyH;
        /// <summary>Device pixels per Legacy pixel (whole or, since ARC-UI-148, fractional).</summary>
        public readonly double Scale;
        /// <summary>The width of a one-unit line in device pixels: <c>max(1, round(Scale))</c>. A hairline stays one device pixel at every scale and equal
        /// bands stay equal (the device-pixel grid rule of docs/rendering/hd-interface-density.md); at a whole scale it is the scale.</summary>
        public readonly int S;
        public readonly Rgb[] Color;
        public readonly float[] Alpha;
        public readonly byte[] Legacy;
        public readonly byte[]? Picture;
        /// <summary>The wood's darkest and lightest colour: our own defaults, or (a panel) matched to the Legacy frame's own wood (<see cref="TuneWood"/>).</summary>
        public Rgb WoodLow = InterfaceChromeRenderer.WoodLowDefault, WoodHigh = InterfaceChromeRenderer.WoodHighDefault;
        /// <summary>ARC-UI-1196: where the art's corner lies in the window's larger picture (<see cref="ChromeRecipe.OriginX"/>): the materials are sampled at that offset.</summary>
        public double OriginX, OriginY;

        public Canvas(int legacyW, int legacyH, byte[] legacy, byte[]? picture, double scale)
        {
            LegacyW = legacyW; LegacyH = legacyH; Scale = scale; S = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
            W = Edge(legacyW); H = Edge(legacyH); Legacy = legacy; Picture = picture;
            Color = new Rgb[W * H]; Alpha = new float[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Alpha[y * W + x] = LegacyOpaque(Cell(x), Cell(y)) ? 1f : 0f;
        }

        /// <summary>The device pixel of an edge at <paramref name="legacy"/> Legacy pixels: <c>round(x × Scale)</c> (neighbouring rectangles never gap or overlap).</summary>
        public int Edge(double legacy) => (int)Math.Round(legacy * Scale, MidpointRounding.AwayFromZero);

        /// <summary>The Legacy pixel whose device span <c>[Edge(c), Edge(c + 1))</c> holds device pixel <paramref name="device"/> (the inverse of <see cref="Edge"/>, so a
        /// rectangle drawn from rounded edges and the Legacy silhouette agree to the pixel); at a whole scale it is <c>device / scale</c>.</summary>
        public int Cell(int device)
        {
            // Start from the exact quotient and settle on Edge itself, so rounding ties never disagree with it.
            var cell = Math.Max(0, (int)Math.Floor((device + 0.5) / Scale));
            while (cell > 0 && Edge(cell) > device) cell--;
            while (Edge(cell + 1) <= device) cell++;
            return cell;
        }

        /// <summary>A position in Legacy pixels of the centre of a device pixel (the unit the materials are sampled in).</summary>
        public float At(int device) => (float)((device + 0.5) / Scale);

        public bool LegacyOpaque(int lx, int ly) => lx >= 0 && ly >= 0 && lx < LegacyW && ly < LegacyH && Legacy[(ly * LegacyW + lx) * 4 + 3] > 0;

        public Rgb PictureAt(int x, int y)
        {
            if (Picture is { } p) { var i = (y * W + x) * 4; return new Rgb(p[i], p[i + 1], p[i + 2]); }
            var j = (Math.Min(Cell(y), LegacyH - 1) * LegacyW + Math.Min(Cell(x), LegacyW - 1)) * 4;
            return new Rgb(Legacy[j], Legacy[j + 1], Legacy[j + 2]);
        }

        public void Blend(int x, int y, Rgb c, float coverage)
        {
            if (coverage <= 0) return;
            var i = y * W + x;
            Color[i] = coverage >= 1 ? c : Rgb.Lerp(Color[i], c, coverage);
        }
    }

    /// <summary>Paints frame <paramref name="frameIndex"/> of the recipe's art at <paramref name="scale"/> device pixels per Legacy pixel
    /// (ARC-UI-141: the window's exact device scale; 1 to <see cref="MaxScale"/>). <paramref name="legacy"/> is the Legacy RGBA
    /// (width × height × 4); <paramref name="picture"/> is the HD redraw at the same <paramref name="scale"/>× (same layout; see
    /// <see cref="ResamplePicture"/>) or empty to sample the Legacy pixels. Returns RGBA of (width × scale) × (height × scale).</summary>
    public static byte[] Render(ChromeRecipe recipe, int width, int height, ReadOnlySpan<byte> legacy, ReadOnlySpan<byte> picture, int frameIndex, double scale = Scale)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide) throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height}");
        if (!(scale >= 1 && scale <= MaxScale)) throw new ArgumentOutOfRangeException(nameof(scale), scale.ToString());
        if (legacy.Length != width * height * 4) throw new ArgumentException("legacy RGBA size", nameof(legacy));
        var (pixelWidth, pixelHeight) = PixelSize(width, height, scale);
        if (!picture.IsEmpty && picture.Length != pixelWidth * pixelHeight * 4) throw new ArgumentException("picture RGBA size", nameof(picture));
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var canvas = new Canvas(width, height, legacy.ToArray(), picture.IsEmpty ? null : picture.ToArray(), scale) { OriginX = recipe.OriginX, OriginY = recipe.OriginY };
        if (recipe.Kind == ChromeKind.Button) PaintButton(canvas, frameIndex == recipe.DownFrame, frameIndex == recipe.HoverFrame && recipe.HoverFrame != recipe.DownFrame, recipe.KeepFace);
        else if (recipe.Kind == ChromeKind.Tab) PaintTab(canvas, recipe);
        else PaintPanel(canvas, recipe);

        var output = new byte[canvas.W * canvas.H * 4];
        for (var i = 0; i < canvas.Color.Length; i++)
        {
            var c = canvas.Color[i];
            var alpha = ToByte(canvas.Alpha[i] * 255f);
            // A transparent texel carries ink, not the wood under it, so the mipmapped edge of a hole fades to dark, not to wood.
            if (alpha == 0) c = Ink;
            output[i * 4] = ToByte(c.R); output[i * 4 + 1] = ToByte(c.G); output[i * 4 + 2] = ToByte(c.B);
            output[i * 4 + 3] = alpha;
        }
        return output;
    }

    /// <summary>ARC-UI-148: the pixel size of a frame of <paramref name="width"/> × <paramref name="height"/> Legacy pixels painted at <paramref name="scale"/>
    /// (each edge is <c>round(size × scale)</c>, so a whole scale gives exactly size × scale).</summary>
    public static (int Width, int Height) PixelSize(int width, int height, double scale) =>
        ((int)Math.Round(width * scale, MidpointRounding.AwayFromZero), (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));

    /// <summary>ARC-UI-141: the picture a <see cref="ChromeKeep"/> copies, at the render scale: <paramref name="source"/> (RGBA,
    /// <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/>, e.g. the pack's 4× redraw) averaged over the area each target pixel
    /// covers (alpha-weighted colour, so a transparent texel adds no colour; every target pixel is opaque), the exact box filter for a whole ratio
    /// (4 to 2 and 4 to 1) and the area-weighted one for 4 to 3. A source of the target's size is copied with alpha 255.</summary>
    public static byte[] ResamplePicture(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth < 1 || sourceHeight < 1 || targetWidth < 1 || targetHeight < 1) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (source.Length != sourceWidth * sourceHeight * 4) throw new ArgumentException("source RGBA size", nameof(source));
        if (sourceWidth == targetWidth && sourceHeight == targetHeight)
        {
            // The same size: the pixels as they are, opaque like every other ratio's.
            var copy = source.ToArray();
            for (var i = 3; i < copy.Length; i += 4) copy[i] = 255;
            return copy;
        }
        var output = new byte[targetWidth * targetHeight * 4];
        double fx = (double)sourceWidth / targetWidth, fy = (double)sourceHeight / targetHeight;
        for (var ty = 0; ty < targetHeight; ty++)
        {
            double y0 = ty * fy, y1 = y0 + fy;
            int sy0 = (int)y0, sy1 = Math.Min(sourceHeight - 1, (int)Math.Ceiling(y1) - 1);
            for (var tx = 0; tx < targetWidth; tx++)
            {
                double x0 = tx * fx, x1 = x0 + fx;
                int sx0 = (int)x0, sx1 = Math.Min(sourceWidth - 1, (int)Math.Ceiling(x1) - 1);
                double r = 0, g = 0, b = 0, weight = 0, rawR = 0, rawG = 0, rawB = 0, area = 0;
                for (var sy = sy0; sy <= sy1; sy++)
                {
                    var wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                    for (var sx = sx0; sx <= sx1; sx++)
                    {
                        var w = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
                        var i = (sy * sourceWidth + sx) * 4;
                        var aw = w * source[i + 3];
                        r += aw * source[i]; g += aw * source[i + 1]; b += aw * source[i + 2]; weight += aw;
                        rawR += w * source[i]; rawG += w * source[i + 1]; rawB += w * source[i + 2]; area += w;
                    }
                }
                var o = (ty * targetWidth + tx) * 4;
                // A fully transparent area keeps its own colour (the texel under a hole is never shown, but stays defined).
                if (weight > 0) { output[o] = (byte)Math.Round(r / weight); output[o + 1] = (byte)Math.Round(g / weight); output[o + 2] = (byte)Math.Round(b / weight); }
                else { output[o] = (byte)Math.Round(rawR / area); output[o + 1] = (byte)Math.Round(rawG / area); output[o + 2] = (byte)Math.Round(rawB / area); }
                output[o + 3] = 255;
            }
        }
        return output;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private static void PaintPanel(Canvas c, ChromeRecipe recipe)
    {
        TuneWood(c, recipe);
        Fill(c, 0, 0, c.W, c.H, recipe.Base);
        foreach (var part in recipe.Parts)
        {
            switch (part)
            {
                case ChromePlate plate: Plate(c, plate); break;
                case ChromeSunk sunk: Sunk(c, sunk); break;
                case ChromeGrid grid: Grid(c, grid); break;
                case ChromeRuling ruling: Ruling(c, ruling); break;
            }
        }
        if (recipe.Holes != ChromeHoles.None)
            foreach (var hole in Holes(c))
                if (!recipe.Parts.OfType<ChromeRoundView>().Any(view => view.CenterX >= hole.X0 && view.CenterX < hole.X1 && view.CenterY >= hole.Y0 && view.CenterY < hole.Y1)
                    && !recipe.Parts.OfType<ChromeBare>().Any(bare => bare.X + bare.Width * 0.5 >= hole.X0 && bare.X + bare.Width * 0.5 < hole.X1 && bare.Y + bare.Height * 0.5 >= hole.Y0 && bare.Y + bare.Height * 0.5 < hole.Y1))
                    HoleRim(c, hole, recipe.Holes == ChromeHoles.Cloth);
        foreach (var part in recipe.Parts)
        {
            switch (part)
            {
                case ChromeRoundView view: RoundView(c, view); break;
                case ChromeRivet rivet: Rivet(c, rivet); break;
                case ChromeKeep keep: Keep(c, c.Edge(keep.X), c.Edge(keep.Y), c.Edge(keep.X + keep.Width) - c.Edge(keep.X), c.Edge(keep.Y + keep.Height) - c.Edge(keep.Y), keep.Round); break;
                case ChromeGlyph glyph: Glyph(c, glyph); break;
                case ChromeMarks marks: Marks(c, marks); break;
            }
        }
        Rim(c, recipe.Rim);
    }

    // ARC-UI-1196: a logbook tab. Each tab art draws its own paper bookmark (its place and slanted corner differ from tab to tab and with the pressed frame) on the stack's
    // edge and the cover. The paper is read from the Legacy frame: a pixel is paper when it is nearly as bright as the frame's brightest tenth, and a row's paper runs from its first
    // to its last such pixel (the glyph's dark pixels are inside). The ground is the recipe's parts (the stack's edge and the cover's cloth, the table's wood beyond); the paper
    // is parchment with a dark edge on its lower right, a light one on its upper left, a soft shadow beside it, and the glyph's marks multiplied onto it.
    private static void PaintTab(Canvas c, ChromeRecipe recipe)
    {
        TuneWood(c, recipe);
        Fill(c, 0, 0, c.W, c.H, recipe.Base);
        foreach (var plate in recipe.Parts.OfType<ChromePlate>()) Plate(c, plate);
        var paper = PaperMask(c);
        bool PaperIn(int x, int y) => x >= 0 && y >= 0 && x < c.W && y < c.H && paper[c.Cell(y) * c.LegacyW + c.Cell(x)];
        // Beyond the art's edge the page goes on, so no edge is drawn there.
        bool PaperAt(int x, int y) => x < 0 || y < 0 || x >= c.W || y >= c.H || paper[c.Cell(y) * c.LegacyW + c.Cell(x)];
        var inks = new List<int>[3] { [], [], [] };
        for (var i = 0; i < paper.Length; i++)
        {
            if (!paper[i] || c.Legacy[i * 4 + 3] == 0) continue;
            for (var k = 0; k < 3; k++) inks[k].Add(c.Legacy[i * 4 + k]);
        }
        var median = inks.Select(list => { list.Sort(); return list.Count == 0 ? 255f : Math.Max(24, list[list.Count / 2]); }).ToArray();
        // The marks are darker than the paper; a lighter pixel (the grain of the Legacy paper) may lift it by a little only.
        static float Departure(float ratio, float lift) => MathF.Abs(ratio - 1) <= 0.1f ? 0 : MathF.Min(ratio - 1 - MathF.Sign(ratio - 1) * 0.1f, lift);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            if (!PaperAt(x, y))
            {
                // The shadow the paper throws to its right and below.
                if (PaperIn(x - c.S, y) || PaperIn(x - 2 * c.S, y) || PaperIn(x, y - c.S) || PaperIn(x - c.S, y - c.S)) c.Color[i] = c.Color[i].Times(0.55f);
                continue;
            }
            var colour = Material(c, recipe.TabPaper, c.At(x), c.At(y));
            var p = c.PictureAt(x, y);
            colour = new Rgb(Clamp8(colour.R * (1 + 1.15f * Departure(p.R / median[0], 0.12f))), Clamp8(colour.G * (1 + 1.15f * Departure(p.G / median[1], 0.12f))), Clamp8(colour.B * (1 + 1.15f * Departure(p.B / median[2], 0.12f))));
            if (!PaperAt(x + c.S, y) || !PaperAt(x, y + c.S)) colour = colour.Times(0.62f);
            else if (!PaperAt(x - c.S, y) || !PaperAt(x, y - c.S)) colour = Rgb.Lerp(colour, White, 0.22f);
            c.Color[i] = colour;
        }
    }

    private static bool[] PaperMask(Canvas c)
    {
        var lum = new float[c.LegacyW * c.LegacyH];
        var opaque = new List<float>();
        for (var i = 0; i < lum.Length; i++)
        {
            if (c.Legacy[i * 4 + 3] == 0) continue;
            lum[i] = c.Legacy[i * 4] * .299f + c.Legacy[i * 4 + 1] * .587f + c.Legacy[i * 4 + 2] * .114f;
            opaque.Add(lum[i]);
        }
        var mask = new bool[lum.Length];
        if (opaque.Count == 0) return mask;
        opaque.Sort();
        var threshold = 0.75f * opaque[(int)(opaque.Count * 0.9)];
        // Per row: the first run of three bright pixels and the last bright pixel (a speckle of the stack's edge is no paper). The paper's left edge is straight, and a glyph's
        // dark pixels would move a row's own start, so the left edge is the tenth percentile of the rows' starts.
        var firsts = new int[c.LegacyH]; var lasts = new int[c.LegacyH];
        for (var y = 0; y < c.LegacyH; y++)
        {
            int first = -1, last = -1, run = 0;
            for (var x = 0; x < c.LegacyW; x++)
            {
                var bright = c.Legacy[(y * c.LegacyW + x) * 4 + 3] > 0 && lum[y * c.LegacyW + x] >= threshold;
                run = bright ? run + 1 : 0;
                if (bright) last = x;
                if (run >= 3 && first < 0) first = x - 2;
            }
            (firsts[y], lasts[y]) = (first, first < 0 ? -1 : last);
        }
        var starts = firsts.Where(f => f >= 0).OrderBy(f => f).ToArray();
        if (starts.Length == 0) return mask;
        var left = starts[starts.Length / 10];
        for (var y = 0; y < c.LegacyH; y++)
            for (var x = left; lasts[y] >= 0 && x <= lasts[y]; x++) mask[y * c.LegacyW + x] = true;
        return mask;
    }

    // ARC-UI-1199: see ChromeRuling. A line is `S` device pixels wide from the device edge of its Legacy position, clipped to the rectangle, and blended faintly over what is there.
    private static readonly Rgb RuleInk = new(170, 112, 36);

    private static void Ruling(Canvas c, ChromeRuling ruling)
    {
        if (ruling.Step <= 0) return;
        int x0 = c.Edge(ruling.X), y0 = c.Edge(ruling.Y), x1 = c.Edge(ruling.X + ruling.Width), y1 = c.Edge(ruling.Y + ruling.Height);
        void Line(int lx0, int ly0, int lx1, int ly1)
        {
            for (var y = Math.Max(y0, ly0); y < Math.Min(Math.Min(y1, c.H), ly1); y++)
            for (var x = Math.Max(x0, lx0); x < Math.Min(Math.Min(x1, c.W), lx1); x++) c.Blend(x, y, RuleInk, 0.5f);
        }
        for (var gx = ruling.GridX; gx < ruling.Width; gx += ruling.Step)
        {
            var dx = c.Edge(ruling.X + gx);
            Line(dx, y0, dx + c.S, y1);
        }
        for (var gy = ruling.GridY; gy < ruling.Height; gy += ruling.Step)
        {
            var dy = c.Edge(ruling.Y + gy);
            Line(x0, dy, x1, dy + c.S);
        }
    }

    // ARC-UI-1817: see ChromeMarks. A device pixel takes the picture's colour when the Legacy cell it lies in is opaque and its channels spread by at least ChromaThreshold.
    private const int ChromaThreshold = 24;

    private static void Marks(Canvas c, ChromeMarks marks)
    {
        int x0 = c.Edge(Math.Max(0, marks.X)), y0 = c.Edge(Math.Max(0, marks.Y)), x1 = c.Edge(Math.Min(c.LegacyW, marks.X + marks.Width)), y1 = c.Edge(Math.Min(c.LegacyH, marks.Y + marks.Height));
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            int cx = c.Cell(x), cy = c.Cell(y);
            if (!c.LegacyOpaque(cx, cy)) continue;
            var j = (cy * c.LegacyW + cx) * 4;
            int r = c.Legacy[j], g = c.Legacy[j + 1], b = c.Legacy[j + 2];
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= ChromaThreshold) c.Color[i] = c.PictureAt(x, y);
        }
    }

    // ARC-UI-1196: see ChromeGlyph. The median of the rectangle's opaque Legacy pixels is its ground; a pixel that departs from it by more than a tenth of the ground
    // (per channel) multiplies what departs onto the generated colour.
    private static void Glyph(Canvas c, ChromeGlyph glyph)
    {
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        for (var y = Math.Max(0, glyph.Y); y < Math.Min(c.LegacyH, glyph.Y + glyph.Height); y++)
        for (var x = Math.Max(0, glyph.X); x < Math.Min(c.LegacyW, glyph.X + glyph.Width); x++)
        {
            if (!c.LegacyOpaque(x, y)) continue;
            var j = (y * c.LegacyW + x) * 4;
            rs.Add(c.Legacy[j]); gs.Add(c.Legacy[j + 1]); bs.Add(c.Legacy[j + 2]);
        }
        if (rs.Count == 0) return;
        rs.Sort(); gs.Sort(); bs.Sort();
        float mr = Math.Max(24, rs[rs.Count / 2]), mg = Math.Max(24, gs[gs.Count / 2]), mb = Math.Max(24, bs[bs.Count / 2]);
        static float Departure(float ratio) => MathF.Abs(ratio - 1) <= 0.1f ? 0 : MathF.Min(ratio - 1 - MathF.Sign(ratio - 1) * 0.1f, 0.12f);
        int x0 = c.Edge(glyph.X), y0 = c.Edge(glyph.Y), x1 = c.Edge(glyph.X + glyph.Width), y1 = c.Edge(glyph.Y + glyph.Height);
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            var p = c.PictureAt(x, y);
            var colour = c.Color[i];
            c.Color[i] = new Rgb(Clamp8(colour.R * (1 + 1.15f * Departure(p.R / mr))), Clamp8(colour.G * (1 + 1.15f * Departure(p.G / mg))), Clamp8(colour.B * (1 + 1.15f * Departure(p.B / mb))));
        }
    }

    // ARC-UI-141: the wood takes the tone of the Legacy frame's own wood (its opaque pixels outside every part's rectangle), so a kept picture
    // (its painted background is the original wood) does not show its box against a lighter or yellower generated wood. Mean colour of that
    // wood is the middle of our grain; its spread (about six standard deviations of the pixels) is the grain's range, never wider than ours.
    private static void TuneWood(Canvas c, ChromeRecipe recipe)
    {
        double sr = 0, sg = 0, sb = 0, qr = 0, qg = 0, qb = 0; long n = 0;
        for (var y = 0; y < c.LegacyH; y++)
        for (var x = 0; x < c.LegacyW; x++)
        {
            if (!c.LegacyOpaque(x, y) || Covered(recipe, x, y)) continue;
            var i = (y * c.LegacyW + x) * 4;
            double r = c.Legacy[i], g = c.Legacy[i + 1], b = c.Legacy[i + 2];
            sr += r; sg += g; sb += b; qr += r * r; qg += g * g; qb += b * b; n++;
        }
        if (n < 64) return; // too little wood to read: keep our own
        double mr = sr / n, mg = sg / n, mb = sb / n;
        double dr = Range(qr / n - mr * mr), dg = Range(qg / n - mg * mg), db = Range(qb / n - mb * mb);
        // The grain's t averages 0.575: low = mean - 0.575 d, high = mean + 0.425 d.
        c.WoodLow = new Rgb(Clamp8(mr - 0.575 * dr), Clamp8(mg - 0.575 * dg), Clamp8(mb - 0.575 * db));
        c.WoodHigh = new Rgb(Clamp8(mr + 0.425 * dr), Clamp8(mg + 0.425 * dg), Clamp8(mb + 0.425 * db));
    }

    private static double Range(double variance) => Math.Min(5.9 * Math.Sqrt(Math.Max(variance, 0)), 90);
    private static float Clamp8(double v) => (float)Math.Clamp(v, 0, 255);

    private static bool Covered(ChromeRecipe recipe, int x, int y)
    {
        foreach (var part in recipe.Parts)
        {
            // ARC-UI-1194: a round view covers its ring (the circle's edge and the three units beyond it), not the wood in the corners around it.
            if (part is ChromeRoundView view)
            {
                double dx = x + 0.5 - view.CenterX, dy = y + 0.5 - view.CenterY, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= view.Radius - 1 && d <= view.Radius + 4) return true;
                continue;
            }
            var (px, py, pw, ph) = part switch
            {
                ChromePlate p => (p.X, p.Y, p.Width, p.Height),
                ChromeSunk p => (p.X, p.Y, p.Width, p.Height),
                ChromeKeep p => (p.X, p.Y, p.Width, p.Height),
                ChromeGrid p => (p.X, p.Y, p.Columns * p.Cell, p.Rows * p.Cell),
                _ => (0, 0, 0, 0),
            };
            if (x >= px && x < px + pw && y >= py && y < py + ph) return true;
        }
        return false;
    }

    private static void Fill(Canvas c, int x0, int y0, int w, int h, ChromeMaterial material)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x0 + w); x++)
            c.Color[y * c.W + x] = Material(c, material, c.At(x), c.At(y));
    }

    private static Rgb Material(Canvas c, ChromeMaterial material, float u, float v) => Material(material, c, u + (float)c.OriginX, v + (float)c.OriginY);

    private static Rgb Material(ChromeMaterial material, Canvas c, float u, float v) => material switch
    {
        ChromeMaterial.Wood => WoodAt(c, u, v),
        ChromeMaterial.WoodDark => WoodAt(c, u, v).Times(0.62f),
        ChromeMaterial.Iron => Rgb.Lerp(IronLow, IronHigh, Math.Clamp(0.2f + 0.65f * Fbm(u * 0.22f, v * 0.22f, 7), 0, 1)),
        ChromeMaterial.Pit => PitFill,
        ChromeMaterial.Slate => Rgb.Lerp(SlateLow, SlateHigh, Math.Clamp(0.15f + 0.75f * Fbm(u * 0.18f, v * 0.18f, 13), 0, 1)),
        ChromeMaterial.Dark => Rgb.Lerp(DarkFill, new Rgb(16, 16, 26), Fbm(u * 0.05f, v * 0.05f, 11)),
        ChromeMaterial.Cloth => ClothAt(u, v),
        ChromeMaterial.Parchment => ParchmentAt(u, v),
        ChromeMaterial.Paper => PaperAt(u, v),
        ChromeMaterial.PageEdge => PageEdgeAt(u, v),
        _ => FaceHigh,
    };

    // ARC-UI-1196: our own cloth, parchment and page edge (colours chosen by eye).
    private static readonly Rgb ClothLow = new(56, 28, 30), ClothHigh = new(98, 50, 52);
    private static readonly Rgb ParchmentLow = new(132, 126, 110), ParchmentHigh = new(176, 170, 152), PaperLow = new(172, 150, 108), PaperHigh = new(222, 200, 154);
    private static readonly Rgb PageEdgeLow = new(70, 63, 56), PageEdgeHigh = new(136, 128, 116);

    /// <summary>The x, in the book's Legacy pixels, of the fold between the two pages (the book art's 286: the left page ends and the right one starts).</summary>
    private const float Fold = 286f;

    private static Rgb ClothAt(float u, float v)
    {
        // A diagonal two-way weave one or two units wide under a slow tone.
        var weave = 0.25f * (MathF.Sin((u + v) * 2.2f) + MathF.Sin((u - v) * 2.2f)) + 0.5f;
        return Rgb.Lerp(ClothLow, ClothHigh, Math.Clamp(0.3f + 0.35f * Fbm(u * 0.08f, v * 0.08f, 41) + 0.35f * weave, 0, 1));
    }

    // ARC-UI-1199: a loose sheet: the same mottling in a warmer cream, with no fold.
    private static Rgb PaperAt(float u, float v)
    {
        var mottle = Fbm(u * 0.035f, v * 0.04f, 21);
        var grain = Fbm(u * 0.4f, v * 0.4f, 22);
        return Rgb.Lerp(PaperLow, PaperHigh, Math.Clamp(0.35f + 0.55f * mottle + 0.1f * (grain - 0.5f), 0, 1));
    }

    private static Rgb ParchmentAt(float u, float v)
    {
        var mottle = Fbm(u * 0.035f, v * 0.04f, 21);
        var grain = Fbm(u * 0.4f, v * 0.4f, 22);
        var colour = Rgb.Lerp(ParchmentLow, ParchmentHigh, Math.Clamp(0.35f + 0.55f * mottle + 0.1f * (grain - 0.5f), 0, 1));
        // The pages sink toward the fold: a soft shade on both sides and a dark line on the fold itself.
        var d = MathF.Abs(u - Fold);
        return colour.Times(1 - 0.4f * MathF.Exp(-d / 20f) - (d < 1.2f ? 0.3f : 0));
    }

    private static Rgb PageEdgeAt(float u, float v)
    {
        // Leaves seen from the side: stripes that run the page's height (the phase depends on u only), a little uneven in tone along them.
        var stripe = 0.5f + 0.5f * MathF.Sin(u * 2.7f + 2.5f * ValueNoise(u * 0.35f, 0.5f, 31));
        return Rgb.Lerp(PageEdgeLow, PageEdgeHigh, Math.Clamp(stripe, 0, 1)).Times(0.93f + 0.14f * ValueNoise(u * 0.5f, v * 0.06f, 32));
    }

    private static Rgb WoodAt(Canvas c, float u, float v)
    {
        var n = Fbm(u * 0.03f, v * 0.42f, 3);
        var stripe = 0.5f + 0.5f * MathF.Sin((v * 0.85f + n * 7f) * MathF.PI);
        return Rgb.Lerp(c.WoodLow, c.WoodHigh, Math.Clamp(0.15f + 0.55f * n + 0.3f * stripe, 0, 1));
    }

    private static void Plate(Canvas c, ChromePlate plate)
    {
        int x0 = c.Edge(plate.X), y0 = c.Edge(plate.Y), x1 = c.Edge(plate.X + plate.Width), y1 = c.Edge(plate.Y + plate.Height);
        var r = c.Edge(plate.CornerRadius);
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float edge; bool lit;
            if (InCorner(plate.Corner, px, py, x0, y0, x1, y1, r, out var cx, out var cy))
            {
                var d = r - MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                if (d <= -0.5f) continue;
                var coverage = Math.Clamp(d + 0.5f, 0, 1);
                var fill = Bevelled(Material(c, plate.Material, px / (float)c.Scale, py / (float)c.Scale), d, (px - cx) + (py - cy) < 0, c.S);
                c.Blend(x, y, fill, coverage);
                continue;
            }
            var (left, right, top, bottom) = (px - x0, x1 - px, py - y0, y1 - py);
            edge = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
            lit = edge == left || edge == top;
            var plain = Material(c, plate.Material, px / (float)c.Scale, py / (float)c.Scale);
            c.Color[y * c.W + x] = plate.Bevel ? Bevelled(plain, edge, lit, c.S) : plain;
        }
    }

    private static bool InCorner(ChromeCorner corner, float px, float py, int x0, int y0, int x1, int y1, int r, out float cx, out float cy)
    {
        (cx, cy) = corner switch
        {
            ChromeCorner.TopLeft => (x0 + r, y0 + r),
            ChromeCorner.TopRight => (x1 - r, y0 + r),
            ChromeCorner.BottomLeft => (x0 + r, y1 - r),
            ChromeCorner.BottomRight => (x1 - r, y1 - r),
            _ => (0f, 0f),
        };
        return corner switch
        {
            ChromeCorner.TopLeft => px < cx && py < cy,
            ChromeCorner.TopRight => px > cx && py < cy,
            ChromeCorner.BottomLeft => px < cx && py > cy,
            ChromeCorner.BottomRight => px > cx && py > cy,
            _ => false,
        };
    }

    // A one-unit bevel inside a plate's edge: lighter on its lit sides, darker on the others.
    private static Rgb Bevelled(Rgb fill, float edgeDistance, bool lit, int scale) =>
        edgeDistance >= scale ? fill : lit ? Rgb.Lerp(fill, White, 0.22f) : fill.Times(0.6f);

    private static void Sunk(Canvas c, ChromeSunk sunk)
    {
        int x0 = c.Edge(sunk.X), y0 = c.Edge(sunk.Y), x1 = c.Edge(sunk.X + sunk.Width), y1 = c.Edge(sunk.Y + sunk.Height);
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
        {
            int left = x - x0, right = x1 - 1 - x, top = y - y0, bottom = y1 - 1 - y;
            var edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            var i = y * c.W + x;
            if (edge < c.S) c.Color[i] = edge == left || edge == top ? BrassDark : BrassLight;
            else if (edge < 2 * c.S) c.Color[i] = Ink;
            else c.Color[i] = Material(c, sunk.Fill, c.At(x), c.At(y));
        }
    }

    private static void Grid(Canvas c, ChromeGrid grid)
    {
        int left = c.Edge(grid.X), top = c.Edge(grid.Y), right = c.Edge(grid.X + grid.Columns * grid.Cell), bottom = c.Edge(grid.Y + grid.Rows * grid.Cell);
        for (var k = 1; k < grid.Columns + (grid.Closed ? 1 : 0); k++)
        {
            var x0 = c.Edge(grid.X + grid.Cell * k) - c.S; // the last S device pixels of the cell, never over the next one
            for (var x = Math.Max(0, x0); x < Math.Min(right, x0 + c.S); x++)
            for (var y = Math.Max(0, top); y < Math.Min(c.H, bottom); y++)
                c.Color[y * c.W + x] = GridLine;
        }
        for (var k = 1; k < grid.Rows + (grid.Closed ? 1 : 0); k++)
        {
            var y0 = c.Edge(grid.Y + grid.Cell * k) - c.S;
            for (var y = Math.Max(0, y0); y < Math.Min(bottom, y0 + c.S); y++)
            for (var x = Math.Max(0, left); x < Math.Min(c.W, right); x++)
                c.Color[y * c.W + x] = GridLine;
        }
    }

    private static void Rivet(Canvas c, ChromeRivet rivet)
    {
        float cx = (float)(rivet.X * c.Scale), cy = (float)(rivet.Y * c.Scale), r = (float)(rivet.Radius * c.Scale);
        for (var y = Math.Max(0, (int)(cy - r - 1)); y < Math.Min(c.H, (int)(cy + r + 2)); y++)
        for (var x = Math.Max(0, (int)(cx - r - 1)); x < Math.Min(c.W, (int)(cx + r + 2)); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            var coverage = Math.Clamp(r - d + 0.5f, 0, 1);
            if (coverage <= 0 || c.Alpha[y * c.W + x] <= 0) continue;
            var light = Math.Clamp(0.5f - (dx + dy) / (2.8f * r), 0, 1);
            var colour = Rgb.Lerp(BrassDark, BrassLight, light);
            c.Blend(x, y, d > r - c.S * 0.6f ? Rgb.Lerp(colour, Ink, 0.6f) : colour, coverage);
        }
    }

    private static void Keep(Canvas c, int x0, int y0, int w, int h, bool round = false)
    {
        float cx = x0 + w * 0.5f, cy = y0 + h * 0.5f, rx = w * 0.5f, ry = h * 0.5f;
        for (var y = Math.Max(0, y0); y < Math.Min(c.H, y0 + h); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(c.W, x0 + w); x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            if (!round) { c.Color[i] = c.PictureAt(x, y); continue; }
            // Inside the inscribed ellipse; its edge is anti-aliased by the distance to the edge, measured along the ray from the centre.
            float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry, d = MathF.Sqrt(dx * dx + dy * dy);
            var edge = (1f - d) * MathF.Min(rx, ry);
            c.Blend(x, y, c.PictureAt(x, y), Math.Clamp(edge + 0.5f, 0, 1));
        }
    }

    private static void Rim(Canvas c, ChromeSides sides)
    {
        if (sides == ChromeSides.None) return;
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var best = int.MaxValue; var lit = false;
            if (sides.HasFlag(ChromeSides.Top) && y < best) { best = y; lit = true; }
            if (sides.HasFlag(ChromeSides.Left) && x < best) { best = x; lit = true; }
            if (sides.HasFlag(ChromeSides.Bottom) && c.H - 1 - y < best) { best = c.H - 1 - y; lit = false; }
            if (sides.HasFlag(ChromeSides.Right) && c.W - 1 - x < best) { best = c.W - 1 - x; lit = false; }
            if (best >= 3 * c.S) continue;
            var i = y * c.W + x;
            c.Color[i] = best < c.S ? Ink : best < 2 * c.S ? (lit ? BrassLight : BrassDark) : Brass;
        }
    }

    /// <summary>A transparent region of the Legacy frame inside the panel, in Legacy pixels; <c>Round</c> when it fills a circle's share of
    /// its box (a round socket), else a rectangle.</summary>
    public readonly record struct Hole(int X0, int Y0, int X1, int Y1, bool Round);

    public static IReadOnlyList<Hole> Holes(int width, int height, ReadOnlySpan<byte> legacy) => Holes(new Canvas(width, height, legacy.ToArray(), null, 1));

    private static List<Hole> Holes(Canvas c)
    {
        var seen = new bool[c.LegacyW * c.LegacyH];
        var holes = new List<Hole>();
        var queue = new Queue<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || c.LegacyOpaque(start % c.LegacyW, start / c.LegacyW)) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
            var border = false;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                int px = p % c.LegacyW, py = p / c.LegacyW;
                count++; x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py);
                border |= px == 0 || py == 0 || px == c.LegacyW - 1 || py == c.LegacyH - 1;
                foreach (var (nx, ny) in new[] { (px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= c.LegacyW || ny >= c.LegacyH) continue;
                    var n = ny * c.LegacyW + nx;
                    if (seen[n] || c.LegacyOpaque(nx, ny)) continue;
                    seen[n] = true; queue.Enqueue(n);
                }
            }
            var box = (x1 - x0 + 1) * (y1 - y0 + 1);
            if (border && count * 4 > c.LegacyW * c.LegacyH) continue; // the outside of a shaped frame, not a hole
            if (count < MinHolePixels) continue; // a stray transparent speck gets no frame
            holes.Add(new Hole(x0, y0, x1 + 1, y1 + 1, IsRound(count, x1 - x0 + 1, y1 - y0 + 1)));
        }
        return holes;
    }

    private const int MinHolePixels = 6;

    // Round: a near-square box (sides within one Legacy pixel) that the shape fills like a circle (π/4 ≈ 0.785 of the box).
    private static bool IsRound(int count, int width, int height) =>
        Math.Abs(width - height) <= 1 && (float)count / (width * height) is > 0.6f and < 0.95f;

    private static void HoleRim(Canvas c, Hole hole, bool cloth = false)
    {
        if (hole.Round) { RoundRim(c, hole); return; }
        float hx0 = c.Edge(hole.X0), hy0 = c.Edge(hole.Y0), hx1 = c.Edge(hole.X1), hy1 = c.Edge(hole.Y1);
        var reach = (int)(2.5f * c.S);
        for (var y = Math.Max(0, (int)hy0 - reach); y < Math.Min(c.H, (int)hy1 + reach); y++)
        for (var x = Math.Max(0, (int)hx0 - reach); x < Math.Min(c.W, (int)hx1 + reach); x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            float px = x + 0.5f, py = y + 0.5f;
            float dx = px < hx0 ? hx0 - px : px > hx1 ? px - hx1 : 0, dy = py < hy0 ? hy0 - py : py > hy1 ? py - hy1 : 0;
            var d = MathF.Max(dx, dy);
            if (d <= 0 || d >= reach) continue; // only the opaque band around the hole's box, never an opaque island inside it
            var lit = dy >= dx ? py < hy0 : px < hx0;
            // ARC-UI-1196: a cover's edge is the cloth's own darker line against the hole and a lighter one behind it.
            if (cloth) c.Color[i] = d < c.S ? c.Color[i].Times(0.42f) : Rgb.Lerp(c.Color[i], White, lit ? 0.2f : 0.05f);
            else c.Color[i] = d < c.S ? Ink : lit ? BrassLight : BrassDark;
        }
    }

    private static void RoundRim(Canvas c, Hole hole)
    {
        float cx = (float)((hole.X0 + hole.X1) * 0.5 * c.Scale), cy = (float)((hole.Y0 + hole.Y1) * 0.5 * c.Scale);
        var r = (float)(((hole.X1 - hole.X0) + (hole.Y1 - hole.Y0)) * 0.25 * c.Scale);
        var reach = (int)(r + 3f * c.S + 2);
        for (var y = Math.Max(0, (int)(cy - reach)); y < Math.Min(c.H, (int)(cy + reach)); y++)
        for (var x = Math.Max(0, (int)(cx - reach)); x < Math.Min(c.W, (int)(cx + reach)); x++)
        {
            var i = y * c.W + x;
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d >= r + 0.5f) continue;
            if (x < c.Edge(hole.X0) || y < c.Edge(hole.Y0) || x >= c.Edge(hole.X1) || y >= c.Edge(hole.Y1)) continue;
            // Inside the socket: the anti-aliased circular edge replaces the Legacy stairs.
            c.Alpha[i] = MathF.Min(c.Alpha[i], Math.Clamp(d - r + 0.5f, 0, 1));
            if (c.Alpha[i] > 0) c.Color[i] = Ink;
        }
        RingBand(c, cx, cy, r);
    }

    // ARC-UI-1194: a window's round view. The circle's inside is smoothed like a round hole's socket (the Legacy stairs give way to an anti-aliased edge),
    // then the ring is laid outside it. A lens art (the circle touches the frame's sides, so the hole finder takes it for the outside of the frame) has no
    // hole of its own to find; its recipe names the circle.
    private static void RoundView(Canvas c, ChromeRoundView view)
    {
        float cx = (float)(view.CenterX * c.Scale), cy = (float)(view.CenterY * c.Scale), r = (float)(view.Radius * c.Scale);
        var reach = (int)(r + 2);
        for (var y = Math.Max(0, (int)(cy - reach)); y < Math.Min(c.H, (int)(cy + reach)); y++)
        for (var x = Math.Max(0, (int)(cx - reach)); x < Math.Min(c.W, (int)(cx + reach)); x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            // Only the circle's edge is smoothed (the pixels within a unit and a half inside it); deeper inside, the Legacy alpha stays whatever it is.
            if (d >= r + 0.5f || d < r - 1.5f * c.S || !view.Shape) continue;
            var i = y * c.W + x;
            c.Alpha[i] = MathF.Min(c.Alpha[i], Math.Clamp(d - r + 0.5f, 0, 1));
            if (c.Alpha[i] > 0) c.Color[i] = Ink;
        }
        RingBand(c, cx, cy, r);
    }

    // The brass ring around a circle (centre and radius in device pixels), on the opaque pixels outside it: an ink edge, lit brass on the upper left,
    // shaded on the lower right, an ink edge again; three units wide. A round hole's socket and a window's round view (ChromeRoundView) share it.
    private static void RingBand(Canvas c, double centreX, double centreY, double radius)
    {
        float cx = (float)centreX, cy = (float)centreY, r = (float)radius;
        var ring = 3f * c.S;
        var reach = (int)(r + ring + 2);
        for (var y = Math.Max(0, (int)(cy - reach)); y < Math.Min(c.H, (int)(cy + reach)); y++)
        for (var x = Math.Max(0, (int)(cx - reach)); x < Math.Min(c.W, (int)(cx + reach)); x++)
        {
            var i = y * c.W + x;
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < r + 0.5f || c.Alpha[i] <= 0 || d > r + ring + 0.5f) continue;
            var t = (d - r) / ring;
            var facing = -(dx + dy) / (1.4142f * d);
            var brass = Rgb.Lerp(BrassDark, BrassLight, Math.Clamp(0.5f + 0.5f * (t < 0.5f ? -facing : facing), 0, 1));
            var colour = t < 0.18f ? Rgb.Lerp(Ink, brass, t / 0.18f) : t > 0.85f ? Rgb.Lerp(brass, Ink, (t - 0.85f) / 0.15f) : brass;
            c.Blend(x, y, colour, Math.Clamp(r + ring + 0.5f - d, 0, 1));
        }
    }

    private static void PaintButton(Canvas c, bool down, bool hover, bool keepFace = false)
    {
        // The silhouette's box and kind (round when its fill is a circle's share of the box).
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
        for (var y = 0; y < c.LegacyH; y++)
        for (var x = 0; x < c.LegacyW; x++)
            if (c.LegacyOpaque(x, y)) { count++; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (count == 0) return;
        var round = IsRound(count, x1 - x0 + 1, y1 - y0 + 1);
        float bx0 = c.Edge(x0), by0 = c.Edge(y0), bx1 = c.Edge(x1 + 1), by1 = c.Edge(y1 + 1);
        float cx = (bx0 + bx1) * 0.5f, cy = (by0 + by1) * 0.5f, radius = MathF.Min(bx1 - bx0, by1 - by0) * 0.5f;
        var glyph = GlyphMask(c, x0, y0, x1, y1);
        for (var y = (int)by0; y < (int)by1; y++)
        for (var x = (int)bx0; x < (int)bx1; x++)
        {
            var i = y * c.W + x;
            if (c.Alpha[i] <= 0) continue;
            float px = x + 0.5f, py = y + 0.5f, dx = px - cx, dy = py - cy;
            float inset; bool lit;
            if (round)
            {
                var d = MathF.Sqrt(dx * dx + dy * dy);
                inset = radius - d;
                lit = dx + dy < 0;
                c.Alpha[i] = MathF.Min(c.Alpha[i], Math.Clamp(inset + 0.5f, 0, 1));
            }
            else
            {
                float left = px - bx0, right = bx1 - px, top = py - by0, bottom = by1 - py;
                inset = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                lit = inset == left || inset == top;
            }
            if (down) lit = !lit;
            var faceT = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / MathF.Max(1, radius), 0, 1);
            var face = Rgb.Lerp(FaceHigh, FaceLow, down ? 1 - faceT * 0.6f : faceT);
            var rim = lit ? BrassLight : BrassDark;
            // Hover lifts the face and the rim toward light brass (the original's hover frame is the lit one).
            if (hover) { face = Rgb.Lerp(face, BrassLight, 0.3f); rim = Rgb.Lerp(rim, White, 0.2f); }
            Rgb colour = inset < c.S ? Ink : inset < 3 * c.S ? rim : inset < 3.6f * c.S ? Ink : face;
            if (inset >= 3.6f * c.S && (keepFace || glyph[Math.Min(c.Cell(y), c.LegacyH - 1) * c.LegacyW + Math.Min(c.Cell(x), c.LegacyW - 1)])) colour = c.PictureAt(x, y);
            c.Color[i] = colour;
        }
    }

    // The glyph of a button: Legacy pixels away from its edge whose colour stands out from the face's median, grown by one pixel.
    private static bool[] GlyphMask(Canvas c, int x0, int y0, int x1, int y1)
    {
        const int Inset = 5, Threshold = 60;
        var mask = new bool[c.LegacyW * c.LegacyH];
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        for (var y = y0 + Inset; y <= y1 - Inset; y++)
        for (var x = x0 + Inset; x <= x1 - Inset; x++)
        {
            if (!c.LegacyOpaque(x, y)) continue;
            var j = (y * c.LegacyW + x) * 4;
            rs.Add(c.Legacy[j]); gs.Add(c.Legacy[j + 1]); bs.Add(c.Legacy[j + 2]);
        }
        if (rs.Count == 0) return mask;
        rs.Sort(); gs.Sort(); bs.Sort();
        int mr = rs[rs.Count / 2], mg = gs[gs.Count / 2], mb = bs[bs.Count / 2];
        var core = new bool[mask.Length];
        for (var y = y0 + Inset; y <= y1 - Inset; y++)
        for (var x = x0 + Inset; x <= x1 - Inset; x++)
        {
            if (!c.LegacyOpaque(x, y)) continue;
            var j = (y * c.LegacyW + x) * 4;
            core[y * c.LegacyW + x] = Math.Abs(c.Legacy[j] - mr) + Math.Abs(c.Legacy[j + 1] - mg) + Math.Abs(c.Legacy[j + 2] - mb) > Threshold;
        }
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var hit = false;
            for (var oy = -1; oy <= 1 && !hit; oy++)
            for (var ox = -1; ox <= 1 && !hit; ox++)
            {
                int nx = x + ox, ny = y + oy;
                hit = nx >= 0 && ny >= 0 && nx < c.LegacyW && ny < c.LegacyH && core[ny * c.LegacyW + nx];
            }
            mask[y * c.LegacyW + x] = hit;
        }
        return mask;
    }

    // Deterministic value noise (our own hash), three octaves, in [0, 1).
    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (var octave = 0; octave < 3; octave++)
        {
            sum += amplitude * ValueNoise(x, y, seed + octave * 101);
            total += amplitude; x *= 2.03f; y *= 2.03f; amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy, seed); var b = Hash(ix + 1, iy, seed); var cc = Hash(ix, iy + 1, seed); var d = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (cc - a) * fy + (a - b - cc + d) * fx * fy;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
