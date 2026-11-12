using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arcanum.Input;

/// <summary>ARC-GPAD-1376, concept §0.8: what one evidence line is about. Closed set, so a line can never carry free text.</summary>
public enum EvidenceKind { SessionStart, SessionEnd, TaskStart, TaskEnd, Command, PauseOn, PauseOff }

/// <summary>The semantic command of a <see cref="EvidenceKind.Command"/> line (the control layer's commands, not keys or buttons). A closed
/// list: a name, a path or an item can not be written because there is no field that could hold one. <see cref="None"/> for other kinds.</summary>
public enum EvidenceCommand
{
    None, Walk, Stop, Face, Examine, Attack, Use, Talk, Take, OpenContainer, MoveItem, Equip, Drop,
    OpenWindow, CloseWindow, Page, Select, Confirm, Back, Hotkey, Radial, Target, TurnBasedToggle, EndTurn, QuickSave, QuickLoad, Menu, Other,
}

/// <summary>How the command ended. <see cref="WrongTarget"/> is a command the player undid or redid on another target at once; <see cref="Refused"/>
/// a command the game turned down; <see cref="Cancelled"/> a started pick or radial that was dropped.</summary>
public enum EvidenceOutcome { Ok, Cancelled, Refused, WrongTarget }

/// <summary>The control scheme in force: the original mouse and keyboard alone, or the experimental scheme chosen in Options.</summary>
public enum EvidenceScheme { Original, Direct, Pointer, TouchPad }

/// <summary>One evidence line. <paramref name="Millis"/> is time since the session started (no wall-clock time, no date).
/// <paramref name="Task"/> is the test script's task number 1..8 (§5), 0 for none.</summary>
public readonly record struct EvidenceEvent(long Millis, EvidenceKind Kind, EvidenceCommand Command, EvidenceScheme Scheme, ActiveDevice Device,
    EvidenceOutcome Outcome, int Task = 0)
{
    public const int MaxTask = 99;
}

/// <summary>The text of an evidence line: one JSON object per line with the keys t (ms), k, c, s, d, o and n (task), every value a token or a number
/// of the closed sets above. Nothing else is accepted when reading, so a line from another source is refused instead of passed on.</summary>
public static class EvidenceLogCodec
{
    public const string Magic = "arcanum-evidence-v1";
    public const int MaxLineBytes = 256;

    public static string Token<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 2);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) builder.Append('-');
            builder.Append(char.ToLowerInvariant(name[i]));
        }
        return builder.ToString();
    }

    /// <summary>The line of an event without its newline; an event with a value outside its set is not written (null).</summary>
    public static string? Format(in EvidenceEvent e)
    {
        if (e.Millis < 0 || e.Task is < 0 or > EvidenceEvent.MaxTask || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Command) || !Enum.IsDefined(e.Scheme)
            || !Enum.IsDefined(e.Device) || !Enum.IsDefined(e.Outcome)) return null;
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":{e.Millis},\"k\":\"{Token(e.Kind)}\",\"c\":\"{Token(e.Command)}\",\"s\":\"{Token(e.Scheme)}\",\"d\":\"{Token(e.Device)}\",\"o\":\"{Token(e.Outcome)}\",\"n\":{e.Task}}}");
    }

    /// <summary>The header line of a file: its format and nothing else.</summary>
    public static string Header => $"{{\"format\":\"{Magic}\"}}";

    /// <summary>An event of a line, or null for a header, a blank, an oversized or a malformed line, one with an unknown key or token.</summary>
    public static EvidenceEvent? Parse(string? line)
    {
        if (line is null || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            long? millis = null; int? task = null; EvidenceKind? kind = null; EvidenceCommand? command = null; EvidenceScheme? scheme = null;
            ActiveDevice? device = null; EvidenceOutcome? outcome = null;
            var count = 0;
            foreach (var property in root.EnumerateObject())
            {
                count++;
                switch (property.Name)
                {
                    case "t": millis = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var t) && t >= 0 ? t : null; break;
                    case "n": task = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var n) && n is >= 0 and <= EvidenceEvent.MaxTask ? n : null; break;
                    case "k": kind = Read<EvidenceKind>(property.Value); break;
                    case "c": command = Read<EvidenceCommand>(property.Value); break;
                    case "s": scheme = Read<EvidenceScheme>(property.Value); break;
                    case "d": device = Read<ActiveDevice>(property.Value); break;
                    case "o": outcome = Read<EvidenceOutcome>(property.Value); break;
                    default: return null;
                }
            }
            if (count != 7 || millis is not { } m || task is not { } tk || kind is not { } k || command is not { } c || scheme is not { } s
                || device is not { } d || outcome is not { } o) return null;
            return new EvidenceEvent(m, k, c, s, d, o, tk);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static T? Read<T>(JsonElement element) where T : struct, Enum
    {
        if (element.ValueKind != JsonValueKind.String) return null;
        var text = element.GetString();
        foreach (var candidate in Enum.GetValues<T>())
            if (string.Equals(Token(candidate), text, StringComparison.Ordinal)) return candidate;
        return null;
    }
}

