// The fixed status the live view is drawn from without a scheduler: by its self-test (self-test-view.ts) and by the
// screenshots of the README (screenshot.ts), and the agent chats of the chat panel. A fixed time, so that the screens
// are the same on every run.

import type { StatusJob, StatusReply } from "./protocol.ts";
import type { ChatRecord, ChatView } from "./view-chats.ts";

export const Now = Date.UTC(2026, 10, 14, 5, 32, 0);
export const Minute = 60 * 1000;

export function job(id: number, fields: Partial<StatusJob>): StatusJob {
  return {
    id,
    weight: 1,
    gpu: false,
    pid: 100 + id,
    commandPid: null,
    orphan: false,
    cwd: "P:/repo",
    command: `dotnet build project-${id}`,
    queuedAt: Now - 20 * Minute,
    grantedAt: null,
    typicalMs: 10 * Minute,
    pin: null,
    before: null,
    after: null,
    keepIfFailed: false,
    gpuTurn: false,
    memoryMb: 0,
    state: "waiting",
    ahead: 0,
    position: 0,
    startAt: null,
    endAt: null,
    blockedBy: null,
    ...fields,
  };
}

/** Three running jobs (one late, one without history) and three waiting ones (one behind --after). */
export function sampleReply(): StatusReply {
  return {
    type: "status",
    pid: 1,
    now: Now,
    policy: "hrrn",
    capacity: 8,
    used: 4,
    memoryCapacity: 32000,
    memoryUsed: 6000,
    running: [
      job(812, {
        state: "running",
        gpu: true,
        weight: 2,
        memoryMb: 6144,
        command: "just test-category Runtime",
        grantedAt: Now - 7 * Minute,
        endAt: Now + 3 * Minute,
      }),
      job(815, { state: "running", grantedAt: Now - 14 * Minute, endAt: Now + Minute }), // past its typical 10 m
      job(817, { state: "running", typicalMs: null, grantedAt: Now - 6 * Minute, endAt: Now + Minute / 2 }),
    ],
    waiting: [
      job(820, { ahead: 0, position: 1, startAt: Now + 3 * Minute, endAt: Now + 6 * Minute }),
      job(821, {
        gpu: true,
        gpuTurn: true,
        ahead: 1,
        position: 2,
        startAt: Now + 4 * Minute,
        endAt: Now + 19 * Minute,
      }),
      job(822, {
        after: 821,
        blockedBy: 821,
        ahead: 2,
        position: 3,
        startAt: Now + 19 * Minute,
        endAt: Now + 49 * Minute,
      }),
    ],
  };
}

/** The worktrees of the sample: the main checkout and two worktrees under it, spelled as git spells them. */
export const MainCheckout = "P:/repo";
export const PanelWorktree = "P:/repo/tmp/worktree/issue-1851-panel";
export const LaneWorktree = "P:/repo/tmp/worktree/issue-1835";
export const FixWorktree = "P:/repo/tmp/worktree/issue-1890";

export function chat(fields: Partial<ChatRecord>): ChatRecord {
  return {
    agent: "claude",
    worktree: MainCheckout,
    cwd: MainCheckout,
    state: "waiting",
    kind: "reply",
    since: Now,
    ...fields,
  };
}

/**
 * Four chats that wait for an answer (a choice question, a permission, a reply in the main checkout with its title
 * and two open questions judged by Claude Haiku, and a reply of an older file known by its first prompt), one idle
 * after a report, and three that do not wait, as listChats() returns them: the longest wait first. Summaries are on.
 */
export function sampleChats(): ChatView {
  return {
    mainCheckout: MainCheckout,
    ignoreCase: true,
    summaries: { on: true },
    chats: [
      chat({
        agent: "antigravity",
        since: Now - 41 * Minute,
        // as chat-status.js records a reply: the paragraph that asks, which agents put at the end
        question:
          "Both pull requests change the state file of the queue, and merging them in the wrong order would " +
          "drop the key of the round-robin;\nwhich one goes first?",
        asks: true,
        title: "Queue PR order",
        // judged by Claude Haiku: two questions open, the newest first
        askVerdict: "haiku",
        summaryOf: Now - 41 * Minute,
        openQuestions: [
          { text: "Which of the two queue PRs merges first?", askedAt: Now - 41 * Minute },
          { text: "Rebase the docs PR on main now?", askedAt: Now - 70 * Minute },
        ],
      }),
      chat({
        worktree: FixWorktree,
        since: Now - 9 * Minute,
        // an older file: no open questions recorded, the question mark of its paragraph decides
        question: "The rows fit the width now.\nShall I push the branch?",
        asks: true,
        firstPrompt: "Fix the width of the chat rows in the live view",
      }),
      chat({
        worktree: LaneWorktree,
        since: Now - 2 * Minute,
        question: "The three lanes keep running until they finish; each builds and tests through the queue.",
        asks: false,
        firstPrompt: "Run the three lanes of the M2 fixes and report when they are done",
      }),
      chat({
        worktree: PanelWorktree,
        kind: "question",
        since: Now - 6 * Minute,
        question: "Where should the chat panel go: below the jobs, or in a column beside them?",
      }),
      chat({
        worktree: LaneWorktree,
        kind: "permission",
        since: Now - 40 * 1000,
        question: "Claude needs your permission to use Bash",
      }),
      chat({ state: "working", kind: null, since: Now - 3 * Minute }),
      chat({ agent: "antigravity", state: "working", kind: null, since: Now - 2 * Minute }),
      chat({ agent: "antigravity", state: "background", kind: null, since: Now - 8 * Minute }),
    ],
  };
}
