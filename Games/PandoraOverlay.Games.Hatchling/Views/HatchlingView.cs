using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>The game's pages (H27: no scrolling anywhere): Camp or Run by whether a run exists, then the Lineage, the Journal, Records and the Guide (H94).</summary>
internal enum PageKind { Camp, Run, Lineage, Journal, Records, Guide }

/// <summary>
/// One page of the view. <see cref="Show"/> when it becomes the page shown,
/// <see cref="Refresh"/> when the game's state changed while it is shown (a reading
/// paid something), <see cref="Hide"/> when it is left. The Run page plays the run;
/// Camp, the Lineage, the Journal and Records are the pages between runs, built from
/// the state on Show and redrawn in place on Refresh, with nothing ticking on them.
/// </summary>
internal interface IHatchlingPage
{
    FrameworkElement Root { get; }
    void Show();
    void Refresh();
    void Hide();
}

/// <summary>
/// Hatchling on screen (A4): the game's bar (its tabs and its wallet) over one page,
/// in exactly 798 x 510 (G13), built on the engine's view base for the loop,
/// the playback, the guard and the lifecycle (A10, A19, A20).
///
/// <para><b>Where the player left off</b> (G14). The view opens on the first tab, and
/// with a run under way that is the Run page, with the turn list as last shown (H28)
/// and the board painted from the state as saved: a view closed in the middle of a
/// playback lost nothing, because the state was saved before the first beat (A20).</para>
///
/// <para><b>The game's own changes</b> (<c>game.Changed</c>: a reading paid something,
/// or a Reset) refresh the wallet and the page shown, at once at rest and once the
/// playback has drained during one: a page is never redrawn under a turn being
/// watched. The view's own actions never raise it; it refreshes after them itself.</para>
///
/// <para><b>A layout state</b> (A26) is a still picture of a sample
/// built in code (<see cref="Samples"/>), never of the save: no storage is read or
/// written, nothing is enqueued, the loop stays off.</para>
/// </summary>
internal sealed class HatchlingView : GameViewBase
{
    /// <summary>The game's bar's height (G13): the page is the rest.</summary>
    public const double BarHeight = 34;

    /// <summary>The prefix of the Guide's layout states: "guide-" and a section's id (H94).</summary>
    internal const string GuidePrefix = "guide-";

    /// <summary>
    /// Every layout state of the game, in A26's list: the Run page's seven, the ten of
    /// the pages between runs, and one for each section of the Guide (H94), each named
    /// by its page.
    /// </summary>
    internal static readonly IReadOnlyList<string> AllStates = new[]
    {
        "camp-first", "camp-locked", "camp-everything",
        "run-first", "run-mid", "run-playback", "run-apex", "run-offer", "run-lost", "run-won",
        "lineage-fresh", "lineage-mid", "lineage-full",
        "journal-fresh", "journal-busy",
        "records-fresh", "records-full",
    }.Concat(HatchlingText.Guide.Select(s => GuidePrefix + s.Id)).ToArray();

    private readonly HatchlingGame _game;
    private readonly HatchlingViewRoot _root;
    private readonly RunPage _run;
    private readonly Dictionary<PageKind, IHatchlingPage> _pages;
    private readonly Action _onChanged;
    private PageKind _shown;
    private bool _hasShown;
    private bool _refreshOwed;
    private bool _disposed;
    private GameState? _sample;

    public HatchlingView(HatchlingGame game, IGameHost host, ArtBook art) : base(host)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(art);
        _game = game;
        _root = new HatchlingViewRoot();
        _run = new RunPage(this, game, art, Playback, Loop);
        _pages = new Dictionary<PageKind, IHatchlingPage>
        {
            [PageKind.Camp] = new CampPage(this, game, art),
            [PageKind.Run] = _run,
            [PageKind.Lineage] = new LineagePage(this, game),
            [PageKind.Journal] = new JournalPage(this, game),
            [PageKind.Records] = new RecordsPage(this),
            [PageKind.Guide] = new GuidePage(this),
        };

        _root.FirstTab.Click += (_, _) => TabClicked(State.Run is null ? PageKind.Camp : PageKind.Run);
        _root.LineageTab.Click += (_, _) => TabClicked(PageKind.Lineage);
        _root.JournalTab.Click += (_, _) => TabClicked(PageKind.Journal);
        _root.RecordsTab.Click += (_, _) => TabClicked(PageKind.Records);
        _root.GuideTab.Click += (_, _) => TabClicked(PageKind.Guide);

