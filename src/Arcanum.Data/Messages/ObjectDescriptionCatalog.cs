using Arcanum.Data.Content;

namespace Arcanum.Data.Messages;

public enum ObjectDescriptionFile { Description, GameDescription, GameKey }

public enum DescriptionCatalogStatus { Ready, DescriptionMissing, ReadFailed, ParseFailed }

/// <param name="File">The file a failure belongs to.</param>
/// <param name="Parse">The parse refusal, for <see cref="DescriptionCatalogStatus.ParseFailed"/>.</param>
public sealed record DescriptionCatalogLoad(DescriptionCatalogStatus Status, ObjectDescriptionCatalog? Catalog,
    ObjectDescriptionFile? File = null, MesParseResult? Parse = null, string? Detail = null);

public enum DescriptionLookupStatus
{
    Found,
    /// <summary>Negative, or above <see cref="ObjectDescriptionCatalog.MaxNumber"/>.</summary>
    OutOfRange,
    /// <summary>The number belongs to a module file that is not loaded.</summary>
    ModuleFileAbsent,
    NotFound,
}

/// <param name="File">The file the number was looked up in; null when the range check refused it first.</param>
public sealed record DescriptionLookup(DescriptionLookupStatus Status, int Number, ObjectDescriptionFile? File, MesMessage? Message);

/// <summary>The original's description text tables: the required base <c>mes/description.mes</c>,
/// the module's optional <c>mes/gamedesc.mes</c> and <c>mes/gamekey.mes</c>, each the winner of
/// the content stack (docs/formats/mes-reader.md, docs/research/content-precedence.md).</summary>
public sealed class ObjectDescriptionCatalog
{
    /// <summary>Numbers from here up are looked up in the module file.</summary>
    public const int GameDescriptionBase = 30000;
    public const string DescriptionPath = "mes/description.mes", GameDescriptionPath = "mes/gamedesc.mes", GameKeyPath = "mes/gamekey.mes";

    private ObjectDescriptionCatalog(MesFile description, MesFile? gameDescription, MesFile? gameKey,
        ContentCandidate? descriptionSource, ContentCandidate? gameDescriptionSource, ContentCandidate? gameKeySource)
    {
        Description = description; GameDescription = gameDescription; GameKey = gameKey;
        DescriptionSource = descriptionSource; GameDescriptionSource = gameDescriptionSource; GameKeySource = gameKeySource;
        // The highest accepted number comes from the last file loaded with messages: the
        // module file when it has any, else the base file. It is not the maximum of both.
        MaxNumber = gameDescription?.LastNumber ?? description.LastNumber ?? 0;
    }

    public MesFile Description { get; }
    public MesFile? GameDescription { get; }
    public MesFile? GameKey { get; }
    /// <summary>The content-stack candidate each file was read from; null for <see cref="Create"/>.</summary>
    public ContentCandidate? DescriptionSource { get; }
    public ContentCandidate? GameDescriptionSource { get; }
    public ContentCandidate? GameKeySource { get; }
    public int MaxNumber { get; }

    /// <summary>Builds a catalog from already-parsed tables (synthetic data or another loader).</summary>
    public static ObjectDescriptionCatalog Create(MesFile description, MesFile? gameDescription = null, MesFile? gameKey = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new(description, gameDescription, gameKey, null, null, null);
    }

    /// <summary>Reads the three tables from <paramref name="stack"/>. The base file is required, as
    /// the original refuses to start without it; a missing module file is simply not loaded.</summary>
    public static DescriptionCatalogLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var failure = Read(stack, DescriptionPath, ObjectDescriptionFile.Description, options, out var description, out var descriptionSource);
        if (failure is not null) return failure;
        if (description is null) return new(DescriptionCatalogStatus.DescriptionMissing, null, ObjectDescriptionFile.Description);
        failure = Read(stack, GameDescriptionPath, ObjectDescriptionFile.GameDescription, options, out var gameDescription, out var gameDescriptionSource);
        if (failure is not null) return failure;
        failure = Read(stack, GameKeyPath, ObjectDescriptionFile.GameKey, options, out var gameKey, out var gameKeySource);
        if (failure is not null) return failure;
        return new(DescriptionCatalogStatus.Ready, new(description, gameDescription, gameKey, descriptionSource, gameDescriptionSource, gameKeySource));
    }

    /// <summary>Description text by number, as the original's description lookup selects it.</summary>
    public DescriptionLookup Find(int number)
    {
        if (number < 0 || number > MaxNumber) return new(DescriptionLookupStatus.OutOfRange, number, null, null);
        if (number < GameDescriptionBase) return Lookup(Description, number, ObjectDescriptionFile.Description);
        return GameDescription is null
            ? new(DescriptionLookupStatus.ModuleFileAbsent, number, ObjectDescriptionFile.GameDescription, null)
            : Lookup(GameDescription, number, ObjectDescriptionFile.GameDescription);
    }

    /// <summary>Key name by key ID. There is no range check; an unloaded key file is a typed
    /// absence, because the original would search an invalid handle.</summary>
    public DescriptionLookup FindKey(int keyId) => GameKey is null
        ? new(DescriptionLookupStatus.ModuleFileAbsent, keyId, ObjectDescriptionFile.GameKey, null)
        : Lookup(GameKey, keyId, ObjectDescriptionFile.GameKey);

    private static DescriptionLookup Lookup(MesFile file, int number, ObjectDescriptionFile kind) =>
        file.Find(number) is { } message
            ? new(DescriptionLookupStatus.Found, number, kind, message)
            : new(DescriptionLookupStatus.NotFound, number, kind, null);

    private static DescriptionCatalogLoad? Read(ContentStack stack, string path, ObjectDescriptionFile kind, MesReaderOptions options,
        out MesFile? file, out ContentCandidate? source)
    {
        file = null; source = stack.Resolve(path).Winner;
        if (source is null) return null;
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(DescriptionCatalogStatus.ReadFailed, null, kind, Detail: read.Detail);
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(DescriptionCatalogStatus.ParseFailed, null, kind, parsed);
        file = parsed.File;
        return null;
    }
}
