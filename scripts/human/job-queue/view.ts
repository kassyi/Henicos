// The screen of the live view (status --watch): one line per job with its timeline, a header with the slots, the
// memory and the GPU, the agent chats that wait for the person (view-chats.ts), and the details of the selected job
// at the bottom. Pure: it turns a StatusReply, the chats and the view state into lines, with no I/O, so that the
// self-test checks it.

import type { StatusJob, StatusReply } from "./protocol.ts";
import { type Style, ScreenLine, displayWidth, duration, fit } from "./screen-line.ts";
import { type ChatView, type LegendEntry, chatCounts, chatLegend, chatLines, waitingChats } from "./view-chats.ts";

export { type Style, ScreenLine, displayWidth, fit };

/** What the person is typing: a confirmation, or a number for move / set-weight. */
export type Prompt =
  | { kind: "cancel"; id: number }
  | { kind: "start"; id: number }
  | { kind: "move"; id: number; digits: string }
  | { kind: "weight"; id: number; digits: string };

export interface ViewState {
  /** The selected job; kept by id across refreshes. */
  selectedId: number | null;
  /** The first job line shown, when the jobs do not fit. */
  offset: number;
  prompt: Prompt | null;
  /** The result of the last action, shown on the bottom line until `until`. */
  message: { text: string; until: number; style: Style } | null;
  /** The help page shown: 0 for none, else 1 to `helpPages`. */
  help: number;
  /** How many pages the help takes at the current width (set by renderScreen: 1 when both columns fit). */
  helpPages: number;
  /** The chat panel is hidden (key a). */
  chatsHidden: boolean;
}

export function initialViewState(): ViewState {
  return { selectedId: null, offset: 0, prompt: null, message: null, help: 0, helpPages: 2, chatsHidden: false };
}

export interface Screen {
  columns: number;
  rows: number;
  now: number;
}

/**
 * A job as one line of the screen. A --gpu job and the client it starts under the GPU lock (the GPU holder, which
 * registers the same command from the same directory) are one line with the id of the --gpu job, the one its caller
 * was told and the one a cancel stops; `holderId` is the inner job. `reason` says what a waiting job waits for.
 */
export interface ScreenJob extends StatusJob {
  holderId: number | null;
  reason: string;
}

/**
 * What a waiting job waits for, in words a person reads without the docs: "after #821" (its --after job), "GPU busy"
 * (another job has the GPU), "CPU full" (every slot in use), "mem full" (the memory pool of the test runs), or
 * "in line" (there is room, but an earlier job goes first).
 */
function waitReason(job: StatusJob, reply: StatusReply): string {
  if (job.blockedBy != null) {
    return `after #${job.blockedBy}`;
  }
  if (job.gpuTurn) {
    return "GPU busy";
  }
  if (reply.used + job.weight > reply.capacity) {
    return "CPU full";
  }
  const memory = job.memoryMb ?? 0;
  if (reply.memoryCapacity != null && reply.memoryUsed != null && reply.memoryUsed + memory > reply.memoryCapacity) {
    return "mem full";
  }
  return "in line";
}

/** The jobs in screen order: running first, then waiting in queue order; a --gpu job and its holder as one. */
export function screenJobs(reply: StatusReply | null): ScreenJob[] {
  if (reply === null) {
    return [];
  }
  const holders = reply.running.filter((job) => job.gpu && !job.gpuTurn);
  const pairs = new Map<number, StatusJob>(); // turn id -> holder
  for (const turn of reply.running.filter((job) => job.gpuTurn)) {
    const holder = holders.find(
      (h) => h.command === turn.command && h.cwd === turn.cwd && ![...pairs.values()].includes(h),
    );
    if (holder !== undefined) {
      pairs.set(turn.id, holder);
    }
  }
  const paired = [...pairs.values()];
  const running: ScreenJob[] = reply.running
    .filter((job) => !paired.includes(job))
    .map((job) => {
      const holder = pairs.get(job.id);
      return holder === undefined
        ? { ...job, gpu: job.gpu || job.gpuTurn, holderId: null, reason: "" }
        : { ...holder, id: job.id, gpu: true, gpuTurn: false, holderId: holder.id, reason: "" };
    });
  const waiting = reply.waiting.map((job) => ({
    ...job,
    gpu: job.gpu || job.gpuTurn,
    holderId: null,
    reason: waitReason(job, reply),
  }));
  return running.concat(waiting);
}

