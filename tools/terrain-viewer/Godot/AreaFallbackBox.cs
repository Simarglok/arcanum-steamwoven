using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3136 (owner rule 2026-11-19: original fonts everywhere, never Godot's default font): the gate of the diagnostic windows, the plain Godot
/// <see cref="Control"/> boxes a window builds beside its original look and shows only when the original's art did not read (no game folder, a
/// synthetic self-test, a diagnostic look). Their text is Godot's default font, so they are developer-only: shown in a developer run (the debug
/// mode, a self-test, a smoke run) and drawn by nobody else. A player's run without the art draws nothing, like <see cref="AreaArtLabel"/>.
/// </summary>
public static class AreaFallbackLook
{
    /// <summary>True in a developer run (debug mode, self-test bootstrap, smoke); the app sets it. A run outside the app (a unit test) keeps the default, true.</summary>
    public static bool Allowed { get; set; } = true;

    /// <summary>The app's original text fonts (null without them: no game folder, a self-test, art that did not read); the app sets it.</summary>
    public static Func<AreaTextFonts?>? Fonts { get; set; }
}

/// <summary>
/// A diagnostic window's fallback box: it draws nothing whenever <see cref="AreaFallbackLook.Allowed"/> is false (fully transparent, so the
/// window's own state, focus and keys behave as before; only the default-font drawing is gone).
/// </summary>
public partial class AreaFallbackBox : PanelContainer
{
    public override void _Ready() => Apply();

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged) Apply();
    }

    private void Apply() => Modulate = AreaFallbackLook.Allowed ? Colors.White : new Color(1, 1, 1, 0);
}
