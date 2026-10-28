using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

public enum AreaLevelTextsStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed }

/// <summary>
/// The texts a level-up message reads (ARC-UI-046), from the player's installation at run time: <c>mes\intgame.mes</c> (title
/// 21, level 11, points 12), <c>mes\level.mes</c> (scheme texts 1–4, pool names 5 and 6), and the purchase names of
/// <c>mes\stat.mes</c>, <c>mes\skill.mes</c>, <c>mes\tech.mes</c> and <c>mes\spell.mes</c>, and (ARC-WORLD-087) the encumbrance
/// messages of <c>mes\critter.mes</c> (lines 11..17). A file that is missing or does
/// not parse is left out (its lines fall back to the project's labels); <see cref="Missing"/> names them.
/// </summary>
public sealed class AreaLevelTexts
{
    public const string InterfacePath = "mes/intgame.mes", LevelPath = "mes/level.mes", StatPath = "mes/stat.mes", SkillPath = "mes/skill.mes",
        TechPath = "mes/tech.mes", SpellPath = "mes/spell.mes", CritterPath = "mes/critter.mes";
    private static readonly string[] Paths = [InterfacePath, LevelPath, StatPath, SkillPath, TechPath, SpellPath, CritterPath];
    private readonly IReadOnlyDictionary<string, MesFile> _files;

    private AreaLevelTexts(IReadOnlyDictionary<string, MesFile> files) => _files = files;

    /// <summary>No texts: every line is the project's label.</summary>
    public static AreaLevelTexts None { get; } = new(new Dictionary<string, MesFile>());

    /// <summary>Texts from already-parsed files keyed by their path (synthetic data).</summary>
    public static AreaLevelTexts Create(IReadOnlyDictionary<string, MesFile> files) => new(new Dictionary<string, MesFile>(files ?? throw new ArgumentNullException(nameof(files))));

    /// <summary>The files that are not loaded.</summary>
    public IReadOnlyList<string> Missing => Paths.Where(path => !_files.ContainsKey(path)).ToArray();

    /// <summary>A message's text, or null when its file or message is missing or its bytes do not decode.</summary>
    public string? Text(string path, int number)
    {
        if (!_files.TryGetValue(path, out var file) || file.Find(number) is not { } message) return null;
        var decoded = MesText.Decode(message, AreaHoverIndex.TextProfile);
        return decoded.Status == MesDecodeStatus.Decoded ? decoded.Text : null;
    }

    /// <summary>Loads the seven files through the module's game folder (the rule of the description tables,
    /// <see cref="AreaContentRoot.Discover"/>), each from the base sources of the game-start phase.</summary>
    public static (AreaLevelTextsStatus Status, AreaLevelTexts Texts, string Detail) Load(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return (found.Status == AreaContentRootStatus.MountFailed ? AreaLevelTextsStatus.MountFailed : AreaLevelTextsStatus.ModuleOutsideGameRoot, None,
                found.Detail ?? found.Status.ToString());
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return (AreaLevelTextsStatus.StackFailed, None, opened.Status.ToString());
        using var stack = opened.Stack!;
        var options = new MesReaderOptions();
        var files = new Dictionary<string, MesFile>(StringComparer.Ordinal);
        foreach (var path in Paths)
        {
            if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source || source.Length > options.MaxBytes) continue;
            var read = stack.Read(source);
            if (read.Status != ContentReadStatus.Read) continue;
            var parsed = MesFile.Parse(read.Bytes!, options);
            if (parsed.Status == MesParseStatus.Parsed) files[path] = parsed.File!;
        }
        var texts = new AreaLevelTexts(files);
        return (AreaLevelTextsStatus.Ready, texts, texts.Missing.Count == 0 ? "level texts loaded" : "missing " + string.Join(", ", texts.Missing));
    }
}

/// <summary>One entry of the message window: its title and its lines.</summary>
public sealed record AreaMessageEntry(string Title, string Line, string Detail);

/// <summary>
/// The level-up message as the original draws it (ARC-UI-046; checked in the 1.0.7.4 executable, ledger OL27–OL28): the title
/// (<c>intgame.mes</c> 21), message 11 formatted with the new level, then message 12 formatted with the points, or the scheme
/// text built by 0x4A7030: <c>level.mes</c> 2, then message 4 for a scheme without a rule, message 3 when nothing was bought, or
/// each purchase as its name, a space and its logged value (a tech degree: a space and the degree's name), joined by ", " with
/// the last separator turned into ". "; a space and message 1 follow when the rule completed. Purchases are logged by name text:
/// a repeated name only updates its value, and at most ten names are kept (0x4A7B90).
/// </summary>
public static class AreaLevelMessages
{
    public const int TitleMessage = 21, LevelMessage = 11, PointsMessage = 12;
    public const int CompletedMessage = 1, SchemeMessage = 2, NothingMessage = 3, NoRuleMessage = 4, HitPointsMessage = 5, FatigueMessage = 6;
    public const int MaxLoggedNames = 10, FirstTechSkillName = 12, FirstDegreeName = 8, SpellsPerCollege = 5;

    /// <summary>The first <c>mes\critter.mes</c> line of the encumbrance messages (line 11 + level, ARC-WORLD-087).</summary>
    public const int EncumbranceMessage = Arcanum.Simulation.Items.CarryRules.MessageLine;

