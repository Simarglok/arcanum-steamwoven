using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

// ARC-UI-020 (alpha N4, slice 3 of docs/research/combat-hud.md): health and fatigue bars in crash-site-area. The PC's gauges
// (CH7) follow the session's hero life; the follower portraits' bars (CH9) and the hovered critter's bars (CH8) read an NPC's
// life through the combat host's values on the shown map (a visiting companion's on its home map). Plain Godot controls.
public partial class TerrainViewerApp
{
    private AreaHeroGauges? _heroGauges;
    private (Arcanum.Session.GameSessionOwner? Session, long Revision, bool Shown)? _heroGaugeStamp;

    /// <summary>Shows the PC's gauges while a session with a hero drives crash-site-area and no window is open; cheap when the
    /// session's revision did not change.</summary>
    private void RefreshHeroGauges()
    {
        var shown = _readyArea is not null && _areaSession is { } session && session.QueryHero() is not null && _readyMode == TerrainViewerMode.CrashSiteArea && !WorldModal;
        var stamp = (_areaSession, _areaSession?.Version.Revision ?? -1, shown);
        if (_heroGaugeStamp == stamp) return;
        _heroGaugeStamp = stamp;
        if (!shown) { _heroGauges?.Show(null); return; }
        if (_heroGauges is null) { _heroGauges = new AreaHeroGauges(); AddChild(_heroGauges); }
        _heroGauges.Show(_areaSession!.QueryHeroLife() is { } life ? AreaLife.From(life) : null);
    }

    /// <summary>A companion's life: its combat values on the shown map, else on its home map, or null when the host has none.</summary>
    private AreaLife? CompanionLife(AreaCompanion companion)
    {
        if (_areaSession is not { } session || _combatHost is not { } host) return null;
        return AreaLife.Of(session, host, CurrentAreaMap, companion.Entity)
            ?? (companion.Template.Home.RuntimeMap != CurrentAreaMap ? AreaLife.Of(session, host, companion.Template.Home.RuntimeMap, companion.Entity) : null);
    }

    /// <summary>The hovered object's bars: a companion's or an NPC's life with whether it follows the PC (CH8's
    /// "current/maximum"), or null for anything else (no combat values, not a critter).</summary>
    private (AreaLife Life, bool PcSide)? HoverLife(AreaObjectKey key)
    {
        if (_areaSession is not { } session || _combatHost is not { } host || _readyMode != TerrainViewerMode.CrashSiteArea) return null;
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == key) is { } companion)
            return CompanionLife(companion) is { } life ? (life, session.QueryCompanion(companion.Entity).LedByPc) : null;
        return host.Target(session, CurrentAreaMap, key) is { Build.Combat: not null } target && AreaLife.Of(session, host, CurrentAreaMap, target.Entity) is { } npc
            ? (npc, target.Follower) : null;
    }

    internal AreaHeroGauges? HeroGaugesForSelfTest => _heroGauges;
    internal (int Health, string HealthText, int Fatigue, string FatigueText, bool Poisoned)? HoverBarsForSelfTest =>
        _hoverPanel?.Visible == true ? _hoverBars?.ValuesForSelfTest : null;
}
