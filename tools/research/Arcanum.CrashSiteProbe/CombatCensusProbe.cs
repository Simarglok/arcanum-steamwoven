using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Inventory;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-041: read-only census of the combat-domain data (section 10 of
// docs/research/combat-domain-load-map.md) over the module content stack. It reports which layer holds every
// file of the load map's catalogue, the AI parameter rows, the spell AI fields, the effect operators, prototype
// ranges, NPC flags, factions and item AI actions, and the NPCs and creatures of the Crash Site exterior and cave.
// Aggregates only: counts, numbers, entry and prototype numbers, flag and key names; never text or payload bytes.
// Every parser here follows the source reading recorded in the load map (docs/research/combat-domain-load-map.md); it is a census, not a production reader.
internal static partial class Program
{
    private const int CcAiParams = 17, CcBaseAiRows = 100, CcModuleAiFirst = 100, CcModuleAiRows = 150;
    private const int CcSpells = 223, CcSpellComponentLines = 44, CcNoSpell = 10000;
    private const int CcWieldedWeapon = 1004;
    private const int CcItemParentSlot = 37;
    private const uint CcValidAiAction = 0x00200000, CcMonsterFlag = 0x00008000;
    private const uint CcGrenade = 0x10, CcHealingItem = 0x8;

    /// <summary>The data files of section 3 of the load map with the phase that reads them: init files come from the
    /// base only in the original, module files are read after the module is mounted.</summary>
    internal static readonly (string Path, string Phase)[] CcFiles =
    [
        ("rules/ai_params.mes", "init"), ("rules/gameai.mes", "module"),
        ("rules/magictech.mes", "init"), ("mes/spell.mes", "init"), ("rules/spelllist.mes", "init"), ("rules/spelleyecandy.mes", "init"),
        ("mes/tech.mes", "init"), ("rules/effect.mes", "init"), ("rules/gameeffect.mes", "module"),
        ("mes/item_effect.mes", "init"), ("mes/gameitemeffect.mes", "module"), ("mes/gamebless.mes", "module"), ("mes/gamecurse.mes", "module"),
        ("mes/combat.mes", "init"), ("rules/combateyecandy.mes", "init"), ("mes/skill.mes", "init"), ("mes/stat.mes", "init"),
        ("rules/xp_level.mes", "init"), ("rules/gamelevel.mes", "init"), ("rules/userlevel.mes", "init"), ("mes/gamelevelname.mes", "init"),
        ("mes/userlevelname.mes", "init"), ("mes/level.mes", "init"), ("rules/xp_critter.mes", "init"), ("mes/critter.mes", "init"),
        ("rules/backgrnd.mes", "init"), ("mes/gameback.mes", "init"), ("mes/reaction.mes", "init"),
        ("rules/gamerep.mes", "module"), ("mes/gamereplog.mes", "module"), ("mes/game_rp_npc_m2m.mes", "module"), ("mes/game_rp_npc_m2f.mes", "module"),
        ("mes/game_rp_npc_f2f.mes", "module"), ("mes/game_rp_npc_f2m.mes", "module"),
        ("rules/invensource.mes", "init"), ("rules/invensourcebuy.mes", "init"), ("rules/trapeyecandy.mes", "init"), ("mes/trap.mes", "init"),
        ("mes/item.mes", "init"), ("rules/animeyecandy.mes", "init"), ("rules/weaponeyecandy.mes", "init"),
        ("rules/npc.txt", "init"), ("rules/monster.txt", "init"), ("rules/unique.txt", "init"),
        ("oemes/oname.mes", "init"), ("oemes/gameoname.mes", "module"), ("oemes/faction.mes", "init"), ("oemes/gamefaction.mes", "module"),
        ("mes/description.mes", "init"), ("mes/gamedesc.mes", "module"), ("rules/scripteyecandy.mes", "init"), ("mes/storystate.mes", "module"),
        ("rules/maplist.mes", "module"),
    ];

    /// <summary>Description-number ranges of the pinned <c>proto.c</c> tables (start inclusive, end exclusive); a prototype
    /// file number is its description number + 20. The last two are named by number because their labels conflict
    /// (LM15): <c>proto.c</c> calls 27309.. monsters and 28310.. unique NPCs, the description header the opposite.</summary>
    internal static readonly (string Label, int Start, int End)[] CcRanges =
    [
        ("wall", 1000, 1008), ("portal", 2008, 2023), ("container", 3023, 3061), ("scenery", 4006, 4051), ("projectile", 5028, 5029),
        ("weapon", 6029, 6179), ("ammo", 7038, 7042), ("armor", 8042, 8293), ("gold", 9056, 9057), ("food", 10057, 10145),
        ("scroll", 11059, 11143), ("key", 12060, 12063), ("key_ring", 13061, 13062), ("written", 14062, 14130), ("generic", 15065, 15213),
        ("pc", 16066, 16079), ("npc", 17067, 17317), ("trap", 26000, 26008), ("critter_27309", 27309, 27394), ("critter_28310", 28310, 28472),
    ];

    internal static readonly string[] CcNpcFlags =
    [
        "FIGHTING", "WAYPOINTS_DAY", "WAYPOINTS_NIGHT", "AI_WAIT_HERE", "AI_SPREAD_OUT", "JILTED", "CHECK_WIELD", "CHECK_WEAPON", "KOS",
        "WAYPOINTS_BED", "FORCED_FOLLOWER", "KOS_OVERRIDE", "WANDERS", "WANDERS_IN_DARK", "FENCE", "FAMILIAR", "CHECK_LEADER", "ALOOF",
        "CAST_HIGHEST", "GENERATOR", "GENERATED", "GENERATOR_RATE1", "GENERATOR_RATE2", "GENERATOR_RATE3", "DEMAINTAIN_SPELLS",
        "LOOK_FOR_WEAPON", "LOOK_FOR_ARMOR", "LOOK_FOR_AMMO", "BACKING_OFF", "NO_ATTACK", "CHECK_GRENADE",
    ];

    internal static readonly string[] CcCritterFlags =
    [
        "IS_CONCEALED", "MOVING_SILENTLY", "UNDEAD", "ANIMAL", "FLEEING", "STUNNED", "PARALYZED", "BLINDED", "CRIPPLED_ARMS_ONE",
        "CRIPPLED_ARMS_BOTH", "CRIPPLED_LEGS_BOTH", "UNUSED", "SLEEPING", "MUTE", "SURRENDERED", "MONSTER", "SPELL_FLEE", "ENCOUNTER",
        "COMBAT_MODE_ACTIVE", "LIGHT_SMALL", "LIGHT_MEDIUM", "LIGHT_LARGE", "LIGHT_XLARGE", "UNREVIVIFIABLE", "UNRESSURECTABLE", "DEMON",
        "FATIGUE_IMMUNE", "NO_FLEE", "NON_LETHAL_COMBAT", "MECHANICAL", "ANIMAL_ENSHROUD", "FATIGUE_LIMITING",
    ];

    /// <summary>The stat key table <c>Resist:</c> names are matched against (case-insensitive); entries 21 and 22 hold the
    /// poison-level and age keys (LM17, an original bug per ARC-RES-048); the source's last five entries are null and left out.</summary>
    internal static readonly string[] CcStatKeys =
    [
        "stat_strength", "stat_dexterity", "stat_constitution", "stat_beauty", "stat_intelligence", "stat_perception", "stat_willpower",
        "stat_charisma", "stat_carry_weight", "stat_damage_bonus", "stat_ac_adjustment", "stat_speed", "stat_heal_rate",
        "stat_poison_recovery_rate", "stat_reaction_modifier", "stat_max_followers", "stat_magic_tech_aptitude", "stat_level",
        "stat_experience_points", "stat_alignment", "stat_fate_points", "stat_poison_level", "stat_age",
    ];

    internal static readonly string[] CcSpellInfo = ["neutral", "friendly", "aggressive", "may_be_aggressive"];

    internal static readonly string[] CcAiActions =
    [
        "AI_Flee:", "AI_Summon:", "AI_Defensive:", "AI_Offensive:", "AI_HealingLight:", "AI_HealingMedium:", "AI_HealingHeavy:",
        "AI_CurePoison:", "AI_FatigueRecover:", "AI_Resurrect:",
    ];
    internal static readonly string[] CcLine4Keys = [.. CcAiActions, "No_Resist:", "No_Reflect:"];
    internal static readonly string[] CcLine1Keys =
    [
        "IQ:", "Cost:", "Maintain:", "Duration:", "DurationStatInfo:", "DurationTriggerCount:", "Resist:", "Range:", "ChargesFromCells:",
        "ChargeBeginCost:", "Info:", "Disabled:",
    ];

    private static int ProbeCombatCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("COMBAT_CENSUS_USAGE --combat-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"COMBAT_CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"COMBAT_CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        Console.WriteLine(JsonSerializer.Serialize(CombatCensus(stack), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>A source label without a local path: the role, plus the archive's file name for archives.</summary>
    internal static string CcLayer(ContentSource source) =>
        source.Kind == ContentSourceKind.Archive ? $"{source.Role}:{Path.GetFileName(source.Path).ToLowerInvariant()}" : source.Role.ToString();

    internal static bool CcIsModule(ContentSourceRole role) => role is ContentSourceRole.ModuleArchive or ContentSourceRole.ModulePatch or ContentSourceRole.ModuleDirectory;

    /// <summary>The candidate the original reads: init files see only the base, so the first non-module candidate;
    /// module-phase files the stack winner.</summary>
    internal static ContentCandidate? CcOriginalCandidate(ContentResolution resolution, string phase) =>
        resolution.Status != ContentResolutionStatus.Resolved ? null
        : phase == "init" ? resolution.Candidates.FirstOrDefault(c => !CcIsModule(c.Source.Role)) : resolution.Winner;

    private static object CombatCensus(ContentStack stack)
    {
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|COMBAT_CENSUS_READ_{result.Status}");
            return null;
        }
        var tables = new Dictionary<string, MesFile>(StringComparer.Ordinal);

        // 1. Layers of every catalogue file, and the tables the rest of the census reads (the original's copy).
        var files = new List<object>();
        foreach (var (path, phase) in CcFiles)
        {
            var resolution = stack.Resolve(path);
            if (resolution.Status != ContentResolutionStatus.Resolved) { files.Add(new { path, phase, present = false }); continue; }
            var original = CcOriginalCandidate(resolution, phase);
            string? status = null; int? messages = null; int? first = null, last = null;
            if (original is not null && path.EndsWith(".mes", StringComparison.Ordinal) && Read(original, "mes") is { } bytes)
            {
                var parsed = MesFile.Parse(bytes);
                status = parsed.Status.ToString();
                if (parsed.File is { } mes) { tables[path] = mes; messages = mes.Count; first = mes.Messages.FirstOrDefault()?.Number; last = mes.LastNumber; }
            }
            files.Add(new
            {
                path, phase, present = true,
                winner = CcLayer(resolution.Winner!.Source),
                copies = resolution.Candidates.Select(c => CcLayer(c.Source)).ToArray(),
                original_reads = original is null ? null : CcLayer(original.Source),
                init_file_shadowed_by_module = phase == "init" && CcIsModule(resolution.Winner.Source.Role),
                mes_status = status, messages, first, last,
            });
        }

        // 2. AI parameter rows.
        var baseAi = tables.TryGetValue("rules/ai_params.mes", out var aiBase) ? CcAiTable(aiBase, 0, CcBaseAiRows) : null;
        var moduleAi = tables.TryGetValue("rules/gameai.mes", out var aiModule) ? CcAiTable(aiModule, CcModuleAiFirst, CcModuleAiRows) : null;

        // 3. Spell records.
        var spells = tables.TryGetValue("rules/spelllist.mes", out var spellList) ? CcSpellCensus(spellList) : null;

        // 4. Effects.
        var effects = new
        {
            base_table = tables.TryGetValue("rules/effect.mes", out var effectBase) ? CcEffectCensus(effectBase, EffectRules.FirstNumber, EffectRules.LastNumber) : null,
            module_table = tables.TryGetValue("rules/gameeffect.mes", out var effectModule) ? CcEffectCensus(effectModule, 0, 49) : null,
        };

        // 5. Prototypes through the catalogue (the base files the original reads, ARC-DATA-026) and the combat-field reader.
        var catalog = WorldCatalog.Open(stack);
        var prototypeObjects = new Dictionary<int, WorldObject>();
        var prototypeFields = new Dictionary<int, CombatFields>();
        var prototypeLayers = new CensusTally(); var prototypeRefusals = new CensusTally();
        PrototypeFile Lookup(int number)
        {
            if (catalog.Prototypes.Candidate(number) is not { } candidate) return catalog.Prototypes.Read(number);
            return Read(candidate, "prototype") is { } bytes ? new(PrototypeFileStatus.Found, bytes) : new(PrototypeFileStatus.Unreadable, null);
        }
        foreach (var number in catalog.PrototypeNumbers.Order())
        {
            prototypeLayers.Add(CcLayer(catalog.Prototypes.Candidate(number)!.Source));
            var read = catalog.ReadPrototype(number);
            if (read.Prototype is null) { prototypeRefusals.Add($"catalog|{read.Issue?.Code}"); continue; }
            prototypeObjects[number] = read.Prototype;
            var fields = CombatFieldReader.ReadPrototype(Lookup, number);
            if (fields.Fields is not null) prototypeFields[number] = fields.Fields;
            else prototypeRefusals.Add($"combat|{fields.Failure}");
        }
        var prototypes = CcPrototypeCensus(prototypeObjects, prototypeFields);

        // 6a. Every map's mobile NPCs and their carried weapons, by effective fields.
        var world = CcWorldInstances(catalog, prototypeObjects);

        // 6b. The Crash Site exterior and cave through the pinned Area profiles.
        var areas = new
        {
            exterior = CcArea(stack, catalog, tables, 5000, cave: false, baseAi),
            cave = CcArea(stack, catalog, tables, 5078, cave: true, baseAi),
        };

        return new
        {
            module = stack.Plan.ModuleName,
            files,
            prototype_layers = new { read_from = prototypeLayers.Snapshot(), ignored_module_copies = catalog.Prototypes.IgnoredModuleKeys.Count },
            ai = new { base_table = baseAi, module_table = moduleAi },
            spells,
            effects,
            prototypes = new { refusals = prototypeRefusals.Snapshot(), census = prototypes },
            world_instances = world,
            areas,
            refusals = refusals.Snapshot(),
        };
    }

    // ---- AI parameter rows ----

    internal sealed record CcColumn(int Index, int Min, int Max, int Distinct, IReadOnlyDictionary<int, int>? Values);

    internal sealed record CcAiTableResult(int Entries, int? First, int? Last, IReadOnlyList<int> Gaps, int OutsideRows, bool FirstEntryPresent,
        int Rows, bool ByNumberEqualsByPosition, IReadOnlyDictionary<int, int> TokensPerRow, int NonNumericTokens, IReadOnlyList<CcColumn> Columns,
        IReadOnlyDictionary<int, IReadOnlyList<int>> RowValues);

    /// <summary>C <c>atoi</c>: leading whitespace, an optional sign, then digits; anything else stops it.</summary>
    internal static int CcAtoi(string text, out bool whole)
    {
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var start = i; var negative = false;
        if (i < text.Length && text[i] is '+' or '-') { negative = text[i] == '-'; i++; }
        long value = 0; var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) { value = Math.Min(value * 10 + (text[i] - '0'), (long)int.MaxValue + 1); i++; digits++; }
        whole = digits > 0 && i == text.Length && start == 0;
        return (int)Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
    }

    /// <summary>The AI rows as the original loads them: the entry numbered <paramref name="first"/> must exist; from it,
    /// each next entry by position fills the next row up to <paramref name="limit"/>, whatever its number (LM3); each
    /// row is up to 17 integers split on space, tab and line feed, a missing trailing value staying 0.</summary>
    internal static CcAiTableResult CcAiTable(MesFile mes, int first, int limit)
    {
        var numbers = mes.Messages.Select(m => m.Number).ToArray();
        var gaps = new List<int>();
        for (var i = 1; i < numbers.Length; i++) for (var n = numbers[i - 1] + 1; n < numbers[i] && gaps.Count < 64; n++) gaps.Add(n);
        var start = Array.IndexOf(numbers, first);
        var rows = new SortedDictionary<int, IReadOnlyList<int>>();
        var tokensPerRow = new SortedDictionary<int, int>(); var nonNumeric = 0;
        if (start >= 0)
            for (int position = start, row = first; position < numbers.Length && row < limit; position++, row++)
            {
                var tokens = Encoding.Latin1.GetString(mes.Messages[position].Bytes.Span).Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);
                tokensPerRow[tokens.Length] = tokensPerRow.GetValueOrDefault(tokens.Length) + 1;
                var values = new int[CcAiParams];
                for (var p = 0; p < Math.Min(tokens.Length, CcAiParams); p++)
                {
                    values[p] = CcAtoi(tokens[p], out var whole);
                    if (!whole) nonNumeric++;
                }
                rows[row] = values;
            }
        var columns = Enumerable.Range(0, CcAiParams).Select(c =>
        {
            var values = rows.Values.Select(r => r[c]).ToArray();
            if (values.Length == 0) return new CcColumn(c, 0, 0, 0, null);
            var tally = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
            return new CcColumn(c, values.Min(), values.Max(), tally.Count, tally.Count <= 8 ? new SortedDictionary<int, int>(tally) : null);
        }).ToArray();
        var outside = numbers.Count(n => n < first || n >= limit);
        var byNumber = start >= 0 && rows.Count == numbers.Count(n => n >= first && n < limit) && rows.Keys.All(r => numbers[start + (r - first)] == r);
        return new(numbers.Length, numbers.Length == 0 ? null : numbers[0], numbers.Length == 0 ? null : numbers[^1], gaps, outside, start >= 0,
            rows.Count, byNumber, tokensPerRow, nonNumeric, columns, rows);
    }

    // ---- Spell records ----

    /// <summary>The first message number of spell record <paramref name="spell"/> (0..222): 1000 + 50·n, and 2000 more
    /// for the technological records from 140.</summary>
    internal static int CcSpellBase(int spell) => 1000 + 50 * spell + (spell >= 140 ? 2000 : 0);

    /// <summary>The source's named-key cursor: whitespace is skipped, a key must start
    /// exactly at the cursor (case-sensitive), a value ends at the next comma, a key out of order is not found and
    /// leaves the cursor where it is. <see cref="Exit"/> records where the original would end the process.</summary>
    internal sealed class CcNamedLine(string text)
    {
        private string? _rest = text;
        public bool Exit { get; private set; }

        private static string Skip(string text)
        {
            var i = 0;
            while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
            return text[i..];
        }

        public string? Value(string key)
        {
            if (_rest is null || Exit) return null;
            var at = Skip(_rest); _rest = at;
            if (!at.StartsWith(key, StringComparison.Ordinal)) return null;
            at = Skip(at[key.Length..]);
            var comma = at.IndexOf(',');
            if (comma < 0) { _rest = null; return at; }
            _rest = at[(comma + 1)..];
            return at[..comma];
        }

        /// <summary>A <c>(a@b)</c> value: a missing <c>(</c> ends the original's process.</summary>
        public (string First, string? Second)? Complex(string key)
        {
            if (Value(key) is not { } value) return null;
            if (!value.StartsWith('(')) { Exit = true; return null; }
            var inner = value[1..];
            var at = inner.IndexOf('@');
            return at < 0 ? (inner, null) : (inner[..at], inner[(at + 1)..]);
        }

        /// <summary>The key of every comma-separated segment the cursor did not consume.</summary>
        public IReadOnlyList<string> Leftover()
        {
            if (_rest is null || Exit) return [];
            return CcSegmentKeys(_rest).ToArray();
        }
    }

    /// <summary>The key (text up to and including the first colon) of every comma-separated segment.</summary>
    internal static IEnumerable<string> CcSegmentKeys(string text) =>
        text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(s => s.IndexOf(':') is var colon and >= 0 ? s[..(colon + 1)] : "<no key>");

    internal sealed record CcSpellLine1(int? Iq, string? ResistStat, bool ResistMatched, string? Info, bool Disabled, bool Exit, IReadOnlyList<string> Leftover);

    internal static CcSpellLine1 CcParseLine1(string text)
    {
        var line = new CcNamedLine(text);
        int? iq = line.Value("IQ:") is { } iqText ? CcAtoi(iqText, out _) : null;
        line.Value("Cost:");
        line.Complex("Maintain:");
        if (line.Complex("Duration:") is { } duration && CcAtoi(duration.Second ?? duration.First, out _) != -1) line.Complex("DurationStatInfo:");
        line.Value("DurationTriggerCount:");
        string? resist = null; var matched = false;
        if (line.Complex("Resist:") is { } r)
        {
            // The stat name ends at the first space; the original ends the process when no table entry matches.
            resist = r.First.Split(' ')[0].ToLowerInvariant();
            matched = CcStatKeys.Contains(resist, StringComparer.Ordinal);
        }
        line.Value("Range:"); line.Value("ChargesFromCells:"); line.Value("ChargeBeginCost:");
        string? info = line.Value("Info:");
        var infoMatched = info is null || CcSpellInfo.Contains(info, StringComparer.OrdinalIgnoreCase);
        var disabled = line.Value("Disabled:") is { } d && CcAtoi(d, out _) != 0;
        return new(iq, resist, matched, info?.ToLowerInvariant(), disabled, line.Exit || !infoMatched || (resist is not null && !matched), line.Leftover());
    }

    internal sealed record CcSpellLine4(IReadOnlyList<int> Values, int DefensiveSecond, bool NoResist, bool NoReflect, bool Exit, IReadOnlyList<string> Leftover);

    /// <summary>Line +4: ten <c>AI_*</c> values (−1 when absent; the defensive one is <c>(value@second)</c>), then the
    /// two presence keys.</summary>
    internal static CcSpellLine4 CcParseLine4(string text)
    {
        var line = new CcNamedLine(text);
        var values = Enumerable.Repeat(-1, CcAiActions.Length).ToArray(); var second = 0;
        for (var a = 0; a < CcAiActions.Length; a++)
        {
            if (a == 2)
            {
                if (line.Complex(CcAiActions[a]) is { } complex) { values[a] = CcAtoi(complex.First, out _); second = CcAtoi(complex.Second ?? complex.First, out _); }
            }
            else if (line.Value(CcAiActions[a]) is { } value) values[a] = CcAtoi(value, out _);
        }
        var noResist = line.Value("No_Resist:") is not null;
        var noReflect = line.Value("No_Reflect:") is not null;
        return new(values, second, noResist, noReflect, line.Exit, line.Leftover());
    }

    private static object CcSpellCensus(MesFile list)
    {
        string? Text(int number) => list.Find(number) is { } m ? Encoding.Latin1.GetString(m.Bytes.Span) : null;
        var lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var iq = new SortedDictionary<int, int>(); var iqForcedZero = 0; var resist = new CensusTally(); var resistUnmatched = 0; var lm17 = new CensusTally();
        var info = new CensusTally(); var disabled = 0; var exits = new CensusTally(); var noResist = 0; var noReflect = 0;
        var outOfOrder = new CensusTally(); var unknown = new CensusTally(); var seen = new CensusTally();
        var components = new SortedDictionary<int, int>();
        var lists = CcAiActions.Select(_ => new List<(int Spell, int Value, int Second)>()).ToArray();
        for (var spell = 0; spell < CcSpells; spell++)
        {
            var b = CcSpellBase(spell);
            for (var offset = 0; offset <= 4; offset++) if (list.Find(b + offset) is not null) lines[$"+{offset}"] = lines.GetValueOrDefault($"+{offset}") + 1;
            var count = 0;
            while (count < CcSpellComponentLines && list.Find(b + 5 + count) is not null) count++;
            components[count] = components.GetValueOrDefault(count) + 1;

            // Every key at the start of a comma-separated segment, in any order: a cross-check of the cursor.
            foreach (var offset in new[] { 1, 4 })
                if (Text(b + offset) is { } raw)
                    foreach (var key in CcSegmentKeys(raw)) seen.Add($"+{offset}|{key}|{(spell < 80 ? "0-79" : spell < 140 ? "80-139" : "140-222")}");
            // A missing +1 line reads the shared missing-line text, which matches no key.
            var line1 = CcParseLine1(Text(b + 1) ?? "");
            if (line1.Iq is { } value)
            {
                if (spell < 80) iq[value] = iq.GetValueOrDefault(value) + 1;
                else if (value != 0) iqForcedZero++;
            }
            if (line1.ResistStat is { } stat)
            {
                resist.Add(stat);
                if (!line1.ResistMatched) resistUnmatched++;
                if (stat is "stat_poison_level" or "stat_age") lm17.Add(stat);
            }
            if (line1.Info is { } i) info.Add(i);
            if (line1.Disabled) disabled++;
            if (line1.Exit) exits.Add("+1");
            foreach (var key in line1.Leftover) (CcLine1Keys.Contains(key) ? outOfOrder : unknown).Add($"+1|{key}");

            if (Text(b + 4) is { } text4)
            {
                var line4 = CcParseLine4(text4);
                for (var a = 0; a < CcAiActions.Length; a++) if (line4.Values[a] != -1) lists[a].Add((spell, line4.Values[a], a == 2 ? line4.DefensiveSecond : 0));
                if (line4.NoResist) noResist++;
                if (line4.NoReflect) noReflect++;
                if (line4.Exit) exits.Add("+4");
                foreach (var key in line4.Leftover) (CcLine4Keys.Contains(key) ? outOfOrder : unknown).Add($"+4|{key}");
            }
        }
        return new
        {
            records = CcSpells,
            lines_present = lines,
            component_lines = components,
            iq_values_below_80 = iq,
            iq_nonzero_forced_to_zero = iqForcedZero,
            resist_stats = resist.Snapshot(),
            resist_unmatched = resistUnmatched,
            resist_lm17_keys = lm17.Snapshot(),
            info = info.Snapshot(),
            disabled,
            no_resist = noResist,
            no_reflect = noReflect,
            original_would_exit = exits.Snapshot(),
            keys_seen = seen.Snapshot(),
            keys_out_of_order = outOfOrder.Snapshot(),
            keys_unknown = unknown.Snapshot(),
            ai_lists = CcAiActions.Select((name, a) => new
            {
                action = name.TrimEnd(':'),
                set = lists[a].Count,
                entries = lists[a].OrderByDescending(e => e.Value).ThenBy(e => e.Spell).Select(e => a == 2 ? new[] { e.Spell, e.Value, e.Second } : new[] { e.Spell, e.Value }).ToArray(),
            }).ToArray(),
        };
    }

    // ---- Effects ----

    /// <param name="MaxPerCollection">The most pairs one attribute collection (stats, basic skills, …) gets in the entry; the
    /// source stores each collection's pairs in its own fixed array.</param>
    internal sealed record CcEffectLine(int Pairs, int MaxPerCollection, IReadOnlyList<string> Operators, bool InvalidSymbol);

    /// <summary>The attribute collection <paramref name="token"/> names, by the production keyword table, or null.</summary>
    internal static EffectAttributeKind? CcEffectAttribute(string token) =>
        token.Length > 0 && token.All(c => c is > ' ' and < '\x7f') && EffectRules.Parse(Encoding.ASCII.GetBytes(token + " +1"), out var changes) == EffectRuleStatus.Parsed
            ? changes[0].Kind : null;

    /// <summary>One effect entry tokenised like the source's effect parser 0x4E9C20: tokens split on space and comma; an
    /// attribute, then its operator (<c>min</c>/<c>max</c> take the next token). Operators are classified by shape.</summary>
    internal static CcEffectLine CcEffectTokens(string text)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var operators = new List<string>(); var pairs = 0; var perCollection = new Dictionary<EffectAttributeKind, int>();
        int Max() => perCollection.Count == 0 ? 0 : perCollection.Values.Max();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (CcEffectAttribute(tokens[i]) is not { } kind) return new(pairs, Max(), operators, true);
            pairs++; perCollection[kind] = perCollection.GetValueOrDefault(kind) + 1;
            if (++i >= tokens.Length) { operators.Add("missing"); break; }
            var op = tokens[i];
            if (op.Equals("min", StringComparison.OrdinalIgnoreCase) || op.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                // The source tells min from max by a lowercase second letter 'i', so "MIN" acts as max (ledger LM9 row).
                operators.Add(op[1] == 'i' ? "min" : op[1] is 'a' or 'A' ? "max" : "min_acting_as_max");
                i++;
            }
            else operators.Add(op[0] switch
            {
                '+' or '-' when op.EndsWith('%') => "percent",
                '+' or '-' => "add",
                '*' => "multiply",
                '/' => "divide",
                _ => "unknown",
            });
        }
        return new(pairs, Max(), operators, false);
    }

    private static object CcEffectCensus(MesFile table, int first, int last)
    {
        var operators = new CensusTally(); var pairs = new SortedDictionary<int, int>(); var statuses = new CensusTally(); var maxPerCollection = 0;
        var multiplyDivide = new List<int>(); var invalid = 0;
        foreach (var message in table.Messages)
        {
            if (message.Number < first || message.Number > last) { statuses.Add("outside_range"); continue; }
            var line = CcEffectTokens(Encoding.Latin1.GetString(message.Bytes.Span));
            pairs[line.Pairs] = pairs.GetValueOrDefault(line.Pairs) + 1;
            maxPerCollection = Math.Max(maxPerCollection, line.MaxPerCollection);
            foreach (var op in line.Operators) operators.Add(op);
            if (line.InvalidSymbol) invalid++;
            if (line.Operators.Any(o => o is "multiply" or "divide")) multiplyDivide.Add(message.Number);
            statuses.Add(EffectRules.Parse(message.Bytes.Span, out _).ToString());
        }
        return new
        {
            entries = table.Count,
            pairs_per_entry = pairs,
            entries_over_5_pairs = pairs.Where(p => p.Key > 5).Sum(p => p.Value),
            entries_over_7_pairs = pairs.Where(p => p.Key > 7).Sum(p => p.Value),
            max_pairs_in_one_collection = maxPerCollection,
            operators = operators.Snapshot(),
            entries_with_multiply_or_divide = multiplyDivide,
            entries_with_invalid_symbol = invalid,
            production_reader = statuses.Snapshot(),
        };
    }

    // ---- Prototypes ----

    internal static string? CcRange(int prototypeNumber)
    {
        var description = prototypeNumber - 20;
        foreach (var (label, start, end) in CcRanges) if (description >= start && description < end) return label;
        return null;
    }

    internal static int CcArtType(uint art) => (int)(art >> 28);

    internal static IReadOnlyList<string> CcFlagNames(uint flags, IReadOnlyList<string> names) =>
        Enumerable.Range(0, 32).Where(b => (flags & (1u << b)) != 0).Select(b => b < names.Count ? names[b] : $"bit{b}").ToArray();

    private static object CcPrototypeCensus(IReadOnlyDictionary<int, WorldObject> objects, IReadOnlyDictionary<int, CombatFields> fields)
    {
        var ranges = CcRanges.Select(r =>
        {
            var inRange = objects.Where(o => o.Key - 20 >= r.Start && o.Key - 20 < r.End).ToArray();
            var critters = inRange.Where(o => fields.TryGetValue(o.Key, out var f) && f.Critter is not null).Select(o => fields[o.Key]).ToArray();
            return new
            {
                label = r.Label, start = r.Start, end = r.End, slots = r.End - r.Start, present = inRange.Length,
                types = inRange.GroupBy(o => o.Value.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                art_types = critters.GroupBy(f => CcArtType(f.Object.CurrentArt)).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                monster_flag = critters.Count(f => (f.Critter!.Flags & CcMonsterFlag) != 0),
                monster_damage_table = critters.Count(f => f.Npc?.Damage.Any(d => d.Maximum != 0) == true),
            };
        }).ToArray();
        var outside = objects.Keys.Where(n => CcRange(n) is null).Order().ToArray();

        var npcs = fields.Where(f => f.Value.Npc is not null).ToArray();
        var npcFlags = new CensusTally(); var critterFlags = new CensusTally();
        foreach (var (_, f) in npcs)
        {
            foreach (var name in CcFlagNames(f.Npc!.Flags, CcNpcFlags)) npcFlags.Add(name);
            foreach (var name in CcFlagNames(f.Critter!.Flags, CcCritterFlags)) critterFlags.Add(name);
        }
        var items = fields.Where(f => f.Value.Item is not null).ToArray();
        var validAi = items.Where(i => (i.Value.Item!.Flags & CcValidAiAction) != 0).ToArray();
        var weapons = items.Where(i => i.Value.Weapon is not null).ToArray();
        return new
        {
            ranges,
            outside_ranges = outside,
            npc = new
            {
                count = npcs.Length,
                ai_rows = npcs.GroupBy(n => n.Value.Npc!.AiData).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                npc_flags = npcFlags.Snapshot(),
                critter_flags = critterFlags.Snapshot(),
                factions = npcs.GroupBy(n => n.Value.Npc!.Faction).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                with_college_levels = npcs.Count(n => n.Value.Critter!.SpellTech.Take(16).Any(v => v > 0)),
                with_tech_degrees = npcs.Count(n => n.Value.Critter!.SpellTech.Skip(17).Any(v => v > 0)),
                with_monster_damage = npcs.Count(n => n.Value.Npc!.Damage.Any(d => d.Maximum != 0)),
            },
            items = new
            {
                count = items.Length,
                valid_ai_action = validAi.GroupBy(i => (i.Value.Object.Type, i.Value.Item!.AiAction)).OrderBy(g => g.Key)
                    .Select(g => new { type = g.Key.Type, ai_action = g.Key.AiAction, action = g.Key.AiAction is >= 0 and < 10 ? CcAiActions[g.Key.AiAction].TrimEnd(':') : null, count = g.Count(), prototypes = g.Select(i => i.Key).Order().ToArray() }).ToArray(),
                ai_action_without_flag = items.Count(i => i.Value.Item!.AiAction != 0 && (i.Value.Item.Flags & CcValidAiAction) == 0),
                with_spells = items.Where(i => i.Value.Item!.Spells.Any(s => s != CcNoSpell)).GroupBy(i => i.Value.Object.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                grenades = items.Count(i => i.Value.Generic is { } g && (g.Flags & CcGrenade) != 0),
                healing_items = items.Count(i => i.Value.Generic is { } g && (g.Flags & CcHealingItem) != 0),
                weapons = weapons.Length,
                weapon_skills = weapons.GroupBy(w => w.Value.Weapon!.Skill.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                weapon_ammo_types = weapons.GroupBy(w => w.Value.Weapon!.AmmoType).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            },
        };
    }

    // ---- Instances ----

    /// <summary>An instance's effective fields as an Area record (instance field, else its prototype's whole field), so
    /// the production combat-field reader can read catalogue mobiles.</summary>
    internal static AreaObjectRecord CcEffective(WorldObject instance, WorldObject prototype, int index)
    {
        static AreaFieldValue Value(int slot, WorldFieldValue v, AreaValueOrigin origin) =>
            new(slot, (AreaPresence)((int)v.Presence + 1), origin, v.Number, v.Elements.Select(e => new AreaSparseElement(e.Key, e.Words)).ToArray(), v.Fingerprint);
        var effective = instance.Fields.Keys.Union(prototype.Fields.Keys).Order()
            .Select(slot => instance.Fields.TryGetValue(slot, out var own) ? Value(slot, own, AreaValueOrigin.Instance) : Value(slot, prototype.Fields[slot], AreaValueOrigin.Prototype))
            .ToArray();
        return new(new("", 0, AreaSourceKind.Mobile, null, index, 0, instance.OwnIdSha256, 0), 119, instance.Type, instance.PrototypeNumber ?? 0,
            "", "", "", "", AreaScope.Unknown, null, null, null, AreaDisposition.ContextOnly, [], effective, true, true, true);
    }

    /// <summary>How the stored-damage weapon choice of an NPC changes under LM1 (only the fatigue average scores) against
    /// the original (the five averages add). Assumes one positive skill effectiveness for all weapons, so it is an
    /// estimate; a tie is decided by the runtime handle and is reported as such.</summary>
    internal static string CcLm1(IReadOnlyList<IReadOnlyList<CombatDamageRange>> weapons)
    {
        if (weapons.Count < 2) return "single";
        static int[] Best(int[] scores) { var max = scores.Max(); return Enumerable.Range(0, scores.Length).Where(i => scores[i] == max).ToArray(); }
        var sum = Best(weapons.Select(w => w.Sum(d => (d.Minimum + d.Maximum) / 2)).ToArray());
        var fatigue = Best(weapons.Select(w => (w[(int)CombatDamageIndex.Fatigue].Minimum + w[(int)CombatDamageIndex.Fatigue].Maximum) / 2).ToArray());
        if (fatigue.Length > 1) return sum.Length == 1 && fatigue.Contains(sum[0]) ? "lm1_tie_includes_original" : "lm1_tie";
        if (sum.Length > 1) return sum.Contains(fatigue[0]) ? "original_tie_includes_lm1" : "differs";
        return sum[0] == fatigue[0] ? "same" : "differs";
    }

    private static object CcWorldInstances(WorldCatalog catalog, IReadOnlyDictionary<int, WorldObject> prototypes)
    {
        var npcCount = 0; var maps = 0; var mobileIssues = 0; var missingPrototype = 0; var refused = new CensusTally();
        var aiRows = new SortedDictionary<int, int>(); var npcFlags = new CensusTally(); var critterFlags = new CensusTally(); var factions = new SortedDictionary<int, int>();
        var weaponsPerNpc = new SortedDictionary<int, int>(); var wielded = 0; var lm1 = new CensusTally(); var lm2Candidates = 0;
        var withColleges = 0; var withMonsterDamage = 0;
        foreach (var map in catalog.Maps)
        {
            var mobiles = catalog.ReadMobiles(map);
            mobileIssues += mobiles.Issues.Count;
            if (mobiles.Objects.Count == 0) continue;
            maps++;
            var rows = new List<(int Index, WorldObject Object, CombatFields Fields)>();
            for (var i = 0; i < mobiles.Objects.Count; i++)
            {
                var o = mobiles.Objects[i];
                if (o.Type is < CombatFieldReader.WeaponType or > CombatFieldReader.NpcType) continue;
                if (o.PrototypeNumber is not { } p || !prototypes.TryGetValue(p, out var prototype)) { missingPrototype++; continue; }
                var read = CombatFieldReader.Read(CcEffective(o, prototype, i));
                if (read.Fields is null) { refused.Add(read.Failure.ToString()); continue; }
                rows.Add((i, o, read.Fields));
            }
            var npcsById = rows.Where(r => r.Fields.Npc is not null).GroupBy(r => r.Object.OwnIdSha256, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var carried = rows.Where(r => r.Fields.Weapon is not null && r.Object.Fields.TryGetValue(CcItemParentSlot, out var parent) && parent.Presence == WorldFieldPresence.Value
                    && parent.Fingerprint is { } id && npcsById.ContainsKey(id))
                .GroupBy(r => r.Object.Fields[CcItemParentSlot].Fingerprint!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            foreach (var (id, npc) in npcsById)
            {
                npcCount++;
                var (critter, n) = (npc.Fields.Critter!, npc.Fields.Npc!);
                aiRows[n.AiData] = aiRows.GetValueOrDefault(n.AiData) + 1;
                factions[n.Faction] = factions.GetValueOrDefault(n.Faction) + 1;
                foreach (var name in CcFlagNames(n.Flags, CcNpcFlags)) npcFlags.Add(name);
                foreach (var name in CcFlagNames(critter.Flags, CcCritterFlags)) critterFlags.Add(name);
                if (critter.SpellTech.Take(16).Any(v => v > 0)) withColleges++;
                if (n.Damage.Any(d => d.Maximum != 0)) withMonsterDamage++;
                var weapons = carried.GetValueOrDefault(id) ?? [];
                weaponsPerNpc[weapons.Length] = weaponsPerNpc.GetValueOrDefault(weapons.Length) + 1;
                if (weapons.Any(w => w.Fields.Item!.InventoryLocation == CcWieldedWeapon)) wielded++;
                if (weapons.Length >= 2)
                {
                    var skills = weapons.Select(w => w.Fields.Weapon!.Skill).Distinct().Count();
                    lm1.Add(skills == 1 ? CcLm1(weapons.Select(w => w.Fields.Weapon!.Damage).ToArray()) : "mixed_skills");
                    // Beyond melee reach LM2 can only change the choice between two or more weapons in range.
                    if (weapons.Count(w => w.Fields.Weapon!.Range > 1) >= 2) lm2Candidates++;
                }
            }
        }
        return new
        {
            maps_with_mobiles = maps, mobile_issues = mobileIssues, missing_prototype = missingPrototype, refused = refused.Snapshot(),
            npcs = npcCount, ai_rows = aiRows, npc_flags = npcFlags.Snapshot(), critter_flags = critterFlags.Snapshot(),
            factions_distinct = factions.Count, factions_top = factions.OrderByDescending(f => f.Value).ThenBy(f => f.Key).Take(12).ToDictionary(f => f.Key, f => f.Value),
            with_college_levels = withColleges, with_monster_damage = withMonsterDamage,
            carried_weapons_per_npc = weaponsPerNpc, with_wielded_weapon = wielded,
            lm1_with_two_or_more_weapons = lm1.Snapshot(), lm2_two_or_more_weapons_with_range_above_1 = lm2Candidates,
        };
    }

    private static object CcArea(ContentStack stack, WorldCatalog catalog, IReadOnlyDictionary<string, MesFile> tables, int catalogue, bool cave, CcAiTableResult? ai)
    {
        try
        {
            if (!tables.TryGetValue("rules/maplist.mes", out var mapList)) return new { available = false, reason = "maplist" };
            var rows = DecodeWorldCatalogue(mapList.Messages.ToDictionary(m => m.Number, m => Encoding.Latin1.GetString(m.Bytes.Span)));
            var name = rows.FirstOrDefault(r => r.Catalogue == catalogue)?.Name;
            var map = name is null ? null : catalog.Find(name);
            if (map?.AggregatePath is not { } aggregate || stack.Resolve(aggregate).Winner?.FilePath is not { } mobile) return new { available = false, reason = "aggregate" };
            var order = stack.Plan.SearchOrder;
            var module = order.Where(s => s.Role == ContentSourceRole.ModuleArchive).ToArray();
            var resource1 = order.Where(s => s.Role == ContentSourceRole.BaseArchive && Path.GetFileName(s.Path).Equals("arcanum1.dat", StringComparison.OrdinalIgnoreCase)).ToArray();
            var resource2 = order.Where(s => s.Role == ContentSourceRole.BaseArchive && Path.GetFileName(s.Path).Equals("arcanum2.dat", StringComparison.OrdinalIgnoreCase)).ToArray();
            var data = order.Where(s => s.Role == ContentSourceRole.DataDirectory).ToArray();
            if (module.Length != 1 || resource1.Length != 1 || resource2.Length != 1 || data.Length != 1) return new { available = false, reason = "sources" };
            var prototypes = Path.Combine(data[0].Path, "proto");
            using var owner = cave
                ? AreaOriginalValidator.OpenCaveFiles(module[0].Path, resource1[0].Path, resource2[0].Path, mobile, prototypes)
                : AreaOriginalValidator.OpenFiles(module[0].Path, resource1[0].Path, resource2[0].Path, mobile, prototypes);
            var snapshot = owner.LoadStrict();
            var read = CombatFieldReader.ReadArea(snapshot);
            if (read.Objects is null) return new { available = false, reason = $"combat_{read.Failure}" };
            var inventory = AreaInventoryReader.Read(snapshot).Inventory;
            var scope = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).ToDictionary(o => o.Key.SourceIndex, o => o.Scope);
            var byIndex = read.Objects.ToDictionary(o => o.SourceIndex);
            var npcs = read.Objects.Where(o => o.Fields.Npc is not null).Select(o =>
            {
                var (critter, npc) = (o.Fields.Critter!, o.Fields.Npc!);
                var held = inventory is not null && inventory.TryGetHolder(o.SourceIndex, out var holder) ? holder.Items.ToArray() : [];
                var weapons = held.Where(i => byIndex.TryGetValue(i, out var item) && item.Fields.Weapon is not null).Select(i => byIndex[i]).ToArray();
                return new
                {
                    index = o.SourceIndex, scope = scope[o.SourceIndex].ToString(), prototype = o.Prototype,
                    range = CcRange(o.Prototype), art_type = CcArtType(o.Fields.Object.CurrentArt),
                    ai_row = npc.AiData, faction = npc.Faction, level = critter.BaseStats[17], hp_damage = o.Fields.Object.HitPointDamage,
                    npc_flags = CcFlagNames(npc.Flags, CcNpcFlags), critter_flags = CcFlagNames(critter.Flags, CcCritterFlags),
                    colleges = critter.SpellTech.Take(16).Select((v, c) => (v, c)).Where(p => p.v > 0).Select(p => $"{p.c}:{p.v}").ToArray(),
                    mastery = critter.SpellTech[16],
                    monster_damage = npc.Damage[0].Maximum == 0 ? null : $"{npc.Damage[0].Minimum}-{npc.Damage[0].Maximum}",
                    items = held.Length,
                    weapons = weapons.Select(w => new { prototype = w.Prototype, skill = w.Fields.Weapon!.Skill.ToString(), wielded = w.Fields.Item!.InventoryLocation == CcWieldedWeapon }).ToArray(),
                    item_spells = held.Where(byIndex.ContainsKey).Sum(i => byIndex[i].Fields.Item?.Spells.Count(s => s != CcNoSpell) ?? 0),
                    lm1 = CcLm1(weapons.Select(w => w.Fields.Weapon!.Damage).ToArray()),
                };
            }).ToArray();
            var usedRows = npcs.Select(n => n.ai_row).Distinct().Order().ToArray();
            return new
            {
                available = true, map = catalogue, npcs = npcs.Length,
                inventory_view = inventory is not null,
                ai_rows_used = usedRows.ToDictionary(r => r, r => ai is not null && ai.RowValues.TryGetValue(r, out var values) ? values : null),
                rows = npcs,
            };
        }
        catch (AreaException error) { return new { available = false, reason = $"area_{error.Issue.Code}_{error.Issue.Stage}" }; }
    }

    private static void CombatCensusSelfTest()
    {
        static void Check(bool condition, string what) { if (!condition) throw new InvalidOperationException($"combat-census self-test failed: {what}"); }
        static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File ?? throw new InvalidOperationException("combat-census self-test failed: fixture");

        // AI rows: positional filling across a numbering gap (LM3), short rows, non-numeric tokens.
        var ai = CcAiTable(Mes("{0}{1 2 3}\n{1}{4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21}\n{3}{x -2}\n"), 0, CcBaseAiRows);
        Check(ai.Rows == 3 && ai.Gaps.SequenceEqual([2]) && !ai.ByNumberEqualsByPosition, "positional rows across a gap");
        Check(ai.RowValues[2][1] == -2 && ai.RowValues[2][0] == 0 && ai.NonNumericTokens == 1, "row after the gap takes the next position");
        Check(ai.RowValues[1][16] == 20 && ai.TokensPerRow[18] == 1, "only 17 values per row");
        Check(ai.Columns[0].Min == 0 && ai.Columns[0].Max == 4 && ai.Columns[0].Distinct == 3, "column statistics");
        var noFirst = CcAiTable(Mes("{1}{1}\n"), 0, CcBaseAiRows);
        Check(!noFirst.FirstEntryPresent && noFirst.Rows == 0, "nothing loads without entry 0");
        var module = CcAiTable(Mes("{100}{1}\n{101}{2}\n"), CcModuleAiFirst, CcModuleAiRows);
        Check(module.Rows == 2 && module.ByNumberEqualsByPosition, "module rows from 100");
        Check(CcAtoi(" -12x", out var whole) == -12 && !whole && CcAtoi("7", out whole) == 7 && whole, "atoi");

        // Spell records: numbering, named-key order, unknown keys, exits, LM17 and AI values.
        Check(CcSpellBase(0) == 1000 && CcSpellBase(139) == 7950 && CcSpellBase(140) == 10000 && CcSpellBase(222) == 14100, "spell numbering");
        var line1 = CcParseLine1("IQ: 9, Cost: 5, Resist:(stat_age@-10), Range: 4, Info: friendly");
        Check(line1.Iq == 9 && line1.ResistStat == "stat_age" && line1.ResistMatched && line1.Info == "friendly" && !line1.Exit && line1.Leftover.Count == 0, "line +1");
        var swapped = CcParseLine1("Cost: 5, IQ: 9, Bogus: 1");
        Check(swapped.Iq is null && swapped.Leftover.SequenceEqual(["IQ:", "Bogus:"]), "an out-of-order key is skipped and left over");
        Check(CcParseLine1("Maintain: 5").Exit && CcParseLine1("Resist:(stat_nothing@1)").Exit && CcParseLine1("Info: hostile").Exit, "exits");
        var line4 = CcParseLine4("AI_Flee: 10, AI_Defensive:(30@2), AI_Offensive: 50, No_Reflect: 1");
        Check(line4.Values[0] == 10 && line4.Values[1] == -1 && line4.Values[2] == 30 && line4.DefensiveSecond == 2 && line4.Values[3] == 50, "AI values");
        Check(!line4.NoResist && line4.NoReflect && line4.Leftover.Count == 0, "presence keys");
        var late = CcParseLine4("AI_Offensive: 50, AI_Flee: 10");
        Check(late.Values[3] == 50 && late.Values[0] == -1 && late.Leftover.SequenceEqual(["AI_Flee:"]), "AI key out of order");
        Check(CcParseLine4("AI_Defensive: 30").Exit, "defensive without a pair exits");

        // Effects: operators by shape, min/max consume a value, unknown symbols stop the entry.
        var effect = CcEffectTokens("st +1, dx -2%, carry *2 melee /3 maxhps min 5 ac MAX 4");
        Check(effect.Pairs == 6 && effect.MaxPerCollection == 4 && !effect.InvalidSymbol, "effect pairs");
        Check(effect.Operators.SequenceEqual(["add", "percent", "multiply", "divide", "min", "max"]), "effect operators");
        Check(CcEffectTokens("st MIN 3").Operators.SequenceEqual(["min_acting_as_max"]), "MIN acts as max");
        Check(CcEffectTokens("st +1 nothing +2").InvalidSymbol, "invalid symbol");

        // Ranges and art types (LM15), layer labels, effective fields and the LM1 estimate.
        Check(CcRange(6198) == "weapon" && CcRange(28359) == "critter_28310" && CcRange(27356) == "critter_27309" && CcRange(17102) == "npc" && CcRange(20) is null, "ranges");
        Check(CcArtType(0xC0001234) == 12 && CcArtType(0xD0000000) == 13, "art type");
        Check(CcLayer(new(ContentSourceRole.ModuleArchive, 0, "/x/Modules/Arcanum.dat")) == "ModuleArchive:arcanum.dat"
              && CcLayer(new(ContentSourceRole.DataDirectory, 0, "/x/data")) == "DataDirectory", "layer label");
        var candidates = new ContentResolution(ContentResolutionStatus.Resolved, "rules/x.mes",
            [new(new(ContentSourceRole.ModuleArchive, 0, "m.dat"), null, null, 1), new(new(ContentSourceRole.BaseArchive, 1, "arcanum1.dat"), null, null, 1)]);
        Check(CcOriginalCandidate(candidates, "init")!.Source.Role == ContentSourceRole.BaseArchive
              && CcOriginalCandidate(candidates, "module")!.Source.Role == ContentSourceRole.ModuleArchive, "init files read the base");
        Check(CcFlagNames(0x08000100, CcNpcFlags).SequenceEqual(["KOS", "LOOK_FOR_AMMO"]) && CcFlagNames(0x08000000, CcCritterFlags).SequenceEqual(["NO_FLEE"]), "flag names");
        CombatDamageRange[] Damage(int normal, int fatigue) => [new(normal, normal), default, default, default, new(fatigue, fatigue)];
        Check(CcLm1([Damage(10, 0), Damage(2, 0)]) == "lm1_tie_includes_original", "LM1 tie when no fatigue");
        Check(CcLm1([Damage(10, 0), Damage(2, 1)]) == "differs" && CcLm1([Damage(10, 2), Damage(2, 1)]) == "same" && CcLm1([Damage(1, 1)]) == "single", "LM1 estimate");
        Console.WriteLine("combat-census self-test: PASS");
    }
}