/**
 * The selected job, or none: nothing is selected at the start, after Esc, and once the selected job has ended, so that
 * a key never acts on a job the person did not pick.
 */
export function selectedJob(jobs: ScreenJob[], state: ViewState): ScreenJob | undefined {
  return state.selectedId === null ? undefined : jobs.find((job) => job.id === state.selectedId);
}

const Assignment = /^[A-Za-z_][A-Za-z0-9_]*=/;

/**
 * A command for a short column: without leading `export X=1;` steps and `X=1` assignments, redirections, the program
 * name every command starts with (dotnet, just, node), directories and the `Kassyi.GSharp.` prefix of project names.
 */
export function shortCommand(command: string): string {
  const words: string[] = [];
  let atStart = true; // at the start of a step: assignments and the program name are dropped here
  let skipStep = false; // inside an `export ...` step
  let skipTarget = false; // the file after a > redirection
  for (const word of command.trim().split(/\s+/)) {
    const separator = /^(&&|\|\||;|\|)$/.test(word) || word.endsWith(";");
    const bare = word.endsWith(";") ? word.slice(0, -1) : word;
    if (skipTarget) {
      skipTarget = false;
    } else if (skipStep || (atStart && bare === "export")) {
      skipStep = !separator;
      continue;
    } else if (/^\d*>>?(&\d)?$/.test(bare)) {
      skipTarget = !bare.includes("&");
    } else if (/^\d*>>?\S/.test(bare) || bare === "-nodeReuse:false") {
      // a redirection with its target in the same word, or a flag every agent adds
    } else if (atStart && (Assignment.test(bare) || /^(dotnet|just|node)$/.test(bare))) {
      // dropped
    } else if (!/^(&&|\|\||;|\|)$/.test(word)) {
      const name = /[\\/]/.test(bare)
        ? (bare
            .split(/[\\/]/)
            .filter((p) => p !== "")
            .pop() ?? bare)
        : bare;
      words.push(name.replace(/^Kassyi\.GSharp\./, ""));
      atStart = false;
    } else if (words.length > 0) {
      words.push(word);
    }
    if (separator) {
      atStart = true;
      if (word.length > 1 && words.length > 0 && !/^(&&||||;||)$/.test(words[words.length - 1] ?? "")) {
        words.push(";"); // the ; of a word such as "x;" or "2>&1;"
      }
    }
  }
  while (words.length > 0 && /^(&&|\|\||;|\|)$/.test(words[words.length - 1] ?? "")) {
    words.pop();
  }
  return words.length > 0 ? words.join(" ") : command.trim();
}

const clock = (time: number): string => new Date(time).toTimeString().slice(0, 5);

const finite = (value: number | null | undefined): value is number =>
  typeof value === "number" && Number.isFinite(value);

/** A running job past its typical run time. */
function isLate(job: StatusJob, now: number): boolean {
  return (
    job.state === "running" && finite(job.grantedAt) && finite(job.typicalMs) && now - job.grantedAt > job.typicalMs
  );
}

