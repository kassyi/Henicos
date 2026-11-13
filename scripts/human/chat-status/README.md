# Chat status files

`scripts/human/chat-status.js` runs as a hook of Claude Code (`.claude/settings.json`) and Antigravity
(`.agents/hooks.json`) and keeps one JSON file per agent chat, so that a status window can show which chats wait for
the person and what they asked. It only writes the files; drawing them is the job of the live view.

## Files

| File | Responsibility |
| :- | :- |
|  | The entry: arguments, , , , ; re-exports  and the helpers the self-test uses |
|  | : reads the payload on stdin, always exits 0, picks the output the agent expects |
|  | What each Claude Code and Antigravity event means (the state to record), with no file access but the transcript read |
|  | The question of a text, the last text of a transcript read from its end, the questions and options of a choice question |
|  | The chat files: the main checkout lookup, the atomic write, removing stale files, the error log,  |
|  |  |

## Where

`<main checkout>/tmp/chat-status/`, the same directory for every worktree (the main checkout is found from the `.git`
file of the worktree, as the GPU lock and the job queue find theirs). `GSHARP_CHAT_STATUS_DIR` overrides it.

| File | Content |
| :- | :- |
| `<agent>-<session>.json` | One chat (`claude-<session_id>.json`, `antigravity-<conversationId>.json`) |
| `errors.log` | What a hook call could not do (a damaged payload, a file error); started again past 1 MiB |

A file is written to `<file>.<pid>.<time>.tmp` and renamed over the old one, so a reader sees either the old or the
new content. A chat file not changed for 12 hours is removed by the next hook call and skipped by the reader.
Claude Code removes its file at `SessionEnd`; Antigravity has no event for the end of a conversation.

## Format

```json
{
  "version": 1,
  "agent": "claude",
  "session": "<session id>",
  "cwd": "<the agent's working directory>",
  "worktree": "<git rev-parse --show-toplevel of cwd>",
  "state": "waiting",
  "kind": "question",
  "since": 1794595313821,
  "question": "Which layout?",
  "options": [
    {
      "question": "Which layout?",
      "multiSelect": false,
      "choices": [{ "label": "Wide", "description": "One line per chat" }]
    }
  ],
  "event": "PreToolUse",
  "updatedAt": 1794595313821
}
```

| Field | Meaning |
| :- | :- |
| `agent` | `claude` or `antigravity` |
| `state` | `working`, `waiting` (for the person) or `background` (Antigravity: the turn ended while background tasks run) |
| `kind` | For `waiting`: `reply` (the turn ended), `question` (a choice question), `permission` (a permission prompt), `input` (another notification); otherwise `null` |
| `since` | When the chat entered this state (ms since the epoch) |
| `question` | The question, at most 2000 characters: for `reply` the last sentence ending in `?` or `？` of the last assistant text, else its last paragraph; for `question` the question texts, one per line; for `permission` and `input` the notification text |
| `options` | For `question`: each question with its choices (`label`, and `description` when the agent gave one) and `multiSelect`; otherwise `null` |
| `event` | The hook event that wrote the file |
| `updatedAt` | When the file was written (ms since the epoch) |

## Events

| Agent | Event | Effect |
| :- | :- | :- |
| Claude Code | `UserPromptSubmit` | `working` |
| Claude Code | `Stop` | `waiting` / `reply`; the text is `last_assistant_message` when the payload has it, else the last assistant message of the main chain (not a subagent's) in `transcript_path` |
| Claude Code | `PreToolUse` on `AskUserQuestion` | `waiting` / `question` with the questions and options |
| Claude Code | `PostToolUse` | `working`, only when the chat waited for a question or a permission (the tool ran, so it was answered) |
| Claude Code | `Notification` | `waiting` / `permission` for `permission_prompt`; any other notification gives `waiting` / `input`, unless the chat already waits (then its question and `since` stay) |
| Claude Code | `SessionEnd` | the file is removed |
| Antigravity | `Stop` | `waiting` / `reply` when `fullyIdle`, else `background`; the text is the last `PLANNER_RESPONSE` of the `MODEL` with content in `transcript_full.jsonl` next to `transcriptPath`, else in `transcriptPath` |
| Antigravity | `PreInvocation` | `working`, only when the chat is not working already (it fires before every model call of a turn) |
| Antigravity | `PreToolUse` on `ask_question` | `waiting` / `question` with the questions and options |

The hook reads at most the last MiB of a transcript, starts no network request, and exits 0 on every input. For Claude
Code it prints nothing; for Antigravity it prints `{}` after `Stop` and `PreInvocation` and nothing after `PreToolUse`
(an Antigravity `allow` would skip the permission prompt).

## Reading

`require('scripts/human/chat-status.js').listChats({ directory?, now? })` returns the chats not older than 12 hours,
waiting chats first with the longest wait first, then the others. `node scripts/human/chat-status.js --list [--json]`
prints the same. `node scripts/human/chat-status.js --self-test` checks the script on synthetic payloads and transcripts.
