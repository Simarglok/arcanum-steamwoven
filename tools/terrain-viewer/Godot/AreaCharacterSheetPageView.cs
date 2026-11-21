using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-076: the skills page and the tech page of the character sheet in the original's look
/// (<see cref="AreaCharacterSheetPagesLayout"/>, ledger CHW10–CHW16): a control of 235 × 325 at the sheet's (503, 63), drawn in the page's own
/// coordinates. The skills page is art 29 with four group tabs (the shown group is kept between opens), four rows of a skill's name and
/// training, a level gauge of art 623, and a − and a + of arts 625 and 624; the tech page is art 30 with eight discipline buttons (the shown
/// discipline is kept), the header with the degree's name, the expertise, seven degree rows with their learned-degree icons and a + and a −
/// in the next and the last icon slot. The pointer over a part writes its text to the message window (a skill's as the type-7 hint). A
/// button takes the pointer on its rectangle (PROJECT).
/// </summary>
public partial class AreaCharacterSheetPageView : Control
{
    private enum Page { Skills, Tech }
    private readonly record struct Hit(AreaPageHit Kind, int A);

    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaCharacterSheetFonts? _fonts;
    private AreaCharacterSheetTexts _texts = AreaCharacterSheetTexts.Project("not set");
    private AreaSkillsPageView? _skills;
    private AreaTechPageView? _tech;
    private Page _page = Page.Skills;
    private int _group, _discipline;
    private Hit? _hovered, _pressed;

    /// <summary>A skill's + (true) or − (false) released over itself: the page row 0..15.</summary>
    public event Action<int, bool>? SkillPressed;
    /// <summary>A discipline's + or −.</summary>
    public event Action<int, bool>? TechPressed;
    /// <summary>The pointer is over a part (its text), or left (null).</summary>
    public event Action<AreaMessageEntry?>? HoverEntryRequested;
    public event Action<int>? SoundRequested;

    /// <summary>ARC-UI-088: the NPC sheet of a dialog (mode 2) shows no skill, degree + or −.</summary>
    public bool ReadOnly { get; set; }
    public int Group => _group;
    public int Discipline => _discipline;
    internal AreaSheetTab ShownForSelfTest => _page == Page.Skills ? AreaSheetTab.Skills : AreaSheetTab.Tech;
    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    // ARC-UI-3035: the kit's sheet behind this page (the page draws its own panel art only while the kit has none).
    private readonly AreaKitSheetView _kitSheet = new();
    internal bool KitSheetShownForSelfTest => _kitSheet.Shown;

    public override void _EnterTree() => AreaKitSheets.Changed += QueueRedraw;

    public override void _ExitTree() => AreaKitSheets.Changed -= QueueRedraw;