/** "72% ends ~14:36", "overrun +2m", "6m, no history", "starts ~14:35 ends ~14:38, waits: GPU busy, 1 ahead". */
export function describeProgress(job: ScreenJob, now: number): string {
  if (job.state === "running") {
    const elapsed = now - (job.grantedAt ?? now);
    if (!finite(job.typicalMs) || job.typicalMs <= 0) {
      return `${duration(elapsed)}, no history`;
    }
    if (elapsed > job.typicalMs) {
      return `${duration(elapsed)}, overrun +${duration(elapsed - job.typicalMs)}`;
    }
    const end = finite(job.endAt) ? ` ends ~${clock(job.endAt)}` : "";
    return `${Math.floor((elapsed / job.typicalMs) * 100)}%${end}`;
  }
  const times =
    finite(job.startAt) && finite(job.endAt) ? `starts ~${clock(job.startAt)} ends ~${clock(job.endAt)}, ` : "";
  return `${times}waits: ${job.reason}, ${job.ahead} ahead`;
}

/** The progress in a few cells, for a terminal too narrow for the timeline: "72% ~14:36", "14m +4m", "after #821". */
export function shortProgress(job: ScreenJob, now: number): string {
  if (job.state === "running") {
    const elapsed = now - (job.grantedAt ?? now);
    if (!finite(job.typicalMs) || job.typicalMs <= 0) {
      return `${duration(elapsed)} ?`;
    }
    if (elapsed > job.typicalMs) {
      return `${duration(elapsed)} +${duration(elapsed - job.typicalMs)}`;
    }
    return `${Math.floor((elapsed / job.typicalMs) * 100)}%${finite(job.endAt) ? ` ~${clock(job.endAt)}` : ""}`;
  }
  return finite(job.startAt) && job.blockedBy == null ? `${job.reason} ~${clock(job.startAt)}` : job.reason;
}

/** The time range the timeline shows: a quarter in the past, the rest up to the last expected end (at least 10 m). */
export interface TimeWindow {
  start: number;
  msPerCell: number;
  cells: number;
}

export function timeWindow(jobs: StatusJob[], now: number, cells: number): TimeWindow {
  const latest = jobs.reduce((last, job) => (finite(job.endAt) ? Math.max(last, job.endAt) : last), now);
  const future = Math.max(latest - now, 10 * 60 * 1000) * 1.05;
  const span = (future * 4) / 3;
  return { start: now - span / 4, msPerCell: span / cells, cells };
}

const TickMinutes = [1, 2, 5, 10, 15, 30, 60, 120, 240, 480, 1440];

/** The axis: ▼now at the column of now, which always shows, and clock labels at round local times around it. */
export function axisLine(window: TimeWindow, now: number): string {
  const cells = Array.from({ length: window.cells }, () => " ");
  const nowAt = Math.floor((now - window.start) / window.msPerCell);
  [..."▼now"].forEach((char, i) => {
    if (nowAt + i < window.cells) {
      cells[nowAt + i] = char;
    }
  });
  const minutes = TickMinutes.find((m) => (m * 60000) / window.msPerCell >= 8) ?? 1440;
  const step = minutes * 60000;
  const offset = new Date(now).getTimezoneOffset() * 60000; // local round times
  const end = window.start + window.cells * window.msPerCell;
  for (let t = Math.ceil((window.start - offset) / step) * step + offset; t < end; t += step) {
    const at = Math.floor((t - window.start) / window.msPerCell);
    const label = clock(t);
    const free = cells.slice(Math.max(0, at - 1), at + label.length + 1).every((c) => c === " ");
    if (at >= 0 && at + label.length <= window.cells && free) {
      [...label].forEach((char, i) => (cells[at + i] = char));
    }
  }
  return cells.join("");
}

/**
 * The timeline of one job: █ run so far (red past its typical time), ▓ expected rest (? without history), ▒ expected
 * run of a waiting job, ⋯ waiting for its predecessor or the GPU. The column of now is drawn in every line, │ where
 * the line is empty and ┃ over a bar, so that past (left) and expected (right) read apart. The window reaches every
 * expected end.
 */
