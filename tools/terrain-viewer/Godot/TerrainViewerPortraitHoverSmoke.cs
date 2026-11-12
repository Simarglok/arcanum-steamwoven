using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1281 (ledger HK26) native observation of the follower portraits' hover, in the "hover" stage (a free companion of the shown map joins the party and
// comes up to the hero) and in the "companion-click" stage (the joined companion it already has). With the pointer moved by motion events (the production
// path: the strip's button sends the entry and the exit):
//  1. entering a portrait with no menu and no dialog runs the follower's examine once (the pointer moving inside the portrait runs nothing more) and, with the
//     interface bars drawn, shows the follower's window in the message window;
//  2. leaving it clears the hover key and the window;
//  3. entering with the order menu open runs nothing;
// (the dialog case is the companion-click stage's, where a dialog is open: a hover then runs nothing).
public partial class TerrainViewerApp
{
    /// <summary>The facts of the portrait hover of <paramref name="follower"/> (its portrait is shown): (name, as the original, what was seen).</summary>
    private async Task<List<(string Name, bool Original, string Seen)>> ObservePortraitHoverAsync(string follower)
    {
        var facts = new List<(string, bool, string)>();
        var portraits = _followerPortraits ?? throw new InvalidOperationException("ARC-UI-1281 smoke: no portrait strip");
        var companion = _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == follower) ?? throw new InvalidOperationException("ARC-UI-1281 smoke: no companion");
        var at = portraits.PortraitCentreForSelfTest(follower) ?? throw new InvalidOperationException("ARC-UI-1281 smoke: no portrait");
        var away = GetViewport().GetVisibleRect().Size * .5f;
        async Task MoveAsync(Vector2 to)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to });
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        void Fact(string name, bool original, string seen) => facts.Add((name, original, seen));
        // ARC_AREA_HOVER_WINDOW_CAPTURE: the before and after readbacks for the thread's composite (never committed).
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_HOVER_WINDOW_CAPTURE");
        async Task SaveAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng($"{capture}-portrait-{name}.png") != Error.Ok) throw new InvalidOperationException("ARC-UI-1281 smoke: capture failed");
        }
        var bars = _gameBarPanel is { Visible: true };
        portraits.CloseMenu();
        await MoveAsync(away);
        Fact("the pointer off the portraits: no hover", portraits.HoveredForSelfTest is null && PortraitHoverKeyForSelfTest is null, $"{portraits.HoveredForSelfTest}/{PortraitHoverKeyForSelfTest}");

        // 1. Entering the portrait runs the examine once.
        await SaveAsync("before");
        var before = PortraitHoversForSelfTest;
        await MoveAsync(at);
        await SaveAsync("after");
        Fact("entering the portrait hovers the follower", portraits.HoveredForSelfTest == follower && PortraitHoverKeyForSelfTest == companion.Key,
            $"{portraits.HoveredForSelfTest} key {PortraitHoverKeyForSelfTest}");
        Fact("... and runs the examine once", PortraitHoversForSelfTest == before + 1, $"{PortraitHoversForSelfTest - before} run(s)");
        await MoveAsync(at + new Vector2(1, 1));
        Fact("... the pointer moving inside the portrait runs nothing more", PortraitHoversForSelfTest == before + 1, $"{PortraitHoversForSelfTest - before} run(s)");
        if (bars)
        {
            var window = HoverWindowForSelfTest;
            Fact("... the follower's window shows in the message window", window is not null && ReferenceEquals(_messageLog.Shown, window), window is null ? "none" : "shown");
        }
        else Fact("... the bars are off: the script ran, no window", HoverWindowForSelfTest is null, "bars off");

        // 2. Leaving clears.
        await MoveAsync(away);
        Fact("leaving the portrait clears the hover", portraits.HoveredForSelfTest is null && PortraitHoverKeyForSelfTest is null && HoverWindowForSelfTest is null,
            $"{portraits.HoveredForSelfTest}/{PortraitHoverKeyForSelfTest}/{(HoverWindowForSelfTest is null ? "no window" : "window")}");

        // 3. With the order menu open, entering the portrait runs nothing.
        // The menu opens by the right release over the portrait (it stands right of the frame, not over it), then the pointer goes away and enters again.
        foreach (var down in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = down, Position = at, GlobalPosition = at });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Fact("the right release opened the order menu", portraits.MenuShown, portraits.MenuShown ? "menu" : "none");
        await MoveAsync(away);
        before = PortraitHoversForSelfTest;
        await MoveAsync(at);
        Fact("entering with the order menu open runs nothing (the pointer is on the portrait)", portraits.HoveredForSelfTest == follower && PortraitHoversForSelfTest == before,
            $"{portraits.HoveredForSelfTest} {PortraitHoversForSelfTest - before} run(s)");
        portraits.CloseMenu();
        await MoveAsync(away);

        return facts;
    }

    /// <summary>The "hover" stage's part: a free companion of the shown map joins, comes up to the hero, and its portrait's hover is observed; he leaves
    /// the party again. Returns the summary, or "none" when the map has no free companion (not a failure).</summary>
    private async Task<string> PortraitHoverStageAsync(GameSessionOwner session, Action<bool, string> require)
    {
        if (_conversationHost is not { PartyWired: true } || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid) return "none (no party)";
        var map = _areaTransitions?.Current ?? new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker) && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free);
        if (template is null) return "none (no free companion on the map)";
        var follower = template.Entity;
        var joined = false;
        try
        {
            var join = session.ForceJoinParty(session.Version, follower);
            require(join.Disposition != GameSessionDisposition.Rejected, "portrait: the companion joined: " + join.Failure);
            joined = true;
            AreaSmokePlace(map, AreaSmokeStandable(map, template.HomeOrigin, 1));
            for (var f = 0; f < 600 && _followerPortraits?.EntitiesForSelfTest.Contains(follower) != true; f++) AreaSmokeFrame();
            require(_followerPortraits?.EntitiesForSelfTest.Contains(follower) == true, "portrait: his portrait is shown: " + _areaCompanions?.Status());
            var facts = await ObservePortraitHoverAsync(follower);
            var failed = facts.Where(f => !f.Original).Select(f => $"{f.Name} ({f.Seen})").ToArray();
            require(failed.Length == 0, "portrait hover not as the original's (ledger HK26): " + string.Join("; ", failed));
            return $"portrait={facts.Count}facts";
        }
        finally
        {
            _followerPortraits?.CloseMenu();
            if (joined) session.LeaveParty(session.Version, follower, new PartyLeave(Force: true));
            BindAreaCompanions();
            PublishWorldSelection();
        }
    }
}
