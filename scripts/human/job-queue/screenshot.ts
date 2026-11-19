// Screenshots of the live view (job-queue.js --screenshots <dir>): the lines of renderScreen, with their ANSI styles,
// drawn as an SVG of a dark terminal, and the sample screens of the README. Everything but writeScreenshots is pure,
// and the screens come from a fixed status at a fixed time and time zone, so the images are the same on every run.

import * as fs from "node:fs";
import * as path from "node:path";
import type { StatusJob, StatusReply } from "./protocol.ts";
import { displayWidth } from "./screen-line.ts";
import { initialViewState, renderScreen, type ShortNameProvider, type ViewState } from "./view.ts";
import { LaneWorktree, Minute, Now, PanelWorktree, job, samplePanel, sampleChats, sampleReply } from "./view-sample.ts";

export const CellWidth = 8.4;
export const LineHeight = 18;
export const Padding = 12;
const FontSize = 14;
/** The Windows Terminal default: the Campbell scheme in Cascadia Mono. */
export const Background = "#0c0c0c";
export const Foreground = "#cccccc";
const FontFamily =
  "'Cascadia Mono', 'Cascadia Code', Consolas, ui-monospace, SFMono-Regular, Menlo, 'DejaVu Sans Mono', monospace";

/** The 16 ANSI colors of Campbell: black, red, green, yellow, blue, purple, cyan, white, then the bright ones. */
export const Campbell = [
  "#0c0c0c",
  "#c50f1f",
  "#13a10e",
  "#c19c00",
  "#0037da",
  "#881798",
  "#3a96dd",
  "#cccccc",
  "#767676",
  "#e74856",
  "#16c60c",
  "#f9f1a5",
  "#3b78ff",
  "#b4009e",
  "#61d6d6",
  "#f2f2f2",
];

/** The color of an index of the 256-color palette: the 16 above, the 6x6x6 cube, then 24 greys. */
function indexedColor(index: number): string {
  if (index < 16) {
    return Campbell[index] ?? Foreground;
  }
  const hex = (v: number): string => v.toString(16).padStart(2, "0");
  if (index < 232) {
    const level = (n: number): number => (n === 0 ? 0 : 55 + 40 * n);
    const n = index - 16;
    return `#${hex(level(Math.floor(n / 36)))}${hex(level(Math.floor(n / 6) % 6))}${hex(level(n % 6))}`;
  }
  const grey = 8 + 10 * (index - 232);
  return `#${hex(grey)}${hex(grey)}${hex(grey)}`;
}

interface Sgr {
  bold: boolean;
  dim: boolean;
  reverse: boolean;
  /** The foreground: the index (a single digit) of one of the eight dark colors, or a #color, "" for the default. */
  color: string;
  /** The background color, "" for the default. */
  background: string;
}

const Plain: Sgr = { bold: false, dim: false, reverse: false, color: "", background: "" };

function applyCodes(sgr: Sgr, codes: string): Sgr {
  let next = { ...sgr };
  const list = codes.split(";");
  for (let i = 0; i < list.length; i++) {
    const code = list[i] ?? "";
    const number = Number(code);
    if (code === "" || code === "0") {
      next = { ...Plain };
    } else if (code === "1") {
      next.bold = true;
    } else if (code === "2") {
      next.dim = true;
    } else if (code === "7") {
      next.reverse = true;
    } else if (number >= 30 && number <= 37) {
      next.color = String(number - 30);
    } else if (number >= 90 && number <= 97) {
      next.color = Campbell[number - 90 + 8] ?? "";
    } else if (number >= 40 && number <= 47) {
      next.background = Campbell[number - 40] ?? "";
    } else if (number >= 100 && number <= 107) {
      next.background = Campbell[number - 100 + 8] ?? "";
    } else if ((number === 38 || number === 48) && list[i + 1] === "5") {
      const index = Number(list[i + 2]);
      const color = index >= 0 && index <= 255 ? indexedColor(index) : "";
      if (number === 38) {
        next.color = color;
      } else {
        next.background = color;
      }
      i += 2;
    }
  }
  return next;
}

