using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

// ARC-UI-020 (alpha N4, slice 3 of docs/research/combat-hud.md): health and fatigue bars in crash-site-area. The PC's gauges
// (CH7) follow the session's hero life (in the interface bar's vials while ARC-UI-018's bars are shown); the follower portraits' bars (CH9) and the hovered critter's bars (CH8) read an NPC's
// life through the combat host's values on the shown map (a visiting companion's on its home map). Plain Godot controls.
public partial class TerrainViewerApp
{
    private AreaHeroGauges? _heroGauges;
    private (Arcanum.Session.GameSessionOwner? Session, long Revision, bool Shown, bool InBars)? _heroGaugeStamp;
    // Automated runs draw the gauges only when their smoke asks (or the interface bars are drawn), as the bars (ARC-UI-018), so the
    // readback smokes see the scene they expect.
    private bool _lifeBarsForSelfTest;

    /// <summary>Shows the PC's gauges while a session with a hero drives crash-site-area: in the interface bar's vials while the bars
    /// are shown, else in their own panel while no window is open. Cheap when nothing changed.</summary>
    private void RefreshHeroGauges()
    {
        var inBars = _gameBarPanel is { Visible: true };
        var shown = _readyArea is not null && _areaSession is { } session && session.QueryHero() is not null && _readyMode == TerrainViewerMode.Area
            && (inBars || !WorldModal) && (!ViewerAutomatedStorage.Automated || _lifeBarsForSelfTest || _gameBarForSelfTest);
        var stamp = (_areaSession, _areaSession?.Version.Revision ?? -1, shown, inBars);
        if (_heroGaugeStamp == stamp) return;
        _heroGaugeStamp = stamp;
        if (!shown) { _heroGauges?.Show(null, null); return; }
        if (_heroGauges is null) { _heroGauges = new AreaHeroGauges(); AddChild(_heroGauges); }
        _heroGauges.Show(_areaSession!.QueryHeroLife() is { } life ? AreaLife.From(life) : null, inBars ? _gameBarPanel : null);
    }

    /// <summary>A companion's life by identity (ARC-DEV-076, MIG3): its combat values wherever it stands, or null when the host has none.</summary>
    private AreaLife? CompanionLife(AreaCompanion companion) =>
        _areaSession is { } session && _combatHost is { } host ? AreaLife.Of(session, host, companion.Entity) : null;

    /// <summary>The hovered object's bars: a companion's or an NPC's life with whether it follows the PC (CH8's
    /// "current/maximum"), or null for anything else (no combat values, not a critter).</summary>
    private (AreaLife Life, bool PcSide)? HoverLife(AreaObjectKey key)
    {
        if (_areaSession is not { } session || _combatHost is not { } host || _readyMode != TerrainViewerMode.Area) return null;
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == key) is { } companion)
            return CompanionLife(companion) is { } life ? (life, session.QueryCompanion(companion.Entity).LedByPc) : null;
        return host.Target(session, key) is { Build.Combat: not null } target && AreaLife.Of(session, host, target.Entity) is { } npc
            ? (npc, target.Follower) : null;
    }

    internal AreaHeroGauges? HeroGaugesForSelfTest => _heroGauges;
    internal (int Health, string HealthText, int Fatigue, string FatigueText, bool Poisoned)? HoverBarsForSelfTest =>
        _hoverPanel?.Visible == true ? _hoverBars?.ValuesForSelfTest : null;
}