export function timelineCells(job: StatusJob, window: TimeWindow, now: number): { char: string; style: Style }[] {
  const late = isLate(job, now);
  const cells: { char: string; style: Style }[] = [];
  for (let i = 0; i < window.cells; i++) {
    const from = window.start + i * window.msPerCell;
    const to = from + window.msPerCell;
    let cell: { char: string; style: Style } = { char: " ", style: "" };
    if (job.state === "running") {
      const started = job.grantedAt ?? now;
      const end = finite(job.endAt) ? job.endAt : now;
      if (to > started && from < now) {
        cell = { char: "█", style: late ? "late" : "run" };
      } else if (finite(job.typicalMs) && from >= now && from < end) {
        cell = { char: "▓", style: "rest" };
      } else if (!finite(job.typicalMs) && from >= now && from < now + window.msPerCell) {
        cell = { char: "?", style: "rest" }; // no recorded run time: one ? right after now, not a made-up rest
      }
    } else if (finite(job.startAt) && finite(job.endAt) && to > now) {
      if (from >= job.startAt && from < job.endAt) {
        cell = { char: "▒", style: "wait" };
      } else if (from < job.startAt && (job.blockedBy != null || job.gpuTurn)) {
        cell = { char: "⋯", style: "dim" };
      }
    }
    cells.push(cell);
  }
  const nowAt = Math.floor((now - window.start) / window.msPerCell);
  const atNow = cells[nowAt];
  if (atNow !== undefined) {
    cells[nowAt] = atNow.char === " " ? { char: "│", style: "dim" } : { char: "┃", style: "now" };
  }
  return cells;
}

function bar(used: number, capacity: number, cells: number): string {
  const filled = capacity <= 0 ? 0 : Math.min(cells, Math.round((used / capacity) * cells));
  return "■".repeat(filled) + "□".repeat(cells - filled);
}

function headerLine(reply: StatusReply | null, jobs: ScreenJob[], columns: number, now: number): ScreenLine {
  const line = new ScreenLine(columns).add(` queue ${new Date(now).toTimeString().slice(0, 8)}`, "bold");
  if (reply === null) {
    return line.add("  no scheduler (idle)", "dim").padTo();
  }
  const slots = Math.min(Math.max(reply.capacity, 1), 10);
  line
    .add("  CPU ")
    .add(bar(reply.used, reply.capacity, slots), "run")
    .add(` ${reply.used}/${reply.capacity}`);
  if (reply.memoryCapacity != null && reply.memoryUsed != null && reply.memoryCapacity > 0) {
    const percent = Math.round((reply.memoryUsed / reply.memoryCapacity) * 100);
    line
      .add("  MEM ")
      .add(bar(reply.memoryUsed, reply.memoryCapacity, 7), "wait")
      .add(` ${percent}%`);
  }
  const holder = jobs.find((job) => job.state === "running" && job.gpu);
  const turns = jobs.filter((job) => job.state === "waiting" && (job.gpu || job.gpuTurn)).length;
  line.add("  GPU ").add(holder ? `#${holder.id}` : "-", holder ? "gpu" : "dim");
  if (turns > 0) {
    line.add(` +${turns}`, "dim");
  }
  const keys = "  ?:keys";
  return line.width + keys.length <= columns ? line.padTo(columns - keys.length).add(keys, "dim") : line.padTo();
}

/** A key and what it does, for the Keys column of the help. */
const HelpKeys: [string, string][] = [
  ["↑↓ j k", "select a job"],
  ["c", "cancel it (y/n)"],
  ["s", "start it now, over the pool (y/n)"],
  ["m", "move it: f first, r reset,"],
  ["", "or an id + Enter (before that job)"],
  ["w", "its slots: a number + Enter"],
  ["a", "hide / show the waiting chats"],
  ["r", "refresh now"],
  ["Esc", "clear the selection"],
  ["?", "next help page / close"],
  ["q", "quit (also Ctrl+C)"],
];

