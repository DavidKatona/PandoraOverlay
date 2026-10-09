---
name: release-pass
description: The owner's release procedure for PandoraOverlay. FIRST the checklist for the owner (Part A checked and reported, Part B asked), and only after their answers and go the version bump, commit, annotated tag and push. Also the release model, pre-releases (rc tags) and hotfixing an old release.
when_to_use: When the owner says "do the release pass" (or "a release pass"), or asks to release, tag, publish, ship a pre-release / rc, or hotfix a version. Not for ordinary commits, and a version number in a request ("add it to 1.35") is not a release request.
---

# The release pass

Run this only when the owner asked for it. A version number in a request
only says which CHANGELOG section a change belongs to: commit locally,
report and wait. Never bump the version, tag or push the release before
the owner has answered Part B and said go; any other push happens only
when the owner asks.

## Release pass checklist (the owner's, Oct 9 2026)

"Do a release pass" starts with this checklist, not with the version bump:
before touching the version, post it to the owner, Part A filled in with
results, each saying HOW it was checked, and Part B as questions. Bump,
commit, tag and push only after the owner has answered Part B and said go.
Why: the owner doesn't read the code — they question decisions and test
in game — so what can't be seen in game must be checked and shown here.
And "verified" can be wrong: on Oct 9 the map's backdrop was reported
working by a spike that couldn't tell a 70% dim from black; the owner
caught it in game. So state the evidence, say first what could NOT be
checked, and say when a check was a tool or a render rather than an eye.

**Part A — Claude checks and reports.** The diff is
`git diff <last tag>..HEAD` plus any uncommitted work.

1. **Every network call that is new or changed.** For each, give the
   endpoint, the method, what triggers it (timer, click or hot path), how
   often at most, and whether it carries the cookie. Name the hard
   constraint that allows it: #2 for approved endpoints, #3 for nothing
   faster, writes only on a click. Find them by grepping the diff for
   `GetAsync|PostAsync|SendAsync|HttpClient|HttpRequestMessage|WebView2|Navigate`,
   and by reading what changed in `PandoraClient*`, `PollService*`,
   `UpdateChecker`, `Updater`, `SignInWindow` and the skin and avatar
   picture fetchers. "None" is an answer, and is said.
2. **New timers, loops and retries that can lead to a request**
   (`DispatcherTimer`, delay loops): each named, with its pace and its gate.
3. **The cookie.** List every changed line that reads, writes, sends or
   stores it: GetCookie, SetCookie, CurrentCookie, connect.sid, the Cookie
   header, config saves. Check that none of it reaches a log, an exception
   message, window text, a file other than the DPAPI blob, or a commit
   (constraint #4).
4. **The game process (anti-cheat).** Grep the overlay's own code (not
   tests or tools) for `Process`, `GetProcesses`, `OpenProcess`,
   `ReadProcessMemory`, `EnumWindows`, `FindWindow`, `SetWindowsHookEx`,
   global keyboard or mouse hooks, and focus calls on windows that aren't
   ours. There must be none (constraint #1; Velopack's installer scan is
   the one accepted exception).
5. **Build and tests.** Give the `dotnet test` count and result, and the
   Release build. If anything under `Minimap/` changed, run the golden-render
   harness (`Desktop\Pandora Overlay Files\pandora-big-map-sketch\golden-renders`,
   `run-golden.ps1`) against its baseline. Re-baseline only after the owner
   has checked a deliberate visual change in game.
6. **The release text.**
   - The CHANGELOG section for the version exists and is written for
     players, since the update card shows it.
   - Features are named the way players see them: "the map", never "big map".
   - The csproj `<Version>` equals the tag: minor for features, patch for
     fixes.
   - CLAUDE.md's version (the intro line and Releases → "Current:"), and the
     Roadmap's never-tested list (add what this release leaves untried).
7. **Offer a fresh-eyes review**: a reviewer that didn't write the code (a
   fresh subagent or `/code-review`) reading the diff against the hard
   constraints only. The owner decides whether to spend it. Recommend it
   when item 1, 3 or 4 has entries.

**Part B — ask the owner.** These can't be checked from here.

1. **In game:** list THIS release's behaviours to try, drawn from the diff
   (not a generic list), and ask which were tried. Name anything untried in
   the reply; don't gloss over it.
2. **Requests**, when A1 or A2 changed anything: was the pace checked in
   game? A proxy such as Fiddler shows each request, or ask for a temporary
   request log in a test build.
3. **A pre-release round:** recommend an rc when the release touches
   install or update, sign-in, focus or hotkeys, or anything that varies
   by PC. The owner decides.
4. **The commits:** how to group them (a refactor can be its own release),
   with no Claude attribution lines.
5. **After the tag:** is a Discord announcement or new README screenshots
   wanted for this one?

## After the owner's go: version, tag, publish

- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch.
- Release model: main moves freely between releases; tags mark the stable
  points. Anyone wanting "a version" uses a tag or its GitHub Release —
  never a random commit. No standing release/version branches. Pushing a
  `vX.Y.Z` tag runs `.github/workflows/release.yml`, which builds, tests and
  publishes: it fails unless the tag
  equals the csproj `<Version>`, cuts the version's notes out of CHANGELOG.md
  (`## [Unreleased]` for a pre-release; a missing or empty section fails the
  release) into the update package and the release text, and attaches
  `PandoraOverlay-vX.Y.Z-Setup.exe`, the self-updating
  `PandoraOverlay-vX.Y.Z-win-x64.zip` and Velopack's feed files
  (`.nupkg` full + delta, `releases.win.json`, `RELEASES`, `assets.win.json` —
  what the updater reads, under Velopack's names). The workflow's comments
  hold the `vpk` options and why. A tag containing "-" (`v1.35.0-rc.1`, with
  the csproj `<Version>` set the same) is published as a PRE-RELEASE: players'
  overlays never see it, only a pre-release build is offered it — that is how
  a release is rehearsed with testers before the real tag.
- Never move or re-tag an existing tag. If a release ships broken, fix forward
  and tag the next patch version.
- Hotfixing an old release while main holds unreleased work:
  `git switch -c fix vX.Y.Z` → fix → bump patch in csproj + CHANGELOG →
  tag `vX.Y.(Z+1)` → push the tag (release builds automatically) →
  merge/cherry-pick the fix back to main → delete the branch.
