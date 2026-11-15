using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

// The Run page: how a turn is played (A20, G9, A19; A20, A35, G9).
internal sealed partial class RunPage
{
    /// <summary>The scene a playback left at its end, before the rest replaced it: the tests' "skip equals run" reads it.</summary>
    internal Scene? PlayedScene { get; private set; }

    /// <summary>The rows the beats wrote by the playback's end, before the drain wrote the rest.</summary>
    internal string?[]? PlayedRows { get; private set; }

    /// <summary>The panel's values at the playback's end.</summary>
    internal PanelValues? PlayedPanel { get; private set; }

    /// <summary>The banner at the playback's end.</summary>
    internal string? PlayedBanner { get; private set; }

    /// <summary>
    /// Plays a turn the game has already committed and saved (A20): the script built
    /// from the result by the one function (H2), ONE step per beat, each enqueued by
    /// itself (A35: never one Sequence for the turn, so the engine's 300-tick limit is
    /// a beat's and the turn lasts exactly its script's ticks), and the loop at the full
    /// rate. A turn with no beat (a pick, a redraw, a pace, a run left) plays nothing:
    /// the page rests at once on the state.
    /// </summary>
    internal void Play(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var script = TurnScripts.Build(result, SightKnows);
        if (script.Beats.Count == 0)
        {
            Rest();
            return;
        }
        var play = Begin(result, script);
        // The beats' cues go to the view's one door (H96): only a turn played here has a
        // sink; ShowFrozen's still picture never does (H100).
        play.Sound = _view.Sound;
        foreach (var step in BeatStep.Make(play))
            _playback.Enqueue(step);
        _playback.Speed = State.Fast ? 2 : 1;
        _loop.Rate = LoopRate.Full;
        ShowPlay(play);
        Paint();
    }

    /// <summary>
    /// The page at a turn's start: the board as the state before it showed it, but not
    /// your turn (no move marks, and <c>mark/you</c> hidden from the first beat to the
    /// drain, H52); the turn list's rows as the script lists them, none of them written
    /// yet (H28: rows still to act say nothing); the panel as it stood; no offer
    /// and no summary; the context line saying a click skips (H23).
    /// </summary>
    private TurnPlay Begin(TurnResult result, TurnScript script)
    {
        var before = result.Before;
        var stats = Stats.Of(before);
        _scene = Scene.Of(before, stats, yourTurn: false, hoverCell: -1);
        Face(_scene);
        _scene.ShowYourMark = false;
        _scene.AmbientFrame = 0;
        var play = new TurnPlay(result, script, _scene, _art);
        _play = play;
        // A new turn owes no Reward of the last one, and no skip has cued it (H99).
        _rewardAtDrain = false;
        _skipCued = false;
        Disarm();

        TurnNumber.Text = HatchlingText.TurnNumber(result.After.Turn);
        ShowYourDino(before, play.Panel);
        ShowMoves(before, stats, open: true);
        OfferCover.Visibility = System.Windows.Visibility.Collapsed;
        _pressedCard = NoCard;   // the cards are gone while a turn plays (H79)
        SummaryCover.Visibility = System.Windows.Visibility.Collapsed;
        Context.Text = HatchlingText.Skip;
        return play;
    }

    /// <summary>What the beats have set so far, on screen: the banner, the rows and their marker, the panel's values.</summary>
    private void ShowPlay(TurnPlay play)
    {
        Banner.Text = play.Banner;
        ShowRows(play.Script.Rows, play.RowTexts, play.Marker, playing: true);
        ShowYourDino(play.Result.Before, play.Panel);
    }

    /// <summary>
    /// <c>Playback.Drained</c>: the turn has been watched (or skipped). The picture the
    /// beats left is kept for the tests; each creature's facing carried over; then the
    /// page rests on the state as saved: the scene from the state (<c>Scene.Of</c>),
    /// every row from the script's rows (those with no beat are written here), the
    /// banner, the offer or the summary if the state has one, the panel, and the loop
    /// at the quiet cue or off.
    /// </summary>
    internal void Drained()
    {
        if (_play is not { } play) return;
        PlayedScene = play.Scene;
        PlayedRows = (string?[])play.RowTexts.Clone();
        PlayedPanel = play.Panel;
        PlayedBanner = play.Banner;
        foreach (var sprite in play.Scene.Sprites)
            if (sprite.Opacity > 0) _facing[sprite.Creature] = sprite.Flip;
        // H99: the summary this view's own move brought plays Reward when that move
        // earned an achievement, unless a skip's cue already played at this click (one
        // cue a click). Decided at the move (_rewardAtDrain), so a reading that earned
        // something meanwhile is never taken for the run's (A85).
        var reward = _rewardAtDrain && !_skipCued && play.Sound is not null && State.Run?.Over is not null;
        _rewardAtDrain = false;
        _skipCued = false;
        _play = null;
        Rest();
        if (reward) _view.Sound(Cue.Reward);
    }

    /// <summary>The move whose turn plays earned an achievement (H99): its summary, once the playback has drained, plays <see cref="Cue.Reward"/>.</summary>
    private bool _rewardAtDrain;

    /// <summary>The skip that drains the playback played its one cue (H98): the summary it opens adds none (H99).</summary>
    private bool _skipCued;

    /// <summary>A press on a tab during a playback (H5): only a skip.</summary>
    internal void SkipPlayback() => Skip();

    /// <summary>
    /// A skip (H5): the playback finished at once; its Drained rests the page. The press
    /// that asked for it is spent. Before it, at most one cue (H98): among the beats not
    /// yet begun, whose cues have not played, the run's end, else the apex, else a stage
    /// (<see cref="BeatSounds.OnSkip"/>), through the play's sink; the beats it finishes
    /// sound nothing (H96), and the summary it opens nothing more (H99).
    /// </summary>
    private void Skip()
    {
        if (!_playback.Idle)
        {
            if (_play is { Sound: { } sink } play && BeatSounds.OnSkip(play.Script.Beats, play.LastBegun + 1) is { } cue)
            {
                _skipCued = true;
                sink(cue);
            }
            _playback.SkipAll();
        }
        // A play that had no queue left (a layout state's frozen turn) rests here.
        if (_play is not null && _playback.Idle) Drained();
    }

    /// <summary>A view going to a layout state: whatever is queued is dropped, as a closing window drops it (A20: nothing was pending); no Reward is owed for it (H100).</summary>
    private void DropPlayback()
    {
        _rewardAtDrain = false;
        if (!_playback.Idle) _playback.Clear();
        _play = null;
    }
}
