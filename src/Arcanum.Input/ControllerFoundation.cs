namespace Arcanum.Input;

/// <summary>The name and GUID a pad reports (Godot <c>Input.GetJoyName/GetJoyGuid</c>); either may be missing.</summary>
public readonly record struct PadIdentity(string? Name, string? Guid);

/// <summary>ARC-GPAD-1374 (concept §0.1 to §0.4), Godot-free: the host's one controller foundation. It owns the scheme choice, the
/// <see cref="ActiveDeviceTracker"/> ("last meaningful input wins"), the glyph family of the pad in hand and the prompt line. Every pad is
/// live: the pad of the latest signal is named by its device id, never assumed to be device 0. The tracker and the strip hold no window or
/// focus state, so a change of device or of settings never resets an open window.</summary>
public sealed class ControllerFoundation
{
    private readonly Func<int, PadIdentity> _padIdentity;
    private readonly bool _hostIsSteamDeck;
    private PromptBindings _pad;
    private PromptBindings _touch;
    private IReadOnlyDictionary<string, ControllerButtons>? _remap;

    public ControllerFoundation(Func<int, PadIdentity> padIdentity, bool hostIsSteamDeck = false, ControlSchemeSettings? settings = null,
        ActiveDeviceOptions? options = null)
    {
        _padIdentity = padIdentity ?? throw new ArgumentNullException(nameof(padIdentity));
        _hostIsSteamDeck = hostIsSteamDeck;
        Settings = (settings ?? ControlSchemeSettings.Default).Validated();
        Tracker = new ActiveDeviceTracker(options);
        _pad = PromptBindings.ForPad(Settings.Pad);
        _touch = PromptBindings.ForTouch(Settings.Touch);
    }

    public ActiveDeviceTracker Tracker { get; }
    public ControlSchemeSettings Settings { get; private set; }

    /// <summary>The device whose prompts show (after the tracker's hysteresis).</summary>
    public ActiveDevice Device => Tracker.Active;

    /// <summary>The pad of the latest pad signal, or -1.</summary>
    public int PadDeviceId => Tracker.LastPadDeviceId;

    /// <summary>The glyph family the prompts use: <see cref="GlyphFamily.KeyboardMouse"/> while the mouse or keyboard is active, else the pad's (or the
    /// manual override's).</summary>
    public GlyphFamily Family => Device == ActiveDevice.KeyboardMouse ? GlyphFamily.KeyboardMouse : PadFamily;

    /// <summary>The family of the pad in hand, whichever device is active (the override wins; an unknown or absent pad reads as Xbox).</summary>
    public GlyphFamily PadFamily
    {
        get
        {
            var identity = Tracker.LastPadDeviceId >= 0 ? _padIdentity(Tracker.LastPadDeviceId) : default;
            return GlyphFamilyDetector.Resolve(Settings.Prompts, identity.Name, identity.Guid, _hostIsSteamDeck);
        }
    }

    /// <summary>Raised after the active device changed or a setting was applied.</summary>
    public event Action? Changed;

    /// <summary>A run-time switch of the scheme choice; nothing needs a restart and no window is touched.</summary>
    public void Apply(ControlSchemeSettings settings)
    {
        var next = (settings ?? throw new ArgumentNullException(nameof(settings))).Validated();
        if (next == Settings) return;
        Settings = next;
        _pad = PromptBindings.ForPad(next.Pad).Remapped(_remap);
        _touch = PromptBindings.ForTouch(next.Touch).Remapped(_remap);
        Changed?.Invoke();
    }

    /// <summary>The user's remaps of pad buttons by prompt id (none yet is the default); the prompts name the remapped button.</summary>
    public void SetRemap(IReadOnlyDictionary<string, ControllerButtons>? remap)
    {
        _remap = remap;
        _pad = PromptBindings.ForPad(Settings.Pad).Remapped(_remap);
        _touch = PromptBindings.ForTouch(Settings.Touch).Remapped(_remap);
        Changed?.Invoke();
    }

    /// <summary>Feeds the tracker; <see cref="Changed"/> is raised when the active device changes.</summary>
    public void Observe(in DeviceSignal signal)
    {
        var before = Tracker.Active;
        Tracker.Observe(signal);
        if (Tracker.Active != before) Changed?.Invoke();
    }

    /// <summary>Applies a device change that has stood for the hysteresis; call with the frame time when no signal arrives.</summary>
    public void Update(double now)
    {
        var before = Tracker.Active;
        Tracker.Update(now);
        if (Tracker.Active != before) Changed?.Invoke();
    }

    /// <summary>The prompt line for <paramref name="context"/>; empty while the mouse or keyboard is active.</summary>
    public string Strip(PromptContext context) => PromptStrip.Compose(Device, Family, Settings, context, _pad, _touch);

    public bool CursorVisible(double now) => Tracker.CursorVisible(now);
}