/** A sample drawn in its screen style and what it means, for the help's legend column (the chats' part follows). */
const HelpLegend: LegendEntry[] = [
  { heading: "Timeline" },
  { sample: "█", style: "run", text: "run so far" },
  { sample: "█", style: "late", text: "past its usual run time" },
  { sample: "▓", style: "rest", text: "expected rest" },
  { sample: "?", style: "rest", text: "no run time recorded yet" },
  { sample: "▒", style: "wait", text: "expected run of a waiting job" },
  { sample: "┃", style: "now", text: "now" },
  { heading: "Waits for (left of now)" },
  { sample: "CPU full", style: "warn", text: "every slot in use" },
  { sample: "mem full", style: "warn", text: "the memory pool is full" },
  { sample: "GPU busy", style: "warn", text: "another job has the GPU" },
  { sample: "in line", style: "warn", text: "an earlier job goes first" },
  { sample: "after #n", style: "warn", text: "until job n has ended" },
];

/** A screen of this many rows shows a row per waiting chat; a shorter one sums them up in one line. */
const TallRows = 16;
/** The job rows the chats leave on a tall screen, at least, and at least half of the list (fewer with fewer jobs). */
const MinJobRows = 4;

/** The keys column fits in this many cells; the legend column starts after it. */
const HelpKeysWidth = 46;

function helpKeyLines(columns: number): ScreenLine[] {
  const lines = [new ScreenLine(columns).add(" Keys", "bold")];
  for (const [key, text] of HelpKeys) {
    lines.push(new ScreenLine(columns).add(` ${key.padEnd(9)}`, "bold").add(text));
  }
  return lines;
}

function helpLegendLine(line: ScreenLine, entry: LegendEntry): ScreenLine {
  if ("heading" in entry) {
    return line.add(entry.heading, "bold");
  }
  const width = entry.sample.length === 1 ? 2 : 10;
  return line
    .add(entry.sample, entry.style)
    .add(" ".repeat(width - entry.sample.length))
    .add(entry.text);
}

/** The legend column needs this many cells beside the keys. */
const HelpLegendWidth = 34;

/**
 * The whole help as lines: the keys and the legend (samples drawn in the colors of the screen) side by side when both
 * fit, else the keys, a blank line and the legend; the screen shows it a page of its job rows at a time.
 */
export function helpLines(columns: number, chats: ChatView | null = null): ScreenLine[] {
  const keys = helpKeyLines(columns);
  const legendEntries = [...HelpLegend, ...chatLegend(chats)];
  if (columns >= HelpKeysWidth + HelpLegendWidth) {
    const rows = Math.max(keys.length, legendEntries.length);
    return Array.from({ length: rows }, (_, i) => {
      const line = keys[i] ?? new ScreenLine(columns);
      const entry = legendEntries[i];
      line.padTo(HelpKeysWidth);
      return entry === undefined ? line : helpLegendLine(line, entry);
    });
  }
  const legend = legendEntries.map((entry) => helpLegendLine(new ScreenLine(columns).add(" "), entry));
  return [...keys, new ScreenLine(columns), ...legend];
}

function promptText(prompt: Prompt, jobs: ScreenJob[]): string {
  const job = jobs.find((j) => j.id === prompt.id);
  switch (prompt.kind) {
    case "cancel":
      return `cancel #${prompt.id}${job?.state === "running" ? " (stops its command)" : ""}? y/n`;
    case "start":
      return `start #${prompt.id} now, over the pool (it waits: ${job?.reason ?? "?"})? y/n`;
    case "move":
      return `move #${prompt.id}: f first, r reset, or id + Enter to go before it: ${prompt.digits}_  (Esc)`;
    case "weight":
      return `slots of #${prompt.id} (now ${job?.weight ?? "?"}): ${prompt.digits}_ + Enter  (Esc)`;
  }
}

const LateLegend = "  red = longer than usual";
const SelectHint = "  ↑↓ select a job";

/**
 * The queue in one line when no job is selected: "4 running, 2 waiting; next end ~00:45 #965", then what a red bar
 * means while one is drawn (the legend is otherwise only in the help), else how to select a job.
 */
