// Self-tests of the just plugin: estimate keys (`stepKeys`), plugin-less core fallback, and `shortName`.

import { createRequire } from "node:module";
import * as path from "node:path";
import { EntryScript } from "../../constants.ts";
import { loadConfig } from "../../config.ts";
import { commandKeys, type KeyContext } from "../../estimate-key.ts";
import type { AssertModule } from "../../plugin-api.ts";
import { PluginHost } from "../../plugin-host.ts";
import { shortCommand } from "../../view.ts";

const require = createRequire(import.meta.url);
const justfileReader = require("./justfile.js") as {
  justVariables(justfile: string): Map<string, string>;
  recipeParameters(justfile: string, recipe: string): string[];
};

const Windows: KeyContext = { cwd: "C:\\work\\repo\\wt\\feat-a", worktree: "c:\\work\\repo\\wt\\feat-a" };

export async function checks(assert: AssertModule): Promise<void> {
  const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
  const host = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
    ...process.env,
    JOB_QUEUE_PLUGINS: undefined,
  });
  assert.ok(
    host.plugins.some((p) => p.name === "just"),
    "just plugin loaded from repo config",
  );

  // Moved from estimate-checks.ts: just recipe keys via PluginHost
  assert.deepStrictEqual(commandKeys("just test-category Runtime", Windows, host), {
    normalized: "just test-category Runtime",
    shape: "just:test-category",
    kind: "just",
  });
  assert.deepStrictEqual(commandKeys("just --dry-run", Windows, host), {
    normalized: "just --dry-run",
    shape: "just:",
    kind: "just",
  });
  assert.deepStrictEqual(commandKeys("just build && just test", Windows, host), {
    normalized: "just build ; just test",
    shape: "just:build&just:test",
    kind: "just&just",
  });

  // Without plugins: core generic key only
  assert.deepStrictEqual(commandKeys("just test-category Runtime", Windows), {
    normalized: "just test-category Runtime",
    shape: "just",
    kind: "just",
  });

  // shortName strips leading `just` per step
  assert.strictEqual(shortCommand("just test-category Runtime", host), "test-category Runtime");
  assert.strictEqual(shortCommand("just build && just test", host), "build && test");
  assert.strictEqual(shortCommand("export CI=true; just doctest > tmp/d.log 2>&1", host), "doctest");
  assert.strictEqual(shortCommand("just", host), "just", "single `just` word is preserved");
  assert.strictEqual(shortCommand("just test-category Runtime"), "just test-category Runtime", "without plugins");
  checkJustfileValues(assert);
}

// The values just gives the top-level variables and the parameter names of a recipe header
function checkJustfileValues(assert: AssertModule): void {
  const justfile = [
    'plain := "a&b"',
    "raw := 'x\\y'",
    'export exported := "--filter Category=Stress"',
    'escaped := "say \\"hi\\""',
    'computed := plain + "c"',
    'args := "GLOBAL"',
    'test *args="":',
    "    echo {{args}}",
    "@quiet +files $env='1':",
    "    echo",
  ].join("\r\n");
  assert.deepStrictEqual(
    [...justfileReader.justVariables(justfile)],
    [
      ["plain", "a&b"],
      ["raw", "x\\y"],
      ["exported", "--filter Category=Stress"],
      ["escaped", 'say "hi"'],
      ["args", "GLOBAL"],
    ],
    "a variable assigned an expression is left out",
  );
  assert.deepStrictEqual(justfileReader.recipeParameters(justfile, "test"), ["args"]);
  assert.deepStrictEqual(justfileReader.recipeParameters(justfile, "quiet"), ["files", "env"]);
  assert.deepStrictEqual(justfileReader.recipeParameters(justfile, "absent"), []);
}
