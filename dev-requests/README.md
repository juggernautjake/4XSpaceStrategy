# Dev Requests

**Want something built, fixed, or changed? Put a file in `in-progress/`. That's the whole process.**

Claude reads it, writes a plan, builds it, and pushes to `main`. Pull, and it's there.

---

## How to ask for something

1. Write a plain text or Markdown file. Anything readable — a numbered list, a paragraph, a rant.
2. Add screenshots, mockups, or a screen recording if they help. Same folder.
3. Put it in **`dev-requests/in-progress/`** and push.
   - Through GitHub in a browser: **Add file → Upload files**, navigate into `dev-requests/in-progress/`.
   - Or `git add` / `commit` / `push` it like any other file.

Within a couple of minutes the **Actions** tab shows a *Dev Requests* run. When it finishes, your
request has been built and pushed to `main`.

Name the file whatever you like — `power-grid-bugs.txt`, `stuff.md`. The system works out who you are
from the git commit, not the filename.

## What happens to it

A planning doc appears at `dev-requests/completed/YYYY-MM-DD-your-name.md`, sitting next to the files
you uploaded. That's your receipt: what you asked for, how it was broken into slices, what got built,
and anything Claude couldn't do or wasn't sure about. Read the closing note — that's where the caveats
live.

If a request was too big to finish in one run, the doc stays in `in-progress/` marked
**Status: In progress**, with the finished slices ticked and pushed. It picks up where it left off next
time anyone uploads. Work already built is already on `main` either way — it's pushed slice by slice,
not saved up for the end.

If you upload more on the same day, it **updates the same doc** instead of making a second one. Two
people on the same day get one doc each, so your requests stay separate from theirs.

## Things worth knowing

**Nothing is compiled.** There's no Unity in the cloud, so Claude never sees a compiler error — it
reviews its own work hard, but it cannot know the code builds. **You are the compiler.** If it doesn't
build, paste the error into a new request file and it'll get fixed.

**Video gets turned into stills.** Claude can look at images but can't watch a clip, so any video is
sampled at one frame every 2 seconds (up to 40 frames). Fine for a UI walkthrough or a visual bug. If
the thing you're showing happens in a single frame, a screenshot is better.

**Be concrete about what "done" looks like.** "Make the map better" gets you a guess. "The map should
stay centred on the selected planet when you zoom" gets you the thing you wanted. Ambiguity doesn't
stall the run — Claude picks a reading and writes down what it assumed — but its guess might not be
yours.

**Big requests get sliced.** A large ask is broken into pieces and built in order. If it can't finish
them all in one run, it builds what it can, leaves the rest unticked, and picks up where it left off
next time somebody uploads. Nothing is lost.

**Anything marked `- [!]`** in a planning doc is something Claude decided it *shouldn't* build, with the
reason underneath. Worth reading — it's usually a conflict in the request or something that would break
elsewhere.

## Layout

```
dev-requests/
├── in-progress/
│   ├── READ THESE INSTRUCTIONS FIRST.md   <- Claude's spec. Permanent. Don't delete it.
│   └── (your files, while they're being worked on)
└── completed/
    └── (planning docs + the files that produced them)
```

When `in-progress/` has nothing in it but the instructions file, the system is asleep and costs nothing.
Uploading anything wakes it up.

## Setup (once, by the repo owner)

Add an **`ANTHROPIC_API_KEY`** secret under *Settings → Secrets and variables → Actions*. It comes from
the [Anthropic Console](https://console.anthropic.com/settings/keys) and bills to that account —
separate from a Claude subscription.

Also check *Settings → Actions → General → Workflow permissions* is set to **Read and write**, or the
agent can't push what it builds.

Tuning lives in `.github/workflows/dev-requests.yml`: the model, `--max-turns`, and
`SLICE_GATE_MAX_CONTINUATIONS` (how many slices one run will grind through).