function summaryLine(jobs: ScreenJob[], columns: number, now: number): ScreenLine {
  const line = new ScreenLine(columns);
  if (jobs.length === 0) {
    return line.add(" no jobs; they show up here when one is queued", "dim").padTo();
  }
  const running = jobs.filter((job) => job.state === "running");
  const late = running.some((job) => isLate(job, now));
  const next = running.filter((job) => finite(job.endAt)).sort((a, b) => (a.endAt ?? 0) - (b.endAt ?? 0))[0];
  return line
    .add(` ${running.length} running, ${jobs.length - running.length} waiting`)
    .add(next !== undefined && finite(next.endAt) ? `; next end ~${clock(next.endAt)} #${next.id}` : "")
    .add(late ? LateLegend : SelectHint, late ? "late" : "dim")
    .padTo();
}

function detailLine(job: ScreenJob | undefined, jobs: ScreenJob[], columns: number, now: number): ScreenLine {
  const line = new ScreenLine(columns);
  if (job === undefined) {
    return summaryLine(jobs, columns, now);
  }
  const flags = [
    `w${job.weight}`,
    job.memoryMb ? `${(job.memoryMb / 1024).toFixed(1)}GB` : "",
    job.gpu ? "gpu" : "",
    job.pin != null ? "pinned" : "",
    job.before != null ? `before #${job.before}` : "",
    job.after != null ? `after #${job.after}` : "",
    job.orphan ? "client-away" : "",
    job.holderId !== null ? `runs as #${job.holderId}` : "",
  ].filter((f) => f !== "");
  return line
    .add(` #${job.id} `, "bold")
    .add(describeProgress(job, now), isLate(job, now) ? "late" : "")
    .add(`  ${flags.join(" ")}  `, "dim")
    .add(job.command)
    .padTo();
}

/** The fields left of the timeline: marker, id, GPU flag, command. */
function jobLine(
  job: ScreenJob,
  selected: boolean,
  columns: number,
  idWidth: number,
  window: TimeWindow | null,
  now: number,
): ScreenLine {
  const commandWidth = window === null ? Math.max(8, columns - idWidth - 25) : Math.min(22, columns - idWidth - 14);
  const line = new ScreenLine(columns)
    .add(selected ? "▸" : " ", "bold")
    .add(String(job.id).padStart(idWidth), job.state === "running" ? "bold" : "")
    .add(job.gpu ? "G" : " ", "gpu")
    .add(" ")
    .add(fit(shortCommand(job.command), commandWidth), job.state === "running" ? "" : "dim")
    .add(" ");
  if (window === null) {
    return line.add(shortProgress(job, now), isLate(job, now) ? "late" : "").padTo();
  }
  const cells = timelineCells(job, window, now);
  // A waiting job has nothing left of now: what it waits for goes there, right before the now column
  const nowAt = Math.floor((now - window.start) / window.msPerCell);
  const reason = [...job.reason];
  const from = nowAt - reason.length - 1;
  if (job.state === "waiting" && from >= 0 && cells.slice(from, nowAt).every((cell) => cell.char === " ")) {
    reason.forEach((char, i) => (cells[from + i] = { char, style: "warn" }));
  }
  for (const cell of cells) {
    line.add(cell.char, cell.style);
  }
  return line.padTo();
}

/**
 * The whole screen as rendered lines (no line breaks): header, axis, one line per job, a rule (with "+n more" when
 * jobs are hidden, and the chats by state), the chats that wait (a row each on a screen of TallRows, else one line),
 * and the selected job's details, a prompt or a message at the bottom. Updates `state.offset` so that the selected
 * job stays visible.
 */
