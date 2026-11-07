using System.Globalization;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>One discipline of the tech page: its stored degree, the degree the intelligence lets count, the effective expertise
/// (<c>0x4B00B0</c>, TD9) and the degree the editor's floor holds (the − button is shown only above it).</summary>
public sealed record AreaTechRow(int Discipline, int Degree, int CountingDegree, int Expertise, int FloorDegree);
