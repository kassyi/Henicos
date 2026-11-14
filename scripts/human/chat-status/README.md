# Chat status files

`scripts/human/chat-status.js` runs as a hook of Claude Code (`.claude/settings.json`) and Antigravity
(`.agents/hooks.json`) and keeps one JSON file per agent chat, so that a status window can show which chats wait for
the person and what they asked. It only writes the files; drawing them is the job of the live view. When a chat stops,
Claude Haiku decides through the Claude Code CLI which questions it leaves open for the person (see Open questions); a
chat that only reported is idle, not waiting for an answer.

## Files

| File | Responsibility |
| :- | :- |
| `scripts/human/chat-status.js` | The entry: arguments, `--help`, `--hook`, `--list`, `--self-test`; re-exports `listChats` and the helpers the self-test uses |
| `chat-status/hook.js` | `--hook`: reads the payload on stdin, always exits 0, picks the output the agent expects |
| `chat-status/events.js` | What each Claude Code and Antigravity event means (the state to record), with no file access but the transcript read |
| `chat-status/questions.js` | The question of a text, the last text of a transcript read from its end, the questions and options of a choice question |
| `chat-status/store.js` | The chat files: the main checkout lookup, the atomic write, removing stale files, the error log, `listChats()` |
| `chat-status/open-questions.js` | The open questions of a turn by the heuristic, and what Haiku needs to judge the turn |
| `chat-status/summary.js` | Finding the Claude Code CLI, the prompt, the answer, starting the summarizer, `runSummarizer` |
| `chat-status/summarizer.js` | The summarizer process the hook starts detached |
| `chat-status/antigravity.js` | Antigravity's own files: subagent conversations, the title of a conversation |
| `chat-status/self-test.js`, `self-test-summary.js`, `self-test-antigravity.js` | `--self-test` (a fake CLI, fake Antigravity folders; never the real CLI) |

## Where

`<main checkout>/tmp/chat-status/`, the same directory for every worktree (the main checkout is found from the `.git`
file of the worktree, as the GPU lock and the job queue find theirs). `GSHARP_CHAT_STATUS_DIR` overrides it.

| File | Content |
| :- | :- |
| `<agent>-<session>.json` | One chat (`claude-<session_id>.json`, `antigravity-<conversationId>.json`) |
| `errors.log` | What a hook call or the summarizer could not do (a damaged payload, a file error, a failed CLI call); started again past 1 MiB |
| `claude-executable.txt` | The last `CLAUDE_CODE_EXECPATH` a Claude Code hook had, for the Antigravity hooks and the live view |
| `<chat file>.summary-<since>.lock` | A summarizer runs for that wait; removed when it ends |

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
  "asks": null,
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
| `question` | The question, at most 2000 characters: for `reply` the last paragraph of the last assistant text that has a `?` or `？` outside code spans and fenced blocks (agents ask at the end of a reply, after the summary), else its last paragraph, without list markers, headings, `**` and link targets, and cut from its start when longer, so that the question stays; for `question` the question texts, one per line; for `permission` and `input` the notification text |
| `asks` | For `reply`: whether `question` has a question mark; otherwise `null` |
| `options` | For `question`: each question with its choices (`label`, and `description` when the agent gave one) and `multiSelect`; otherwise `null` |
| `openQuestions` | For a chat: the questions it asked the person that are still open, newest first, at most 3, each `{ text, askedAt }` (see Open questions); kept across states |
| `askVerdict` | For `reply`: `heuristic` when the hook wrote `openQuestions`, `haiku` once Claude Haiku replaced them |
| `openBefore`, `turnPrompt` | For `reply`: the open questions before the turn and the prompt the person sent in it (null when none), the input of Haiku |
| `summaryOf` | The `since` of the wait Haiku last judged |
| `firstPrompt` | The first prompt of the chat, at most 500 characters (`UserPromptSubmit`, else the start of the transcript), without the IDE and system tags (`<ide_opened_file>`, `<ide_selection>`, `<system-reminder>`, ...) that every stored prompt loses |
| `recentPrompts` | The latest prompts, newest first, at most 3 of 300 characters (a prompt in front stays once): Haiku names the chat from them, so that a long or compacted chat is titled by its current work |
| `lastPrompt`, `promptedAt` | The end of the latest prompt (2000 characters) and when it was sent (Antigravity: the first `PreInvocation` after a wait; its text from the transcript or `lastUserInput`) |
| `stoppedAt` | When the last turn ended |
| `title` | A title by Claude Haiku (kept when a later answer has none) |
| `agentTitle` | Antigravity: the title of `annotations/<conversationId>.pbtxt` |
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

