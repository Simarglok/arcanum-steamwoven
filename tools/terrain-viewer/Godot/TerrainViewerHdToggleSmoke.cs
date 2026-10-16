using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Hd;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-006 acceptance (ARC-RENDER-013, "Gameplay never reads HD"): one scripted scenario
/// is replayed from the same quick save under Legacy, under HD and with switches before and in the
/// middle of every step (by the H key while the world owns input, by the setting while a dialog is
/// open). The Session snapshots must be byte-identical and every drawn item must keep its world
/// destination. The pack is a nearest 4× pack in a temporary directory outside the repository.</summary>
public partial class TerrainViewerApp
{
    private enum VisualScenarioPlan { Legacy, Hd, Switching }

    /// <summary>One scripted step; it calls its argument where a switching run toggles mid-step.</summary>
    private sealed record VisualScenarioStep(string Name, Func<Func<Task>, Task> Act);

    private sealed record VisualScenarioRun(string Snapshot, IReadOnlyList<string> Destinations, int HdDraws, int Switches, int Commits, int PendingSwitches);

    // Restores the saved start (F8 through the production key route), then runs the steps under the plan.
    private async Task<VisualScenarioRun> RunVisualScenarioAsync(VisualScenarioPlan plan, IReadOnlyList<VisualScenarioStep> steps)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 scenario: " + why); }
        var canvas = _canvas!;
        var start = plan == VisualScenarioPlan.Hd ? VisualProfile.Hd : VisualProfile.Legacy;
        if (canvas.VisualProfile != start || canvas.PendingVisualProfile is not null) canvas.RequestVisualProfile(start);
        await SettleVisualAsync();
        QuickKey(Key.F8);
        Require(_quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 restored the start: " + _quickSaveStatus);
        var placed = _areaSession!.QueryActor();
        var map = AreaRuntimes()[placed.Map.RuntimeMap];
        AreaSmokePlace(map, map.Chart.ToChart(new Arcanum.Data.Traversal.AreaStepCell(placed.Cell.X, placed.Cell.Y)));
        await SettleVisualAsync();
        var commits = canvas.VisualCommits; var switches = 0; var hdDraws = 0; var pendingSwitches = 0;
        var destinations = new List<string>();
        async Task Toggle()
        {
            if (plan != VisualScenarioPlan.Switching) return;
            // A switch still pending from earlier in the step commits first, so every switch takes effect.
            if (canvas.PendingVisualProfile is not null) await SettleVisualAsync();
            // The production key while the world owns input; the setting's request while a dialog owns it.
            if (_worldFocused && !WorldModal && !WorldUiFocus) QuickKey(Key.H); else RequestVisualToggle("scenario");
            switches++;
            if (canvas.PendingVisualProfile is not null) pendingSwitches++;
            // One submission with the switch pending or just committed; the step goes on regardless.
            await NextFrameAsync();
        }
        foreach (var step in steps)
        {
            await Toggle();
            await step.Act(Toggle);
            await SettleVisualAsync();
            destinations.Add($"{step.Name}: {canvas.DrawnDestinationsForSelfTest()}");
            hdDraws += canvas.AreaHdStats.Hd;
        }
        return new(GameSessionSnapshotCodec.Encode(_areaSession!.Capture()), destinations, hdDraws, switches, (int)(canvas.VisualCommits - commits), pendingSwitches);
    }

    // Waits until no switch is pending and no visible frame is still decoding.
    private async Task SettleVisualAsync()
    {
        for (var wait = 0; ; wait++)
        {
            await NextFrameAsync();
            if (wait >= 1 && _canvas!.PendingVisualProfile is null && (_canvas.HdSource?.PendingCount ?? 0) == 0 && _canvas.AreaHdStats[HdFallbackReason.Pending] == 0) return;
            if (wait > 1200) throw new InvalidOperationException("ARC-UI-006 scenario: the view never settled: " + _canvas!.HdStatusText());
        }
    }

