---
name: patch-notes
description: The owner's patch notes for a PandoraOverlay release, the post for the overlay's Discord announcement channel - a short intro, then the changes in New / Changes / Fixes / Removed sections, under Discord's 2000 characters, saved as a txt in the owner's announcements folder for the owner to post.
when_to_use: When the owner asks for the announcement, the patch notes or the update notes of a release or pre-release ("give me the patch notes for 1.38", "write the Discord post"), or one catch-up post for every version since the last announcement, or answers yes to the release pass's Part B item 5 (a Discord announcement). Not for the CHANGELOG, the README or the GitHub release text, which the release workflow cuts from the CHANGELOG.
---

# Patch notes

The overlay's Discord channel is announce-only: the owner posts there, as
themselves, and players read it to see what a version brings. The owner's
format (Oct 24 2026): a short intro, then the changes in sections, like other
software and games write their patch notes. It replaced one emoji, bold label
and paragraph per feature (the 1.27–1.35 posts), which read long.

## The format

```
## Pandora Overlay X.Y.Z
One or two plain sentences: what this version is about, and why if the why
is a player's story.

### New
- **Short lead**: one sentence on what it does for the player.

### Changes
- **Short lead**: what is different now.

### Fixes
- What no longer goes wrong, said plainly.

### Removed
- **What went**: what replaces it, or why it is gone.

Download: <https://github.com/DavidKatona/PandoraOverlay/releases/latest>
-# Questions, bugs and ideas: <https://github.com/DavidKatona/PandoraOverlay/issues>
```

- **The sections**, in this order, each only when it has something: New
  (the CHANGELOG's Added), Changes (Changed), Fixes (Fixed), Removed
  (Removed). Two optional ones go before the links: **Updating**, only when
  a player must do or know something beyond the usual update card (the zip to
  unblock, keys that stay as they were), and **Known issues**, only for a real
  open one.
- **One line per bullet**, about twenty words at most, the thing players
  notice most first. A bullet naming a feature starts with a bold lead of a few
  words and a colon; a fix can be a plain sentence. Several small items share
  one bullet rather than crowd the list.
- **The title** is a Discord header: `## Pandora Overlay X.Y.Z`. A pre-release
  says so and who it is for: `## Pandora Overlay X.Y.Z-rc.N (test build)`, and
  its link is that release's page (`<.../releases/tag/vX.Y.Z-rc.N>`), never
  `releases/latest`, which players' overlays follow.
- **The links** in angle brackets, so Discord shows no preview cards and the
  post stays compact. The questions line is Discord's small text (`-# `). Never
  ask for replies in the channel: it is announce-only, so people go to the
  GitHub issues.
- **Credits** where they are due (the website team, VulnonaMAP for data from
  it), as one small-text line (`-# `) before the links.

## Catch-up posts (several versions in one)

When the owner hasn't posted since an earlier version, one post covers every
version since the last announcement (the owner, Oct 24 2026: always ONE post,
never split). The last announced version is the newest
`pandora-discord-vX.Y.Z.txt` in the announcements folder (`-test` files don't
count); if that is unclear, ask.

- **The title** is the newest version; **the intro** names the range first:
  "The first post since 1.35, so this covers 1.36 to 1.38." Then the one or
  two things that matter most across all of them.
- **One set of sections** for all the versions together, not a block per
  version: players update straight to the newest, so which version brought
  what doesn't matter to them.
- **Describe things as they are now.** A feature changed again later is
  written in its final form; one added and removed again inside the range is
  left out; a fix for something that arrived inside the range is left out
  (it never reached players broken).
- **Cut harder to stay in one post:** first what players can't notice
  (request pacing, log internals), then the smallest changes, merged into
  shared bullets. Keep what a player must know: privacy notes (what the log
  holds), keys that stay as they were, anything that changed where a thing
  lives. Say what was cut when handing it over, so the owner can swap
  something back in.
- **The link** is `releases/latest`, the newest version.
- The test run for 1.36–1.38 (`pandora-discord-v1.38.0-test.txt`, 1,991
  characters) is the worked example.

## Where the content comes from

- **The version's CHANGELOG section** (`CHANGELOG.md`): it is already written
  for players, and the update card shows the same text. The post is that
  section compressed, not new claims. Check it against the release's commits
  (`git log <previous tag>..vX.Y.Z`) so nothing big is missing, and leave out
  what players never see (refactors, tests, docs, the log's internals).
- **The never-tested list** in CLAUDE.md's Roadmap: nothing untested is sold
  as working. It may be in the post if it is in the release; it is not
  promised beyond what it does.

## The words

- Plain, human sentences. **No em-dashes** (use a colon, a comma or a full
  stop) and no polished "AI tells" ("a lot has landed", "at a glance",
  "blended right in"), no arrows (→). Menu paths are written as the app
  writes them: Settings › Skins.
- The player's names for things: "the map", never "big map"; the widgets'
  and pages' names as they appear in the overlay.
- Only what the overlay really does and the website really shows (CLAUDE.md,
  Conventions): never that something "detects" what it can't, never a promise
  about the server's side (cooldowns, tiers).
- British spelling, like the CHANGELOG (favourite, colour).

## Length

Discord cuts a message at **2000 characters**. Count the file (`wc -m`, or
`(Get-Content -Raw <file>).Length`) and say the count. Over it: merge the
smallest items, shorten the leads, drop Updating if nothing in it is
essential. Never split into two posts unless the owner asks.

## Delivery

- Save it as a NEW file in the owner's announcements folder,
  `C:\Users\david\Desktop\Pandora Overlay Files\Pandora Overlay announcements\`,
  named `pandora-discord-vX.Y.Z.txt` (an rc: `pandora-discord-vX.Y.Z-rc.N.txt`),
  UTF-8 without a BOM, blank lines between the sections. Never overwrite an
  earlier one: if the name is taken, ask. A trial run for a version not yet
  released gets a `-test` suffix (`pandora-discord-vX.Y.Z-test.txt`), so the
  real name stays free for release day.
- Show the text in the reply too, in a code block, with its character count.
- Name the picture to attach: the release's hero,
  `pandora-channel-hero-vX.Y.Z.png` in the same folder (the README's
  `docs/screenshot.png`), or say there is none yet.
- The owner posts it. Never post, send or schedule it anywhere.
