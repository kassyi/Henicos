---
trigger: model_decision
description: Read when starting a Claude chat or when asked to call another chat ("call <name>", "電話して") - the registry of chats in tmp/status, the first-reply topic confirmation, resolving a call name to a session before SendMessage.
paths: "scripts/agent/chat-registry.js, tmp/status/**"
---

# Chat Registry and Calls

Several Claude chats work on this repository at once. The registry maps each chat's call name (the name a person uses,
such as "CPU 合流" or "Claude電話") to its session reference, so that an agent can send to the right chat.
The registry is `tmp/status/registry.md` in the main checkout. It is shared by every worktree and is not committed.

## 1. Starting a chat

- A SessionStart hook (`scripts/agent/chat-registry.js --hook`, `.claude/settings.json`) adds a pending row with the chat's
  session ID and tells the chat what to do. The pending row has no call name yet.
- In the first reply, summarize the topic in one or two lines and ask the user: 「担当トピック名はこんな感じでいいですか？」
- After the user agrees, get your own reference from `ListAgents` (the first line says `This session is <name> [<ref>]`),
  then run `node scripts/agent/chat-registry.js set --session <id> --ref <ref> --name "<display name>" --call "<call name>" --topic "<topic>"`.

## 2. Calling a chat

- "〜へ電話して" means an asynchronous message (`SendMessage`), not a live conversation. The receiving chat answers when it
  next runs a tool; nothing is blocked while waiting.
- Resolve the call name first: `node scripts/agent/chat-registry.js resolve "<call name>"` prints `<display name> [<ref>]`.
  Exit 1 means no chat has that name, exit 2 means it is ambiguous. Ask the user in both cases; do not guess.
- Load `SendMessage` with ToolSearch before sending, and send to `<display name> [<ref>]`.
- Never send to a ref that is not in the registry, and never ask a chat to do work outside its topic.

## 3. Keeping it current

- A chat that changes its topic runs `set` again with the same session ID.
- A row for a chat that has ended stays until someone removes it; a send to it reports a delivery failure.