    // Frames with the left stick held, then neutral frames until the actor (and any companion) settles.
    private void AreaSmokeWalk(Vec2 stick, int heldFrames)
    {
        for (var i = 0; i < heldFrames; i++)
        {
            _areaSmokeTime++;
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .1, true, SimulationTime.At(_areaSmokeTime));
        }
        for (var i = 0; i < 400 && (_worldActor.Session!.State.Phase != MovementPhase.Settled
            || _areaCompanions?.Companions.Any(c => c.Grid.State.Phase != MovementPhase.Settled) == true); i++) AreaSmokeFrame();
    }

    /// <summary>Runs the scenario under Legacy twice (a control: the scenario itself is deterministic),
    /// under HD and with switches, and requires equal snapshots and drawn destinations. The quick save
    /// of the start goes to a temporary slot; the caller places the actor first.</summary>
    private async Task<string> AssertVisualInvarianceAsync(IReadOnlyList<VisualScenarioStep> steps, HdFrameSource source)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 invariance: " + why); }
        var canvas = _canvas!;
        var (profile, previousSource, budget) = (canvas.VisualProfile, canvas.HdSource, canvas.HdBudget);
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-save-").FullName;
        _quickSaveSlotForSelfTest = GameSessionSaveSlot.QuickSave(directory);
        try
        {
            canvas.SetVisualProfile(VisualProfile.Legacy, source, HdDeviceBudget.Ceiling(source.Scale > 1 ? source.Scale : 4));
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved the start: " + _quickSaveStatus);
            var control = await RunVisualScenarioAsync(VisualScenarioPlan.Legacy, steps);
            var legacy = await RunVisualScenarioAsync(VisualScenarioPlan.Legacy, steps);
            Require(control.Snapshot == legacy.Snapshot && control.Destinations.SequenceEqual(legacy.Destinations), "the scenario is not deterministic under Legacy alone");
            var hd = await RunVisualScenarioAsync(VisualScenarioPlan.Hd, steps);
            // Switches start with no HD texture resident, so a switch to HD is still pending while its step goes on.
            using var fresh = new HdFrameSource(source.Pack);
            canvas.SetVisualProfile(VisualProfile.Legacy, fresh, canvas.HdBudget);
            var switching = await RunVisualScenarioAsync(VisualScenarioPlan.Switching, steps);
            Require(legacy.HdDraws == 0 && hd.HdDraws > 0, $"HD was drawn only under HD: legacy {legacy.HdDraws}, hd {hd.HdDraws}");
            Require(switching.Switches == 2 * steps.Count && switching.Commits > 0 && switching.PendingSwitches > 0,
                $"switches {switching.Switches}, commits {switching.Commits}, pending across a step {switching.PendingSwitches}");
            Require(hd.Snapshot == legacy.Snapshot, "the Session snapshot differs under HD");
            Require(switching.Snapshot == legacy.Snapshot, "the Session snapshot differs with switches");
            for (var i = 0; i < steps.Count; i++)
                Require(hd.Destinations[i] == legacy.Destinations[i] && switching.Destinations[i] == legacy.Destinations[i],
                    $"drawn destinations differ after {steps[i].Name}: legacy [{legacy.Destinations[i]}] hd [{hd.Destinations[i]}] switching [{switching.Destinations[i]}]");
            return $"steps={string.Join("/", steps.Select(s => s.Name))} snapshot={legacy.Snapshot.Length} chars sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.Snapshot)))[..16].ToLowerInvariant()} "
                + $"hd-draws={hd.HdDraws} switches={switching.Switches} commits={switching.Commits} pending-mid-step={switching.PendingSwitches} last-commit={canvas.LastVisualCommit}";
        }
        finally
        {
            _quickSaveSlotForSelfTest = null; _quickKeys.Reset();
            canvas.SetVisualProfile(profile, previousSource, budget);
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    // A nearest 4× pack of every character and object frame of the loaded maps and the actor, in a
    // temporary directory; returns its frame source (the caller disposes it and deletes the root).
    private static HdFrameSource NearestPackSource(string root, IEnumerable<AreaLoadResult> areas, TerrainLoadResult result)
    {
        var frames = areas.SelectMany(area => area.Plan.Commands.Where(command => command.Identity is not null && command.ArtClass != 1).Select(command => command.Frame).Distinct()
                .Select(key => new HdNearestPack.Frame(HdFrameQueries.FromArea(key, area.Frames[key]), area.Frames[key].Rgba)))
            .GroupBy(frame => frame.Query).Select(group => group.First())
            .Concat(result.ActorTextures.Values.Where(texture => texture.HdQuery is not null).Select(texture => new HdNearestPack.Frame(texture.HdQuery!, texture.Rgba)))
            .ToArray();
        var open = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, "pack"), "ui006-nearest-4x", 4, frames));
        if (open.Status != HdPackOpenStatus.Opened) throw new InvalidOperationException("ARC-UI-006 pack " + open.Status);
        return new HdFrameSource(open.Pack);
    }

    /// <summary>Native smoke on the installed original (after the follow phase): talk to Virgil, walk,
    /// wait one game hour, enter the cave; under Legacy, HD and with switches.</summary>
    private async Task RunAreaVisualToggleSmokeAsync(TerrainLoadResult result)
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-smoke-").FullName;
        HdFrameSource? source = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (_areaTransitions?.CurrentMap != 1 || _conversationHost is null || _areaAmbient is null || _caveLoad is null)
                throw new InvalidOperationException("ARC-UI-006 smoke: the exterior with dialog, lighting and the cave is required");
            source = NearestPackSource(root, [_exteriorArea!, _caveLoad.Area], result);
            var map = _areaTransitions.Current;
            var speaker = _conversationHost.Speakers.Single(s => AreaCompanionRoster.For(s) is not null);
            // Start next to Virgil where he stands now (an earlier phase may have moved him).
            var virgil = _areaCompanions?.Companions.SingleOrDefault()?.Grid.State.CommittedCell ?? map.Chart.ToChart(new(speaker.WorldX, speaker.WorldY));
            AreaSmokePlace(map, AreaSmokeStandable(map, virgil, 2));
            var host = 1_000_000L;
            var steps = new VisualScenarioStep[]
            {
                new("dialog", async toggle =>
                {
                    BeginConversation(speaker);
                    var conversation = _conversationPanel!.Conversation ?? throw new InvalidOperationException("ARC-UI-006 smoke: no conversation");
                    await toggle();
                    for (var i = 0; i < 64 && conversation.Phase == Arcanum.Scripting.Dialog.DialogConversationPhase.AwaitingSelection; i++)
                    {
                        var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == Arcanum.Scripting.Dialog.DialogOptionTransition.Direct)
                            ?? conversation.Options.First(o => o.Block is null);
                        conversation.Select(option.Token);
                    }
                    EndConversation();
                }),
                new("walk", async toggle =>
                {
                    AreaSmokeWalk(new Vec2(1, 0), 6); await toggle(); AreaSmokeWalk(new Vec2(0, 1), 6);
                }),
                new("hour", async toggle =>
                {
                    PaletteGameHour(); await toggle();
                    for (var i = 0; i < 8; i++) TickAreaClock(host += 250);
                }),
                new("cave", async toggle =>
                {
                    await UseNextToTriggerAsync(79); await toggle();
                    for (var i = 0; i < 3; i++) AreaSmokeFrame();
                }),
            };
            var report = await AssertVisualInvarianceAsync(steps, source);
            // Leave the exterior published for any later stage.
            if (_areaTransitions.CurrentMap != 1) await UseNextToTriggerAsync(1);
            GD.Print("ARC-UI-006 native-smoke Session invariance PASS " + report + " parity=NOT_VERIFIED");
        }
        finally
        {
            if (ConversationOpen) EndConversation();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; _worldTestTime = testTime;
            source?.Dispose();
            System.IO.Directory.Delete(root, true);
        }
    }

    /// <summary>The synthetic counterpart for the self-test: the H key route and palette action, the
    /// saved request, and Session invariance over two walks with switches. Returns a summary.</summary>
    internal async Task<string> VisualToggleForSelfTestAsync(TerrainLoadResult result)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-006 app self-test: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null || _canvas is null) throw new InvalidOperationException("ARC-UI-006 app self-test: a published area is required");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-006-app-").FullName;
        var previousPath = OS.GetEnvironment(VisualProfileSettingsStore.TestVariable);
        HdFrameSource? source = null;
        try
        {
            var path = System.IO.Path.Combine(root, "visual.json");
            OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, path);
            _visualStore = new VisualProfileSettingsStore(automated: true);
            source = NearestPackSource(root, [_readyArea], result);
            _canvas.SetVisualProfile(VisualProfile.Legacy, source, HdDeviceBudget.Ceiling(4));
            var map = AreaRuntimes()[_readyArea.Snapshot.RuntimeMap];
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);

            // H through the production key route requests HD and saves it at once; the commit follows.
            var generation = _canvas.VisualGeneration;
            QuickKey(Key.H);
            Require(_canvas.VisualGeneration == generation + 1 && (_canvas.PendingVisualProfile == VisualProfile.Hd || _canvas.VisualProfile == VisualProfile.Hd), "H requested HD");
            Require(System.IO.File.ReadAllText(path) == VisualProfileCodec.Serialize(VisualProfile.Hd), "the request was saved at once");
            await SettleVisualAsync(); RefreshReadyStatus();
            Require(_canvas.VisualProfile == VisualProfile.Hd && _canvas.AreaHdStats.Hd > 0, "HD committed: " + _canvas.HdStatusText());
            Require(GetSelfTestSnapshot().Hud.Contains("H visual Legacy/HD: HD; pack ui006-nearest-4x 4x", StringComparison.Ordinal), "HUD names the profile and pack");
            // Modifiers and the palette: Ctrl+H is not the toggle; under the palette H is ignored; the palette action toggles.
            InputWorld(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true, CtrlPressed = true });
            InputWorld(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false, CtrlPressed = true });
            Require(_canvas.VisualGeneration == generation + 1, "Ctrl+H toggled");
            ToggleWorldSettings();
            Require(WorldModal, "palette opened");
            QuickKey(Key.H);
            Require(_canvas.VisualGeneration == generation + 1 && _visualNotice?.StartsWith("H ignored", StringComparison.Ordinal) == true, "H under the palette was not ignored");
            var visual = (Label)_worldPanel!.FindChild("PaletteVisual", true, false)!;
            var action = _worldPanel.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Visual Legacy/HD (H)");
            action.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_canvas.VisualProfile == VisualProfile.Legacy && System.IO.File.ReadAllText(path) == VisualProfileCodec.Serialize(VisualProfile.Legacy),
                "the palette action switched to Legacy and saved it");
            Require(visual.Text.StartsWith("Visual: Legacy; pack ui006-nearest-4x 4x", StringComparison.Ordinal), "palette line: " + visual.Text);
            ToggleWorldSettings();
            // The request during a pending switch cancels it (newer generation wins), through the same key.
            var pendingSource = NearestPackSource(System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "fresh")).FullName, [_readyArea], result);
            try
            {
                _canvas.SetVisualProfile(VisualProfile.Legacy, pendingSource, HdDeviceBudget.Ceiling(4));
                AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                QuickKey(Key.H);
                var pending = _canvas.PendingVisualProfile == VisualProfile.Hd;
                QuickKey(Key.H);
                Require(!pending || (_canvas.PendingVisualProfile is null && _canvas.VisualProfile == VisualProfile.Legacy), "the second H cancelled the pending switch");
            }
            finally { _canvas.SetVisualProfile(VisualProfile.Legacy, source, HdDeviceBudget.Ceiling(4)); pendingSource.Dispose(); }

            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            var steps = new VisualScenarioStep[]
            {
                new("walk-right", async toggle => { AreaSmokeWalk(new Vec2(1, 0), 4); await toggle(); AreaSmokeWalk(new Vec2(1, 1), 4); }),
                new("walk-back", async toggle => { AreaSmokeWalk(new Vec2(-1, 0), 4); await toggle(); AreaSmokeWalk(new Vec2(0, -1), 4); }),
            };
            return "H/Ctrl+H/palette/cancel routed, request saved; " + await AssertVisualInvarianceAsync(steps, source);
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            if (string.IsNullOrEmpty(previousPath)) OS.UnsetEnvironment(VisualProfileSettingsStore.TestVariable);
            else OS.SetEnvironment(VisualProfileSettingsStore.TestVariable, previousPath);
            _visualStore = null;
            _canvas.SetVisualProfile(VisualProfile.Legacy, null, null);
            source?.Dispose();
            System.IO.Directory.Delete(root, true);
        }
    }
}
