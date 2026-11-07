using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-SCRIPT-005 diagnostic conversation surface: the NPC line, up to five replies (mouse or controller menu), the
/// outcome and the recent typed effects. It renders a <see cref="DialogConversation"/>; it owns no dialog rules. ARC-UI-071 (B29): with the
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
        HintColour = (140, 153, 166), LineColour = (255, 255, 255), ReplyColour = (223, 223, 223), BlockedColour = (111, 111, 111);
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _npcLine = null!;
    private VBoxContainer _options = null!;
    private Label _outcome = null!;
    private Label _effects = null!;
    private Label _hint = null!;
    private readonly List<Button> _buttons = new();
    private Button _leave = null!;
    private DialogConversation? _conversation;
    private string _title = string.Empty;
    private string? _message;
    // ARC-UI-071: the original's text fonts (null: the viewer's font) and the paragraphs that draw with them beside each label.
    private AreaTextFonts? _fonts;
    private AreaArtParagraph _headerArt = null!, _npcLineArt = null!, _outcomeArt = null!, _effectsArt = null!, _hintArt = null!, _leaveArt = null!;
    private int _contentWidth = 640;
    private bool _artDirty;
    public event Action? CloseRequested;
    /// <summary>ARC-UI-043: a reply the PC picked was processed (whatever it did to the dialog).</summary>
    public event Action? Picked;
    public bool IsOpen => Visible;
    public DialogConversation? Conversation => _conversation;
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    internal string NpcLineForSelfTest => _npcLine.Text;
    internal string OutcomeForSelfTest => _outcome.Text;
    internal Button LeaveForSelfTest => _leave;
    /// <summary>Whether the original's font art draws the panel's texts (false: the viewer's font).</summary>
    internal bool ArtFontsForSelfTest => _fonts is not null;
    /// <summary>The text a reply button shows, in either font.</summary>
    internal static string ReplyTextForSelfTest(Button button) => button.HasMeta(ReplyTextMeta) ? button.GetMeta(ReplyTextMeta).AsString() : button.Text;
    /// <summary>The panel's art-font paragraphs: (name, text, lines drawn, wrap width, visible).</summary>
    internal IReadOnlyList<(string Name, string Text, int Lines, int Wrap, bool Visible)> ArtTextsForSelfTest()
    {
        var found = new List<(string, string, int, int, bool)>();
        foreach (var (name, art) in new[] { ("header", _headerArt), ("npc", _npcLineArt), ("outcome", _outcomeArt), ("effects", _effectsArt), ("hint", _hintArt), ("leave", _leaveArt) })
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
        _npcLine = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _options = new VBoxContainer();
        _outcome = new Label { Modulate = new Color(1f, .85f, .55f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _effects = new Label { Modulate = new Color(.6f, .8f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _hint = new Label { Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _leave = new Button { Name = "Leave", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _leave.Pressed += Leave;
        _headerArt = Paragraph("HeaderArt"); _npcLineArt = Paragraph("NpcLineArt"); _outcomeArt = Paragraph("OutcomeArt");
        _effectsArt = Paragraph("EffectsArt"); _hintArt = Paragraph("HintArt");
        _leaveArt = Paragraph(ArtName); _leave.AddChild(_leaveArt);
        foreach (var control in new Control[] { _header, _headerArt, _npcLine, _npcLineArt, _options, _outcome, _outcomeArt, _effects, _effectsArt, _leave, _hint, _hintArt })
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
        foreach (var art in new[] { _headerArt, _npcLineArt, _outcomeArt, _effectsArt, _hintArt, _leaveArt }) art.SetFonts(fonts);
        if (IsNodeReady() && IsOpen) Render(_message);
    }

    /// <summary>Shows a conversation, or only a message when none could start.</summary>
    public void Open(string title, DialogConversation? conversation, string? message = null)
    {
        _title = title; _conversation = conversation; _message = message;
        Show(); Render(message);
    }

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

    /// <summary>Selects exactly the reply the button showed: a token from older offers is NotOffered.</summary>
    private void ChooseToken(DialogOptionToken token)
    {
        if (_conversation is not { Phase: DialogConversationPhase.AwaitingSelection } conversation) return;
        var result = conversation.Select(token);
        if (result.Status == DialogConversationSelectStatus.NotOffered) return;
        if (result.Status is not (DialogConversationSelectStatus.UnsupportedTransition or DialogConversationSelectStatus.StaleSelection
            or DialogConversationSelectStatus.WrongPhase)) Picked?.Invoke();
        Render(result.Status is DialogConversationSelectStatus.UnsupportedTransition or DialogConversationSelectStatus.StaleSelection
            ? $"[{result.Status}: the reply was not executed]" : null);
        // ARC-TECHNOLOGY-006: SU ends the dialog at once for the schematic window, with no further NPC line (ledger NC1–NC2); ARC-UI-050:
        // b: for the barter window (BT7); ARC-UI-088: c-e for the NPC's sheet (WN2).
        if (conversation.End?.Kind is DialogEndKind.SchematicWindow or DialogEndKind.BarterWindow or DialogEndKind.NpcSheetWindow) CloseRequested?.Invoke();
    }

    /// <summary>Diagnostic leave: closes an awaiting dialog without a reply effect, or dismisses a finished one.</summary>
    public void Leave()
    {
        _conversation?.Close();
        CloseRequested?.Invoke();
    }

    public void Navigate(int direction)
    {
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
        Show(_npcLine, _npcLineArt, conversation?.NpcLine?.Text ?? string.Empty, LineColour);
        if (conversation is not null)
        {
            foreach (var option in conversation.Options)
            {
                var token = option.Token;
                var button = new Button
                {
                    Name = $"Reply{option.Number}", Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, Disabled = option.Block is not null, FocusMode = Control.FocusModeEnum.All,
                };
                var text = AreaConversationText.Option(option);
                button.SetMeta(ReplyTextMeta, text);
                if (_fonts is null) button.Text = text;
                else
                {
                    var art = new AreaArtParagraph { Name = ArtName, Position = new(ButtonPadX, ButtonPadY), WrapWidth = Math.Max(1, _contentWidth - 2 * ButtonPadX) };
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
        Show(_hint, _hintArt, "Click a reply (controller: D-pad, confirm, cancel) | PROJECT diagnostic PC/state profile; original runtime parity NOT_VERIFIED", HintColour);
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
        foreach (var art in new[] { _headerArt, _npcLineArt, _outcomeArt, _effectsArt, _hintArt }) art.WrapWidth = _contentWidth;
        foreach (var button in _buttons.Append(_leave))
        {
            if (button.GetNodeOrNull<AreaArtParagraph>(ArtName) is not { } art) continue;
            art.WrapWidth = Math.Max(1, _contentWidth - 2 * ButtonPadX);
            button.CustomMinimumSize = new(0, art.GetMinimumSize().Y + 2 * ButtonPadY);
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

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
