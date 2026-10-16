using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Area;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-023 research only: who holds which item at the Crash Site at T0 (the
// normal-game aggregate before any tick), how the display-name chain resolves, and
// which DLG IN/$$ operands the core dialog NPCs reference. Independently authored
// from the pinned CE locators cited in docs/research/crash-site-item-inventory.md;
// no CE code or structure is reused. No inventory runtime, no transfer, no script
// VM. Only numbers, booleans and SHA-256 digests leave the probe; MES/DLG text
// stays in memory.
internal static partial class Program
{
    // Effective-field slots: common block, then the type's first and second group.
    private const int InvFlagsSlot = 18, CritterHpDamageSlot = 28, InvNameSlot = 21, InvDescriptionSlot = 22, InvScriptsSlot = 31;
    private const int ContainerFlagsSlot = 36, ContainerKeySlot = 38, ContainerCountSlot = 39, ContainerListSlot = 40, ContainerSourceSlot = 41;
    private const int CritterGoldSlot = 50, CritterCountSlot = 55, CritterListSlot = 56, CritterSourceSlot = 57;
    private const int NpcLeaderSlot = 70, NpcSubstituteSlot = 83;
    private const int ItemFlagsSlot = 36, ItemParentSlot = 37, ItemWeightSlot = 38, ItemWorthSlot = 40, ItemInvLocationSlot = 43,
        ItemComplexitySlot = 45, ItemUnknownDescriptionSlot = 47, ItemSecondGroupSlot = 59;
    private const uint InventoryFlag = 0x1000, IdentifiedItemFlag = 0x1, NoDropItemFlag = 0x20, NoDecayItemFlag = 0x20000, NoDisplayItemFlag = 0x10;
    private const uint SpawnOnceContainerFlag = 0x200, SpawnIndependentContainerFlag = 0x400, LockedContainerFlag = 0x1,
        NeverLockedContainerFlag = 0x8, BustedContainerFlag = 0x80, DayNightLockContainerFlags = 0x60;
    private const int DialogSap = 9, GoldType = 8, AmmoType = 6, KeyType = 11, ContainerType = 2;
    private const int GameDescriptionBase = 30000, MaxDialogEntryBytes = 512 * 1024;

    internal sealed record InvItem(int Index, string RawId, string Entity, int Type, int Prototype, uint Flags, uint ItemFlags, string? ParentRaw,
        int Name, int Description, int UnknownDescription, int Complexity, int? KeyId);
    internal sealed record InvOwner(int Index, string RawId, int Type, int DeclaredCount, IReadOnlyList<(string Raw, string Entity)> List);
    internal enum InvLookup { NotQueried, Found, Absent, UnresolvedElement }
    internal readonly record struct InvMatch(InvLookup Kind, int ListIndex, InvItem? Item);
    internal readonly record struct InvCode(string Code, int Value, bool Overflow);

    // Name lookup: the owner's list in index order, the first item whose OBJ_F_NAME
    // equals the operand. An element that does not resolve to a record stops the scan
    // as a frontier instead of being skipped.
    internal static InvMatch InvFirstByName(InvOwner owner, IReadOnlyDictionary<string, InvItem> itemsByRaw, int name)
    {
        for (var i = 0; i < owner.List.Count; i++)
        {
            if (!itemsByRaw.TryGetValue(owner.List[i].Raw, out var item) || item.Entity != owner.List[i].Entity) return new(InvLookup.UnresolvedElement, i, null);
            if (item.Name == name) return new(InvLookup.Found, i, item);
        }
        return new(InvLookup.Absent, -1, null);
    }

    // Display-name source per object_examine: keys by key id, unidentified items by
    // their unknown description, everything else by its description. Numbers at or
    // above 30000 live in the module description file.
    internal static (string File, int Number) InvNameSource(InvItem item)
    {
        if (item.Type == KeyType) return ("gamekey", item.KeyId ?? throw new InvalidDataException("ITEM_KEY_ID_MISSING"));
        var unidentified = item.Complexity > 0 && (item.ItemFlags & IdentifiedItemFlag) == 0;
        var number = unidentified ? item.UnknownDescription : item.Description;
        return (number >= GameDescriptionBase ? "gamedesc" : "description", number);
    }

    // DLG condition/action scan: skip to a letter or '$', take two bytes as the code,
    // read a C-style integer (leading space, optional sign, digits) right after it.
    internal static List<InvCode> InvScanCodes(ReadOnlySpan<byte> field)
    {
        var codes = new List<InvCode>(); var i = 0;
        while (i < field.Length)
        {
            while (i < field.Length && !(char.IsAsciiLetter((char)field[i]) || field[i] == (byte)'$')) i++;
            if (i >= field.Length - 1) break;
            var code = Encoding.ASCII.GetString(field.Slice(i, 2)).ToUpperInvariant(); i += 2;
            var j = i; while (j < field.Length && field[j] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\v' or (byte)'\f') j++;
            var negative = false;
            if (j < field.Length && field[j] is (byte)'-' or (byte)'+') { negative = field[j] == (byte)'-'; j++; }
            long value = 0; var overflow = false;
            while (j < field.Length && field[j] is >= (byte)'0' and <= (byte)'9') { value = value * 10 + (field[j] - '0'); if (value > int.MaxValue) { overflow = true; value = int.MaxValue; } j++; }
            codes.Add(new(code, (int)(negative ? -value : value), overflow));
        }
        return codes;
    }

    // Inventory graph at T0: every listed element must resolve to exactly one item
    // record whose parent field names the listing owner and which carries the
    // inventory flag; an inventory-flagged item must be listed exactly once.
    internal static (int Listed, int ParentAgrees, int FlagAgrees, int Unlisted, int MultiplyListed, int UnresolvedElements, int CountMismatches)
        InvReconcile(IReadOnlyList<InvOwner> owners, IReadOnlyList<InvItem> items)
    {
        var byRaw = items.ToDictionary(i => i.RawId, StringComparer.Ordinal);
        var listedBy = new Dictionary<string, List<InvOwner>>(StringComparer.Ordinal);
        int unresolved = 0, countMismatch = 0;
        foreach (var owner in owners)
        {
            if (owner.DeclaredCount != owner.List.Count) countMismatch++;
            foreach (var (raw, entity) in owner.List)
            {
                if (!byRaw.TryGetValue(raw, out var item) || item.Entity != entity) { unresolved++; continue; }
                if (!listedBy.TryGetValue(raw, out var list)) listedBy[raw] = list = [];
                list.Add(owner);
            }
        }
        int listed = 0, parent = 0, flag = 0, unlisted = 0, multiple = 0;
        foreach (var item in items)
        {
            if (!listedBy.TryGetValue(item.RawId, out var by)) { if ((item.Flags & InventoryFlag) != 0) unlisted++; continue; }
            listed++;
            if (by.Count > 1) multiple++;
            if (by.Count == 1 && item.ParentRaw == by[0].RawId) parent++;
            if ((item.Flags & InventoryFlag) != 0) flag++;
        }
        return (listed, parent, flag, unlisted, multiple, unresolved, countMismatch);
    }

    private static int ProbeItemInventory(string[] args)
    {
        const int fixedArgs = 4;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--prototype-root" || args[2] != "--mobile") throw new ArgumentException("ITEM_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("ITEM_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var prototypeRoot = args[1]; var mobilePath = args[3];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("ITEM_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha)) throw new InvalidDataException("ITEM_AGGREGATE_IDENTITY");

        using var owner = AreaOriginalValidator.OpenFiles(archivePaths[0], archivePaths[1], archivePaths[2], mobilePath, prototypeRoot);
        var snapshot = owner.LoadStrict();
        var mobileBytes = File.ReadAllBytes(mobilePath);
        if (AreaHash(mobileBytes) != AreaMobileSha) throw new InvalidDataException("ITEM_AGGREGATE_IDENTITY");

        var options = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = MaxDialogEntryBytes, MaxDecodedEntryBytes = MaxDialogEntryBytes };
        var archives = archivePaths.Select(p => DatArchive.Open(p, options)).ToArray();
        try
        {
            var mobiles = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).ToArray();
            var rawIdOf = new Dictionary<AreaObjectRecord, (string Raw, string Entity)>();
            foreach (var o in mobiles)
            {
                var record = mobileBytes.AsSpan(checked((int)o.Key.Offset), o.Key.Length);
                if (AreaHash(record) != o.RawSha256) throw new InvalidDataException("ITEM_RECORD_IDENTITY");
                var own = record.Slice(28, 24); // version, prototype ID, own ID
                if (AreaHash(own) != o.Key.ObjectIdSha256) throw new InvalidDataException("ITEM_OWN_ID");
                rawIdOf[o] = (AreaHash(own), InvEntity(own));
            }

            var items = new List<(AreaObjectRecord Row, InvItem Item)>();
            foreach (var o in mobiles.Where(o => o.Type is >= 5 and <= 14))
            {
                var keyId = o.Type == KeyType ? (int?)InvNumber(o, ItemSecondGroupSlot) : null;
                var parent = o.EffectiveFields.FirstOrDefault(f => f.Slot == ItemParentSlot);
                items.Add((o, new(o.Key.SourceIndex, rawIdOf[o].Raw, rawIdOf[o].Entity, o.Type, o.PrototypeNumber, (uint)InvNumber(o, InvFlagsSlot), (uint)InvNumber(o, ItemFlagsSlot),
                    parent is { Presence: AreaPresence.Value } ? parent.Fingerprint : null, InvNumber(o, InvNameSlot), InvNumber(o, InvDescriptionSlot),
                    InvNumber(o, ItemUnknownDescriptionSlot), InvNumber(o, ItemComplexitySlot), keyId)));
            }
            var itemsByRaw = items.ToDictionary(i => i.Item.RawId, i => i.Item, StringComparer.Ordinal);
            var rowByRaw = mobiles.ToDictionary(o => rawIdOf[o].Raw, StringComparer.Ordinal);

            var owners = new List<(AreaObjectRecord Row, InvOwner Owner)>();
            foreach (var o in mobiles.Where(o => o.Type is ContainerType or 15 or 16))
            {
                var listSlot = o.Type == ContainerType ? ContainerListSlot : CritterListSlot;
                var countSlot = o.Type == ContainerType ? ContainerCountSlot : CritterCountSlot;
                var list = o.EffectiveFields.FirstOrDefault(f => f.Slot == listSlot);
                var elements = list is { Presence: AreaPresence.Array } ? list.Elements.OrderBy(e => e.Key).Select(e => InvElement(e.Words)).ToArray() : [];
                if (list is { Presence: AreaPresence.Array } && !list.Elements.Select(e => e.Key).Order().SequenceEqual(Enumerable.Range(0, elements.Length)))
                    throw new InvalidDataException("ITEM_LIST_KEYS_NOT_DENSE");
                owners.Add((o, new(o.Key.SourceIndex, rawIdOf[o].Raw, o.Type, InvNumber(o, countSlot), elements)));
            }

            var resources = InvResources(archives);
            var description = resources["mes/description.mes"].Mes; var gamedesc = resources["mes/gamedesc.mes"].Mes;
            var gamekey = resources["mes/gamekey.mes"].Mes; var invensource = resources["rules/invensource.mes"].Hits;
            object NameRow(InvItem item)
            {
                var (file, number) = InvNameSource(item);
                var table = file switch { "gamekey" => gamekey, "gamedesc" => gamedesc, _ => description };
                return new { file, number, present = table?.ContainsKey(number) ?? false, text_sha256 = table is not null && table.TryGetValue(number, out var t) ? AreaHash(Encoding.Latin1.GetBytes(t)) : null };
            }

            var (listed, parentAgrees, flagAgrees, unlisted, multiple, unresolved, countMismatch) = InvReconcile(owners.Select(o => o.Owner).ToArray(), items.Select(i => i.Item).ToArray());
            var listedBy = owners.SelectMany(o => o.Owner.List.Select((e, index) => (e.Raw, Owner: o, index))).ToLookup(x => x.Raw, StringComparer.Ordinal);

            object OwnerRow((AreaObjectRecord Row, InvOwner Owner) pair)
            {
                var (o, inv) = pair;
                var source = InvNumber(o, o.Type == ContainerType ? ContainerSourceSlot : CritterSourceSlot);
                string? Handle(int slot) => o.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value } h ? h.Fingerprint : null;
                object HandleRow(int slot) { var h = Handle(slot); return new { present = h is not null, resolved_source_index = h is not null && rowByRaw.TryGetValue(h, out var r) ? r.Key.SourceIndex : (int?)null, resolved_type = h is not null && rowByRaw.TryGetValue(h, out var t) ? t.Type : (int?)null }; }
                return new
                {
                    source_index = o.Key.SourceIndex, type = o.Type, prototype = o.PrototypeNumber, scope = o.Scope.ToString(), world_x = o.WorldX, world_y = o.WorldY,
                    description = InvNumber(o, InvDescriptionSlot), flags = $"0x{(uint)InvNumber(o, InvFlagsSlot):X}",
                    hp_damage = o.Type != ContainerType ? InvNumber(o, CritterHpDamageSlot) : (int?)null,
                    declared_count = inv.DeclaredCount,
                    list = inv.List.Select((e, index) => new { index, resolved_source_index = itemsByRaw.TryGetValue(e.Raw, out var it) && it.Entity == e.Entity ? it.Index : (int?)null }).ToArray(),
                    inventory_source = source,
                    inventory_source_entry_by_archive = source == 0 ? null : invensource.Select(h => new { archive = h.Archive, present = h.Mes.ContainsKey(source), text_sha256 = h.Mes.TryGetValue(source, out var t) ? AreaHash(Encoding.Latin1.GetBytes(t)) : null }).ToArray(),
                    container_flags = o.Type == ContainerType ? $"0x{(uint)InvNumber(o, ContainerFlagsSlot):X}" : null,
                    container_spawn_once = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & SpawnOnceContainerFlag) != 0 : (bool?)null,
                    container_spawn_independent = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & SpawnIndependentContainerFlag) != 0 : (bool?)null,
                    container_locked_flag = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & LockedContainerFlag) != 0 : (bool?)null,
                    container_never_locked = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & NeverLockedContainerFlag) != 0 : (bool?)null,
                    container_busted = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & BustedContainerFlag) != 0 : (bool?)null,
                    container_day_night_lock = o.Type == ContainerType ? (InvNumber(o, ContainerFlagsSlot) & DayNightLockContainerFlags) != 0 : (bool?)null,
                    container_key = o.Type == ContainerType ? InvNumber(o, ContainerKeySlot) : (int?)null,
                    critter_gold_handle = o.Type != ContainerType ? HandleRow(CritterGoldSlot) : null,
                    npc_leader = o.Type == 16 ? HandleRow(NpcLeaderSlot) : null,
                    npc_substitute_inventory = o.Type == 16 ? HandleRow(NpcSubstituteSlot) : null,
                    scripts = InvScripts(o),
                };
            }

            object ItemRow((AreaObjectRecord Row, InvItem Item) pair)
            {
                var (o, item) = pair;
                var by = listedBy[item.RawId].ToArray();
                return new
                {
                    source_index = item.Index, type = item.Type, prototype = item.Prototype, scope = o.Scope.ToString(), world_x = o.WorldX, world_y = o.WorldY,
                    disposition = o.Disposition.ToString(), inventory_flag = (item.Flags & InventoryFlag) != 0, flags = $"0x{item.Flags:X}", item_flags = $"0x{item.ItemFlags:X}",
                    no_drop = (item.ItemFlags & NoDropItemFlag) != 0, no_decay = (item.ItemFlags & NoDecayItemFlag) != 0, no_display = (item.ItemFlags & NoDisplayItemFlag) != 0,
                    listed_by = by.Select(x => new { owner_source_index = x.Owner.Owner.Index, owner_type = x.Owner.Owner.Type, list_index = x.index }).ToArray(),
                    parent_present = item.ParentRaw is not null,
                    parent_resolved_source_index = item.ParentRaw is not null && rowByRaw.TryGetValue(item.ParentRaw, out var p) ? p.Key.SourceIndex : (int?)null,
                    inventory_location = InvNumber(o, ItemInvLocationSlot), name = item.Name, description = item.Description, unknown_description = item.UnknownDescription,
                    complexity = item.Complexity, identified = item.Complexity <= 0 || (item.ItemFlags & IdentifiedItemFlag) != 0,
                    weight = InvNumber(o, ItemWeightSlot), worth = InvNumber(o, ItemWorthSlot),
                    quantity = item.Type is GoldType or AmmoType ? InvNumber(o, ItemSecondGroupSlot + 1) : (int?)null, key_id = item.KeyId,
                    display_name = NameRow(item), scripts = InvScripts(o),
                };
            }

            var coreOwners = owners.Where(o => o.Row.Scope is AreaScope.Core).ToArray();
            var relevantItems = items.Where(i => i.Row.Scope is AreaScope.Core || listedBy[i.Item.RawId].Any(x => x.Owner.Row.Scope is AreaScope.Core)).ToArray();

            // Dialog item operands of every core NPC with a SAP_DIALOG attachment.
            var dialogs = new List<object>();
            foreach (var (o, inv) in coreOwners.Where(p => p.Row.Type == 16))
            {
                var scripts = o.EffectiveFields.FirstOrDefault(f => f.Slot == InvScriptsSlot);
                var dialog = scripts is { Presence: AreaPresence.Array } ? scripts.Elements.FirstOrDefault(e => e.Key == DialogSap && e.Words[2] != 0) : null;
                if (dialog is null) continue;
                var script = checked((int)dialog.Words[2]);
                var hits = archives.Select((a, ai) => (a, ai)).SelectMany(x => x.a.Entries.Where(e => InvDialogKey(e) == script).Select(e => (Archive: x.ai, Entry: e))).ToArray();
                if (hits.Length != 1) { dialogs.Add(new { owner_source_index = inv.Index, script, dlg_candidates = hits.Length }); continue; }
                var bytes = archives[hits[0].Archive].ReadEntry(hits[0].Entry);
                var document = DlgReader.Read(bytes);
                var substituteRaw = o.EffectiveFields.FirstOrDefault(f => f.Slot == NpcSubstituteSlot) is { Presence: AreaPresence.Value } s ? s.Fingerprint : null;
                var substitute = substituteRaw is null ? (InvOwner?)null : owners.Select(x => x.Owner).FirstOrDefault(x => x.RawId == substituteRaw);
                var operands = new List<object>(); var gold = 0; var barter = 0;
                foreach (var record in document.Records)
                {
                    // A player reply whose whole text is the "b:" directive opens barter.
                    if (record.Role == DlgRecordRole.Player && Encoding.ASCII.GetString(record.Text.Bytes.ToArray()).Equals("b:", StringComparison.OrdinalIgnoreCase)) barter++;
                    var fields = record.Role == DlgRecordRole.Player ? new[] { ("condition", record.Condition), ("action", record.Action) } : [("action", record.Action)];
                    foreach (var (role, field) in fields)
                        foreach (var code in InvScanCodes(field.Bytes.ToArray()))
                        {
                            if (code.Code == "$$") { gold++; continue; }
                            if (code.Code != "IN") continue;
                            var name = Math.Abs(code.Value);
                            var primary = code.Value < 0 ? InvFirstByName(inv, itemsByRaw, name) : default;
                            string? substituteLookup = null; InvItem? substituteItem = null;
                            if (code.Value < 0 && primary.Kind == InvLookup.Absent)
                            {
                                if (substitute is null) substituteLookup = substituteRaw is null ? "NoSubstitute" : "SubstituteNotAnOwner";
                                else { var fallback = InvFirstByName(substitute, itemsByRaw, name); substituteLookup = fallback.Kind.ToString(); substituteItem = fallback.Item; }
                            }
                            operands.Add(new
                            {
                                record = record.Id, record_role = record.Role.ToString(), field = role, sign = Math.Sign(code.Value), name, overflow = code.Overflow,
                                npc_lookup = code.Value < 0 ? primary.Kind.ToString() : null, npc_list_index = primary.Kind == InvLookup.Found ? primary.ListIndex : (int?)null,
                                npc_item_source_index = primary.Item?.Index, substitute_lookup = substituteLookup, substitute_item_source_index = substituteItem?.Index,
                                area_items_with_name = items.Where(i => i.Item.Name == name).Select(i => new { source_index = i.Item.Index, scope = i.Row.Scope.ToString(), listed_by = listedBy[i.Item.RawId].Select(x => x.Owner.Owner.Index).ToArray() }).ToArray(),
                            });
                        }
                }
                dialogs.Add(new
                {
                    owner_source_index = inv.Index, description = InvNumber(o, InvDescriptionSlot), script, dlg_archive = CaveArchives[hits[0].Archive].Role, dlg_bytes = bytes.Length,
                    dlg_sha256 = AreaHash(bytes), records = document.Records.Count, gold_operations = gold, barter_replies = barter, in_operations = operands,
                });
            }

            var after = archivePaths.Select(InputFingerprint.Create).ToArray();
            if (!before.SequenceEqual(after) || InputFingerprint.Create(mobilePath) != mobileBefore) throw new InvalidDataException("ITEM_INPUT_CHANGED");
            var report = new
            {
                inputs = CaveArchives.Select((a, i) => new { role = a.Role, bytes = before[i].Bytes, sha256 = before[i].Sha256 }).Append(new { role = "mobile", bytes = mobileBefore.Bytes, sha256 = mobileBefore.Sha256 }).ToArray(),
                inputs_unchanged = true,
                resources = resources.Select(r => new { name = r.Key, archives = r.Value.Archives, bytes = r.Value.Bytes, sha256 = r.Value.Sha256, records = r.Value.Hits.Select(h => h.Mes.Count).ToArray() }).ToArray(),
                reconciliation = new { area_items = items.Count, area_owners = owners.Count, listed, parent_agrees = parentAgrees, inventory_flag_agrees = flagAgrees, inventory_flagged_unlisted = unlisted, multiply_listed = multiple, unresolved_elements = unresolved, count_mismatches = countMismatch,
                    ground_core_items = items.Count(i => i.Row.Scope == AreaScope.Core && (i.Item.Flags & InventoryFlag) == 0),
                    inventory_core_items = items.Count(i => i.Row.Scope == AreaScope.Core && (i.Item.Flags & InventoryFlag) != 0) },
                core_owners = coreOwners.Select(OwnerRow).ToArray(),
                core_items = relevantItems.OrderBy(i => i.Item.Index).Select(ItemRow).ToArray(),
                dialogs,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { foreach (var archive in archives) archive.Dispose(); }
    }

    private static object[] InvScripts(AreaObjectRecord o) =>
        o.EffectiveFields.FirstOrDefault(f => f.Slot == InvScriptsSlot) is { Presence: AreaPresence.Array } scripts
            ? scripts.Elements.Where(e => e.Words[2] != 0).Select(e => (object)new { sap = e.Key, script = e.Words[2] }).ToArray() : [];

    private static int InvNumber(AreaObjectRecord o, int slot) =>
        o.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? checked((int)n) : throw new InvalidDataException($"ITEM_FIELD_{slot}_UNRESOLVED");

    // Entity identity of a 24-byte wire ID: the type tag and its 16-byte payload,
    // excluding the six padding bytes.
    private static string InvEntity(ReadOnlySpan<byte> raw)
    {
        var kind = BinaryPrimitives.ReadInt16LittleEndian(raw);
        if (kind is < 0 or > 3) throw new InvalidDataException("ITEM_ID_FORM");
        return $"{kind}:{Convert.ToHexString(raw.Slice(8, 16)).ToLowerInvariant()}";
    }

    private static (string Raw, string Entity) InvElement(IReadOnlyList<long> words)
    {
        if (words.Count != 6) throw new InvalidDataException("ITEM_LIST_ELEMENT");
        var raw = new byte[24];
        for (var i = 0; i < 6; i++) BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * 4), checked((uint)words[i]));
        return (AreaHash(raw), InvEntity(raw));
    }

    private static int? InvDialogKey(DatEntry entry)
    {
        var name = Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()).TrimEnd('\0').Replace('\\', '/').ToLowerInvariant();
        if (!name.StartsWith("dlg/", StringComparison.Ordinal) || !name.EndsWith(".dlg", StringComparison.Ordinal)) return null;
        var digits = name[4..].TakeWhile(char.IsAsciiDigit).ToArray();
        return digits.Length is > 0 and <= 5 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    // Each resource must occur in exactly one of the six archives; a second hit is a
    // precedence question this research does not answer, so the table stays unresolved.
    private static Dictionary<string, (object[] Archives, int? Bytes, string? Sha256, SortedDictionary<int, string>? Mes, (string Archive, SortedDictionary<int, string> Mes)[] Hits)> InvResources(DatArchive[] archives)
    {
        string[] wanted = ["mes/description.mes", "mes/gamedesc.mes", "mes/gamekey.mes", "rules/invensource.mes"];
        var result = new Dictionary<string, (object[], int?, string?, SortedDictionary<int, string>?, (string, SortedDictionary<int, string>)[])>(StringComparer.Ordinal);
        foreach (var name in wanted)
        {
            var hits = archives.Select((a, i) => (a, i)).SelectMany(x => x.a.Entries.Where(e => ClosureEntryName(e) == name).Select(e => (x.i, e))).ToArray();
            var payloads = hits.Select(h => archives[h.i].ReadEntry(h.e)).ToArray();
            var rows = hits.Select((h, k) => (object)new { archive = CaveArchives[h.i].Role, bytes = payloads[k].Length, sha256 = AreaHash(payloads[k]) }).ToArray();
            var tables = hits.Select((h, k) => (CaveArchives[h.i].Role, ClosureMes(payloads[k]))).ToArray();
            result[name] = hits.Length == 1 ? (rows, payloads[0].Length, AreaHash(payloads[0]), tables[0].Item2, tables) : (rows, null, null, null, tables);
        }
        return result;
    }

    private static void ItemInventorySelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("item self-test failed: " + label); }
        static bool Throws(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
        static InvItem Item(int index, string raw, int name, uint flags = InventoryFlag, string? parent = "npc", int type = 14, uint itemFlags = 0, int complexity = 0, int? key = null) =>
            new(index, raw, "e" + raw, type, 1, flags, itemFlags, parent, name, 100 + index, 200 + index, complexity, key);

        // Scanner: codes, signs, case, '$$', separators, missing value and overflow.
        var codes = InvScanCodes("gf 12 1, IN -2027; $$ 50 in5 re-3 xx"u8);
        Check(codes.Select(c => c.Code).SequenceEqual(["GF", "IN", "$$", "IN", "RE", "XX"]), "codes");
        Check(codes[1].Value == -2027 && codes[2].Value == 50 && codes[3].Value == 5 && codes[4].Value == -3 && codes[5].Value == 0, "values");
        Check(InvScanCodes("in 99999999999"u8).Single().Overflow, "overflow");
        Check(InvScanCodes("i"u8).Count == 0 && InvScanCodes(""u8).Count == 0, "short");

        // First match by name in list order; an unresolved element stops the scan.
        var a = Item(1, "a", 7); var b = Item(2, "b", 9); var c = Item(3, "c", 9);
        var byRaw = new[] { a, b, c }.ToDictionary(i => i.RawId);
        var npc = new InvOwner(10, "npc", 16, 3, [("a", "ea"), ("c", "ec"), ("b", "eb")]);
        var match = InvFirstByName(npc, byRaw, 9);
        Check(match.Kind == InvLookup.Found && match.ListIndex == 1 && match.Item == c, "first match is list order, not record order");
        Check(InvFirstByName(npc, byRaw, 8).Kind == InvLookup.Absent, "absent");
        var broken = npc with { List = [("a", "ea"), ("z", "ez"), ("b", "eb")] };
        Check(InvFirstByName(broken, byRaw, 9) is { Kind: InvLookup.UnresolvedElement, ListIndex: 1 }, "unresolved stops");
        Check(InvFirstByName(npc with { List = [("a", "wrong")] }, byRaw, 7).Kind == InvLookup.UnresolvedElement, "entity mismatch");
        Check(default(InvMatch).Kind == InvLookup.NotQueried, "unqueried lookup is not a match");

        // List element words rebuild the same raw ID and entity as the record's own 24 bytes.
        var raw = new byte[24]; raw[0] = 2; raw[3] = 0x7F; for (var k = 8; k < 24; k++) raw[k] = (byte)k;
        var words = Enumerable.Range(0, 6).Select(k => (long)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(k * 4))).ToArray();
        Check(InvElement(words) == (AreaHash(raw), InvEntity(raw)) && InvEntity(raw) == "2:" + Convert.ToHexString(raw, 8, 16).ToLowerInvariant(), "element identity");
        raw[0] = 9; Check(Throws(() => InvEntity(raw)), "unknown ID form refused");

        // Name chain: key id, unidentified unknown description, identified description, file split.
        Check(InvNameSource(Item(1, "k", 0, type: KeyType, key: 5)) == ("gamekey", 5), "key");
        Check(InvNameSource(Item(1, "u", 0, complexity: 10)) == ("description", 201), "unidentified");
        Check(InvNameSource(Item(1, "i", 0, complexity: 10, itemFlags: IdentifiedItemFlag)) == ("description", 101), "identified");
        Check(InvNameSource(new InvItem(1, "g", "eg", 14, 1, 0, 0, null, 0, 30001, 0, 0, null)) == ("gamedesc", 30001), "module file");

        // Reconciliation: parent/flag agreement, an unlisted inventory item, a ground item, double listing, count mismatch.
        var ground = Item(4, "d", 1, flags: 0, parent: null);
        var orphan = Item(5, "o", 1);
        var r = InvReconcile([npc], [a, b, c, ground, orphan]);
        Check(r == (3, 3, 3, 1, 0, 0, 0), "reconcile");
        var twice = InvReconcile([npc, new InvOwner(11, "box", 2, 2, [("a", "ea")])], [a, b, c]);
        Check(twice.MultiplyListed == 1 && twice.ParentAgrees == 2 && twice.CountMismatches == 1, "double listing");
        var unflagged = InvReconcile([npc], [a with { Flags = 0 }, b, c]);
        Check(unflagged.FlagAgrees == 2, "flag");
        Console.WriteLine("item-inventory self-test: PASS (scanner, first match, element identity, name chain, reconciliation)");
    }
}