        Playback.Drained += () => Guard(OnDrained);
        _onChanged = () => Guard(OnGameChanged);
        _game.Changed += _onChanged;

        ShowWallet();
        ShowPage(FirstPage);
    }

    /// <inheritdoc />
    public override FrameworkElement Root => _root;

    /// <inheritdoc />
    public override IReadOnlyList<string> LayoutStates => AllStates;

    /// <summary>The state shown: a layout state's sample, else the game's own.</summary>
    internal GameState State => _sample ?? _game.State;

    /// <summary>The page shown.</summary>
    internal PageKind Shown => _shown;

    /// <summary>The Run page, for the tests that call what its events call.</summary>
    internal RunPage RunPage => _run;

    /// <summary>The loop, for the tests that drive it with a manual clock.</summary>
    internal GameLoop TheLoop => Loop;

    /// <summary>The playback, for the tests that skip it at a chosen tick.</summary>
    internal Playback ThePlayback => Playback;

    /// <summary>The wallet as it reads now.</summary>
    internal string WalletShown => _root.Wallet.Text;

    /// <summary>The tabs as they read now, and which is checked.</summary>
    internal (string First, PageKind Checked) TabsShown =>
        ((string)_root.FirstTab.Content, _root.LineageTab.IsChecked == true ? PageKind.Lineage
            : _root.JournalTab.IsChecked == true ? PageKind.Journal
            : _root.RecordsTab.IsChecked == true ? PageKind.Records
            : _root.GuideTab.IsChecked == true ? PageKind.Guide
            : State.Run is null ? PageKind.Camp : PageKind.Run);

    /// <summary>The Guide page, for the tests that read and click it.</summary>
    internal GuidePage GuidePage => (GuidePage)_pages[PageKind.Guide];

    /// <summary>The first tab's page: the run while one exists (ended or not), else Camp (G13, G14).</summary>
    private PageKind FirstPage => State.Run is null ? PageKind.Camp : PageKind.Run;

    // ===== Pages ===============================================================================

    /// <summary>
    /// Shows a page: the one shown before is hidden (the Run page skips what it plays,
    /// and its loop goes off), the new one shown; the tabs say which.
    /// </summary>
    internal void ShowPage(PageKind kind)
    {
        // Camp and Run are one tab: a run that exists is the Run page, none is Camp (H20).
        if (kind is PageKind.Camp or PageKind.Run) kind = FirstPage;
        if (_hasShown && _shown != kind) _pages[_shown].Hide();
        var page = _pages[kind];
        if (!_hasShown || _shown != kind || _root.PageHost.Children.Count == 0)
        {
            _root.PageHost.Children.Clear();
            _root.PageHost.Children.Add(page.Root);
        }
        _shown = kind;
        _hasShown = true;
        page.Show();
        SyncTabs();
    }

    /// <summary>
    /// A tab. During a playback a press on a tab only finishes the playback: the press
    /// is spent (H5). A click on the checked tab keeps it checked.
    /// </summary>
    internal void TabClicked(PageKind kind)
    {
        Guard(() =>
        {
            if (_run.Playing && _shown == PageKind.Run)
            {
                _run.SkipPlayback();
                return;
            }
            _run.Disarm();
            ShowPage(kind);
        });
        Settled(SyncTabs);
    }

    /// <summary>The tabs as the page shown has them: exactly one checked; the first reads Run while a run exists, else Camp.</summary>
    private void SyncTabs()
    {
        _root.FirstTab.Content = State.Run is null ? HatchlingText.TabCamp : HatchlingText.TabRun;
        _root.FirstTab.IsChecked = _shown is PageKind.Camp or PageKind.Run;
        _root.LineageTab.IsChecked = _shown == PageKind.Lineage;
        _root.JournalTab.IsChecked = _shown == PageKind.Journal;
        _root.RecordsTab.IsChecked = _shown == PageKind.Records;
        _root.GuideTab.IsChecked = _shown == PageKind.Guide;
    }

    /// <summary>The wallet (G13): "1,240" and the currency's word, at once whenever the balance changes, on any page.</summary>
    internal void ShowWallet() => _root.Wallet.Text = HatchlingText.Money(State.Wallet.Balance);

    /// <summary>"Back to camp" was taken: the ended run is gone, Camp is the first tab's page.</summary>
    internal void AfterBackToCamp()
    {
        ShowWallet();
        ShowPage(PageKind.Camp);
    }

    // ===== The game's events =======================================================================

    private void OnGameChanged()
    {
        if (_sample is not null) return;
        if (_run.Playing)
        {
            _refreshOwed = true;   // after the playback: a page is never redrawn under a turn being watched
            return;
        }
        Refreshed();
    }

    private void OnDrained()
    {
        _run.Drained();
        ShowWallet();
        SyncTabs();
        if (_refreshOwed)
        {
            _refreshOwed = false;
            Refreshed();
        }
    }

    private void Refreshed()
    {
        ShowWallet();
        // A Reset from the window can take the run away under the Run page.
        if (_shown is PageKind.Camp or PageKind.Run && FirstPage != _shown) ShowPage(FirstPage);
        else _pages[_shown].Refresh();
        SyncTabs();
    }

    // ===== The loop ===================================================================================

    /// <inheritdoc />
    protected override void Step(int ticks)
    {
        if (_shown == PageKind.Run) _run.Step(ticks);
    }

    /// <inheritdoc />
    protected override void Draw()
    {
        if (_shown == PageKind.Run) _run.Draw();
    }

    // ===== Layout states (A26) ===============================================================

    /// <summary>
    /// Shows a layout state with its sample (A11): any number of times, in any order,
    /// on a view never shown; no storage read or written; nothing enqueued; the loop
    /// off. They are the Run page's seven, the ten of the pages between runs (Camp,
    /// the Lineage, the Journal, Records) and one for each of the Guide's sections
    /// (H94), each named by its page. A name
    /// not in <see cref="LayoutStates"/> throws <see cref="ArgumentException"/>.
    /// </summary>
    public override void ShowLayoutState(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!AllStates.Contains(state))
            throw new ArgumentException($"Hatchling has no layout state '{state}'.", nameof(state));

        var sample = Samples.For(state);
        _sample = sample.State;
        _refreshOwed = false;
        var kind = PageOf(state);
        if (kind == PageKind.Run)
        {
            ShowPage(PageKind.Run);
            if (sample.Turn is { } turn) _run.ShowFrozen(sample.State, turn, sample.Beat, sample.Tick);
            else _run.ShowSample(sample.State);
        }
        else
        {
            // The Journal's last line reads the real dino's reading: a sample's own,
            // never the game's (A26: the same picture on every machine).
            ((JournalPage)_pages[PageKind.Journal]).SampleReading = sample.Reading;
            if (kind == PageKind.Guide) GuidePage.Choose(state[GuidePrefix.Length..]);
            ShowPage(kind);
            if (sample.Armed && kind == PageKind.Camp) ((CampPage)_pages[PageKind.Camp]).Arm();
        }
        ShowWallet();
        SyncTabs();
        Loop.Rate = LoopRate.Off;
    }

    /// <summary>The page a layout state shows, by the prefix of its name.</summary>
    private static PageKind PageOf(string state) =>
        state.StartsWith("run-", StringComparison.Ordinal) ? PageKind.Run
        : state.StartsWith("camp-", StringComparison.Ordinal) ? PageKind.Camp
        : state.StartsWith("lineage-", StringComparison.Ordinal) ? PageKind.Lineage
        : state.StartsWith("journal-", StringComparison.Ordinal) ? PageKind.Journal
        : state.StartsWith("records-", StringComparison.Ordinal) ? PageKind.Records
        : state.StartsWith(GuidePrefix, StringComparison.Ordinal) ? PageKind.Guide
        : throw new ArgumentException($"Hatchling has no page for the layout state '{state}'.", nameof(state));

    // ===== Handlers =====================================================================================

    /// <summary>A handler of the view's pages, through the view's guard (A10): nothing when paused or disposed, an exception to the host's Fault.</summary>
    internal void Guarded(Action action) => Guard(action);

    /// <summary>
    /// What follows a click once its handler ran, whether or not it was taken: the
    /// controls put back as the state has them (a toggle the guard refused stays as
    /// the state says). Nothing once disposed; an exception goes to the host's Fault.
    /// </summary>
    internal void Settled(Action action)
    {
        if (_disposed) return;
        try
        {
            action();
        }
        catch (Exception error)
        {
            Host.Fault(error);
        }
    }

    /// <summary>The view goes: the game no longer heard, then the loop and the playback dropped (A20: nothing was pending). Idempotent.</summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _game.Changed -= _onChanged;
        base.Dispose();
    }
}
