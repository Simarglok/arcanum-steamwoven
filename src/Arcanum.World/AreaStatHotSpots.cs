namespace Arcanum.World;

/// <summary>Which stat lines a hot spot of the inventory windows opens (ledger IW46): the weapon stat lines (0x556220) or the armour stat lines (0x5566B0), for the hero or for the other party.</summary>
public enum AreaStatTarget { HeroWeapon, HeroArmour, PartnerWeapon, PartnerArmour }

/// <summary>
/// ARC-UI-1924 (ledger IW46): the four hot-spot buttons the open of the inventory window (0x5727B0) makes for the hero (the art buttons 676 and 677 and the two rectangles over the
/// damage and armour numbers) and the two it makes for the other party, and what the handler (0x573850) does with them. The hero's: in the windows of modes 0, 5 and 6 at (29, 318),
/// (254, 319) and over the rectangles of 0x5CAEF8 and 0x5CAF08, made visible; in the windows of modes 1 to 4 at (374, 318), (599, 319) and over those of 0x5CAF18 and 0x5CAF28, made hidden
/// (flag 8) and shown and hidden again with the hero's doll toggle. The other party's two, 55 × 40 at (75, 329) and (225, 329), exist when the shown object is no container, are made hidden
/// and follow the other party's doll toggle. A release on one (message subtype 0, which first brings the bar to the message mode, 0x5506C0 with 0) and the pointer entering one (subtype 2)
/// show the weapon stat lines for the first and third, the armour stat lines for the second and fourth. The buttons make no sound. Godot-free.
/// </summary>
public static class AreaStatHotSpots
{
    /// <summary>The art buttons' arts, 0x2A4 and 0x2A5 (<c>interface.mes</c> 676 and 677); a button is as big as its art's frame.</summary>
    public const int WeaponArt = 676, ArmourArt = 677;

    public static IReadOnlyList<int> ArtNumbers { get; } = [WeaponArt, ArmourArt];

    /// <summary>The hero's art buttons in the windows of modes 0, 5 and 6 (the open's first branch).</summary>
    public static readonly (int X, int Y) OwnWeaponButton = (29, 318), OwnArmourButton = (254, 319);

    /// <summary>The hero's art buttons in the windows of modes 1 to 4 (the open's second branch).</summary>
    public static readonly (int X, int Y) SideWeaponButton = (374, 318), SideArmourButton = (599, 319);

    /// <summary>The other party's two hot spots, 55 × 40 each (no art, no sound).</summary>
    public static readonly (int X, int Y, int Width, int Height) PartnerWeaponSpot = (75, 329, 55, 40), PartnerArmourSpot = (225, 329, 55, 40);

    /// <summary>
    /// Whether the hero's buttons are shown: always in the windows of modes 0, 5 and 6, and in those of modes 1 to 4 only while the hero's doll toggle is set (the open makes them hidden, the
    /// button handler shows them with the toggle and hides them without).
    /// </summary>
    public static bool HeroShown(AreaInventoryMode mode, bool heroToggle) => !AreaHeroDollToggle.TogglesShown(mode) || heroToggle;

    /// <summary>The art button of the hero's weapon (<paramref name="weapon"/>) or armour stat lines in the window of <paramref name="mode"/>.</summary>
    public static (int X, int Y) ArtButton(AreaInventoryMode mode, bool weapon) =>
        AreaHeroDollToggle.TogglesShown(mode) ? weapon ? SideWeaponButton : SideArmourButton : weapon ? OwnWeaponButton : OwnArmourButton;

    /// <summary>The rectangle over the hero's damage (<paramref name="weapon"/>) or armour number in the window of <paramref name="mode"/> (0x5CAEF8, 0x5CAF08, 0x5CAF18, 0x5CAF28).</summary>
    public static (int X, int Y, int Width, int Height) HeroSpot(AreaInventoryMode mode, bool weapon) =>
        AreaHeroDollToggle.TogglesShown(mode) ? weapon ? AreaHeroDollToggle.DamageRatingBox : AreaHeroDollToggle.ArmourRatingBox
            : weapon ? AreaInventoryArtLayout.DamageRatingBox : AreaInventoryArtLayout.ArmourRatingBox;

    /// <summary>
    /// The hot spot under a point of the window, or null: the hero's art button of each kind (sized by its art) and its rectangle over the number, when they are shown
    /// (<see cref="HeroShown"/>), then the other party's two rectangles when <paramref name="partnerShown"/> (its doll toggle set in a window that makes them).
    /// </summary>
    public static AreaStatTarget? Hit(AreaInventoryMode mode, bool heroToggle, bool partnerShown, float x, float y, (int Width, int Height) weaponArt, (int Width, int Height) armourArt)
    {
        static bool In(float px, float py, (int X, int Y, int Width, int Height) rect) => px >= rect.X && px < rect.X + rect.Width && py >= rect.Y && py < rect.Y + rect.Height;
        if (HeroShown(mode, heroToggle))
        {
            var (wx, wy) = ArtButton(mode, true);
            var (ax, ay) = ArtButton(mode, false);
            if (In(x, y, (wx, wy, weaponArt.Width, weaponArt.Height)) || In(x, y, HeroSpot(mode, true))) return AreaStatTarget.HeroWeapon;
            if (In(x, y, (ax, ay, armourArt.Width, armourArt.Height)) || In(x, y, HeroSpot(mode, false))) return AreaStatTarget.HeroArmour;
        }
        if (partnerShown)
        {
            if (In(x, y, PartnerWeaponSpot)) return AreaStatTarget.PartnerWeapon;
            if (In(x, y, PartnerArmourSpot)) return AreaStatTarget.PartnerArmour;
        }
        return null;
    }
}
