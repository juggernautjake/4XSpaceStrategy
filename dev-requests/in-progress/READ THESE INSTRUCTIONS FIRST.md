# READ THESE INSTRUCTIONS FIRST

You are Claude Code, running automatically in GitHub Actions because somebody added a file to
`dev-requests/in-progress/`. This document is your complete spec for that job. Read it start to finish
before you touch anything else, and follow it exactly.

This file is **permanent**. It never moves to `completed/`, and its presence alone is not work to do.
Everything *else* in this folder is the job.

## The job, end to end

1. **Read everything.** Every text file in full; every image and video frame actually looked at. (§1)
2. **Find out who pushed them, and when** — from git, not from guessing. (§1)
3. **Look in `completed/` for `YYYY-MM-DD-<their-slug>.md`.** If today's doc for that person is already
   there, pull it back and append to it. If not, create it. One doc per person per day. (§2)
4. **Turn what you read into slices** — small, ordered, checkable objectives. (§2)
5. **Build each slice, verify it, tick it, commit and push it.** Then the next one. (§3, §4)
6. **When every box is ticked: verify the whole thing together**, then move the doc and its files to
   `completed/` — leaving this file behind. (§5)

Then the folder holds only this file again, and the system sleeps until somebody uploads something.

---

## 0. The one thing that will bite you

**You cannot compile this project.** It is a Unity 6 project, and there is no Unity Editor in CI. You
will never see a compiler error, and you must never imply otherwise.

So:

- **Never write "this builds", "compiles cleanly", or "tested"** in a commit, a planning doc, or a
  summary. You don't know. Say "not compiled — please build before playing."
- Compensate with review, because it's all you have. Before marking any slice complete, read the actual
  declaration of every symbol you called — don't trust your memory of an API — and spawn independent
  review subagents to hunt compile errors and logic bugs. That process has caught real bugs here that a
  confident read-through missed.
- The person who submitted the request is the one who compiles and plays it. Broken code costs them a
  round trip, so the bar for "done" is *reviewed hard*, not *looks right*.

---

## 1. Read all of it, properly

Everything in this folder except this file is part of one or more requests.

**Read every text file in full, and look at every image and every frame.** Not the first page, not a
skim — somebody chose to include each of these, and the one detail you skip is disproportionately
likely to be the one that mattered. A screenshot is usually the clearest statement of the bug in the
whole request; a log is the compiler error you cannot generate for yourself. This is the cheapest part
of the job and the part that decides whether you build the right thing.

| What you find | What it means |
|---|---|
| `.md`, `.txt`, `.log`, `.pdf` | **The request.** Read it. It's the list of things somebody wants built. A `.log` is usually a compile error from their Unity — the thing you can't see for yourself, so read it closely. |
| `.docx` or anything else you can't open | You have no reader for it. Don't guess at the contents. Say so in the planning doc and ask for `.md` or `.txt` next time. |
| `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp` | **Evidence.** Read them with the Read tool — you can actually see images. Mockups, bug screenshots, reference art. |
| `_frames/<video-name>/frame_*.jpg` | **A video, already turned into stills for you.** The workflow ran ffmpeg because Claude cannot watch video. Treat the folder as a sequence: it's usually a bug repro or a UI walkthrough. |
| `YYYY-MM-DD-<name>.md` | **A planning doc you wrote earlier** that isn't finished. Continue it — do not start a new one. |

Media files are **never** the whole request on their own. They exist to explain the text. Tie each one
to the thing it illustrates ("the flicker in `frame_004.jpg` is the strobe described in item 3"), and
say so in the planning doc. If media arrives with no text at all, do your best to infer the request
from it, and say plainly in the planning doc that you inferred it.

### Who submitted it, and when

Get this from git, not from guessing:

```bash
git log -1 --format='%an|%aI' -- "<the request file>"
```

That gives you the author name and the ISO date of the commit that added the file. Use the **author
name**, lowercased and hyphenated, as the person's slug (`Steve Wozniak` → `steve-wozniak`), and the
**date part** of the timestamp as the date.

If several request files arrived from different people, treat them as **separate requests** — one
planning doc each. Never merge two people's requests into one doc; the whole point of the log is that
each person can see what they asked for.

---

## 2. Find or create the planning doc

The doc is named `YYYY-MM-DD-<person-slug>.md` — one per person per day.

Look for it in this order:

1. **`dev-requests/in-progress/YYYY-MM-DD-<slug>.md`** — you were already working on it and the run was
   interrupted. Continue it. Don't rewrite the objectives you already have; append the new ones.
2. **`dev-requests/completed/YYYY-MM-DD-<slug>.md`** — same person, same day, second batch of requests.
   **Move the doc back** into `in-progress/` (`git mv`), then append. This is deliberate: one doc per
   person per day, not one per upload.

   Move **only the doc**. The earlier request files and images stay in `completed/` — they're already
   filed, and dragging them back would only mean moving them again. Append the *new* filenames to the
   doc's `**Source files:**` line and leave the old entries pointing where they are.
