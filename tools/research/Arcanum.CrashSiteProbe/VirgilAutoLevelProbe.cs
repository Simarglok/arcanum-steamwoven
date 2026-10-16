using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-031 research only: what Virgil's auto-level scheme 91 is, when his level
// can change, and which installed rules, effects and scripts could move his level
// relative to the PC's. Independently authored from the pinned CE locators cited in
// docs/research/virgil-auto-level.md; no CE code or structure is reused. No level-up
// runtime; ARC-RES-032 adds a research replay (VirgilLevelReplay.cs). Only numbers, booleans, engine rule tokens and SHA-256
// digests leave the probe; MES text other than parsed rule tokens stays in memory.
internal static partial class Program
{
    private const int AlHpPtsSlot = 26, AlHpAdjSlot = 27, AlFatiguePtsSlot = 42, AlFatigueAdjSlot = 43;
    private const int AlBasicSkillSlot = 39, AlTechSkillSlot = 40, AlSpellTechSlot = 41;
    private const int AlStatExperience = 18, AlStatUnspent = 21, AlStatMagickPoints = 22, AlStatTechPoints = 23;
    private const int AlStandardNpcScheme = 70, AlLevelMax = 51, AlMaxRuleBytes = 2000;
    private const int AlTransformAction = 40, AlAdjustStatAction = 137, AlSetSchemeAction = 139, AlNumberOperand = 3;
    private const int AlMaxScripts = 8192;

    // Rule-file vocabulary in the order the pinned source searches it (level.c 68-159):
    // stats, basic skills, tech skills, spell colleges, tech disciplines, misc.
    internal static readonly (string Category, string[] Tokens)[] AlRuleTokens =
    [
        ("stat", ["st", "dx", "cn", "be", "in", "pe", "wp", "ch"]),
        ("basic_skill", ["bow", "dodge", "melee", "throwing", "backstab", "pickpocket", "prowling", "spottrap", "gambling", "haggle", "heal", "persuasion"]),
        ("tech_skill", ["repair", "firearms", "picklock", "armtrap"]),
        ("college", ["conveyance", "divination", "air", "earth", "fire", "water", "force", "mental", "meta", "morph", "nature", "necro_evil", "necro_good", "phantasm", "summoning", "temporal"]),
        ("tech", ["anatomical", "chemistry", "electric", "explosives", "gun_smithy", "mechanical", "smithy", "therapeutics"]),
        ("misc", ["maxhps", "maxfatigue"]),
    ];

    internal sealed record AlStep(string Category, int Index, string Token, int Target);
    internal sealed record AlRule(IReadOnlyList<AlStep> Steps, bool Complete, string? Stop);

