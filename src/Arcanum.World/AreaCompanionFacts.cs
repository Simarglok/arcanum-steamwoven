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
/// Measured follow inputs of one area NPC (ARC-RES-026): the NPC is matched by prototype and dialog script, and
/// <see cref="Thresholds"/>, <see cref="Level"/> and <see cref="Alignment"/> are its T0 values. <see cref="WaitIndex"/> is its wait
/// field (300), the index of its wait timer (ARC-WORLD-045).
/// </summary>
public sealed record AreaCompanionFacts(int Prototype, int ScriptId, FollowThresholds Thresholds, int Level, int Alignment, int WaitIndex);
