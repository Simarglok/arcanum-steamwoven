using Arcanum.Data;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Sound;

namespace Arcanum.TerrainViewer.Core;

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
public readonly record struct HeroBody(int Body, int Gender, int Armor = 0, int Palette = 0, int Weapon = 0, int Shield = 0)
{
    private static readonly string[] BodyCodes = ["HM", "DF", "GH", "HG", "EF"];
    private static readonly string[] ArmorCodes = ["UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD"];
    private const string WeaponCodes = "ABCDEFGHIJKXYNZ";

    /// <summary>The body the fixed diagnostic human is drawn with (<see cref="ActorDiagnosticProfile.HumanFixedArea"/>).</summary>
    public static HeroBody HumanFemale { get; } = new(0, 0);

    /// <summary>The hero's body for a race, gender, armour part (<see cref="Armor"/>) and palette (<see cref="Palette"/>), or null for a
    /// race outside the creation table, an armour part without a code or a palette outside 0..3.</summary>
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
    public string Label => Armor is 4 or 6 ? BodyCodes[Body switch { 4 => 0, 2 => 1, _ => Body }] + "X" : BodyCodes[Body] + (Gender == 0 ? "F" : "M");

    /// <summary>Dwarf and halfling bodies add 4 to every walk and run frame rate (research L6; the rate 0x437990 asks the art id's
    /// body, 0x503EA0, for 1 or 2).</summary>
    public int GaitRateBonus => Body is 1 or 2 ? 4 : 0;

    /// <summary>
    /// The fixed human's art selector with this body, gender, armour part and palette, the same animation, and the weapon and shield the
    /// file of that animation shows (ARC-RENDER-037, MA2): the stand both, the walk the weapon only, the attacks (20, 21) both (the
    /// human's bare hands when the body shows no weapon: out of combat mode nothing attacks), the animations 6..19 (run, fall, get up,
    /// vault) neither.
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
            _ => (Weapon, Shield),
        };
        return (humanSelector & ~0x0FF8003Fu) | BodyBits | ((uint)shield << 19) | (uint)weapon;
    }

    /// <summary>
    /// The selectors <see cref="HeroBodyArt"/> tries for an action, in order (the missing-art repair, MA1): <see cref="Selector"/>, then
    /// for an armed walk the walk without the weapon, and for a stand without a weapon the stand with bare hands.
    /// </summary>
    public IReadOnlyList<uint> Candidates(uint humanSelector)
    {
        var selector = Selector(humanSelector);
        var animation = (selector >> 6) & 31;
        return animation == 1 && (selector & 15) != 0 ? [selector, selector & ~15u]
            : animation == 0 && (selector & 15) == 0 ? [selector, selector | WeaponArtRules.Unarmed]
            : [selector];
    }

    /// <summary>The critter ART file a selector's shield, weapon and animation name for this body, armour part and palette:
    /// <c>art/critter/{label}/{label}{armour}{X or S}{weapon}{animation}.art</c>, rules N1 and MA3 (a two-handed sword with a shield is
    /// written as the sword). The repair of a missing file is the loader's (<see cref="Candidates"/>).</summary>
    public string ArtPath(uint selector)
    {
        var animation = (int)((selector >> 6) & 31); var weapon = (int)(selector & 15); var shield = (selector >> 19) & 1;
        if (animation > 24 || weapon >= WeaponCodes.Length) throw new ArgumentOutOfRangeException(nameof(selector), "no critter ART names this animation or weapon");
        if (shield != 0 && weapon == TwoHandedSword) weapon = Sword;
        return $"art/critter/{Label}/{Label}{ArmorCodes[Armor]}{(shield != 0 ? 'S' : 'X')}{WeaponCodes[weapon]}{(char)('a' + animation)}.art";
    }

    private const int Sword = 3, TwoHandedSword = 7;
}

/// <summary>A hero body's decoded ART: its profile, the gait timing from its strides and every frame the facing table reaches.</summary>
public sealed record HeroBodyLoad(HeroBody Body, ActorDiagnosticProfile Profile, GaitTiming Timing, IReadOnlyList<ActorDiagnosticFrame> Frames,
    IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> Textures, long RgbaBytes)
{
    /// <summary>The actor plan of this body at a placement (its action, facing and frame; the frame's geometry is filled in).</summary>
    public ActorDiagnosticScenePlan Plan(ActorDiagnosticPlacementInput input) => PlanOf(Profile, Frames, input);

    /// <summary>The actor plan of a profile's decoded frames at a placement, the frame's geometry filled in; a typed
    /// <see cref="TerrainViewerException"/> when the placement's frame is not among them.</summary>
    public static ActorDiagnosticScenePlan PlanOf(ActorDiagnosticProfile profile, IReadOnlyList<ActorDiagnosticFrame> frames, ActorDiagnosticPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(frames); ArgumentNullException.ThrowIfNull(input);
        var facing = profile.Facing(input.Action, input.Facing);
        var selector = profile.Resource(input.Action).Selector;
        var matches = frames.Where(f => f.TextureKey.Selector == selector && f.TextureKey.PhysicalGroup == facing.PhysicalGroup
            && f.TextureKey.Frame == input.Frame).Take(2).ToArray();
        if (matches.Length != 1) throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorFrame, Stage: TerrainViewerFailureStage.ActorFrame));
        var frame = matches[0];
        return ActorDiagnosticScenePlan.CreateSynthetic(input with { Width = frame.Width, Height = frame.Height, HotX = frame.HotX, HotY = frame.HotY,
            ArtOffsetX = frame.ArtOffsetX, ArtOffsetY = frame.ArtOffsetY }, frames, profile);
    }
}

