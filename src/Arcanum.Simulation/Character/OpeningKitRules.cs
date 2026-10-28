using System.Collections.Immutable;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Simulation.Character;

/// <summary>
/// ARC-UI-036 (N10b slice 4, FR-CHAR-007): the new character's starting kit, which the original makes whenever the opening shop
/// (window 15) refreshes: <c>item_generate_inventory</c> (0x460FF0), read in the retail 1.0.7.4 executable (ledger OS3, OS4). It
/// destroys the PC's whole inventory (0x463B30), gives the background's money and items (0x4C26F0), then, when no item sits in the
/// body-armour slot (1006), creates clothes by gender and armour size and keeps them when the wear check (0x464D20) allows them,
/// else destroys them and creates the size's jacket; then crude lockpicks for any pick-locks points and bandages for any heal points.
/// The kit is a list of basic prototype numbers in creation order; the caller creates the items (a failed creation skips its item in
/// the original) and asks the wear check for the clothes. A new character's items are never worn before the start, so the clothes
/// are always made (the background's items go to free pack slots). Pure functions.
/// </summary>
public static class OpeningKitRules
{
    /// <summary>The crude lockpicks a PC with any pick-locks points gets (prototype 15178, SR13).</summary>
    public const int Lockpicks = 15178;
    /// <summary>The bandages a PC with any heal points gets (prototype 15179, SR13).</summary>
    public const int Bandages = 15179;

    /// <summary>The clothes (a nice suit) for <paramref name="gender"/> and the race's armour size (the table 0x5B32C0,
    /// <see cref="EquipmentRules.ArmorSize"/>): men 8065 small, 8049 medium, 8069 large; women 8134, 8133, 8135. Null for any other size
    /// (no race has one).</summary>
    public static int? Clothes(CharacterGender gender, ArmorSizes size) => (gender, size) switch
    {
        (CharacterGender.Male, ArmorSizes.Small) => 8065, (CharacterGender.Male, ArmorSizes.Medium) => 8049, (CharacterGender.Male, ArmorSizes.Large) => 8069,
        (CharacterGender.Female, ArmorSizes.Small) => 8134, (CharacterGender.Female, ArmorSizes.Medium) => 8133, (CharacterGender.Female, ArmorSizes.Large) => 8135,
        _ => null,
    };

    /// <summary>The jacket the kit makes instead when the wear check refuses the clothes: 8118 small, 8110 medium, 8126 large, for both
    /// genders; null for any other size.</summary>
    public static int? Jacket(ArmorSizes size) => size switch
    {
        ArmorSizes.Small => 8118, ArmorSizes.Medium => 8110, ArmorSizes.Large => 8126,
        _ => null,
    };

    /// <summary>
    /// The kit's items in creation order: the background's items (its item list, in order), the clothes (<paramref name="clothes"/>:
    /// the suit, or the jacket when the wear check refused it; null makes none), crude lockpicks when <paramref name="pickLockPoints"/>
    /// is above 0, then bandages when <paramref name="healPoints"/> is above 0 (points, not levels: SR13).
    /// </summary>
    [OriginalFunction(0x460FF0, OriginalFunctionScope.Partial)]
    public static ImmutableArray<int> Items(IReadOnlyList<int> backgroundItems, int? clothes, int pickLockPoints, int healPoints)
    {
        ArgumentNullException.ThrowIfNull(backgroundItems);
        var items = ImmutableArray.CreateBuilder<int>(backgroundItems.Count + 3);
        items.AddRange(backgroundItems);
        if (clothes is { } made) items.Add(made);
        if (pickLockPoints > 0) items.Add(Lockpicks);
        if (healPoints > 0) items.Add(Bandages);
        return items.ToImmutable();
    }
}
