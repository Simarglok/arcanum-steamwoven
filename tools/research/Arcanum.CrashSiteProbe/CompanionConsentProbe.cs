using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-026 research only: the inputs of the follow consent check for Virgil at
// T0 (his AI-data row, level, alignment, wait index, reaction base, flags and
// effects) and the operands of every JO/UW in his DLG. Independently authored from
// the pinned CE locators cited in docs/research/virgil-follow-consent.md; no CE
// code or structure is reused. No consent evaluator, no party state. Only numbers,
// booleans, two-letter operation codes and SHA-256 digests leave the probe; MES and
// DLG text stays in memory.
internal static partial class Program
{
    // Effective-field slots (common block 0..35, critter 36..68, NPC 69..93).
    private const int CcCritterFlagsSlot = 36, CcCritterFlags2Slot = 37, CcStatBaseSlot = 38, CcEffectsSlot = 46, CcEffectCausesSlot = 47,
        CcFollowersSlot = 59, CcAutoLevelSlot = 63;
    private const int CcNpcFlagsSlot = 69, CcNpcLeaderSlot = 70, CcAiDataSlot = 71, CcOriginSlot = 80, CcFactionSlot = 81,
        CcReactionBaseSlot = 84, CcSocialClassSlot = 85, CcReactionPcSlot = 86, CcReactionLevelSlot = 87, CcWaitSlot = 89;
    private const int CcNpcType = 16, CcDialogSap = 9, CcVirgilScript = 1324, CcAiParamCount = 17, CcMaxEntryBytes = 512 * 1024;

    // Stat indexes and the effect-file stat symbols, in stat order. The symbols are the file-format
    // vocabulary of rules\effect.mes as the pinned source reads it (effect.c 87-116), lowercased.
    internal static readonly string[] CcStatNames =
    [
        "strength", "dexterity", "constitution", "beauty", "intelligence", "perception", "willpower", "charisma", "carry_weight",
        "damage_bonus", "ac_adjustment", "speed", "heal_rate", "poison_recovery", "reaction_modifier", "max_followers",
        "magick_tech_aptitude", "level", "experience_points", "alignment", "fate_points", "unspent_points", "magick_points",
        "tech_points", "poison_level", "age", "gender", "race",
    ];
    internal static readonly string[] CcEffectStatSymbols =
    [
        "st", "dx", "cn", "be", "in", "pe", "wp", "ch", "carry", "damage", "ac", "speed", "healrate", "poisonrate", "beautyreaction",
        "maxfollowers", "aptitude", "level", "xpsunused", "alignment", "fateunused", "unspentunused", "magicpts", "techpts",
        "poisonlevel", "age", "gender", "race",
    ];
    private const int CcStatBeauty = 3, CcStatCharisma = 7, CcStatLevel = 17, CcStatAlignment = 19, CcStatGender = 26, CcStatRace = 27;

    private static readonly (string Name, uint Bit)[] CcNpcFlagNames =
    [
        ("wait_here", 0x8), ("spread_out", 0x10), ("jilted", 0x20), ("forced_follower", 0x400), ("check_leader", 0x10000), ("aloof", 0x20000),
    ];
    private static readonly (string Name, uint Bit)[] CcCritterFlagNames =
    [
        ("undead", 0x4), ("animal", 0x8), ("monster", 0x8000), ("mechanical", 0x20000000),
    ];
    private static readonly (string Name, uint Bit)[] CcCritterFlag2Names =
    [
        ("check_reaction_bad", 0x01000000), ("check_align_good", 0x02000000), ("check_align_bad", 0x04000000),
    ];

    internal readonly record struct CcInt(int Value, bool Overflow);
    internal sealed record CcAiRow(int Row, int Entry, int Tokens, int[] Values);

    // C atoi: leading isspace, an optional sign, then decimal digits; anything else ends
    // the number. Overflow is undefined in C, so it is flagged instead of reproduced.
    internal static CcInt CcAtoi(ReadOnlySpan<byte> text)
    {
        var i = 0;
        while (i < text.Length && CcIsSpace(text[i])) i++;
        var negative = false;
        if (i < text.Length && text[i] is (byte)'-' or (byte)'+') { negative = text[i] == (byte)'-'; i++; }
        long value = 0; var overflow = false;
        while (i < text.Length && text[i] is >= (byte)'0' and <= (byte)'9')
        {
            value = value * 10 + (text[i] - '0');
            if (value > 2_147_483_648L) { overflow = true; value = 2_147_483_648L; }
            i++;
        }
        if (!negative && value > int.MaxValue) { overflow = true; value = int.MaxValue; }
        return new(negative ? (int)-value : (int)value, overflow);
    }

