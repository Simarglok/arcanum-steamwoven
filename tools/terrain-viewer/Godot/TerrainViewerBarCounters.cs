using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-2311: the bottom strip's counters 3 and 4 (ledger BC1–BC8). The original refreshes them (0x54B3C0) on every inventory change of the hero,
// a wield on or off, a gold change, a spell payment, the mode-8 open and a load; here they follow the session's revision and the weapon pane's
// state, which move on each of those. Counter 3: the wielded weapon's ammunition (the hero's first stack of its type) with its type's icon, else
// the weapon's mana store (object field 92, the prototype's value: the session keeps no changing copy) with icon 469, else the hero's gold with icon
// 474. Counter 4: only in bar mode 8, the item's spell charges (−1 unlimited, −2 unidentified).
public partial class TerrainViewerApp
{
    private AreaBarCounterView? _barCountersView;
    private ((GameSessionOwner, long, string?, bool, object?, object?, object?) Stamp, AreaBarCounterLook Look)? _barCountersShown;

    internal AreaBarCounterView? BarCountersForSelfTest => _barCountersView is { Visible: true } ? _barCountersView : null;

    /// <summary>Per frame while the bars show (after the weapon parts, whose mode it reads): the two counters.</summary>
    private void SyncBarCounters()
    {
        if (_gameBarPanel is null) return;
        if (_barCountersView is null)
        {
            _barCountersView = new AreaBarCounterView { Visible = false };
            // Over the strip's other parts (the original draws the numbers into the bar window last).
            _gameBarPanel.AddPart(AreaBarStrip.Bottom, _barCountersView);
        }
        if (_areaSession is not { } session || _barCountersOffForSelfTest)
        {
            _barCountersShown = null;
            if (_barCountersView.Visible) _barCountersView.Visible = false;
            return;
        }
        AdoptBarterData();
        var font = TextFonts()?.Bar;
        var mode8 = _weaponBarShown is { Open: true } && _itemPaneItem is not null;
        var stamp = (session, session.Version.Revision, mode8 ? _itemPaneItem : null, mode8, (object?)_hotkeyBarArt, (object?)_barterData?.Data, (object?)font);
        if (_barCountersShown is { } known && known.Stamp.Equals(stamp))
        {
            if (!_barCountersView.Visible) _barCountersView.Visible = true;
            return;
        }
        var look = BarCountersLook(session, mode8 ? _itemPaneItem : null) with { Font = font };
        _barCountersShown = (stamp, look);
        _barCountersView.Bind(look);
        _barCountersView.Visible = true;
    }

    /// <summary>ARC-MAGIC-2371 (BC7, BC10): the wielded weapon's mana store, field 92: the session's copy once a payment drained it or a recharge restored it, else the weapon prototype's value.</summary>
    private int WieldedManaStore(GameSessionOwner session, SessionWornItem weapon) =>
        session.QueryItemManaStore(weapon.Entity) ?? (HeroWeaponPrototype(session, null) is { } prototype ? CombatPrototype(prototype)?.Item?.ManaStore ?? 0 : 0);

    /// <summary>What the original's counter refresh would write now (ledger BC1–BC5): counter 3 from the hero's wielded weapon, gold and ammunition, counter 4 from
    /// the mode-8 item's charges.</summary>
    private AreaBarCounterLook BarCountersLook(GameSessionOwner session, string? modeItem)
    {
        var weapon = session.QueryEquipment().Worn.FirstOrDefault(worn => worn.Slot == EquipmentSlot.Weapon);
        var ammoType = weapon?.Item.Weapon?.AmmoType;
        var ammo = ammoType is { } type ? session.QueryHeroAmmo().Where(stack => stack.AmmoType == type).Select(stack => stack.Units).FirstOrDefault() : 0;
        var manaStore = ammoType is null && weapon is not null ? WieldedManaStore(session, weapon) : 0;
        var counter3 = BarCounterRules.Counter3(session.QueryHero() is not null, ammoType, ammo, manaStore, session.QueryGold());
        string? counter4 = null;
        if (modeItem is not null && SpellCasterNow()?.ItemPane(modeItem) is { } pane)
            counter4 = BarCounterRules.Text(BarCounterRules.Counter4(pane.Gauge.Kind != ItemSpellGaugeKind.Unknown,
                pane.Gauge.Kind == ItemSpellGaugeKind.Unlimited ? -1 : pane.Gauge.Charges), BarCounterRules.Counter4Digits);
        return new(counter3.Icon is { } icon ? Art(icon) : null, BarCounterRules.Text(counter3.Value, BarCounterRules.Counter3Digits), counter4);
    }
}
