#!/usr/bin/env bash
# Records Claude Code activity for the AI-usage report (see .claude/skills/ai-report).
#
# Hook mode (wired up in .claude/settings.json): reads the hook's JSON from stdin
# and appends one compact line to docs/ai-log/raw/<git-user>/<session-id>.jsonl.
#
# Backfill mode: converts an existing transcript into the same format, for
# sessions that ran before the hooks existed.
#   .claude/hooks/ai-log.sh backfill ~/.claude/projects/<project>/<session>.jsonl
#
# One file per person per session, so teammates never merge-conflict on the log.
# The hook never blocks Claude: every failure path exits 0.

set -u

ROOT="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
command -v jq >/dev/null 2>&1 || exit 0

AUTHOR="$(git -C "$ROOT" config user.name 2>/dev/null || whoami)"
AUTHOR="$(printf '%s' "$AUTHOR" | tr -c 'A-Za-z0-9._-' '_')"
LOG_DIR="$ROOT/docs/ai-log/raw/$AUTHOR"
BRANCH="$(git -C "$ROOT" branch --show-current 2>/dev/null || true)"

# Masks API keys (Anthropic sk-ant-…, Voyage pa-…, generic key=…) before anything is written.
REDACT='def redact: if type == "string" then
    gsub("sk-ant-[A-Za-z0-9_-]+"; "[REDACTED]")
  | gsub("\\bpa-[A-Za-z0-9_-]{20,}"; "[REDACTED]")
  | gsub("(?<k>(api[_-]?key|secret|token|password)\"?\\s*[:=]\\s*\"?)[^\\s\"]{8,}"; "\(.k)[REDACTED]"; "i")
  else . end;
def cut($n): if type == "string" and length > $n then .[0:$n] + "…" else . end;'

# Turns one tool call into a short summary, keeping only what the report needs.
SUMMARIZE_TOOL='def tool_summary($name; $in; $out):
  if   $name == "Edit" or $name == "Write" or $name == "MultiEdit" or $name == "NotebookEdit"
       then {file: (($in.file_path // $in.notebook_path // "") | sub("^" + $root + "/"; ""))}
  elif $name == "Bash" then {command: ($in.command | cut(300)), description: $in.description}
  elif $name == "WebSearch" then {query: $in.query}
  elif $name == "WebFetch" then {url: $in.url}
  elif $name == "Agent" or $name == "Task" then {subagent: ($in.subagent_type // "general-purpose"), task: ($in.description // ""), prompt: ($in.prompt | cut(600))}
  elif $name == "Skill" then {skill: $in.skill, args: $in.args}
  elif $name == "AskUserQuestion" then
       {questions: [$in.questions[]? | {q: .question, options: [.options[]?.label]}],
        answers: ((($out | if type == "object" then (.answers // .) else . end) // null) | tostring | cut(1500))}
  elif $name == "Read" then {file: (($in.file_path // "") | sub("^" + $root + "/"; ""))}
  else {} end;'

write_line() { # $1 = session id, stdin = one JSON line
  mkdir -p "$LOG_DIR" 2>/dev/null || return 0
  cat >> "$LOG_DIR/$1.jsonl" 2>/dev/null
}

if [ "${1:-}" = "backfill" ]; then
  TRANSCRIPT="${2:?usage: ai-log.sh backfill <transcript.jsonl>}"
  SID="$(basename "$TRANSCRIPT" .jsonl)"
  OUT="$LOG_DIR/$SID.jsonl"
  mkdir -p "$LOG_DIR"
  : > "$OUT"
  # Pair each tool_use with its tool_result so AskUserQuestion answers survive.
  jq -c -s --arg author "$AUTHOR" --arg root "$ROOT" --arg sid "$SID" "$REDACT $SUMMARIZE_TOOL"'
    ([.[] | select(.type == "user") | .message.content | arrays | .[] | select(.type == "tool_result")
      | {key: .tool_use_id, value: (.content | if type == "array" then map(.text? // "") | join("") else . end)}]
     | from_entries) as $results
    | (map(select(.type == "assistant") | .message.model) | map(select(. != null)) | first) as $model
    | .[]
    | . as $e
    | if .type == "user" and (.message.content | type) == "string" and (.isMeta | not) then
        {event: "UserPromptSubmit", prompt: (.message.content | cut(4000))}
      elif .type == "assistant" then
        (.message.content[]? |
          if .type == "tool_use" then
            {event: "PostToolUse", tool: .name} + tool_summary(.name; .input; $results[.id])
          elif .type == "text" and (.text | length) > 0 then
            {event: "AssistantText", text: (.text | cut(3000))}
          else empty end)
      else empty end
    | . + {ts: $e.timestamp, author: $author, session: $sid, branch: ($e.gitBranch // null), model: $model, backfilled: true}
    | walk(redact)
  ' "$TRANSCRIPT" >> "$OUT" || exit 1
  echo "Backfilled $(wc -l < "$OUT" | tr -d ' ') events to ${OUT#$ROOT/}"
  exit 0
fi

INPUT="$(cat)"
EVENT="$(printf '%s' "$INPUT" | jq -r '.hook_event_name // empty' 2>/dev/null)" || exit 0
SID="$(printf '%s' "$INPUT" | jq -r '.session_id // "unknown"' 2>/dev/null)"
[ -n "$EVENT" ] || exit 0

# Stop: record what Claude told the user at the end of the turn.
LAST_TEXT=""
if [ "$EVENT" = "Stop" ] || [ "$EVENT" = "SubagentStop" ]; then
  LAST_TEXT="$(printf '%s' "$INPUT" | jq -r '.last_assistant_message // empty' 2>/dev/null)"
  TRANSCRIPT="$(printf '%s' "$INPUT" | jq -r '.agent_transcript_path // .transcript_path // empty' 2>/dev/null)"
  if [ -z "$LAST_TEXT" ] && [ -f "$TRANSCRIPT" ]; then
    LAST_TEXT="$(tail -n 400 "$TRANSCRIPT" | jq -r -s '
      [.[] | select(.type == "assistant") | .message.content[]? | select(.type == "text") | .text] | last // empty' 2>/dev/null)"
  fi
fi

printf '%s' "$INPUT" | jq -c --arg author "$AUTHOR" --arg root "$ROOT" --arg branch "$BRANCH" \
  --arg last "$LAST_TEXT" --arg ts "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$REDACT $SUMMARIZE_TOOL"'
  {ts: $ts, author: $author, session: .session_id, branch: $branch, event: .hook_event_name,
   agent: (.agent_type // null)}
  + (if .hook_event_name == "UserPromptSubmit" then {prompt: (.prompt | cut(4000))}
     elif .hook_event_name == "PostToolUse" then {tool: .tool_name} + tool_summary(.tool_name; .tool_input; .tool_response)
     elif .hook_event_name == "Stop" or .hook_event_name == "SubagentStop" then {text: ($last | cut(3000))}
     elif .hook_event_name == "SessionStart" then {source: .source, model: (.model // null)}
     elif .hook_event_name == "SessionEnd" then {reason: .reason}
     else {} end)
  | with_entries(select(.value != null and .value != ""))
  | walk(redact)
' 2>/dev/null | write_line "$SID"

exit 0
