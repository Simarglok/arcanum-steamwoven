using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-017: the home-map T0 data of one roster companion, enough to stand, draw, light and pick it on any loaded
/// map: its speaker row, its T0 body commands with their visuals, and its T0 chart cell on the home map. The body keeps
/// the T0 frame, flip and alpha wherever it stands.
/// </summary>
public sealed record AreaCompanionTemplate(AreaSpeaker Speaker, AreaCompanionFacts Facts, SessionMapId Home, CellCoord HomeOrigin,
    CellCoord HomeWorld, IReadOnlyList<AreaDrawCommand> Commands, IReadOnlyDictionary<AreaVisualKey, AreaVisualRecord> Visuals)
{
    public string Entity => AreaConversationState.EntityId(Speaker.Row);
    public AreaObjectKey Key => Speaker.Row.Key;

    /// <summary>The roster speakers of <paramref name="speakers"/> on home map <paramref name="home"/>, with their body
    /// commands from <paramref name="plan"/> and the visuals those commands draw.</summary>
    /// <param name="facts">ARC-WORLD-037: the follow facts of a speaker that can join (null skips it); by default the roster.</param>
    public static IReadOnlyList<AreaCompanionTemplate> From(IEnumerable<AreaSpeaker> speakers, AreaScenePlan plan,
        IEnumerable<AreaVisualRecord> visuals, AreaChart chart, SessionMapId home, Func<AreaSpeaker, AreaCompanionFacts?>? facts = null)
    {
        facts ??= AreaCompanionRoster.For;
        ArgumentNullException.ThrowIfNull(speakers); ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(visuals); ArgumentNullException.ThrowIfNull(chart);
        var catalog = new Dictionary<AreaVisualKey, AreaVisualRecord>();
        foreach (var visual in visuals) catalog.TryAdd(visual.Key, visual);
        var templates = new List<AreaCompanionTemplate>();
        foreach (var speaker in speakers)
        {
            if (facts(speaker) is not { } known) continue;
            var commands = plan.Commands.Where(command => command.Identity == speaker.Row.Key).ToArray();
            // A speaker the plan does not draw has no body to follow with.
            if (commands.Length == 0) continue;
            var drawn = new Dictionary<AreaVisualKey, AreaVisualRecord>();
            foreach (var command in commands)
                drawn[command.Visual] = catalog.TryGetValue(command.Visual, out var visual) ? visual : throw new InvalidOperationException("AREA_COMPANION_VISUAL_REQUIRED");
            templates.Add(new(speaker, known, home, chart.ToChart(new AreaStepCell(speaker.WorldX, speaker.WorldY)),
                new(checked((int)speaker.WorldX), checked((int)speaker.WorldY)), Array.AsReadOnly(commands), new ReadOnlyDictionary<AreaVisualKey, AreaVisualRecord>(drawn)));
        }
        return templates.AsReadOnly();
    }

    /// <summary>The frame repository of another map with the templates' T0 body frames added from the home map's
    /// (a key already present must be the same decoded frame).</summary>
    public static IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames(IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> map,
        IEnumerable<AreaCompanionTemplate> templates, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> home)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(templates); ArgumentNullException.ThrowIfNull(home);
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>(map);
        foreach (var command in templates.SelectMany(template => template.Commands))
        {
            var frame = home.TryGetValue(command.Frame, out var found) ? found : throw new InvalidOperationException("AREA_COMPANION_FRAME_REQUIRED");
            if (frames.TryGetValue(command.Frame, out var known))
            {
                if (!ReferenceEquals(known, frame) && (known.Width != frame.Width || known.Height != frame.Height || !known.Rgba.AsSpan().SequenceEqual(frame.Rgba)))
                    throw new InvalidOperationException("AREA_COMPANION_FRAME_CONFLICT");
            }
            else frames.Add(command.Frame, frame);
        }
        return new ReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>(frames);
    }
}
