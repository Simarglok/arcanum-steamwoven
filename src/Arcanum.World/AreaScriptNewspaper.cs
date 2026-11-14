using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Data.Written;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1764 (script actions 85 and 86, ledger NP9–NP12): what the script hosts do with the newspaper requests of a run. Queue newspaper (85, 0x4BF140) writes the session's issue queue by the rule of
/// <see cref="GameSessionOwner.QueueNewspaper"/>; float newspaper headline (86) floats the head issue's headline (0x56CCA0 text, punctuated as the vendor's headline line is) over the action's object, through the host's
/// float list (<see cref="AreaScriptFloat"/>). Shared by the dialog host (<see cref="AreaScriptHost"/>) and the use host (<see cref="AreaUseScriptHost"/>).
/// </summary>
internal static class AreaScriptNewspaper
{
    /// <summary>
    /// The module's newspaper headlines (the written texts of <paramref name="content"/>'s stack, the way the vendor's talk loads them), or null when there is no content or no stack. A module without a newspaper file gives
    /// a texts object that answers every issue with an empty headline.
    /// </summary>
    public static WrittenTexts? OpenTexts(ScriptContent? content)
    {
        if (content is null) return null;
        var opened = ContentStack.OpenShared(content.Plan);
        if (opened.Status != ContentStackStatus.Ready) return null;
        using var stack = opened.Stack!;
        return WrittenTexts.Open(stack);
    }

    /// <summary>The headlines of a host, opened the first time a float newspaper headline needs them; a failed open is tried again on the next need, so a transient mount failure does not silence the host for good.</summary>
    public sealed class TextsCache(Func<ScriptContent?> content)
    {
        private WrittenTexts? _texts;

        /// <summary>The module's newspaper headlines, or null when they could not be opened.</summary>
        public WrittenTexts? Get() => _texts ??= OpenTexts(content());
    }

    /// <summary>
    /// NP10: the float of each float newspaper headline request of <paramref name="effects"/>, read before anything applies. The headline is the head issue's (the queue's word 4, which a queue request never changes), so the text does
    /// not depend on the order of the run's requests. An empty headline (no newspaper file, or an issue without a message) floats nothing: the original's display is handed the empty text and draws none (the display is F0).
    /// <paramref name="texts"/> is read only when the run holds a float request.
    /// </summary>
    public static IReadOnlyList<AreaScriptFloat> Floats(GameSessionOwner session, ScrEffects effects, Func<WrittenTexts?> texts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(texts);
        List<AreaScriptFloat>? floats = null;
        string? text = null;
        foreach (var request in effects.Newspapers)
        {
            if (request.Kind != ScrNewspaperKind.FloatHeadline || request.Speaker is not { } speaker) continue;
            text ??= texts() is { } read ? WrittenTexts.PunctuatedHeadline(read.Headline(session.QueryNewspaper().HeadIssue, session.Rules.Written)) : string.Empty;
            if (text.Length > 0) (floats ??= []).Add(new(speaker, text));
        }
        return floats ?? (IReadOnlyList<AreaScriptFloat>)[];
    }

    /// <summary>
    /// NP9: each queue newspaper request in action order. A request the session refuses for a limit (the revision budget) stops the later steps, as a refused call request does; an issue already queued is no change.
    /// </summary>
    public static bool Queue(GameSessionOwner session, ScrEffects effects)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(effects);
        foreach (var request in effects.Newspapers)
            if (request.Kind == ScrNewspaperKind.Queue && session.QueueNewspaper(session.Version, request.Issue, request.Front).Disposition == GameSessionDisposition.Rejected) return false;
        return true;
    }
}
