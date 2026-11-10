---
name: ai-report
description: Turn the automatic AI-usage logs (docs/ai-log/raw, written by the project hooks) into the team's AI documentation, adding new entries to docs/prompt-log.md and regenerating docs/AI_USAGE.md. Use when someone runs /ai-report, finishes a phase or PR, or asks to document, summarize or report how AI was used on TaxWise.
---

# AI usage report

The course grades AI use on this criterion:

> "Your use of vibe coding tools should be thoughtful, critical, and well-documented. We're looking for evidence that you've pushed the boundaries of what these tools can accomplish while maintaining awareness of their limitations."

Every claim in the report must be backed by evidence from the logs: a session id, a commit, a file or a test result. A report that admits failures and shows the human catching them scores better than one that only lists wins. Never invent outcomes, and never make the AI sound better than the logs show.

## Where the data comes from

- `docs/ai-log/raw/<git-user>/<session-id>.jsonl`: written automatically by `.claude/hooks/ai-log.sh` for every Claude Code session in this repo. It records prompts, tool calls (with file paths, commands, web sources, subagents), decision checkpoints (AskUserQuestion: the options offered, which one was marked Recommended, and what the human picked) and Claude's reply at the end of each turn. API keys are redacted before anything is written.
- `.claude/skills/ai-report/stats.sh`: prints per-person totals, the session index, git stats and the sessions already logged. `stats.sh <session-id-prefix>` prints one session's full timeline.
- git history: `git log --all --format='%h %an %ad %s%n%b' --date=short`. Trailers like `Co-Authored-By: Claude` mark AI-written commits.
- `docs/BUILD_PLAN.md` describes the intended agent workflow (planning agent → execution agent, phase by phase, with human approval → evaluation agent), the prompt-log format (§9) and the required Copilot head-to-heads.

To bring in a session that ran before the hooks existed, or on a machine without them: `.claude/hooks/ai-log.sh backfill ~/.claude/projects/<project-dir>/<session-id>.jsonl`.

## Steps

1. **Gather.** Run `stats.sh`. Sessions whose 8-character id doesn't appear in `docs/prompt-log.md` are new. Skip sessions with zero prompts, and skip the session you are running in right now (it gets logged next time).

2. **Read each new session** with `stats.sh <id>`. For each one, work out:
   - **Role:** planning agent, execution agent (which phase), evaluation agent, or ad-hoc (debugging, questions, docs). Use the branch name and the first prompt.
   - **What was generated:** files created or changed, tests added.
   - **Human decisions:** every AskUserQuestion. Note when the human picked something other than the option marked "(Recommended)". These overrides are the strongest evidence of thoughtful use.
   - **Verification:** builds and tests run and whether they passed, web sources checked, values cross-checked against primary sources.
   - **AI mistakes or limits:** wrong assumptions, failed tests, retries, things Claude flagged it couldn't verify, and anything it says a human must check.
   - **Boundary-pushing:** multi-agent handoffs, subagents, an agent writing instructions for another agent, live research, skills, comparisons between tools.

3. **Ask about the facts only a human knows**, and only for sessions by the person running the report (`git config user.name`). Use AskUserQuestion with multiple-choice options and your best guess first, so nobody has to type. Ask at most 4 questions per call, and batch sessions together:
   - Outcome tag: `success` / `partial` / `failure`.
   - What the human changed afterwards. Offer options drawn from the logs and git diff, such as "Nothing yet", "Fixed bugs by hand" or "Rewrote part of it".
   - Did they also use another AI tool for this work (ChatGPT, Copilot, Claude on the web)? If yes, ask for a short description in a follow-up. If BUILD_PLAN §9's head-to-head comparisons are due, ask whether this was one and which tool needed fewer fixes.

   For a teammate's sessions, fill in everything the logs show and put `_(awaiting <author>)_` in the human-only fields. When that teammate runs `/ai-report`, fill in their gaps.

4. **Append entries to `docs/prompt-log.md`**, oldest first, in BUILD_PLAN §9 format plus these fields:

   ```markdown
   ### 2026-11-12 · Phase 1 · Claude Code · @author · session `abcd1234`
   - **Role:** execution agent
   - **Prompt:** "…" (first prompt, trimmed. List follow-ups only if they changed direction)
   - **Generated:** …
   - **Human decisions:** … (AI recommended X → we chose Y because …)
   - **Verified by:** …
   - **Worked?** …
   - **Human change:** …
   - **AI limits seen:** …
   - **Tag:** success | partial | failure
   - **Head-to-head:** (optional)
   ```

   Never rewrite or delete existing entries, except to replace an `_(awaiting …)_` placeholder for the person running the report.

5. **Regenerate `docs/AI_USAGE.md`** from scratch with the structure below. Base it on the whole prompt log and the stats, not just the new sessions.

   1. **Summary.** 3–5 sentences, then a table per person: sessions, prompts, files changed, decision checkpoints, outcome tally (success/partial/failure), and AI-co-authored commits.
   2. **How we work with AI.** The agent pipeline and the human checkpoints, as actually practiced (not just as planned in BUILD_PLAN). Name any divergence from the plan.
   3. **Thoughtful: decisions we made.** A table of Decision | AI recommended | We chose | Why | Session.
   4. **Critical: what the AI got wrong or couldn't do.** A table of Issue | How we caught it | Fix | Session. Include AI errors caught by humans, things the AI caught in the source material, and failed or partial sessions.
   5. **Pushing the boundaries.** Concrete experiments with evidence: multi-agent handoffs, agents writing specs for other agents, live research into primary sources, head-to-head comparisons, evaluation harnesses.
   6. **What we don't let AI decide.** Standing limits and why (for example, hand-verified tax constants, golden test values, secrets).
   7. **Session index.** One line per session linking to its prompt-log entry.
   8. **How this was collected.** Hooks, redaction, backfills, and what isn't captured (AI tools outside Claude Code, unless someone reports them).

   Write it plainly, as the team would write it, and keep it short enough for the professor to read in five minutes. Link evidence with relative links (`prompt-log.md#…`, file paths, short commit hashes).

6. **Suggest `DECISIONS.md` items.** P4 requires a `DECISIONS.md`. If a human decision from step 3 isn't recorded there, list it for the user, but don't edit `DECISIONS.md` unless they ask.

7. **Finish.** Show the user the new prompt-log entries and a 3-line summary of what changed in AI_USAGE.md. Leave the files uncommitted unless the user asks you to commit. When committing, include `docs/ai-log/raw/` so teammates' reports can see everyone's sessions.
