using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;
using System.IO;

namespace Arcanum.TerrainViewer;

// ARC-DEV-2270: a close request (Cmd+Q on macOS, Alt+F4, the window's close button) asks the original's quit question before the game ends:
// the yes/no dialog box with message 5100 of mes\MainMenu.mes (the original's window procedure turns WM_CLOSE into that question, ledger
// SF3/DB1, TL4). It is asked over the launcher, the main menu, the game and every window. Y or Return quits, N or Escape goes on. A held Cmd+Q
// (the key repeats as a stream of close requests, QuitRequestGate) quits at once while the launcher's "Hold Cmd+Q to quit" box is on (the
// default). Only an interactive window asks: a headless run, a self-test and the native smokes keep Godot's default (they are stopped by
// the harness; the quit smoke drives HandleCloseRequest itself). The owner's QA overlay key is Ctrl+Q, not this one (owner play-test 2026-11-15,
// issue #2270: the game closed on a Cmd+Q).
public partial class TerrainViewerApp
{
    private readonly QuitRequestGate _quitGate = new();
    private AreaQuitPanel? _quitPanel;
    private bool _quitIntercept;
    private Control? _quitFocus;
    private AreaMainMenuTexts? _quitTexts;
    private string? _quitTextsModule;
    /// <summary>The smoke's stand-in for ending the process and for the millisecond clock.</summary>
    private Action<string>? _quitForSelfTest;
    private Func<long>? _quitClockForSelfTest;
    private bool QuitAsking => _quitPanel?.Asking == true;

    /// <summary>Takes the window's close requests over from Godot (which would end the game at once) when this run is interactive.</summary>
    private void InstallQuitRequest()
    {
        if (ViewerAutomatedStorage.Automated || SelfTestBootstrap || DisplayServer.GetName() == "headless") return;
        _quitIntercept = true;
        GetTree().AutoAcceptQuit = false;
        QuitPanel();   // built now: a node cannot be added from inside a notification
        GD.Print("ARC-DEV-2270 close requests ask the quit question");
    }

    private void QuitNotification(int what)
    {
        // Deferred: the request arrives inside a notification, where the scene tree is busy (the box moves in the tree to stay on top).
        if (what == NotificationWMCloseRequest && _quitIntercept) Callable.From(HandleCloseRequest).CallDeferred();
    }

    /// <summary>One close request: the first asks the question, a stream of repeats that lasts quits at once (when the setting is on).</summary>
    private void HandleCloseRequest()
    {
        if (_exiting) return;
        var decision = _quitGate.Request(_quitClockForSelfTest?.Invoke() ?? (long)Time.GetTicksMsec(), _launcherSettings.HoldToQuit);
        if (decision == QuitDecision.Quit) { QuitNow("held key"); return; }
        if (decision != QuitDecision.Ask) return;
        try
        {
            var panel = QuitPanel();
            _quitFocus = GetViewport().GuiGetFocusOwner();
            _dragging = false;
            // A held key must not act on its release afterwards, and a walk under the box stops (as under the bug report).
            if (IsWorld && _worldInitialized) { QuarantineWorld(); _quickKeys.Reset(); StopWorldRoute("QUIT_QUESTION"); }
            // The dialog art follows the shown module (the box was built at the launcher, before any game folder), and the box stays the
            // topmost child so that its keys are read before any other panel's.
            if (DialogBoxArt() is { } art) panel.SetDialogArt(art);
            MoveChild(panel, -1);
            panel.Ask(QuitTextsNow().Text(AreaMainMenuTexts.QuitQuestion));
            GD.Print("ARC-DEV-2270 quit question asked");
            if (IsWorld && _worldInitialized) RefreshReadyStatus();
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException or ArgumentException or IOException)
        {
            // The question could not be shown: the player asked to close the window, so it closes rather than stay unclosable.
            GD.Print("ARC-DEV-2270 quit question failed: " + error.GetType().Name);
            _quitGate.Answered();
            QuitNow("question failed");
        }
    }

    private AreaQuitPanel QuitPanel()
    {
        if (_quitPanel is not null) return _quitPanel;
        _quitPanel = new AreaQuitPanel { Name = "QuitPanel" };
        AddChild(_quitPanel);
        _quitPanel.AnswerRequested += AnswerQuit;
        return _quitPanel;
    }

    private AreaMainMenuTexts QuitTextsNow()
    {
        var module = SelfTestBootstrap ? null : _arguments?.ModulePath;
        if (_quitTexts is null || module != _quitTextsModule)
        {
            _quitTextsModule = module;
            _quitTexts = module is null ? AreaMainMenuTexts.Project("no game folder yet") : AreaMainMenuTexts.Load(module);
        }
        return _quitTexts;
    }

    private void AnswerQuit(bool yes)
    {
        if (!QuitAsking) return;
        if (yes) { QuitNow("yes"); return; }
        GD.Print("ARC-DEV-2270 quit question answered no");
        _quitPanel!.Dismiss(); _quitGate.Answered();
        _dragging = false;
        if (_quitFocus is { } focus && IsInstanceValid(focus) && focus.IsVisibleInTree()) focus.GrabFocus();
        _quitFocus = null;
        if (IsWorld && _worldInitialized) { QuarantineWorld(); _quickKeys.Reset(); RefreshReadyStatus(); }
    }

    private void QuitNow(string how)
    {
        GD.Print("ARC-DEV-2270 quit " + how);
        _quitPanel?.Dismiss();
        if (_quitForSelfTest is { } stand) { stand(how); return; }
        GetTree().Quit(0);
    }
}