/// <summary>Where lines go: the host's file. Never reached while the recorder is off.</summary>
public interface IEvidenceSink
{
    /// <summary>Appends text (lines each ending in a newline); false when it could not be stored, after which the recorder stops.</summary>
    bool Append(string text);
    void Close();
}

/// <summary>The opt-in recorder (§0.8). Off by default and when off it touches nothing: no sink is opened, no line is built. Turning it on starts a
/// session (a fresh sink from the factory, the header, a session-start line); turning it off ends it. A session is bounded
/// (<see cref="MaximumLines"/>); a full or failed sink stops the session instead of growing or throwing into the game.</summary>
public sealed class EvidenceRecorder
{
    public const int MaximumLines = 100_000;
    private readonly Func<IEvidenceSink?> _open;
    private IEvidenceSink? _sink;
    private double _start;
    private int _lines;

    /// <param name="open">Opens the session's sink; null when the device has no place to write (the recorder then stays off).</param>
    public EvidenceRecorder(Func<IEvidenceSink?> open) => _open = open ?? throw new ArgumentNullException(nameof(open));

    public bool Recording => _sink is not null;
    public int Lines => _lines;
    /// <summary>True when the last session stopped by itself (full or a write failed).</summary>
    public bool Stopped { get; private set; }

    /// <summary>Switches recording; <paramref name="now"/> is the host's clock in seconds, the session's zero.</summary>
    public bool SetEnabled(bool enabled, double now, EvidenceScheme scheme, ActiveDevice device)
    {
        if (enabled == Recording) return Recording == enabled;
        if (!enabled) { Write(Event(now, EvidenceKind.SessionEnd, EvidenceCommand.None, scheme, device, EvidenceOutcome.Ok, 0)); Close(); return true; }
        Stopped = false;
        IEvidenceSink? sink;
        try { sink = _open(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sink = null; }
        if (sink is null) return false;
        if (!sink.Append(EvidenceLogCodec.Header + "\n")) { sink.Close(); return false; }
        _sink = sink; _start = now; _lines = 0;
        Write(Event(now, EvidenceKind.SessionStart, EvidenceCommand.None, scheme, device, EvidenceOutcome.Ok, 0));
        return Recording;
    }

    public void Record(double now, EvidenceKind kind, EvidenceCommand command, EvidenceScheme scheme, ActiveDevice device,
        EvidenceOutcome outcome = EvidenceOutcome.Ok, int task = 0)
    {
        if (_sink is null) return;
        Write(Event(now, kind, command, scheme, device, outcome, task));
    }

    /// <summary>A semantic command and how it ended.</summary>
    public void Command(double now, EvidenceCommand command, EvidenceScheme scheme, ActiveDevice device, EvidenceOutcome outcome = EvidenceOutcome.Ok, int task = 0)
        => Record(now, EvidenceKind.Command, command, scheme, device, outcome, task);

    private EvidenceEvent Event(double now, EvidenceKind kind, EvidenceCommand command, EvidenceScheme scheme, ActiveDevice device,
        EvidenceOutcome outcome, int task)
    {
        var millis = double.IsFinite(now) && now >= _start ? (long)Math.Round((now - _start) * 1000, MidpointRounding.AwayFromZero) : 0;
        return new EvidenceEvent(millis, kind, command, scheme, device, outcome, task);
    }

    private void Write(in EvidenceEvent e)
    {
        if (_sink is null) return;
        if (_lines >= MaximumLines && e.Kind != EvidenceKind.SessionEnd) { Stopped = true; Close(); return; }
        var line = EvidenceLogCodec.Format(e);
        if (line is null) return;
        bool written;
        try { written = _sink.Append(line + "\n"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { written = false; }
        if (!written) { Stopped = true; Close(); return; }
        _lines++;
    }

    private void Close()
    {
        var sink = _sink; _sink = null;
        try { sink?.Close(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
