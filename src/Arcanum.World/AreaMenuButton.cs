using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;

namespace Arcanum.World;

/// <param name="Message">The label's message in <c>mes\MainMenu.mes</c>; its hotkey letter is message + 10 (ledger MM3).</param>
/// <param name="Target">The window an <see cref="AreaMenuRole.Open"/> button opens.</param>
/// <param name="Unavailable">Why the button is hidden, for the window's note; null when it is shown.</param>
/// <param name="Page">The page an <see cref="AreaMenuRole.OptionsPage"/> button shows.</param>
public sealed record AreaMenuButton(int Message, AreaMenuRole Role, AreaMenuWindow? Target = null, string? Unavailable = null, AreaOptionsPage Page = default)
{
    public bool Shown => Role != AreaMenuRole.Unavailable;
    public int HotkeyMessage => Message + AreaMainMenuTexts.HotkeyOffset;
}