    /// <summary>
    /// ARC-WORLD-087: the message of a change of the hero's encumbrance level (the recalculation 0x45F820, ledger WE5): line
    /// <c>11 + level</c> of <c>mes\critter.mes</c>, which the original shows to the local PC as a one-line message; the project's label
    /// when the text is missing.
    /// </summary>
    public static AreaMessageEntry Encumbrance(int level, AreaLevelTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return new(texts.Text(AreaLevelTexts.CritterPath, EncumbranceMessage + level) ?? $"Encumbrance level {level}", "", "");
    }

    public static AreaMessageEntry Compose(SessionLevelUp levelUp, AreaLevelTexts texts)
    {
        ArgumentNullException.ThrowIfNull(levelUp);
        ArgumentNullException.ThrowIfNull(texts);
        var title = texts.Text(AreaLevelTexts.InterfacePath, TitleMessage) ?? "Level up";
        var line = Format(texts.Text(AreaLevelTexts.InterfacePath, LevelMessage) ?? "Level %d", levelUp.Level);
        var detail = levelUp.Scheme is { } scheme ? SchemeText(scheme, texts)
            : Format(texts.Text(AreaLevelTexts.InterfacePath, PointsMessage) ?? "Unspent points: %d", levelUp.Points);
        return new(title, line, detail);
    }

    /// <summary>The scheme's text (0x4A7030) with the purchases logged as 0x4A7B90 logs them.</summary>
    public static string SchemeText(SessionLevelUpScheme scheme, AreaLevelTexts texts)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(texts);
        string Level(int number, string label) => texts.Text(AreaLevelTexts.LevelPath, number) ?? label;
        var text = new StringBuilder(Level(SchemeMessage, "Auto-level: "));
        if (!scheme.HasRule) return text.Append(Level(NoRuleMessage, "no scheme rule")).ToString();
        var log = new List<(string Name, int Value)>();
        foreach (var purchase in scheme.Purchases)
        {
            var (name, value) = Logged(purchase, texts);
            var at = log.FindIndex(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
            if (at >= 0) log[at] = (name, value);
            else if (log.Count < MaxLoggedNames) log.Add((name, value));
        }
        if (log.Count == 0) text.Append(Level(NothingMessage, "nothing bought"));
        else
        {
            foreach (var (name, value) in log)
            {
                text.Append(name);
                if (value > 0) text.Append(' ').Append(value);
                else if (value < 0) text.Append(' ').Append(texts.Text(AreaLevelTexts.TechPath, FirstDegreeName - value) ?? $"degree {-value}");
                text.Append(", ");
            }
            text[^2] = '.';
        }
        if (scheme.Completed) text.Append(' ').Append(Level(CompletedMessage, "(scheme complete)"));
        return text.ToString();
    }

    /// <summary>A purchase's logged name and value: a stat's name and effective value, a skill's name and points (tech skills
    /// from message 12 on), the bought spell's name and the college's level, a discipline's name and its degree logged negative
    /// (shown as the degree's name), the pool's name (<c>level.mes</c> 5 or 6) and its new maximum.</summary>
    public static (string Name, int Value) Logged(AdvancementPurchase purchase, AreaLevelTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return purchase.Track switch
        {
            AdvancementTrack.Stat => (texts.Text(AreaLevelTexts.StatPath, purchase.Index) ?? $"stat {purchase.Index}", purchase.Value),
            AdvancementTrack.BasicSkill => (texts.Text(AreaLevelTexts.SkillPath, purchase.Index) ?? $"skill {purchase.Index}", purchase.Value),
            AdvancementTrack.TechSkill => (texts.Text(AreaLevelTexts.SkillPath, FirstTechSkillName + purchase.Index) ?? $"tech skill {purchase.Index}", purchase.Value),
            AdvancementTrack.College => (texts.Text(AreaLevelTexts.SpellPath, purchase.Index * SpellsPerCollege + purchase.Value - 1)
                ?? $"college {purchase.Index} spell {purchase.Value}", purchase.Value),
            AdvancementTrack.TechDiscipline => (texts.Text(AreaLevelTexts.TechPath, purchase.Index) ?? $"discipline {purchase.Index}", -purchase.Value),
            AdvancementTrack.HitPoints => (texts.Text(AreaLevelTexts.LevelPath, HitPointsMessage) ?? "hit points", purchase.Value),
            AdvancementTrack.Fatigue => (texts.Text(AreaLevelTexts.LevelPath, FatigueMessage) ?? "fatigue", purchase.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(purchase)),
        };
    }

    /// <summary>The original formats messages 11 and 12 with one number. Here the first <c>%d</c> or <c>%i</c> takes it and
    /// <c>%%</c> is a percent sign; any other conversion is kept as text (the installed messages have only the one number).</summary>
    public static string Format(string format, int value)
    {
        ArgumentNullException.ThrowIfNull(format);
        var text = new StringBuilder(format.Length + 8);
        var used = false;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                var next = format[i + 1];
                if (next == '%') { text.Append('%'); i++; continue; }
                if (!used && next is 'd' or 'i') { text.Append(value); used = true; i++; continue; }
            }
            text.Append(format[i]);
        }
        return text.ToString();
    }
}
