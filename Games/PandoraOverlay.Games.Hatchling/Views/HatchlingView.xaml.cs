using System.Windows.Controls;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The element Hatchling's view lays out in: the game's bar (the tabs and the wallet)
/// over the page. Layout only; what it shows and what its tabs do are the view's
/// (<see cref="HatchlingView"/>), which owns it.
/// </summary>
internal sealed partial class HatchlingViewRoot : Grid
{
    public HatchlingViewRoot() => InitializeComponent();
}