export function renderScreen(
  reply: StatusReply | null,
  state: ViewState,
  screen: Screen,
  color: boolean,
  chats: ChatView | null = null,
): string[] {
  const { columns, now } = screen;
  const rows = Math.max(1, screen.rows);
  const jobs = screenJobs(reply);
  const selected = selectedJob(jobs, state);
  let bottom: ScreenLine;
  if (state.prompt !== null) {
    bottom = new ScreenLine(columns).add(` ${promptText(state.prompt, jobs)}`, "warn").padTo();
  } else if (state.message !== null && state.message.until > now) {
    bottom = new ScreenLine(columns).add(` ${state.message.text}`, state.message.style).padTo();
  } else {
    bottom = detailLine(selected, jobs, columns, now);
  }
  const header = headerLine(reply, jobs, columns, now);
  if (rows < 5) {
    return [header, bottom].slice(0, rows).map((line) => line.render(color));
  }
  // The chats take their rows from the job list, which keeps at least half of them or MinJobRows (or all its jobs)
  const waiting = waitingChats(chats);
  const jobRows = Math.max(MinJobRows, Math.floor((rows - 4) / 2));
  const room = rows >= TallRows ? rows - 4 - Math.min(jobRows, Math.max(1, jobs.length)) : 1;
  const chatRows = chats === null || state.chatsHidden ? [] : chatLines(waiting, chats, jobs, columns, room, now);
  const panel = chatRows.length < rows - 4 ? chatRows : []; // the job list keeps at least one row
  const listRows = rows - 4 - panel.length;
  const idWidth = Math.max(3, ...jobs.map((job) => String(job.id).length));
  const left = 1 + idWidth + 1 + 1 + Math.min(22, columns - idWidth - 14) + 1;
  const cells = columns - left;
  const window = cells >= 12 ? timeWindow(jobs, now, cells) : null;
  const axis = new ScreenLine(columns).add(" #".padEnd(left), "dim");
  if (window !== null && jobs.length > 0) {
    axis.add(axisLine(window, now), "dim");
  }
  const lines: string[] = [header.render(color), axis.padTo().render(color)];
  // The help takes the job rows, a page at a time; ? turns the page and closes it after the last
  const help = helpLines(columns, chats);
  state.helpPages = Math.max(1, Math.ceil(help.length / listRows));
  const helpPage = Math.min(state.help, state.helpPages);
  if (helpPage > 0) {
    help
      .slice((helpPage - 1) * listRows, helpPage * listRows)
      .forEach((line) => lines.push(line.padTo().render(color)));
  } else {
    // Keep the selected job on screen; without one, keep the scroll position as far as the jobs still fill it
    const last = Math.max(0, jobs.length - listRows);
    const index = selected === undefined ? -1 : jobs.indexOf(selected);
    state.offset =
      index < 0
        ? Math.min(Math.max(state.offset, 0), last)
        : Math.min(Math.max(state.offset, index - listRows + 1, 0), index, last);
    for (const job of jobs.slice(state.offset, state.offset + listRows)) {
      lines.push(jobLine(job, job === selected, columns, idWidth, window, now).render(color, job === selected));
    }
  }
  while (lines.length < rows - 2 - panel.length) {
    lines.push(new ScreenLine(columns).padTo().render(color));
  }
  const hidden = helpPage > 0 ? 0 : jobs.length - Math.min(listRows, jobs.length);
  const chatNote =
    chats === null || waiting.length === 0
      ? ""
      : state.chatsHidden
        ? `${waiting.length} chats wait, a shows them `
        : `chats: ${chatCounts(chats)} `;
  const rule = [
    helpPage > 0 && state.helpPages > 1
      ? `help ${helpPage}/${state.helpPages}, ? next `
      : hidden > 0
        ? `+${hidden} more `
        : "",
    chatNote,
  ]
    .filter((part) => part !== "")
    .map((part) => `── ${part}`)
    .join("");
  lines.push(new ScreenLine(columns).add(rule + "─".repeat(Math.max(0, columns - rule.length)), "dim").render(color));
  panel.forEach((line) => lines.push(line.render(color)));
  lines.push(bottom.render(color));
  return lines;
}
