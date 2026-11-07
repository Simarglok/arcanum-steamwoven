using System.Globalization;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>The tech page of the hero: the eight disciplines and whether the editor is open.</summary>
public sealed record AreaTechPageView(IReadOnlyList<AreaTechRow> Rows, bool EditorOpen);