const sameSgr = (a: Sgr, b: Sgr): boolean =>
  a.bold === b.bold &&
  a.dim === b.dim &&
  a.reverse === b.reverse &&
  a.color === b.color &&
  a.background === b.background;

/** The styled parts of one line of ANSI text, neighbours of the same style joined. */
export function parseAnsi(line: string): { text: string; sgr: Sgr }[] {
  const parts: { text: string; sgr: Sgr }[] = [];
  const push = (text: string, sgr: Sgr): void => {
    const last = parts[parts.length - 1];
    if (last !== undefined && sameSgr(last.sgr, sgr)) {
      last.text += text;
    } else if (text !== "") {
      parts.push({ text, sgr });
    }
  };
  let sgr = Plain;
  let at = 0;
  for (const match of line.matchAll(/\x1b\[([0-9;]*)m/g)) {
    push(line.slice(at, match.index), sgr);
    sgr = applyCodes(sgr, match[1] ?? "");
    at = match.index + match[0].length;
  }
  push(line.slice(at), sgr);
  return parts;
}

/**
 * The colors a part is drawn in. Like Windows Terminal's default `intenseTextStyle` ("bright"), bold turns one of the
 * eight dark colors into its bright twin and does not thicken the glyphs. Reverse video swaps the two.
 */
function paint(sgr: Sgr): { fill: string; ground: string } {
  const index = sgr.color.length === 1 ? Number(sgr.color) : -1;
  const color = index >= 0 ? (Campbell[sgr.bold ? index + 8 : index] ?? Foreground) : sgr.color || Foreground;
  const ground = sgr.background || Background;
  return sgr.reverse ? { fill: ground, ground: color } : { fill: color, ground };
}

export function escapeXml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/** The block characters drawn as shapes: full block, upper and lower half, and the three shades (dot patterns). */
const Blocks = "█▀▄░▒▓";
/** The dots of a shade in its 4x4 pixel tile: the light shade one in four, the medium a checker, the dark three. */
const ShadeDots: Record<string, [number, number][]> = {
  "░": [[0, 0]],
  "▒": [
    [0, 0],
    [2, 2],
  ],
  "▓": [
    [0, 0],
    [2, 2],
    [2, 0],
  ],
};
const DotSize = 2;
const TileSize = 4;

/**
 * A part cut into words: runs of narrow characters between spaces, and each wide character alone, each placed at its
 * own column, so that a font whose glyphs are not exactly one or two cells wide cannot shift the columns after them,
 * and no space is left for the SVG renderer to collapse. Block characters are left out: they are drawn as shapes.
 */
function pieces(text: string, column: number): { text: string; column: number; cells: number }[] {
  const out: { text: string; column: number; cells: number }[] = [];
  let run = "";
  let cells = 0;
  let at = column;
  const flush = (): void => {
    if (run !== "") {
      out.push({ text: run, column: at - cells, cells });
    }
    run = "";
    cells = 0;
  };
  for (const char of text) {
    const width = displayWidth(char);
    if (width === 2 || char === " " || Blocks.includes(char)) {
      flush();
      if (width === 2) {
        out.push({ text: char, column: at, cells: 2 });
      }
    } else {
      run += char;
      cells += width;
    }
    at += width;
  }
  flush();
  return out;
}

const px = (value: number): string => String(Number(value.toFixed(1)));

/** The runs of equal block characters of a part, as [char, first column, cells]. */
function blockRuns(text: string, column: number): [string, number, number][] {
  const runs: [string, number, number][] = [];
  let at = column;
  for (const char of text) {
    const width = displayWidth(char);
    const last = runs[runs.length - 1];
    if (Blocks.includes(char)) {
      if (last !== undefined && last[0] === char && last[1] + last[2] === at) {
        last[2] += width;
      } else {
        runs.push([char, at, width]);
      }
    }
    at += width;
  }
  return runs;
}

/** The lines of renderScreen(..., color = true) as an SVG of a terminal `columns` cells wide. */
export function terminalSvg(lines: string[], columns: number): string {
  const width = columns * CellWidth + 2 * Padding;
  const height = lines.length * LineHeight + 2 * Padding;
  const rects: string[] = [];
  const patterns: string[] = [];
  const texts: string[] = [];
  lines.forEach((line, row) => {
    const top = Padding + row * LineHeight;
    const spans: string[] = [];
    let column = 0;
    for (const { text, sgr } of parseAnsi(line)) {
      const { fill, ground } = paint(sgr);
      const opacity = sgr.dim ? ' fill-opacity="0.6"' : "";
      if (ground !== Background) {
        const w = displayWidth(text) * CellWidth;
        rects.push(
          `<rect x="${px(Padding + column * CellWidth)}" y="${px(top)}" width="${px(w)}" height="${LineHeight}" fill="${ground}"/>`,
        );
      }
      // Blocks are shapes that fill their cells, so that rows and neighbours touch as in the terminal
      for (const [char, first, cells] of blockRuns(text, column)) {
        const left = px(Padding + first * CellWidth);
        const size = `width="${px(cells * CellWidth)}"`;
        if (char === "█") {
          rects.push(`<rect x="${left}" y="${px(top)}" ${size} height="${LineHeight}" fill="${fill}"${opacity}/>`);
        } else if (char === "▀" || char === "▄") {
          const y = char === "▀" ? top : top + LineHeight / 2;
          rects.push(`<rect x="${left}" y="${px(y)}" ${size} height="${LineHeight / 2}" fill="${fill}"${opacity}/>`);
        } else {
          const id = `shade${patterns.length}`;
          const dots = (ShadeDots[char] ?? [])
            .map(([dx, dy]) => `<rect x="${dx}" y="${dy}" width="${DotSize}" height="${DotSize}"/>`)
            .join("");
          patterns.push(
            `<pattern id="${id}" x="${left}" y="${px(top)}" width="${TileSize}" height="${TileSize}" ` +
              `patternUnits="userSpaceOnUse" fill="${fill}"${opacity}>${dots}</pattern>`,
          );
          rects.push(`<rect x="${left}" y="${px(top)}" ${size} height="${LineHeight}" fill="url(#${id})"/>`);
        }
      }
      for (const piece of pieces(text, column)) {
        const attributes = [
          `x="${px(Padding + piece.column * CellWidth)}"`,
          `textLength="${px(piece.cells * CellWidth)}"`,
          'lengthAdjust="spacingAndGlyphs"',
          `fill="${fill}"`,
          sgr.dim ? 'fill-opacity="0.6"' : "",
        ].filter((a) => a !== "");
        spans.push(`<tspan ${attributes.join(" ")}>${escapeXml(piece.text)}</tspan>`);
      }
      column += displayWidth(text);
    }
    if (spans.length > 0) {
      texts.push(`<text y="${px(top + LineHeight * 0.75)}">${spans.join("")}</text>`);
    }
  });
  return [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${px(width)}" height="${px(height)}" ` +
      `viewBox="0 0 ${px(width)} ${px(height)}">`,
    ...(patterns.length > 0 ? ["<defs>", ...patterns, "</defs>"] : []),
    `<rect width="100%" height="100%" rx="6" fill="${Background}"/>`,
    '<g shape-rendering="crispEdges">',
    ...rects,
    "</g>",
    `<g font-family="${FontFamily}" font-size="${FontSize}">`,
    ...texts,
    "</g>",
    "</svg>",
    "",
  ].join("\n");
}

/** The worktrees of some jobs, as the scheduler keys them on Windows (lower case), for the chat panel's job ids. */
const Worktrees: Record<number, string> = {
  818: PanelWorktree.toLowerCase(),
  821: LaneWorktree.toLowerCase(),
  822: LaneWorktree.toLowerCase(),
};

/** Commands as the jobs of the self-test's sample would have them on a real queue. */
const Commands: Record<number, string> = {
  815: "dotnet build src/Compiler/Kassyi.GSharp.Compiler",
  817: "just doctest",
  820: "dotnet build tools/fuzz-diff",
  821: "just test-differential-cuda",
  822: "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda",
};

/**
 * The sample of the README: the status of the self-test (a late job, one without history, a job waiting for the GPU
 * and one --after it) with the GPU job as a pair (the --gpu job and its holder under the GPU lock), a test run, and
 * waiting jobs held by each of the other reasons: the slots, the memory pool and the order.
 */
export function screenshotReply(): StatusReply {
  const base = sampleReply();
  const named = (sample: StatusJob): StatusJob => ({
    ...sample,
    command: Commands[sample.id] ?? sample.command,
    worktree: Worktrees[sample.id] ?? sample.worktree,
  });
  const runtime = "just test-category Runtime";
  return {
    ...base,
    capacity: 8,
    used: 5,
    memoryCapacity: 32000,
    memoryUsed: 10240,
    running: [
      job(812, { state: "running", gpuTurn: true, weight: 0, command: runtime, grantedAt: Now - 7 * Minute }),
      job(813, {
        state: "running",
        gpu: true,
        weight: 2,
        memoryMb: 6144,
        command: runtime,
        grantedAt: Now - 7 * Minute,
        endAt: Now + 3 * Minute,
      }),
      ...base.running.slice(1).map(named),
      job(818, {
        state: "running",
        worktree: Worktrees[818],
        memoryMb: 4096,
        command: "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu -c Release",
        typicalMs: 12 * Minute,
        grantedAt: Now - 2 * Minute,
        endAt: Now + 10 * Minute,
      }),
    ],
    waiting: [
      ...base.waiting.map(named),
      job(823, {
        weight: 4,
        command: "dotnet build Henicos.slnx -c Release",
        ahead: 3,
        position: 4,
        startAt: Now + 3 * Minute,
        endAt: Now + 11 * Minute,
      }),
      job(824, {
        memoryMb: 24000,
        command: "just test-differential-cpu",
        ahead: 4,
        position: 5,
        startAt: Now + 10 * Minute,
        endAt: Now + 22 * Minute,
      }),
      job(825, {
        command: "node scripts/human/fuzz-differential.js --minutes 20",
        ahead: 5,
        position: 6,
        startAt: Now + 11 * Minute,
        endAt: Now + 31 * Minute,
      }),
    ],
  };
}

export interface Screenshot {
  file: string;
  columns: number;
  rows: number;
  state: Partial<ViewState>;
  /** The queue is paused (pause) in this sample. */
  paused?: boolean;
}

export const Screenshots: Screenshot[] = [
  { file: "wide.svg", columns: 120, rows: 17, state: {} },
  { file: "small.svg", columns: 70, rows: 13, state: {} },
  { file: "selected.svg", columns: 120, rows: 17, state: { selectedId: 812 } },
  { file: "help.svg", columns: 120, rows: 17, state: { help: 1 } },
  { file: "chats-hidden.svg", columns: 120, rows: 17, state: { panelHidden: true } },
  { file: "paused.svg", columns: 120, rows: 17, state: { flash: { kind: "pause", until: Now + 1000 } }, paused: true },
  // just resumed: the play mark, and nothing of the pause in the header
  { file: "resumed.svg", columns: 120, rows: 17, state: { flash: { kind: "resume", until: Now + 1000 } } },
];

/** The time zone of the clock times on the screens, so that they do not follow the machine that draws them. */
const TimeZone = "Asia/Tokyo";

/** Draws the screenshots into `dir` and returns their paths. */
export function writeScreenshots(dir: string, plugins?: ShortNameProvider): string[] {
  const zone = process.env.TZ;
  process.env.TZ = TimeZone;
  try {
    fs.mkdirSync(dir, { recursive: true });
    return Screenshots.map(({ file, columns, rows, state, paused }) => {
      const lines = renderScreen(
        paused ? { ...screenshotReply(), paused: true, pausedAt: Now - 4 * Minute } : screenshotReply(),
        { ...initialViewState(), ...state },
        { columns, rows, now: Now },
        true,
        samplePanel(sampleChats()),
        plugins,
      );
      const target = path.join(dir, file);
      fs.writeFileSync(target, terminalSvg(lines, columns));
      return target;
    });
  } finally {
    if (zone === undefined) {
      delete process.env.TZ;
    } else {
      process.env.TZ = zone;
    }
  }
}