/// <summary>ARC-UI-039: reads a hero body's five ARTs (stand, walk, run, vault, unarmed attack) by name from the actor archive
/// (resource 1, where every critter ART of the installation is; SH-04) and decodes the frames the fixed facing table draws. The
/// header gives frames, rate and action frame; the walk and run strides are the frame offsets of one cycle (research L7). Since
/// ARC-COMBAT-021 also the fall-down and get-up ARTs (animations 7 and 8) when the body has both. Since ARC-RENDER-037 the stand and
/// attack show the body's weapon and shield (<see cref="HeroBody.Selector"/>), a missing armed walk the walk without it. Typed failures as
/// <see cref="TerrainViewerException"/>; nothing is retained on failure.</summary>
public static class HeroBodyArt
{
    /// <summary>Upper bounds of one body: distinct frames and their RGBA bytes (the human uses 195 and 1,705,144).</summary>
    public const int MaxKeys = 512;
    public const long MaxRgbaBytes = 16L * 1024 * 1024;

    public static HeroBodyLoad Load(DatArchive resource, HeroBody body, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (body.Body is < 0 or > 4 || body.Gender is not (0 or 1) || body.Armor is < 0 or > 8 || body.Palette is < 0 or > 3
            || body.Weapon is < 0 or >= WeaponArtRules.WeaponParts || body.Shield is not (0 or 1)) throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        var human = ActorDiagnosticProfile.HumanFixedArea;
        var palette = body.Palette;
        var options = new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = 8 * 64 };
        var read = new Dictionary<ActorDiagnosticAction, (ActorDiagnosticResource Resource, byte[] Bytes, DatEntry Entry, ArtMetadata Metadata)>();
        try
        {
            // ARC-COMBAT-021: the fall-down and get-up ARTs follow the five; a body without both, or with one that is not one entry
            // with a readable header in the body's palette (none of the installed creatable bodies), loads without them, and its hero
            // then lies down without a drawn fall.
            var wanted = human.Actions.Select(action => (Action: action, Selector: human.Resource(action).Selector, Optional: false))
                .Append((ActorDiagnosticAction.FallDown, ActorDiagnosticProfile.HumanFallDownSelector, true))
                .Append((ActorDiagnosticAction.GetUp, ActorDiagnosticProfile.HumanGetUpSelector, true));
            foreach (var (action, humanSelector, optional) in wanted)
            {
                token.ThrowIfCancellationRequested();
                // ARC-RENDER-037: the first of the repair's candidates (MA1) the archive holds (a name held twice fails below); an armed
                // body walks without its weapon.
                var (selector, entries) = body.Candidates(humanSelector).Select(candidate => (Selector: candidate, Entries: Named(resource, body.ArtPath(candidate))))
                    .FirstOrDefault(found => found.Entries.Length != 0, (body.Selector(humanSelector), []));
                if (entries.Length != 1)
                {
                    if (optional) continue;
                    throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                }
                var bytes = resource.ReadEntry(entries[0]);
                ArtMetadata metadata;
                try { metadata = ArtDecoder.Inspect(bytes, options); }
                catch (ArtException) when (optional) { continue; }
                var h = metadata.Header;
                if (h.PhysicalGroupCount != 8 || h.FramesPerGroup <= 0 || h.FramesPerSecond <= 0 || h.PalettePresenceWords.Count <= palette
                    || h.PalettePresenceWords[palette] == 0 || (action == ActorDiagnosticAction.Attack && (h.ActionFrame < 0 || h.ActionFrame >= h.FramesPerGroup)))
                {
                    if (optional) continue;
                    throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                }
                var descriptor = new ActorDiagnosticResource(action, selector, entries[0].Ordinal, palette, 8, h.FramesPerGroup, h.FramesPerSecond, h.FramesPerSecond)
                {
                    Stride = action is ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run ? Stride(metadata) : null,
                    ActionFrame = action == ActorDiagnosticAction.Attack ? h.ActionFrame : 0,
                };
                if (descriptor.Stride is { IsValid: false }) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                read.Add(action, (descriptor, bytes, entries[0], metadata));
            }
            // Every action shares the fixed facing table, so the human's used groups are every action's. The fall and get-up come only
            // together and only within the key cap.
            var groups = human.UsedGroups(ActorDiagnosticAction.Idle).Count();
            var keys = human.Actions.Sum(action => groups * read[action].Resource.FramesPerGroup);
            var falls = read.TryGetValue(ActorDiagnosticAction.FallDown, out var fall) && read.TryGetValue(ActorDiagnosticAction.GetUp, out var rise)
                && keys + groups * (fall.Resource.FramesPerGroup + rise.Resource.FramesPerGroup) <= MaxKeys;
            if (falls) keys += groups * (read[ActorDiagnosticAction.FallDown].Resource.FramesPerGroup + read[ActorDiagnosticAction.GetUp].Resource.FramesPerGroup);
            if (keys > MaxKeys) throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
            var profile = ActorDiagnosticProfile.ForHeroBody(read[ActorDiagnosticAction.Idle].Resource, read[ActorDiagnosticAction.Walk].Resource,
                read[ActorDiagnosticAction.Run].Resource, read[ActorDiagnosticAction.Vault].Resource, read[ActorDiagnosticAction.Attack].Resource, keys,
                falls ? read[ActorDiagnosticAction.FallDown].Resource : null, falls ? read[ActorDiagnosticAction.GetUp].Resource : null);
            var frames = new List<ActorDiagnosticFrame>(keys);
            var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>(keys);
            long rgbaBytes = 0;
            foreach (var action in profile.Actions)
            {
                var (descriptor, bytes, entry, _) = read[action];
                var art = HdFrameQueries.ArtIdentity(bytes, entry.RawNameBytes);
                foreach (var group in profile.UsedGroups(action))
                for (var frame = 0; frame < descriptor.FramesPerGroup; frame++)
                {
                    token.ThrowIfCancellationRequested();
                    var key = new ActorDiagnosticTextureKey(descriptor.Ordinal, descriptor.Selector, group, frame, palette);
                    var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, palette), options);
                    if (decoded.EffectivePaletteSlot != palette || decoded.Width <= 0 || decoded.Height <= 0)
                        throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                    rgbaBytes = checked(rgbaBytes + TerrainCachePolicy.CheckedRgbaBytes(decoded.Width, decoded.Height));
                    if (rgbaBytes > MaxRgbaBytes) throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
                    var rgba = decoded.Rgba.ToArray();
                    textures.Add(key, new DecodedActorTexture(key, decoded.Width, decoded.Height, rgba)
                    { HdQuery = HdFrameQueries.FromArt(art.PayloadSha256, art.ViewLength, art.NameSha256, group, frame, decoded.EffectivePaletteSlot, decoded.Width, decoded.Height, rgba) });
                    var geometry = decoded.Frame;
                    frames.Add(new ActorDiagnosticFrame(key, decoded.Width, decoded.Height, geometry.HotX, geometry.HotY, geometry.OffsetX, geometry.OffsetY,
                        descriptor.FramesPerGroup, descriptor.RawFramesPerSecond));
                }
            }
            var timing = new GaitTiming(profile.Walk.Stride!, profile.Run!.Stride!, GaitTiming.BaseSpeed, profile.Vault!.FramesPerGroup,
                profile.Vault.RawFramesPerSecond, body.GaitRateBonus);
            return new(body, profile, timing, frames.AsReadOnly(), textures.AsReadOnly(), rgbaBytes);
        }
        catch (TerrainViewerException) { throw; }
        catch (DatException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArtException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (IOException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArgumentException) { throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata); }
        catch (OverflowException) { throw Failure(TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode); }
    }

    /// <summary>
    /// The stride of a walk or run ART (research L7): the offsets of one cycle on an axis rotation (group 1) and on a diagonal
    /// rotation (group 2), as positive pixels; <see cref="ActorDiagnosticProfile.HumanFixedArea"/>'s strides restate the same sums.
    /// </summary>
    public static GaitStride Stride(ArtMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        (int X, int Y) Sum(int group)
        {
            var (x, y) = (0, 0);
            for (var frame = 0; frame < metadata.Header.FramesPerGroup; frame++) { var row = metadata.GetFrame(group, frame); x += row.OffsetX; y += row.OffsetY; }
            return (x, y);
        }
        var (axisX, axisY) = Sum(1); var (diagonal, _) = Sum(2);
        return new(metadata.Header.FramesPerGroup, metadata.Header.FramesPerSecond, Math.Abs(axisX), Math.Abs(axisY), Math.Abs(diagonal));
    }

    private static DatEntry[] Named(DatArchive resource, string path) =>
        resource.Entries.Where(entry => string.Equals(Name(entry), path, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string Name(DatEntry entry)
    {
        var raw = entry.RawNameBytes;
        var length = raw.Count > 0 && raw[^1] == 0 ? raw.Count - 1 : raw.Count;
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = raw[i] == (byte)'\\' ? '/' : (char)raw[i];
        return new string(chars);
    }

    private static TerrainViewerException Failure(TerrainViewerErrorCode code, TerrainViewerFailureStage stage) => new(new TerrainViewerError(code, Stage: stage));
}
