// The chat panel of the live view: the agent chats that wait for the person (the files of chat-status.js), one row each
// below the jobs on a tall screen, one summary line on a short one. Pure, like view.ts: watch.ts reads the chats and
// passes them in, so that the self-test draws the panel from fixed records.

import type { ScreenJob } from "./view.ts";
import { ScreenLine, duration, fit, fitEnd } from "./screen-line.ts";

/** A chat as listChats() of chat-status.js returns it (the fields the panel reads). */
export interface ChatRecord {
  agent: string;
  /** The root of the chat's worktree as git spells it; the working directory when git did not know it. */
  worktree?: string | null;
  cwd?: string | null;
  state: string;
  /** For a waiting chat: reply, question, permission or input. */
  kind?: string | null;
  since: number;
  /** For a reply: the paragraph that asks (agents ask at the end of a reply), else its last paragraph. */
  question?: string | null;
  /** For a reply: whether `question` has a question mark (absent from older files). */
  asks?: boolean | null;
}

/** What the panel is drawn from. */
export interface ChatView {
  chats: readonly ChatRecord[];
  /** The main checkout, shown as "main"; null when it is not known. */
  mainCheckout: string | null;
  /** Compare paths in one letter case, as the queue keys its worktrees on Windows. */
  ignoreCase: boolean;
}

/** A path as a key: forward slashes, no trailing slash, one letter case when `ignoreCase`. */
export function pathKey(directory: string, ignoreCase: boolean): string {
  const slashed = directory.replace(/\\/g, "/").replace(/\/+$/, "");
  return ignoreCase ? slashed.toLowerCase() : slashed;
}

/** A chat that asks through a tool (a choice question, a permission) rather than ending its turn. */
export function asks(chat: ChatRecord): boolean {
  return chat.kind === "question" || chat.kind === "permission";
}

/** A turn that ended with a question in its text. */
export function asksInReply(chat: ChatRecord): boolean {
  return chat.kind === "reply" && chat.asks === true;
}

/** 0: asks through a tool, 1: a reply that asks, 2: a reply that only reports (or another wait). */
function rank(chat: ChatRecord): number {
  return asks(chat) ? 0 : asksInReply(chat) ? 1 : 2;
}

/** The mark before a row: ? for a tool's question, …? for a reply that ends in a question. */
export function chatMark(chat: ChatRecord): string {
  return asks(chat) ? "?" : asksInReply(chat) ? "…?" : "";
}

/** The chats that wait for the person: tool questions, then replies that ask, then the rest; the most recent first. */
export function waitingChats(view: ChatView | null): ChatRecord[] {
  if (view === null) {
    return [];
  }
  return view.chats.filter((chat) => chat.state === "waiting").sort((a, b) => rank(a) - rank(b) || b.since - a.since);
}

const directoryOf = (chat: ChatRecord): string => chat.worktree ?? chat.cwd ?? "";

/** The worktree in a few cells: "main" for the main checkout, else the last part of its path. */
export function shortWorktree(chat: ChatRecord, view: ChatView): string {
  const directory = directoryOf(chat);
  if (
    view.mainCheckout !== null &&
    pathKey(directory, view.ignoreCase) === pathKey(view.mainCheckout, view.ignoreCase)
  ) {
    return "main";
  }
  return (
    directory
      .split(/[\\/]/)
      .filter((part) => part !== "")
      .pop() ?? "?"
  );
}

/** The running and waiting jobs of the chat's worktree, by the worktree the scheduler keys them by (or their cwd). */
export function chatJobs(chat: ChatRecord, view: ChatView, jobs: readonly ScreenJob[]): number[] {
  const key = pathKey(directoryOf(chat), view.ignoreCase);
  return key === ""
    ? []
    : jobs.filter((job) => pathKey(job.worktree ?? job.cwd, view.ignoreCase) === key).map((job) => job.id);
}

