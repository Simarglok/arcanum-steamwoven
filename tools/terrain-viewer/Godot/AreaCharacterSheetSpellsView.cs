using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-077: the spells page of the character sheet in the original's look (<see cref="AreaCharacterSheetSpellsLayout"/>, ledger CHW17–CHW22):
/// a control of 235 × 325 at the sheet's (503, 63), drawn in the page's own coordinates. Art 31 with sixteen college tabs (the shown college's
/// tab pressed), then the college's five spells, each a 32 × 32 icon (the next spell and the ones above it greyed), the name in Morph15 and the
/// minimum level in Pork12, a + over the next spell's icon and a − over the highest known one. The pointer over a tab or a spell writes its
/// hint to the message window. A button takes the pointer on its rectangle (PROJECT); a press acts when released over it.
/// </summary>
public partial class AreaCharacterSheetSpellsView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private readonly Dictionary<int, Texture2D> _grey = [];
    private AreaCharacterSheetFonts? _fonts;
    private AreaCharacterSheetTexts _texts = AreaCharacterSheetTexts.Project("not set");
    private AreaSpellsPageView? _spells;
    private AreaCharacterSheetSpellsLayout.Hit? _hovered, _pressed;

    /// <summary>A college tab released over itself (0..15).</summary>
    public event Action<int>? CollegeSelected;
    /// <summary>The + (true) or − of the shown college, released over itself.</summary>
    public event Action<int, bool>? SpellPressed;
    /// <summary>The pointer is over a part (its entry), or left (null).</summary>
    public event Action<AreaMessageEntry?>? HoverEntryRequested;
    public event Action<int>? SoundRequested;
    /// <summary>The text bar entry of a spell 0..79 (the app builds it with the game bar's texts and the spell data); null: the page's own name line.</summary>
    public Func<int, AreaMessageEntry?>? SpellEntry { get; set; }
    /// <summary>ARC-UI-088: the NPC sheet of a dialog (mode 2) shows no + or −, and the next spell grey.</summary>
    public bool ReadOnly { get; set; }

    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);
    internal AreaCharacterSheetSpellsLayout.Hit? HoveredForSelfTest => _hovered;
    internal AreaSpellsPageView? ViewForSelfTest => _spells;

    public override void _Ready()
    {
        Name = "SheetSpells";
        var (x, y, width, height) = AreaCharacterSheetArtLayout.Panel;
        Position = new Vector2(x, y);
        Size = new Vector2(width, height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts, AreaCharacterSheetTexts texts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _grey.Clear();
    }

    public void ShowPage()
    {
        (_hovered, _pressed) = (null, null);
        Visible = true;
        QueueRedraw();
    }

    public void Render(AreaSpellsPageView? spells)
    {
        _spells = spells;
        QueueRedraw();
        if (_hovered is null) return;
        // A press moves the + and − (the next and the last icon slot): the part under the pointer is found again.
        _hovered = HitAt(GetLocalMousePosition());
        HoverEntryRequested?.Invoke(_hovered is { } hit ? EntryOf(hit) : null);
    }

    public void CloseView()
    {
        if (_hovered is not null) HoverEntryRequested?.Invoke(null);
        (_hovered, _pressed, _spells) = (null, null, null);
        Visible = false;
    }

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private int Frames(int art) => _art.TryGetValue(art, out var frames) ? frames.Length : 1;

    private int Frame(int art, AreaCharacterSheetSpellsLayout.Hit hit, bool selected = false) => selected ? AreaGameBarLayout.Frame(AreaBarButtonLook.Down, Math.Max(1, Frames(art)))
        : AreaCharacterArtDraw.Frame(Frames(art), _pressed is not null, _pressed == hit, _hovered == hit);

    private AreaCharacterSheetSpellsLayout.Hit? HitAt(Vector2 at) => _spells is null ? null : AreaCharacterSheetSpellsLayout.HitAt(at.X, at.Y, _spells,
        art => Art(art) is { } texture ? (texture.GetWidth(), texture.GetHeight()) : null, ReadOnly);

    /// <summary>The text bar entry of a hit: a college tab's, or the spell's (a row's, the + one's next, the − one's highest).</summary>
    private AreaMessageEntry? EntryOf(AreaCharacterSheetSpellsLayout.Hit hit)
    {
        if (_spells is null) return null;
        if (hit.Part == AreaCharacterSheetSpellsLayout.Part.CollegeTab) return AreaCharacterSheetSpellsLayout.CollegeEntry(_spells.Colleges[hit.Index]);
        var spell = _spells.College * AreaSpellsPage.Ranks + hit.Index;
        return SpellEntry?.Invoke(spell) ?? AreaMessageEntry.Text(AreaMessageKind.Hover, AreaSpellsPage.Hint(_spells.Spells[hit.Index]));
    }

    /// <summary>A copy of an icon with each pixel's grey (0x52C370 sums the three channels' lookups; the weights are not read, so the usual
    /// luminance is used, ledger CHW21 F0).</summary>
    private Texture2D Greyed(int art)
    {
        if (_grey.TryGetValue(art, out var known)) return known;
        var image = Art(art)!.GetImage();
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                var grey = c.R * .299f + c.G * .587f + c.B * .114f;
                image.SetPixel(x, y, new Color(grey, grey, grey, c.A));
            }
        return _grey[art] = ImageTexture.CreateFromImage(image);
    }

    private void DrawArt(Texture2D? texture, (int X, int Y) place)
    {
        if (texture is not null) DrawTextureRect(texture, new Rect2(place.X, place.Y, texture.GetWidth(), texture.GetHeight()), false);
    }

    private void DrawText(AreaArtFontFace? face, string text, (int X, int Y) place, AreaSheetColour colour)
    {
        if (face is null || text.Length == 0) return;
        AreaArtTextDraw.Text(this, face, text, place.X, place.Y, AreaCharacterSheetArtLayout.Rgb(colour));
    }

    public override void _Draw()
    {
        if (_fonts is not { } fonts) return;
        DrawArt(Art(AreaCharacterSheetSpellsLayout.BackgroundArt), (0, 0));
        if (_spells is null) return;
        for (var college = 0; college < AreaCharacterSheetSpellsLayout.Colleges; college++)
        {
            var art = AreaCharacterSheetSpellsLayout.CollegeArt(college);
            DrawArt(Art(art, Frame(art, new(AreaCharacterSheetSpellsLayout.Part.CollegeTab, college), selected: college == _spells.College)),
                AreaCharacterSheetSpellsLayout.CollegeTab(college));
        }
        var rank = _spells.Rank;
        for (var row = 0; row < AreaCharacterSheetSpellsLayout.Rows; row++)
        {
            var art = AreaCharacterSheetSpellsLayout.SpellArt(_spells.College, row);
            if (Art(art) is not null)
                DrawArt(AreaCharacterSheetSpellsLayout.Greyed(row, rank) ? Greyed(art) : Art(art), AreaCharacterSheetSpellsLayout.Icon(row));
            DrawText(fonts.Label, _spells.Spells[row].Name, AreaCharacterSheetSpellsLayout.Name(row), AreaCharacterSheetSpellsLayout.NameColour(row, rank, ReadOnly));
            DrawText(fonts.Pork, AreaCharacterSheetSpellsLayout.LevelText(_texts, _spells.Spells[row].MinimumLevel), AreaCharacterSheetSpellsLayout.Level(row),
                AreaCharacterSheetSpellsLayout.LevelColour(row, rank, ReadOnly));
        }
        if (!ReadOnly && AreaCharacterSheetSpellsLayout.PlusShown(rank))
            DrawArt(Art(AreaCharacterSheetSpellsLayout.PlusArt, Frame(AreaCharacterSheetSpellsLayout.PlusArt, new(AreaCharacterSheetSpellsLayout.Part.Plus, rank))),
                AreaCharacterSheetSpellsLayout.Icon(rank));
        if (!ReadOnly && AreaCharacterSheetSpellsLayout.MinusShown(rank, _spells.FloorRank))
            DrawArt(Art(AreaCharacterSheetSpellsLayout.MinusArt, Frame(AreaCharacterSheetSpellsLayout.MinusArt, new(AreaCharacterSheetSpellsLayout.Part.Minus, rank - 1))),
                AreaCharacterSheetSpellsLayout.Icon(rank - 1));
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

    private void Act(AreaCharacterSheetSpellsLayout.Hit hit)
    {
        if (_spells is null) return;
        switch (hit.Part)
        {
            case AreaCharacterSheetSpellsLayout.Part.CollegeTab: CollegeSelected?.Invoke(hit.Index); break;
            case AreaCharacterSheetSpellsLayout.Part.Plus: SpellPressed?.Invoke(_spells.College, true); break;
            case AreaCharacterSheetSpellsLayout.Part.Minus: SpellPressed?.Invoke(_spells.College, false); break;
        }
    }
}
