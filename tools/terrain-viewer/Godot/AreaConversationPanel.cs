using Arcanum.Presentation.Hud;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-SCRIPT-005 diagnostic conversation surface: up to five replies (mouse or controller menu), the
/// outcome and the recent typed effects. ARC-UI-2027: the NPC's line is not drawn here: the original shows it as a persistent text bubble over the NPC (0x5684C0
/// → 0x568540) and its window lists only the PC's options; the panel hands the line to its owner through <see cref="NpcLineShown"/> and tells it when a reply is picked (<see cref="ReplyPicking"/>). It renders a <see cref="DialogConversation"/>; it owns no dialog rules. ARC-UI-071 (B29): with the
/// original's text fonts (<see cref="SetFonts"/>) every text of the panel, the replies' too, is drawn in font art 229 with the original's
/// blend (<see cref="AreaArtParagraph"/>), the font of the original's dialog box (DB1); without them (no game folder) the labels and buttons
/// draw in the viewer's font. The original's own reply band is built with the same font art 229 (0x4C9280); this panel is not that band.
/// </summary>
public partial class AreaConversationPanel : CanvasLayer
{
    public const int MaxShownEffects = 6;
    // ARC-UI-071: the panel's content margin on each side, and a reply button's padding around its art text.
    private const int ContentMargin = 16, ButtonPadX = 8, ButtonPadY = 4;
    private static readonly (byte, byte, byte) HeaderColour = (179, 191, 204), OutcomeColour = (255, 217, 140), EffectsColour = (153, 204, 166),
        HintColour = (140, 153, 166), ReplyColour = (223, 223, 223), BlockedColour = (111, 111, 111);
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private VBoxContainer _options = null!;
    private Label _outcome = null!;
    private Label _effects = null!;
    private Label _hint = null!;
    private readonly List<Button> _buttons = new();
    private Button _leave = null!;
    private Control _bandTop = null!;
    private VBoxContainer _contents = null!;
    // ARC-UI-2022: the Original band: a translucent strip over the game view (the shade subtracts 18 from each channel under it), the reply stack inside it.
    private Control _band = null!;
    private ColorRect _bandShade = null!;
    private int _bandWidth = DialogReplyBand.MinWidth;
    private DialogReplyBand.Hover? _hover;
    private (int Width, int Rows) _hoverKey;
    // ARC-UI-2052: the pointer's last place as a viewport point (null: left the band or never seen; the panel moves under a stationary pointer when its
    // texts change, so a band-local point would go stale) and whether the hover is to be recomputed; a re-render (a reply rebuilt the rows) redraws
    // them against the pointer, as the original's row drawer does against the live cursor.
    private Vector2? _pointer;
    private bool _hoverStale;
    private DialogConversation? _conversation;
    private string _title = string.Empty;
    private string? _message;
    // ARC-UI-071: the original's text fonts (null: the viewer's font) and the paragraphs that draw with them beside each label.
    private AreaTextFonts? _fonts;
    private AreaArtParagraph _headerArt = null!, _outcomeArt = null!, _effectsArt = null!, _hintArt = null!, _leaveArt = null!;
    private int _contentWidth = 640;
    private bool _artDirty;
    public event Action? CloseRequested;
    /// <summary>ARC-UI-043: a reply the PC picked was processed (whatever it did to the dialog).</summary>
    public event Action? Picked;
    /// <summary>ARC-UI-2027 (0x5684C0): the conversation's NPC line was shown (opened, or a reply continued the dialog): the owner makes it a bubble over the NPC: persistent while the dialog awaits a reply (0x5684C0, life −2), else the final line with the default life (0x567E30 result 4).</summary>
    public event Action<string, bool>? NpcLineShown;
    /// <summary>ARC-UI-2027 (0x567E30): the PC picked a reply that the dialog will process: the owner removes the NPC's bubble first.</summary>
    public event Action? ReplyPicking;
    public bool IsOpen => Visible;
    /// <summary>ARC-UI-2019: whether each reply shows its number and a digit key chooses it (Steamwoven); Original draws and reads neither. Set before <see cref="Open"/>.</summary>
    public bool NumberedReplies
    {
        get => _numberedReplies;
        set
        {
            if (_numberedReplies == value) return;
            _numberedReplies = value;
            if (IsNodeReady() && IsOpen) Render(_message);
        }
    }
    private bool _numberedReplies = true;
    /// <summary>ARC-UI-2019: where an open panel reads the setting from every frame, so a rule-mode switch made over the dialog (Esc, Options) draws and reads at once.</summary>
    public Func<bool>? NumberedSource { get; set; }
    /// <summary>
    /// ARC-UI-2025 (ledger DH1–DH7): the Original interface setting. The replies are laid out as the original's reply band (five 16 px rows
    /// every 19 px, 3 px down, no wrapping) and a press is resolved by the band's rules, y / 20 included, with the original's hover; false
    /// (Steamwoven): the replies are the wrapped buttons and the row hit is the button's own rectangle. In both a reply is chosen on the
    /// press itself, not on the release. Set before <see cref="Open"/>.
    /// </summary>
    public bool OriginalReplyRows
    {
        get => _originalRows;
        set
        {
            if (_originalRows == value) return;
            _originalRows = value;
            if (IsNodeReady() && IsOpen) Render(_message);
        }
    }
    private bool _originalRows;
    /// <summary>ARC-UI-2025: where an open panel reads <see cref="OriginalReplyRows"/> from every frame (a rule-mode switch made over the dialog).</summary>
    public Func<bool>? OriginalRowsSource { get; set; }
    /// <summary>
    /// ARC-UI-2268 (ledger DH3): whether a press on the band is resolved by the original's click bands (y / 20, an empty slot refused: Original) or by the drawn row
    /// (Steamwoven). The band itself is the layout of both settings. Defaults to the original's bands, as the self-tests drive it.
    /// </summary>
    public bool ClickBandsDrift { get; set; } = true;
    /// <summary>ARC-UI-2268: where an open panel reads <see cref="ClickBandsDrift"/> from every frame.</summary>
    public Func<bool>? ClickBandsDriftSource { get; set; }
    /// <summary>
    /// ARC-UI-2023, ARC-UI-2268: the viewer sets it in normal play (both settings; false only in the diagnostic mode). The original's dialog window shows only the PC's reply rows (ways out: a DLG reply, or a window or
    /// event that pops mode 3, 0x5678D0), so the header, the outcome line, the effects ledger, the hint and the Leave button are hidden and the
    /// controller cancel does not leave an awaiting dialog that has an enabled reply; false: the diagnostic chrome stays. A message-only panel (no conversation
    /// could start) keeps its message and a finished dialog keeps its Close button: nothing else could close them. Set before <see cref="Open"/>.
    /// </summary>
    public bool DiagnosticChromeHidden
    {
        get => _chromeHidden;
        set
        {
            if (_chromeHidden == value) return;
            _chromeHidden = value;
            if (IsNodeReady() && IsOpen) Render(_message);
        }
    }
    private bool _chromeHidden;
    /// <summary>ARC-UI-2023: where an open panel reads <see cref="DiagnosticChromeHidden"/> from every frame (a rule-mode switch made over the dialog).</summary>
    public Func<bool>? ChromeHiddenSource { get; set; }
    private bool Awaiting => _conversation is { Phase: DialogConversationPhase.AwaitingSelection };
    /// <summary>Whether the Leave button is offered: hidden with the chrome hidden while a reply can be chosen (the original has no way out but a reply); with no enabled reply (none offered, all blocked, a refused reply) it stays so that the modal panel cannot lock.</summary>
    private bool LeaveOffered => !(_chromeHidden && Awaiting && _buttons.Any(button => !button.Disabled));
    internal bool LeaveOfferedForSelfTest => LeaveOffered;
    /// <summary>ARC-UI-2023: which diagnostic parts draw now (a label or an art paragraph is visible): header, outcome, effects, hint, Leave.</summary>
    internal (bool Header, bool Outcome, bool Effects, bool Hint, bool Leave) ChromeShownForSelfTest =>
        (_header.Visible || _headerArt.Visible, _outcome.Visible || _outcomeArt.Visible, _effects.Visible || _effectsArt.Visible, _hint.Visible || _hintArt.Visible, _leave.Visible);
    public DialogConversation? Conversation => _conversation;
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    /// <summary>The conversation's current NPC line (held, not drawn here: it is a bubble over the NPC).</summary>
    internal string NpcLineForSelfTest => _conversation?.NpcLine?.Text ?? string.Empty;
    internal string OutcomeForSelfTest => _outcome.Text;
    internal Button LeaveForSelfTest => _leave;
    /// <summary>ARC-UI-2025: the reply stack's width (the band's) and whether row i draws lit now.</summary>
    internal int BandWidthForSelfTest => (int)_band.Size.X;
    /// <summary>ARC-UI-2022: the band's place in the view ((left, top, width, height)), whether it is shown and the shade's blend mode name.</summary>
    internal (int X, int Y, int W, int H) BandRectForSelfTest => ((int)_band.Position.X, (int)_band.Position.Y, (int)_band.Size.X, (int)_band.Size.Y);
    /// <summary>ARC-UI-2022: whether the framed diagnostic panel draws (with the chrome hidden it holds nothing and does not).</summary>
    internal bool PanelShownForSelfTest => _panel.Visible;
    internal bool BandShownForSelfTest => _band.Visible && _originalRows && IsOpen;
    /// <summary>ARC-UI-2022: whether the reply stack lives in the band (Original) rather than in the panel.</summary>
    internal bool StackInBandForSelfTest => _options.GetParent() == _band;
    internal string BandShadeForSelfTest => $"{_bandShade.Color.R8},{_bandShade.Color.G8},{_bandShade.Color.B8}|{(_bandShade.Material as CanvasItemMaterial)?.BlendMode}";
    /// <summary>ARC-UI-2022: the text colour of reply row i (the main paragraph's, or the button's font colour without the original's fonts).</summary>
    internal (byte R, byte G, byte B)? RowColourForSelfTest(int row) => row < 0 || row >= _buttons.Count ? null
        : _buttons[row].GetNodeOrNull<AreaArtParagraph>(ArtName) is { } art ? art.Colour
        : ((byte)(_buttons[row].GetThemeColor("font_color").R8), (byte)_buttons[row].GetThemeColor("font_color").G8, (byte)_buttons[row].GetThemeColor("font_color").B8);
    /// <summary>ARC-UI-2022: whether row i has its drop shadow paragraph, and where (relative to the row).</summary>
    internal Vector2? RowShadowForSelfTest(int row) => row < 0 || row >= _buttons.Count ? null : _buttons[row].GetNodeOrNull<AreaArtParagraph>(ShadowName)?.Position;
    internal bool RowLitForSelfTest(int row) => _hover is { } hover && hover.IsLit(row);
    /// <summary>ARC-UI-2025: an event as the reply stack receives it (point relative to the band's top-left); true when the band took it.</summary>
    internal bool BandInputForSelfTest(InputEvent input) => HandleBandInput(input);
    /// <summary>ARC-UI-2083: the width of the first line a band row would draw for <paramref name="text"/> broken at <paramref name="wrap"/>, in the original's fonts (0 without them).</summary>
    internal int FirstLineWidthForSelfTest(string text, int wrap)
    {
        if (_fonts is null) return 0;
        var lines = AreaPremadeBrowserArtLayout.Wrap(_fonts.Text.Font, text, wrap);
        return lines.Count == 0 || lines[0].Count == 0 ? 0 : lines[0][^1].X + lines[0][^1].Glyph.Width;
    }
    /// <summary>
    /// ARC-UI-2083: lays the texts out as a new reply set of the band (the box back at its 400 px first, the rows widening it as the original's row drawer does) and
    /// returns the band's rectangle (the band's rows are used for the probe whatever the setting is); null without the original's fonts or an open panel.
    /// <see cref="RestoreRepliesForSelfTest"/> puts the setting and the conversation's own set back.
    /// </summary>
    internal (int X, int Y, int W, int H)? ShowReplySetForSelfTest(IReadOnlyList<string> texts)
    {
        if (_fonts is null || !IsOpen) return null;
        _probedRows ??= _originalRows;
        _originalRows = true;
        _bandWidth = DialogReplyBand.MinWidth;
        foreach (var button in _buttons) { _options.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        ApplyRowGeometry();
        for (var row = 0; row < texts.Count; row++) AddReplyRow($"Probe{row}", texts[row], false, () => { });
        _artDirty = true;
        ResizePanel();
        return BandRectForSelfTest;
    }
    /// <summary>ARC-UI-2083: draws the conversation's own reply set again after <see cref="ShowReplySetForSelfTest"/>.</summary>
    internal void RestoreRepliesForSelfTest()
    {
        if (_probedRows is { } rows) { _originalRows = rows; _probedRows = null; }
        Render(_message);
    }
    private bool? _probedRows;
    /// <summary>Whether the original's font art draws the panel's texts (false: the viewer's font).</summary>
    internal bool ArtFontsForSelfTest => _fonts is not null;
    /// <summary>The text a reply button shows, in either font.</summary>
    internal static string ReplyTextForSelfTest(Button button) => button.HasMeta(ReplyTextMeta) ? button.GetMeta(ReplyTextMeta).AsString() : button.Text;
    /// <summary>The panel's art-font paragraphs: (name, text, lines drawn, wrap width, visible).</summary>
    internal IReadOnlyList<(string Name, string Text, int Lines, int Wrap, bool Visible)> ArtTextsForSelfTest()
    {
        var found = new List<(string, string, int, int, bool)>();
        foreach (var (name, art) in new[] { ("header", _headerArt), ("outcome", _outcomeArt), ("effects", _effectsArt), ("hint", _hintArt), ("leave", _leaveArt) })
            found.Add((name, art.Text, art.LineCount, art.WrapWidth, art.Visible));
        foreach (var button in _buttons)
            if (button.GetNodeOrNull<AreaArtParagraph>(ArtName) is { } reply) found.Add((button.Name, reply.Text, reply.LineCount, reply.WrapWidth, reply.Visible));
        return found;
    }
    private const string ReplyTextMeta = "replyText", ArtName = "Art", ShadowName = "Shadow";

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaConversation", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = _contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.7f, .75f, .8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _options = new VBoxContainer();
        // ARC-UI-2022: the band (the Original setting) takes the pointer; its stack lives inside it, the plain stack in the panel.
        _band = new Control { Name = "ReplyBand", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _bandShade = new ColorRect
        {
            Name = "Shade", MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(DialogReplyBand.DarkenBy / 255f, DialogReplyBand.DarkenBy / 255f, DialogReplyBand.DarkenBy / 255f, 1f),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Sub },
        };
        _band.AddChild(_bandShade);
        _band.GuiInput += input => { if (HandleBandInput(input)) _band.AcceptEvent(); };
        _band.MouseExited += () => { _pointer = null; _hover?.Leave(); };
        AddChild(_band);
        _bandTop = new Control { Name = "BandTop", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _options.AddChild(_bandTop);
        _outcome = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _effects = new Label { Modulate = new Color(.6f, .8f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _hint = new Label { Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _leave = new Button { Name = "Leave", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _leave.ActionMode = BaseButton.ActionModeEnum.Press;
        _leave.Pressed += Leave;
        _headerArt = Paragraph("HeaderArt"); _outcomeArt = Paragraph("OutcomeArt");
        _effectsArt = Paragraph("EffectsArt"); _hintArt = Paragraph("HintArt");
        _leaveArt = Paragraph(ArtName); _leave.AddChild(_leaveArt);
        foreach (var control in new Control[] { _header, _headerArt, _options, _outcome, _outcomeArt, _effects, _effectsArt, _leave, _hint, _hintArt })
            contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private AreaArtParagraph Paragraph(string name) => new() { Name = name, Visible = false };

    /// <summary>ARC-UI-071: gives the panel the original's text fonts (null: the viewer's own font) and draws it again when it is open.</summary>
    public void SetFonts(AreaTextFonts? fonts)
    {
        if (ReferenceEquals(fonts, _fonts)) return;
        _fonts = fonts;
        foreach (var art in new[] { _headerArt, _outcomeArt, _effectsArt, _hintArt, _leaveArt }) art.SetFonts(fonts);
        if (IsNodeReady() && IsOpen) Render(_message);
    }

    /// <summary>Shows a conversation, or only a message when none could start.</summary>
    public void Open(string title, DialogConversation? conversation, string? message = null)
    {
        _title = title; _conversation = conversation; _message = message; _focusLit = false;
        Show(); Render(message);
        ShowNpcLine();
    }

    /// <summary>ARC-UI-2027 (ledger TB23): hands the conversation's current NPC line to the owner (nothing for an empty one, nor for one a window-opening reply
    /// leaves unchanged: the barter, sheet, schematic and other windows open with no new line). A dialog that no longer awaits a reply shows its last line with the default life.</summary>
    private void ShowNpcLine(DialogNpcLine? previous = null)
    {
        if (_conversation?.NpcLine is not { Text.Length: > 0 } current) return;
        var awaiting = _conversation.Phase == DialogConversationPhase.AwaitingSelection;
        if (!awaiting && (ReferenceEquals(current, previous) || IsWindowEnd(_conversation.End?.Kind))) return;
        NpcLineShown?.Invoke(current.Text, awaiting);
    }

    private static bool IsWindowEnd(DialogEndKind? kind) => kind is DialogEndKind.SchematicWindow or DialogEndKind.BarterWindow or DialogEndKind.NpcSheetWindow
        or DialogEndKind.WorldMapWindow or DialogEndKind.ItemIdentifyWindow or DialogEndKind.ItemRepairWindow;

    public void CloseView()
    {
        _conversation = null; _pointer = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Selects the reply with this 1-based number; once the dialog is over any choice closes the panel.</summary>
    public void Choose(int number)
    {
        if (_conversation is not { Phase: DialogConversationPhase.AwaitingSelection } conversation) { CloseRequested?.Invoke(); return; }
        var option = conversation.Options.FirstOrDefault(candidate => candidate.Number == number);
        if (option is not null) ChooseToken(option.Token);
    }

    /// <summary>
    /// ARC-UI-2019: the digit key of the reply with this number; true when it chose a reply. Only while a numbered panel awaits a choice and the
    /// reply is offered and not blocked: any other key press leaves the dialog as it is (it never closes a finished one, as a click does).
    /// </summary>
    public bool ChooseByDigit(int number)
    {
        if (!NumberedReplies || _conversation is not { Phase: DialogConversationPhase.AwaitingSelection } conversation) return false;
        var option = conversation.Options.FirstOrDefault(candidate => candidate.Number == number);
        if (option is null || option.Block is not null) return false;
        ChooseToken(option.Token);
        return true;
    }

    /// <summary>Selects exactly the reply the button showed: a token from older offers is NotOffered.</summary>
    private void ChooseToken(DialogOptionToken token)
    {
        if (_conversation is not { Phase: DialogConversationPhase.AwaitingSelection } conversation) return;
        var before = conversation.NpcLine;
        var result = conversation.Select(token);
        if (result.Status == DialogConversationSelectStatus.NotOffered) return;
        // Only a reply that ran (the dialog went on or ended) is processed; one the dialog refused or left at the frontier ran nothing and keeps the bubble.
        var processed = result.Status is DialogConversationSelectStatus.Continued or DialogConversationSelectStatus.Ended;
        if (result.Status is not (DialogConversationSelectStatus.UnsupportedTransition or DialogConversationSelectStatus.StaleSelection
            or DialogConversationSelectStatus.WrongPhase)) Picked?.Invoke();
        if (processed) ReplyPicking?.Invoke();
        Render(result.Status is DialogConversationSelectStatus.UnsupportedTransition or DialogConversationSelectStatus.StaleSelection
            ? $"[{result.Status}: the reply was not executed]" : null);
        // ARC-UI-2027: the reply's answer line (the next node's) becomes the new bubble; a reply that was not processed keeps the old bubble.
        if (processed) ShowNpcLine(before);
        // ARC-TECHNOLOGY-006: SU ends the dialog at once for the schematic window, with no further NPC line (ledger NC1–NC2); ARC-UI-050:
        // b: for the barter window (BT7); ARC-UI-088: c-e for the NPC's sheet (WN2).
        // ARC-UI-2028: a reply that leads to the NPC's last line closes the window in the same call; the host floats the line (ledger DF1).
        if (IsWindowEnd(conversation.End?.Kind) || AreaConversationText.EndsWithNpcLine(conversation)) CloseRequested?.Invoke();
    }

    /// <summary>Diagnostic leave: closes an awaiting dialog without a reply effect, or dismisses a finished one.</summary>
    public void Leave()
    {
        _conversation?.Close();
        CloseRequested?.Invoke();
    }

    /// <summary>The controller cancel: leaves like <see cref="Leave"/>, except with the chrome hidden while a reply is awaited (ARC-UI-2023: the original has no such way out).</summary>
    public void Cancel()
    {
        if (LeaveOffered) Leave();
    }

    public void Navigate(int direction)
    {
        _focusLit = true;
        var enabled = _buttons.Where(button => !button.Disabled).ToList();
        if (LeaveOffered) enabled.Add(_leave);
        if (enabled.Count == 0) return;
        var focus = enabled.FindIndex(button => button.HasFocus());
        enabled[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, enabled.Count - 1)].GrabFocus();
    }

    public void Confirm()
    {
        var focused = _buttons.Append(_leave).FirstOrDefault(button => button.HasFocus() && !button.Disabled && (button != _leave || LeaveOffered));
        if (focused is not null) focused.EmitSignal(BaseButton.SignalName.Pressed);
        else if (_conversation is not { Phase: DialogConversationPhase.AwaitingSelection }) CloseRequested?.Invoke();
    }

    /// <summary>One reply row (a button, in the band's geometry), added to the stack; <paramref name="chosen"/> runs after the pressing signal returns.</summary>
    private void AddReplyRow(string name, string text, bool blocked, Action chosen)
    {
        var button = new Button
        {
            Name = name, Alignment = HorizontalAlignment.Left,
            AutowrapMode = _originalRows ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart, Disabled = blocked,
            FocusMode = Control.FocusModeEnum.All, ActionMode = BaseButton.ActionModeEnum.Press,
        };
        if (_originalRows) StyleBandRow(button);
        button.SetMeta(ReplyTextMeta, text);
        if (_fonts is null) button.Text = text;
        else
        {
            // ARC-UI-2022 (0x4C9810, 0x5351D0): a band row is the text at its row's left with a drop shadow 1 px right and down, the shadow drawn first.
            if (_originalRows)
            {
                var shadow = new AreaArtParagraph { Name = ShadowName, Position = new(1, 1) };
                shadow.SetFonts(_fonts); button.AddChild(shadow); shadow.SetText(text, DialogReplyBand.ShadowColour);
            }
            var art = new AreaArtParagraph { Name = ArtName, Position = new(_originalRows ? 0 : ButtonPadX, _originalRows ? 0 : ButtonPadY), WrapWidth = _originalRows ? _bandWidth - DialogReplyBand.Margins : Math.Max(1, _contentWidth - 2 * ButtonPadX) };
            art.SetFonts(_fonts);
            button.AddChild(art);
            art.SetText(text, blocked ? BlockedColour : _originalRows ? DialogReplyBand.RowColour : ReplyColour);
        }
        // Rebuild after the emitting button's signal returns: Render frees the buttons.
        button.Pressed += () => Callable.From(chosen).CallDeferred();
        _options.AddChild(button); _buttons.Add(button);
    }

    private void Render(string? message)
    {
        _message = message; _artDirty = true;
        // ARC-UI-2022 (0x4C9720): every new reply set starts the box at its 400 px again.
        _bandWidth = DialogReplyBand.MinWidth;
        foreach (var button in _buttons) { _options.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        var conversation = _conversation;
        var header = conversation is null ? _title : $"{_title} | turn {conversation.Turn} | DLG node {conversation.CurrentNode.Value}";
        var chromeHidden = _chromeHidden && conversation is not null;
        Show(_header, _headerArt, chromeHidden ? string.Empty : header, HeaderColour, chromeHidden);
        ApplyRowGeometry();
        if (conversation is not null)
        {
            foreach (var option in conversation.Options)
            {
                var token = option.Token;
                AddReplyRow($"Reply{option.Number}", AreaConversationText.OptionText(option, NumberedReplies), option.Block is not null, () => ChooseToken(token));
            }
        }
        var outcome = conversation is null ? string.Empty : AreaConversationText.Outcome(conversation);
        Show(_outcome, _outcomeArt, chromeHidden ? string.Empty : string.Join("\n", new[] { message, outcome }.Where(text => !string.IsNullOrEmpty(text))), OutcomeColour, chromeHidden);
        Show(_effects, _effectsArt, chromeHidden || conversation is null || conversation.Effects.Count == 0 ? string.Empty
            : "Effects (diagnostic ledger, newest last):\n" + string.Join("\n", conversation.Effects.TakeLast(MaxShownEffects).Select(AreaConversationText.Effect)), EffectsColour, chromeHidden);
        var awaiting = conversation is { Phase: DialogConversationPhase.AwaitingSelection };
        var leaveOffered = LeaveOffered;
        var leave = awaiting ? "Leave without answering (diagnostic)" : "Close";
        _leave.SetMeta(ReplyTextMeta, leave);
        _leave.Visible = leaveOffered;
        _leave.Text = _fonts is null ? leave : string.Empty;
        _leaveArt.Visible = _fonts is not null && leaveOffered;
        _leaveArt.Position = new(ButtonPadX, ButtonPadY);
        _leaveArt.SetText(leave, ReplyColour);
        Show(_hint, _hintArt, chromeHidden ? string.Empty : (NumberedReplies ? "Click a reply or press its number (controller: D-pad, confirm, cancel)" : "Click a reply (controller: D-pad, confirm, cancel)") + " | PROJECT diagnostic PC/state profile; original runtime parity NOT_VERIFIED", HintColour, chromeHidden);
        (_buttons.FirstOrDefault(button => !button.Disabled) ?? (leaveOffered ? _leave : null))?.GrabFocus();
        ResizePanel();
    }

    /// <summary>Sets a text in the label (the viewer's font, in the colour as a modulate) or, with the original's fonts, in its paragraph (hidden when empty).</summary>
    private void Show(Label label, AreaArtParagraph art, string text, (byte R, byte G, byte B) colour, bool hidden = false)
    {
        label.Text = text;
        label.Modulate = new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f);
        label.Visible = _fonts is null && !hidden;
        art.Visible = _fonts is not null && text.Length > 0;
        art.SetText(text, colour);
    }

    /// <summary>ARC-UI-071: breaks every art text at the panel's content width and gives the reply buttons the height of theirs.</summary>
    private void LayoutArt()
    {
        if (_fonts is null) return;
        foreach (var art in new[] { _headerArt, _outcomeArt, _effectsArt, _hintArt }) art.WrapWidth = _contentWidth;
        foreach (var button in _buttons.Append(_leave))
        {
            if (button.GetNodeOrNull<AreaArtParagraph>(ArtName) is not { } art) continue;
            if (_originalRows && button != _leave) { LayoutBandRow(button, art); continue; }
            art.WrapWidth = Math.Max(1, _contentWidth - 2 * ButtonPadX);
            button.CustomMinimumSize = new(0, art.GetMinimumSize().Y + 2 * ButtonPadY);
        }
    }

    /// <summary>
    /// ARC-UI-2022 (ledger DH7, DH13): a band row's text is broken at the box width minus 12; only a line that fits the 16 px row whole is placed
    /// (<see cref="AreaArtParagraph.Room"/>); a word wider than the row draws nothing; the drawn width plus 1 and 12 widens the box (and re-centres it) when larger.
    /// </summary>
    private void LayoutBandRow(Button button, AreaArtParagraph art)
    {
        button.CustomMinimumSize = new(0, DialogReplyBand.RowHeight);
        var wrap = _bandWidth - DialogReplyBand.Margins;
        art.WrapWidth = wrap;
        art.Room = DialogReplyBand.RowHeight;
        var shadow = button.GetNodeOrNull<AreaArtParagraph>(ShadowName);
        if (shadow is not null) { shadow.WrapWidth = wrap; shadow.Room = DialogReplyBand.RowHeight; }
        // A word wider than the row leaves no line (0x535C40 fails), so nothing is drawn and nothing widens the box.
        _bandWidth = DialogReplyBand.Widened(_bandWidth, art.FirstLineWidth);
    }

    private const string LitMeta = "rowLit";
    private bool _focusLit;
    private static readonly StyleBoxFlat PlainRow = new() { BgColor = new Color(1f, 1f, 1f, 0f), ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0 };

    /// <summary>ARC-UI-2025: the reply stack as the original's band (a 100 px tall area, rows every 19 px starting 3 px down) or as the plain stack.
    /// ARC-UI-2022: the band's stack lives in the band over the game view; the plain one in the panel.</summary>
    private void ApplyRowGeometry()
    {
        var home = _originalRows ? (Node)_band : _contents;
        if (_options.GetParent() != home)
        {
            _options.GetParent()?.RemoveChild(_options);
            home.AddChild(_options);
            if (!_originalRows) _contents.MoveChild(_options, _headerArt.GetIndex() + 1);
        }
        _band.Visible = _originalRows && _buttons.Count > 0;
        // The empty first child makes the stack's 3 px separation the 3 px between the band's top and the first row.
        _bandTop.Visible = _originalRows;
        _options.MouseFilter = _originalRows ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Pass;
        _options.CustomMinimumSize = new(0, _originalRows ? DialogReplyBand.Height : 0);
        if (_originalRows) _options.AddThemeConstantOverride("separation", DialogReplyBand.RowPitch - DialogReplyBand.RowHeight);
        else _options.RemoveThemeConstantOverride("separation");
        _hover = null; _hoverStale = true;
    }

    /// <summary>
    /// ARC-UI-2052 (ledger DH9): every frame the hover is recomputed from the pointer's viewport place (the last one seen in the band, else the live mouse
    /// once after a rebuild), so a row under a stationary pointer is lit without a motion even when the panel moved. Waits until the band has a width.
    /// </summary>
    private void RefreshHover()
    {
        var width = (int)_band.Size.X;
        if (width <= 0) return;
        var at = _pointer is { } viewport ? _band.GetGlobalTransformWithCanvas().AffineInverse() * viewport
            : _hoverStale ? _band.GetLocalMousePosition() : (Vector2?)null;
        if (at is not { } local) return;
        if (_hover is null || _hoverKey != (width, _buttons.Count)) { _hover = new DialogReplyBand.Hover(width, _buttons.Count); _hoverKey = (width, _buttons.Count); }
        _hover.Move((int)local.X, (int)local.Y);
        _hoverStale = false;
    }

    /// <summary>A reply row of the band: 16 px tall, no frame, the pointer reaches it through the band, not the button.</summary>
    private static void StyleBandRow(Button button)
    {
        button.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.ClipContents = true;
        button.CustomMinimumSize = new(0, DialogReplyBand.RowHeight);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        button.AddThemeFontSizeOverride("font_size", 11);
        foreach (var name in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed" }) button.AddThemeStyleboxOverride(name, PlainRow);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        SetRowFontColour(button, DialogReplyBand.RowColour);
    }

    /// <summary>The viewer's font colour of a band row (without the original's fonts), in every button state.</summary>
    private static void SetRowFontColour(Button button, (byte R, byte G, byte B) colour)
    {
        var tint = new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f);
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(name, tint);
    }

    /// <summary>The row the pointer or the keyboard focus is on draws lit: the original's second font, yellow (ledger DH6; a blocked row, which the original has none of, stays grey).</summary>
    private void LightRows()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            // The keyboard or controller focus lights a row only once it was moved by Navigate: the original has none, and the focus the panel
            // gives the first reply on opening must not light it under a pointer that is elsewhere.
            var lit = !_buttons[i].Disabled && (_hover?.IsLit(i) == true || (_focusLit && _buttons[i].HasFocus()));
            if (_buttons[i].Disabled || (_buttons[i].HasMeta(LitMeta) && _buttons[i].GetMeta(LitMeta).AsBool() == lit)) continue;
            _buttons[i].SetMeta(LitMeta, lit);
            var colour = lit ? DialogReplyBand.LitColour : DialogReplyBand.RowColour;
            if (_buttons[i].GetNodeOrNull<AreaArtParagraph>(ArtName) is { } art) art.SetText(art.Text, colour);
            SetRowFontColour(_buttons[i], colour);
        }
    }

    /// <summary>
    /// ARC-UI-2025: a pointer event on the reply band. Original rows: a move updates the original's hover bookkeeping and a left press resolves
    /// the row by the band's rules (a gap or an empty slot is refused, a blocked reply chooses nothing). True when the band took the event.
    /// </summary>
    private bool HandleBandInput(InputEvent input)
    {
        if (!_originalRows || !IsOpen) return false;
        var width = (int)_band.Size.X;
        switch (input)
        {
            case InputEventMouseMotion motion:
                _pointer = _band.GetGlobalTransformWithCanvas() * motion.Position;
                if (_hover is null || _hoverKey != (width, _buttons.Count)) { _hover = new DialogReplyBand.Hover(width, _buttons.Count); _hoverKey = (width, _buttons.Count); }
                _hover.Move((int)motion.Position.X, (int)motion.Position.Y);
                return false;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _pointer = _band.GetGlobalTransformWithCanvas() * press.Position;
                var hit = DialogReplyBand.Press((int)press.Position.X, (int)press.Position.Y, width, _buttons.Count, clickBandsDrift: ClickBandsDrift);
                if (hit.Kind == DialogReplyHitKind.Outside) { _hover?.Leave(); return false; }
                if (hit.Kind == DialogReplyHitKind.Row && !_buttons[hit.Row].Disabled) _buttons[hit.Row].EmitSignal(BaseButton.SignalName.Pressed);
                return true;
            default:
                return false;
        }
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        // ARC-UI-2022: with the chrome gone (ARC-UI-2023) the framed panel has nothing to hold (the band carries the replies): it is not drawn.
        // The band shows only while there is a reply in it (a message-only panel or a finished dialog has none).
        _band.Visible = _originalRows && _buttons.Count > 0;
        _panel.Visible = !_originalRows || new Control[] { _header, _headerArt, _outcome, _outcomeArt, _effects, _effectsArt, _hint, _hintArt, _leave }.Any(control => control.Visible);
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        var content = Math.Max(1, (int)width - 2 * ContentMargin);
        if (content != _contentWidth || _artDirty) { _contentWidth = content; _artDirty = false; LayoutArt(); }
        _panel.Size = new(width, 0);
        // ARC-UI-2024 (DB3): the panel lies in the part the bars leave free (the original's band is in the game view above the bottom bar).
        var (_, free) = AreaGameBarPanel.FreeArea(size);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, free.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = PanelPosition(size, width, height);
        if (_originalRows) PlaceBand(size, height);
    }

    /// <summary>
    /// ARC-UI-2022 (ledger DH4, DH11, DB3): the band over the game view: 100 px tall, as wide as the rows made it (at least 400), centred, its top at 299 in a
    /// 400 px view else the view height minus 181; the view is the part the bars leave free (the original's iso window, (0, 41, 800, 400) with the
    /// interface on, ledger DB3), so the band lies above the bottom bar. The stack inside it is 6 px in on both sides. A diagnostic panel that is still drawn sits above the band.
    /// </summary>
    private void PlaceBand(Vector2 window, float panelHeight)
    {
        var (freeTop, free) = AreaGameBarPanel.FreeArea(window);
        // ARC-UI-2268: the band is drawn at the screen's whole factor, as the bars are; its geometry (and the view it sits in) is counted in the original's pixels.
        var factor = AreaGameBarPanel.ScreenFactor(window);
        var left = DialogReplyBand.BoxLeft((int)(free.X / factor), _bandWidth) * factor;
        var top = freeTop + DialogReplyBand.BoxTop((int)(free.Y / factor)) * factor;
        _band.Scale = new(factor, factor);
        _band.Position = new(left, top);
        _band.Size = new(_bandWidth, DialogReplyBand.Height);
        _bandShade.Position = Vector2.Zero;
        _bandShade.Size = _band.Size;
        _options.Position = new(DialogReplyBand.TextX, 0);
        _options.Size = new(_bandWidth - DialogReplyBand.Margins, DialogReplyBand.Height);
        _panel.Position = new(_panel.Position.X, Mathf.Round(Math.Max(freeTop, top - panelHeight - 8)));
    }

    /// <summary>ARC-UI-2024: the panel's top left corner: centred, its bottom 24 px above the bottom bar (the view's bottom without the bars), and not above
    /// the top strip's 24 px. Whole pixels: the art text's glyphs are drawn pixel for pixel (MW31).</summary>
    internal static Vector2 PanelPosition(Vector2 viewSize, float width, float height)
    {
        var (freeTop, free) = AreaGameBarPanel.FreeArea(viewSize);
        return new(Mathf.Round((viewSize.X - width) / 2), Mathf.Round(freeTop + Math.Max(24, free.Y - height - 24)));
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        if (NumberedSource is { } source) NumberedReplies = source();
        if (OriginalRowsSource is { } rows) OriginalReplyRows = rows();
        if (ClickBandsDriftSource is { } drift) ClickBandsDrift = drift();
        if (ChromeHiddenSource is { } chrome) DiagnosticChromeHidden = chrome();
        if (_originalRows) { if (_hoverStale || _pointer is not null) RefreshHover(); LightRows(); }
        ResizePanel();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
