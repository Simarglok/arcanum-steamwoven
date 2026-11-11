using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-045 (ledger OD1–OD6): the bodies a spell's object-flag writes hide, shrink or make translucent. The session keeps the flags a run wrote over the value the map data stores
// (invisible, translucent, shrunk, don't-draw); the draw-list builder, the blit set-up and the frame rectangle of the original read them (AreaBodyStyleRules). The scene plan already places a
// body the data hides or shrinks; this applies the change since, by body, to the final view (the canvas drops an undrawn body, so it is not picked either).
public partial class TerrainViewerApp
{
    private object? _bodyStyleStamp;
    private int _bodyStyleCount;

    // For the smoke: the styles the last sync handed to the canvas.
    internal IReadOnlyDictionary<AreaObjectKey, AreaBodyStyle>? BodyStylesForSelfTest => _canvas?.AreaBodyStyles;

    /// <summary>
    /// Recomputes the body styles when the session changed: for each mobile critter of the shown map (and each critter the session created on it) that carries written object flags, the style
    /// its flags now ask for, less what the map data's own flags already did to its T0 body (<see cref="AreaBodyStyleRules.Delta"/>); and the hero's. The local player sees invisible objects when
    /// its spell flags hold detects-invisible or its perception is extraordinary (0x458A80, 0x4B0EE0).
    /// </summary>
    private void SyncAreaBodyStyles()
    {
        if (_canvas is null) return;
        if (_readyArea is not { } area || _areaSession is not { } session || _combatHost is null || _inventoryHost is null
            || _inventoryHost.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } map0)
        {
            if (_bodyStyleCount > 0 || _bodyStyleStamp is not null) { _canvas.SetAreaBodyStyles(null, null); _bodyStyleCount = 0; _bodyStyleStamp = null; }
            return;
        }
        var stamp = (area, session, session.Version.Revision);
        if (Equals(stamp, _bodyStyleStamp)) return;
        _bodyStyleStamp = stamp;
        var hero = session.QueryObjectFlags(SessionSpellCaster.Hero);
        // 0x458A80 with the mask 0x200: the player's spell flags hold detects-invisible, or (0x4B0EE0, stat 5) its perception is extraordinary (20 or more).
        var sees = (hero.Spell(0) & AreaBodyStyleRules.DetectsInvisibleSpell) != 0 || session.QueryHeroStats(new(0))?.Snapshot?.IsExtraordinary(CharacterStat.Perception) == true;
        var styles = new Dictionary<AreaObjectKey, AreaBodyStyle>();
        var maps = new List<AreaInventoryMap> { map0 };
        maps.AddRange(CreatedCritterMaps());
        foreach (var record in session.QueryAllObjectFlags())
        {
            if (record.Entity == SessionSpellCaster.Hero || (record.Flags.ObjectSet | record.Flags.ObjectCleared | record.Flags.BlitSet | record.Flags.BlitCleared) == 0) continue;
            var map = maps.FirstOrDefault(m => m.Seed.Holders.Any(h => h.Kind == SessionHolderKind.Npc && h.Entity == record.Entity));
            if (map is null) continue;
            var stored = unchecked((int)(_combatHost.Fields(record.Entity)?.Object.Flags ?? 0));
            var data = AreaBodyStyleRules.Of(stored, 0, true);
            var now = AreaBodyStyleRules.Of(record.Flags.Object(stored), record.Flags.Blit(0), sees);
            var delta = AreaBodyStyleRules.Delta(data, now);
            // A body the data hides (don't-draw, or invisible with no observer) is not in the plan: nothing to take away, and a style that would show it again is not applied.
            if (delta == AreaBodyStyle.Plain || !data.Drawn) continue;
            styles[map.Row(record.Entity).Key] = delta;
        }
        // The hero has no data flags and is never made invisible (OB1 skips the local player): its translucent bit and don't-draw are what the spells wrote.
        var heroStyle = AreaBodyStyleRules.Of(hero.Object(0), hero.Blit(0), true);
        var count = styles.Count + (heroStyle == AreaBodyStyle.Plain ? 0 : 1);
        // Said once per change of the styled set (the session's revision moves with every clock tick, the styles seldom do).
        var previous = _canvas.AreaBodyStyles;
        var same = previous is null ? styles.Count == 0 : previous.Count == styles.Count && styles.All(p => previous.TryGetValue(p.Key, out var o) && o == p.Value);
        if (!same || count != _bodyStyleCount) GD.Print($"ARC-MAGIC-045 body-styles count={styles.Count} hero={heroStyle}");
        _bodyStyleCount = count;
        _canvas.SetAreaBodyStyles(styles, heroStyle);
    }
}
