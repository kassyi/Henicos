// The state of the live view (status --watch) that the keys change and the screen reads: the selection, the scroll
// position, a prompt being typed, the last message, the help page, the chat panel and the mark of Space.

import type { Style } from "./screen-line.ts";
import type { Flash } from "./view-flash.ts";

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
  /** The mark flashed over the job rows after Space (pause or play), until `until`; null when none. */
  flash: Flash | null;
}

export function initialViewState(): ViewState {
  return {
    selectedId: null,
    offset: 0,
    prompt: null,
    message: null,
    help: 0,
    helpPages: 2,
    chatsHidden: false,
    flash: null,
  };
}