    // The JO/UW node operand as the source reads it from the text right after the
    // code: skip isspace, skip decimal digits (a sign is not skipped), then atoi.
    internal static CcInt CcSecondOperand(ReadOnlySpan<byte> afterCode)
    {
        var i = 0;
        while (i < afterCode.Length && CcIsSpace(afterCode[i])) i++;
        while (i < afterCode.Length && afterCode[i] is >= (byte)'0' and <= (byte)'9') i++;
        return CcAtoi(afterCode[i..]);
    }

    private static bool CcIsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';

    // Code scan of a DLG operation field: skip to an ASCII letter or '$', take two
    // bytes as the code, and return the code with the offset right after it.
    internal static List<(string Code, int After)> CcScanCodes(ReadOnlySpan<byte> field)
    {
        var codes = new List<(string, int)>(); var i = 0;
        while (i < field.Length)
        {
            while (i < field.Length && !(char.IsAsciiLetter((char)field[i]) || field[i] == (byte)'$')) i++;
            if (i >= field.Length - 1) break;
            codes.Add((Encoding.ASCII.GetString(field.Slice(i, 2)).ToUpperInvariant(), i + 2));
            i += 2;
        }
        return codes;
    }

    // AI parameter rows: loading needs an entry numbered exactly `first`; from there the
    // row index is `first` plus a running count over the entries in number order, up to
    // (not including) `limit`. Each entry gives at most 17 integers, split on space, tab
    // and newline only, each read with atoi; a short entry leaves its later fields 0 (true for a first
    // load only: the source does not clear rows when a module is reloaded).
    internal static IReadOnlyList<CcAiRow> CcAiRows(SortedDictionary<int, string> mes, int first, int limit)
    {
        var rows = new List<CcAiRow>();
        if (!mes.ContainsKey(first)) return rows;
        var row = first;
        foreach (var (entry, text) in mes.Where(pair => pair.Key >= first))
        {
            if (row >= limit) break;
            var tokens = text.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(CcAiParamCount).ToArray();
            var values = new int[CcAiParamCount];
            for (var k = 0; k < tokens.Length; k++) values[k] = CcAtoi(Encoding.Latin1.GetBytes(tokens[k])).Value;
            rows.Add(new(row, entry, tokens.Length, values));
            row++;
        }
        return rows;
    }

