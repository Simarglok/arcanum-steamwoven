using System.Globalization;
using System.Text.Json;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Combat;

namespace Arcanum.World;

/// <summary>The options window's three pages, in the original's order (window 6 buttons 4000–4002, ledger OP1).</summary>
public enum AreaOptionsPage { Game = 0, Video = 1, Sound = 2 }
