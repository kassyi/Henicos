// The lower panel of the live view for the agent chats: reads the chats of chat-status.js and wraps view-chats.ts as a
// ViewPanel. The panel is left out when chat-status.js cannot be loaded (an older checkout).

import { createRequire } from "node:module";
import * as path from "node:path";
import type { LegendEntry, PanelJob, ScreenLine, ViewPanel } from "../../plugin-api.ts";
import {
  chatCounts,
  chatLegend,
  chatLines,
  waitingChats,
  type ChatRecord,
  type ChatView,
  type SummaryState,
} from "./view-chats.ts";

/** The part of chat-status.js the panel uses. */
export interface ChatStatusModule {
  listChats: () => ChatRecord[];
  readMainCheckout: (root: string) => string | null;
  /** Absent from an older chat-status.js. */
  summaryStatus?: () => SummaryState;
}

/** The module at `file`, or null when it cannot be loaded. */
export function loadChatStatus(file: string): ChatStatusModule | null {
  try {
    return createRequire(import.meta.url)(file) as ChatStatusModule;
  } catch {
    return null; // no chat-status.js there: the view works without the panel
  }
}

/**
 * A reader of the chats: one call gives the ChatView, or null when the directory cannot be read (no panel until the
 * next read). `root` is the checkout whose main checkout is shown as "main".
 */
export function chatReader(module: ChatStatusModule, root: string): () => ChatView | null {
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

/** The panel of the waiting chats, drawn from what `read` returns. */
export function chatPanel(read: () => ChatView | null): ViewPanel {
  return {
    read,
    render(data: unknown, jobs: readonly PanelJob[], columns: number, rows: number, now: number): ScreenLine[] {
      const view = data as ChatView;
      return chatLines(waitingChats(view), view, jobs, columns, rows, now);
    },
    summary(data: unknown): string {
      const view = data as ChatView;
      return waitingChats(view).length === 0 ? "" : `chats: ${chatCounts(view)}`;
    },
    help(data: unknown): LegendEntry[] {
      return chatLegend(data as ChatView | null);
    },
  };
}

/** The panel for the module at `file` (relative to `configRoot`, which is also the checkout shown as "main"). */
export function panelFor(configRoot: string, file: string): ViewPanel | undefined {
  const module = loadChatStatus(path.resolve(configRoot, file));
  return module === null ? undefined : chatPanel(chatReader(module, configRoot));
}
