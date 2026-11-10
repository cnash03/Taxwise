#!/usr/bin/env bash
# Prints the numbers the AI-usage report needs, from docs/ai-log/raw and git history.
#   stats.sh            → per-person totals + per-session index
#   stats.sh <session>  → that session's events, in order (prompts, tools, answers, replies)
set -eu
ROOT="$(git rev-parse --show-toplevel)"
RAW="$ROOT/docs/ai-log/raw"

if [ $# -gt 0 ]; then
  f="$(find "$RAW" -name "$1*.jsonl" | head -1)"
  [ -n "$f" ] || { echo "no session $1"; exit 1; }
  jq -r '"[\(.ts // "")] \(.event)\(if .agent then " (" + .agent + ")" else "" end) "
    + (if .prompt then "PROMPT: " + .prompt
       elif .tool then .tool + " " + ((del(.ts,.author,.session,.branch,.event,.tool,.model,.backfilled,.agent)) | tostring)
       elif .text then "SAID: " + .text
       else ((del(.ts,.author,.session,.branch,.event)) | tostring) end)' "$f"
  exit 0
fi

echo "## Per person (Claude Code, from hooks)"
cat "$RAW"/*/*.jsonl 2>/dev/null | jq -s -r '
  group_by(.author)[] |
  "- \(.[0].author): \(map(.session) | unique | length) sessions, "
  + "\(map(select(.event == "UserPromptSubmit")) | length) prompts, "
  + "\(map(select(.file and (.tool | IN("Edit","Write","MultiEdit","NotebookEdit")))) | map(.file) | unique | length) files changed, "
  + "\(map(select(.tool == "AskUserQuestion")) | length) decision checkpoints, "
  + "\(map(select(.tool == "WebFetch" or .tool == "WebSearch")) | length) web lookups, "
  + "\(map(select(.tool == "Agent" or .tool == "Task")) | length) subagents, "
  + "\(map(select(.tool == "Bash" and ((.command // "") | test("dotnet (test|build)")))) | length) build/test runs"'

echo
echo "## Sessions (newest first)"
for f in "$RAW"/*/*.jsonl; do
  [ -f "$f" ] || continue
  jq -s -r --arg f "${f#$ROOT/}" '
    (map(select(.event == "UserPromptSubmit")) | first.prompt // "(no prompt)") as $p
    | "\(map(.ts) | min // "?") | \(.[0].author) | \(.[0].session[0:8]) | branch \(map(.branch // empty) | first // "?")"
      + " | \(map(select(.event == "UserPromptSubmit")) | length) prompts"
      + "\(if .[0].backfilled then " | backfilled" else "" end) | \($p | gsub("\n"; " ") | .[0:120])"' "$f"
done | sort -r

echo
echo "## Git"
git -C "$ROOT" shortlog -sne --all | sed 's/^/- /'
echo "- commits co-authored by Claude: $(git -C "$ROOT" log --all --format=%B | grep -ci 'Co-Authored-By: Claude' || true)"

echo
echo "## Already in docs/prompt-log.md"
grep -o 'session `[0-9a-f]\{8\}`' "$ROOT/docs/prompt-log.md" 2>/dev/null | sort -u || echo "(none)"
