// The chat panel of the live view: the agent chats that wait for the person (the files of chat-status.js), one row each
// below the jobs on a tall screen, one summary line on a short one, and the chat part of the help legend. Pure, like
// view.ts: watch.ts reads the chats and passes them in, so that the self-test draws the panel from fixed records.

import type { ScreenJob } from "./view.ts";
import { ScreenLine, type Style, displayWidth, duration, fit, fitEnd } from "./screen-line.ts";

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
  /** For a reply: whether `question` has a question mark (the heuristic; absent from older files). */
  asks?: boolean | null;
  /** The questions the chat asked the person that are still open, newest first (absent from older files). */
  openQuestions?: readonly { text: string; askedAt: number }[] | null;
  /** Who judged `openQuestions` for the wait that began at `summaryOf`: `haiku`, else the `heuristic`. */
  askVerdict?: string | null;
  /** The chat's session id: the jobs it queued carry it (absent from older files). */
  session?: string | null;
  /** The first prompt of the chat, cut to 500 characters (absent when not known). */
  firstPrompt?: string | null;
  /** A short title of the chat by Claude Haiku (absent until the first summary). */
  title?: string | null;
  /** The title Antigravity gave the conversation (absent for Claude Code); it wins over Haiku's. */
  agentTitle?: string | null;
  /** The wait Claude Haiku last judged (its `since`). */
  summaryOf?: number | null;
}

/** Whether chat-status.js makes summaries: on, or off and why (`disabled` by its variable, or no CLI `missing`). */
export interface SummaryState {
  on: boolean;
  reason?: string;
}

