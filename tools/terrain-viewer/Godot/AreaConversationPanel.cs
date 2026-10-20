using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-SCRIPT-005 diagnostic conversation surface: the NPC line, up to five replies (mouse or controller menu), the
/// outcome and the recent typed effects. It renders a <see cref="DialogConversation"/>; it owns no dialog rules.
/// </summary>
public partial class AreaConversationPanel : CanvasLayer
{
    public const int MaxShownEffects = 6;
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
    public event Action? CloseRequested;
    /// <summary>ARC-UI-043: a reply the PC picked was processed (whatever it did to the dialog).</summary>
    public event Action? Picked;
    public bool IsOpen => Visible;
    public DialogConversation? Conversation => _conversation;
    internal IReadOnlyList<Button> ButtonsForSelfTest => _buttons;
    internal string NpcLineForSelfTest => _npcLine.Text;
    internal string OutcomeForSelfTest => _outcome.Text;
    internal Button LeaveForSelfTest => _leave;

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
        foreach (var control in new Control[] { _header, _npcLine, _options, _outcome, _effects, _leave, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    /// <summary>Shows a conversation, or only a message when none could start.</summary>
    public void Open(string title, DialogConversation? conversation, string? message = null)
    {
        _title = title; _conversation = conversation;
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
        foreach (var button in _buttons) { _options.RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        var conversation = _conversation;
        _header.Text = conversation is null ? _title : $"{_title} | turn {conversation.Turn} | DLG node {conversation.CurrentNode.Value}";
        _npcLine.Text = conversation?.NpcLine?.Text ?? string.Empty;
        if (conversation is not null)
        {
            foreach (var option in conversation.Options)
            {
                var token = option.Token;
                var button = new Button
                {
                    Name = $"Reply{option.Number}", Text = AreaConversationText.Option(option), Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, Disabled = option.Block is not null, FocusMode = Control.FocusModeEnum.All,
                };
                // Rebuild after the emitting button's signal returns: Render frees the buttons.
                button.Pressed += () => Callable.From(() => ChooseToken(token)).CallDeferred();
                _options.AddChild(button); _buttons.Add(button);
            }
        }
        var outcome = conversation is null ? string.Empty : AreaConversationText.Outcome(conversation);
        _outcome.Text = string.Join("\n", new[] { message, outcome }.Where(text => !string.IsNullOrEmpty(text)));
        _effects.Text = conversation is null || conversation.Effects.Count == 0 ? string.Empty
            : "Effects (diagnostic ledger, newest last):\n" + string.Join("\n", conversation.Effects.TakeLast(MaxShownEffects).Select(AreaConversationText.Effect));
        var awaiting = conversation is { Phase: DialogConversationPhase.AwaitingSelection };
        _leave.Text = awaiting ? "Leave without answering (diagnostic)" : "Close";
        _hint.Text = "Click a reply (controller: D-pad, confirm, cancel) | PROJECT diagnostic PC/state profile; original runtime parity NOT_VERIFIED";
        (_buttons.FirstOrDefault(button => !button.Disabled) ?? _leave).GrabFocus();
        ResizePanel();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, size.Y - height - 24));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
