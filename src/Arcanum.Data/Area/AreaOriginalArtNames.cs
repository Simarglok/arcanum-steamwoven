using System.Buffers.Binary;
using Arcanum.Data.Objects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Area;

/// <summary>ARC-DATA-023: the original's art names (rules N1-N7 of
/// docs/research/whole-map-art-gaps.md, ledger MA1-MA8). Critter, monster and unique NPC
/// names are normalized and repaired with the missing-art masks, damaged walls take their
/// damage letter, a tile id names its tile file, and a resolved name that no archive holds
/// is drawn from <c>art/badart.art</c>.</summary>
public sealed partial class AreaDataOwner
{
    internal const string BadArtPath = "art/badart.art";
    internal const string BadArtPolicy = "original-badart-v1";
    private const int CritterMasks = 10;
    // The original's 19 checked (animation, weapon) pairs and the animation each one is
    // replaced by when its file is missing (MA1). Animation numbers: 0 stand, 1 walk,
    // 3 stealth walk, 6 run, 7 fall down, 17-19 bloody deaths, 20 attack, 21 low attack,
    // 23 stunned, 24 explode; weapon 0 none, 1 unarmed, 2.. the weapon kinds.
    private static readonly int[] CheckedAnimations = [1, 6, 23, 0, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 17, 18, 19, 24, 3];
    private static readonly int[] CheckedWeapons = [1, 1, 1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 13, 1, 1, 1, 1, 0];
    private static readonly int[] RepairedAnimations = [1, 1, 0, 0, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 7, 7, 7, 7, 1];
    private static readonly string[] ArmorCodes = ["UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD"];
    private static readonly string[] BodyCodes = ["HM", "DF", "GH", "HG", "EF"];
    private const string WeaponCodes = "ABCDEFGHIJKXYNZ";
    // Build-local caches (cleared when a build ends; charged to that build).
    private uint[]? _missingMasks;
    private HashSet<string>? _artHolders;
    private TerrainLoader.TileNames? _objectTiles;
    // ARC-DATA-024: frame facts of each distinct selection decoded by this build (every plan since ARC-DATA-036).
    private Dictionary<(AreaEntryIdentity Entry, int Group, int Frame, int Palette, bool SingleRotationClass, string? Policy), AreaVisualRecord>? _sharedFrames;
    // ARC-DEV-375: the records this build derived for the resident memo, committed after the build's closing authentication (null without a memo).
    private List<KeyValuePair<ResidentVisualKey, AreaVisualRecord>>? _residentStaged;
    private static readonly string[] CritterRoots = ["art/critter/", "art/monster/", "art/unique_npc/"];

    /// <summary>The file a critter (2), monster (12) or unique NPC (13) art id names in the
    /// original: normalized (N1), with the missing-art repair (N2) applied first.</summary>
    [OriginalFunction(0x41D510, OriginalFunctionScope.Partial)]
    private string OriginalCritterPath(uint cls, uint aid, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, CancellationToken token)
    {
        var state = CritterState.From(cls, aid);
        var (animation, weapon) = RepairMissingArt(cls, state.Number, state.Animation, state.Weapon, maps, token);
        return CritterName(cls, state with { Animation = animation, Weapon = weapon }, maps);
    }

