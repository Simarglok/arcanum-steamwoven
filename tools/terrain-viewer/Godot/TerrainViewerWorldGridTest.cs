using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Actual adapter/callback/notification/frame closure, with literal independent
    // D8 expectations. The scripted receipt clock never derives from frame delta.
    internal void WorldGridObservationMatrixForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("GRID observation: " + why); }
        var none = new RawInputFrame(-1, default);
        var observed = none;
        // The matrix runs on whichever movement profile the publication installed (WORLD bounded or AREA whole-core).
        var original = _worldActor.Session!;
        void Configure(SimulationTime time, StepDuration? duration = null)
        {
            if (original is AreaGridSession area) _worldActor.Configure(area.Snapshot, time, duration);
            else _worldActor.Configure(((DiagnosticGridSession)original).Snapshot, time, duration);
        }
        var events = new List<MovementEvent>();
        void Frame(int time, bool complete = true, params RawInputFrame[] pads)
        {
            WorldFrameForSelfTest(observed, pads, .1, complete, SimulationTime.At(time));
            var advance = _worldActor.Session!.LastAdvance;
            if (advance is not null) events.AddRange(advance.Movement.Events.Where(e => !events.Any(old => old.Ordinal == e.Ordinal)));
        }
        void At(int time, Action callback) { _worldTestTime = SimulationTime.At(time); callback(); }
        // The WORLD keyboard has no actor route (ARC-INPUT-001); the selected
        // controller's stick replays the former S/A key edges callback for callback.
        void SendKey(Key key, bool pressed) => SimulatedSectorKey(key, pressed);
        void SendOpen(bool pressed) { SendKey(Key.S, pressed); SendKey(Key.A, pressed); }
        RawInputFrame[] Pad() => new[] { SimulatedPadLevel() };
        void Prepare(bool pad = true)
        {
            if (WorldModal) ToggleWorldSettings();
            GetViewport().GuiReleaseFocus(); _worldAdapter.Clear(); ClearSimulatedSectorKeys(); RetireWorldLifecycle();
            observed = none;
            Configure(SimulationTime.At(0), StepDuration.Of(8));
            _worldGridInput = new(); _worldTestTime = SimulationTime.At(0); _worldFocused = true;
            _worldReducer.SetStartupDevices(pad ? new[] { 3 } : Array.Empty<int>());
            QuarantineWorld(); PublishWorldSelection();
            var pads = pad ? new[] { new RawInputFrame(3, default) } : Array.Empty<RawInputFrame>();
            Frame(1, true, pads); Frame(2, true, pads); Frame(3, true, pads);
            Require(_worldGate.IsOpen, "complete neutral preparation"); events.Clear();
        }
        string Trace() => string.Join("|", events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}"));
        const string start = "StepStarted@10/1:6,1>7,1|ActionChanged@10/1:6,1>7,1";
        const string cancel = "|StepCancelled@12/1:6,1>7,1|FacingChanged@12/1:6,1>7,1";
        const string arrive = "|CenterArrived@14/1:6,1>7,1|ActionChanged@14/1:6,1>7,1";
        // All material input occurs BETWEEN frames, including two entries at13.
        Prepare(); At(10, () => SendOpen(true)); At(12, () => SendOpen(false));
        At(13, () => { SendKey(Key.S, true); SendKey(Key.S, false); });
        Frame(14, true, Pad()); Frame(15, true, Pad());
        Require(Trace() == start + cancel + arrive, "between-frame/same-tick full transcript: " + Trace());
        Require(_worldActor.Session!.State.Anchor.U == ExactScalar.Integer(6)
            && _worldActor.Session.State.CommittedCell == new CellCoord(6,1), "same-tick neutral suppressed stale start");
        // S then A form one completed chord; its second same-tick material
        // observation (sequence1) owns the frozen segment, not interim S.
        Require(events.Select(e => e.Key).Distinct().Count() == 1 && events[0].Key.MaterialIntentKey == new InputKey(SimulationTime.At(10), 1), "literal original callback chord key");
        Require(events.All(e => e.Kind != MovementEventKind.StepStarted || e.Key.From == new CellCoord(6,1)), "stick sector route");
        // Safety notifications at early/equality/late release. Entry precedes
        // equal-time neutral; Return/Finish remain Walk even though gate closes.
        foreach (var safety in new[] { "focus", "modal", "disconnect", "incomplete" })
        foreach (var release in new[] { 12, 14, 16 })
        {
            Prepare();
            At(10, () => SendOpen(true));
            var pads = Pad();
            At(release, () =>
            {
                if (safety == "focus") _Notification((int)NotificationApplicationFocusOut);
                else if (safety == "modal") SendPaletteChordForSelfTest();
                else if (safety == "disconnect") WorldConnectionChanged(3, false);
                else Frame(release, false, pads);
            });
            if (safety == "modal") Require(WorldModal, "Ctrl+Shift+F9 opened the palette");
            Frame(release + 1, true, pads); Frame(release + 2, true, pads);
            var state = _worldActor.Session!.State;
            if (release == 12)
            {
                Require(state.Phase == MovementPhase.Returning && state.Action == MovementAction.Walk
                    && state.Anchor.U == ExactScalar.Create(49,8) && state.CommittedCell == new CellCoord(6,1), safety + " early return13");
                Frame(15, true, pads);
                Require(Trace() == start + cancel + arrive, safety + " early transcript: " + Trace());
            }
            else
            {
                Require(state.Phase == MovementPhase.OutboundAfterEntry && state.Action == MovementAction.Walk
                    && state.CommittedCell == new CellCoord(7,1), safety + " post-entry Walk");
                Frame(18, true, pads); Frame(19, true, pads);
                Require(Trace() == start + "|CellCommitted@14/1:6,1>7,1|CenterArrived@18/1:6,1>7,1|ActionChanged@18/1:6,1>7,1",
                    safety + " equality/late transcript: " + Trace());
            }
            Require(_worldActor.Session.State.Phase == MovementPhase.Settled && !_worldActor.IsPlaying, safety + " settled Idle");
            // Held callbacks cannot bypass quarantine; no new admission key.
            At(20, () => SendOpen(true)); Frame(21, true, pads); Frame(22, true, pads);
            Require(events.Count(e => e.Kind == MovementEventKind.StepStarted) == 1, safety + " held callback bypassed safety");
        }
        Prepare(true);
        At(10, () => { _Input(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = -1 });
            _Input(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 1 }); });
        At(11, () =>
        {
            _Input(new InputEventMouseMotion { Relative = Vector2.One });
            _Input(new InputEventJoypadMotion { Device = 9, Axis = JoyAxis.LeftY, AxisValue = 1 });
        });
        At(12, () => { _Input(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 });
            _Input(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 }); });
        Frame(13, true, new RawInputFrame(3, default)); Frame(14, true, new RawInputFrame(3, default)); Frame(15, true, new RawInputFrame(3, default));
        Require(Trace() == start + cancel + arrive, "neutral mouse callback released active stick: " + Trace());
        // Restore a fresh live-duration session after isolated D8 assertions.
        Configure(_worldTestTime!.Value); _worldGridInput = new(); RetireWorldLifecycle();
        _worldReducer.SetStartupDevices(Array.Empty<int>()); QuarantineWorld(); PublishWorldSelection();
        GD.Print("ARC-WORLD-003 production observation PASS callbacks/between-frame/same-tick/aggregate/focus/modal/disconnect/incomplete early/equality/late full literal D8 transcripts");
    }
}