/** "2 wait, 3 working, 1 in background": the chats by state, for the rule above the panel. */
export function chatCounts(view: ChatView): string {
  const count = (state: string): number => view.chats.filter((chat) => chat.state === state).length;
  return [
    [count("waiting"), "wait"],
    [count("working"), "working"],
    [count("background"), "in background"],
  ]
    .filter(([n]) => n !== 0)
    .map(([n, word]) => `${n} ${word}`)
    .join(", ");
}

/** A narrower screen leaves out the kind: the ? and the color still tell a question from a reply. */
const WideColumns = 80;
/** The job ids a row shows; the rest are counted. */
const MaxJobIds = 3;

const agentName = (agent: string): string => (agent === "antigravity" ? "agy" : agent);

/** One waiting chat: agent, worktree, how long it waits, kind, its jobs, and the question cut to the width. */
export function chatRow(
  chat: ChatRecord,
  view: ChatView,
  jobs: readonly ScreenJob[],
  columns: number,
  worktreeWidth: number,
  now: number,
): ScreenLine {
  const ask = asks(chat);
  const style = ask ? "warn" : "";
  const ids = chatJobs(chat, view, jobs);
  const line = new ScreenLine(columns)
    .add(fit(chatMark(chat), 3), ask || asksInReply(chat) ? "warn" : "")
    .add(`${fit(agentName(chat.agent), 6)} `, ask ? "warn" : "bold")
    .add(`${fit(shortWorktree(chat, view), worktreeWidth)} `, style)
    .add(`${duration(now - chat.since).padStart(5)} `);
  if (columns >= WideColumns) {
    line.add(`${fit(chat.kind ?? "", 10)} `, ask ? "warn" : "dim");
  }
  if (ids.length > 0) {
    const more = ids.length > MaxJobIds ? `+${ids.length - MaxJobIds} ` : "";
    line.add(
      `${ids
        .slice(0, MaxJobIds)
        .map((id) => `#${id}`)
        .join(" ")} ${more}`,
      "wait",
    );
  }
  const text = (chat.question ?? "").replace(/\s+/g, " ").trim();
  if (text === "") {
    return line.add("(no text)", "dim").padTo();
  }
  // A reply keeps its end, where the question is; a tool's question its start
  const cut = chat.kind === "reply" ? fitEnd(text, columns - line.width) : fit(text, columns - line.width);
  return line.add(cut, style).padTo();
}

/** The waiting chats in one line, for a short screen: " 3 chats wait: ?issue-1851 2m, …?main 9m, ...". */
export function chatSummary(waiting: readonly ChatRecord[], view: ChatView, columns: number, now: number): ScreenLine {
  const line = new ScreenLine(columns).add(
    ` ${waiting.length} chat${waiting.length === 1 ? "" : "s"} wait${waiting.length === 1 ? "s" : ""}:`,
    "bold",
  );
  waiting.forEach((chat, i) => {
    const mark = chatMark(chat);
    line
      .add(i === 0 ? " " : ", ")
      .add(`${mark}${shortWorktree(chat, view)} ${duration(now - chat.since)}`, mark !== "" ? "warn" : "");
  });
  return line.padTo();
}

/**
 * The lines of the panel in at most `room` lines: a row per waiting chat when they fit, else the rows that fit and a
 * line that counts the rest; one line (the summary) when `room` is 1.
 */
export function chatLines(
  waiting: readonly ChatRecord[],
  view: ChatView,
  jobs: readonly ScreenJob[],
  columns: number,
  room: number,
  now: number,
): ScreenLine[] {
  if (waiting.length === 0 || room < 1) {
    return [];
  }
  if (room === 1 && waiting.length > 1) {
    return [chatSummary(waiting, view, columns, now)];
  }
  const shown = waiting.length <= room ? waiting : waiting.slice(0, room - 1);
  const worktreeWidth = Math.min(18, Math.max(4, ...shown.map((chat) => shortWorktree(chat, view).length)));
  const lines = shown.map((chat) => chatRow(chat, view, jobs, columns, worktreeWidth, now));
  if (shown.length < waiting.length) {
    lines.push(new ScreenLine(columns).add(` +${waiting.length - shown.length} more chats wait`, "dim").padTo());
  }
  return lines;
}
