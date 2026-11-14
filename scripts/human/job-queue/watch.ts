// status --watch: the live view of the queue in the terminal. Asks the scheduler for its status every second, draws
// the screen of view.ts on the alternate screen, and acts on keys (select, cancel, move, set-weight). It never starts a
// scheduler; while none runs it shows "no scheduler" and keeps asking. Every 2 s it also reads the agent chats
// (listChats() of chat-status.js) for the chat panel; when that script cannot be loaded, the panel is left out.

import { createRequire } from "node:module";
import * as path from "node:path";
import { request } from "./client-connect.ts";
import { usageError } from "./client.ts";
import type { ClientMessage, Incoming, StatusReply } from "./protocol.ts";
import {
  FlashMs,
  initialViewState,
  renderScreen,
  screenJobs,
  selectedJob,
  type Style,
  type ViewState,
} from "./view.ts";
import type { ChatRecord, ChatView, SummaryState } from "./view-chats.ts";

const RefreshMs = 1000;
const ChatRefreshMs = 2000;
/** What m, w and s do, for the message on a running job. */
const Verbs: Record<string, string> = { m: "move", w: "change its slots", s: "start" };
const MessageMs = 4000;

/** What a key asks for besides the new view state: an action on the queue, a refresh, or quitting. */
export type KeyResult = { state: ViewState; action?: ClientMessage | "quit" | "refresh" };

