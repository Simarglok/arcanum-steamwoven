using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1112 native observation (a step of the bar smoke, ARC_AREA_MAP_SMOKE_ONLY=bar): after a new game as the first pre-made hero the Fate Points
    // button opens the window at (0, 41) of the original's screen; a press on a button arms its kind (a point is spent, sound 3000), the next press undoes
    // it (the point back, 3001), a press with no point left pops back, the full heal pops up at once, a kind an event used pops up, Space closes the
    // window and the key F toggles it.
    // With ARC_AREA_BAR_CAPTURE=PREFIX the smoke saves the whole view before the window opens (PREFIX-fate-before.png) and with two kinds armed
    // (PREFIX-fate-after.png): ignored local files, original-derived, for the graphics report; never committed.
    private void CaptureFate(string name)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        var path = System.IO.Path.GetFullPath(capture) + $"-fate-{name}.png";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1112 fate capture not saved: " + path);
        GD.Print($"ARC-UI-1112 fate capture {name} {image.GetWidth()}x{image.GetHeight()}");
    }

    private async Task RunAreaFateSmokeAsync(AreaGameBarPanel panel, Func<int, Task> frames, Func<Vector2, Task> move, Func<Vector2, bool, Task> button,
        Action<Key> key, Func<AreaBarButton, Task> click)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-1112 fate smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-1112 fate smoke: " + why); } }
        var session = _areaSession ?? throw new InvalidOperationException("ARC-UI-1112 fate smoke: no session");
        if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await frames(2); }
        Require(!FateOpen, "the window starts closed");
        for (var i = 0; i < 3; i++) Require(session.GrantHeroFatePoint(session.Version).Disposition == GameSessionDisposition.Changed, "a point was granted");
        int Points() => session.QueryHeroFate()?.Points ?? -1;
        var start = Points();
        Require(start >= 3, "the hero has points: " + start);

        await move(Vector2.Zero);
        await frames(2);
        CaptureFate("before");
        await click(AreaBarButton.FatePoints);
        await frames(2);
        Require(FateOpen && FateViewForSelfTest is { ArtSet: true } view, "the Fate Points button opened the window: " + _gameBarNotice);
        var rect = view.WindowRectForSelfTest;
        Require(Math.Abs(rect.Position.X - AreaGameBarPanel.ScreenLeft(GetViewport().GetVisibleRect().Size, panel.Factor)) < 1
            && Math.Abs(rect.Position.Y - AreaFateLayout.ScreenY * panel.Factor) < 1 && rect.Size.X > 0 && rect.Size.Y > 0, "the window stands at (0, 41) of the screen: " + rect);
        Require(Enumerable.Range(0, AreaFateLayout.Kinds).All(kind => view.FrameForSelfTest(kind) == 0 && !view.DownForSelfTest(kind)), "every button starts idle");
        var sounds = () => _gameBarSoundsForSelfTest.Count(sound => sound.Id == AreaGameBarLayout.PressSound && sound.Started);
        var releases = () => _gameBarSoundsForSelfTest.Count(sound => sound.Id == AreaGameBarLayout.ReleaseSound && sound.Started);

        async Task Press(int kind)
        {
            var at = view.ButtonCentreForSelfTest(kind);
            await move(at); await button(at, true); await button(at, false); await frames(1);
        }

        var hover = view.ButtonCentreForSelfTest((int)FateKind.CriticalHit);
        await move(hover);
        Require(view.HoveredForSelfTest == (int)FateKind.CriticalHit, "the pointer over a button lights it");
        var logBefore = FateLogForSelfTest.Count;
        var pressBefore = sounds();

        await Press((int)FateKind.CriticalHit);
        Require(session.HeroFateArmed(FateKind.CriticalHit) && Points() == start - 1 && view.DownForSelfTest((int)FateKind.CriticalHit), "a press armed critical hit and spent a point");
        Require(FateLogForSelfTest.Count == logBefore + 1 && FateLogForSelfTest[^1] == ((int)FateKind.CriticalHit, true, GameSessionDisposition.Changed, GameSessionFailure.None),
            "the press ran one arm: " + string.Join(";", FateLogForSelfTest.Skip(logBefore)));
        Require(sounds() == pressBefore + 1, "turning a button on played 3000");
        Require(view.FrameForSelfTest((int)FateKind.CriticalHit) != 0, "an armed button shows another frame than idle");

        await Press((int)FateKind.Gambling);
        Require(session.HeroFateArmed(FateKind.Gambling) && Points() == start - 2, "a second kind armed");
        await move(view.ButtonCentreForSelfTest((int)FateKind.CriticalMiss));
        await frames(2);
        CaptureFate("after");

        var releaseBefore = releases();
        await Press((int)FateKind.FullHeal);
        Require(Points() == start - 3 && !view.DownForSelfTest((int)FateKind.FullHeal) && !session.HeroFateArmed(FateKind.FullHeal), "the full heal was used at once and its button popped up");

        // Spend what is left, then a press with no point refuses and pops back.
        for (var kind = (int)FateKind.Heal; kind < AreaFateLayout.Kinds && Points() > 0; kind++) await Press(kind);
        Require(Points() == 0, "the points ran out");
        var refusedBefore = FateLogForSelfTest.Count;
        await Press((int)FateKind.ForceGoodReaction);
        var refused = FateLogForSelfTest[^1];
        Require(FateLogForSelfTest.Count == refusedBefore + 1 && refused.Arm && refused.Disposition == GameSessionDisposition.Rejected && refused.Failure == GameSessionFailure.FateRefused,
            "a press with no point left was refused: " + refused);
        Require(!view.DownForSelfTest((int)FateKind.ForceGoodReaction) && !session.HeroFateArmed(FateKind.ForceGoodReaction) && Points() == 0, "the refused button popped back and nothing was spent");

        await Press((int)FateKind.CriticalHit);
        Require(!session.HeroFateArmed(FateKind.CriticalHit) && Points() == 1 && !view.DownForSelfTest((int)FateKind.CriticalHit), "a second press undid critical hit and gave its point back");
        Require(FateLogForSelfTest[^1] == ((int)FateKind.CriticalHit, false, GameSessionDisposition.Changed, GameSessionFailure.None), "the press ran one undo");
        Require(releases() > releaseBefore, "turning a button off played 3001");

        // An event that uses an armed kind pops its button.
        Require(session.HeroFateArmed(FateKind.Gambling), "gambling is still armed");
        Require(session.UseHeroFate(session.Version, FateKind.Gambling).Disposition == GameSessionDisposition.Changed, "an event used gambling");
        await frames(2);
        Require(!view.DownForSelfTest((int)FateKind.Gambling) && Points() == 1, "the used kind's button popped up, no point back");

        key(Godot.Key.Space);
        await frames(1);
        Require(!FateOpen, "Space closed the window");

        key(Godot.Key.F);
        await frames(1);
        Require(FateOpen && view.DownForSelfTest((int)FateKind.Heal) == session.HeroFateArmed(FateKind.Heal), "the key F opened the window with the buttons set from the hero: " + _gameBarNotice);
        key(Godot.Key.F);
        await frames(1);
        Require(!FateOpen, "the key F closed it");

        await click(AreaBarButton.FatePoints);
        await frames(1);
        Require(FateOpen, "the button opened it again");
        await click(AreaBarButton.FatePoints);
        await frames(1);
        Require(!FateOpen, "a second press closed it");
        await move(Vector2.Zero);
        GD.Print($"ARC-UI-1112 native-smoke fate PASS points={start}->{Points()} arms={FateLogForSelfTest.Count(entry => entry.Arm)} undos={FateLogForSelfTest.Count(entry => !entry.Arm)}");
    }
}
