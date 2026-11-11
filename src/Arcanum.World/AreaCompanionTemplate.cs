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
/// the T0 frame, flip and alpha wherever it stands. ARC-MAGIC-050: <paramref name="Created"/> marks the template of a critter the session
/// created (<see cref="ForCreated"/>): its "home" is the map it stands on, and a change of map moves the whole object in the session
/// (the original moves an object whole, FM1/FM3), so its placement keeps no away change and no stored home cell.
/// </summary>
public sealed record AreaCompanionTemplate(AreaSpeaker Speaker, AreaCompanionFacts Facts, SessionMapId Home, CellCoord HomeOrigin,
    CellCoord HomeWorld, IReadOnlyList<AreaDrawCommand> Commands, IReadOnlyDictionary<AreaVisualKey, AreaVisualRecord> Visuals, bool Created = false)
{
    /// <summary>The first <see cref="AreaSpeaker.Index"/> of a created critter's speaker (the index only names its grid actor: no map has this many dialog NPCs).</summary>
    public const int CreatedIndexBase = 1_000_000;

    /// <summary>
    /// ARC-MAGIC-050: the body template of the critter the session created, standing on <paramref name="map"/> at the cell of its row, drawn with
    /// <paramref name="body"/> (its stand pose, key = the row's) and <paramref name="visual"/>. It has no dialog script (speaker script 0). <paramref name="number"/>
    /// is the critter's creation number; <paramref name="facts"/> gives the follow facts of the speaker (null when its AI row or fields do not read: no template).
    /// </summary>
    public static AreaCompanionTemplate? ForCreated(int number, AreaObjectRecord row, Func<AreaSpeaker, AreaCompanionFacts?> facts, SessionMapId map, AreaChart chart,
        AreaDrawCommand body, AreaVisualRecord visual)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        if (row.WorldX is not { } x || row.WorldY is not { } y) throw new ArgumentException("AREA_COMPANION_CREATED_CELL_REQUIRED", nameof(row));
        if (body.Identity != row.Key || body.Visual != visual.Key) throw new ArgumentException("AREA_COMPANION_CREATED_BODY_MISMATCH", nameof(body));
        var speaker = new AreaSpeaker(CreatedIndexBase + number, row, 0, x, y);
        if (facts(speaker) is not { } known) return null;
        return new(speaker, known, map, chart.ToChart(new AreaStepCell(x, y)), new(checked((int)x), checked((int)y)), Array.AsReadOnly([body]),
            new ReadOnlyDictionary<AreaVisualKey, AreaVisualRecord>(new Dictionary<AreaVisualKey, AreaVisualRecord> { [visual.Key] = visual }), true);
    }

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
