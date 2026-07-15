#!/usr/bin/env bash
# ============================================================================
# THE SLICE GATE — a Claude Code Stop hook that refuses to let the agent stop
# while the planning docs still have unticked objectives in them.
#
# This is what turns "build a slice" into "build every slice". Claude finishes a
# slice and tries to end its turn; this hook counts the unticked boxes across the
# planning docs, and if any remain it emits a `block` decision, which sends Claude
# straight back to work on the next one.
#
# ---- Why the state is the boxes, and not a counter ----
# The gate reads the SAME artifact a human reads: `- [ ]` in the planning doc. There
# is no parallel progress file to drift out of sync with reality, and no way for the
# agent to be "done" according to one source and not the other. Ticking a box is the
# single act that means progress, which is exactly why the instructions are blunt
# about only ticking what's actually finished.
#
# ---- Why stop_hook_active is deliberately NOT the guard ----
# The documented loop-prevention idiom is to allow the stop when `stop_hook_active`
# is true. That flag means "you already blocked once this stretch" — so honouring it
# would cap this hook at exactly ONE continuation, and we need one per remaining
# slice. A five-slice job would build one and quietly go home.
#
# So the runaway backstop is MAX_CONTINUATIONS below, counted in a file of our own.
# It bounds the loop by iterations rather than by one-shot, which is the actual
# requirement.
# ============================================================================
set -uo pipefail

# Absolute, via CLAUDE_PROJECT_DIR. Hooks run in "the current directory", and the
# agent holds git and bash — if cwd ever wanders out of the repo root, a relative path
# would make this hook silently do nothing. That failure is invisible and points the
# wrong way: a Stop hook that errors or finds no directory does NOT block, so the agent
# would stop early with objectives still open and the planning doc would be the only
# evidence anything went wrong.
PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
REQUESTS_DIR="$PROJECT_DIR/dev-requests/in-progress"
COUNTER_FILE="${TMPDIR:-/tmp}/claude-slice-gate.count"
MAX_CONTINUATIONS="${SLICE_GATE_MAX_CONTINUATIONS:-12}"

# The hook's stdin payload. We don't need a field from it, but draining it matters:
# leaving it unread can hand the caller a broken pipe on a short write.
cat >/dev/null 2>&1 || true

# ONLY planning docs are counted, matched by their `YYYY-MM-DD-<person>.md` name.
#
# This scoping is load-bearing, and getting it wrong is expensive in two different
# ways. Counting every *.md in the folder would sweep in:
#   - the instructions file, which documents the checkbox format BY SHOWING IT; and
#   - the submitter's own request, which is very often a Markdown to-do list — the
#     README literally invites "a numbered list".
# Neither can ever be ticked (§6 forbids editing somebody's request), so the gate would
# block every stop, grind to MAX_CONTINUATIONS, and give up — on every single run,
# burning real credit to accomplish nothing.
DOC_GLOB='[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]-*.md'

if [ ! -d "$REQUESTS_DIR" ]; then
  # Fail LOUD, not open. Silently allowing the stop here is indistinguishable from
  # "all work finished", which is the one lie this whole system is built to avoid.
  echo "slice-gate: $REQUESTS_DIR does not exist — cannot verify the work is finished." >&2
  exit 2
fi

# `- [ ]` = still to do. `- [x]` = done. `- [!]` = deliberately not done, with a note
# underneath (see the instructions) — resolved as far as the gate is concerned, since
# an impossible objective is answered, not pending.
#
# The bullet may be `-` or `*` and the spacing is loose, because the gate must count
# what a human would count. A regex stricter than the format it polices would silently
# read a real objective as zero and let the agent stop early.
remaining=$(grep -rhoE '^[[:space:]]*[-*][[:space:]]+\[[[:space:]]\]' "$REQUESTS_DIR" \
  --include="$DOC_GLOB" 2>/dev/null | wc -l | tr -d ' ')
remaining="${remaining:-0}"

if [ "$remaining" -eq 0 ]; then
  rm -f "$COUNTER_FILE"
  exit 0
fi

# Runaway backstop.
count=0
[ -f "$COUNTER_FILE" ] && count=$(cat "$COUNTER_FILE" 2>/dev/null || echo 0)
case "$count" in ''|*[!0-9]*) count=0 ;; esac

if [ "$count" -ge "$MAX_CONTINUATIONS" ]; then
  # Allow the stop rather than grind forever. Safe to give up here only because every
  # finished slice was already committed AND PUSHED (instructions §3/§4) — the work
  # stands, the planning doc still shows what's left, and the next upload resumes it.
  echo "slice-gate: hit $MAX_CONTINUATIONS continuations with $remaining objective(s) still open — letting it stop." >&2
  rm -f "$COUNTER_FILE"
  exit 0
fi

echo $((count + 1)) > "$COUNTER_FILE"

# `decision: block` + `reason` is the documented way to refuse a stop; the reason is
# fed back to Claude as its next instruction, so it's written as one.
cat <<EOF
{
  "decision": "block",
  "reason": "$remaining objective(s) are still unticked in dev-requests/in-progress. Do not stop yet.\n\nPick up the FIRST unticked objective in slice order and build it. Before you tick anything: re-read the real declarations of every symbol you touched, and spawn review subagents with the Agent tool to hunt compile errors and logic bugs — you cannot compile Unity here, so that review is the only thing between this code and the submitter's editor. Record how many review agents ran in the slice's commit body, so a silent denial is visible rather than invisible.\n\nTick a box only if it is genuinely done; if an objective turns out to be impossible or wrong, change it to '- [!]' and write the reason underneath instead of faking it. Commit AND PUSH the slice before moving on — an unpushed commit dies with the runner.\n\nWhen every box is [x] or [!], finish the job per section 5 of 'READ THESE INSTRUCTIONS FIRST.md': set Status to Complete, git mv the doc and its remaining source files into dev-requests/completed/, commit, and push."
}
EOF
exit 0