    public override void _Ready()
    {
        Name = "SheetPages";
        var (x, y, width, height) = AreaCharacterSheetArtLayout.Panel;
        Position = new Vector2(x, y);
        Size = new Vector2(width, height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
        AddChild(_kitSheet);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts, AreaCharacterSheetTexts texts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    /// <summary>Shows the skills (true) or the tech page.</summary>
    public void ShowPage(bool skills)
    {
        _page = skills ? Page.Skills : Page.Tech;
        (_hovered, _pressed) = (null, null);
        Visible = true;
        QueueRedraw();
    }

    public void Render(AreaSkillsPageView? skills, AreaTechPageView? tech)
    {
        (_skills, _tech) = (skills, tech);
        QueueRedraw();
        if (_hovered is null) return;
        // A press can move the + and − (the next and the last icon slot): the part under the pointer is found again.
        _hovered = HitAt(GetLocalMousePosition());
        HoverEntryRequested?.Invoke(_hovered is { } hit ? EntryOf(hit) : null);
    }

    public void CloseView()
    {
        if (_hovered is not null) HoverEntryRequested?.Invoke(null);
        (_hovered, _pressed, _skills, _tech) = (null, null, null, null);
        Visible = false;
    }

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private int Frames(int art) => _art.TryGetValue(art, out var frames) ? frames.Length : 1;

    private int Frame(int art, Hit hit, bool selected = false) => selected ? AreaGameBarLayout.Frame(AreaBarButtonLook.Down, Math.Max(1, Frames(art)))
        : AreaCharacterArtDraw.Frame(Frames(art), _pressed is not null, _pressed == hit, _hovered == hit);

    private int Skill(int row) => _group * AreaCharacterSheetPagesLayout.GroupRows + row;

    private int Degree => _tech?.Rows[_discipline].Degree ?? 0;

    /// <summary>The buttons first, the plus and minus of a row (created before the tabs and regions in the exe, so they win the hit test),
    /// then the tabs, then the regions.</summary>
    private Hit? HitAt(Vector2 at)
    {
        bool In(int art, (int X, int Y) place) => Art(art) is { } texture
            && AreaCharacterSheetPagesLayout.Inside((place.X, place.Y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y);
        if (_page == Page.Skills)
        {
            if (_skills is null) return null;
            for (var row = 0; row < AreaCharacterSheetPagesLayout.GroupRows && !ReadOnly; row++)
            {
                if (In(AreaCharacterSheetPagesLayout.SkillPlusArt, AreaCharacterSheetPagesLayout.SkillPlus(row))) return new(AreaPageHit.SkillPlus, row);
                if (In(AreaCharacterSheetPagesLayout.SkillMinusArt, AreaCharacterSheetPagesLayout.SkillMinus(row))) return new(AreaPageHit.SkillMinus, row);
            }
            for (var group = 0; group < AreaCharacterSheetPagesLayout.Groups; group++)
                if (In(AreaCharacterSheetPagesLayout.GroupTabArtBase + group, AreaCharacterSheetPagesLayout.GroupTab(group))) return new(AreaPageHit.GroupTab, group);
            for (var row = 0; row < AreaCharacterSheetPagesLayout.GroupRows; row++)
                if (AreaCharacterSheetPagesLayout.Inside(AreaCharacterSheetPagesLayout.SkillRegion(row), at.X, at.Y)) return new(AreaPageHit.SkillRow, row);
            return null;
        }
        if (_tech is null) return null;
        var degree = Degree;
        if (!ReadOnly && AreaCharacterSheetPagesLayout.PlusShown(degree) && In(AreaCharacterSheetPagesLayout.TechPlusArt, AreaCharacterSheetPagesLayout.DegreeSlot(degree)))
            return new(AreaPageHit.TechPlus, degree);
        if (!ReadOnly && AreaCharacterSheetPagesLayout.MinusShown(degree, _tech.Rows[_discipline].FloorDegree) && In(AreaCharacterSheetPagesLayout.TechMinusArt,
            AreaCharacterSheetPagesLayout.DegreeSlot(degree - 1))) return new(AreaPageHit.TechMinus, degree);
        for (var discipline = 0; discipline < AreaCharacterSheetPagesLayout.Disciplines; discipline++)
            if (In(AreaCharacterSheetPagesLayout.DisciplineArtBase + discipline, AreaCharacterSheetPagesLayout.DisciplineTab(discipline))) return new(AreaPageHit.DisciplineTab, discipline);
        if (AreaCharacterSheetPagesLayout.Inside(AreaCharacterSheetPagesLayout.HeaderRegion, at.X, at.Y)) return new(AreaPageHit.TechHeader, 0);
        for (var k = 1; k <= AreaCharacterSheetPagesLayout.Degrees; k++)
            if (AreaCharacterSheetPagesLayout.Inside(AreaCharacterSheetPagesLayout.DegreeRegion(k), at.X, at.Y)) return new(AreaPageHit.TechDegree, k);
        return null;
    }

    /// <summary>The text bar entry of a hit (null: nothing to show).</summary>
    private AreaMessageEntry? EntryOf(Hit hit)
    {
        AreaMessageEntry? Text(string? text) => string.IsNullOrEmpty(text) ? null : AreaMessageEntry.Text(AreaMessageKind.Hover, text);
        switch (hit.Kind)
        {
            case AreaPageHit.GroupTab: return Text(_texts.Hint(AreaCharacterSheetPagesLayout.GroupHint(hit.A)));
            case AreaPageHit.SkillRow or AreaPageHit.SkillPlus or AreaPageHit.SkillMinus:
                return _skills is { } skills ? AreaSkillsHover.Hint(skills.Rows[Skill(hit.A)], _texts) : null;
            case AreaPageHit.DisciplineTab: return Text(AreaTechPage.DisciplineHint(_texts, hit.A));
            case AreaPageHit.TechHeader: return Text(_texts.Hint(AreaCharacterSheetPagesLayout.HeaderHint));
            case AreaPageHit.TechDegree: return Text(AreaTechPage.DegreeHint(_texts, _discipline, hit.A));
            case AreaPageHit.TechPlus: return Text(AreaTechPage.DegreeHint(_texts, _discipline, hit.A + 1));
            case AreaPageHit.TechMinus: return Text(AreaTechPage.DegreeHint(_texts, _discipline, hit.A));
            default: return null;
        }
    }

    public override void _Draw()
    {
        if (_fonts is not { } fonts) return;
        // ARC-UI-3035: the kit's sheet for this page, if any, stands behind and the page's own panel art is not drawn.
        _kitSheet.Set(_page == Page.Skills ? AreaCharacterSheetPagesLayout.SkillsBackgroundArt : AreaCharacterSheetPagesLayout.TechBackgroundArt, Size);
        if (_page == Page.Skills) DrawSkills(fonts); else DrawTech(fonts);
    }

    private void DrawArt(int art, (int X, int Y) place, int frame = 0)
    {
        if (Art(art, frame) is { } texture) DrawTextureRect(texture, new Rect2(place.X, place.Y, texture.GetWidth(), texture.GetHeight()), false);
    }

    private void DrawText(AreaArtFontFace? face, string text, int x, int y, AreaSheetColour colour)
    {
        if (face is null || text.Length == 0) return;
        AreaArtTextDraw.Text(this, face, text, x, y, AreaCharacterSheetArtLayout.Rgb(colour));
    }

    private void DrawSkills(AreaCharacterSheetFonts fonts)
    {
        if (!_kitSheet.Shown) DrawArt(AreaCharacterSheetPagesLayout.SkillsBackgroundArt, (0, 0));
        if (_skills is null) return;
        for (var group = 0; group < AreaCharacterSheetPagesLayout.Groups; group++)
        {
            var art = AreaCharacterSheetPagesLayout.GroupTabArtBase + group;
            DrawArt(art, AreaCharacterSheetPagesLayout.GroupTab(group), Frame(art, new(AreaPageHit.GroupTab, group), selected: group == _group));
        }
        for (var row = 0; row < AreaCharacterSheetPagesLayout.GroupRows; row++)
        {
            var skill = _skills.Rows[Skill(row)];
            var y = AreaCharacterSheetPagesLayout.RowY(row);
            DrawText(fonts.Label, skill.Name, 17, y, AreaSheetColour.White);
            DrawText(fonts.Label, AreaCharacterSheetPagesLayout.TrainingText(skill.TrainingName), 17 + fonts.Label.Font.Measure(skill.Name).Width, y, AreaSheetColour.White);
            var width = AreaCharacterSheetPagesLayout.GaugeVisible(skill.Level);
            if (width > 0 && Art(AreaCharacterSheetPagesLayout.GaugeArt) is { } gauge)
            {
                var (gx, gy) = AreaCharacterSheetPagesLayout.Gauge(row);
                DrawTextureRectRegion(gauge, new Rect2(gx, gy, width, AreaCharacterSheetPagesLayout.GaugeHeight), new Rect2(0, 0, width, AreaCharacterSheetPagesLayout.GaugeHeight));
            }
            if (ReadOnly) continue;
            DrawArt(AreaCharacterSheetPagesLayout.SkillMinusArt, AreaCharacterSheetPagesLayout.SkillMinus(row),
                Frame(AreaCharacterSheetPagesLayout.SkillMinusArt, new(AreaPageHit.SkillMinus, row)));
            DrawArt(AreaCharacterSheetPagesLayout.SkillPlusArt, AreaCharacterSheetPagesLayout.SkillPlus(row),
                Frame(AreaCharacterSheetPagesLayout.SkillPlusArt, new(AreaPageHit.SkillPlus, row)));
        }
    }

    private void DrawTech(AreaCharacterSheetFonts fonts)
    {
        if (!_kitSheet.Shown) DrawArt(AreaCharacterSheetPagesLayout.TechBackgroundArt, (0, 0));
        if (_tech is null) return;
        for (var discipline = 0; discipline < AreaCharacterSheetPagesLayout.Disciplines; discipline++)
        {
            var art = AreaCharacterSheetPagesLayout.DisciplineArtBase + discipline;
            DrawArt(art, AreaCharacterSheetPagesLayout.DisciplineTab(discipline), Frame(art, new(AreaPageHit.DisciplineTab, discipline), selected: discipline == _discipline));
        }
        var row = _tech.Rows[_discipline];
        var degree = row.Degree;
        var (ix, iy) = AreaCharacterSheetPagesLayout.HeaderIcon;
        DrawText(fonts.Icons, AreaCharacterSheetPagesLayout.HeaderGlyph(_discipline).ToString(), ix, iy, AreaSheetColour.White);
        var header = $"{_texts.TechDisciplineName(_discipline)} {_texts.TechDegreeName(degree)}";
        var (cx, hy) = AreaCharacterSheetPagesLayout.HeaderText;
        DrawText(fonts.Nick, header, cx - (fonts.Nick?.Font.Measure(header).Width ?? 0) / 2, hy, AreaSheetColour.White);
        var (ex, ey) = AreaCharacterSheetPagesLayout.Expertise;
        DrawText(fonts.Flare, row.Expertise.ToString(), ex, ey, AreaSheetColour.Blue);
        for (var i = 0; i < AreaCharacterSheetPagesLayout.Degrees; i++)
        {
            var colour = AreaCharacterSheetPagesLayout.DegreeColour(i, degree);
            var (nx, ny) = AreaCharacterSheetPagesLayout.DegreeName(i);
            // The row's schematic (0x56EA10); a degree without one keeps the degree's own name (PROJECT). The components line is not drawn yet.
            DrawText(fonts.Nick, _texts.SchematicName(_discipline, i + 1) ?? _texts.TechDegreeName(i + 1), nx, ny, colour);
            if (i < degree) DrawArt(AreaCharacterSheetPagesLayout.DegreeIcon(_discipline), AreaCharacterSheetPagesLayout.DegreeSlot(i));
        }
        if (!ReadOnly && AreaCharacterSheetPagesLayout.PlusShown(degree))
            DrawArt(AreaCharacterSheetPagesLayout.TechPlusArt, AreaCharacterSheetPagesLayout.DegreeSlot(degree),
                Frame(AreaCharacterSheetPagesLayout.TechPlusArt, new(AreaPageHit.TechPlus, degree)));
        if (!ReadOnly && AreaCharacterSheetPagesLayout.MinusShown(degree, row.FloorDegree))
            DrawArt(AreaCharacterSheetPagesLayout.TechMinusArt, AreaCharacterSheetPagesLayout.DegreeSlot(degree - 1),
                Frame(AreaCharacterSheetPagesLayout.TechMinusArt, new(AreaPageHit.TechMinus, degree)));
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        HoverEntryRequested?.Invoke(null);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = HitAt(motion.Position);
                // A hover entry is shown only when nothing was stored in the last 3 s (ML2): written again on each move over a part.
                if (over is { } hit && EntryOf(hit) is { } entry) HoverEntryRequested?.Invoke(entry);
                else if (_hovered is not null) HoverEntryRequested?.Invoke(null);
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = HitAt(button.Position);
                _hovered = at;
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                }
                else
                {
                    var held = _pressed;
                    _pressed = null;
                    if (held is { } pressed && at == pressed)
                    {
                        SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                        Act(pressed);
                    }
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    private void Act(Hit hit)
    {
        switch (hit.Kind)
        {
            case AreaPageHit.GroupTab: _group = hit.A; break;
            case AreaPageHit.DisciplineTab: _discipline = hit.A; break;
            case AreaPageHit.SkillPlus: SkillPressed?.Invoke(Skill(hit.A), true); break;
            case AreaPageHit.SkillMinus: SkillPressed?.Invoke(Skill(hit.A), false); break;
            case AreaPageHit.TechPlus: TechPressed?.Invoke(_discipline, true); break;
            case AreaPageHit.TechMinus: TechPressed?.Invoke(_discipline, false); break;
        }
    }
}
