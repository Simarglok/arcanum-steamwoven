using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-011 main menu: a backdrop over the whole window and a column of the shown buttons of <see cref="AreaMainMenu"/>'s window
/// with their original labels (each with its hotkey letter), the hidden buttons' note and the notice; the pre-made browser shows the
/// browsed hero with Previous, Next, Start and Back. The app owns the state and rebuilds the window after every change; this panel
/// does not draw the original's menu art and layout (ARC-UI-058 below). ARC-UI-012: the in-play windows, and the quit box as its question with Yes and No (ARC-DEV-334: with the game's art the original's
/// dialog box, <see cref="AreaDialogBoxView"/>, DB1–DB4, over the in-play window it was asked in; the main menu's Exit Game asks nothing, as in the original, MM2/MM6).
/// ARC-UI-013: the options window: the page's shown controls, each with its label, value and arrows, above the page and Done buttons.
/// ARC-UI-030: the new-character window: gender, race and background rows with their arrows (the original's art buttons), the
/// background's name and text from the game, Accept and Cancel. ARC-UI-034: the character editor (window 14) is the creation-mode
/// character sheet the app shows above this backdrop; the menu's own panel is hidden there. ARC-UI-036: the opening shop (window 15):
/// the kit the shop made (the host's text: gold and items), Accept and Back. ARC-UI-058: with the game's menu art read, the button windows
/// (main, single player, new game, in-play) are drawn in the original's look (<see cref="AreaMenuArtView"/>) with a project line at the
/// screen's bottom for the notice and the buttons not offered; the other windows keep this panel. ARC-UI-059: the
/// options window too, its page buttons in the art; ARC-UI-061: and its page's controls in their own art at their slots (MW8–MW10).
/// ARC-UI-066: the pre-made browser in its own art (<see cref="AreaPremadeBrowserArtView"/>, MW17–MW21, MW26) when the browsed hero read.
/// ARC-UI-068: the new-character window in its own art (<see cref="AreaNewCharacterArtView"/>, MW27–MW29) when its character can be drawn.
/// ARC-UI-069: the opening shop in its own art (<see cref="AreaShopArtView"/>, MW30) while its barter window shows the original look.
/// </summary>
public partial class AreaMainMenuPanel : CanvasLayer
{
    private ColorRect _backdrop = null!;
    private SteamwovenColumnSidesView _sides = null!;
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private VBoxContainer _buttons = null!;
    private VBoxContainer _controls = null!;
    private VBoxContainer _browser = null!;
    private VBoxContainer _creation = null!;
    private Label _creationText = null!;
    private VBoxContainer _shop = null!;
    private Label _shopText = null!;
    private Label _hero = null!;
    private Label _note = null!;
    private Label _notice = null!;
    private Label _hint = null!;
    private AreaMenuArtView _art = null!;
    private AreaPremadeBrowserArtView _premadeArt = null!;
    private AreaNewCharacterArtView _newCharacterArt = null!;
    private AreaShopArtView _shopArt = null!;
    private static readonly Color DiagnosticBackdrop = new(.06f, .05f, .04f, .96f);
    // ARC-UI-2968: the note over the art menus (name-typing and creation refusals) in the original's font 229, on the plate's scale.
    private Control _noteScreen = null!;
    private AreaArtParagraph _artNote = null!;
    private Control _modal = null!, _dialogScreen = null!;
    private AreaDialogBoxView _dialog = null!;
    public event Action<int>? ButtonPressed;
    public event Action<int>? BrowseRequested;
    public event Action? StartRequested;
    public event Action? BackRequested;
    public event Action<bool>? AnswerRequested;
    /// <summary>ARC-UI-013: an arrow of the options window's control (index among the shown ones, direction -1 or 1).</summary>
    public event Action<int, int>? OptionChangeRequested;
    /// <summary>ARC-UI-018: a window button's interface sound: the pointer entering it (3026) or a press on it (3027), as the
    /// original's text-only menu buttons sound (ledger GB7).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-UI-030: a race, gender or background arrow of the new-character window.</summary>
    public event Action<AreaNewCharacterButton>? CreationPressed;
    /// <summary>ARC-UI-030: the new-character window's Accept.</summary>
    public event Action? AcceptRequested;
    /// <summary>ARC-UI-033: a click on the new-character window's name field.</summary>
    public event Action? NameClicked;
    public bool IsOpen => Visible;
    /// <summary>ARC-DEV-334: the quit question is drawn as the original's dialog box (DB1–DB4) over the menu's screen.</summary>
    public bool QuitInArt => Visible && _dialog.Visible;
    internal AreaDialogBoxView DialogForSelfTest => _dialog;
    /// <summary>ARC-DEV-334: the window position of the quit box's Yes (true) or No button's centre, or null.</summary>
    internal Vector2? QuitButtonCentreForSelfTest(bool yes) => QuitInArt ? _dialog.ButtonCentreForSelfTest(yes) : null;
    internal Vector2? ButtonCentreForSelfTest(int index) => ArtMode ? _art.ButtonCentreForSelfTest(index) :
        _buttons.Visible && _buttons.GetChildren().OfType<Button>().ElementAtOrDefault(index) is { } button ? button.GetGlobalRect().GetCenter() : null;
    internal IReadOnlyList<string> ButtonsForSelfTest => ArtMode ? _art.LabelsForSelfTest : _buttons.Visible ? _buttons.GetChildren().OfType<Button>().Select(button => button.Text).ToArray() : [];
    internal string HeroForSelfTest => _browser.Visible ? _hero.Text : "";
    internal string NoteForSelfTest => _note.Text;
    internal string NoticeForSelfTest => _notice.Text;
    internal string HeaderForSelfTest => _header.Text;
    /// <summary>ARC-UI-058: the button window is drawn in the original's look.</summary>
    public bool ArtMode => Visible && _art.Visible;
    internal AreaMenuArtView ArtForSelfTest => _art;
    /// <summary>ARC-UI-066: the pre-made browser is drawn in the original's look.</summary>
    public bool PremadeArtMode => Visible && _premadeArt.Visible;
    internal AreaPremadeBrowserArtView PremadeArtForSelfTest => _premadeArt;
    /// <summary>ARC-UI-068: the new-character window is drawn in the original's look.</summary>
    public bool NewCharacterArtMode => Visible && _newCharacterArt.Visible;
    internal AreaNewCharacterArtView NewCharacterArtForSelfTest => _newCharacterArt;
    /// <summary>ARC-UI-069: the opening shop is drawn in the original's look.</summary>
    public bool ShopArtMode => Visible && _shopArt.Visible;
    /// <summary>ARC-UI-069: the opening shop's art is set (its strips and buttons read), so the shop can be drawn in the original's look.</summary>
    public bool ShopArtSet => _shopArt.ArtSet;
    internal AreaShopArtView ShopArtForSelfTest => _shopArt;
    /// <summary>ARC-UI-3041: the kit's body window of the opening shop or the creation editor (the barter window, the character sheet), or null while the screen draws its own art.</summary>
    internal SteamwovenKitWindow? ShopKitWindow => _shopArt.KitBodyWindow;
    /// <summary>ARC-UI-3041: the overlays the shop's barter frame takes from the kit's body, or null.</summary>
    internal AreaKitBackdrop? ShopKitBackdrop => _shopArt.KitBackdrop;
    /// <summary>ARC-UI-3041: the kit's body of the shop or the editor began, stopped or changed variant.</summary>
    internal event Action? ShopKitChanged;
    /// <summary>ARC-UI-3041: the merchant background art of the shop's barter window (235 or 825), which picks the kit piece of its side.</summary>
    internal void SetShopBarterBackground(int art) => _shopArt.SetBarterBackground(art);
    /// <summary>ARC-UI-068: a press in the new-character art not on the name field.</summary>
    public event Action? PressedOutsideName;
    /// <summary>The diagnostic window is shown (ARC-UI-066: the browser's, when its art is not drawn).</summary>
    internal bool PanelVisibleForSelfTest => Visible && _panel.Visible;
    internal string ArtNoteForSelfTest => _artNote.Text;
    internal AreaArtParagraph ArtNoteViewForSelfTest => _artNote;
    /// <summary>ARC-UI-013: the options window's controls as "label: value" (ARC-UI-061: the art's when it draws them).</summary>
    internal IReadOnlyList<string> ControlsForSelfTest => _optionsArt ? _art.ControlsForSelfTest : _controls.Visible
        ? _controls.GetChildren().OfType<HBoxContainer>().Select(row => row.GetChildren().OfType<Label>().ToArray()).Select(labels => $"{labels[0].Text}: {labels[1].Text}").ToArray() : [];
    internal void ChangeForSelfTest(int index, int direction) => OptionChangeRequested?.Invoke(index, direction);
    internal void PressForSelfTest(int index) => ButtonPressed?.Invoke(index);
    /// <summary>ARC-UI-030: the new-character window's rows as "label: value", then the background text.</summary>
    internal IReadOnlyList<string> CreationForSelfTest => _creation.Visible
        ? [.. _creation.GetChildren().OfType<HBoxContainer>().Where(row => row.Name.ToString().StartsWith("Creation", StringComparison.Ordinal))
            .Select(row => row.GetChildren().OfType<Label>().ToArray())
            .Select(labels => $"{labels[0].Text}: {(labels.Length > 1 ? labels[1].Text : NameFieldText)}"), _creationText.Text] : [];
    private string NameFieldText => _creation.GetNodeOrNull<Button>("CreationName/Field")?.Text ?? "";
    internal void NameClickForSelfTest() => NameClicked?.Invoke();
    internal void CreationPressForSelfTest(AreaNewCharacterButton button) => CreationPressed?.Invoke(button);
    internal void AcceptForSelfTest() => AcceptRequested?.Invoke();
    /// <summary>ARC-UI-036: the opening shop's kit text (empty outside the shop).</summary>
    internal string ShopForSelfTest => _shop.Visible ? _shopText.Text : "";
    /// <summary>ARC-UI-036: the opening shop's Accept (true) or Back.</summary>
    internal void ShopPressForSelfTest(bool accept) => (accept ? AcceptRequested : BackRequested)?.Invoke();