/** What the panel is drawn from. */
export interface ChatView {
  chats: readonly ChatRecord[];
  /** The main checkout, shown as "main"; null when it is not known. */
  mainCheckout: string | null;
  /** Compare paths in one letter case, as the queue keys its worktrees on Windows. */
  ignoreCase: boolean;
  /** The state of the summaries for the help; absent when chat-status.js could not tell. */
  summaries?: SummaryState | null;
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

/** The questions of a reply still open, newest first: the stored list, or for an older file its asking paragraph. */
export function openQuestions(chat: ChatRecord): string[] {
  if (chat.kind !== "reply") {
    return [];
  }
  if (Array.isArray(chat.openQuestions)) {
    return chat.openQuestions.map((item) => item.text).filter((text) => typeof text === "string" && text !== "");
  }
  return chat.asks === true && chat.question ? [chat.question] : [];
}

/** A reply that leaves a question open (Claude Haiku's verdict once it answered, the question mark before). */
export function asksInReply(chat: ChatRecord): boolean {
  return openQuestions(chat).length > 0;
}

/** Claude Haiku judged the chat's current wait. */
export function judgedByHaiku(chat: ChatRecord): boolean {
  return chat.askVerdict === "haiku" && chat.summaryOf === chat.since;
}

/** A waiting chat that asks the person: a tool's question, or a reply with an open question; a report does not. */
export function asksPerson(chat: ChatRecord): boolean {
  return chat.state === "waiting" && (asks(chat) || asksInReply(chat));
}

/** The mark before a row: ? for a tool's question, …? for a reply that leaves a question open. */
export function chatMark(chat: ChatRecord): string {
  return asks(chat) ? "?" : "…?";
}

/**
 * The chats that wait for an answer of the person: tool questions, then replies with an open question; the most recent
 * first. A reply that only reports is idle, not listed (chatCounts counts it).
 */
export function waitingChats(view: ChatView | null): ChatRecord[] {
  if (view === null) {
    return [];
  }
  const rank = (chat: ChatRecord): number => (asks(chat) ? 0 : 1);
  return view.chats.filter(asksPerson).sort((a, b) => rank(a) - rank(b) || b.since - a.since);
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

const isMain = (chat: ChatRecord, view: ChatView): boolean =>
  view.mainCheckout !== null &&
  pathKey(directoryOf(chat), view.ignoreCase) === pathKey(view.mainCheckout, view.ignoreCase);

/**
 * The running and waiting jobs the chat queued (the session id they carry). A job without one (another caller, an older
 * client or scheduler) counts for the chats of its worktree, except in the main checkout, where every chat would get
 * every such job.
 */
export function chatJobs(chat: ChatRecord, view: ChatView, jobs: readonly ScreenJob[]): number[] {
  const key = isMain(chat, view) ? "" : pathKey(directoryOf(chat), view.ignoreCase);
  const linked = (job: ScreenJob): boolean =>
    job.session
      ? job.session === chat.session
      : key !== "" && pathKey(job.worktree ?? job.cwd, view.ignoreCase) === key;
  return jobs.filter(linked).map((job) => job.id);
}

/** The start of the first prompt that stands for a title until Claude Haiku has given one. */
const PromptTitleLength = 24;

/**
 * Which chat a row is: its title (Antigravity's, else Haiku's; else the start of its first prompt), after its
 * worktree unless that is the main checkout; the worktree alone when neither is known.
 */
export function chatLabel(chat: ChatRecord, view: ChatView): string {
  const worktree = shortWorktree(chat, view);
  const prompt = (chat.firstPrompt ?? "").replace(/\s+/g, " ").trim();
  const name = chat.agentTitle?.trim() || chat.title?.trim() || Array.from(prompt).slice(0, PromptTitleLength).join("");
  return name === "" ? worktree : worktree === "main" ? name : `${worktree}: ${name}`;
}

/** "2 wait, 3 idle, 3 working, 1 in background": the chats by state (idle: a report), for the rule above the panel. */
export function chatCounts(view: ChatView): string {
  const count = (state: string): number => view.chats.filter((chat) => chat.state === state).length;
  const asking = view.chats.filter(asksPerson).length;
  return [
    [asking, "wait"],
    [count("waiting") - asking, "idle"],
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
/** The cells of the label column (title or worktree) on a wide screen and on a narrow one. */
const WideLabelWidth = 36;
const NarrowLabelWidth = 18;

const agentName = (agent: string): string => (agent === "antigravity" ? "agy" : agent);

/** One waiting chat: agent, worktree, how long it waits, kind, its jobs, and the question cut to the width. */
export function chatRow(
  chat: ChatRecord,
  view: ChatView,
  jobs: readonly ScreenJob[],
  columns: number,
  labelWidth: number,
  now: number,
): ScreenLine {
  const ask = asks(chat);
  const style = ask ? "warn" : "";
  const ids = chatJobs(chat, view, jobs);
  const line = new ScreenLine(columns)
    .add(fit(chatMark(chat), 3), "warn")
    .add(`${fit(agentName(chat.agent), 6)} `, ask ? "warn" : "bold")
    .add(`${fit(chatLabel(chat, view), labelWidth)} `, style)
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
  const open = openQuestions(chat);
  if (open.length > 0) {
    // The newest open question, with the count of the others; Claude Haiku's is one short question, marked with ≈
    // and kept from its start, the heuristic's is the asking paragraph, kept from its end, where the question is
    const more = open.length > 1 ? ` (+${open.length - 1})` : "";
    const text = (open[0] ?? "").replace(/\s+/g, " ").trim();
    if (judgedByHaiku(chat)) {
      line.add("≈ ", "dim");
      return line
        .add(fit(text, columns - line.width - more.length).trimEnd())
        .add(more, "dim")
        .padTo();
    }
    return line
      .add(fitEnd(text, columns - line.width - more.length).trimEnd())
      .add(more, "dim")
      .padTo();
  }
  const text = (chat.question ?? "").replace(/\s+/g, " ").trim();
  if (text === "") {
    return line.add("(no text)", "dim").padTo();
  }
  // A reply keeps its end, where the question is; a tool's question its start
  const cut = chat.kind === "reply" ? fitEnd(text, columns - line.width) : fit(text, columns - line.width);
  return line.add(cut, style).padTo();
}

/** The label in at most 20 cells, for the one-line summary. */
function shortLabel(chat: ChatRecord, view: ChatView): string {
  const label = chatLabel(chat, view);
  return displayWidth(label) <= 20 ? label : fit(label, 20);
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
      .add(`${mark}${shortLabel(chat, view)} ${duration(now - chat.since)}`, mark !== "" ? "warn" : "");
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
  const cap = columns >= 100 ? WideLabelWidth : NarrowLabelWidth;
  const labelWidth = Math.min(cap, Math.max(4, ...shown.map((chat) => displayWidth(chatLabel(chat, view)))));
  const lines = shown.map((chat) => chatRow(chat, view, jobs, columns, labelWidth, now));
  if (shown.length < waiting.length) {
    lines.push(new ScreenLine(columns).add(` +${waiting.length - shown.length} more chats wait`, "dim").padTo());
  }
  return lines;
}

/** A line of the help legend: a heading, or a sample drawn in its screen style and what it means. */
export type LegendEntry = { heading: string } | { sample: string; style: Style; text: string };

/** The chat part of the help legend, with the state of the summaries when chat-status.js told it. */
export function chatLegend(view: ChatView | null): LegendEntry[] {
  const entries: LegendEntry[] = [
    { heading: "Chats that wait for you" },
    { sample: "?", style: "warn", text: "a question or permission prompt" },
    { sample: "…?", style: "warn", text: "a reply that leaves a question open" },
    { sample: "reply", style: "dim", text: "its turn ended with a question" },
    { sample: "(+n)", style: "dim", text: "more questions still open" },
    { sample: "#n", style: "wait", text: "a job it queued (or of its worktree)" },
  ];
  const state = view?.summaries;
  if (state === undefined || state === null) {
    return entries;
  }
  // A report is idle and not listed; who decides that a reply asks
  if (state.on) {
    entries.push(
      { sample: "≈", style: "dim", text: "Claude Haiku decides what asks, writes the" },
      { sample: " ", style: "", text: "question and title (Claude Code); reports: idle" },
    );
  } else {
    const why = state.reason === "disabled" ? "GSHARP_CHAT_STATUS_SUMMARY=0" : "no Claude Code CLI";
    entries.push(
      { sample: "?", style: "dim", text: `a "?" decides what asks (${why}); reports: idle` },
      ...(state.reason === "disabled"
        ? []
        : [{ sample: " ", style: "" as const, text: "install Claude Code or set GSHARP_CHAT_STATUS_CLAUDE" }]),
    );
  }
  return entries;
}
