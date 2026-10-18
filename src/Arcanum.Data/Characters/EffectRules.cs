using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

/// <summary>The six attribute collections an effect change names, in the source's lookup order.</summary>
public enum EffectAttributeKind { Stat, BasicSkill, TechSkill, Resistance, TechDiscipline, Special }

/// <summary>The operators of an effect change. Multiply and divide (<c>*n</c>, <c>/n</c>) are read as the original reads them, the
/// number after the sign (ledger SH-07 and LM9, owner decision 7 of ARC-WORLD-035).</summary>
public enum EffectChangeOperator { Add, Percent, Minimum, Maximum, Multiply, Divide }

/// <summary>
/// One change of an effect: the collection, the attribute's zero-based index in it (for <see cref="EffectAttributeKind.Stat"/>
/// the source stat index: 0..7 primary, 8..16 derived, 17 level … 22 magick points, 23 tech points, 25 age, 26 gender,
/// 27 race), the operator and its value.
/// </summary>
public readonly record struct EffectChange(EffectAttributeKind Kind, int Attribute, EffectChangeOperator Operator, int Value);

public enum EffectRuleStatus
{
    Parsed,
    /// <summary>A token that names no attribute of any collection, or an operator token that is none of the admitted forms.</summary>
    InvalidToken,
    /// <summary>An attribute without an operator, or <c>min</c>/<c>max</c> without a value.</summary>
    MissingOperand,
    /// <summary><c>/0</c>: the original would divide by zero when the effect applies, so both rule modes refuse it (owner
    /// decision: the Original mode reproduces no crash). No installed entry has one.</summary>
    ZeroDivisor,
    /// <summary>More than seven changes in one collection, which would overrun the source's per-effect arrays.</summary>
    TooManyChanges,
    /// <summary>A byte outside printable ASCII (space included). The source splits only on spaces and commas, so a tab or line
    /// feed would glue two tokens together; such an entry is refused instead of read partially.</summary>
    InvalidText,
    /// <summary>An effect number outside 50..399, the range <c>rules\effect.mes</c> fills (0..49 are the module's).</summary>
    OutOfRange,
}

/// <summary>An effect rule. <see cref="Defined"/> is false when the table has no entry for the number: the source then
/// keeps the zero-initialised effect, so the rule has no changes.</summary>
public sealed record EffectRule(int Number, bool Defined, IReadOnlyList<EffectChange> Changes);

public sealed record EffectRuleRead(EffectRuleStatus Status, EffectRule? Rule);

/// <summary>
/// <c>rules\effect.mes</c> entries (ARC-RES-027; CE <c>effect_parse</c> at the pinned revision): tokens separated by
/// spaces and commas; an attribute keyword (ASCII case-insensitive, first collection that has it wins) followed by its
/// operator: <c>+n</c>/<c>-n</c> add, the same with a trailing <c>%</c> percent, <c>min n</c>, <c>max n</c>, <c>*n</c> multiply,
/// <c>/n</c> divide (n with an optional sign). Numbers are 1..9 digits. Project admission policy, stricter than the source's scanner: anything else is a typed refusal instead
/// of the source's partial effect.
/// </summary>
public static class EffectRules
{
    public const int FirstNumber = 50, LastNumber = 399, MaxChangesPerCollection = 7;
    public const int RaceEffectBase = 64, FemaleEffect = 330;

    private static readonly string[][] Tokens =
    [
        ["st", "dx", "cn", "be", "in", "pe", "wp", "ch", "carry", "damage", "ac", "speed", "healrate", "poisonrate",
            "beautyreaction", "maxfollowers", "aptitude", "level", "xpsunused", "alignment", "fateunused", "unspentunused",
            "magicpts", "techpts", "poisonlevel", "age", "gender", "race"],
        ["bow", "dodge", "melee", "throwing", "backstab", "pickpocket", "prowling", "spottrap", "gambling", "haggle", "heal", "persuasion"],
        ["repair", "firearms", "picklock", "armtrap"],
        ["resistdamage", "resistfire", "resistelectrical", "resistpoison", "resistmagic"],
        ["expertiseanatomical", "expertisechemistry", "expertiseelectric", "expertiseexplosives", "expertisegun_smithy",
            "expertisemechanical", "expertisesmithy", "expertisetherapeutics"],
        ["maxhps", "maxfatigue", "reaction", "crithitchance", "crithiteffect", "critfailchance", "critfaileffect",
            "badreactionadj", "goodreactionadj", "xpgain"],
    ];

