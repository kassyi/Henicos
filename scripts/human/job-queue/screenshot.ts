// Screenshots of the live view (job-queue.js --screenshots <dir>): the lines of renderScreen, with their ANSI styles,
// drawn as an SVG of a dark terminal, and the sample screens of the README. Everything but writeScreenshots is pure,
// and the screens come from a fixed status at a fixed time and time zone, so the images are the same on every run.

import * as fs from "node:fs";
import * as path from "node:path";
import type { StatusJob, StatusReply } from "./protocol.ts";
import { displayWidth } from "./screen-line.ts";
import { initialViewState, renderScreen, type ViewState } from "./view.ts";
import { Minute, Now, job, sampleReply } from "./view-sample.ts";

export const CellWidth = 8.4;
export const LineHeight = 18;
export const Padding = 12;
const FontSize = 14;
const Background = "#1e1e1e";
const Foreground = "#cccccc";

/** The foreground colors of the SGR codes that screen-line.ts writes (the dark theme of VS Code's terminal). */
const Colors: Record<string, string> = {
  "31": "#f14c4c",
  "32": "#0dbc79",
  "33": "#e5e510",
  "35": "#bc3fbc",
  "36": "#11a8cd",
};
const BoldYellow = "#f5f543";

interface Sgr {
  bold: boolean;
  dim: boolean;
  reverse: boolean;
  color: string;
}

const Plain: Sgr = { bold: false, dim: false, reverse: false, color: "" };

function applyCodes(sgr: Sgr, codes: string): Sgr {
  let next = { ...sgr };
  for (const code of codes.split(";")) {
    if (code === "" || code === "0") {
      next = { ...Plain };
    } else if (code === "1") {
      next.bold = true;
    } else if (code === "2") {
      next.dim = true;
    } else if (code === "7") {
      next.reverse = true;
    } else if (code in Colors) {
      next.color = code;
    }
  }
  return next;
}

const sameSgr = (a: Sgr, b: Sgr): boolean =>
  a.bold === b.bold && a.dim === b.dim && a.reverse === b.reverse && a.color === b.color;

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

export function escapeXml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/**
 * A part cut into words: runs of narrow characters between spaces, and each wide character alone, each placed at its
 * own column, so that a font whose glyphs are not exactly one or two cells wide cannot shift the columns after them,
 * and no space is left for the SVG renderer to collapse.
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
    if (width === 2 || char === " ") {
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

/** The lines of renderScreen(..., color = true) as an SVG of a terminal `columns` cells wide. */
export function terminalSvg(lines: string[], columns: number): string {
  const width = columns * CellWidth + 2 * Padding;
  const height = lines.length * LineHeight + 2 * Padding;
  const rects: string[] = [];
  const texts: string[] = [];
  lines.forEach((line, row) => {
    const top = Padding + row * LineHeight;
    const spans: string[] = [];
    let column = 0;
    for (const { text, sgr } of parseAnsi(line)) {
      const color = sgr.color === "33" && sgr.bold ? BoldYellow : (Colors[sgr.color] ?? Foreground);
      const opacity = sgr.dim ? ' fill-opacity="0.6"' : "";
      if (sgr.reverse) {
        const x = Padding + column * CellWidth;
        const w = displayWidth(text) * CellWidth;
        rects.push(
          `<rect x="${px(x)}" y="${px(top)}" width="${px(w)}" height="${LineHeight}" fill="${color}"${opacity}/>`,
        );
      }
      for (const piece of pieces(text, column)) {
        const attributes = [
          `x="${px(Padding + piece.column * CellWidth)}"`,
          `textLength="${px(piece.cells * CellWidth)}"`,
          'lengthAdjust="spacingAndGlyphs"',
          `fill="${sgr.reverse ? Background : color}"`,
          sgr.bold ? 'font-weight="bold"' : "",
          sgr.dim && !sgr.reverse ? 'fill-opacity="0.6"' : "",
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
    `<rect width="100%" height="100%" rx="6" fill="${Background}"/>`,
    ...rects,
    `<g font-family="ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', monospace" ` +
      `font-size="${FontSize}">`,
    ...texts,
    "</g>",
    "</svg>",
    "",
  ].join("\n");
}

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
  const named = (sample: StatusJob): StatusJob => ({ ...sample, command: Commands[sample.id] ?? sample.command });
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
}

export const Screenshots: Screenshot[] = [
  { file: "wide.svg", columns: 120, rows: 17, state: {} },
  { file: "small.svg", columns: 70, rows: 13, state: {} },
  { file: "selected.svg", columns: 120, rows: 17, state: { selectedId: 812 } },
  { file: "help.svg", columns: 120, rows: 17, state: { help: 1 } },
];

/** The time zone of the clock times on the screens, so that they do not follow the machine that draws them. */
const TimeZone = "Asia/Tokyo";

/** Draws the screenshots into `dir` and returns their paths. */
export function writeScreenshots(dir: string): string[] {
  const zone = process.env.TZ;
  process.env.TZ = TimeZone;
  try {
    fs.mkdirSync(dir, { recursive: true });
    return Screenshots.map(({ file, columns, rows, state }) => {
      const lines = renderScreen(
        screenshotReply(),
        { ...initialViewState(), ...state },
        { columns, rows, now: Now },
        true,
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