    /// <summary>The art fields that name a critter file, after the critter body rules.</summary>
    private readonly record struct CritterState(int Body, int Gender, int Armor, int Shield, int Animation, int Weapon, int Number)
    {
        internal static CritterState From(uint cls, uint aid)
        {
            var animation = (int)((aid >> 6) & 31); var weapon = (int)(aid & 15); var shield = (int)((aid >> 19) & 1);
            var armor = cls == 13 ? 0 : (int)((aid >> 20) & (cls == 2 ? 15U : 7U));
            if (animation > 24 || armor >= ArmorCodes.Length || weapon >= WeaponCodes.Length) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            if (cls != 2) return new(0, 0, armor, shield, animation, weapon, cls == 12 ? (int)((aid >> 23) & 31) : (int)((aid >> 20) & 255));
            var body = (int)((aid >> 24) & 7); var gender = (int)((aid >> 27) & 1);
            if (body >= BodyCodes.Length) throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance);
            // An elf female is drawn with the human body; plate armour (4, 6) is male only and has
            // no elf or halfling art (elf → human, halfling → dwarf).
            if (body == 4 && gender == 0) body = 0;
            if (armor is 4 or 6) { gender = 1; body = body == 4 ? 0 : body == 2 ? 1 : body; }
            return new(body, gender, armor, shield, animation, weapon, gender + 2 * body);
        }
    }

    /// <summary>The weapon and shield rules that follow the repair (N1 step 3), then the name.</summary>
    private static string CritterName(uint cls, CritterState state, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps)
    {
        var (armor, shield, animation, weapon) = (state.Armor, state.Shield, state.Animation, state.Weapon);
        var explode = animation == 24;
        if (explode) { armor = 0; shield = 0; weapon = 0; }
        else if (animation == 23) { weapon = 1; shield = 0; }
        else if (weapon == 1 && animation is 3 or 5) weapon = 0;
        else if (animation is >= 6 and <= 19) { weapon = 0; shield = 0; }
        else if (animation == 1) shield = 0;
        var armorCode = explode ? "XX" : ArmorCodes[armor];
        var suffix = (shield == 0 ? "X" : "S") + WeaponCodes[weapon == 7 && shield != 0 ? 3 : weapon] + (char)('a' + animation) + ".art";
        if (cls == 2)
        {
            var label = BodyCodes[state.Body] + (explode || armor is 4 or 6 ? "X" : state.Gender == 0 ? "F" : "M");
            return "art/critter/" + label + "/" + label + armorCode + suffix;
        }
        var name = Mapped(maps, cls == 12 ? AreaMappingRole.MonsterNames : AreaMappingRole.UniqueNpcNames, state.Number);
        return (cls == 12 ? "art/monster/" : "art/unique_npc/") + name + "/" + name + (cls == 12 ? armorCode : string.Empty) + suffix;
    }

    /// <summary>The missing-art repair (N2, MA1): the checked pairs in order, each against the
    /// animation as replaced so far.</summary>
    [OriginalFunction(0x41DA40, OriginalFunctionScope.Partial)]
    private (int Animation, int Weapon) RepairMissingArt(uint cls, int number, int animation, int weapon,
        Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, CancellationToken token)
    {
        var (start, count) = MaskRange(cls, maps);
        if (number < 0 || number >= count) return (animation, weapon);
        var mask = MissingMasks(maps, token)[start + number];
        for (var pair = 0; pair < CheckedAnimations.Length; pair++)
        {
            if ((mask & (1u << pair)) == 0 || animation != CheckedAnimations[pair] || (animation == 21 && weapon != CheckedWeapons[pair])) continue;
            animation = RepairedAnimations[pair];
            if (animation == 1 && (mask & 1) != 0) weapon = 0;
            else if (animation == 0 && weapon == 0 && (mask & 8) != 0) weapon = 1;
        }
        return (animation, weapon);
    }
    private static (int Start, int Count) MaskRange(uint cls, Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps) => cls switch
    {
        2 => (0, CritterMasks),
        12 => (CritterMasks, NameCount(maps, AreaMappingRole.MonsterNames)),
        _ => (CritterMasks + NameCount(maps, AreaMappingRole.MonsterNames), NameCount(maps, AreaMappingRole.UniqueNpcNames)),
    };
    private static int NameCount(Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, AreaMappingRole role) =>
        maps.TryGetValue(role, out var table) ? table.Count : throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);

    /// <summary>The masks of <c>art/missing.dat</c> when its header equals the current count of
    /// critter, monster and unique NPC ART files and it holds every mask; otherwise rebuilt by
    /// checking each pair's file, as the original rebuilds a stale or absent file (N3, MA4).</summary>
    [OriginalFunction(0x41D2C0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x41D1F0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x41CE60, OriginalFunctionScope.Partial)]
    private uint[] MissingMasks(Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, CancellationToken token)
    {
        if (_missingMasks is not null) return _missingMasks;
        var monsters = NameCount(maps, AreaMappingRole.MonsterNames); var uniques = NameCount(maps, AreaMappingRole.UniqueNpcNames);
        var total = checked(CritterMasks + monsters + uniques);
        var holders = ArtHolders();
        var files = holders.Count(name => CritterFile(name));
        var binding = _plan.Mappings.SingleOrDefault(m => m.Role == AreaMappingRole.MissingPolicy);
        if (binding is not null)
        {
            token.ThrowIfCancellationRequested();
            using var payload = new AreaOwnership(_budget);
            var bytes = ReadEntry(binding.Entry, token, payload);
            if (ObjectWireReader.Hash(bytes) != binding.PayloadSha256) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Mapping);
            if (bytes.Length >= checked(4 * (1 + total)) && BinaryPrimitives.ReadInt32LittleEndian(bytes) == files)
            {
                _budget.Reserve(checked(total * 4L + 4096));
                var read = new uint[total];
                for (var index = 0; index < total; index++) read[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * (index + 1)));
                return _missingMasks = read;
            }
        }
        _budget.Reserve(checked(total * 4L + 4096));
        var rebuilt = new uint[total];
        for (var index = 0; index < total; index++)
        {
            token.ThrowIfCancellationRequested();
            var (cls, number) = index < CritterMasks ? (2u, index) : index < CritterMasks + monsters ? (12u, index - CritterMasks) : (13u, index - CritterMasks - monsters);
            for (var pair = 0; pair < CheckedAnimations.Length; pair++)
            {
                string? name;
                try
                {
                    var state = cls == 2
                        ? CritterState.From(2, (2u << 28) | ((uint)(number & 1) << 27) | ((uint)(number / 2) << 24) | ((uint)CheckedAnimations[pair] << 6) | (uint)CheckedWeapons[pair])
                        : new CritterState(0, 0, 0, 0, CheckedAnimations[pair], CheckedWeapons[pair], number);
                    name = CritterName(cls, state, maps);
                }
                catch (AreaException) { name = null; }
                if (name is null || !holders.Contains(name.ToLowerInvariant())) rebuilt[index] |= 1u << pair;
            }
        }
        return _missingMasks = rebuilt;
    }
    /// <summary>Entry names of the two resource archives (normalized, distinct); with a stack order
    /// (ARC-DATA-018), the file names its sources hold, which is every name the content stack holds
    /// under <c>art/</c>, since each one's winner is a listed source.</summary>
    private HashSet<string> ArtHolders()
    {
        if (_artHolders is not null) return _artHolders;
        if (_plan.ResourceOrder.Count > 0)
        {
            var archives = _plan.ResourceOrder.Where(s => s.Key.Role != AreaSourceRole.Loose && _indexes.ContainsKey(s.Key)).Select(s => _indexes[s.Key]).ToArray();
            _budget.Reserve(checked(archives.Sum(index => (long)index.Count) * 64 + _loose.Count * 64L + 65536));
            var held = new HashSet<string>(_loose.Keys, StringComparer.Ordinal);
            foreach (var index in archives)
                foreach (var (name, entries) in index)
                    if (entries.Any(e => (e.Flags & (DirectoryRecord | IgnoredRecord)) == 0)) held.Add(name);
            return _artHolders = held;
        }
        var indexes = new[] { AreaSourceRole.Resource1, AreaSourceRole.Resource2 }.Select(role => _indexes.TryGetValue(new(role), out var index) ? index : null).OfType<Dictionary<string, DatEntry[]>>().ToArray();
        _budget.Reserve(checked(indexes.Sum(index => (long)index.Count) * 64 + 65536));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in indexes) names.UnionWith(index.Keys);
        return _artHolders = names;
    }
    /// <summary>A file the original counts for the missing-art header: one folder below
    /// <c>art/critter</c>, <c>art/monster</c> or <c>art/unique_npc</c>, ending in <c>.art</c>.</summary>
    internal static bool CritterFile(string name)
    {
        foreach (var root in CritterRoots)
        {
            if (!name.StartsWith(root, StringComparison.Ordinal)) continue;
            var rest = name.AsSpan(root.Length); var slash = rest.IndexOf('/');
            return slash > 0 && rest[(slash + 1)..].IndexOf('/') < 0 && rest.Length > slash + 1 && rest.EndsWith(".art", StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>The damage letter and piece of a damaged wall (N6, MA7): the two damage bits
    /// swap at rotations 2, 3, 6 and 7; 0x400 names L (piece 7 becomes the base piece), 0x80
    /// names L for pieces 1-6 and R otherwise (piece 8 becomes the base piece).</summary>
    internal static (char Letter, int Piece) WallDamage(uint aid, int rotation, int piece)
    {
        var damage = aid & 0x480;
        if (rotation is 2 or 3 or 6 or 7) damage = ((damage & 0x400) != 0 ? 0x80u : 0) | ((damage & 0x80) != 0 ? 0x400u : 0);
        if ((damage & 0x400) != 0) return ('l', piece == 7 ? 0 : piece);
        if ((damage & 0x80) != 0) { if (piece == 8) piece = 0; return (piece is >= 1 and <= 6 ? 'l' : 'r', piece); }
        return ('u', piece);
    }

    /// <summary>The entry a resolved name selects, or <c>art/badart.art</c> when no resource
    /// archive holds the name (N4, MA5). Ambiguous names stay refused.</summary>
    private (AreaEntryIdentity Entry, bool BadArt) ResolveOrBadArt(string path)
    {
        try { return (Resolve(path), false); }
        catch (AreaException error) when (error.Issue.Code == AreaErrorCode.MissingSource)
        {
            return (ClosedUnique(BadArtPath), true);
        }
    }
}
