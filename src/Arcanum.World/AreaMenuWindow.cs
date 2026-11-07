using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;

namespace Arcanum.World;

/// <summary>The original main-menu windows this menu models, by their window type in the original's window table (ledger MM2;
/// ARC-UI-012: the in-play windows 3 and 4 that Escape opens in play, ledger SF1–SF2; ARC-UI-013: the options window 6, OP1; ARC-UI-030:
/// the new-character window 12, NW1; ARC-UI-034: the character editor window 14, CM1).</summary>
public enum AreaMenuWindow { Main = 2, InPlay = 3, InPlayLimited = 4, SinglePlayer = 5, Options = 6, NewGame = 11, NewCharacter = 12, PickCharacter = 13,
    CharacterEditor = 14, Shop = 15 }