    /// <summary>ARC-DEV-050: the run's debug mode, named on the build line.</summary>
    public bool DebugMode { get; init; }

    public override void _Ready()
    {
        Layer = 4;
        _backdrop = new ColorRect { Name = "MainMenuBackdrop", Color = DiagnosticBackdrop, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_backdrop);
        // ARC-UI-2891: the Steamwoven menus fill the window's height with black beside the plate.
        _sides = new SteamwovenColumnSidesView();
        AddChild(_sides);
        _art = new AreaMenuArtView();
        _art.ButtonPressed += index => ButtonPressed?.Invoke(index);
        _art.SoundRequested += id => SoundRequested?.Invoke(id);
        _art.ArrowPressed += (index, direction) => OptionChangeRequested?.Invoke(index, direction);
        AddChild(_art);
        _premadeArt = new AreaPremadeBrowserArtView();
        _premadeArt.ButtonPressed += button =>
        {
            switch (button)
            {
                case AreaPremadeBrowserButton.Start: StartRequested?.Invoke(); break;
                case AreaPremadeBrowserButton.Back: BackRequested?.Invoke(); break;
                case AreaPremadeBrowserButton.Previous: BrowseRequested?.Invoke(-1); break;
                case AreaPremadeBrowserButton.Next: BrowseRequested?.Invoke(1); break;
            }
        };
        _premadeArt.SoundRequested += id => SoundRequested?.Invoke(id);
        AddChild(_premadeArt);
        _newCharacterArt = new AreaNewCharacterArtView();
        _newCharacterArt.ButtonPressed += button =>
        {
            if (AreaNewCharacterArtLayout.Arrow(button) is { } arrow) CreationPressed?.Invoke(arrow);
            else if (button == AreaNewCharacterArtButton.Accept) AcceptRequested?.Invoke();
            else BackRequested?.Invoke();
        };
        _newCharacterArt.NameClicked += () => NameClicked?.Invoke();
        _newCharacterArt.PressedElsewhere += () => PressedOutsideName?.Invoke();
        _newCharacterArt.SoundRequested += id => SoundRequested?.Invoke(id);
        AddChild(_newCharacterArt);
        _shopArt = new AreaShopArtView();
        _shopArt.ButtonPressed += button => (button == AreaShopArtButton.Accept ? AcceptRequested : BackRequested)?.Invoke();
        _shopArt.SoundRequested += id => SoundRequested?.Invoke(id);
        _shopArt.KitBodyChanged += () => ShopKitChanged?.Invoke();
        AddChild(_shopArt);
        _noteScreen = new Control { Name = "MainMenuArtNoteScreen", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(NoteScreenWidth, 600) };
        _artNote = new AreaArtParagraph { Name = "MainMenuArtNote", WrapWidth = NoteScreenWidth - 2 * NoteMargin };
        _noteScreen.AddChild(_artNote);
        AddChild(_noteScreen);
        // ARC-DEV-047: which build this is (ARC-DEV-050: and the debug mode); above the art (ARC-UI-058), which may cover the corner.
        AddChild(ViewerBuild.MakeLabel(DebugMode));
        _panel = new AreaFallbackBox { Name = "AreaMainMenu", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.2f, .12f, .05f), HorizontalAlignment = HorizontalAlignment.Center };
        _buttons = new VBoxContainer { Name = "Buttons" };
        _controls = new VBoxContainer { Name = "OptionControls" };
        _browser = new VBoxContainer { Name = "Browser" };
        _hero = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.1f, .1f, .1f), CustomMinimumSize = new Vector2(320, 120) };
        var browse = new HBoxContainer();
        foreach (var (name, text, action) in new (string, string, Action)[]
        {
            ("Previous", "< Previous", () => BrowseRequested?.Invoke(-1)), ("Next", "Next >", () => BrowseRequested?.Invoke(1)),
            ("Start", "Start", () => StartRequested?.Invoke()), ("Back", "Back", () => BackRequested?.Invoke()),
        })
        {
            var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += action;
            browse.AddChild(button);
        }
        _browser.AddChild(_hero); _browser.AddChild(browse);
        _creation = new VBoxContainer { Name = "NewCharacter" };
        _creationText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.1f, .1f, .1f), CustomMinimumSize = new Vector2(320, 90) };
        _creation.AddChild(_creationText);
        _shop = new VBoxContainer { Name = "Shop" };
        _shopText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.1f, .1f, .1f), CustomMinimumSize = new Vector2(320, 120) };
        _shop.AddChild(_shopText);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.3f, .3f, .3f) };
        _notice = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.45f, .1f, .05f) };
        _hint = new Label { Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        foreach (var control in new Control[] { _header, _controls, _buttons, _browser, _creation, _shop, _notice, _note, _hint }) contents.AddChild(control);
        // ARC-DEV-334: the quit question in the original's dialog box (ARC-WORLD-120) above everything else of the menu; the box is
        // modal, so a full-window catcher under it keeps the clicks off the window it asks over.
        _modal = new Control { Name = "QuitModal", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        AddChild(_modal);
        _dialogScreen = new Control { Name = "QuitScreen", MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight), Visible = false };
        AddChild(_dialogScreen);
        _dialog = new AreaDialogBoxView();
        _dialogScreen.AddChild(_dialog);
        _dialog.AnswerRequested += yes => AnswerRequested?.Invoke(yes);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>ARC-UI-058: the menu art (the button windows' backgrounds by art number and the labels' font).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D> backgrounds, AreaArtFontFace font) => _art.SetArt(backgrounds, font);

    /// <summary>ARC-DEV-334: the original's dialog box art; without it the quit question stays on the diagnostic panel.</summary>
    public void SetDialogArt(AreaDialogBoxArt art) => _dialog.SetArt(art);

    /// <summary>ARC-UI-061: the options controls' art.</summary>
    public void SetControlArt(AreaMenuControlArt art) => _art.SetControlArt(art);

    /// <summary>ARC-UI-066: the pre-made browser's art; ARC-UI-068: the new-character window's from the same read; ARC-UI-069: and the
    /// opening shop's.</summary>
    public void SetPremadeArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaPremadeBrowserFonts fonts, bool browser, bool newCharacter, bool shop)
    {
        if (browser) _premadeArt.SetArt(art, fonts);
        if (newCharacter) _newCharacterArt.SetArt(art, fonts);
        if (shop) _shopArt.SetArt(art);
    }

    /// <summary>The window for the menu's state; <paramref name="hero"/> is the browsed hero's description (browser only), or the opening
    /// shop's kit (ARC-UI-036, shop only). ARC-UI-066: <paramref name="browsed"/> is the browsed hero in the browser's art (null keeps
    /// the diagnostic window) with its large <paramref name="portrait"/>. ARC-UI-068: <paramref name="created"/> is the new character in
    /// its window's art (null keeps the diagnostic window), with the same portrait. ARC-UI-069: <paramref name="shopArt"/>, the opening shop
    /// in its art (its barter window shows the original look docked over it). ARC-UI-147: <paramref name="editorArt"/>, the character editor
    /// (window 14) in the original's look: its strips and Accept and Back buttons are the shop's (same table, CM2, MW30) under the character sheet's
    /// original look, which the editor's own panel draws between them.</summary>
    public void Render(AreaMainMenu menu, string hero, AreaPremadeBrowserView? browsed = null, Texture2D? portrait = null, AreaNewCharacterView? created = null,
        bool shopArt = false, bool editorArt = false)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Show();
        // ARC-DEV-334: with the dialog box's art the quit question is the original's box over the window it was asked in (that window is
        // drawn as without the question); without it the question is the diagnostic panel's, as before (ARC-UI-012).
        var boxed = menu.QuitPending && _dialog.HasArt;
        var quitPanel = menu.QuitPending && !boxed;
        var browser = menu.Window == AreaMenuWindow.PickCharacter;
        var creating = menu.Window == AreaMenuWindow.NewCharacter && menu.Creation is not null && !quitPanel;
        var shopping = menu.Window == AreaMenuWindow.Shop && !quitPanel;
        _header.Text = quitPanel ? menu.Texts.Text(AreaMainMenuTexts.QuitQuestion) : menu.Window switch
        {
            AreaMenuWindow.Main => "Main menu", AreaMenuWindow.InPlay or AreaMenuWindow.InPlayLimited => "Game menu",
            AreaMenuWindow.SinglePlayer => menu.Texts.Text(10), AreaMenuWindow.NewGame => menu.Texts.Text(50),
            AreaMenuWindow.Options => menu.Texts.Text(12) + ": " + menu.Texts.Text(4000 + (int)menu.Page),
            AreaMenuWindow.NewCharacter => menu.Texts.Text(421), AreaMenuWindow.CharacterEditor => "Character editor",
            AreaMenuWindow.Shop => "Opening shop", _ => menu.Texts.Text(420),
        };
        var options = menu.Window == AreaMenuWindow.Options;
        var focus = options ? menu.OptionsFocus - menu.Controls.Count : menu.Focus;
        // ARC-UI-058: a button window in the original's look when its art read; the quit box stays the panel's.
        // ARC-UI-061: the options window's art needs its controls' art too.
        var art = !quitPanel && _art.Draws(menu.Window) && (!options || _art.DrawsControls);
        _optionsArt = art && options;
        if (art) _art.Render(menu.Window, [.. menu.Shown.Select(button => menu.Texts.Text(button.Message))], focus,
            _optionsArt ? ControlViews(menu) : null, _optionsArt ? menu.OptionsFocus : -1);
        else _art.CloseView();
        var premade = browser && !quitPanel && browsed is not null && _premadeArt.ArtSet;
        if (premade) _premadeArt.Render(browsed!, portrait);
        else _premadeArt.CloseView();
        art |= premade;
        var newCharacter = creating && created is not null && _newCharacterArt.ArtSet;
        if (newCharacter) _newCharacterArt.Render(created!, portrait, menu.Focus);
        else _newCharacterArt.CloseView();
        art |= newCharacter;
        var editing = menu.Window == AreaMenuWindow.CharacterEditor && !quitPanel;
        var shopWindow = (shopping && shopArt || editing && editorArt) && _shopArt.ArtSet;
        if (shopWindow) _shopArt.Render(menu.Focus, editing);
        else _shopArt.CloseView();
        art |= shopWindow;
        // ARC-UI-034: the editor window is the creation sheet above the backdrop (the quit box never opens before play).
        _panel.Visible = !art && (menu.Window != AreaMenuWindow.CharacterEditor || quitPanel);
        foreach (var child in _buttons.GetChildren()) { _buttons.RemoveChild(child); child.QueueFree(); }
        RenderControls(menu);
        var shown = menu.Shown;
        // The quit box (ARC-UI-012): the original's yes/no box with its fixed letters; the project's button texts.
        if (quitPanel)
        {
            foreach (var (text, yes) in new[] { ($"Yes  ({AreaMainMenu.YesLetter})", true), ($"No  ({AreaMainMenu.NoLetter})", false) })
            {
                var answer = new Button { Name = yes ? "Yes" : "No", Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34) };
                answer.Pressed += () => AnswerRequested?.Invoke(yes);
                _buttons.AddChild(answer);
            }
            shown = [];
        }
        for (var i = 0; i < shown.Count; i++)
        {
            var index = i;
            var letter = menu.Texts.Hotkey(shown[i]);
            var button = new Button
            {
                Name = "Button" + i, Text = menu.Texts.Text(shown[i].Message) + (letter is { } key ? $"  ({key})" : ""),
                FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34),
                Modulate = i == focus ? new Color(1f, .85f, .6f) : Colors.White,
            };
            button.Pressed += () => ButtonPressed?.Invoke(index);
            button.MouseEntered += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
            button.ButtonDown += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
            _buttons.AddChild(button);
        }
        // ARC-DEV-334: the original's dialog box with the quit question (message 5100), Yes and No (DB1, DB3).
        if (boxed) _dialog.Show(menu.Texts.Text(AreaMainMenuTexts.QuitQuestion), AreaDialogBoxKind.YesNo, AreaDialogBoxLayout.QuitPoint);
        else _dialog.Close();
        _modal.Visible = _dialogScreen.Visible = boxed;
        RenderCreation(menu, creating && !newCharacter);
        RenderShop(menu, shopping && !shopWindow, hero);
        _buttons.Visible = !browser && !creating && !shopping;
        _browser.Visible = browser;
        _hero.Text = browser ? hero : "";
        var hidden = menu.HiddenNotes;
        _note.Text = hidden.Count == 0 ? "" : "Not offered: " + string.Join("; ", hidden);
        _notice.Text = menu.Notice ?? "";
        _noteScreen.Visible = art;
        // ARC-UI-058: outside the original's screen the window is black, as the original's 800 × 600 screen has no margin.
        _backdrop.Color = art ? Colors.Black : DiagnosticBackdrop;
        // ARC-UI-2968: the art menus show the refusal's text only; the developer's "Not offered" list stays in the diagnostic window's note.
        // The character editor and the opening shop show it in the bottom message window as the original does (the refusal posts of 0x550750, ML4 text rectangle),
        // white; the other art menus keep it at the top.
        _noteAtBottom = shopWindow;
        (_artNote.WrapWidth, _artNote.Room) = _noteAtBottom ? (NoteBox.Width, NoteBox.Height) : (NoteScreenWidth - 2 * NoteMargin, 1 << 20);
        _artNote.SetText(art ? menu.Notice ?? "" : "", _noteAtBottom ? NoteBoxColour : NoteColour);
        _hint.Text = (browser ? "Previous/Next browse the pre-made heroes, Start begins a new game with that hero, Escape or Back goes back"
                : shopping ? "the kit the shop made for the character (the original's item_generate_inventory); Accept or Return starts the game "
                    + "with it, Back or Escape returns to the character editor, where every point is still free; item names from the game when loaded"
                : creating ? "the arrows change the gender, race and background (a race or gender change clears the background); Accept opens the character editor "
                    + "for this character, Escape or Cancel goes back; race and gender names are the project's, the background's text the game's"
                : options && !menu.QuitPending ? "Up/Down choose a control, the arrows or Left/Right change its value at once (it is saved as a user preference, not in a save); "
                    + "a page's letter shows it, Done closes the window" + (menu.Root == AreaMenuWindow.Main ? "" : " and the menu, as the O key does")
                    + "; Escape does nothing here, as in the original; options texts "
                    + (menu.OptionsTexts.FromGame ? "from the game" : "are the project's (" + menu.OptionsTexts.Detail + ")")
                : menu.QuitPending ? "Y, Return or Yes quits to the main menu; N, No or Escape keeps playing (saves are not touched)"
                : "a button's letter or a click chooses it" + (menu.BackStack.Count > 0 ? ", Escape goes back"
                    : menu.Root != AreaMenuWindow.Main ? ", Escape continues" : menu.Reopened ? ", Escape exits the game" : ""))
            + " | PROJECT window over the original's menu windows; labels "
            + (menu.Texts.FromGame ? "from the game's MainMenu.mes" : "are the project's (" + menu.Texts.Detail + ")") + "; original art and layout NOT_VERIFIED";
        ResizePanel();
    }

    /// <summary>ARC-UI-030: the new-character window's rows (gender, race, background; ARC-UI-033: portrait, then the name field) with
    /// their arrows, the background's text, Accept and Cancel; the controller's focus row is highlighted.</summary>
    private void RenderCreation(AreaMainMenu? menu, bool creating)
    {
        // The background text stays the window's own child (never orphaned); the rows and buttons are rebuilt around it.
        foreach (var child in _creation.GetChildren()) { if (child == _creationText) continue; _creation.RemoveChild(child); child.QueueFree(); }
        _creation.Visible = creating;
        if (!creating || menu?.Creation is not { } creation) return;
        var text = creation.BackgroundText();
        var rows = new (string Label, string Value, AreaNewCharacterButton Previous, AreaNewCharacterButton Next)[]
        {
            ("Gender", creation.Gender.ToString(), AreaNewCharacterButton.GenderPrevious, AreaNewCharacterButton.GenderNext),
            ("Race", creation.Race.ToString(), AreaNewCharacterButton.RacePrevious, AreaNewCharacterButton.RaceNext),
            ("Background", (text?.Name is { Length: > 0 } title ? title : "?") + $" ({creation.Background})", AreaNewCharacterButton.BackgroundPrevious,
                AreaNewCharacterButton.BackgroundNext),
            // ARC-UI-033: the portrait as its image's name and id (ARC-UI-068: the image is drawn in the window's art).
            ("Portrait", $"{creation.PortraitText() ?? "?"} ({creation.Portrait})", AreaNewCharacterButton.PortraitPrevious, AreaNewCharacterButton.PortraitNext),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var (label, value, previous, next) = rows[i];
            var row = new HBoxContainer { Name = "Creation" + label, Modulate = i == menu.Focus ? new Color(1f, .85f, .6f) : Colors.White };
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 30), Modulate = new Color(.15f, .1f, .05f) });
            var valueLabel = new Label { Text = value, CustomMinimumSize = new Vector2(250, 30), HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(.1f, .1f, .1f), ClipText = true };
            foreach (var (name, arrow, button) in new[] { ("Less", "<", previous), ("More", ">", next) })
            {
                var press = new Button { Name = name, Text = arrow, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(34, 30) };
                press.Pressed += () => CreationPressed?.Invoke(button);
                press.MouseEntered += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
                press.ButtonDown += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
                if (name == "Less") { row.AddChild(press); row.AddChild(valueLabel); }
                else row.AddChild(press);
            }
            _creation.AddChild(row);
        }
        // ARC-UI-033: the name field, a click on it starts the typing (the caret marks it).
        var nameRow = new HBoxContainer { Name = "CreationName", Modulate = rows.Length == menu.Focus ? new Color(1f, .85f, .6f) : Colors.White };
        nameRow.AddChild(new Label { Text = "Name", CustomMinimumSize = new Vector2(120, 30), Modulate = new Color(.15f, .1f, .05f) });
        var field = new Button { Name = "Field", Text = creation.NameText() + (creation.TypingName ? "|" : ""), FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(318, 30), Alignment = HorizontalAlignment.Left, ClipText = true };
        field.Pressed += () => NameClicked?.Invoke();
        field.MouseEntered += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
        nameRow.AddChild(field);
        _creation.AddChild(nameRow);
        _creationText.Text = text is { } shown ? shown.Body : "(the background table did not read)";
        _creation.MoveChild(_creationText, _creation.GetChildCount() - 1);
        var actions = new HBoxContainer { Name = "Actions" };
        foreach (var (name, label, row, action) in new (string, string, int, Action)[]
        {
            ("Accept", "Accept", rows.Length + 1, () => AcceptRequested?.Invoke()), ("Cancel", "Cancel", rows.Length + 2, () => BackRequested?.Invoke()),
        })
        {
            var button = new Button { Name = name, Text = label, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 34),
                Modulate = row == menu.Focus ? new Color(1f, .85f, .6f) : Colors.White };
            button.Pressed += action;
            button.MouseEntered += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
            button.ButtonDown += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
            actions.AddChild(button);
        }
        _creation.AddChild(actions);
    }

    /// <summary>ARC-UI-036: the opening shop's kit text and its Accept (right in the original) and Back buttons; the controller's focus
    /// row (0 Accept, 1 Back) is highlighted.</summary>
    private void RenderShop(AreaMainMenu? menu, bool shopping, string text)
    {
        foreach (var child in _shop.GetChildren()) { if (child == _shopText) continue; _shop.RemoveChild(child); child.QueueFree(); }
        _shop.Visible = shopping;
        _shopText.Text = shopping ? text : "";
        if (!shopping || menu is null) return;
        var actions = new HBoxContainer { Name = "ShopActions" };
        foreach (var (name, row, action) in new (string, int, Action)[] { ("Back", 1, () => BackRequested?.Invoke()), ("Accept", 0, () => AcceptRequested?.Invoke()) })
        {
            var button = new Button { Name = name, Text = name, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 34),
                Modulate = row == menu.Focus ? new Color(1f, .85f, .6f) : Colors.White };
            button.Pressed += action;
            button.MouseEntered += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
            button.ButtonDown += () => SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
            actions.AddChild(button);
        }
        _shop.AddChild(actions);
    }

    /// <summary>ARC-UI-061: the shown controls for the art: label, kind, value of the original's top (a slider's bar) and value text.</summary>
    private static IReadOnlyList<AreaMenuControlView> ControlViews(AreaMainMenu menu) => [.. menu.Controls.Select(control =>
    {
        var value = menu.Options?.Value(control.Option);
        return new AreaMenuControlView(menu.OptionsTexts.Label(control), control.Kind, value ?? 0, Math.Max(1, control.Max),
            value is { } v ? menu.OptionsTexts.Value(control, v) : "?");
    })];

    /// <summary>ARC-UI-013: the options window's controls of the shown page (none in other windows).</summary>
    private void RenderControls(AreaMainMenu menu)
    {
        foreach (var child in _controls.GetChildren()) { _controls.RemoveChild(child); child.QueueFree(); }
        _controls.Visible = menu.Window == AreaMenuWindow.Options && !menu.QuitPending && !_optionsArt;
        if (!_controls.Visible) return;
        var controls = menu.Controls;
        for (var i = 0; i < controls.Count; i++)
        {
            var index = i;
            var control = controls[i];
            var value = menu.Options?.Value(control.Option);
            var row = new HBoxContainer { Name = "Control" + i, Modulate = i == menu.OptionsFocus ? new Color(1f, .85f, .6f) : Colors.White };
            row.AddChild(new Label { Text = menu.OptionsTexts.Label(control), CustomMinimumSize = new Vector2(220, 30), Modulate = new Color(.15f, .1f, .05f) });
            var valueLabel = new Label { Text = value is { } v ? menu.OptionsTexts.Value(control, v) : "?", CustomMinimumSize = new Vector2(150, 30),
                HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(.1f, .1f, .1f) };
            foreach (var (name, text, direction) in new[] { ("Less", "<", -1), ("More", ">", 1) })
            {
                var arrow = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(34, 30) };
                arrow.Pressed += () => OptionChangeRequested?.Invoke(index, direction);
                if (direction < 0) { row.AddChild(arrow); row.AddChild(valueLabel); }
                else row.AddChild(arrow);
            }
            _controls.AddChild(row);
        }
    }

    public void CloseView()
    {
        Hide(); _panel.Visible = true; _dialog.Close(); _modal.Visible = _dialogScreen.Visible = false; _art.CloseView(); _premadeArt.CloseView(); _newCharacterArt.CloseView(); _shopArt.CloseView(); _noteScreen.Visible = false; _artNote.SetText("", NoteColour); _optionsArt = false; _sides.Clear();
        foreach (var child in _buttons.GetChildren()) { _buttons.RemoveChild(child); child.QueueFree(); }
        foreach (var child in _controls.GetChildren()) { _controls.RemoveChild(child); child.QueueFree(); }
        RenderCreation(null, false);
        RenderShop(null, false, "");
        GetViewport().GuiReleaseFocus();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _backdrop.Position = Vector2.Zero; _backdrop.Size = size;
        UpdateSides();
        _modal.Position = Vector2.Zero; _modal.Size = size;
        if (_dialogScreen.Visible)
        {
            // The plate the menu's art stands on (the dialog's point is a point of the original's 800 × 600 screen).
            var (factor, position) = _art.PlateFit();
            _dialogScreen.Scale = new Vector2(factor, factor); _dialogScreen.Position = position;
            _dialogScreen.TextureFilter = _art.TextureFilter; // the box samples as the plate under it does (ARC-UI-2577)
        }
        if (_art.Visible || _premadeArt.Visible || _newCharacterArt.Visible || _shopArt.Visible)
        {
            if (_art.Visible) _art.Place();
            if (_premadeArt.Visible) _premadeArt.Place();
            if (_newCharacterArt.Visible) _newCharacterArt.Place();
            if (_shopArt.Visible) _shopArt.Place();
            // At the top, away from the build line at the bottom right, on the scale of the plate the visible art stands on.
            Control plate = _art.Visible ? _art : _premadeArt.Visible ? _premadeArt : _newCharacterArt.Visible ? _newCharacterArt : _shopArt;
            _noteScreen.Scale = plate.Scale; _noteScreen.Position = plate.Position;
            _artNote.Position = _noteAtBottom ? new(NoteBox.X, NoteBox.Y)
                : new(_artNote.LineCount == 1 ? (NoteScreenWidth - _artNote.FirstLineWidth) / 2f : NoteMargin, NoteTop);
        }
        var width = Math.Max(1, Math.Min(560, size.X - 48));
        _panel.Size = new(width, 0);
        var height = _panel.GetCombinedMinimumSize().Y;
        _panel.Position = new((size.X - width) / 2, _dockedBelow ? Math.Max(24, size.Y - height - 24) : Math.Max(24, (size.Y - height) / 2));
    }

    /// <summary>ARC-UI-2891: while a Steamwoven menu is drawn by this panel (the full-screen menus and the pre-made, new-character and shop screens) the plate fills the window's height and the window around it is black (<see cref="SteamwovenColumnSidesView"/>); no backdrop of its own.</summary>
    private void UpdateSides()
    {
        var anyArt = _art.Visible || _premadeArt.Visible || _newCharacterArt.Visible || _shopArt.Visible;
        if (anyArt && AreaGameBarPanel.ColumnMenuPlan(GetViewport().GetVisibleRect().Size) is { } column) _sides.Present(column);
        else _sides.Clear();
    }

    /// <summary>ARC-UI-2891: for the self-test: the black around the plate and whether it shows.</summary>
    internal (SteamwovenColumnSidesView Sides, bool Shown) ColumnSidesForSelfTest => (_sides, _sides.Visible);

    private const int NoteScreenWidth = 800, NoteMargin = 24, NoteTop = 8;
    private static readonly (byte R, byte G, byte B) NoteColour = (224, 214, 190);
    private static readonly (byte R, byte G, byte B) NoteBoxColour = (255, 255, 255);
    private static readonly (int X, int Y, int Width, int Height) NoteBox = (290, 504, 291, 78);
    private bool _dockedBelow, _optionsArt, _noteAtBottom;

    /// <summary>ARC-UI-2968: the original's text font (art 229) for the art menus' note; without it the note draws nothing.</summary>
    public void SetNoteFonts(AreaTextFonts? fonts) => _artNote.SetFonts(fonts);

    /// <summary>ARC-UI-038: the opening shop's window at the bottom, below the docked barter window, with its text compact.</summary>
    public void SetDockedBelow(bool docked)
    {
        _dockedBelow = docked;
        if (_shopText is not null) _shopText.CustomMinimumSize = new Vector2(320, docked ? 0 : 120);
        ResizePanel();
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