Antigravity runs its subagents as conversations that fire the same hooks. A conversation whose transcript
(`transcript_full.jsonl` next to `transcriptPath`, or under `~/.gemini/antigravity/brain/<conversationId>/`) opens
with a system message instead of the person's input, or that has no transcript yet and an annotation without a title,
is a subagent's: its file is not written (one written before is removed) and `listChats` never returns it.

The hook reads at most the last MiB of a transcript, starts no network request, and exits 0 on every input. For Claude
Code it prints nothing; for Antigravity it prints `{}` after `Stop` and `PreInvocation` and nothing after `PreToolUse`
(an Antigravity `allow` would skip the permission prompt).

## Open questions

A chat waits for the person when it asks through a tool (a choice question, a permission prompt) or when its last
reply leaves a question open. At each `Stop` the hook records at once what a heuristic says: the open questions stay
until the person sends a new prompt; after one, only the latest reply counts, by a question mark in its asking
paragraph. It then starts `chat-status/summarizer.js` detached (the hook never waits for it), for a reply, a choice
question or a permission prompt. The summarizer runs
`claude -p --model haiku --no-session-persistence --tools "" --strict-mcp-config --setting-sources ""` (the person's
subscription; no tools, MCP servers or settings, so quoted chat text can shape the answer but never act) in the system
temporary directory with `GSHARP_CHAT_STATUS_CHILD=1`, which makes the chat-status hook of
that session do nothing, at most 60 s, with the prompt on stdin. Claude Haiku gets the first prompt of the chat, the
questions open before the turn, the prompt of the turn and the latest text, each a delimited block of data, and
answers with one JSON object `{"title": "...", "open": ["...?"]}`: it drops the questions the prompt answered or the
reply settled and adds the one the reply asks, judging the meaning, not the question mark (a request to decide counts
without one; a rhetorical question, one in code or a quoted item does not; a report followed by a question counts the
question only). The summarizer writes the list (`askVerdict: haiku`) and the title only while the chat still waits in
the state it judged, so a newer state is never overwritten. A failed, late or unreadable answer (prose around the
JSON) leaves the heuristic's list, so no question is dropped because Haiku failed.

Only questions the agent asks are open. The prompt tells Haiku which blocks the person wrote (`first_prompt`,
`recent_prompts`, `turn_prompt`) and which the agent wrote (`open_before`, `latest`), and that a sentence of the person
is never an open question. Wording alone is not trusted: `groundedOpen` (`open-questions.js`) drops an answered
question that is not one open before (about the same text) and that the prompt of the turn or a recent prompt shares
more character pairs with than the latest text does (a question the latest text supports, half of its pairs, stays; so
does one that no text explains, since a paraphrase cannot be told from a mistake then). The drop is noted in
`errors.log`. The title is asked for from the current work (recent prompts, the turn prompt, the latest text); the
first prompt only tells how the chat began. `SessionStart` with `compact` is not used: the recent prompts make a
compaction irrelevant.

The CLI: `GSHARP_CHAT_STATUS_CLAUDE` names it (a `.js` file runs with node; a missing file means none), else the
`CLAUDE_CODE_EXECPATH` of the Claude Code that runs the hook (kept in `claude-executable.txt`), else that file, else
the newest `anthropic.claude-code-<version>` extension under `~/.antigravity-ide/extensions`, `~/.vscode/extensions`
or `~/.vscode-insiders/extensions` (versions compared as numbers). Without one, and with
`GSHARP_CHAT_STATUS_SUMMARY=0`, the heuristic decides alone. `summaryStatus()` tells which.

## Reading

`require('scripts/human/chat-status.js').listChats({ directory?, now?, antigravityRoot? })` returns the chats not older
than 12 hours (no subagent's), waiting chats first with the longest wait first, then the others. The chat panel of the
job queue's live view (`status --watch`) draws those that ask the person and counts the reports as idle. `node scripts/human/chat-status.js --list [--json]`
prints the same. `node scripts/human/chat-status.js --self-test` checks the script on synthetic payloads and transcripts.
