// The big mark that the live view (status --watch) flashes over the job rows for a second after Space: two bars for a
// pause, a right-pointing triangle for a play, drawn with block characters as the timeline is. Pure, like view.ts.

import type { ScreenLine, Style } from "./screen-line.ts";

/** Which mark is flashed, and until when (ms since epoch). */
export interface Flash {
  kind: "pause" | "resume";
  until: number;
}

/** How long the mark of Space stays on the screen. */
export const FlashMs = 1000;

const FlashMarks: Record<Flash["kind"], { rows: string[]; style: Style }> = {
  pause: {
    rows: Array.from({ length: 5 }, () => "████   ████"),
    style: "now",
  },
  resume: {
    rows: ["██        ", "██████    ", "██████████", "██████    ", "██        "],
    style: "run",
  },
};
/** The mark's rows are this tall and it needs this many rows of the job area and columns to be drawn. */
const FlashRows = 5;
const FlashMinColumns = 24;

/**
 * Draws the mark over the middle of the job area's lines, while it lasts and when the area is large enough for it
 * (the caller leaves it out over the help).
 */
export function overlayFlash(area: { line: ScreenLine }[], flash: Flash | null, now: number, columns: number): void {
  if (flash === null || flash.until <= now || area.length < FlashRows + 2 || columns < FlashMinColumns) {
    return;
  }
  const mark = FlashMarks[flash.kind];
  const top = Math.floor((area.length - FlashRows) / 2);
  const width = (mark.rows[0] as string).length + 4; // two cells of space around the mark
  const left = Math.floor((columns - width) / 2);
  mark.rows.forEach((row, offset) => {
    (area[top + offset] as { line: ScreenLine }).line.overlay(left, `  ${row.padEnd(width - 4)}  `, mark.style);
  });
}