/** Splits a chunk of terminal input into keys: escape sequences (arrows) stay whole. */
export function splitKeys(chunk: string): string[] {
  const keys: string[] = [];
  for (let i = 0; i < chunk.length; ) {
    const sequence = /^\x1b\[[0-9;]*[A-Za-z~]/.exec(chunk.slice(i));
    const key = sequence ? sequence[0] : chunk.slice(i, i + 1);
    keys.push(key);
    i += key.length;
  }
  return keys;
}

function note(state: ViewState, text: string, now: number, style: Style = "warn"): ViewState {
  return { ...state, prompt: null, message: { text, until: now + MessageMs, style } };
}

/** The view state after one key, and what it asks for. Pure, so that the self-test drives it. */
export function reduceKey(state: ViewState, key: string, reply: StatusReply | null, now: number): KeyResult {
  if (key === "\x03") {
    return { state, action: "quit" };
  }
  const jobs = screenJobs(reply);
  const prompt = state.prompt;
  if (prompt !== null) {
    if (key === "\x1b") {
      return { state: { ...state, prompt: null } };
    }
    if (prompt.kind === "cancel" || prompt.kind === "start") {
      return key === "y" || key === "Y"
        ? { state: { ...state, prompt: null }, action: { type: prompt.kind, id: prompt.id } }
        : { state: { ...state, prompt: null } };
    }
    if (/^[0-9]$/.test(key)) {
      return { state: { ...state, prompt: { ...prompt, digits: (prompt.digits + key).slice(0, 7) } } };
    }
    if (key === "\x7f" || key === "\b") {
      return { state: { ...state, prompt: { ...prompt, digits: prompt.digits.slice(0, -1) } } };
    }
    if (prompt.kind === "move" && prompt.digits === "" && (key === "f" || key === "r")) {
      const message: ClientMessage =
        key === "f" ? { type: "move", id: prompt.id, first: true } : { type: "move", id: prompt.id, reset: true };
      return { state: { ...state, prompt: null }, action: message };
    }
    if ((key === "\r" || key === "\n") && prompt.digits !== "") {
      const value = Number(prompt.digits);
      if (value <= 0) {
        return { state: note(state, "give a positive number", now) };
      }
      const message: ClientMessage =
        prompt.kind === "move"
          ? { type: "move", id: prompt.id, before: value }
          : { type: "set-weight", id: prompt.id, weight: value };
      return { state: { ...state, prompt: null }, action: message };
    }
    return { state };
  }
  // The help covers the jobs: a key that acts on them (a move, a cancel, a pause) would act on what is not on the screen
  if (state.help > 0 && key !== "?" && key !== "\x1b" && key !== "q") {
    return { state: note(state, "help: ? next page, Esc closes it", now) };
  }
  const selected = selectedJob(jobs, state);
  const index = selected === undefined ? -1 : jobs.indexOf(selected);
  const select = (at: number): KeyResult => {
    const job = jobs[Math.min(Math.max(at, 0), jobs.length - 1)];
    return { state: { ...state, selectedId: job === undefined ? null : job.id } };
  };
  switch (key) {
    case "q":
      return { state, action: "quit" };
    case "?":
      return { state: { ...state, help: state.help >= state.helpPages ? 0 : state.help + 1 } };
    case "\x1b": // closes the help first, then clears the selection
      return { state: state.help > 0 ? { ...state, help: 0 } : { ...state, selectedId: null } };
    case "r":
      return { state, action: "refresh" };
    case "a":
      return { state: { ...state, chatsHidden: !state.chatsHidden } };
    case " ": {
      // Like the space bar of a video player: no question, since both ways are harmless and undone by pressing it again
      if (reply === null) {
        return { state: note(state, "no scheduler running", now) };
      }
      const kind = reply.paused === true ? "resume" : "pause";
      return { state: { ...state, flash: { kind, until: now + FlashMs } }, action: { type: kind } };
    }
    case "\x1b[A":
    case "k":
      return select(index < 0 ? jobs.length - 1 : index - 1); // from no selection: the last job
    case "\x1b[B":
    case "j":
      return select(index + 1);
    case "\x1b[H":
    case "g":
      return select(0);
    case "\x1b[F":
    case "G":
      return select(jobs.length - 1);
  }
  if (key === "c" || key === "m" || key === "w" || key === "s") {
    if (selected === undefined) {
      return { state: note(state, "no job selected; pick one with ↑↓", now) };
    }
    if (key === "c") {
      return { state: { ...state, help: 0, prompt: { kind: "cancel", id: selected.id } } };
    }
    if (key === "s" && selected.state === "waiting") {
      // what start refuses, said before asking
      const refusal = selected.gpuTurn
        ? `#${selected.id} waits for the GPU, one job at a time; m f puts it next`
        : selected.blockedBy != null
          ? `#${selected.id} waits for #${selected.blockedBy} to end`
          : null;
      return refusal === null
        ? { state: { ...state, help: 0, prompt: { kind: "start", id: selected.id } } }
        : { state: note(state, refusal, now) };
    }
    if (selected.state !== "waiting") {
      return {
        state: note(state, `#${selected.id} already runs; only a waiting job can ${Verbs[key]}`, now),
      };
    }
    return {
      state: { ...state, help: 0, prompt: { kind: key === "m" ? "move" : "weight", id: selected.id, digits: "" } },
    };
  }
  return { state };
}

/** The line that tells what an action did, from the scheduler's reply. */
export function describeResult(message: ClientMessage, reply: Incoming | null): { text: string; style: Style } {
  if (reply === null) {
    return { text: "no scheduler running", style: "warn" };
  }
  if (reply.type === "error") {
    return { text: `scheduler: ${String(reply.message)}`, style: "late" };
  }
  if (reply.type === "cancel-result" && Array.isArray(reply.cancelled)) {
    const cancelled = reply.cancelled as number[];
    return cancelled.length > 0
      ? { text: `cancelled ${cancelled.map((id) => `#${id}`).join(" ")}`, style: "run" }
      : { text: `no job #${message.type === "cancel" ? String(message.id) : "?"}`, style: "warn" };
  }
  if (reply.type === "start-result") {
    return { text: `#${String(reply.id)} started over the pool`, style: "run" };
  }
  if (reply.type === "pause-result") {
    const paused = reply.paused === true;
    const text = paused ? "paused: nothing new starts until resume (Space)" : "resumed";
    return { text: reply.changed === true ? text : paused ? "was already paused" : "was not paused", style: "run" };
  }
  if (reply.type === "move-result") {
    const position = Number(reply.position);
    return {
      text: position > 0 ? `#${String(reply.id)} is now at position ${position}` : `#${String(reply.id)} was granted`,
      style: "run",
    };
  }
  return { text: JSON.stringify(reply), style: "warn" };
}

interface ChatStatusModule {
  listChats: () => ChatRecord[];
  readMainCheckout: (root: string) => string | null;
  /** Absent from an older chat-status.js. */
  summaryStatus?: () => SummaryState;
}

/**
 * A reader of the chats for the panel: null when chat-status.js cannot be loaded (an older checkout). A failed read
 * shows no panel until the next one.
 */
export function chatReader(): (() => ChatView | null) | null {
  let module: ChatStatusModule;
  try {
    module = createRequire(import.meta.url)("../chat-status.js") as ChatStatusModule;
  } catch {
    return null; // no chat-status.js beside the queue: the view works without the panel
  }
  const root = path.resolve(import.meta.dirname, "..", "..", "..");
  let mainCheckout: string | null = null;
  try {
    mainCheckout = module.readMainCheckout(root);
  } catch {
    mainCheckout = null; // "main" is then shown as the last part of its path
  }
  // Read once: whether the help says that summaries are on, and how to turn them on
  let summaries: SummaryState | null = null;
  try {
    summaries = module.summaryStatus?.() ?? null;
  } catch {
    summaries = null; // the help then leaves the line out
  }
  return () => {
    try {
      return { chats: module.listChats(), mainCheckout, ignoreCase: process.platform === "win32", summaries };
    } catch {
      return null; // a directory that cannot be read: no panel this time
    }
  };
}

export async function watch(port: number): Promise<void> {
  const input = process.stdin;
  const output = process.stdout;
  if (!input.isTTY || !output.isTTY) {
    usageError("status --watch needs a terminal; use status or status --json in a pipe.");
  }
  const color = process.env.NO_COLOR === undefined && output.hasColors();
  let reply: StatusReply | null = null;
  let state = initialViewState();
  const readChats = chatReader();
  let chats: ChatView | null = null;
  let chatsReadAt = 0;
  let stopped = false;
  let wake: (() => void) | null = null;

  const draw = (): void => {
    const now = Date.now();
    if (readChats !== null && now - chatsReadAt >= ChatRefreshMs) {
      chats = readChats();
      chatsReadAt = now;
    }
    const lines = renderScreen(reply, state, { columns: output.columns, rows: output.rows, now }, color, chats);
    output.write(`\x1b[H${lines.join("\x1b[K\r\n")}\x1b[K\x1b[J`);
  };
  const restore = (): void => {
    if (input.isTTY) {
      input.setRawMode(false);
    }
    output.write("\x1b[0m\x1b[?25h\x1b[?1049l");
  };
  const quit = (): void => {
    stopped = true;
    restore();
    process.exit(0);
  };
  const refresh = (): void => {
    const resolve = wake;
    wake = null;
    resolve?.();
  };

  output.write("\x1b[?1049h\x1b[?25l");
  process.on("exit", restore);
  process.on("SIGTERM", quit);
  output.on("resize", draw);
  input.setRawMode(true);
  input.setEncoding("utf8");
  input.on("data", (chunk: string) => {
    for (const key of splitKeys(chunk)) {
      const result = reduceKey(state, key, reply, Date.now());
      state = result.state;
      const action = result.action;
      if (action === "quit") {
        quit();
        return;
      }
      if (action === "refresh") {
        refresh();
      } else if (action !== undefined) {
        void request(port, action).then(
          (answer) => {
            const { text, style } = describeResult(action, answer);
            state = { ...state, message: { text, until: Date.now() + MessageMs, style } };
            refresh();
          },
          (error: unknown) => {
            const text = `request failed: ${error instanceof Error ? error.message : String(error)}`;
            state = { ...state, message: { text, until: Date.now() + MessageMs, style: "late" } };
            draw();
          },
        );
      }
    }
    draw();
    if (state.flash !== null) {
      setTimeout(draw, FlashMs + 50); // the mark is gone a second after the key, not at the next refresh
    }
  });

  while (!stopped) {
    try {
      const raw = await request(port, { type: "status" });
      reply = raw !== null && raw.type === "status" ? (raw as unknown as StatusReply) : null;
    } catch (error) {
      reply = null;
      const text = `status failed: ${error instanceof Error ? error.message : String(error)}`;
      state = { ...state, message: { text, until: Date.now() + RefreshMs * 2, style: "late" } };
    }
    draw();
    await new Promise<void>((resolve) => {
      const timer = setTimeout(refresh, RefreshMs);
      wake = () => {
        clearTimeout(timer);
        resolve();
      };
    });
  }
}
