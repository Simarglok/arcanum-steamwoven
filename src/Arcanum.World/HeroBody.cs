using Arcanum.Data;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Sound;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-039: the critter body the hero is drawn with, from its race, gender and worn armour art (ledger HB1–HB4). The original writes the
/// race table's body type (NW2) and the gender into the PC's art id when it sets race and gender (NW3, 0x441C70); the twelve
/// pre-made prototypes carry the same ids (SH-04). An elf woman is drawn with the human body, as every critter name is
/// (whole-map art rule N1). Only the eight creation races have a body here; any other race has none (the caller keeps the
/// fixed human). Godot-free.
/// </summary>
/// <param name="Body">The art id's body type (bits 24..26): 0 human, 1 dwarf, 2 halfling (and gnome), 3 half-ogre, 4 elf.</param>
/// <param name="Gender">The art id's gender bit (27): 0 female, 1 male.</param>
/// <param name="Armor">The art id's armour part (bits 20..23): 0 underwear, up to 8 (rule N1's armour codes); worn body armour sets it
/// (<see cref="Wearing"/>, ARC-WORLD-112).</param>
/// <param name="Palette">The art id's palette (bits 4..5, 0..3): the base art's (0 for every creatable PC) until worn body armour's
/// use art sets it (0x465AE0, ARC-WORLD-112).</param>
/// <param name="Weapon">The art id's weapon part (bits 0..3, 0..14): 0 out of combat mode; in it the wielded weapon's use-art subtype, 1
/// bare-handed (<see cref="Armed"/>, ARC-RENDER-037, ledger WA1).</param>
/// <param name="Shield">The art id's shield bit (19): 1 in combat mode with a shield worn (WA1).</param>
/// <param name="MonsterSpecies">ARC-MAGIC-052: the monster species (art id bits 23..27, 0..31) the Trait component put on the hero (Polymorph, the body spells), or -1 for the hero's own body; the body is then a
/// monster art (class 12) of that species and <see cref="Palette"/>, and the human fields are unused (<see cref="Monster"/>).</param>
public readonly record struct HeroBody(int Body, int Gender, int Armor = 0, int Palette = 0, int Weapon = 0, int Shield = 0, int MonsterSpecies = -1)
{
    /// <summary>ARC-MAGIC-052: whether this is a monster body (the hero polymorphed by a Trait component).</summary>
    public bool IsMonster => MonsterSpecies >= 0;

    /// <summary>ARC-MAGIC-052 (ledger TR4): the monster body of a species (0..31) and palette (0..3): no armour or shield part. ARC-MAGIC-1219 (PR1, PR2): <paramref name="weapon"/> is the weapon part,
    /// 0 out of combat mode and 1 in it (the hero wields nothing in a monster body, so the part is only ever 0 or 1); <see cref="Palette"/> is the one the spell asked for, which the ART may not hold
    /// (the draw then reads palette 0, PR5).</summary>
    public static HeroBody Monster(int species, int palette, int weapon = 0) => new(0, 0, 0, palette, weapon, 0, species);

    /// <summary>ARC-MAGIC-1219: this monster body in or out of combat mode (the weapon part 1 or 0, PR2).</summary>
    public HeroBody MonsterInCombat(bool combatMode) => this with { Weapon = combatMode ? 1 : 0 };

    /// <summary>ARC-MAGIC-052: the art id of this monster body for an animation: class 12, the species, the animation, the weapon part (see <see cref="MonsterWeaponPart"/>) and the palette (no armour, shield,
    /// rotation or frame).</summary>
    public uint MonsterSelector(int animation) => MonsterSelector(animation, MonsterWeaponPart(animation));

    private uint MonsterSelector(int animation, int weapon) =>
        (12u << 28) | ((uint)MonsterSpecies << 23) | ((uint)animation << 6) | ((uint)Palette << 4) | (uint)weapon;

    /// <summary>The weapon part the art id of an animation carries (MA2): the animations 6 to 19 (run, fall, get up, vault, throw) clear it, the attack (20) and the low attack (21) take 1 even out of combat
    /// mode (every installed attack file is a weapon part 1 one, and nothing attacks out of combat mode; the human body does the same), the rest keep the body's.</summary>
    public int MonsterWeaponPart(int animation) => animation switch
    {
        >= 6 and <= 19 => 0,
        WeaponArtRules.AttackAnimation or WeaponArtRules.LowAttackAnimation => Weapon == 0 ? 1 : Weapon,
        // ARC-COMBAT-1865 (N1): the stunned animation names the unarmed weapon part whatever the body wields.
        (int)StunnedAnimation => WeaponArtRules.Unarmed,
        _ => Weapon,
    };

    /// <summary>
    /// ARC-MAGIC-1219 (PR4 of ARC-RES-145, the original's missing-art repair on a monster): the art ids the loader tries for an animation, in order. The stand with weapon part 0 missing becomes the stand with part 1; the walk
    /// with part 1 missing (30 of 32 species, not the lizard man and the phantom knight) becomes the walk with part 0; the run missing becomes the walk; a low attack missing becomes the attack (the caller does not
    /// read it twice). The throw (14) has no entry and is not loaded at all (owner D2: nothing in a monster body can play it).
    /// </summary>
    public IReadOnlyList<uint> MonsterCandidates(int animation)
    {
        var own = MonsterSelector(animation);
        var weapon = (int)(own & 15);
        return animation switch
        {
            0 when weapon == 0 => [own, MonsterSelector(0, 1)],
            1 when weapon != 0 => [own, MonsterSelector(1, 0)],
            6 => [own, MonsterSelector(1, 0)],
            _ => [own],
        };
    }

    private static readonly string[] BodyCodes = ["HM", "DF", "GH", "HG", "EF"];
    private static readonly string[] ArmorCodes = ["UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD"];
    private const string WeaponCodes = "ABCDEFGHIJKXYNZ";

    /// <summary>The body the fixed diagnostic human is drawn with (<see cref="ActorDiagnosticProfile.HumanFixedArea"/>).</summary>
    public static HeroBody HumanFemale { get; } = new(0, 0);

    /// <summary>The hero's body for a race, gender, armour part (<see cref="Armor"/>) and palette (<see cref="Palette"/>), or null for a
    /// race outside the creation table, an armour part without a code or a palette outside 0..3.</summary>
    [OriginalFunction(0x441C70, OriginalFunctionScope.Partial)]
    public static HeroBody? For(CharacterRace race, CharacterGender gender, int armor = 0, int palette = 0)
    {
        if ((int)race is < 0 or >= CharacterCreationRules.RaceCount || !Enum.IsDefined(gender) || armor is < 0 || armor >= ArmorCodes.Length
            || palette is < 0 or > 3) return null;
        var body = CharacterCreationRules.Races[(int)race].BodyType;
        var male = gender == CharacterGender.Male ? 1 : 0;
        // N1: an elf woman has no body of her own; the human body draws her.
        return new(body == 4 && male == 0 ? 0 : body, male, armor, palette);
    }

    /// <summary>
    /// ARC-WORLD-112: this body with what the body-armour slot gives it (0x465AE0, ledger BA1–BA2): a worn item's armour subtype and its
    /// use art's palette, or with no item the underwear and <paramref name="basePalette"/> (the base art's; 0 for every creatable PC).
    /// Null when the item's subtype does not stand in a critter art id (9..15; the original's wear check refuses such an item, BA6).
    /// Only the body-armour slot changes the body.
    /// </summary>
    /// <param name="useArt">The worn body armour's use art (item field 0x5F), or null for none.</param>
    public HeroBody? Wearing(uint? useArt, int basePalette = 0)
    {
        if (basePalette is < 0 or > 3) return null;
        var current = Selector(CritterArtType);
        if (BodyArmorArtRules.WornArt(current, BodyArmorArtRules.WithPalette(current, basePalette), useArt) is not { } worn) return null;
        return this with { Armor = SoundCueRules.CritterArtArmor(worn), Palette = BodyArmorArtRules.Palette(worn) };
    }

    private const uint CritterArtType = BodyArmorArtRules.CritterArt << 28;

    /// <summary>
    /// ARC-RENDER-037: this body with the weapon and shield it shows (0x465020, ledger WA1): out of combat mode none; in it the wielded
    /// weapon's use-art subtype (bare hands without one or for a subtype of 0) and the shield bit for an armour item in the shield slot.
    /// Null for a weapon part the art id cannot hold (15: the original sets 0 and its wear check refuses such a weapon, WA8).
    /// </summary>
    /// <param name="weaponUseArt">The wielded weapon's use art (item field 0x5F), or null for none.</param>
    /// <param name="shieldItemType">The shield slot's item's type (7 armour), or null for none.</param>
    public HeroBody? Armed(bool combatMode, uint? weaponUseArt, int? shieldItemType)
    {
        var weapon = WeaponArtRules.WeaponPart(combatMode, weaponUseArt);
        if (weapon >= WeaponArtRules.WeaponParts) return null;
        return this with { Weapon = weapon, Shield = WeaponArtRules.ShieldPart(combatMode, shieldItemType) };
    }

    /// <summary>The art id bits of the body itself: body type, gender, armour part and palette (no weapon, shield or animation).</summary>
    public uint BodyBits => ((uint)Gender << 27) | ((uint)Body << 24) | ((uint)Armor << 20) | ((uint)Palette << 4);

    /// <summary>The folder and file prefix of this body's critter ART (for example <c>EFM</c>). Plate armour (4, 6) has one
    /// sexless art per body and none for the elf or halfling body, which take the human and dwarf ones (rule N1).</summary>
    public string Label => IsMonster ? $"monster{MonsterSpecies}" : Armor is 4 or 6 ? BodyCodes[Body switch { 4 => 0, 2 => 1, _ => Body }] + "X" : BodyCodes[Body] + (Gender == 0 ? "F" : "M");

    /// <summary>Dwarf and halfling bodies add 4 to every walk and run frame rate (research L6; the rate 0x437990 asks the art id's
    /// body, 0x503EA0, for 1 or 2).</summary>
    public int GaitRateBonus => Body is 1 or 2 ? 4 : 0;

    /// <summary>ARC-COMBAT-1741: the stunned animation (goal 57).</summary>
    public const uint StunnedAnimation = 23;

    /// <summary>
    /// The fixed human's art selector with this body, gender, armour part and palette, the same animation, and the weapon and shield the
    /// file of that animation shows (ARC-RENDER-037, MA2): the stand both, the walk the weapon only, the attacks (20, 21) both (the
    /// human's bare hands when the body shows no weapon: out of combat mode nothing attacks), the animations 6..19 (run, fall, get up,
    /// vault, and since ARC-RENDER-039 the throw, 14) neither.
    /// </summary>
    public uint Selector(uint humanSelector)
    {
        var animation = (humanSelector >> 6) & 31;
        var (weapon, shield) = animation switch
        {
            0 => (Weapon, Shield),
            1 => (Weapon, 0),
            WeaponArtRules.AttackAnimation or WeaponArtRules.LowAttackAnimation => (Weapon == 0 ? (int)(humanSelector & 15) : Weapon, Shield),
            >= 6 and <= 19 => (0, 0),
            // ARC-COMBAT-1741 (N1): the stunned animation names the unarmed weapon letter and no shield whatever the body wields.
            StunnedAnimation => (WeaponArtRules.Unarmed, 0),
            _ => (Weapon, Shield),
        };
        return (humanSelector & ~0x0FF8003Fu) | BodyBits | ((uint)shield << 19) | (uint)weapon;
    }

    /// <summary>
    /// The selectors <see cref="HeroBodyArt"/> tries for an action, in order (the missing-art repair, MA1): <see cref="Selector"/>, then
    /// for an armed walk the walk without the weapon, for a stand without a weapon the stand with bare hands, and (ARC-RENDER-039) for
    /// a low attack with one of the ten repaired weapons (bare hands, 2..8, 10, 13) the attack with the same weapon and shield.
    /// </summary>
    public IReadOnlyList<uint> Candidates(uint humanSelector)
    {
        var selector = Selector(humanSelector);
        var animation = (selector >> 6) & 31;
        return animation == 1 && (selector & 15) != 0 ? [selector, selector & ~15u]
            : animation == 0 && (selector & 15) == 0 ? [selector, selector | WeaponArtRules.Unarmed]
            : animation == WeaponArtRules.LowAttackAnimation && LowAttackRepaired((int)(selector & 15))
                ? [selector, (selector & ~(31u << 6)) | (uint)WeaponArtRules.AttackAnimation << 6]
            : [selector];
    }

    // MA1: the missing-art pairs of the low attack (0x5A10B8 animation 21) name these weapons (0x5A1104); the repair turns each into
    // the attack (0x5A1150). A low attack with another weapon is not repaired (its attack goal fails, WA5).
    internal static bool LowAttackRepaired(int weapon) => weapon is >= 1 and <= 8 or 10 or 13;

    /// <summary>The critter ART file a selector's shield, weapon and animation name for this body, armour part and palette:
    /// <c>art/critter/{label}/{label}{armour}{X or S}{weapon}{animation}.art</c>, rules N1 and MA3 (a two-handed sword with a shield is
    /// written as the sword). The repair of a missing file is the loader's (<see cref="Candidates"/>).</summary>
    public string ArtPath(uint selector)
    {
        var animation = (int)((selector >> 6) & 31); var weapon = (int)(selector & 15); var shield = (selector >> 19) & 1;
        if (animation > 24 || weapon >= WeaponCodes.Length) throw new ArgumentOutOfRangeException(nameof(selector), "no critter ART names this animation or weapon");
        return $"art/critter/{Label}/{Label}{ArmorCodes[Armor]}{(shield != 0 ? 'S' : 'X')}{WeaponCode(weapon, (int)shield)}{(char)('a' + animation)}.art";
    }

    /// <summary>The weapon letter of a critter, monster or unique NPC ART name for a weapon part 0..14 (MA3; ARC-RENDER-038 shares it
    /// with the NPCs' armed ARTs): a two-handed sword with a shield is written as the sword.</summary>
    public static char WeaponCode(int weapon, int shield)
    {
        if (weapon < 0 || weapon >= WeaponCodes.Length) throw new ArgumentOutOfRangeException(nameof(weapon));
        return WeaponCodes[shield != 0 && weapon == TwoHandedSword ? Sword : weapon];
    }

    private const int Sword = 3, TwoHandedSword = 7;
}
