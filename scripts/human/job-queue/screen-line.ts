// Styled text for the live view: the styles, the width of a string in terminal cells, and one line of the screen
// clipped at the width of the terminal.

export type Style = "" | "bold" | "dim" | "run" | "rest" | "late" | "wait" | "gpu" | "warn" | "now";

const StyleCodes: Record<Exclude<Style, "">, string> = {
  bold: "1",
  dim: "2",
  run: "32",
  rest: "2;32",
  late: "31",
  wait: "36",
  gpu: "35",
  warn: "33",
  now: "1;33",
};

/** East Asian wide and fullwidth characters take two cells in a terminal. */
function charWidth(code: number): number {
  const wide =
    (code >= 0x1100 && code <= 0x115f) ||
    (code >= 0x2e80 && code <= 0xa4cf) ||
    (code >= 0xac00 && code <= 0xd7a3) ||
    (code >= 0xf900 && code <= 0xfaff) ||
    (code >= 0xfe30 && code <= 0xfe4f) ||
    (code >= 0xff00 && code <= 0xff60) ||
    (code >= 0xffe0 && code <= 0xffe6);
  return wide ? 2 : 1;
}

export function displayWidth(text: string): number {
  let width = 0;
  for (const char of text) {
    width += charWidth(char.codePointAt(0) ?? 0);
  }
  return width;
}

/** A span of time in a few cells: "42s", "12m", "1h05m". */
export function duration(ms: number): string {
  const seconds = Math.max(0, Math.round(ms / 1000));
  if (seconds < 60) {
    return `${seconds}s`;
  }
  const minutes = Math.floor(seconds / 60);
  return minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h${String(minutes % 60).padStart(2, "0")}m`;
}

/** The text cut to `width` cells (with "…" when cut) and padded with spaces to exactly `width`. */
export function fit(text: string, width: number): string {
  if (width <= 0) {
    return "";
  }
  if (displayWidth(text) <= width) {
    return text + " ".repeat(width - displayWidth(text));
  }
  let out = "";
  let used = 0;
  for (const char of text) {
    const w = charWidth(char.codePointAt(0) ?? 0);
    if (used + w > width - 1) {
      break;
    }
    out += char;
    used += w;
  }
  return `${out}…${" ".repeat(width - 1 - used)}`;
}

/** The text cut to `width` cells from its start (with "…" first when cut), keeping its end, padded to `width`. */
export function fitEnd(text: string, width: number): string {
  if (width <= 0) {
    return "";
  }
  if (displayWidth(text) <= width) {
    return text + " ".repeat(width - displayWidth(text));
  }
  let out = "";
  let used = 0;
  for (const char of [...text].reverse()) {
    const w = charWidth(char.codePointAt(0) ?? 0);
    if (used + w > width - 1) {
      break;
    }
    out = char + out;
    used += w;
  }
  return `…${out}${" ".repeat(width - 1 - used)}`;
}

/** One line of the screen: styled parts, clipped at the width of the terminal. */
export class ScreenLine {
  readonly parts: { text: string; style: Style }[] = [];
  width = 0;
  readonly limit: number;

  constructor(limit: number) {
    this.limit = limit;
  }

  add(text: string, style: Style = ""): this {
    let kept = "";
    for (const char of text) {
      const w = charWidth(char.codePointAt(0) ?? 0);
      if (this.width + w > this.limit) {
        break;
      }
      kept += char;
      this.width += w;
    }
    if (kept !== "") {
      this.parts.push({ text: kept, style });
    }
    return this;
  }

  /** Spaces up to column `column` (or to the end of the line). */
  padTo(column = this.limit): this {
    return this.add(" ".repeat(Math.max(0, column - this.width)));
  }

  get text(): string {
    return this.parts.map((part) => part.text).join("");
  }

  /**
   * Draws `text` over the cells from `column` on, keeping the rest of the line (a wide character that it cuts becomes a
   * space). For a mark that floats over the job rows.
   */
  overlay(column: number, text: string, style: Style): this {
    const cells: { char: string; style: Style }[] = [];
    for (const part of this.parts) {
      for (const char of part.text) {
        cells.push({ char, style: part.style });
        if (charWidth(char.codePointAt(0) ?? 0) === 2) {
          cells.push({ char: "", style: part.style }); // the second cell of a wide character
        }
      }
    }
    const marks = [...text];
    marks.forEach((char, offset) => {
      cells[column + offset] = { char, style };
    });
    for (let i = 0; i < cells.length; i++) {
      // a wide character with one cell overwritten, or its second cell without it, leaves a space
      const cell = cells[i] as { char: string; style: Style };
      const wide = charWidth(cell.char.codePointAt(0) ?? 0) === 2;
      if (wide && (cells[i + 1] as { char: string } | undefined)?.char !== "") {
        cells[i] = { char: " ", style: cell.style };
      } else if (
        cell.char === "" &&
        (cells[i - 1] === undefined || charWidth(cells[i - 1]?.char.codePointAt(0) ?? 0) !== 2)
      ) {
        cells[i] = { char: " ", style: cell.style };
      }
    }
    this.parts.length = 0;
    for (const cell of cells) {
      if (cell.char === "") {
        continue;
      }
      const last = this.parts[this.parts.length - 1];
      if (last !== undefined && last.style === cell.style) {
        last.text += cell.char;
      } else {
        this.parts.push({ text: cell.char, style: cell.style });
      }
    }
    return this;
  }

  /** The line with ANSI colors (when `color`), the whole line in reverse video when `selected`. */
  render(color: boolean, selected = false): string {
    if (!color) {
      return this.text;
    }
    return this.parts
      .map(({ text, style }) => {
        const codes = [selected ? "7" : "", style === "" ? "" : StyleCodes[style]].filter((c) => c !== "").join(";");
        return codes === "" ? text : `\x1b[${codes}m${text}\x1b[0m`;
      })
      .join("");
  }
}
