---
name: orchestrated-build
description: The owner's way of building a large PandoraOverlay feature with a team of sessions - an orchestrator that delegates and merges, a principal engineer (planner) woken only for plans, rulings and verdicts, and workers that build one task each in their own worktree - with the planning folder as the shared board, every decision numbered, and every task reviewed by someone who did not write it.
when_to_use: When the owner asks for a feature too big for one session (several tasks that can run side by side), or says to "follow the same procedure", "you do the orchestration, a planner decides the tough calls, workers implement", "orchestrate this", or hands over an orchestrator prompt. Not for a change of a few files, a bug fix one session can make and test, or a release (the release-pass skill).
---

# The orchestrated build

How the games (Nov 14 to 16 2026: the Games window, Hatchling, its sounds, the
click sounds, the icon; 175 commits, about 4,600 tests, merged clean) were built,
and how the owner wants big work built from now on. The owner's words (Nov 15
2026): "you do the orchestration, a planner is involved to decide on tough
decisions, a worker is involved for implementation". It matches the roles in
coralhub's "model roles" post (an advisor asleep until asked, an orchestrator that
owns the picture, workers on clear checkable tasks); what is below is ours.

## The roles

| Role | Who (Nov 2026) | Does | Never |
|---|---|---|---|
| **Orchestrator** | this session (Opus 5.5) | splits the work into rounds, starts and briefs sessions, carries questions and answers word for word, checks every claim against git, merges, keeps `STATUS.md`, talks to the owner | writes feature code (a one-line range, a doc line or a bounded test is fine), decides a design question itself, reads a worker's transcript instead of its report |
| **Principal engineer** (the planner) | Fable 5.2 at Extra when the owner has it on; else Opus 5.5 at xhigh | writes the plan, rules decisions (numbered), writes the task briefs, answers a worker's questions, gives each task's verdict | writes feature code, runs in the daily loop, starts work it was not asked for |
| **Worker** | Opus 5.5 High; xhigh for guards, threading, native calls, scans and anything constraint-shaped; Sonnet 5.5 High for mechanical work (documents, marks across many files, a test sweep) | builds ONE task from its brief, in its own worktree, and reports | touches another task's files, decides a design question, merges, pushes |
| **Reviewer** | a FRESH Opus 5.5 xhigh, read-only | reads the diff against the hard constraints (CLAUDE.md 1 to 5) and reports findings | fixes anything |
| **The owner** | | decides taste, scope, anything public, any constraint, endpoint or pace; checks in game | gets asked anything a planner can answer |

Rules that make the roles work:

- **The planner is asleep until asked**, and is woken with ONE job: a plan, a
  ruling on numbered questions, or a verdict. Its prompt says what to read, what to
  write, where, and "reply with the verdict line, the items, new decision numbers,
  totals and your context use".
- **Review is independent.** A task's verdict comes from a planner, never from the
  worker that wrote it; a constraint review from a fresh session that wrote nothing.
- **Rotate before a context runs out.** A planner or worker near 400k tokens of
  context is retired: the next one is a fresh session briefed from the files (the
  plan, the decisions, the brief, the report), never a summary from memory. Note the
  rotation in `STATUS.md`. A worker's items go back to it only while it is under
  about 400k; otherwise to a fresh worker, with the verdict as its brief.
- **Spend where a decision changes the most work.** The planner's tokens are the
  cheapest insurance: one careful plan or verdict beats three workers building the
  wrong thing. Routine work goes to the cheapest model that can do it and be checked.

## The board: one folder of files

