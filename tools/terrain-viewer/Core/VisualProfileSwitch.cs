using System.Text.Json;
using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The saved visual profile (ARC-RENDER-013 toggle protocol, step 2):
/// <c>{"schema":1,"profile":"Legacy"|"Hd"}</c>. A missing, unreadable or unknown value loads as
/// <see cref="VisualProfile.Legacy"/>; nothing else is stored, and the profile is never part of a
/// session snapshot or quick save.</summary>
public static class VisualProfileCodec
{
    public const int Schema = 1;

    public static VisualProfile DeserializeOrLegacy(string? json)
    {
        if (json is null) return VisualProfile.Legacy;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return VisualProfile.Legacy;
            // Exactly the two properties: an extra or repeated one is an unknown value.
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != 2 || !names.Contains("schema", StringComparer.Ordinal) || !names.Contains("profile", StringComparer.Ordinal))
                return VisualProfile.Legacy;
            var schema = root.GetProperty("schema"); var profile = root.GetProperty("profile");
            if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != Schema
                || profile.ValueKind != JsonValueKind.String) return VisualProfile.Legacy;
            return profile.GetString() switch
            {
                "Hd" => VisualProfile.Hd,
                _ => VisualProfile.Legacy,
            };
        }
        // ArgumentException: text that is not valid UTF-16 (a lone surrogate) is unreadable too.
        catch (Exception error) when (error is JsonException or ArgumentException) { return VisualProfile.Legacy; }
    }

    public static string Serialize(VisualProfile profile) => profile switch
    {
        VisualProfile.Legacy => "{\"schema\":1,\"profile\":\"Legacy\"}",
        VisualProfile.Hd => "{\"schema\":1,\"profile\":\"Hd\"}",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}

/// <summary>What a profile request did.</summary>
public enum VisualRequestOutcome
{
    /// <summary>The request equals the active profile with nothing pending, or equals the profile
    /// already pending: nothing changes and no generation is spent.</summary>
    NoOp,
    /// <summary>A switch to another profile is pending under a new generation.</summary>
    Started,
    /// <summary>A request for the active profile while a switch was pending: the pending switch is
    /// dropped under a new generation and the active profile stays.</summary>
    Cancelled,
}

/// <summary>The timed commit of the ARC-RENDER-013 toggle protocol (steps 1 and 4), Godot-free.
/// One profile is active (it draws); a request for the other one makes it pending under a new
/// generation; the presentation layer prepares the pending profile's textures for the visible set
/// and calls <see cref="TryCommit"/> once per submission, which switches the active profile between
/// two submissions as soon as every visible frame is ready or refused, or once
/// <see cref="CommitWaitMilliseconds"/> have passed since the request (frames still pending then
/// draw Legacy and switch individually). Time is the caller's monotonic millisecond clock.
/// Caller-serialized.</summary>
public sealed class VisualProfileSwitch
{
    /// <summary>The bounded wait of step 4. Tuned in ARC-UI-006: the first bind of the whole Crash
    /// Site exterior view from a 4× pack took about 0.1 s (ARC-RENDER-014), so 0.5 s commits a
    /// complete view on that class of machine and caps the wait on a slower one.</summary>
    public const long DefaultCommitWaitMilliseconds = 500;

    public VisualProfileSwitch(VisualProfile active, long commitWaitMilliseconds = DefaultCommitWaitMilliseconds)
    {
        if (!Enum.IsDefined(active)) throw new ArgumentOutOfRangeException(nameof(active));
        if (commitWaitMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(commitWaitMilliseconds));
        Active = active; CommitWaitMilliseconds = commitWaitMilliseconds;
    }

    public long CommitWaitMilliseconds { get; }
    /// <summary>The profile that draws.</summary>
    public VisualProfile Active { get; private set; }
    /// <summary>The requested profile being prepared, or null.</summary>
    public VisualProfile? Pending { get; private set; }
    /// <summary>Increases with every request that changes something (start, cancel, set).</summary>
    public long Generation { get; private set; }
    /// <summary>When the pending switch was requested (caller clock).</summary>
    public long RequestedAt { get; private set; }
    /// <summary>The profile a new toggle asks for: the other one than pending, else than active.</summary>
    public VisualProfile Toggled => (Pending ?? Active) == VisualProfile.Legacy ? VisualProfile.Hd : VisualProfile.Legacy;

    public VisualRequestOutcome Request(VisualProfile profile, long now)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (Pending is null && profile == Active) return VisualRequestOutcome.NoOp;
        if (Pending == profile) return VisualRequestOutcome.NoOp;
        Generation++;
        if (profile == Active) { Pending = null; return VisualRequestOutcome.Cancelled; }
        Pending = profile; RequestedAt = now;
        return VisualRequestOutcome.Started;
    }

    /// <summary>True when the bounded wait of the pending switch has passed.</summary>
    public bool TimedOut(long now) => Pending is not null && now - RequestedAt >= CommitWaitMilliseconds;

    /// <summary>Commits the pending switch when <paramref name="settled"/> (every visible frame of
    /// the pending profile is ready or refused) or when it timed out. Returns true when the active
    /// profile changed.</summary>
    public bool TryCommit(bool settled, long now)
    {
        if (Pending is not { } pending || !(settled || TimedOut(now))) return false;
        Active = pending; Pending = null;
        return true;
    }

    /// <summary>Sets the active profile at once and drops any pending switch (startup, before the
    /// first submission, and diagnostic harnesses).</summary>
    public void Set(VisualProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (Pending is null && profile == Active) return;
        Generation++; Active = profile; Pending = null;
    }
}

/// <summary>Which discovered HD pack is active (ARC-UI-006; one pack at a time): the first pack
/// that opened and is not rejected, in discovery order (locations in order, packs of a collection
/// in ordinal directory order); else the first one that opened (a rejected pack: every frame
/// <c>PackRejected</c>); else none (every frame <c>NoPack</c>).</summary>
public static class HdActivePack
{
    public static HdDiscoveredPack? Choose(IReadOnlyList<HdDiscoveredPack> found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var opened = found.Where(pack => pack.Result.Status == HdPackOpenStatus.Opened && pack.Result.Pack is not null).ToArray();
        return opened.FirstOrDefault(pack => pack.Result.Pack!.Review != HdReviewStatus.Rejected) ?? opened.FirstOrDefault();
    }
}
