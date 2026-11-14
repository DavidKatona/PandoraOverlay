namespace PandoraOverlay.Games;

/// <summary>
/// What a game's tile on the grid says, as <see cref="IGame.Tile"/> returns
/// it. The words only: the tile's picture is <see cref="IGame.CreateTileArt"/>,
/// and the title and pitch are the game's own. Achievements, which are the
/// game's own data (G22), show here in <see cref="Detail"/> if anywhere on
/// the grid.
/// </summary>
/// <param name="Status">The status line, shown in its <paramref name="Tone"/>.</param>
/// <param name="Tone">The status line's tone.</param>
/// <param name="Detail">The detail line, if any.</param>
/// <param name="Tag">The tile's tag, if any.</param>
/// <param name="CanReset">The tile offers a small "Reset", armed by a first click; the second calls <see cref="IGame.Reset"/>.</param>
public sealed record GameTile(string Status, GameTileTone Tone, string? Detail, string? Tag, bool CanReset);
