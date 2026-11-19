// Built-in agents plugin: the chat session id of a job (`session`, from the agent's environment variables) and the lower
// panel of the live view with the chats that wait for the person (`panel`). The PreToolUse hook rules are in
// hook-io.js and hook-checks.js.
//
// Options: `sessionEnv` (variables to read, in order; default CLAUDE_CODE_SESSION_ID, ANTIGRAVITY_CONVERSATION_ID) and
// `chatStatus` (chat-status.js relative to the config folder; default scripts/human/chat-status.js).

import { PluginApiVersion, type ClientContext, type QueuePlugin, type ViewPanel } from "../../plugin-api.ts";
import { panelFor } from "./panel.ts";

export const DefaultSessionEnv: readonly string[] = ["CLAUDE_CODE_SESSION_ID", "ANTIGRAVITY_CONVERSATION_ID"];
export const DefaultChatStatus = "scripts/human/chat-status.js";

function strings(value: unknown, fallback: readonly string[]): readonly string[] {
  return Array.isArray(value) && value.every((item) => typeof item === "string") ? (value as string[]) : fallback;
}

const plugin = {
  name: "agents",
  apiVersion: PluginApiVersion,

  session(context: ClientContext): string | undefined {
    for (const name of strings(context.options.sessionEnv, DefaultSessionEnv)) {
      const value = context.env[name];
      if (value !== undefined && value !== "") {
        return value;
      }
    }
    return undefined;
  },

  panel(context: ClientContext): ViewPanel | undefined {
    const file = context.options.chatStatus;
    return panelFor(context.configRoot, typeof file === "string" && file !== "" ? file : DefaultChatStatus);
  },
} satisfies QueuePlugin;

export default plugin;