3. **Neither exists** — create it in `in-progress/`.

Two docs on the same date is correct and expected when two *different* people submitted. Two docs for
the same person on the same date is a bug — you missed step 2.

### The format

Use exactly this shape. The checkboxes are load-bearing: the Stop hook counts them to decide whether
you're allowed to finish, so an unchecked box is a promise you haven't kept yet.

```markdown
# Requests from <Person> — <YYYY-MM-DD>

**Source files:** `<request.txt>`, `<mockup.png>`, `_frames/<clip>/`
**Status:** In progress

## What they asked for

<A short, honest restatement in your own words — not a copy-paste. If the request was vague,
say what you took it to mean. If two items conflict, say so here rather than silently picking one.>

## Slices

### 1a. <Short title>
- [ ] <One concrete, checkable objective>
- [ ] <Another>

**Notes:** <Why this slice is first; what it depends on; anything the submitter should know.>

### 1b. <Short title>
- [ ] <objective>

### 2. <Short title>
- [ ] <objective>
```

**How to slice.** A slice is a chunk you can build, review, and commit as one coherent change. Letters
(`1a`, `1b`) mean "same area of the code, do them together, in order." Numbers mean "independent enough
to stand alone." Order them so nothing depends on a later slice. Prefer more small slices over few
large ones — each one is a checkpoint you can't lose.

**Write objectives you can check off honestly.** "Improve the UI" is not an objective. "The Build tab
lists power buildings under an Electrical category" is.

---

## 3. Build the slices, one at a time

Work top to bottom. For each slice:

1. Build it.
2. **Review it before ticking anything.** Read the real declarations of what you called — don't trust
   your memory of an API. Spawn review subagents (the **`Agent`** tool) to look for compile errors and
   logic bugs; you cannot compile, so this is the only thing standing between your code and their
   Unity. Fix what they find, and remember the count for the commit body.
3. Make the improvements you thought of while reviewing, if they're in scope for this slice. If they're
   not, add them to the planning doc as a new objective rather than sprawling.
4. Tick the boxes for what you actually finished. **Leave a box unticked if it isn't done** — an honest
   half-finished doc is useful; a doc that claims work you didn't do is worse than no doc.
5. Commit **and push** (see §4).

Then start the next slice. A Stop hook is watching: if unticked boxes remain, it will send you back to
work. That's not an error — it's the loop. Just keep going.

**If a slice turns out to be impossible or wrong**, don't fake it and don't silently drop it. Change the
box to `- [!]`, explain why underneath it, and move on. `- [!]` reads as "deliberately not done, see
note" and the hook treats it as resolved.

Follow this repo's conventions — read `CODEBASE_GUIDE.md` first, and match the surrounding code's style.
The comments here explain *why*, not *what*; write yours the same way.

---

## 4. Commit **and push** after every slice

Not at the end — after each one.

**A commit you haven't pushed is not a checkpoint.** This whole job runs on a throwaway CI runner that
is destroyed the moment the job ends, and it can end without warning: the 120-minute timeout, the turn
limit, the continuation backstop, a crash. Everything local dies with it. An unpushed commit is exactly
as gone as an uncommitted one — so "commit as you go, push at the end" would mean every early exit
throws away the entire run's work.

Pushing per slice is what makes every other promise in this document true: that an interrupted run
resumes (§2), that unfinished work gets picked up next time (§6), that nothing is lost.

```bash
git add -A
git commit -m "$(cat <<'EOF'
<Slice title>: <what changed, in plain words>

<Why, and anything the submitter should know. Note what you could NOT verify.>

Reviewed by <N> review agent(s); not compiled — please build before playing.
Requested by <Person> in dev-requests/in-progress/YYYY-MM-DD-<slug>.md

[dev-requests bot]

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"

git pull --rebase origin main && git push origin main
```

**Rebase before you push, every time.** A run can last two hours, and a human may well have pushed to
`main` while you were working. Without the rebase your push is rejected as a non-fast-forward, and the
tempting fix — forcing it — would destroy their commit. Force-push is denied for exactly that reason;
if a rebase conflicts, resolve it in favour of *their* change, note it in the planning doc, and move on.

**Keep the `[dev-requests bot]` marker.** The workflow skips any push carrying it, so it's part of what
stops this system from triggering itself in a loop.

**Say how many review agents ran.** It's the only externally visible sign that §0's review actually
happened. A commit body claiming zero is a bug worth chasing, and a commit body that never mentions it
hides the problem entirely.

---

## 5. Finishing

### First, verify the whole thing together

Every box is ticked. Before you file anything, do one last pass over the **complete** change — not slice
by slice, but all of it at once, as the submitter will receive it.

Per-slice review can't catch this class of problem by construction: each slice was reviewed against the
code as it stood *at the time*, and slice 4 may have quietly broken an assumption slice 1 relied on.
This is your only look at the finished shape.

```bash
git diff origin/main...HEAD --stat     # everything this run changed
```

Three questions, in this order:

