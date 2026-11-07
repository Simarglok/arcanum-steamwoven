using System.Collections.Immutable;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-036: the kit the opening shop made for the new character: its background (the block the money and items came from), the
/// gold, the items in creation order (basic prototype numbers, <see cref="OpeningKitRules.Items"/>), the clothes it made (the suit,
/// or the jacket when the wear check refused the suit), how many items it skipped (a missing prototype, as the original's failed
/// creation) and the hero load that starts the game with it (<see cref="AreaStartingHero.WithKit"/>).
/// </summary>
public sealed record AreaOpeningKit(int Background, int Gold, ImmutableArray<int> Items, int? Clothes, bool Jacket, int Skipped, AreaHeroLoad Hero)
{
    /// <summary>The character the kit was made for (the menu opens the shop only for its own character).</summary>
    public AreaNewCharacter? Creation { get; init; }

    /// <summary>The kit's items as the session gets them (kit:1 first). They keep their prototypes' stored item flags: the original's
    /// identified mark on the background's items (OS4) is not modelled.</summary>
    public IReadOnlyList<SessionHeroKitItem> Kit => Hero.Definition!.Kit;

    /// <summary>
    /// The original's <c>item_generate_inventory</c> (0x460FF0, ledger OS3, OS4) for the character of <paramref name="editor"/> as
    /// its draft stands: the money and items of the background the character holds (block 0 when no background button was pressed:
    /// the shop reads the PC's field, 0), the clothes for its gender and race (kept when the draft's wear check allows them in the
    /// body-armour slot, else the size's jacket; the art lookup is answered yes, as the viewer's start answers it), crude lockpicks
    /// for any pick-locks points and bandages for any heal points the editor bought. Null with the reason when the background block
    /// does not read or an item is refused.
    /// </summary>
    public static AreaOpeningKit? Make(AreaCreationEditor editor, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(editor);
        detail = null;
        var creation = editor.Creation;
        if (editor.Disposed || editor.Draft.QueryHero() is not { } drafted) { detail = "the character editor's draft is gone"; return null; }
        if (creation.Hero(out var why) is not { } hero) { detail = "the new character cannot be made (" + why + ")"; return null; }
        if (creation.Generic.Backgrounds is not { } table) { detail = "the background table did not read: " + creation.Generic.BackgroundsDetail; return null; }
        if (table.Block(creation.Background) is not { Block: { } block }) { detail = $"background {creation.Background} does not read ({table.Block(creation.Background).Status})"; return null; }
        if (block.Money < 0) { detail = $"background {creation.Background} has negative money {block.Money}"; return null; }
        var size = EquipmentRules.ArmorSize(creation.Race);
        var clothes = OpeningKitRules.Clothes(creation.Gender, size);
        var jacket = false;
        if (clothes is { } suit && AreaStartingHero.WithKit(hero, 0, [suit], out var suitSkipped, out _) is { KitWear: [{ } wearable] } && suitSkipped == 0
            && editor.Draft.CheckHeroWear(wearable, EquipmentSlot.Armor) is { } refusal && refusal != EquipmentRefusal.None)
        {
            clothes = OpeningKitRules.Jacket(size);
            jacket = true;
        }
        var advancement = drafted.Advancement;
        var items = OpeningKitRules.Items(block.Items, clothes, SkillRules.Points(advancement.TechSkills[(int)Skill.PickLocks - SkillRules.BasicCount]),
            SkillRules.Points(advancement.BasicSkills[(int)Skill.Heal]));
        if (AreaStartingHero.WithKit(hero, block.Money, items, out var skipped, out detail) is not { } started) return null;
        return new(creation.Background, block.Money, items, clothes, jacket, skipped, started) { Creation = creation };
    }
}
