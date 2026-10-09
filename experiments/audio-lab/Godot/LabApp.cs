using System.Text.Json;
using AudioLab.Core;
using AudioLab.Synthetic;
using Godot;

namespace AudioLab.Godot;

public partial class LabApp : Control
{
    private AudioBackend backend = null!;
    private EncodedAudio wave = null!;
    private readonly Dictionary<AudioBus, AudioHandle> handles = new();
    private readonly Dictionary<AudioBus, Button> playButtons = new();
    private readonly Dictionary<AudioBus, Button> stopButtons = new();
    private readonly Dictionary<AudioBus, HSlider> sliders = new();
    private readonly Dictionary<AudioBus, CheckButton> mutes = new();
    private Label status = null!;
    private string prefsPath = "";
    private string? reportPath;
    private readonly List<object> observations = new();
    private AudioEffectCapture? capture;
    private bool shuttingDown;
    private string[] args = [];
    private static readonly AudioBus[] Children = [AudioBus.Music, AudioBus.Voice, AudioBus.SFX, AudioBus.Ambient];
    private string? Argument(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    public override async void _Ready()
    {
        args = OS.GetCmdlineUserArgs(); reportPath = Argument("--report=");
        prefsPath = Argument("--prefs=") ?? ProjectSettings.GlobalizePath("user://audio-preferences-v1.json");
        GetTree().AutoAcceptQuit = false;
        var created = AudioBackend.Create(this, AudioLimits.Lab, out var instance);
        if (!created.Success) { GD.Print("AUDIO_STARTUP_FAIL " + created.Error); GetTree().Quit(2); return; }
        backend = instance!;
        EncodedAudio.Create(SyntheticWave.Create(), MediaKind.Wav, 1_048_576, out var encoded); wave = encoded!;
        var loaded = PreferencesStore.Load(prefsPath, out var prefs); backend.ApplyPreferences(prefs);
        BuildUi();
        GD.Print($"AUDIO_STARTUP driver={backend.Driver} device={backend.Device} prefs={loaded.Error} viewport={GetViewportRect().Size}");
        Observe("startup", new { backend.Driver, backend.Device, Preferences = loaded.Error.ToString(), Width = GetViewportRect().Size.X, Height = GetViewportRect().Size.Y });
        status.Text = "Native " + backend.Driver + " / " + backend.Device + " | preferences: " + loaded.Error;
        try
        {
            if (args.Contains("--write-prefs"))
            {
                SetUi(AudioBus.Master, .5f, false); SetUi(AudioBus.Music, .25f, true);
                SetUi(AudioBus.Voice, .75f, false); SetUi(AudioBus.SFX, .5f, true); SetUi(AudioBus.Ambient, 1, false);
                Require(PreferencesStore.Save(prefsPath, backend.Policy.Preferences).Success, "save real restart prefs");
                await Wait(.2); Finish(0);
            }
            else if (args.Contains("--read-prefs"))
            {
                Require(loaded.Success, "restart load success");
                Require(backend.Policy.Preferences == new MixerPreferences(1, new(.5f, false), new(.25f, true), new(.75f, false), new(.5f, true), new(1, false)), "restart literal persisted preferences");
                Require(sliders[AudioBus.Music].Value == .25 && mutes[AudioBus.Music].ButtonPressed, "restart UI matches preferences");
                await Wait(.2); Finish(0);
            }
            else if (args.Contains("--defaults-prefs"))
            {
                Require(loaded.Error == AudioError.PreferencesInvalid && backend.Preferences == MixerPreferences.Defaults, "corrupt preferences typed failure and literal defaults");
                await Wait(.2); Finish(0);
            }
            else if (args.Contains("--verify")) { await Verify(); Finish(0); }
            else if (Argument("--ordinary-seconds=") is { } seconds)
            {
                foreach (var bus in Children) playButtons[bus].EmitSignal(Button.SignalName.Pressed);
                await Wait(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
                Require(backend.NativePlayers == 4, "ordinary launch four simultaneous loops"); Finish(0);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr("AUDIO_VERIFY_FAIL " + e); Observe("failure", e.Message); Finish(3);
        }
    }
    private void BuildUi()
    {
        var margin = new MarginContainer { OffsetLeft = 20, OffsetTop = 20, OffsetRight = 980, OffsetBottom = 630 };
        AddChild(margin); var column = new VBoxContainer(); margin.AddChild(column);
        column.AddChild(new Label { Text = "Synthetic Audio Lab — own PCM16, 400Hz / 250ms" });
        column.AddChild(new Label { Text = "Master → Music / Voice / SFX / Ambient | no simulation, no original assets" });
        column.AddChild(new Label { Text = "8 players; 1MiB per encoded/decoded buffer; 8MiB total; zero cache" });
        foreach (var bus in Enum.GetValues<AudioBus>())
        {
            var row = new HBoxContainer(); column.AddChild(row);
            row.AddChild(new Label { Text = bus.ToString(), CustomMinimumSize = new(85, 36) });
            var level = backend.Policy.Level(bus);
            var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = .01, Value = level.Volume, CustomMinimumSize = new(180, 36) };
            sliders[bus] = slider; row.AddChild(slider);
            var mute = new CheckButton { Text = "Mute", ButtonPressed = level.Muted }; mutes[bus] = mute; row.AddChild(mute);
            slider.ValueChanged += value => Show(backend.SetLevel(bus, new((float)value, backend.Policy.Level(bus).Muted)));
            mute.Toggled += value => Show(backend.SetLevel(bus, new(backend.Policy.Level(bus).Volume, value)));
            if (bus == AudioBus.Master) continue;
            playButtons[bus] = AddButton(row, "Loop", () => Start(bus, true));
            AddButton(row, "Once", () => Start(bus, false));
            stopButtons[bus] = AddButton(row, "Stop", () => { if (handles.TryGetValue(bus, out var handle)) Show(backend.Stop(handle)); });
            AddButton(row, "Pause/resume", () =>
            {
                if (handles.TryGetValue(bus, out var handle) && backend.Policy.State(handle, out var state).Success) Show(backend.Pause(handle, !state.Paused));
            });
            AddButton(row, "Fade 1s", () => { if (handles.TryGetValue(bus, out var handle)) Show(backend.Fade(handle, 0, 1)); });
        }
        var actions = new HBoxContainer(); column.AddChild(actions);
        AddButton(actions, "Save preferences", () => Show(PreferencesStore.Save(prefsPath, backend.Policy.Preferences)));
        AddButton(actions, "Defaults", () => { foreach (var bus in Enum.GetValues<AudioBus>()) SetUi(bus, bus == AudioBus.Master ? .5f : 1, false); });
        AddButton(actions, "Quit", () => Finish(0));
        column.AddChild(new Label { Text = "Preferences use a dedicated v1 file, independent of gamepad/OS settings.\nEngine signal measurements do not establish physical hearing/device quality." });
        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; column.AddChild(status);
    }
    private static Button AddButton(Node row, string text, Action action)
    {
        var button = new Button { Text = text }; row.AddChild(button); button.Pressed += action; return button;
    }
    private void Start(AudioBus bus, bool loop)
    {
        if (handles.TryGetValue(bus, out var previous)) backend.Stop(previous);
        var result = backend.Play(new(wave, bus, loop)); if (result.Success) handles[bus] = result.Handle; Show(result);
    }
    private void Show(AudioResult result) { if (status is not null) status.Text = "Command: " + result.Error + " | players=" + backend.NativePlayers + " bytes=" + backend.Policy.RetainedBytes; }
    private void SetUi(AudioBus bus, float volume, bool mute)
    {
        sliders[bus].Value = volume; mutes[bus].ButtonPressed = mute;
        // Initial equal UI values need no signal; explicit state remains consistent.
    }
    public override void _Process(double delta) { if (backend is not null && !shuttingDown) backend.Tick(delta); }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
    private void Observe(string name, object value) { observations.Add(new { Name = name, Value = value }); GD.Print("AUDIO_OBSERVE " + name + " " + JsonSerializer.Serialize(value)); }
    private void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Observe("PASS", name);
    }
    private async Task<double> Signal()
    {
        await Wait(.15); capture!.ClearBuffer(); await Wait(.12);
        var frames = capture.GetBuffer(capture.GetFramesAvailable());
        Require(frames.Length >= 1024, "native capture produced frames");
        double sum = 0; foreach (var frame in frames) sum += (frame.X * frame.X + frame.Y * frame.Y) / 2;
        double rms = Math.Sqrt(sum / frames.Length); Observe("master-capture", new { Frames = frames.Length, Rms = rms }); return rms;
    }
    private async Task Verify()
    {
        Require(backend.Driver == "CoreAudio" && AudioServer.GetOutputDeviceList().Length > 0, "real native CoreAudio backend and device list");
        Require(AudioBackend.Create(this, AudioLimits.Lab, out _).Error == AudioError.BackendUnavailable, "exclusive bus topology ownership");
        Require(GetViewportRect().Size.X == 1000 && GetViewportRect().Size.Y == 650, "actual1000x650 viewport");
        using (var decoded = AudioStreamWav.LoadFromBuffer(wave.CopyForDecoder()))
        {
            var pcm = decoded.Data;
            Require(pcm.Length == 8000 && decoded.MixRate == 16000 && !decoded.Stereo && decoded.GetLength() == .25, "Godot decoded literal mono16kHz250ms8000bytes");
            for (int i = 0; i < 4000; i++)
                if (System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) != (i % 40 < 20 ? 4096 : -4096)) throw new InvalidOperationException("Decoded waveform sample " + i);
            Require(true, "Godot decoded4000 literal waveform samples");
        }
        backend.ApplyPreferences(MixerPreferences.Defaults);
        foreach (var bus in Enum.GetValues<AudioBus>()) SetUi(bus, bus == AudioBus.Master ? .5f : 1, false);
        SetUi(AudioBus.Master, .25f, true);
        Require(Math.Abs(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(0)) - .25f) < .00001 && AudioServer.IsBusMute(0) && backend.Preferences.Music == new BusLevel(1, false), "native Master independent normalized volume and mute");
        SetUi(AudioBus.Master, .5f, false);
        capture = new AudioEffectCapture { BufferLength = .25f }; AudioServer.AddBusEffect(0, capture);
        foreach (var bus in Children) playButtons[bus].EmitSignal(Button.SignalName.Pressed);
        Require(backend.NativePlayers == 4 && backend.Policy.ActivePlayers == 4 && backend.Policy.RetainedBytes == 64176, "all four buses simultaneous exact retained64176");
        Require(await Signal() > .02, "simultaneous nonzero master signal");
        foreach (var bus in Children)
        {
            foreach (var other in Children) SetUi(other, 1, other != bus);
            double baseline = await Signal(); Require(baseline > .02, bus + " independent nonzero WAV");
            mutes[bus].ButtonPressed = true;
            Require(await Signal() < .0001, bus + " isolated mute stops master signal");
            Require(backend.Policy.Level(bus) == new BusLevel(1, true) && AudioServer.IsBusMute((int)bus), bus + " muted independently without changing gain");
            mutes[bus].ButtonPressed = false; sliders[bus].Value = .5;
            double half = await Signal(); Require(half > baseline * .35 && half < baseline * .65, bus + " actual volume halves signal");
            sliders[bus].Value = 1;
        }
        foreach (var bus in Children) mutes[bus].ButtonPressed = false;
        mutes[AudioBus.Voice].ButtonPressed = true;
        Require(await Signal() > .02 && !AudioServer.IsBusMute((int)AudioBus.Music), "one muted bus leaves others playing");
        var music = handles[AudioBus.Music];
        Require(backend.Pause(music, true).Success, "native pause");
        foreach (var bus in Children.Where(b => b != AudioBus.Music)) mutes[bus].ButtonPressed = true;
        Require(await Signal() < .0001, "paused player silent");
        Require(backend.Fade(music, 0, .2).Success, "fade while paused"); await Wait(.3);
        backend.Policy.State(music, out var paused); Require(paused.Paused && paused.Gain == 0, "fade advances independent of pause/simulation");
        backend.Pause(music, false); Require(await Signal() < .0001, "zero fade output");
        backend.Fade(music, 1, .2); await Wait(.3); Require(await Signal() > .02, "resume fade restores signal");
        foreach (var bus in Children) stopButtons[bus].EmitSignal(Button.SignalName.Pressed);
        Require(backend.NativePlayers == 0 && backend.Policy.RetainedBytes == 0, "stop UI releases all nodes/bytes");
        Require(await Signal() < .0001, "stop releases mixer signal");
        Require(backend.Stop(music).Error == AudioError.AlreadyStopped, "native double stop");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Require(backend.Play(new(wave, AudioBus.Music), cancelled.Token).Error == AudioError.Cancelled, "cancelled load no nodes");
        EncodedAudio.Create([1, 2, 3, 4], MediaKind.Wav, 10, out var bad);
        Require(backend.Play(new(bad!, AudioBus.Music)).Error == AudioError.InvalidMedia, "bad WAV failure releases reservation");
        var replacements = new List<AudioHandle>();
        for (int i = 0; i < 8; i++)
        {
            var r = backend.Play(new(wave, Children[i % 4], true)); Require(r.Success, "native at concurrency " + (i + 1)); replacements.Add(r.Handle);
        }
        Require(backend.Play(new(wave, AudioBus.SFX, true)).Error == AudioError.PlayerLimit, "native beyond8 concurrency");
        foreach (var bus in Children) backend.SetLevel(bus, new(1, false));
        Require(await Signal() > .02 && backend.NativePlayers == 8, "eight live simultaneous players produce signal");
        Require(backend.Stop(music).Error == AudioError.StaleHandle, "native stale generation");
        Require(backend.Stop(replacements[0] with { Owner = Guid.NewGuid() }).Error == AudioError.ForeignHandle, "native foreign ownership");
        foreach (var h in replacements) backend.Stop(h);
        for (int i = 0; i < 100; i++)
        {
            var r = backend.Play(new(wave, AudioBus.SFX, true));
            if (i % 10 == 0) await Wait(.03);
            Require(r.Success && backend.Stop(r.Handle).Success, "native play/stop cycle " + i);
        }
        var once = backend.Play(new(wave, AudioBus.Voice)); Require(once.Success, "native one-shot");
        await Wait(.5); Require(backend.NativePlayers == 0 && backend.Policy.RetainedBytes == 0, "natural completion releases");
        Require(await Task.Run(() => backend.Play(new(wave, AudioBus.Music))).ContinueWith(t => t.Result.Error) == AudioError.WrongThread, "background operation rejects no nodes");
        AudioServer.RemoveBusEffect(0, 0); capture.Dispose(); capture = null;
        backend.Play(new(wave, AudioBus.SFX, true)); backend.Dispose();
        Require(AudioServer.BusCount == 1 && backend.NativePlayers == 0 && backend.Policy.RetainedBytes == 0, "dispose restores topology and releases");
        Require(backend.Play(new(wave, AudioBus.Music)).Error == AudioError.Disposed, "native disposed command");
        Require(AudioBackend.Create(this, new(2, 8044, 16044), out var bounded).Success, "native custom literal byte bounds");
        backend = bounded!;
        var at = backend.Play(new(wave, AudioBus.Music, true));
        Require(at.Success && backend.RetainedBytes == 16044, "native at8044 encoded and16044 total");
        Require(backend.Play(new(wave, AudioBus.Voice)).Error == AudioError.TotalByteLimit, "native beyond16044 total");
        backend.Stop(at.Handle); backend.Dispose();
        Require(AudioBackend.Create(this, new(2, 8044, 16043), out var decodedBound).Success, "native decoded byte budget profile");
        backend = decodedBound!;
        Require(backend.Play(new(wave, AudioBus.Music)).Error == AudioError.TotalByteLimit && backend.RetainedBytes == 0 && backend.NativePlayers == 0, "decoded beyond16043 rolls back reservation");
        backend.Dispose();
        Require(AudioBackend.Create(this, new(2, 8043, 16044), out var undersized).Success, "native smaller buffer profile");
        backend = undersized!;
        Require(backend.Play(new(wave, AudioBus.Music)).Error == AudioError.BufferLimit && backend.RetainedBytes == 0, "native encoded beyond8043 no nodes");
        backend.Dispose();
        Require(AudioBackend.Create(this, AudioLimits.Lab, out var restarted).Success, "same-process backend restart"); backend = restarted!;
        Require(backend.Play(new(wave, AudioBus.Ambient, true)).Success, "restart play");
    }
    public override void _Notification(int what) { if (what == NotificationWMCloseRequest) Finish(0); }
    private void Finish(int exit)
    {
        if (shuttingDown) return; shuttingDown = true;
        if (reportPath is not null)
        {
            using var image = GetViewport().GetTexture().GetImage();
            bool saved = image.GetWidth() == 1000 && image.GetHeight() == 650 && image.SavePng(reportPath + ".png") == Error.Ok;
            Observe(saved ? "PASS" : "failure", "actual viewport PNG1000x650");
            if (!saved) exit = 3;
        }
        if (capture is not null) { AudioServer.RemoveBusEffect(0, 0); capture.Dispose(); capture = null; }
        backend.Dispose(); Observe("shutdown", new { Exit = exit, Players = backend.NativePlayers, Bytes = backend.Policy.RetainedBytes, Buses = AudioServer.BusCount });
        if (reportPath is not null) File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Exit = exit, Observations = observations }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(exit == 0 ? "AUDIO_NATIVE_PASS" : "AUDIO_NATIVE_FAIL"); GetTree().Quit(exit);
    }
    public override void _ExitTree() { if (backend is not null && !shuttingDown) backend.Dispose(); }
}