1. **Does it hold together?** Read the full diff. Anything renamed, moved, or re-signatured in a later
   slice that an earlier one still calls? Anything you added twice under two names? Spawn review agents
   over the whole diff, not the pieces — you cannot compile, so this is the last defence.
2. **Did it actually do what they asked?** Re-read the original request — their words, not your
   restatement of them. Walk each objective and find the code that satisfies it. An objective you ticked
   because you *worked on* it, rather than because it's *done*, gets untick and finished now.
3. **What can't you stand behind?** Anything you're unsure of goes in the closing note. Nothing you're
   unsure of gets described as working.

If this pass finds something, fix it and commit — that's what the pass is for. Don't file around it.

### Then file it

1. Set `**Status:** Complete` at the top of the doc, and add a short closing note: what got built, what
   you couldn't verify, anything the submitter should look at first, and anything the final pass turned
   up.
2. Move the doc **and every file still sitting in `in-progress/` that fed it** into `completed/` — the
   request text, the logs, the images, the `_frames/` folders. Use `git mv`.
   ```bash
   git mv "dev-requests/in-progress/YYYY-MM-DD-<slug>.md" dev-requests/completed/
   git mv "dev-requests/in-progress/<request.txt>" dev-requests/completed/
   git mv "dev-requests/in-progress/_frames/<clip>" dev-requests/completed/
   ```
   Sources from an earlier batch are already in `completed/` (see §2) — leave them there.
3. **`READ THESE INSTRUCTIONS FIRST.md` stays put.** It is not part of any request, and it is the only
   file that ever stays in `in-progress/`.
4. Commit the move, then push.
5. **Check the folder.** `ls dev-requests/in-progress/` should print exactly this file and nothing else.

That last check is the one people skip. When `in-progress/` holds only this file, the system is dormant
and costs nothing until somebody uploads something. A stray file left behind means it wakes up on the
next push, finds work that isn't there, and burns credit discovering that.

---

## 6. Rules

- **Never claim you compiled or tested anything.** See §0. This is the one that damages trust.
- **Never delete a request file.** Move it to `completed/`. It's somebody's record of what they asked for.
- **Never edit somebody's request text** to match what you built. The doc is where your interpretation
  goes; their file stays as they wrote it.
- **Never touch another person's planning doc.** One doc, one person.
- **If the request is genuinely ambiguous**, build the reading you think is right and write the ambiguity
  into the planning doc under "What they asked for", along with what you assumed. Nobody can answer a
  question here — the run is autonomous — so a documented assumption beats a stall.
- **If a request is huge**, slice it and build what you can. Mark the rest `- [ ]` and leave
  `**Status:** In progress` so it's picked up next time. Shipping four good slices beats twelve rushed ones.

---

## 7. Working fast

You have a real budget: a 120-minute job, a turn limit, and a cap on how many times the Stop hook will
send you back. Slices you never reach don't get built. Speed here is not rushing — it's not wasting the
budget on things that were never going to produce code.

**Spend the budget on building, not on re-discovering.**

- **Read `CODEBASE_GUIDE.md` first.** It's a per-file map of this project maintained for exactly this
  purpose. Ten minutes with it beats an hour of grepping, and it will tell you the thing you were about
  to spend twenty turns finding out.
- **Don't re-explore what you already know.** Within one run, you've read what you've read. Re-opening a
  file to check something you established an hour ago is pure cost.
- **Never re-read a file you just wrote to confirm the write landed.** Edit and Write error if they fail.
  A successful edit is proof.
- **Use `Grep` and `Glob`, not shell.** They're faster, they're built for it, and the shell allowlist is
  deliberately narrow.

**Do things at the same time.**

- **Batch independent tool calls into one message.** Reading six files is one step, not six.
- **Run review agents concurrently.** Three reviewers in one message finish in the time one takes. This
  is the single biggest lever you have, because review is mandatory (§0) and would otherwise dominate the
  budget.
- **Delegate breadth to subagents.** When answering a question means sweeping thirty files, send an
  `Agent` and keep the conclusion — not thirty files' worth of context you'll be carrying for the rest of
  the run.

**Build what was asked, and stop.**

- **No gold-plating.** No abstractions for hypothetical futures, no error handling for things that can't
  happen, no refactor of surrounding code you happened to notice. A bug fix does not need a helper class.
  It is not thoroughness, it's budget spent on work nobody asked for and review surface nobody wanted.
- **Don't re-litigate.** The slice plan is decided once, in §2. Don't reopen it mid-build because a
  neater ordering occurred to you.
- **When you have enough to act, act.** Don't survey options you won't pursue. Pick the reading you
  believe, write down the assumption (§6), and build.
- **Don't narrate.** Nobody is watching this run. The planning doc and the commits are the output; a
  running commentary is tokens that could have been a slice.

**Size slices to finish.** A slice you can build, review, and push inside ~30 turns is a checkpoint that
survives. A slice that spans half the request is one crash away from being nothing. If a slice starts
feeling open-ended while you're in it, split it, tick what's genuinely done, and push — the rest becomes
a new objective rather than a lost hour.
