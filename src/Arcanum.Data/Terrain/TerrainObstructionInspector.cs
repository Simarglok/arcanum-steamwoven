namespace Arcanum.Data.Terrain;

/// <summary>
/// Bounded component-only terrain obstruction inspection. It never combines components and does
/// not determine whether an actor can occupy or traverse a cell.
/// </summary>
public static class TerrainObstructionInspector
{
    public static TerrainObstructionSectorReport InspectSector(DatArchive moduleArchive, string moduleSourceId,
        TerrainSectorSelector selector, TerrainTileNameSource? tileNameSource, TerrainLoaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(moduleArchive);
        ArgumentNullException.ThrowIfNull(selector);
        TerrainObstructionSourceId.Validate(moduleSourceId, nameof(moduleSourceId));
        var limits = options ?? new TerrainLoaderOptions();
        limits.Validate();

        var entry = TerrainLoader.SelectModule(moduleArchive, selector);
        if (entry.DeclaredOutputSize > limits.MaxSectorDecodedEntryBytes)
            throw new TerrainLimitExceededException("The selected sector exceeds the decoded byte limit.");
        return InspectBytes(moduleArchive.ReadEntry(entry), entry.Ordinal, moduleSourceId, selector, tileNameSource, limits);
    }

    /// <summary>ARC-DATA-035: the same inspection of sector bytes already read (a loose file is entry 0 of its source).</summary>
    internal static TerrainObstructionSectorReport InspectBytes(byte[] bytes, int ordinal, string moduleSourceId,
        TerrainSectorSelector selector, TerrainTileNameSource? tileNameSource, TerrainLoaderOptions limits)
    {
        TerrainObstructionSourceId.Validate(moduleSourceId, nameof(moduleSourceId));
        if (bytes.Length > limits.MaxSectorDecodedEntryBytes) throw new TerrainLimitExceededException("The selected sector exceeds the decoded byte limit.");
        var sector = TerrainLoader.ParseSector(bytes, limits);
        var moduleProvenance = new TerrainObstructionProvenance(TerrainObstructionProvenanceKind.SerializedSector, moduleSourceId, ordinal);

        TerrainTileNameRuleTable? table = null;
        TerrainEntryExpectation? nameEntry = null;
        TerrainObstructionComponentResult unavailable;
        if (tileNameSource is null)
        {
            unavailable = TerrainObstructionSectorReport.Unknown("TILE_NAME_SOURCE_ABSENT",
                new TerrainObstructionProvenance(TerrainObstructionProvenanceKind.ProjectPolicy, "NO_TILE_NAME_SOURCE"));
        }
        else
        {
            try
            {
                var selectedMesEntry = TerrainLoader.SelectSelected(tileNameSource.Archive, tileNameSource.MesEntryPath, "tile-name MES", limits.MaxMesDecodedEntryBytes);
                var sourceProvenance = new TerrainObstructionProvenance(TerrainObstructionProvenanceKind.TileNameMes, tileNameSource.SourceId, selectedMesEntry.Ordinal);
                nameEntry = new(selectedMesEntry.Ordinal, selectedMesEntry.Flags, selectedMesEntry.DeclaredOutputSize);
                var names = tileNameSource.Archive.ReadEntry(selectedMesEntry);
                var records = TerrainLoader.ParseMes(names, limits);
                table = TerrainTileNameRuleTable.Create(records, sourceProvenance);
                unavailable = TerrainObstructionSectorReport.Unknown("TILE_NAME_TABLE_UNAVAILABLE", sourceProvenance);
            }
            catch (TerrainSelectionException exception) when (exception.Code is TerrainErrorCode.MissingMesEntry or TerrainErrorCode.AmbiguousMesEntry)
            {
                unavailable = TerrainObstructionSectorReport.Unknown(exception.Code == TerrainErrorCode.MissingMesEntry ? "TILE_NAME_ENTRY_MISSING" : "TILE_NAME_ENTRY_AMBIGUOUS",
                    new TerrainObstructionProvenance(TerrainObstructionProvenanceKind.TileNameMes, tileNameSource.SourceId));
            }
        }
        return new TerrainObstructionSectorReport(selector, sector.Words, sector.BlockMask, table, unavailable, moduleProvenance,
            nameEntry, sector.Framing.TileScriptCount, sector.Framing[TerrainSection.SectorScript].Length,
            sector.Framing.ObjectWindow.TerminalObjectCount);
    }
}

