using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;

namespace Arcanum.World;

/// <summary>What a menu button does in Steamwoven.</summary>
public enum AreaMenuRole
{
    /// <summary>Opens <see cref="AreaMenuButton.Target"/> over the current window (the current one goes on the back stack).</summary>
    Open,
    /// <summary>Back to the previous window (the original's target -2).</summary>
    Back,
    LoadGame,
    LastSave,
    Exit,
    /// <summary>ARC-UI-012: opens the save list's save window (in play).</summary>
    SaveGame,
    /// <summary>ARC-UI-012: asks whether to quit the game (message 5100); yes goes back to the main menu.</summary>
    Quit,
    /// <summary>ARC-UI-012: closes the in-play menu and play goes on.</summary>
    Continue,
    /// <summary>ARC-UI-013: shows <see cref="AreaMenuButton.Page"/> of the options window.</summary>
    OptionsPage,
    /// <summary>ARC-UI-013: the options window's Done: back to the main menu, or from play closes the menu (OP6).</summary>
    OptionsDone,
    /// <summary>ARC-UI-030: "New Character": the host makes the new character and opens its window (<see cref="AreaMainMenu.OpenNewCharacter"/>).</summary>
    NewCharacter,
    /// <summary>ARC-RENDER-1170: "View Intro" (window 5): plays the intro movies (1 and 7, 0x5445F0), then the menu shows again.</summary>
    ViewIntro,
    /// <summary>ARC-RENDER-1171: "Credits" (window 2, 0x544640): the credits slide show (script 999), then the menu shows again.</summary>
    Credits,
    /// <summary>A button the original has whose function Steamwoven does not have yet; hidden (FR-UI-001).</summary>
    Unavailable,
}