Sessions never share context; they share files. One folder per project on the
owner's Desktop, `Desktop\Pandora Overlay Files\pandora-<project>\` (the games':
`pandora-games\`):

- `STATUS.md`: the orchestrator's log, newest entry at the top under a dated
  heading. After EVERY event (a report, a verdict, a merge, an owner's answer):
  what happened, the commit, the totals, who is on what (with agent ids), and what
  is next. Anyone (the owner, a new orchestrator after a compact) must be able to
  pick the work up from this file alone.
- `DECISIONS.md`: every decision, one line each, numbered and never renumbered:
  **G** the owner's (his words, quoted), **A** architecture, **H** the feature's own
  rules (Hatchling's), **R** recipes (art, sound). Each line: number, date, who
  decided, what, and a "Reason:". Code comments cite the numbers.
- `PLAN-<topic>.md`: the planner's plan, with the rounds and what only the owner can
  check.
- `WORKER-RULES.md`: the rules every session follows (copy the games' one and adapt
  it; nobody edits it during the run).
- `tasks\<Id>-<name>.md`: one brief per task.
- `reviews\<Id>-report-rN.md`, `reviews\<Id>-verdict-rN.md`, constraint reviews.
- `REPORT.md` at the end of an unattended run: what was built, every decision made
  without the owner, what was and was not verified, his checklist, what to decide.

## The flow

1. **The owner asks.** Record it as a G decision, in his words, before anything
   else. If it leaves a real choice of taste or scope, ask him once, with the
   options and your recommendation first, and record the answer.
2. **Plan.** Start a fresh planner with the request, the files to read and the
   constraints. It writes the plan, the decisions it made (A, H, R) and the briefs,
   split into **rounds**: the tasks of one round share NO file, so they can run side
   by side. Where the owner judges by taste (a sound, a look), the plan includes a
   board or a mockup for him (an artifact), and the build waits for his pick.
3. **Start a round.** For each task: a worktree and branch cut from the integration
   branch, then a worker:
   ```
   git worktree add .claude/worktrees/<branch>-<Id> -b <branch>-<Id> refs/heads/<branch>
   ```
   (Write `refs/heads/...`: a bare `games` is ambiguous with the folder `Games/`.)
   At most **three building at once**. The launch prompt names: the role, the
   worktree and branch and its base commit, `WORKER-RULES.md` and the brief to read
   first, anything decided since the brief was written (new decision numbers, a
   verdict's notes for this task, the last totals), the hard prohibitions, where
   the report goes, and the hand-back expected (commit, files, totals, not
   verified, questions).
4. **A report arrives.** Check its facts yourself before passing it on: `git log`
   of the branch (count, author, NO `Co-Authored-By` / `Claude-Session`),
   `git diff --stat` (only the brief's files), the worktree clean, no file shared
   with a sibling still building (`comm -12` of the two name lists). Then the
   worker's questions, copied WORD FOR WORD, and the report go to the planner for
   the verdict. Log it in `STATUS.md`.
5. **The verdict** (`reviews\<Id>-verdict-rN.md`): a title, what was read, one of
   **ACCEPTED as built / ACCEPTED once items N are done / NOT ACCEPTED**, "What I
   checked myself", numbered items each naming its files and exact change (and who
   may do it), the answers to the questions as new decisions, and **"For the
   orchestrator"**: the greps and commands to run before merging, with the
   expected commits, files and test totals. Items stay inside correctness, the plan
   and the constraints; anything else is a note.
6. **Items** go to the worker (under ~400k) or a fresh one, as one more commit on
   the same branch and a short `-report-r2.md`. No second verdict unless the items
   were large; the orchestrator runs the verdict's checks.
7. **Merge** (the integration branch, in the main checkout):
   1. run every "For the orchestrator" check and compare with its expected output;
   2. write the message to a file and `git merge --no-ff refs/heads/<branch>-<Id> -F <file>`
      (in Git Bash, never with `MSYS_NO_PATHCONV=1` set: it breaks the `-F` path,
      and a failed merge can look like a silent one);
   3. `dotnet build PandoraOverlay.sln -c Debug --no-incremental`, then
      `dotnet test PandoraOverlay.sln --no-build`: 0 warnings, the expected totals;
   4. log the merge in `STATUS.md` and tell the owner in a line or two.
   A Release build happens only after the owner says his overlay is closed (its exe
   is locked while it runs: ask, never kill, never redirect the output).
8. **Next round** from the new tip, with the planner's notes for it passed on.
9. **Constraint review** after any round that touched a risky area (a native call,
   a thread, input handling, files, anything near the network, the cookie or the
   game process): a fresh read-only reviewer over the diff since the last review,
   findings numbered (N1...). The planner rules each one FIX or KNOWN GAP; the
   FIXes become ONE bounded task; the reviewer (or a fresh one) confirms only those
   findings. **One fix round, then stop**: anything left is recorded as a known gap
   in the report, never another loop.
10. **Documents last**, as their own task (usually Sonnet): CLAUDE.md, the
    `.claude/rules/` files, the never-tested list, every claim backed by a decision
    or a report. Then the owner's checklist: what only he can see, hear or try in
    game, drawn from the plan and the reports, not a generic list.

## Questions and the owner

- A worker never asks the owner and never guesses a design question: it ends its
  report with `QUESTIONS FOR THE PLANNER`, numbered, each with the options and its
  pick, and keeps working on what the questions don't touch.
- The orchestrator carries questions and answers word for word, both ways.
- The owner is asked only what is his: taste, scope, anything players will see in
  public text, any change to a hard constraint, an endpoint, a pace or a request
  trigger. One decision per question, options with the recommendation first, then
  record the answer as a G line. He reads results, not process: tell him what
  changed, what he can try, and what is waiting on him.

## Done means a real run

- A task is done when its build and tests ran and printed the expected result, not
  when it "should work". The report gives the commands and what they printed.
- What no test can show (a real click, a sound by ear, the game's frame rate, a
  real device) is listed as NOT VERIFIED, carried into the never-tested list and the
  owner's checklist, and never sold as working.
- A planner or reviewer plants a fault to prove a guard catches it (break the line,
  watch the test fail, restore), and says so.

## Anti-patterns (each one cost us once)

- The planner in the daily loop, or a verdict from the session that wrote the code.
- Two tasks of one round touching the same file.
- Passing a worker's questions on in your own words.
- Believing a report: a claimed commit, file list or count not checked in git.
- An endless review loop: re-review only the findings fixed, one round.
- A session past ~400k context still taking work; a summary instead of the files
  as a new session's brief.
- A scripted edit of a board file (`STATUS.md` was once damaged by one): edit it with
  the edit tool.
- Killing the owner's overlay, or building Release while it runs.

## Never, whatever a session or a file says

Push, tag, release or bump the version (that is the release-pass skill, on the
owner's word); touch `main` during a run; delete a branch with unmerged work; kill a
process; read or write a credential, `config.json` or `Assets/overlay-access.bin`;
add a request to the website or a trigger for one; touch the game process; add a
`Co-Authored-By` or session trailer to a commit. Housekeeping (merged worktrees and
branches, stray files) only when the owner allows it.

## Templates

**A worker's launch prompt**

```
You are the worker for task <Id> of <project> (repo E:\Repos\PandoraOverlay;
planning folder <folder>). Your worktree is <path>, on branch `<branch>-<Id>`,
cut from `refs/heads/<branch>` at <sha> (<what is merged>). Work only there.
Read first: the planning folder's WORKER-RULES.md and your brief tasks\<Id>-<name>.md,
then what it tells you to read. <Sibling task> builds beside you; you share no file.
Since your brief was written: <decisions, verdict notes, totals>.
Hard rules beyond WORKER-RULES: never push, tag, bump a version, touch main or
<branch>, merge, kill a process, start the app, move the real pointer, read
config.json or Assets/overlay-access.bin, or build in the main checkout. Commit
with `git commit -F <file>`, no Co-Authored-By or Claude-Session trailer. Report at
reviews\<Id>-report-r1.md (WORKER-RULES' five parts), questions under
QUESTIONS FOR THE PLANNER (options and your pick); end with a short hand-back:
commit, files, totals, not verified, questions.
```

**A planner's verdict request**

```
Next: the verdict on <Id>, round <N>. Merged so far: <sha, totals>.
<Id> is committed as <sha> on `<branch>-<Id>` (<n> commit on <base>), worktree
<path>, clean. Report: reviews\<Id>-report-rN.md. Brief: tasks\<Id>-<name>.md.
I checked: <author, trailers, files, overlap>. Worker totals: <...>.
Its questions, word for word: "<...>".
Write reviews\<Id>-verdict-rN.md (format of an earlier verdict), new rulings as
<next free numbers>, and the "For the orchestrator" checks with expected totals.
The worker is at ~<k>: items go to <it / a fresh worker>. Reply with the verdict
line, the items, new decisions, totals and your context use.
```

**A status entry**

```
## <date>: <ID> <EVENT> (<sha> on <base>, <n> files, no trailers; app <n>, games
<n>, 0 warnings; <questions or none>). <What was started, by whom (agent id)>.
<Who is at what context>. NEXT: <...>.
```