/// <summary>Internal projection of the existing bounded MES parser; it is not a general MES API.</summary>
internal sealed class TerrainTileNameRuleTable
{
    private readonly KeyValuePair<int, string>[][] _categories;
    private readonly TerrainObstructionProvenance _provenance;

    private TerrainTileNameRuleTable(KeyValuePair<int, string>[][] categories, TerrainObstructionProvenance provenance)
    {
        _categories = categories;
        _provenance = provenance;
    }

    internal static TerrainTileNameRuleTable Create(SortedDictionary<int, string> records, TerrainObstructionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(records);
        var categories = new[]
        {
            records.Where(pair => pair.Key is >= 0 and <= 99).ToArray(),
            records.Where(pair => pair.Key is >= 100 and <= 199).ToArray(),
            records.Where(pair => pair.Key is >= 200 and <= 299).ToArray(),
            records.Where(pair => pair.Key is >= 300 and <= 399).ToArray(),
        };
        return new TerrainTileNameRuleTable(categories, provenance);
    }

    internal TerrainObstructionComponentResult Evaluate(uint rawId)
    {
        var outdoor = ((rawId >> 8) & 1) != 0;
        var flippable = ((rawId >> 7) & 1) != 0;
        var category = (outdoor, flippable) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3,
        };
        var firstMaterial = checked((int)((rawId >> 22) & 63));
        if (firstMaterial >= _categories[category].Length)
            return TerrainObstructionSectorReport.Unknown("TILE_NAME_MAPPING_UNRESOLVED", _provenance);
        return Interpret(_categories[category][firstMaterial].Value);
    }

    internal int? Key(uint rawId)
    {
        var category = CategoryIndex(rawId);
        var material = (int)((rawId >> 22) & 63);
        return material < _categories[category].Length ? _categories[category][material].Key : null;
    }

    internal static int CategoryIndex(uint rawId) => (rawId & 0x180) switch
    {
        0x180 => 0, 0x100 => 1, 0x80 => 2, _ => 3
    };

    private TerrainObstructionComponentResult Interpret(string value)
    {
        if (value.Length == 0 || value.Any(c => c is < ' ' or > '~' or '{' or '}'))
            return TerrainObstructionSectorReport.Unknown("TILE_NAME_VALUE_UNSUPPORTED", _provenance);
        var slash = value.IndexOf('/');
        if (slash < 0)
            return value.Length == 3
                ? TerrainObstructionSectorReport.Applicable(TerrainObstructionDisposition.NoBlock, "TILE_NAME_RULE_NO_BLOCK", _provenance)
                : TerrainObstructionSectorReport.Unknown("TILE_NAME_VALUE_UNSUPPORTED", _provenance);
        if (slash == 0 || slash != value.LastIndexOf('/'))
            return TerrainObstructionSectorReport.Unknown("TILE_NAME_VALUE_UNSUPPORTED", _provenance);

        var index = slash + 1;
        var blocked = false;
        while (index < value.Length && value[index] is 's' or 'b' or 'f' or 'i' or 'n' or 'p')
        {
            blocked |= value[index] is 'b' or 'f';
            index++;
        }
        while (index < value.Length && value[index] == ' ') index++;
        if (index < value.Length)
        {
            if (value[index] is '+' or '-') index++;
            var digitsStart = index;
            while (index < value.Length && value[index] is >= '0' and <= '9') index++;
            if (digitsStart == index || index != value.Length)
                return TerrainObstructionSectorReport.Unknown("TILE_NAME_VALUE_UNSUPPORTED", _provenance);
        }
        // A non-flag character before spaces is not a number tail and is deliberately unsupported.
        var suffix = value.AsSpan(slash + 1);
        var firstNonFlag = 0;
        while (firstNonFlag < suffix.Length && suffix[firstNonFlag] is 's' or 'b' or 'f' or 'i' or 'n' or 'p') firstNonFlag++;
        if (firstNonFlag < suffix.Length && suffix[firstNonFlag] != ' ' && suffix[firstNonFlag] is not '+' and not '-')
            return TerrainObstructionSectorReport.Unknown("TILE_NAME_VALUE_UNSUPPORTED", _provenance);
        return TerrainObstructionSectorReport.Applicable(blocked ? TerrainObstructionDisposition.Blocked : TerrainObstructionDisposition.NoBlock,
            blocked ? "TILE_NAME_RULE_BLOCKED" : "TILE_NAME_RULE_NO_BLOCK", _provenance);
    }
}
