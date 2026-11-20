// Self-tests of the agents plugin: the session of a job and the panel of the live view (below), the chat panel of
// self-test-chats.ts, and the PreToolUse payload parsing and formatting of hook-checks.js.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { createRequire } from "node:module";
import type { AssertModule, ClientContext } from "../../plugin-api.ts";
import plugin from "./plugin.ts";
import { checkChats } from "./self-test-chats.ts";
import { chatCounts, chatLegend, chatLines, waitingChats } from "./view-chats.ts";
import { Now, sampleChats } from "../../view-sample.ts";

const require = createRequire(import.meta.url);

function context(env: NodeJS.ProcessEnv, options: Record<string, unknown> = {}, configRoot = "."): ClientContext {
  return { cwd: ".", worktree: ".", configRoot, shell: "bash", env, options, log: () => {} };
}

/** The session: the first non-empty variable of `sessionEnv` in order, from the context's env and not the process's. */
function checkSession(assert: AssertModule): void {
  const both = { CLAUDE_CODE_SESSION_ID: "claude-chat", ANTIGRAVITY_CONVERSATION_ID: "agy-chat" };
  assert.strictEqual(plugin.session(context(both)), "claude-chat", "the default order: Claude Code first");
  assert.strictEqual(plugin.session(context({ ANTIGRAVITY_CONVERSATION_ID: "agy-chat" })), "agy-chat");
  assert.strictEqual(
    plugin.session(context({ CLAUDE_CODE_SESSION_ID: "", ANTIGRAVITY_CONVERSATION_ID: "agy-chat" })),
    "agy-chat",
    "an empty value is skipped",
  );
  assert.strictEqual(plugin.session(context({})), undefined, "no variable, no session");
  assert.strictEqual(
    plugin.session(context(both, { sessionEnv: ["ANTIGRAVITY_CONVERSATION_ID", "CLAUDE_CODE_SESSION_ID"] })),
    "agy-chat",
    "the order of sessionEnv decides",
  );
  assert.strictEqual(plugin.session(context(both, { sessionEnv: ["OTHER_CHAT"] })), undefined, "only the listed names");
  // The same names are left out of the environment hash the scheduler logs
  assert.deepStrictEqual(plugin.volatileEnv(context({})), ["CLAUDE_CODE_SESSION_ID", "ANTIGRAVITY_CONVERSATION_ID"]);
  assert.deepStrictEqual(plugin.volatileEnv(context({}, { sessionEnv: ["OTHER_CHAT"] })), ["OTHER_CHAT"]);
  assert.strictEqual(
    plugin.session(context(both, { sessionEnv: "OTHER" })),
    "claude-chat",
    "a bad option: the default",
  );
  // The context's env is read, not the environment of the process
  const before = process.env.CLAUDE_CODE_SESSION_ID;
  process.env.CLAUDE_CODE_SESSION_ID = "process-chat";
  try {
    assert.strictEqual(plugin.session(context({})), undefined, "process.env is not read");
    assert.strictEqual(plugin.session(context({ CLAUDE_CODE_SESSION_ID: "given" })), "given");
  } finally {
    if (before === undefined) {
      delete process.env.CLAUDE_CODE_SESSION_ID;
    } else {
      process.env.CLAUDE_CODE_SESSION_ID = before;
    }
  }
}

/** The panel: undefined without a chat-status module, else read, render, summary and help of view-chats.ts. */
function checkPanel(assert: AssertModule): void {
  const folder = fs.mkdtempSync(path.join(os.tmpdir(), "agents-panel-"));
  try {
    assert.strictEqual(plugin.panel(context({}, {}, folder)), undefined, "no scripts/human/chat-status.js: no panel");
    assert.strictEqual(
      plugin.panel(context({}, { chatStatus: "missing.js" }, folder)),
      undefined,
      "a path without a file: no panel",
    );
    fs.writeFileSync(path.join(folder, "broken.js"), "throw new Error('not a module');");
    assert.strictEqual(plugin.panel(context({}, { chatStatus: "broken.js" }, folder)), undefined, "a throwing module");
    const chats = sampleChats();
    // A chat-status.js in CommonJS, as the real one: the records of the sample, the main checkout and no summaries.
    // The sample goes to a JSON file that the module reads, so no data is written into code.
    fs.writeFileSync(
      path.join(folder, "status-sample.json"),
      JSON.stringify({ chats: chats.chats, mainCheckout: chats.mainCheckout }),
    );
    const module = [
      'const sample = require("./status-sample.json");',
      "exports.listChats = () => sample.chats;",
      "exports.readMainCheckout = (root) => { exports.mainCheckoutOf = root; return sample.mainCheckout; };",
      "exports.summaryStatus = () => ({ on: true });",
    ];
    fs.writeFileSync(path.join(folder, "status.js"), module.join(os.EOL));
    const panel = plugin.panel(context({}, { chatStatus: "status.js" }, folder));
    assert.ok(panel !== undefined, "a readable module gives a panel");
    const loaded = require(path.join(folder, "status.js")) as { mainCheckoutOf?: string };
    assert.strictEqual(loaded.mainCheckoutOf, folder, "the main checkout is looked up from the config folder");
    const data = panel.read() as ReturnType<typeof sampleChats>;
    assert.deepStrictEqual(
      [data.chats, data.mainCheckout, data.summaries],
      [chats.chats, chats.mainCheckout, { on: true }],
    );
    const jobs = [{ id: 7, worktree: "P:/repo/tmp/worktree/issue-1835" }];
    assert.deepStrictEqual(
      panel.render(data, jobs, 120, 8, Now).map((line) => line.text),
      chatLines(waitingChats(data), data, jobs, 120, 8, Now).map((line) => line.text),
      "render draws the waiting chats",
    );
    assert.strictEqual(panel.summary(data), `chats: ${chatCounts(data)}`);
    assert.strictEqual(panel.summary({ ...data, chats: [] }), "", "no chat waits: no summary");
    assert.deepStrictEqual(panel.help(data), chatLegend(data));
    // A module whose listing fails: the read gives null, the panel is left out until the next read
    const failing = [
      "exports.listChats = () => { throw new Error('unreadable'); };",
      "exports.readMainCheckout = () => { throw new Error('unreadable'); };",
    ];
    fs.writeFileSync(path.join(folder, "failing.js"), failing.join(os.EOL));
    const failingPanel = plugin.panel(context({}, { chatStatus: "failing.js" }, folder));
    assert.strictEqual(failingPanel?.read(), null, "a listing that throws reads as nothing");
  } finally {
    fs.rmSync(folder, { recursive: true, force: true });
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  const { createEngine } = require("../../../../agent/check-queued-command.js") as {
    createEngine: unknown;
  };
  const { checks: hookChecks } = require("./hook-checks.js") as {
    checks: (assert: AssertModule, options: { createEngine: unknown }) => void;
  };
  hookChecks(assert, { createEngine });
  checkSession(assert);
  checkPanel(assert);
  checkChats();
}