    /// <summary>Reads effect <paramref name="number"/> from the loaded <c>rules\effect.mes</c> table.</summary>
    public static EffectRuleRead Read(MesFile effects, int number)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (number is < FirstNumber or > LastNumber) return new(EffectRuleStatus.OutOfRange, null);
        if (effects.Find(number) is not { } message) return new(EffectRuleStatus.Parsed, new(number, false, []));
        var status = Parse(message.Bytes.Span, out var changes);
        return status == EffectRuleStatus.Parsed ? new(status, new(number, true, changes)) : new(status, null);
    }

    /// <summary>
    /// Reads effect <paramref name="number"/> as the source's tables hold it after the module is loaded (ARC-WORLD-035): 0..49
    /// from the module's <c>rules\gameeffect.mes</c> (<paramref name="module"/>; null when the module has none), 50..399 from
    /// <paramref name="effects"/>. A number with no entry has no changes, as in the source.
    /// </summary>
    public static EffectRuleRead ReadAny(MesFile effects, MesFile? module, int number)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (number is < 0 or > LastNumber) return new(EffectRuleStatus.OutOfRange, null);
        if (number >= FirstNumber) return Read(effects, number);
        if (module?.Find(number) is not { } message) return new(EffectRuleStatus.Parsed, new(number, false, []));
        var status = Parse(message.Bytes.Span, out var changes);
        return status == EffectRuleStatus.Parsed ? new(status, new(number, true, changes)) : new(status, null);
    }

    /// <summary>Parses one entry's text (the bytes of a MES message).</summary>
    public static EffectRuleStatus Parse(ReadOnlySpan<byte> text, out IReadOnlyList<EffectChange> changes)
    {
        changes = [];
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e) return EffectRuleStatus.InvalidText;
        var tokens = System.Text.Encoding.ASCII.GetString(text).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var list = new List<EffectChange>();
        var counts = new int[Tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!Lookup(tokens[i], out var kind, out var attribute)) return EffectRuleStatus.InvalidToken;
            if (++i >= tokens.Length) return EffectRuleStatus.MissingOperand;
            var op = tokens[i];
            EffectChangeOperator @operator;
            int value;
            if (op.Equals("min", StringComparison.OrdinalIgnoreCase) || op.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= tokens.Length) return EffectRuleStatus.MissingOperand;
                if (!Number(tokens[i], signRequired: false, out value)) return EffectRuleStatus.InvalidToken;
                @operator = char.ToLowerInvariant(op[1]) == 'i' ? EffectChangeOperator.Minimum : EffectChangeOperator.Maximum;
            }
            else if (op[0] is '*' or '/')
            {
                if (!Number(op[1..], signRequired: false, out value)) return EffectRuleStatus.InvalidToken;
                if (op[0] == '/' && value == 0) return EffectRuleStatus.ZeroDivisor;
                @operator = op[0] == '*' ? EffectChangeOperator.Multiply : EffectChangeOperator.Divide;
            }
            else if (op[0] is '+' or '-')
            {
                var percent = op[^1] == '%';
                if (!Number(percent ? op[..^1] : op, signRequired: true, out value)) return EffectRuleStatus.InvalidToken;
                @operator = percent ? EffectChangeOperator.Percent : EffectChangeOperator.Add;
            }
            else return EffectRuleStatus.InvalidToken;
            if (++counts[(int)kind] > MaxChangesPerCollection) return EffectRuleStatus.TooManyChanges;
            list.Add(new(kind, attribute, @operator, value));
        }
        changes = list.AsReadOnly();
        return EffectRuleStatus.Parsed;
    }

    private static bool Lookup(string token, out EffectAttributeKind kind, out int attribute)
    {
        for (var collection = 0; collection < Tokens.Length; collection++)
        {
            var index = Array.FindIndex(Tokens[collection], name => name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            (kind, attribute) = ((EffectAttributeKind)collection, index);
            return true;
        }
        (kind, attribute) = (default, -1);
        return false;
    }

    // An optional (or required) sign and 1..9 ASCII digits; never a partial atoi.
    internal static bool Number(string token, bool signRequired, out int value)
    {
        value = 0;
        var digits = token.Length > 0 && token[0] is '+' or '-' ? token[1..] : token;
        if (signRequired && digits.Length == token.Length) return false;
        if (digits.Length is < 1 or > 9 || digits.Any(c => c is < '0' or > '9')) return false;
        value = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) * (token[0] == '-' ? -1 : 1);
        return true;
    }
}
