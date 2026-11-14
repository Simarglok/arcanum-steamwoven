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
/// draw in the viewer's font. PROJECT: the font of the original's talk window itself is not read.
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
    private DialogReplyBand.Hover? _hover;
    private (int Width, int Rows) _hoverKey;
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
    public DialogConversation? Conversation => _conversation;
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    /// <summary>The conversation's current NPC line (held, not drawn here: it is a bubble over the NPC).</summary>
    internal string NpcLineForSelfTest => _conversation?.NpcLine?.Text ?? string.Empty;
    internal string OutcomeForSelfTest => _outcome.Text;
    internal Button LeaveForSelfTest => _leave;
    /// <summary>ARC-UI-2025: the reply stack's width (the band's) and whether row i draws lit now.</summary>
    internal int BandWidthForSelfTest => (int)_options.Size.X;
    internal bool RowLitForSelfTest(int row) => _hover is { } hover && hover.IsLit(row);
    /// <summary>ARC-UI-2025: an event as the reply stack receives it (point relative to the band's top-left); true when the band took it.</summary>
    internal bool BandInputForSelfTest(InputEvent input) => HandleBandInput(input);
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
    private const string ReplyTextMeta = "replyText", ArtName = "Art";

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
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Modulate = new Color(.7f, .75f, .8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _options = new VBoxContainer();
        _options.GuiInput += input => { if (HandleBandInput(input)) _options.AcceptEvent(); };
        _options.MouseExited += () => { _hover?.Leave(); };
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
        _conversation = null; Hide();
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

    public void Navigate(int direction)
    {
        _focusLit = true;
        var enabled = _buttons.Where(button => !button.Disabled).Append(_leave).ToList();
        if (enabled.Count == 0) return;
        var focus = enabled.FindIndex(button => button.HasFocus());
        enabled[Math.Clamp(focus < 0 ? 0 : focus + direction, 0, enabled.Count - 1)].GrabFocus();
    }

    public void Confirm()
    {
        var focused = _buttons.Append(_leave).FirstOrDefault(button => button.HasFocus() && !button.Disabled);
        if (focused is not null) focused.EmitSignal(BaseButton.SignalName.Pressed);
        else if (_conversation is not { Phase: DialogConversationPhase.AwaitingSelection }) CloseRequested?.Invoke();
    }

    private void Render(string? message)
    {
        _message = message; _artDirty = true;
        foreach (var button in _buttons) { _options.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        var conversation = _conversation;
        var header = conversation is null ? _title : $"{_title} | turn {conversation.Turn} | DLG node {conversation.CurrentNode.Value}";
        Show(_header, _headerArt, header, HeaderColour);
        ApplyRowGeometry();
        if (conversation is not null)
        {
            foreach (var option in conversation.Options)
            {
                var token = option.Token;
                var button = new Button
                {
                    Name = $"Reply{option.Number}", Alignment = HorizontalAlignment.Left,
                    AutowrapMode = _originalRows ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart, Disabled = option.Block is not null,
                    FocusMode = Control.FocusModeEnum.All, ActionMode = BaseButton.ActionModeEnum.Press,
                };
                if (_originalRows) StyleBandRow(button);
                var text = AreaConversationText.OptionText(option, NumberedReplies);
                button.SetMeta(ReplyTextMeta, text);
                if (_fonts is null) button.Text = text;
                else
                {
                    var art = new AreaArtParagraph { Name = ArtName, Position = new(ButtonPadX, _originalRows ? 0 : ButtonPadY), WrapWidth = _originalRows ? NoWrap : Math.Max(1, _contentWidth - 2 * ButtonPadX) };
                    art.SetFonts(_fonts);
                    button.AddChild(art);
                    art.SetText(text, option.Block is not null ? BlockedColour : ReplyColour);
                }
                // Rebuild after the emitting button's signal returns: Render frees the buttons.
                button.Pressed += () => Callable.From(() => ChooseToken(token)).CallDeferred();
                _options.AddChild(button); _buttons.Add(button);
            }
        }
        var outcome = conversation is null ? string.Empty : AreaConversationText.Outcome(conversation);
        Show(_outcome, _outcomeArt, string.Join("\n", new[] { message, outcome }.Where(text => !string.IsNullOrEmpty(text))), OutcomeColour);
        Show(_effects, _effectsArt, conversation is null || conversation.Effects.Count == 0 ? string.Empty
            : "Effects (diagnostic ledger, newest last):\n" + string.Join("\n", conversation.Effects.TakeLast(MaxShownEffects).Select(AreaConversationText.Effect)), EffectsColour);
        var awaiting = conversation is { Phase: DialogConversationPhase.AwaitingSelection };
        var leave = awaiting ? "Leave without answering (diagnostic)" : "Close";
        _leave.SetMeta(ReplyTextMeta, leave);
        _leave.Text = _fonts is null ? leave : string.Empty;
        _leaveArt.Visible = _fonts is not null;
        _leaveArt.Position = new(ButtonPadX, ButtonPadY);
        _leaveArt.SetText(leave, ReplyColour);
        Show(_hint, _hintArt, (NumberedReplies ? "Click a reply or press its number (controller: D-pad, confirm, cancel)" : "Click a reply (controller: D-pad, confirm, cancel)") + " | PROJECT diagnostic PC/state profile; original runtime parity NOT_VERIFIED", HintColour);
        (_buttons.FirstOrDefault(button => !button.Disabled) ?? _leave).GrabFocus();
        ResizePanel();
    }

    /// <summary>Sets a text in the label (the viewer's font, in the colour as a modulate) or, with the original's fonts, in its paragraph (hidden when empty).</summary>
    private void Show(Label label, AreaArtParagraph art, string text, (byte R, byte G, byte B) colour)
    {
        label.Text = text;
        label.Modulate = new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f);
        label.Visible = _fonts is null;
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
            if (_originalRows && button != _leave) { art.WrapWidth = NoWrap; button.CustomMinimumSize = new(0, DialogReplyBand.RowHeight); continue; }
            art.WrapWidth = Math.Max(1, _contentWidth - 2 * ButtonPadX);
            button.CustomMinimumSize = new(0, art.GetMinimumSize().Y + 2 * ButtonPadY);
        }
    }

    private const int NoWrap = 100000;
    private const string LitMeta = "rowLit";
    private bool _focusLit;
    private static readonly StyleBoxFlat PlainRow = RowStyle(0f), LitRow = RowStyle(.16f);
    private static StyleBoxFlat RowStyle(float alpha) => new() { BgColor = new Color(1f, 1f, 1f, alpha), ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0 };

    /// <summary>ARC-UI-2025: the reply stack as the original's band (a 100 px tall area, rows every 19 px starting 3 px down) or as the plain stack.</summary>
    private void ApplyRowGeometry()
    {
        // The empty first child makes the stack's 3 px separation the 3 px between the band's top and the first row.
        _bandTop.Visible = _originalRows;
        _options.MouseFilter = _originalRows ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Pass;
        _options.CustomMinimumSize = new(0, _originalRows ? DialogReplyBand.Height : 0);
        if (_originalRows) _options.AddThemeConstantOverride("separation", DialogReplyBand.RowPitch - DialogReplyBand.RowHeight);
        else _options.RemoveThemeConstantOverride("separation");
        _hover = null;
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
    }

    /// <summary>The row the pointer or the keyboard focus is on draws lit (the original's second font, here a light shade).</summary>
    private void LightRows()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            // The keyboard or controller focus lights a row only once it was moved by Navigate: the original has none, and the focus the panel
            // gives the first reply on opening must not light it under a pointer that is elsewhere.
            var lit = !_buttons[i].Disabled && (_hover?.IsLit(i) == true || (_focusLit && _buttons[i].HasFocus()));
            if (_buttons[i].HasMeta(LitMeta) && _buttons[i].GetMeta(LitMeta).AsBool() == lit) continue;
            _buttons[i].SetMeta(LitMeta, lit);
            _buttons[i].AddThemeStyleboxOverride("normal", lit ? LitRow : PlainRow);
        }
    }

    /// <summary>
    /// ARC-UI-2025: a pointer event on the reply band. Original rows: a move updates the original's hover bookkeeping and a left press resolves
    /// the row by the band's rules (a gap or an empty slot is refused, a blocked reply chooses nothing). True when the band took the event.
    /// </summary>
    private bool HandleBandInput(InputEvent input)
    {
        if (!_originalRows || !IsOpen) return false;
        var width = (int)_options.Size.X;
        switch (input)
        {
            case InputEventMouseMotion motion:
                if (_hover is null || _hoverKey != (width, _buttons.Count)) { _hover = new DialogReplyBand.Hover(width, _buttons.Count); _hoverKey = (width, _buttons.Count); }
                _hover.Move((int)motion.Position.X, (int)motion.Position.Y);
                return false;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                var hit = DialogReplyBand.Press((int)press.Position.X, (int)press.Position.Y, width, _buttons.Count, clickBandsDrift: true);
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
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        var content = Math.Max(1, (int)width - 2 * ContentMargin);
        if (content != _contentWidth || _artDirty) { _contentWidth = content; _artDirty = false; LayoutArt(); }
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        // Whole pixels: the art text's glyphs are drawn pixel for pixel (MW31).
        _panel.Position = new(Mathf.Round((size.X - width) / 2), Mathf.Round(Math.Max(24, size.Y - height - 24)));
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        if (NumberedSource is { } source) NumberedReplies = source();
        if (OriginalRowsSource is { } rows) OriginalReplyRows = rows();
        if (_originalRows) LightRows();
        ResizePanel();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
