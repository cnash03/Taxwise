# Taxwise
Full Stack LLMs semester long project

# 1st presentation link
https://docs.google.com/presentation/d/1-NAtO33yZJG0BExMTAKbcu3BMdrtexmxr1D42kJGFK8/edit?slide=id.p#slide=id.p

# AI usage documentation
How we use AI is documented in [docs/AI_USAGE.md](docs/AI_USAGE.md), with session-by-session detail in [docs/prompt-log.md](docs/prompt-log.md).

- **Automatic logging:** `.claude/settings.json` hooks record every Claude Code session in this repo to `docs/ai-log/raw/<git-user>/`. You need `jq` (`brew install jq`; on Windows, `winget install jqlang.jq` plus Git Bash). Commit the raw logs along with your work.
- **Updating the report:** run `/ai-report` in Claude Code after each phase or PR. It drafts prompt-log entries and asks you multiple-choice questions for anything only you know.
- **Earlier sessions:** import them with `.claude/hooks/ai-log.sh backfill ~/.claude/projects/<this-project>/<session-id>.jsonl`.
- **Other AI tools** (ChatGPT, Copilot, …) aren't captured automatically. Mention them when `/ai-report` asks.