    // Stat symbols an effect entry names: tokens split on space and comma that equal a
    // stat symbol case-insensitively, in entry order.
    internal static int[] CcEffectStats(string text) =>
        text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Array.IndexOf(CcEffectStatSymbols, t.ToLowerInvariant())).Where(i => i >= 0).ToArray();

    // Whether an effect entry names the special "reaction" attribute (the NPC-side reaction adjustment).
    internal static bool CcNamesReaction(string text) =>
        text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Any(t => t.Equals("reaction", StringComparison.OrdinalIgnoreCase));

    private static int ProbeCompanionConsent(string[] args)
    {
        const int fixedArgs = 6;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--game-root" || args[2] != "--prototype-root" || args[4] != "--mobile")
            throw new ArgumentException("CONSENT_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("CONSENT_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var gameRoot = args[1]; var prototypeRoot = args[3]; var mobilePath = args[5];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("CONSENT_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha)) throw new InvalidDataException("CONSENT_AGGREGATE_IDENTITY");

        using var area = AreaOriginalValidator.OpenFiles(archivePaths[0], archivePaths[1], archivePaths[2], mobilePath, prototypeRoot);
        var snapshot = area.LoadStrict();

        // Virgil: the only core NPC whose SAP_DIALOG attachment names script 1324.
        var virgils = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope == AreaScope.Core && o.Type == CcNpcType &&
            o.EffectiveFields.FirstOrDefault(f => f.Slot == InvScriptsSlot) is { Presence: AreaPresence.Array } s &&
            s.Elements.Any(e => e.Key == CcDialogSap && e.Words[2] == CcVirgilScript)).ToArray();
        if (virgils.Length != 1) throw new InvalidDataException("CONSENT_VIRGIL_NOT_UNIQUE");
        var virgil = virgils[0];

        var discovery = ContentMountPlan.Discover(gameRoot);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("CONSENT_MOUNT_" + discovery.Status);
        // Fail closed unless every base, module and patch archive of the stack is one of the pinned inputs.
        var pinned = archivePaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (discovery.Plan!.SearchOrder.Any(s => s.Kind == ContentSourceKind.Archive && s.Role != ContentSourceRole.Tig && !pinned.Contains(Path.GetFullPath(s.Path))))
            throw new InvalidDataException("CONSENT_STACK_UNPINNED_ARCHIVE");
        var opened = ContentStack.Open(discovery.Plan!, new ContentStackOptions { Dat = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = CcMaxEntryBytes, MaxDecodedEntryBytes = CcMaxEntryBytes } });
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("CONSENT_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var archiveRoles = CaveArchives.Select((a, i) => (Path.GetFullPath(archivePaths[i]), a.Role)).ToDictionary(x => x.Item1, x => x.Role, StringComparer.OrdinalIgnoreCase);
        string SourceName(ContentSource source) => archiveRoles.TryGetValue(Path.GetFullPath(source.Path), out var role) ? role : $"{source.Role}:{source.Number}";

        (object Report, byte[]? Winner) Resolve(string path)
        {
            var resolution = stack.Resolve(path);
            // Every candidate, winner or shadowed, must come from a pinned archive (tig.dat and loose folders are not pinned).
            if (resolution.Candidates.Any(c => !archiveRoles.ContainsKey(Path.GetFullPath(c.Source.Path)))) throw new InvalidDataException("CONSENT_UNPINNED_CANDIDATE");
            var payloads = resolution.Candidates.Select(c => stack.Read(c) is { Status: ContentReadStatus.Read, Bytes: { } b } ? b : throw new InvalidDataException("CONSENT_READ")).ToArray();
            return (new
            {
                path = resolution.Key, status = resolution.Status.ToString(),
                candidates = resolution.Candidates.Select((c, k) => new { source = SourceName(c.Source), bytes = payloads[k].Length, sha256 = AreaHash(payloads[k]) }).ToArray(),
            }, payloads.FirstOrDefault());
        }

        long? Number(int slot) => virgil.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? n : null;
        object Field(int slot)
        {
            var f = virgil.EffectiveFields.FirstOrDefault(x => x.Slot == slot);
            return f is null ? new { presence = "Missing", origin = (string?)null, value = (long?)null, elements = (object?)null }
                : new { presence = f.Presence.ToString(), origin = (string?)f.Origin.ToString(), value = f.Number,
                    elements = f.Presence == AreaPresence.Array ? f.Elements.OrderBy(e => e.Key).Select(e => (object)new { key = e.Key, words = e.Words }).ToArray() : (object?)null };
        }
        // Handle fields: presence, origin and element count only (handles never leave the probe).
        object Shape(int slot) => virgil.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { } f
            ? new { presence = f.Presence.ToString(), origin = f.Origin.ToString(), elements = f.Elements.Count }
            : new { presence = "Missing", origin = "", elements = 0 };
        static object? Bits(long? value, (string Name, uint Bit)[] names) =>
            value is { } v ? names.ToDictionary(n => n.Name, n => ((uint)v & n.Bit) != 0) : null;
        var statField = virgil.EffectiveFields.FirstOrDefault(f => f.Slot == CcStatBaseSlot) ?? throw new InvalidDataException("CONSENT_STATS_MISSING");
        if (statField.Presence != AreaPresence.Array) throw new InvalidDataException("CONSENT_STATS_MISSING");
        var stats = statField.Elements.ToDictionary(e => e.Key, e => e.Words.Count == 1 ? unchecked((int)e.Words[0]) : throw new InvalidDataException("CONSENT_STAT_WIDTH"));
        int? Stat(int index) => stats.TryGetValue(index, out var v) ? v : null;

        // AI rows: engine table rows 0..99, module table rows 100..149.
        var (aiParamsReport, aiParamsBytes) = Resolve("rules/ai_params.mes");
        var (gameAiReport, gameAiBytes) = Resolve("rules/gameai.mes");
        var engineRows = aiParamsBytes is null ? [] : CcAiRows(ClosureMes(aiParamsBytes), 0, 100);
        var moduleRows = gameAiBytes is null ? [] : CcAiRows(ClosureMes(gameAiBytes), 100, 150);
        var aiIndex = Number(CcAiDataSlot) is { } ai ? checked((int)ai) : throw new InvalidDataException("CONSENT_AI_DATA_MISSING");
        // The engine tables are required by the source; the module tables are optional.
        if (aiParamsBytes is null) throw new InvalidDataException("CONSENT_ENGINE_TABLE_MISSING");
        var aiRow = engineRows.Concat(moduleRows).FirstOrDefault(r => r.Row == aiIndex) ?? throw new InvalidDataException("CONSENT_AI_ROW_MISSING");

        // Effects on the critter and which stats each names (engine file 50..399, module file 0..49).
        var (effectReport, effectBytes) = Resolve("rules/effect.mes");
        var (gameEffectReport, gameEffectBytes) = Resolve("rules/gameeffect.mes");
        if (effectBytes is null) throw new InvalidDataException("CONSENT_ENGINE_TABLE_MISSING");
        var effectMes = effectBytes is null ? new SortedDictionary<int, string>() : ClosureMes(effectBytes);
        var gameEffectMes = gameEffectBytes is null ? new SortedDictionary<int, string>() : ClosureMes(gameEffectBytes);
        var effectField = virgil.EffectiveFields.FirstOrDefault(f => f.Slot == CcEffectsSlot);
        var causeField = virgil.EffectiveFields.FirstOrDefault(f => f.Slot == CcEffectCausesSlot);
        var effects = effectField is { Presence: AreaPresence.Array } ? effectField.Elements.OrderBy(e => e.Key).Select(e => (e.Key, Id: (int)e.Words[0])).ToArray() : [];
        var causes = causeField is { Presence: AreaPresence.Array } ? causeField.Elements.ToDictionary(e => e.Key, e => (int)e.Words[0]) : [];
        object EffectRow((int Key, int Id) effect)
        {
            var table = effect.Id is >= 0 and < 50 ? gameEffectMes : effect.Id is >= 50 and < 400 ? effectMes : null;
            var present = table is not null && table.ContainsKey(effect.Id);
            return new { index = effect.Key, effect = effect.Id, cause = causes.TryGetValue(effect.Key, out var c) ? c : (int?)null,
                file = table == gameEffectMes ? "gameeffect" : table == effectMes ? "effect" : null, present,
                stats = present ? CcEffectStats(table![effect.Id]).Select(i => CcStatNames[i]).ToArray() : [],
                reaction_special = present && CcNamesReaction(table![effect.Id]) };
        }

        // Virgil's DLG: the winner of the dlg name his script number selects.
        string? dialogName = null;
        foreach (var path in archivePaths)
        {
            using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = CcMaxEntryBytes, MaxDecodedEntryBytes = CcMaxEntryBytes });
            foreach (var entry in archive.Entries.Where(e => InvDialogKey(e) == CcVirgilScript))
            {
                var name = ClosureEntryName(entry);
                if (dialogName is not null && dialogName != name) throw new InvalidDataException("CONSENT_DLG_NAME_AMBIGUOUS");
                dialogName = name;
            }
        }
        if (dialogName is null) throw new InvalidDataException("CONSENT_DLG_MISSING");
        var (dialogReport, dialogBytes) = Resolve(dialogName);
        var document = DlgReader.Read(dialogBytes ?? throw new InvalidDataException("CONSENT_DLG_MISSING"));
        var operations = new List<object>(); var reactions = new List<object>();
        DlgRecord? parent = null;
        foreach (var record in document.Records)
        {
            if (record.Role == DlgRecordRole.Npc) parent = record;
            var condition = record.Condition.Bytes.ToArray();
            foreach (var (role, field) in new[] { ("condition", record.Condition), ("action", record.Action) })
            {
                var bytes = field.Bytes.ToArray();
                foreach (var (code, after) in CcScanCodes(bytes))
                {
                    if (code == "RE" && role == "action")
                    {
                        // Reaction actions: the number and the first non-space byte if it is a sign or a
                        // comparator ('<' / '>' would make atoi read 0, so it must not pass as a target).
                        var j = after; while (j < bytes.Length && CcIsSpace(bytes[j])) j++;
                        reactions.Add(new { record = record.Id, record_role = record.Role.ToString(), value = CcAtoi(bytes.AsSpan(after)).Value,
                            lead = j < bytes.Length && bytes[j] is (byte)'-' or (byte)'+' or (byte)'<' or (byte)'>' ? ((char)bytes[j]).ToString() : null });
                        continue;
                    }
                    if (code is not ("JO" or "UW")) continue;
                    var first = CcAtoi(bytes.AsSpan(after)); var node = CcSecondOperand(bytes.AsSpan(after));
                    var target = document.TryGetRecord(node.Value, out var t) ? t : null;
                    operations.Add(new
                    {
                        code, record = record.Id, record_role = record.Role.ToString(), field = role, ignore_limit_and_force = first.Value, node = node.Value,
                        overflow = first.Overflow || node.Overflow, node_role = target?.Role.ToString(),
                        parent_npc_record = record.Role == DlgRecordRole.Player ? parent?.Id : null,
                        reply_conditions = record.Role == DlgRecordRole.Player ? CcScanCodes(condition)
                            .Select(c => (object)new { code = c.Code, value = CcAtoi(condition.AsSpan(c.After)).Value }).ToArray() : [],
                        reply_response = record.Role == DlgRecordRole.Player ? record.ResponseValue : null,
                        reply_response_kind = record.Role == DlgRecordRole.Player ? record.ResponseKind?.ToString() : null,
                        jo_zero_node_form = code == "JO" && role == "action" && record.Role == DlgRecordRole.Player && first.Value == 0 && node.Value > 0,
                    });
                }
            }
        }

        var afterRun = archivePaths.Select(InputFingerprint.Create).ToArray();
        if (!before.SequenceEqual(afterRun) || InputFingerprint.Create(mobilePath) != mobileBefore) throw new InvalidDataException("CONSENT_INPUT_CHANGED");
        var report = new
        {
            inputs = CaveArchives.Select((a, i) => new { role = a.Role, bytes = before[i].Bytes, sha256 = before[i].Sha256 }).Append(new { role = "mobile", bytes = mobileBefore.Bytes, sha256 = mobileBefore.Sha256 }).ToArray(),
            inputs_unchanged = true,
            content_search_order = discovery.Plan!.SearchOrder.Select(s => SourceName(s)).ToArray(),
            virgil = new
            {
                source_index = virgil.Key.SourceIndex, prototype = virgil.PrototypeNumber, scope = virgil.Scope.ToString(), world_x = virgil.WorldX, world_y = virgil.WorldY,
                description = Number(InvDescriptionSlot), scripts = InvScripts(virgil),
                npc_flags = Field(CcNpcFlagsSlot), npc_flag_bits = Bits(Number(CcNpcFlagsSlot), CcNpcFlagNames),
                critter_flags = Field(CcCritterFlagsSlot), critter_flag_bits = Bits(Number(CcCritterFlagsSlot), CcCritterFlagNames),
                critter_flags2 = Field(CcCritterFlags2Slot), critter_flag2_bits = Bits(Number(CcCritterFlags2Slot), CcCritterFlag2Names),
                ai_data = Field(CcAiDataSlot), wait = Field(CcWaitSlot), reaction_base = Field(CcReactionBaseSlot),
                reaction_pc = Shape(CcReactionPcSlot), reaction_level = Field(CcReactionLevelSlot), leader = Shape(CcNpcLeaderSlot), followers = Shape(CcFollowersSlot),
                origin = Field(CcOriginSlot), faction = Field(CcFactionSlot), social_class = Field(CcSocialClassSlot), auto_level_scheme = Field(CcAutoLevelSlot),
                stat_base = new { origin = statField.Origin.ToString(), values = CcStatNames.Select((n, i) => new { stat = i, name = n, value = Stat(i) }).ToArray() },
                level = Stat(CcStatLevel), alignment = Stat(CcStatAlignment), charisma = Stat(CcStatCharisma), beauty = Stat(CcStatBeauty),
                gender = Stat(CcStatGender), race = Stat(CcStatRace),
                effects = effects.Select(EffectRow).ToArray(),
            },
            ai_tables = new
            {
                ai_params = aiParamsReport, gameai = gameAiReport,
                engine_rows = engineRows.Count, engine_rows_numbered_by_entry = engineRows.All(r => r.Row == r.Entry),
                module_rows = moduleRows.Count, module_rows_numbered_by_entry = moduleRows.All(r => r.Row == r.Entry),
                virgil_row = aiRow is null ? null : new { row = aiRow.Row, entry = aiRow.Entry, tokens = aiRow.Tokens, values = aiRow.Values,
                    follow = new { dislike_at_or_below = aiRow.Values[5], pc_above_npc_alignment_max = aiRow.Values[6], pc_below_npc_alignment_max = aiRow.Values[7],
                        npc_levels_above_pc_refused_at = aiRow.Values[8], hit_reaction_drop = aiRow.Values[9] } },
            },
            effect_tables = new { effect = effectReport, gameeffect = gameEffectReport },
            dialog = new { dlg = dialogReport, records = document.Records.Count, operations, reaction_actions = reactions },
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void CompanionConsentSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("consent self-test failed: " + label); }

        // atoi: whitespace, sign, stop at a non-digit, no digits, overflow flagged.
        Check(CcAtoi(" \t-12x"u8) == new CcInt(-12, false) && CcAtoi("+7"u8).Value == 7 && CcAtoi(",5"u8).Value == 0 && CcAtoi(""u8).Value == 0, "atoi");
        Check(CcAtoi("99999999999"u8).Overflow && CcAtoi("-2147483648"u8) == new CcInt(int.MinValue, false), "atoi overflow");

        // Node operand: skip spaces and the first operand's digits, then atoi.
        Check(CcSecondOperand(" 1 25"u8).Value == 25 && CcSecondOperand(" 0 385, GF 3 1"u8).Value == 385, "second operand");
        Check(CcSecondOperand(" 1,25"u8).Value == 0, "comma before the node reads 0");
        Check(CcSecondOperand(" -1 25"u8).Value == -1, "a signed first operand is not skipped");
        Check(CcSecondOperand(" 1"u8).Value == 0, "missing node reads 0");

        // Code scan: case, separators and offsets.
        var codes = CcScanCodes("gf 3 1, jo 1 20; UW 0 5"u8);
        Check(codes.Select(c => c.Code).SequenceEqual(["GF", "JO", "UW"]) && codes[1].After == 10, "code scan");

        // AI rows: exact first entry, running count, the limit, short and long entries.
        var mes = new SortedDictionary<int, string> { [0] = "1 2 3", [1] = "4\t5\n6", [5] = string.Join(' ', Enumerable.Range(0, 20)), [7] = "9" };
        var rows = CcAiRows(mes, 0, 3);
        Check(rows.Count == 3 && rows[2] is { Row: 2, Entry: 5, Tokens: 17 } && rows[2].Values[16] == 16, "running count and 17-value cap");
        Check(rows[0].Values.Take(4).SequenceEqual([1, 2, 3, 0]) && rows[1].Values.Take(3).SequenceEqual([4, 5, 6]), "short entry and delimiters");
        Check(CcAiRows(new SortedDictionary<int, string> { [1] = "1" }, 0, 100).Count == 0, "no exact first entry, no rows");
        Check(CcAiRows(new SortedDictionary<int, string> { [99] = "1", [100] = "2", [104] = "3" }, 100, 150).Select(r => (r.Row, r.Entry)).SequenceEqual([(100, 100), (101, 104)]), "module rows ignore lower entries");
        Check(CcAiRows(new SortedDictionary<int, string> { [0] = "5 \r" }, 0, 100)[0].Values.Take(2).SequenceEqual([5, 0]) &&
            CcAiRows(new SortedDictionary<int, string> { [0] = "5 \r" }, 0, 100)[0].Tokens == 2, "carriage return is a token, not a delimiter");

        // Effect stat symbols.
        Check(CcEffectStats("ST -1, CN +1 level max 5 foo").SequenceEqual([0, 2, 17]), "effect stats");
        Check(CcNamesReaction("REACTION +10") && !CcNamesReaction("badreactionadj +5, beautyreaction -1"), "reaction special");
        Check(CcStatNames.Length == 28 && CcEffectStatSymbols.Length == 28 && CcStatNames[CcStatLevel] == "level" && CcStatNames[CcStatAlignment] == "alignment", "stat tables");
        Console.WriteLine("companion-consent self-test: PASS (atoi, node operand, code scan, AI rows, effect stats, reaction special)");
    }
}
