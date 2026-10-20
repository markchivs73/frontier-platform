---
description: End-of-session handover — rewrite STATUS.md so the next session (or the dashboard) can pick up cold
---

Update `STATUS.md` at the repository root so whoever comes next — Mark with five minutes, or a fresh
agent with no context — knows exactly where this work stands and what to do next.

1. Read the current `STATUS.md` (if it is missing, create it with the headings below).
2. Look at what actually happened this session: the conversation, `git status`, `git diff --stat`,
   and `git log --since="12 hours ago" --oneline`. Note any open PR or pushed branch.
3. Rewrite the file in place, keeping its shape exactly:
   - `**Updated:**` today's date (YYYY-MM-DD) · `**State:**` active | paused | blocked | idea ·
     `**One-liner:**` where this is, in one sentence
   - `## Where it is` — 2–5 bullets reflecting the state *now*, not a log of the session
   - `## Decisions made` — add any decision taken this session as `- YYYY-MM-DD — decision and why`,
     newest first; keep the last ~8
   - `## Blockers` — what is stuck and what would unstick it, or "None"
   - `## Next actions` — every item tagged `[15m]`, `[1h]` or `[deep]`. Remove what got done.
     Keep at least two `[15m]` items, each startable cold by a fresh session: name the exact file,
     task ID or command, and what "done" looks like.
   - `## Parked ideas` — things deliberately not being done now
4. Don't pad it. The whole file should stay under ~60 lines.
5. If the repo has a plan or state file of its own (for example `docs/state/IMPLEMENTATION-PLAN.md`),
   keep `STATUS.md` consistent with it rather than duplicating it — point to it.
6. If this folder is a git repository, commit `STATUS.md` on the current branch. Follow the repo's own
   commit-message convention (a `git-workflow` skill or CLAUDE.md says what it is — for example
   `[meta] Update STATUS.md handover` or `meta: Update STATUS.md handover`); with no convention, use
   `status: <one-liner>`. Don't push unless the session has been pushing already.
7. Reply with the one-liner and the top `[15m]` item.

$ARGUMENTS
