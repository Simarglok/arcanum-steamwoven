using System.Text.Json;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Data.Scripts;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-1329 research only (issue #1329, plan slice E8-g of docs/project/missing-systems-plan.md): which data of the installed
// game can put poison on a critter or take it off, read from the module content stack. It covers the spell records (Damage and
// Heal components of the poison type), item prototypes (the poison slot of a weapon's damage table, the spells an item holds,
// grenades), the NPC prototypes (the monster damage table's poison slot, a stored poison level), the placed mobiles (NPCs and the
// items they carry, by effective fields), the scripts (actions 60 and 138 of the poison type, heal_poison, the stat actions that
// name stat 24, the spell-casting actions that name a poison spell) with their placed sites, and the native trap scripts 30001..30006
// with their counters. Aggregates only: numbers and map names; never text, names or payload bytes.
internal static partial class Program
{
    private const int PcPoisonStat = 24, PcPoisonSlot = 1, PcActionDamage = 60, PcActionDamageUnresistable = 138, PcActionHealPoison = 128,
        PcActionAdjustStat = 137, PcActionGetStat = 33, PcActionHealHitPoints = 75;
    private static readonly int[] PcCastActions = [48, 61, 118, 121, 122, 136];

    private static int ProbePoisonCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("POISON_CENSUS_USAGE --poison-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"POISON_CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"POISON_CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|POISON_CENSUS_READ_{result.Status}");
            return null;
        }

        // 1. Spells: Damage and Heal components of the poison type, and every spell a poison spell casts onward.
        var spellLoad = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
        if (spellLoad.Data is not { } spellData) throw new InvalidDataException($"POISON_CENSUS_SPELLS_{spellLoad.Status}");
        var poisonDamageSpells = new SortedDictionary<int, List<object>>();
        var poisonHealSpells = new SortedDictionary<int, List<object>>();
        var castsOnward = new Dictionary<int, int[]>();
        foreach (var record in spellData.Records.Records)
        {
            castsOnward[record.Number] = record.Components.Select(c => c.Data).OfType<SpellReferenceData>().Select(r => r.Spell).Where(s => s is >= 0 and < SpellRecord.Count).ToArray();
            foreach (var component in record.Components)
            {
                if (component.Data is not DamageData d || d.DamageType != PcPoisonSlot) continue;
                var row = new { action = component.Action.ToString(), min = d.Minimum, max = d.Maximum, flags = d.Flags, type = component.Type.ToString() };
                (component.Type == SpellComponentType.Heal ? poisonHealSpells : poisonDamageSpells).TryAdd(record.Number, []);
                (component.Type == SpellComponentType.Heal ? poisonHealSpells : poisonDamageSpells)[record.Number].Add(row);
            }
        }
        // A spell that casts a poison spell (a Cast component) reaches poison too; follow the chain to a fixed point.
        var poisonSet = new HashSet<int>(poisonDamageSpells.Keys);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (spell, next) in castsOnward) if (!poisonSet.Contains(spell) && next.Any(poisonSet.Contains)) { poisonSet.Add(spell); changed = true; }
        }
        var healSet = new HashSet<int>(poisonHealSpells.Keys);
        object SpellRow(KeyValuePair<int, List<object>> p)
        {
            var record = spellData.Records.Records[p.Key];
            return new
            {
                spell = p.Key, name = spellData.Texts.Name(p.Key), college = record.College, rank = record.Rank, technology = record.IsTechnology, info = record.Info.ToString(),
                components = record.Components.Select(c => c.Type.ToString()).ToArray(), actions = p.Value, resist_stat = record.ResistStat, no_resist = record.NoResist,
                ai_offensive = record.AiPriorities[3], ai_cure_poison = record.AiPriorities[7],
            };
        }

        // 1b. Effects: every message of the effect and item-effect tables that names the poison level (stat 24) or a poison resistance, by file and number.
        var effectsWithPoison = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var effectNumbersPoisonLevel = new HashSet<int>();
        foreach (var path in new[] { "rules/effect.mes", "rules/gameeffect.mes", "mes/item_effect.mes", "mes/gameitemeffect.mes", "mes/gamebless.mes", "mes/gamecurse.mes" })
        {
            var resolution = stack.Resolve(path);
            if (resolution.Status != ContentResolutionStatus.Resolved) { effectsWithPoison[path] = "absent"; continue; }
            if (resolution.Winner is not { } winner || Read(winner, "mes") is not { } mesBytes) continue;
            if (MesFile.Parse(mesBytes).File is not { } mes) continue;
            var rows = new List<object>();
            foreach (var message in mes.Messages)
            {
                var text = System.Text.Encoding.Latin1.GetString(message.Bytes.Span);
                var level = text.Contains("poisonlevel", StringComparison.OrdinalIgnoreCase); var rate = text.Contains("poisonrate", StringComparison.OrdinalIgnoreCase);
                var resist = text.Contains("resistpoison", StringComparison.OrdinalIgnoreCase);
                if (!(level || rate || resist)) continue;
                rows.Add(new { number = message.Number, poison_level = level, poison_rate = rate, resist_poison = resist });
                if (level && path == "rules/effect.mes") effectNumbersPoisonLevel.Add(message.Number);
            }
            effectsWithPoison[path] = new { messages = mes.Count, matching = rows };
        }
        var spellsWithPoisonLevelEffect = spellData.Records.Records.Where(r => r.Components.Any(c => c.Data is EffectData e && effectNumbersPoisonLevel.Contains(e.Effect)))
            .Select(r => r.Number).ToArray();

        // 2. Prototypes through the catalogue and the combat-field reader.
        var catalog = WorldCatalog.Open(stack);
        var prototypeObjects = new Dictionary<int, WorldObject>();
        var prototypeFields = new Dictionary<int, CombatFields>();
        foreach (var number in catalog.PrototypeNumbers.Order())
        {
            var read = catalog.ReadPrototype(number);
            if (read.Prototype is null) { refusals.Add($"prototype|{read.Issue?.Code}"); continue; }
            prototypeObjects[number] = read.Prototype;
            var fields = CombatFieldReader.ReadPrototype(catalog.Prototypes.Read, number);
            if (fields.Fields is not null) prototypeFields[number] = fields.Fields;
            else refusals.Add($"prototype_fields|{fields.Failure}");
        }
        static bool PoisonDamage(IReadOnlyList<CombatDamageRange> damage) => damage.Count > PcPoisonSlot && damage[PcPoisonSlot].Maximum > 0;
        object Range(CombatDamageRange r) => new { min = r.Minimum, max = r.Maximum };
        var weaponsPoison = prototypeFields.Where(f => f.Value.Weapon is { } w && PoisonDamage(w.Damage)).OrderBy(f => f.Key)
            .Select(f => new
            {
                prototype = f.Key, skill = f.Value.Weapon!.Skill.ToString(), ammo_type = f.Value.Weapon.AmmoType, poison = Range(f.Value.Weapon.Damage[PcPoisonSlot]),
                normal = Range(f.Value.Weapon.Damage[0]), item_flags = f.Value.Item?.Flags, spells = f.Value.Item?.Spells.Where(s => s != CcNoSpell).ToArray(),
            }).ToArray();
        var npcPoison = prototypeFields.Where(f => f.Value.Npc is { } n && PoisonDamage(n.Damage)).OrderBy(f => f.Key)
            .Select(f => new
            {
                prototype = f.Key, range = CcRange(f.Key), poison = Range(f.Value.Npc!.Damage[PcPoisonSlot]), normal = Range(f.Value.Npc.Damage[0]),
                flags = f.Value.Critter!.Flags, level = f.Value.Critter.BaseStats.Count > 17 ? f.Value.Critter.BaseStats[17] : 0,
            }).ToArray();
        var storedPoison = prototypeFields.Where(f => f.Value.Critter is { } c && c.BaseStats.Count > PcPoisonStat && c.BaseStats[PcPoisonStat] != 0).Select(f => f.Key).Order().ToArray();
        bool HoldsSpell(CombatFields f, HashSet<int> set) => f.Item is { } i && i.Spells.Any(set.Contains);
        object ItemRow(KeyValuePair<int, CombatFields> f, HashSet<int> set) => new
        {
            prototype = f.Key, range = CcRange(f.Key), type = f.Value.Object.Type,
            grenade = f.Value.Generic is { } g && (g.Flags & CcGrenade) != 0, healing_item = f.Value.Generic is { } h && (h.Flags & CcHealingItem) != 0,
            spells = f.Value.Item!.Spells.Where(set.Contains).ToArray(),
            ai_action = f.Value.Item.AiAction, valid_ai_action = (f.Value.Item.Flags & CcValidAiAction) != 0,
        };
        var itemsPoisonSpell = prototypeFields.Where(f => HoldsSpell(f.Value, poisonSet)).OrderBy(f => f.Key).Select(f => ItemRow(f, poisonSet)).ToArray();
        var itemsHealSpell = prototypeFields.Where(f => HoldsSpell(f.Value, healSet)).OrderBy(f => f.Key).Select(f => ItemRow(f, healSet)).ToArray();

        // 3. Placed mobiles (NPCs and the items they carry, every map), by effective fields; count by prototype.
        var placedNpcPoison = new SortedDictionary<int, int>(); var placedWeaponPoison = new SortedDictionary<int, int>();
        var placedItemPoisonSpell = new SortedDictionary<int, int>(); var placedItemHealSpell = new SortedDictionary<int, int>();
        var placedStoredPoison = 0; var mapsWithPoisonNpc = new SortedSet<string>(StringComparer.Ordinal);
        var casterNpcs = new SortedDictionary<int, int>(); var casterNpcMaps = new SortedSet<string>(StringComparer.Ordinal);
        var poisonCollegeSpells = spellData.Records.Records.Where(r => poisonDamageSpells.ContainsKey(r.Number) && r.College is not null).ToArray();
        foreach (var map in catalog.Maps)
        {
            var mobiles = catalog.ReadMobiles(map);
            for (var i = 0; i < mobiles.Objects.Count; i++)
            {
                var o = mobiles.Objects[i];
                if (o.PrototypeNumber is not { } p || !prototypeObjects.TryGetValue(p, out var prototype)) continue;
                if (o.Type is < CombatFieldReader.WeaponType or > CombatFieldReader.NpcType) continue;
                var read = CombatFieldReader.Read(CcEffective(o, prototype, i));
                if (read.Fields is not { } f) { refusals.Add($"instance|{read.Failure}"); continue; }
                if (f.Npc is { } n && PoisonDamage(n.Damage)) { placedNpcPoison[p] = placedNpcPoison.GetValueOrDefault(p) + 1; mapsWithPoisonNpc.Add(map.Name); }
                if (f.Critter is { } c && c.BaseStats.Count > PcPoisonStat && c.BaseStats[PcPoisonStat] != 0) placedStoredPoison++;
                if (f.Weapon is { } w && PoisonDamage(w.Damage)) placedWeaponPoison[p] = placedWeaponPoison.GetValueOrDefault(p) + 1;
                if (f.Critter is { } cc && f.Npc is not null)
                    foreach (var rec in poisonCollegeSpells)
                        if (cc.SpellTech.Count > rec.College!.Value && cc.SpellTech[rec.College.Value] >= rec.Rank!.Value) { casterNpcs[rec.Number] = casterNpcs.GetValueOrDefault(rec.Number) + 1; casterNpcMaps.Add(map.Name); }
                if (HoldsSpell(f, poisonSet)) placedItemPoisonSpell[p] = placedItemPoisonSpell.GetValueOrDefault(p) + 1;
                if (HoldsSpell(f, healSet)) placedItemHealSpell[p] = placedItemHealSpell.GetValueOrDefault(p) + 1;
            }
        }

        // 4. Scripts: every winner of scr/<digits>*.scr, then the placed sites by script number.
        var keys = CensusKeys(stack);
        var byNumber = keys.Keys.Where(k => k.StartsWith("scr/", StringComparison.Ordinal) && k.Count(c => c == '/') == 1 && k.EndsWith(".scr", StringComparison.Ordinal))
            .Select(k => (Key: k, Number: ScriptNumberFromFile(k[4..]))).Where(p => p.Number is > 0)
            .GroupBy(p => p.Number!.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).Order(StringComparer.Ordinal).Last());
        var scripts = new SortedDictionary<int, WorldScript>();
        foreach (var (number, key) in byNumber)
        {
            if (Read(keys[key], "scr") is not { } bytes) continue;
            try { scripts[number] = DecodeWorldScript(bytes); } catch (InvalidDataException e) { refusals.Add("script|" + e.Message); }
        }
        var walk = SaWalk(keys, Read, s => s != 0, refusals);
        var placedSites = walk.Hits.Where(h => h.Kind is not "aggregate" and not "prototype").ToArray();
        int? LiteralOf(WorldClause clause, int slot) => slot < clause.Kinds.Length && clause.Kinds[slot] == (byte)ScrValueKind.Number ? clause.Values[slot] : null;
        // type, operand 2 of 60 and 138 (the damage type), per script: clauses and kinds of operand.
        var damageClauses = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var damageTypeCount = new CensusTally();
        var healPoison = new SortedSet<int>(); var statActionsOn24 = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var castsPoison = new SortedDictionary<int, SortedSet<int>>(); var castsHealPoison = new SortedDictionary<int, SortedSet<int>>();
        var allCasts = new CensusTally();
        foreach (var (number, script) in scripts)
        {
            foreach (var line in script.Lines)
            {
                foreach (var clause in new[] { line.True, line.False })
                {
                    if (clause.Type is PcActionDamage or PcActionDamageUnresistable)
                    {
                        var type = LiteralOf(clause, 2);
                        var label = $"a{clause.Type}:{(type is { } t ? t.ToString() : "variable")}";
                        damageTypeCount.Add(label);
                        if (!damageClauses.TryGetValue(label, out var set)) damageClauses[label] = set = [];
                        set.Add(number);
                    }
                    else if (clause.Type == PcActionHealPoison) healPoison.Add(number);
                    else if (clause.Type is PcActionAdjustStat or PcActionGetStat)
                    {
                        // Any literal operand of get stat (33) or adjust stat (137) naming stat 24 (the poison level).
                        if (Enumerable.Range(0, Math.Min(clause.Kinds.Length, 3)).Any(slot => LiteralOf(clause, slot) == PcPoisonStat))
                        {
                            if (!statActionsOn24.TryGetValue($"a{clause.Type}", out var set)) statActionsOn24[$"a{clause.Type}"] = set = [];
                            set.Add(number);
                        }
                    }
                    else if (PcCastActions.Contains(clause.Type))
                    {
                        // The spell number is the literal operand that is a spell (0..222); record every literal operand in range that is in a poison set.
                        for (var slot = 0; slot < clause.Kinds.Length; slot++)
                        {
                            if (LiteralOf(clause, slot) is not { } value || value is < 0 or >= SpellRecord.Count) continue;
                            if (poisonSet.Contains(value)) { if (!castsPoison.TryGetValue(value, out var s)) castsPoison[value] = s = []; s.Add(number); }
                            if (healSet.Contains(value)) { if (!castsHealPoison.TryGetValue(value, out var s)) castsHealPoison[value] = s = []; s.Add(number); }
                        }
                        allCasts.Add($"a{clause.Type}");
                    }
                }
            }
        }
        // Scripts that name a poison-damage script as a literal operand of a call action (7, 54, 55, 81): the callers of the unattached ones.
        var poisonDamageScripts = damageClauses.Where(p => p.Key.EndsWith(":1", StringComparison.Ordinal)).SelectMany(p => p.Value).ToHashSet();
        var callers = new SortedDictionary<int, SortedSet<int>>();
        foreach (var (number, script) in scripts)
            foreach (var line in script.Lines)
                foreach (var clause in new[] { line.True, line.False })
                    if (clause.Type is 7 or 54 or 55 or 81)
                        for (var slot = 0; slot < Math.Min(clause.Kinds.Length, 3); slot++)
                            if (LiteralOf(clause, slot) is { } target && poisonDamageScripts.Contains(target) && target != number)
                            { if (!callers.TryGetValue(target, out var set)) callers[target] = set = []; set.Add(number); }
        object ScriptSites(IEnumerable<int> numbers)
        {
            var set = numbers.ToHashSet();
            var rows = placedSites.Where(h => set.Contains(h.Script)).ToArray();
            return new
            {
                scripts = set.Count, placed_sites = rows.Length, maps = rows.Where(h => h.Map.Length > 0).Select(h => h.Map).Distinct().Count(),
                all_attachment_kinds_including_prototype_and_aggregate = walk.Hits.Where(h => set.Contains(h.Script)).GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                by_kind = rows.GroupBy(h => h.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                by_point = rows.Where(h => h.Sap >= 0).GroupBy(h => h.Sap).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count()),
                // ARC-SCRIPT-1483: the script numbers placed at each attachment point (numbers only), to name the combat-point and dialog-point sites.
                scripts_by_point = rows.Where(h => h.Sap >= 0).GroupBy(h => h.Sap).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Select(h => h.Script).Distinct().Order().ToArray()),
                top_maps = rows.Where(h => h.Map.Length > 0).GroupBy(h => h.Map).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(6)
                    .Select(g => new { map = g.Key, sites = g.Count() }).ToArray(),
                numbers = set.Order().Take(40).ToArray(),
            };
        }

        static string Ops(WorldClause clause) => string.Join(',', Enumerable.Range(0, Math.Min(clause.Kinds.Length, 4)).Select(i => $"{clause.Kinds[i]}:{clause.Values[i]}"));
        // 5. Native trap scripts 30001..30006 on placed objects and on prototypes, with their counters.
        object TrapRows(string kindFilter)
        {
            var rows = walk.Hits.Where(h => h.Script is >= 30000 and <= 30007 && (kindFilter == "placed" ? h.Kind is not "aggregate" and not "prototype" : h.Kind == kindFilter)).ToArray();
            return rows.GroupBy(h => h.Script).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => new
            {
                sites = g.Count(), maps = g.Where(h => h.Map.Length > 0).Select(h => h.Map).Distinct().Count(), by_sap = g.GroupBy(h => h.Sap).OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), x => x.Count()),
                object_types = g.Where(h => h.Type is not null).GroupBy(h => h.Type!.Value).OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), x => x.Count()),
                counters = g.GroupBy(h => $"0x{h.Counters:X8}").OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Take(12).ToDictionary(x => x.Key, x => x.Count()),
            });
        }
        var trapScriptFlagged = scripts.Where(p => (p.Value.Flags & ScriptTrapFlags) != 0).Select(p => p.Key).Order().ToArray();

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            module = stack.Plan.ModuleName,
            effects_naming_poison = effectsWithPoison,
            spells_with_a_poison_level_effect = spellsWithPoisonLevelEffect,
            spells = new
            {
                records = SpellRecord.Count,
                poison_damage = poisonDamageSpells.Select(SpellRow).ToArray(),
                poison_heal = poisonHealSpells.Select(SpellRow).ToArray(),
                reach_poison_by_cast_chain = poisonSet.Except(poisonDamageSpells.Keys).Order().ToArray(),
            },
            prototypes = new
            {
                count = prototypeFields.Count,
                weapons_with_poison_damage = weaponsPoison,
                npcs_with_poison_damage = npcPoison,
                critters_with_stored_poison_level = storedPoison,
                items_holding_a_poison_spell = itemsPoisonSpell,
                items_holding_a_poison_heal_spell = itemsHealSpell,
            },
            placed_mobiles = new
            {
                npcs_able_to_cast_a_poison_college_spell = casterNpcs, maps_with_such_an_npc_caster = casterNpcMaps.Count,
                npcs_with_poison_damage = placedNpcPoison, maps_with_such_an_npc = mapsWithPoisonNpc.Count, weapons_with_poison_damage = placedWeaponPoison,
                items_holding_a_poison_spell = placedItemPoisonSpell, items_holding_a_poison_heal_spell = placedItemHealSpell, critters_with_stored_poison_level = placedStoredPoison,
            },
            scripts = new
            {
                decoded = scripts.Count,
                damage_actions = damageTypeCount.Snapshot(),
                damage_actions_poison_type = damageClauses.Where(p => p.Key.EndsWith(":1", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => ScriptSites(p.Value)),
                damage_actions_variable_type = damageClauses.Where(p => p.Key.EndsWith(":variable", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => ScriptSites(p.Value)),
                call_actions_naming_a_poison_damage_script = callers.ToDictionary(p => p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Value.ToArray()),
                sites_of_those_callers = ScriptSites(callers.Values.SelectMany(x => x)),
                heal_poison = ScriptSites(healPoison),
                stat_actions_naming_stat_24 = statActionsOn24.ToDictionary(p => p.Key, p => ScriptSites(p.Value)),
                cast_actions = allCasts.Snapshot(),
                casts_of_poison_spells = castsPoison.ToDictionary(p => p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p => ScriptSites(p.Value)),
                casts_of_poison_heal_spells = castsHealPoison.ToDictionary(p => p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p => ScriptSites(p.Value)),
            },
            heal_poison_script_lines = healPoison.ToDictionary(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture), n => scripts[n].Lines.Select(l =>
                $"{l.Line}: c{l.Condition.Type}({Ops(l.Condition)}) T a{l.True.Type}({Ops(l.True)}) F a{l.False.Type}({Ops(l.False)})").ToArray()),
            traps = new { native_on_placed = TrapRows("placed"), native_on_prototypes = TrapRows("prototype"), trap_flagged_script_files = trapScriptFlagged },
            refusals = refusals.Snapshot(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
