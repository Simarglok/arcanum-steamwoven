using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>One button of the spell pane: its college or spell, its art's frames (null when the art did not read) and whether it is the
/// selected college (drawn down).</summary>
public sealed record AreaSpellBarButton(int Number, Texture2D[]? Frames, bool Selected = false);

/// <summary>What the spell pane shows: its background art, the college buttons that show and the selected college's spell buttons.</summary>
public sealed record AreaSpellBarLook(Texture2D? Background, IReadOnlyList<AreaSpellBarButton> Colleges, IReadOnlyList<AreaSpellBarButton> Spells);

/// <summary>
/// ARC-UI-053: the original's spell pane (bar mode 1) drawn in the bottom strip's message window of <see cref="AreaGameBarPanel"/>, in the
/// strip's frame (ledger SPN1–SPN6): the mode's background art, a button per college the hero has a rank in at its place on the upper row,
/// and the selected college's known spells as icon buttons on the lower row. The buttons are the bar's push buttons (GB3, GB4): down
/// while held over them, lit under the pointer; the selected college shows down. It raises what the pointer does on them; the app decides
/// (a college is selected on its press, a spell activated on the release over it, a right press on a spell starts a hotkey drag).
/// </summary>
public partial class AreaSpellBarView : Control
{
    private TextureRect _background = null!;
    private readonly TextureRect[] _colleges = new TextureRect[SpellPaneRules.Colleges];
    private readonly TextureRect[] _spells = new TextureRect[SpellPaneRules.Ranks];
    private AreaSpellBarLook? _look;
    // The button held down with the left button (true a college, its number) and the one under the pointer.
    private (bool College, int Number)? _held, _hovered;

    /// <summary>A left press on a college's button (its number).</summary>
    public event Action<int>? CollegePressed;
    /// <summary>The left release after a college's press (and whether over its button).</summary>
    public event Action<int, bool>? CollegeReleased;
    /// <summary>A left press on a spell's button and its release (and whether over the button).</summary>
    public event Action<int>? SpellPressed;
    public event Action<int, bool>? SpellReleased;
    /// <summary>A right press on a spell's button: the original starts a drag onto the hotkey bar (0x57E5D0).</summary>
    public event Action<int>? SpellRightPressed;
    /// <summary>The pointer entered a college's or a spell's button (true a college).</summary>
    public event Action<bool, int>? Entered;

    internal Vector2? CollegeCentreForSelfTest(int college) => _colleges[college].Visible ? _colleges[college].GetGlobalRect().GetCenter() : null;
    internal Vector2? SpellCentreForSelfTest(int spell) =>
        _look?.Spells.Any(button => button.Number == spell) == true && spell % SpellPaneRules.Ranks is var rank && _spells[rank].Visible
            ? _spells[rank].GetGlobalRect().GetCenter() : null;
    internal Texture2D? CollegeArtForSelfTest(int college) => _colleges[college].Visible ? _colleges[college].Texture : null;
    internal Texture2D? SpellArtForSelfTest(int rank) => _spells[rank].Visible ? _spells[rank].Texture : null;
    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal IReadOnlyList<int> ShownCollegesForSelfTest => _look?.Colleges.Select(button => button.Number).ToArray() ?? [];
    internal IReadOnlyList<int> ShownSpellsForSelfTest => _look?.Spells.Select(button => button.Number).ToArray() ?? [];

    public override void _Ready()
    {
        Name = "SpellPane";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        _background = new TextureRect { Name = "Background", Position = Local(SpellPaneRules.BackgroundX, SpellPaneRules.BackgroundY), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_background);
        for (var c = 0; c < SpellPaneRules.Colleges; c++)
            _colleges[c] = Button("College" + c, Local(SpellPaneRules.CollegeButtonX[c], SpellPaneRules.CollegeButtonY), SpellPaneRules.CollegeButtonSize, college: true, c);
        for (var r = 0; r < SpellPaneRules.Ranks; r++)
            _spells[r] = Button("Spell" + r, Local(SpellPaneRules.SpellButtonX[r], SpellPaneRules.SpellButtonY), SpellPaneRules.SpellButtonSize, college: false, r);
        Render();
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private TextureRect Button(string name, Vector2 position, int size, bool college, int place)
    {
        var rect = new TextureRect { Name = name, Position = position, Size = new Vector2(size, size), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        // A spell button stands for the spell its place shows now; a college button always for its college.
        int Number() => college ? place : _look?.Spells.ElementAtOrDefault(place)?.Number ?? -1;
        rect.GuiInput += input => ButtonInput(rect, input, college, Number());
        rect.MouseEntered += () => { var number = Number(); if (number < 0) return; _hovered = (college, number); Render(); Entered?.Invoke(college, number); };
        rect.MouseExited += () => { if (_hovered is { } hovered && hovered.College == college && hovered.Number == Number()) _hovered = null; Render(); };
        AddChild(rect);
        return rect;
    }

    private void ButtonInput(Control button, InputEvent input, bool college, int number)
    {
        if (number < 0 || input is not InputEventMouseButton click) return;
        if (click.ButtonIndex == MouseButton.Right)
        {
            if (!click.Pressed || college) return;
            button.AcceptEvent();
            SpellRightPressed?.Invoke(number);
            return;
        }
        if (click.ButtonIndex != MouseButton.Left) return;
        button.AcceptEvent();
        if (click.Pressed)
        {
            _held = (college, number);
            Render();
            if (college) CollegePressed?.Invoke(number); else SpellPressed?.Invoke(number);
            return;
        }
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        var over = _held == (college, number) && new Rect2(Vector2.Zero, button.Size).HasPoint(click.Position);
        _held = null;
        Render();
        if (college) CollegeReleased?.Invoke(number, over); else SpellReleased?.Invoke(number, over);
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaSpellBarLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (_held is { } held && !Shows(held)) _held = null;
        if (_hovered is { } hovered && !Shows(hovered)) _hovered = null;
        if (IsNodeReady()) Render();
    }

    private bool Shows((bool College, int Number) button) =>
        (button.College ? _look?.Colleges : _look?.Spells)?.Any(shown => shown.Number == button.Number) == true;

    private void Render()
    {
        var look = _look;
        _background.Texture = look?.Background;
        _background.Visible = look?.Background is not null;
        for (var c = 0; c < _colleges.Length; c++)
            DrawButton(_colleges[c], look?.Colleges.FirstOrDefault(button => button.Number == c), college: true);
        for (var r = 0; r < _spells.Length; r++)
            DrawButton(_spells[r], look?.Spells.ElementAtOrDefault(r), college: false);
    }

    // A button's frame (GB4): down while held with the pointer over it, or the selected college; lit (two steps) under the pointer; else
    // idle. The college arts have three frames (idle, down, lit), the spell icons two (so under the pointer they show frame 0). A button
    // whose art did not read is not shown (no invisible area takes the pointer).
    private void DrawButton(TextureRect rect, AreaSpellBarButton? button, bool college)
    {
        rect.Visible = button is { Frames.Length: > 0 };
        if (button is not { Frames.Length: > 0 }) return;
        var held = _held == (college, button.Number);
        var hovered = _hovered == (college, button.Number);
        var look = (held && hovered) || button.Selected ? AreaBarButtonLook.Down : hovered && !held ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        rect.Texture = button.Frames[AreaGameBarLayout.Frame(look, button.Frames.Length)];
    }
}