    // A scheme rule as the pinned processor reads it: split on space for the token, then
    // read the score from the text up to the next comma with atoi; the token is matched
    // case-insensitively against each category in search order. An unknown token stops
    // the rule (the source reports failure there); a token with no score text left is
    // flagged instead of reproduced (the source would pass a null pointer to atoi).
    internal static AlRule AlParseRule(string text)
    {
        var steps = new List<AlStep>(); var position = 0;
        while (AlStrtok(text, ref position, ' ') is { } token)
        {
            if (AlStrtok(text, ref position, ',') is not { } scoreText) return new(steps, false, "score_missing");
            var score = CcAtoi(Encoding.Latin1.GetBytes(scoreText)).Value;
            var match = AlRuleTokens.Select(c => (c.Category, Index: Array.FindIndex(c.Tokens, t => t.Equals(token, StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(m => m.Index >= 0);
            if (match.Category is null) return new(steps, false, "unknown_token");
            steps.Add(new(match.Category, match.Index, AlRuleTokens.First(c => c.Category == match.Category).Tokens[match.Index], score));
        }
        return new(steps, true, null);
    }

    // A MES table through the project's reader (docs/formats/mes-reader.md); any refusal
    // fails the probe rather than guessing what the original would keep.
    private static SortedDictionary<int, string> AlMes(byte[] bytes)
    {
        var parsed = MesFile.Parse(bytes);
        if (parsed.Status != MesParseStatus.Parsed || parsed.File is null) throw new InvalidDataException("AUTOLEVEL_MES_" + parsed.Status);
        return new(parsed.File.Messages.ToDictionary(m => m.Number, m => Encoding.Latin1.GetString(m.Bytes.Span)));
    }

    // C strtok with one delimiter on one buffer: skip leading delimiters, take up to the
    // next delimiter, resume after it.
    private static string? AlStrtok(string text, ref int position, char delimiter)
    {
        var i = position;
        while (i < text.Length && text[i] == delimiter) i++;
        if (i >= text.Length) { position = text.Length; return null; }
        var j = text.IndexOf(delimiter, i);
        if (j < 0) j = text.Length;
        position = Math.Min(j + 1, text.Length);
        return text[i..j];
    }

    // Character points for advancing old -> new: one per level, one more at each
    // multiple of five (level.c 432-447).
    internal static int AlBonusPoints(int oldLevel, int newLevel)
    {
        var points = 0;
        for (var level = oldLevel + 1; level <= newLevel; level++) points += level % 5 == 0 ? 2 : 1;
        return points;
    }

    // A follower's new base level when his PC leader goes from oldPc to newPc
    // (level.c 567-605): at or below the PC's old level he keeps his distance, above
    // it he is pulled up to the PC's new level at most; never past maxLevel - 1, never
    // down, and a follower already at maxLevel is left alone.
    internal static int AlFollowerLevel(int current, int oldPc, int newPc, int maxLevel = AlLevelMax)
    {
        if (current >= maxLevel) return current;
        var level = current <= oldPc ? newPc + current - oldPc : newPc;
        if (level >= maxLevel) level = maxLevel - 1;
        return level > current ? level : current;
    }

    internal readonly record struct AlAction(int Line, string Branch, int Type, byte[] Kinds, int[] Values);

    // Every action of a script file whose type is in `types`, with its eight operand
    // kinds and values (an action slot is a type, eight kind bytes, eight values).
    internal static List<AlAction> AlActions(ReadOnlySpan<byte> bytes, IReadOnlySet<int> types)
    {
        var (_, count, _) = CaveScriptFraming(bytes);
        var found = new List<AlAction>();
        for (var line = 0; line < count; line++)
            foreach (var (offset, branch) in new[] { (44, "true"), (88, "false") })
            {
                var slot = bytes.Slice(ScriptHeaderBytes + line * ScriptLineBytes + offset, 44);
                var type = BinaryPrimitives.ReadInt32LittleEndian(slot);
                if (!types.Contains(type)) continue;
                var values = new int[8];
                for (var k = 0; k < 8; k++) values[k] = BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * k)..]);
                found.Add(new(line, branch, type, slot.Slice(4, 8).ToArray(), values));
            }
        return found;
    }

    private static int ProbeVirgilAutoLevel(string[] args)
    {
        const int fixedArgs = 6;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--game-root" || args[2] != "--prototype-root" || args[4] != "--mobile")
            throw new ArgumentException("AUTOLEVEL_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("AUTOLEVEL_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var gameRoot = args[1]; var prototypeRoot = args[3]; var mobilePath = args[5];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("AUTOLEVEL_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha)) throw new InvalidDataException("AUTOLEVEL_AGGREGATE_IDENTITY");

        using var area = AreaOriginalValidator.OpenFiles(archivePaths[0], archivePaths[1], archivePaths[2], mobilePath, prototypeRoot);
        var snapshot = area.LoadStrict();
        var virgils = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope == AreaScope.Core && o.Type == CcNpcType &&
            o.EffectiveFields.FirstOrDefault(f => f.Slot == InvScriptsSlot) is { Presence: AreaPresence.Array } s &&
            s.Elements.Any(e => e.Key == CcDialogSap && e.Words[2] == CcVirgilScript)).ToArray();
        if (virgils.Length != 1) throw new InvalidDataException("AUTOLEVEL_VIRGIL_NOT_UNIQUE");
        var virgil = virgils[0];

        var discovery = ContentMountPlan.Discover(gameRoot);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("AUTOLEVEL_MOUNT_" + discovery.Status);
        var pinned = archivePaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (discovery.Plan!.SearchOrder.Any(s => s.Kind == ContentSourceKind.Archive && s.Role != ContentSourceRole.Tig && !pinned.Contains(Path.GetFullPath(s.Path))))
            throw new InvalidDataException("AUTOLEVEL_STACK_UNPINNED_ARCHIVE");
        var datOptions = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = CcMaxEntryBytes, MaxDecodedEntryBytes = CcMaxEntryBytes };
        var opened = ContentStack.Open(discovery.Plan!, new ContentStackOptions { Dat = datOptions });
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("AUTOLEVEL_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var archiveRoles = CaveArchives.Select((a, i) => (Path.GetFullPath(archivePaths[i]), a.Role)).ToDictionary(x => x.Item1, x => x.Role, StringComparer.OrdinalIgnoreCase);
        string SourceName(ContentSource source) => archiveRoles.TryGetValue(Path.GetFullPath(source.Path), out var role) ? role : $"{source.Role}:{source.Number}";

        // Every candidate of a resolved path, winner first; each must come from a pinned archive.
        (string Source, byte[] Bytes)[] Candidates(string path)
        {
            var resolution = stack.Resolve(path);
            if (resolution.Candidates.Any(c => !archiveRoles.ContainsKey(Path.GetFullPath(c.Source.Path)))) throw new InvalidDataException("AUTOLEVEL_UNPINNED_CANDIDATE");
            return resolution.Candidates.Select(c => (SourceName(c.Source), stack.Read(c) is { Status: ContentReadStatus.Read, Bytes: { } b } ? b : throw new InvalidDataException("AUTOLEVEL_READ"))).ToArray();
        }
        object Copies((string Source, byte[] Bytes)[] candidates) => candidates.Select(c => new { source = c.Source, bytes = c.Bytes.Length, sha256 = AreaHash(c.Bytes) }).ToArray();

        // Virgil's fields: stats, the scheme, and the three skill/spell arrays (numbers only).
        AreaFieldValue? FieldOf(int slot) => virgil.EffectiveFields.FirstOrDefault(f => f.Slot == slot);
        var statField = FieldOf(CcStatBaseSlot) is { Presence: AreaPresence.Array } sf ? sf : throw new InvalidDataException("AUTOLEVEL_STATS_MISSING");
        var stats = statField.Elements.ToDictionary(e => e.Key, e => e.Words.Count == 1 ? unchecked((int)e.Words[0]) : throw new InvalidDataException("AUTOLEVEL_STAT_WIDTH"));
        int? Stat(int index) => stats.TryGetValue(index, out var v) ? v : null;
        object IntArray(int slot)
        {
            var f = FieldOf(slot);
            if (f is null) return new { presence = "Missing", origin = (string?)null, nonzero = (object?)null, elements = 0 };
            if (f.Presence != AreaPresence.Array) return new { presence = f.Presence.ToString(), origin = (string?)f.Origin.ToString(), nonzero = (object?)null, elements = 0 };
            if (f.Elements.Any(e => e.Words.Count != 1)) throw new InvalidDataException("AUTOLEVEL_ARRAY_WIDTH");
            return new { presence = f.Presence.ToString(), origin = (string?)f.Origin.ToString(),
                nonzero = (object?)f.Elements.Where(e => e.Words[0] != 0).OrderBy(e => e.Key).Select(e => new { index = e.Key, value = unchecked((int)e.Words[0]) }).ToArray(), elements = f.Elements.Count };
        }
        var schemeField = FieldOf(CcAutoLevelSlot);
        var scheme = schemeField is { Presence: AreaPresence.Value, Number: { } sn } ? checked((int)sn) : throw new InvalidDataException("AUTOLEVEL_SCHEME_MISSING");

        // Scheme rules: 1..49 from the user file, 50..199 from the module rule file (level.c 678-701).
        var gameLevel = Candidates("rules/gamelevel.mes");
        var userLevel = Candidates("rules/userlevel.mes");
        var levelNames = Candidates("mes/gamelevelname.mes");
        if (gameLevel.Length == 0) throw new InvalidDataException("AUTOLEVEL_RULES_MISSING");
        object RuleRow(int number, (string Source, byte[] Bytes)[] candidates)
        {
            var copies = candidates.Select(c => (c.Source, Mes: AlMes(c.Bytes))).ToArray();
            object Parsed(SortedDictionary<int, string> mes)
            {
                if (!mes.TryGetValue(number, out var text)) return new { present = false };
                var rule = AlParseRule(text);
                return new { present = true, rule_bytes = Encoding.Latin1.GetByteCount(text), over_string_limit = Encoding.Latin1.GetByteCount(text) >= AlMaxRuleBytes,
                    complete = rule.Complete, stop = rule.Stop, step_count = rule.Steps.Count,
                    steps = rule.Steps.Select((s, k) => new { step = k, category = s.Category, index = s.Index, token = s.Token, target = s.Target }).ToArray() };
            }
            var texts = copies.Select(c => c.Mes.TryGetValue(number, out var t) ? t : null).ToArray();
            return new { scheme = number, winner = copies[0].Source,
                copies = copies.Select((c, k) => new { source = c.Source, same_as_winner = texts[k] == texts[0], rule = Parsed(c.Mes) }).ToArray() };
        }
        var nameMes = levelNames.Length == 0 ? new SortedDictionary<int, string>() : AlMes(levelNames[0].Bytes);

        // Effects that name the level stat (engine file 50..399, module file 0..49).
        var effectCopies = Candidates("rules/effect.mes");
        var gameEffectCopies = Candidates("rules/gameeffect.mes");
        if (effectCopies.Length == 0) throw new InvalidDataException("AUTOLEVEL_EFFECTS_MISSING");
        var effectTable = AlMes(effectCopies[0].Bytes).Where(p => p.Key is >= 50 and < 400)
            .Concat(gameEffectCopies.Length == 0 ? [] : AlMes(gameEffectCopies[0].Bytes).Where(p => p.Key is >= 0 and < 50)).ToArray();
        var levelEffects = effectTable.Where(p => CcEffectStats(p.Value).Contains(CcStatLevel)).Select(p => p.Key).Order().ToArray();

        // Magic-tech definitions: a trait-index component can set any base stat, the level
        // included, but only through the stat key "stat_level" (stat.c 225); count it and the
        // component keyword in every candidate (text stays in memory).
        var spellCopies = Candidates("rules/spelllist.mes");
        if (spellCopies.Length == 0) throw new InvalidDataException("AUTOLEVEL_SPELLLIST_MISSING");
        static int Occurrences(byte[] bytes, string word)
        {
            var text = Encoding.Latin1.GetString(bytes); var count = 0;
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }
        var magicTech = spellCopies.Select(c => new { source = c.Source, bytes = c.Bytes.Length, sha256 = AreaHash(c.Bytes),
            stat_level_keys = Occurrences(c.Bytes, "stat_level"), trait_idx_components = Occurrences(c.Bytes, "traitidx") }).ToArray();

        // Script census: every scr/ name in the six archives, resolved by precedence, then
        // the adjust-stat and set-scheme actions of each winner.
        var scriptNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in archivePaths)
        {
            using var archive = DatArchive.Open(path, datOptions);
            foreach (var entry in archive.Entries)
                if (ScriptNumber(entry) is not null) scriptNames.Add(ClosureEntryName(entry));
        }
        if (scriptNames.Count is 0 or > AlMaxScripts) throw new InvalidDataException("AUTOLEVEL_SCRIPT_COUNT");
        var attached = virgil.EffectiveFields.First(f => f.Slot == InvScriptsSlot).Elements.Where(e => e.Words[2] != 0)
            .Select(e => (Sap: e.Key, Script: unchecked((int)e.Words[2]))).ToArray();
        // How many Crash Site objects attach a script, and on which attachment points.
        object AttachmentsOf(int script) => snapshot.Objects
            .SelectMany(o => o.EffectiveFields.FirstOrDefault(f => f.Slot == InvScriptsSlot) is { Presence: AreaPresence.Array } f
                ? f.Elements.Where(e => e.Words[2] == script).Select(e => (o.Scope, Sap: e.Key)) : [])
            .GroupBy(x => (x.Scope, x.Sap)).Select(g => new { scope = g.Key.Scope.ToString(), sap = g.Key.Sap, objects = g.Count() }).ToArray();
        var wanted = new HashSet<int> { AlTransformAction, AlAdjustStatAction, AlSetSchemeAction };
        var framingFailures = new List<int>(); var shadowedScripts = 0; var scannedNumbers = new HashSet<int>();
        var adjusts = new List<object>(); var schemeSets = new List<object>(); var transforms = new SortedDictionary<int, int>(); var adjustByStat = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var attachedActions = attached.Select(a => a.Script).Distinct().ToDictionary(n => n, _ => 0);
        var edgesByScript = new Dictionary<int, (IReadOnlyList<CaveScriptEdge> Edges, int Unresolved)>();
        var actionsByScript = new Dictionary<int, List<AlAction>>(); var winnerBytes = new Dictionary<int, byte[]>();
        foreach (var name in scriptNames)
        {
            var number = ScriptNumberFromFile(name[4..])!.Value;
            var copies = Candidates(name);
            if (copies.Length == 0) throw new InvalidDataException("AUTOLEVEL_SCRIPT_UNRESOLVED");
            if (copies.Length > 1) shadowedScripts++;
            List<AlAction> actions;
            try { actions = AlActions(copies[0].Bytes, wanted); }
            catch (InvalidDataException) { framingFailures.Add(number); continue; }
            scannedNumbers.Add(number);
            var census = CaveScriptCensus(copies[0].Bytes);
            edgesByScript[number] = (census.Edges, census.Unresolved);
            actionsByScript[number] = actions; winnerBytes[number] = copies[0].Bytes;
            if (attachedActions.ContainsKey(number)) attachedActions[number] += actions.Count;
            foreach (var a in actions)
            {
                if (a.Type == AlAdjustStatAction)
                {
                    var key = a.Kinds[0] == AlNumberOperand ? a.Values[0].ToString(System.Globalization.CultureInfo.InvariantCulture) : "computed";
                    adjustByStat[key] = adjustByStat.GetValueOrDefault(key) + 1;
                    adjusts.Add(new { script = number, line = a.Line, branch = a.Branch, stat_kind = (int)a.Kinds[0], stat = a.Values[0],
                            focus = (int)a.Kinds[1], focus_index = a.Values[1], value_kind = (int)a.Kinds[2], value = a.Values[2] });
                }
                else if (a.Type == AlTransformAction)
                    transforms[number] = transforms.GetValueOrDefault(number) + 1;
                else
                    schemeSets.Add(new { script = number, line = a.Line, branch = a.Branch, focus = (int)a.Kinds[0], focus_index = a.Values[0],
                        scheme_kind = (int)a.Kinds[1], scheme = a.Values[1], crash_site_attachments = AttachmentsOf(number) });
            }
        }

        // ARC-RES-032: the call-edge reach of Virgil's scripts, who attaches or calls the
        // set-scheme scripts, and the numeric lines of the set-scheme and adjust-stat scripts.
        var (reached, reachUnresolved, reachMissing) = AlReach(attached.Select(a => a.Script), n => edgesByScript.TryGetValue(n, out var c) ? c : null);
        var reach = new
        {
            roots = attached.Select(a => a.Script).Distinct().Order().ToArray(), reached = reached.ToArray(), missing = reachMissing.ToArray(), unresolved_edges = reachUnresolved,
            reached_actions = reached.Select(n => new { script = n, transform = actionsByScript[n].Count(a => a.Type == AlTransformAction),
                adjust_stat = actionsByScript[n].Count(a => a.Type == AlAdjustStatAction), set_scheme = actionsByScript[n].Count(a => a.Type == AlSetSchemeAction) })
                .Where(x => x.transform + x.adjust_stat + x.set_scheme > 0).ToArray(),
        };
        var schemeScripts = actionsByScript.Where(p => p.Value.Any(a => a.Type == AlSetSchemeAction)).Select(p => p.Key).Order().ToArray();
        var callers = schemeScripts.Select(target => new
        {
            script = target, crash_site_attachments = AttachmentsOf(target),
            callers = edgesByScript.Where(p => p.Value.Edges.Any(e => e.Target == target)).Select(p => new { script = p.Key,
                actions = p.Value.Edges.Where(e => e.Target == target).Select(e => e.Action).ToArray(), crash_site_attachments = AttachmentsOf(p.Key) }).ToArray(),
            lines = AlDecode(winnerBytes[target]),
        }).ToArray();
        // Global flags the set-scheme scripts test, and who writes them: script set/clear-flag
        // actions and flag assignments with a literal flag, and DLG GF actions.
        var flagsTested = schemeScripts.SelectMany(n => AlActionsAndConditions(winnerBytes[n]).Where(x => x.Condition).SelectMany(x => Enumerable.Range(0, 2).Where(k => x.Kinds[k] == AlFlagOperand).Select(k => x.Values[k]))).Distinct().Order().ToArray();
        var flagWriters = new List<object>();
        foreach (var (number, bytes) in winnerBytes.OrderBy(p => p.Key))
            foreach (var x in AlActionsAndConditions(bytes).Where(x => !x.Condition))
            {
                if (x.Type is AlSetFlagAction or AlClearFlagAction && x.Kinds[0] == AlNumberOperand && flagsTested.Contains(x.Values[0]))
                    flagWriters.Add(new { source = "script", number, line = x.Line, action = x.Type, flag = x.Values[0], value = x.Type == AlSetFlagAction ? 1 : 0 });
                else if (x.Type == AlAssignAction && x.Kinds[0] == AlFlagOperand && flagsTested.Contains(x.Values[0]))
                    flagWriters.Add(new { source = "script", number, line = x.Line, action = x.Type, flag = x.Values[0], value = x.Kinds[1] == AlNumberOperand ? x.Values[1] : (int?)null });
            }
        var dialogNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in archivePaths)
        {
            using var archive = DatArchive.Open(path, datOptions);
            foreach (var entry in archive.Entries) if (InvDialogKey(entry) is not null) dialogNames.Add(ClosureEntryName(entry));
        }
        var unreadableDialogs = 0;
        foreach (var name in dialogNames)
        {
            var copies = Candidates(name);
            if (copies.Length == 0) continue;
            DlgDocument document;
            try { document = DlgReader.Read(copies[0].Bytes); } catch (DlgException) { unreadableDialogs++; continue; }
            foreach (var record in document.Records)
            {
                var bytes = record.Action.Bytes.ToArray();
                foreach (var (code, after) in CcScanCodes(bytes))
                    if (code == "GF" && CcAtoi(bytes.AsSpan(after)).Value is var flag && flagsTested.Contains(flag))
                        flagWriters.Add(new { source = "dlg", number = InvDialogKeyFromName(name), line = record.Id, action = 0, flag, value = (int?)CcSecondOperand(bytes.AsSpan(after)).Value });
            }
        }
        var setSchemeRules = schemeSets.Count == 0 ? [] : actionsByScript.SelectMany(p => p.Value).Where(a => a.Type == AlSetSchemeAction && a.Kinds[1] == AlNumberOperand)
            .Select(a => a.Values[1]).Distinct().Order().Select(n => RuleRow(n, n < 50 ? userLevel : gameLevel)).ToArray();
        var adjustScriptLines = actionsByScript.Where(p => attachedActions.ContainsKey(p.Key) && p.Value.Any(a => a.Type == AlAdjustStatAction))
            .Select(p => new { script = p.Key, lines = AlDecode(winnerBytes[p.Key]) }).ToArray();

        // ARC-RES-032: replay of the scheme over Virgil's T0 sheet while he follows a PC from level 1 to 50.
        var effectTexts = effectTable.ToDictionary(p => p.Key, p => p.Value);
        var virgilEffects = new List<AlChange>();
        foreach (var effect in FieldOf(CcEffectsSlot) is { Presence: AreaPresence.Array } ef ? ef.Elements.OrderBy(x => x.Key).Select(x => unchecked((int)x.Words[0])) : [])
        {
            if (!effectTexts.TryGetValue(effect, out var text)) throw new InvalidDataException("AUTOLEVEL_REPLAY_EFFECT_MISSING");
            var parsed = AlParseEffect(text);
            if (!parsed.Complete) throw new InvalidDataException("AUTOLEVEL_REPLAY_EFFECT_" + parsed.Stop!.ToUpperInvariant());
            virgilEffects.AddRange(parsed.Changes);
        }
        int[] Raw(int slot, int length, int mask)
        {
            var values = new int[length];
            if (FieldOf(slot) is { Presence: AreaPresence.Array } f)
                foreach (var x in f.Elements) { if (x.Key < 0 || x.Key >= length) continue; values[x.Key] = unchecked((int)x.Words[0]) & mask; }
            return values;
        }
        int NumberOf(int slot) => FieldOf(slot) is { Presence: AreaPresence.Value, Number: { } n } ? checked((int)n) : throw new InvalidDataException("AUTOLEVEL_REPLAY_FIELD");
        var spellMes = AlMes(spellCopies[0].Bytes);
        var willpower = Enumerable.Range(0, 80).Select(id =>
        {
            // The willpower a spell needs: an "IQ:" value at the start of its second entry, else the default by rank (magictech.c 1301-1330, 5520-5530, 5650-5660).
            if (spellMes.TryGetValue(1000 + 50 * id + 1, out var t) && t.TrimStart() is var u && u.StartsWith("IQ:", StringComparison.Ordinal))
                return CcAtoi(Encoding.Latin1.GetBytes(u[3..].Split(',')[0])).Value;
            return AlSpellDefaultWillpower[id % 5];
        }).ToArray();
        var overrides = Enumerable.Range(0, 80).Count(id => willpower[id] != AlSpellDefaultWillpower[id % 5]);
        var sheet = new AlSheet
        {
            Stats = Enumerable.Range(0, 28).Select(i => Stat(i) ?? 0).ToArray(),
            BasicPoints = Raw(AlBasicSkillSlot, 12, 63), TechPoints = Raw(AlTechSkillSlot, 4, 63), Colleges = Raw(AlSpellTechSlot, 16, -1),
            HpPts = NumberOf(AlHpPtsSlot), HpAdj = NumberOf(AlHpAdjSlot), FatiguePts = NumberOf(AlFatiguePtsSlot), FatigueAdj = NumberOf(AlFatigueAdjSlot),
            Effects = virgilEffects, SpellWillpower = willpower,
        };
        var trainingBits = Raw(AlBasicSkillSlot, 12, ~63).Concat(Raw(AlTechSkillSlot, 4, ~63)).Any(v => v != 0);
        var standardSteps = AlParseRule(AlMes(gameLevel[0].Bytes)[AlStandardNpcScheme]).Steps;
        var schemeCopies = (scheme < 50 ? userLevel : gameLevel).Select(c => (c.Source, Mes: AlMes(c.Bytes))).Where(c => c.Mes.ContainsKey(scheme)).ToArray();
        object Snapshot(AlSheet x) => new
        {
            stats = new { st = x.StatLevel(0), dx = x.StatLevel(1), cn = x.StatLevel(2), @in = x.StatLevel(4), pe = x.StatLevel(5), wp = x.StatLevel(6),
                base_in = x.Stats[4], base_st = x.Stats[0] },
            dodge = x.BasicLevel(1), melee = x.BasicLevel(2), heal = x.BasicLevel(10), picklock = x.TechLevel(2), necro_good = x.Colleges[12],
            hp_max = x.HpMax, fatigue_max = x.FatigueMax,
        };
        var replays = schemeCopies.Select(c =>
        {
            var steps = AlParseRule(c.Mes[scheme]).Steps;
            var rows = AlReplay(sheet, steps, standardSteps, AlLevelMax - 1);
            return new
            {
                source = c.Source,
                levels = rows.Select(r => new { level = r.Level, granted = r.PointsGranted, unspent_before = r.UnspentBefore,
                    scheme_completed = r.Scheme.Completed, stopped_at = r.Scheme.StoppedAt, stopped_token = r.Scheme.StoppedAt is { } k ? $"{steps[k].Token} {steps[k].Target}" : null,
                    standard_completed = r.Standard?.Completed, standard_stopped_at = r.Standard?.StoppedAt, unspent_after = r.UnspentAfter, wasted_total = r.WastedTotal, sheet = Snapshot(r.After) }).ToArray(),
            };
        }).ToArray();
        var replay = new
        {
            model = "follower matching the PC one level at a time from T0; CE-as-read purchase helpers",
            effects = virgilEffects.Select(c => new { category = c.Category, index = c.Index, op = c.Op.ToString(), param = c.Param }).ToArray(),
            hp_pts = sheet.HpPts, hp_adj = sheet.HpAdj, fatigue_pts = sheet.FatiguePts, fatigue_adj = sheet.FatigueAdj, training_bits_present = trainingBits,
            spell_willpower_overrides = overrides, necro_good_willpower = Enumerable.Range(60, 5).Select(id => willpower[id]).ToArray(),
            t0 = Snapshot(sheet), copies = replays,
        };

        var afterRun = archivePaths.Select(InputFingerprint.Create).ToArray();
        if (!before.SequenceEqual(afterRun) || InputFingerprint.Create(mobilePath) != mobileBefore) throw new InvalidDataException("AUTOLEVEL_INPUT_CHANGED");
        var report = new
        {
            inputs = CaveArchives.Select((a, i) => new { role = a.Role, bytes = before[i].Bytes, sha256 = before[i].Sha256 }).Append(new { role = "mobile", bytes = mobileBefore.Bytes, sha256 = mobileBefore.Sha256 }).ToArray(),
            inputs_unchanged = true,
            content_search_order = discovery.Plan!.SearchOrder.Select(s => SourceName(s)).ToArray(),
            virgil = new
            {
                source_index = virgil.Key.SourceIndex, prototype = virgil.PrototypeNumber,
                name = FieldOf(InvNameSlot) is { Presence: AreaPresence.Value, Number: { } nm } ? nm : (long?)null, description = FieldOf(InvDescriptionSlot)?.Number,
                auto_level_scheme = new { value = scheme, origin = schemeField!.Origin.ToString() },
                stat_origin = statField.Origin.ToString(),
                level = Stat(CcStatLevel), experience_points = Stat(AlStatExperience), unspent_points = Stat(AlStatUnspent),
                magick_points = Stat(AlStatMagickPoints), tech_points = Stat(AlStatTechPoints),
                primary_stats = CcStatNames.Take(8).Select((n, i) => new { stat = i, name = n, value = Stat(i) }).ToArray(),
                basic_skills = IntArray(AlBasicSkillSlot), tech_skills = IntArray(AlTechSkillSlot), spell_tech = IntArray(AlSpellTechSlot),
                attached_scripts = attached.Select(a => new { sap = a.Sap, script = a.Script, scanned = scannedNumbers.Contains(a.Script),
                    transform_adjust_stat_or_set_scheme_actions = attachedActions[a.Script] }).ToArray(),
            },
            rules = new
            {
                gamelevel = Copies(gameLevel), userlevel = Copies(userLevel), gamelevelname = Copies(levelNames),
                gamelevel_entries = AlMes(gameLevel[0].Bytes).Keys.ToArray(),
                userlevel_entries = userLevel.Length == 0 ? [] : AlMes(userLevel[0].Bytes).Keys.ToArray(),
                virgil_scheme = RuleRow(scheme, scheme < 50 ? userLevel : gameLevel),
                standard_npc_scheme = RuleRow(AlStandardNpcScheme, gameLevel),
                names_present = new { virgil_scheme = nameMes.ContainsKey(scheme), standard_npc_scheme = nameMes.ContainsKey(AlStandardNpcScheme) },
            },
            magic_tech = magicTech,
            level_effects = new { effect = Copies(effectCopies), gameeffect = Copies(gameEffectCopies), table_entries = effectTable.Length, naming_level = levelEffects },
            scripts = new
            {
                names = scriptNames.Count, scanned = scannedNumbers.Count, shadowed_by_precedence = shadowedScripts, framing_failures = framingFailures,
                adjust_stat_by_literal_stat = adjustByStat, adjust_stat_actions = adjusts, set_scheme_actions = schemeSets,
                transform_into_prototype = new { actions = transforms.Values.Sum(), by_script = transforms.Select(t => new { script = t.Key, actions = t.Value }).ToArray() },
                virgil_reach = reach, set_scheme_scripts = callers, attached_adjust_stat_scripts = adjustScriptLines,
                flags_tested_by_set_scheme_scripts = flagsTested, flag_writers = flagWriters, dialogs_scanned = dialogNames.Count, dialogs_unreadable = unreadableDialogs, set_scheme_rules = setSchemeRules,
            },
            replay,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void VirgilAutoLevelSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("autolevel self-test failed: " + label); }

        // Rule grammar: space before the score, comma after it, case, unknown token, missing score.
        var rule = AlParseRule("necro_good 1, HEAL 4,maxhps 36, picklock 8");
        Check(rule.Complete && rule.Steps.Select(s => (s.Category, s.Index, s.Target)).SequenceEqual(
            [("college", 12, 1), ("basic_skill", 10, 4), ("misc", 0, 36), ("tech_skill", 2, 8)]), "rule steps");
        Check(AlParseRule("st 10, foo 3, dx 5") is { Complete: false, Stop: "unknown_token" } r1 && r1.Steps.Count == 1, "unknown token stops");
        Check(AlParseRule("st 10, dx") is { Complete: false, Stop: "score_missing" } r2 && r2.Steps.Count == 1, "missing score");
        Check(AlParseRule("heal\t4, st 5") is { Complete: false, Stop: "unknown_token" }, "tab is not a token delimiter");
        Check(AlParseRule("st x7, dx -2") is { Complete: true } r3 && r3.Steps.Select(s => s.Target).SequenceEqual([0, -2]), "atoi score");
        Check(AlParseRule("") is { Complete: true, Steps.Count: 0 }, "empty rule");
        Check(AlRuleTokens.Sum(c => c.Tokens.Length) == 50, "token tables");

        // Bonus points: one per level, one more at multiples of five.
        Check(AlBonusPoints(1, 2) == 1 && AlBonusPoints(4, 5) == 2 && AlBonusPoints(3, 3) == 0 && AlBonusPoints(1, 50) == 59, "bonus points");

        // Follower level: keeps the gap below the PC, is pulled up above it, capped below the maximum.
        Check(AlFollowerLevel(1, 1, 2) == 2 && AlFollowerLevel(1, 5, 6) == 2 && AlFollowerLevel(5, 5, 9) == 9, "keeps the gap");
        Check(AlFollowerLevel(7, 5, 6) == 7 && AlFollowerLevel(7, 5, 9) == 9, "above the PC: at most the PC's new level");
        Check(AlFollowerLevel(49, 49, 51) == 50 && AlFollowerLevel(51, 1, 2) == 51 && AlFollowerLevel(50, 50, 51) == 50, "maximum");
        // Invariant over every input: the NPC-minus-PC gap never grows and never turns positive by levelling.
        for (var current = 0; current <= AlLevelMax; current++)
            for (var oldPc = 1; oldPc < AlLevelMax; oldPc++)
                for (var newPc = oldPc + 1; newPc <= AlLevelMax; newPc++)
                {
                    var after = AlFollowerLevel(current, oldPc, newPc);
                    Check(after >= current && after - newPc <= current - oldPc && (current > oldPc || after <= newPc), "gap invariant");
                }

        // Action scan: header, two lines, an adjust-stat in a true branch and a set-scheme in a false branch.
        var bytes = new byte[ScriptHeaderBytes + 2 * ScriptLineBytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 2);
        var adjust = bytes.AsSpan(ScriptHeaderBytes + 44);
        BinaryPrimitives.WriteInt32LittleEndian(adjust, AlAdjustStatAction);
        adjust[4] = AlNumberOperand; adjust[5] = 1; adjust[6] = AlNumberOperand;
        BinaryPrimitives.WriteInt32LittleEndian(adjust[12..], CcStatLevel); BinaryPrimitives.WriteInt32LittleEndian(adjust[20..], 5);
        var set = bytes.AsSpan(ScriptHeaderBytes + ScriptLineBytes + 88);
        BinaryPrimitives.WriteInt32LittleEndian(set, AlSetSchemeAction); set[5] = AlNumberOperand; BinaryPrimitives.WriteInt32LittleEndian(set[16..], 91);
        var found = AlActions(bytes, new HashSet<int> { AlAdjustStatAction, AlSetSchemeAction });
        Check(found.Count == 2 && found[0] is { Line: 0, Branch: "true", Type: AlAdjustStatAction } && found[0].Values[0] == CcStatLevel && found[0].Kinds[1] == 1 && found[0].Values[2] == 5, "adjust-stat scan");
        Check(found[1] is { Line: 1, Branch: "false", Type: AlSetSchemeAction } && found[1].Kinds[1] == AlNumberOperand && found[1].Values[1] == 91, "set-scheme scan");
        var torn = false; try { AlActions(bytes.AsSpan(0, bytes.Length - 1), new HashSet<int>()); } catch (InvalidDataException) { torn = true; }
        Check(torn, "torn script refused");
        VirgilLevelReplaySelfTest();
        Console.WriteLine("virgil-auto-level self-test: PASS (rule grammar, bonus points, follower level and gap invariant, action scan)");
    }
}
