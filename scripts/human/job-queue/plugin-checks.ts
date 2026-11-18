// Checks of the config loader, the plugin host, and the scheduler's optional memoryMb / keys fields.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import {
  CpusVariable,
  EntryScript,
  GpuLockHeldVariable,
  InGpuLockVariable,
  JobVariable,
  ProtocolVersion,
  ShellVariable,
  StateDirectoryVariable,
  TurnVariable,
  UsageExitCode,
} from "./constants.ts";
import { connect, request } from "./client-connect.ts";
import { loadConfig } from "./config.ts";
import { RunHistory } from "./history.ts";
import type { ConfigFileModule } from "./lib/types.ts";
import { PluginApiVersion, type QueuePlugin, type ShareMode } from "./plugin-api.ts";
import { PluginHost, type BaseClientContext } from "./plugin-host.ts";
import { QueuedRun } from "./client-queued-run.ts";
import type { RunHost } from "./client-run-host.ts";
import type { RunOptions } from "./client.ts";
import type { ShellResult } from "./shell.ts";
import { JobServer } from "./server.ts";
import { ProcessTable } from "./system.ts";
import {
  connectRaw,
  delay,
  firstTestPort,
  registration,
  removeDirectory,
  type RawClient,
} from "./self-test-support.ts";

type AssertModule = typeof Assert;

const require = createRequire(import.meta.url);
const { findConfigFile, loadConfigFile } = require("./lib/config-file.js") as ConfigFileModule;

function baseContext(configRoot: string, warnings: string[]): BaseClientContext {
  return {
    cwd: configRoot,
    worktree: configRoot,
    configRoot,
    shell: "bash",
    env: {},
    log: (text: string) => warnings.push(`[job-queue] ${text}`),
  };
}

/** Runs `body` with a temporary `JOB_QUEUE_CONFIG` value, restoring the original afterwards. */
async function withConfigEnv(value: string | undefined, body: () => void | Promise<void>): Promise<void> {
  const previous = process.env.JOB_QUEUE_CONFIG;
  if (value === undefined) {
    delete process.env.JOB_QUEUE_CONFIG;
  } else {
    process.env.JOB_QUEUE_CONFIG = value;
  }
  try {
    await body();
  } finally {
    if (previous === undefined) {
      delete process.env.JOB_QUEUE_CONFIG;
    } else {
      process.env.JOB_QUEUE_CONFIG = previous;
    }
  }
}

/**
 * Check 1: Config discovery, missing defaults, invalid JSON mode policy (run exits 2, status warns and continues),
 * and unknown keys (warn and ignore).
 */
async function checkConfig(assert: AssertModule): Promise<void> {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-config-check-"));
  const previousCwd = process.cwd();
  try {
    const emptyTree = path.join(tempDir, "no-config", "a", "b");
    const cwdTrapDir = path.join(tempDir, "cwd-trap");
    fs.mkdirSync(emptyTree, { recursive: true });
    fs.mkdirSync(cwdTrapDir, { recursive: true });
    // A broken config in cwd must NOT be picked up when searching from entry directory
    fs.writeFileSync(path.join(cwdTrapDir, "job-queue.config.json"), "{broken-cwd");
    process.chdir(cwdTrapDir);

    await withConfigEnv(undefined, () => {
      const missingUpward = loadConfig({ mode: "run", fromDirectory: emptyTree, warn: () => {} });
      assert.strictEqual(missingUpward.status, "missing");
      assert.strictEqual(missingUpward.path, null);
      assert.deepStrictEqual(missingUpward.config.plugins, []);
      assert.strictEqual(missingUpward.config.resources?.gpu?.slots, 1);

      const missingFile = path.join(tempDir, "does-not-exist.json");
      assert.strictEqual(findConfigFile(emptyTree, missingFile), null, "missing override returns null");
      const missingOverride = loadConfig({
        mode: "run",
        configPath: missingFile,
        fromDirectory: emptyTree,
        warn: () => {},
      });
      assert.strictEqual(missingOverride.status, "missing", "missing override falls back to defaults");
      assert.strictEqual(missingOverride.config.version, 1);
    });

    // Real repo baseline config check
    await withConfigEnv(undefined, () => {
      const repoWarnings: string[] = [];
      const repoConfig = loadConfig({
        mode: "status",
        fromDirectory: path.dirname(EntryScript),
        warn: (msg) => repoWarnings.push(msg),
      });
      assert.strictEqual(repoConfig.status, "ok", "repo job-queue.config.json must load cleanly");
      assert.deepStrictEqual(repoWarnings, [], "repo job-queue.config.json must have no warnings");
      assert.deepStrictEqual(repoConfig.config.plugins, []);
      assert.strictEqual(repoConfig.config.resources?.gpu?.slots, 1);
      assert.strictEqual(repoConfig.config.resources?.gpu?.lock?.seats.length, 1);
      assert.strictEqual(repoConfig.config.resources?.gpu?.probe, undefined);
      assert.strictEqual(repoConfig.config.legacyClients, undefined);
    });

    // Unknown keys warn and ignore; path normalized via realpathSync.native
    const unknownConfigPath = path.join(tempDir, "unknown.config.json");
    fs.writeFileSync(
      unknownConfigPath,
      JSON.stringify({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        futureTopLevelFlag: true,
        pools: { coresPerSlot: 4, futurePoolKnob: 99 },
      }),
    );
    const unknownWarnings: string[] = [];
    const unknownLoaded = loadConfig({
      mode: "run",
      configPath: unknownConfigPath,
      warn: (msg) => unknownWarnings.push(msg),
    });
    assert.strictEqual(unknownLoaded.status, "ok");
    assert.strictEqual(
      unknownLoaded.path,
      fs.realpathSync.native(unknownConfigPath),
      "config path normalized with realpathSync.native",
    );
    assert.strictEqual(unknownLoaded.config.pools?.coresPerSlot, 4);
    assert.ok(
      unknownWarnings.some((w) => w.includes("futureTopLevelFlag")) &&
        unknownWarnings.some((w) => w.includes("futurePoolKnob")),
      "unknown keys produce warnings",
    );

    // Invalid resource name, seats, slots checks
    const badResourcePath = path.join(tempDir, "bad-res.config.json");
    fs.writeFileSync(
      badResourcePath,
      JSON.stringify({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        resources: { GPU: { slots: 1 } },
      }),
    );
    assert.strictEqual(loadConfigFile(tempDir, badResourcePath).status, "ok");
    const badResWarnings: string[] = [];
    const badRes = loadConfig({ mode: "status", configPath: badResourcePath, warn: (m) => badResWarnings.push(m) });
    assert.strictEqual(badRes.status, "invalid");
    assert.ok(badResWarnings.length > 0);

    // Broken JSON: run exits 2, status warns and continues (both in-process and subprocess)
    const brokenPath = path.join(tempDir, "broken.config.json");
    fs.writeFileSync(brokenPath, "{not valid json");

    let runExitCode: number | null = null;
    const runWarnings: string[] = [];
    loadConfig({
      mode: "run",
      configPath: brokenPath,
      warn: (m) => runWarnings.push(m),
      exit: (code) => {
        runExitCode = code;
        throw new Error(`EXITED_${code}`);
      },
    });
    assert.fail("loadConfig in run mode must call exit on broken JSON");
    void runExitCode;
  } catch (error) {
    if (!(error instanceof Error) || !error.message.startsWith("EXITED_")) {
      throw error;
    }
    assert.strictEqual(error.message, `EXITED_${UsageExitCode}`);
  } finally {
    process.chdir(previousCwd);
    removeDirectory(tempDir);
  }

  // Subprocess verification for broken JSON in run (exit 2) and status (warn + exit 0)
  const subDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-config-sub-"));
  try {
    const brokenPath = path.join(subDir, "broken.json");
    fs.writeFileSync(brokenPath, "{not valid json");
    const subEnv = { ...process.env, JOB_QUEUE_CONFIG: brokenPath };

    const runResult = spawnSync(process.execPath, [EntryScript, "run", "--", "echo", "hi"], {
      encoding: "utf8",
      windowsHide: true,
      env: subEnv,
    });
    assert.strictEqual(runResult.status, UsageExitCode, "run with broken config exits with code 2");
    assert.match(runResult.stderr, /config/i);

    const unusedPort = 20000 + Math.floor(Math.random() * 10000);
    const statusResult = spawnSync(process.execPath, [EntryScript, "status", "--port", String(unusedPort)], {
      encoding: "utf8",
      windowsHide: true,
      env: subEnv,
    });
    assert.strictEqual(statusResult.status, 0, "status with broken config warns and continues");
    assert.match(statusResult.stderr, /config/i);
  } finally {
    removeDirectory(subDir);
  }
}

/**
 * Check 2: 2 fake plugins with classify -> per-field first-wins, and forcedExclusive overrides share.
 */
function checkHostClassify(assert: AssertModule): void {
  const warnings: string[] = [];
  const p1: QueuePlugin = {
    name: "first",
    apiVersion: PluginApiVersion,
    classify: () => ({ test: true }),
  };
  const p2: QueuePlugin = {
    name: "second",
    apiVersion: PluginApiVersion,
    classify: () => ({ test: false, memoryMb: 2048, share: "shared" }),
  };
  const host = new PluginHost([p1, p2], [undefined, undefined], process.cwd(), (m) => warnings.push(m));
  const ctx = baseContext(process.cwd(), warnings);

  const shared = host.classify("cmd", { name: "gpu", forcedExclusive: false }, ctx);
  assert.deepStrictEqual(shared, { test: true, memoryMb: 2048, share: "shared" });

  const forced = host.classify("cmd", { name: "gpu", forcedExclusive: true }, ctx);
  assert.deepStrictEqual(forced, { test: true, memoryMb: 2048, share: "exclusive" });
  assert.deepStrictEqual(warnings, []);
}

/**
 * Check 3: Throwing plugin is skipped, warns once per plugin x hook, next plugin's answer is used; throwing check does
 * not refuse.
 */
function checkHostExceptions(assert: AssertModule): void {
  const warnings: string[] = [];
  const throwing: QueuePlugin = {
    name: "bad",
    apiVersion: PluginApiVersion,
    classify: () => {
      throw new Error("boom-classify");
    },
    check: () => {
      throw new Error("boom-check");
    },
  };
  const fallback: QueuePlugin = {
    name: "good",
    apiVersion: PluginApiVersion,
    classify: () => ({ test: true, memoryMb: 512 }),
    check: () => ({ hints: ["good-hint"] }),
  };
  const host = new PluginHost([throwing, fallback], [undefined, undefined], process.cwd(), (m) => warnings.push(m));
  const ctx = baseContext(process.cwd(), warnings);

  assert.deepStrictEqual(host.classify("cmd", null, ctx), { test: true, memoryMb: 512 });
  assert.deepStrictEqual(host.classify("cmd", null, ctx), { test: true, memoryMb: 512 });
  assert.strictEqual(
    warnings.filter((w) => w.includes("bad.classify")).length,
    1,
    "warns exactly once per plugin x hook",
  );
  assert.strictEqual(warnings[0], "[job-queue] plugin bad.classify failed: boom-classify; continuing without it");

  const checkResult = host.check("cmd", null, ctx);
  assert.strictEqual(checkResult.refuse, undefined, "throwing check must not refuse");
  assert.deepStrictEqual(checkResult.hints, ["good-hint"]);
  assert.strictEqual(warnings.filter((w) => w.includes("bad.check")).length, 1);
}

/**
 * Check 4: prepareCommand applies sequentially (command chained, env later-wins, note joined with ", "), plus
 * shortName / check composition.
 */
function checkHostSequential(assert: AssertModule): void {
  const warnings: string[] = [];
  const p1: QueuePlugin = {
    name: "p1",
    apiVersion: PluginApiVersion,
    prepareCommand: (command) => ({
      command: `${command} --a`,
      env: { SHARED: "from-p1", ONLY_A: "1" },
      note: "note-a",
    }),
    shortName: (_command, short) => `${short}:a`,
    check: () => ({ hints: ["h1"] }),
  };
  const p2: QueuePlugin = {
    name: "p2",
    apiVersion: PluginApiVersion,
    prepareCommand: (command) => ({
      command: `${command} --b`,
      env: { SHARED: "from-p2", ONLY_B: "2" },
      note: "note-b",
    }),
    shortName: (_command, short) => `${short}:b`,
    check: () => ({ refuse: "blocked", hints: ["h2"] }),
  };
  const p3: QueuePlugin = {
    name: "p3",
    apiVersion: PluginApiVersion,
    check: () => ({ hints: ["should-not-run"] }),
  };
  const host = new PluginHost([p1, p2, p3], [undefined, undefined, undefined], process.cwd(), (m) => warnings.push(m));
  const ctx = baseContext(process.cwd(), warnings);

  const prepared = host.prepareCommand("base", { jobId: 1, cpus: 4, nested: false }, ctx);
  assert.strictEqual(prepared.command, "base --a --b");
  assert.strictEqual(Object.getPrototypeOf(prepared.env), null);
  assert.deepStrictEqual(
    prepared.env,
    Object.assign(Object.create(null), { SHARED: "from-p2", ONLY_A: "1", ONLY_B: "2" }),
  );
  assert.strictEqual(prepared.note, "note-a, note-b");
  assert.strictEqual(host.shortName("base", "base"), "base:a:b");

  const checked = host.check("base", null, ctx);
  assert.strictEqual(checked.refuse, "blocked");
  assert.deepStrictEqual(checked.hints, ["h1", "h2"]);
}

/**
 * Check 5: Host discards out-of-range / wrong-type return values (memoryMb -1, "x", 1e12, share "both", oversized
 * StepKeys, empty SplitPlan fields).
 */
function checkHostValidation(assert: AssertModule): void {
  const warnings: string[] = [];
  const bad: QueuePlugin = {
    name: "bad-values",
    apiVersion: PluginApiVersion,
    classify: () => ({
      test: true,
      memoryMb: -1,
      share: "both" as ShareMode,
    }),
    stepKeys: () => ({ normalized: "x".repeat(2001), shape: "s", kind: "k" }),
    splitBeforeTurn: () => ({ before: "", after: "test", beforeRole: "b", afterRole: "a" }),
  };
  const badType: QueuePlugin = {
    name: "bad-types",
    apiVersion: PluginApiVersion,
    classify: () => ({
      memoryMb: "x" as unknown as number,
    }),
  };
  const badHuge: QueuePlugin = {
    name: "bad-huge",
    apiVersion: PluginApiVersion,
    classify: () => ({
      memoryMb: 1e12,
    }),
  };
  const validFallback: QueuePlugin = {
    name: "valid",
    apiVersion: PluginApiVersion,
    classify: () => ({ memoryMb: 256, share: "shared" }),
    stepKeys: () => ({ normalized: "ok", shape: "ok", kind: "ok" }),
    splitBeforeTurn: () => ({ before: "build", after: "test", beforeRole: "build", afterRole: "test" }),
  };
  const host = new PluginHost(
    [bad, badType, badHuge, validFallback],
    [undefined, undefined, undefined, undefined],
    process.cwd(),
    (m) => warnings.push(m),
  );
  const ctx = baseContext(process.cwd(), warnings);

  // Without valid fallback, invalid values must all be discarded
  const onlyInvalid = new PluginHost(
    [bad, badType, badHuge],
    [undefined, undefined, undefined],
    process.cwd(),
    () => {},
  );
  assert.deepStrictEqual(onlyInvalid.classify("cmd", null, ctx), { test: true });
  assert.strictEqual(
    onlyInvalid.stepKeys({ words: ["cmd"] }, { cwd: "", resolvePath: (a) => ({ text: a, where: "" }) }),
    undefined,
  );
  assert.strictEqual(onlyInvalid.splitBeforeTurn("cmd", { name: "gpu", forcedExclusive: false }, ctx), undefined);

  // With valid fallback, valid values win after invalid ones are discarded
  assert.deepStrictEqual(host.classify("cmd", null, ctx), { test: true, memoryMb: 256, share: "shared" });
  assert.deepStrictEqual(host.stepKeys({ words: ["cmd"] }, { cwd: "", resolvePath: (a) => ({ text: a, where: "" }) }), {
    normalized: "ok",
    shape: "ok",
    kind: "ok",
  });
  assert.deepStrictEqual(host.splitBeforeTurn("cmd", { name: "gpu", forcedExclusive: false }, ctx), {
    before: "build",
    after: "test",
    beforeRole: "build",
    afterRole: "test",
  });
}

/**
 * Check 6: In-process server accepts valid memoryMb (1234) from request and falls back to machine calculation when
 * absent or out of range; also verifies old client compatibility (raw message without new fields).
 */
async function checkServerMemoryMb(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-server-mem-"));
  const server = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  const port = (server.server.address() as net.AddressInfo).port;
  const clients: RawClient[] = [];
  try {
    const c1 = await connectRaw(port);
    clients.push(c1);
    c1.send({ ...registration("node -e 1", 1, process.pid), memoryMb: 1234 });
    const g1 = await c1.until("granted");
    assert.ok(g1 !== null);
    assert.strictEqual(server.find(Number(g1.id))?.memoryMb, 1234, "server keeps valid memoryMb from request");
    c1.send({ type: "done", completed: true, exitCode: 0 });
    await c1.until("done-ok");
    c1.close();

    // Without memoryMb (old client message) -> current computed value
    const c2 = await connectRaw(port);
    clients.push(c2);
    c2.send(registration("node -e 2", 1, process.pid));
    const g2 = await c2.until("granted");
    assert.ok(g2 !== null);
    assert.strictEqual(
      server.find(Number(g2.id))?.memoryMb,
      server.machine.testMemoryMb("node -e 2"),
      "without memoryMb falls back to machine.testMemoryMb",
    );
    c2.send({ type: "done", completed: true, exitCode: 0 });
    await c2.until("done-ok");
    c2.close();

    // Out-of-range / wrong-type memoryMb -> fallback
    const c3 = await connectRaw(port);
    clients.push(c3);
    c3.send({ ...registration("node -e 3", 1, process.pid), memoryMb: 1e12 });
    const g3 = await c3.until("granted");
    assert.ok(g3 !== null);
    assert.strictEqual(server.find(Number(g3.id))?.memoryMb, server.machine.testMemoryMb("node -e 3"));
    c3.close();
  } finally {
    server.stopping = true;
    for (const c of clients) {
      c.close();
    }
    for (const job of server.records.values()) {
      job.connection?.socket.destroy();
    }
    await new Promise<void>((resolve) => server.server.close(() => resolve()));
    removeDirectory(dir);
  }
}

/**
 * Checks 7 & 8: History lookup uses passed keys, and re-recording an entry without keys preserves existing keys.
 */
function checkHistoryKeys(assert: AssertModule): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-history-keys-"));
  try {
    const file = path.join(dir, "history.json");
    const history = new RunHistory(file, () => {});
    const ctx = { cwd: "", worktree: "" };
    const customKeys = { normalized: "custom:norm-key", shape: "custom:shape-key", kind: "custom" };

    // Check 7: record command A with keys K, then look up different command B with keys K -> from: "normalized"
    history.record("command alpha", 4200, 1, 1000, ctx, customKeys);
    const estimate = history.estimateWithFallback("completely different command beta", ctx, customKeys);
    assert.ok(estimate !== null);
    assert.strictEqual(estimate.from, "normalized");
    assert.strictEqual(estimate.typicalMs, 4200);

    // Check 8: re-recording command A without keys preserves K
    history.record("command alpha", 4800, 2, 2000, ctx);
    const reloaded = new RunHistory(file, () => {});
    assert.deepStrictEqual(reloaded.commands[RunHistory.key("command alpha")]?.keys, customKeys);
    const updatedEstimate = reloaded.estimateWithFallback("completely different command beta", ctx, customKeys);
    assert.strictEqual(updatedEstimate?.from, "normalized");
    assert.strictEqual(updatedEstimate?.typicalMs, 4500);
  } finally {
    removeDirectory(dir);
  }
}

/**
 * Check 9: State file persists waiting job's keys across scheduler restart and keeps the same estimate.
 */
async function checkStateFileKeys(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-state-keys-"));
  const customKeys = { normalized: "custom:state-norm", shape: "custom:state-shape", kind: "custom" };
  const s1 = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
  s1.scheduler.holdUntil = 0;
  s1.history.record("recorded command alpha", 5500, 1, Date.now(), { cwd: "", worktree: "" }, customKeys);
  await new Promise<void>((resolve) => s1.server.listen(0, "127.0.0.1", resolve));
  const port = (s1.server.address() as net.AddressInfo).port;
  const clients: RawClient[] = [];
  try {
    const holder = await connectRaw(port);
    clients.push(holder);
    holder.send(registration("holder", 1, process.pid));
    assert.ok((await holder.until("granted")) !== null);

    const waiter = await connectRaw(port);
    clients.push(waiter);
    waiter.send({ ...registration("different waiting beta", 1, process.pid), keys: customKeys });
    const queued = await waiter.until("queued");
    assert.ok(queued !== null);
    const waitingId = Number(queued.id);
    assert.strictEqual(s1.find(waitingId)?.estimate?.from, "normalized");
    assert.strictEqual(s1.find(waitingId)?.estimate?.typicalMs, 5500);

    s1.store.persist();
    const s2 = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
    assert.strictEqual(s2.store.restore(), "current");
    const restored = s2.find(waitingId);
    assert.deepStrictEqual(restored?.keys, customKeys, "restored job keeps keys");
    assert.strictEqual(restored?.estimate?.from, "normalized", "restored job keeps fallback estimate from keys");
    assert.strictEqual(restored?.estimate?.typicalMs, 5500);

    holder.close();
    waiter.close();
  } finally {
    s1.stopping = true;
    for (const c of clients) {
      c.close();
    }
    for (const job of s1.records.values()) {
      job.connection?.socket.destroy();
    }
    await new Promise<void>((resolve) => s1.server.close(() => resolve()));
    removeDirectory(dir);
  }
}

/**
 * Dynamic plugin loading from temp directories: valid plugin.ts, apiVersion mismatch, name mismatch, missing
 * default export, JOB_QUEUE_PLUGINS=0 escape hatch, and realpathSync.native comparison.
 */
async function checkPluginLoading(assert: AssertModule): Promise<void> {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-plugin-load-"));
  const apiUrl = pathToFileURL(path.join(import.meta.dirname, "plugin-api.ts")).href;
  try {
    const writePlugin = (folderName: string, source: string): void => {
      const folder = path.join(tempDir, folderName);
      fs.mkdirSync(folder, { recursive: true });
      fs.writeFileSync(path.join(folder, "plugin.ts"), source);
    };

    writePlugin(
      "valid-plug",
      `import { PluginApiVersion, type QueuePlugin } from ${JSON.stringify(apiUrl)};\n` +
        `const plugin = {\n` +
        `  name: "./valid-plug",\n` +
        `  apiVersion: PluginApiVersion,\n` +
        `  classify: (_cmd: string, _res: unknown, ctx: { options: Readonly<Record<string, unknown>> }) => ({\n` +
        `    test: ctx.options.flag === true,\n` +
        `    memoryMb: 777,\n` +
        `  }),\n` +
        `} satisfies QueuePlugin;\n` +
        `export default plugin;\n`,
    );
    writePlugin("wrong-version", `export default { name: "./wrong-version", apiVersion: 999 };\n`);
    writePlugin("wrong-name", `export default { name: "other-name", apiVersion: 1 };\n`);
    writePlugin("no-default", `export const helper = 1;\n`);

    const warnings: string[] = [];
    const config = {
      version: 1 as const,
      entry: "scripts/human/job-queue.js",
      plugins: [
        { name: "./valid-plug", options: { flag: true } },
        { name: "./wrong-version" },
        { name: "./wrong-name" },
        { name: "./no-default" },
        { name: "./does-not-exist" },
      ],
    };
    const host = await PluginHost.load(config, tempDir, (m) => warnings.push(m), {});
    assert.strictEqual(host.plugins.length, 1, "only valid plugin loads; invalid ones warn and skip");
    assert.strictEqual(warnings.length, 4, "each invalid/missing plugin produced a warning");

    const ctx = baseContext(fs.realpathSync.native(tempDir), warnings);
    assert.deepStrictEqual(host.classify("cmd", null, ctx), { test: true, memoryMb: 777 });

    // Escape hatch JOB_QUEUE_PLUGINS=0 skips loading all plugins
    const disabledWarnings: string[] = [];
    const disabledHost = await PluginHost.load(config, tempDir, (m) => disabledWarnings.push(m), {
      JOB_QUEUE_PLUGINS: "0",
    });
    assert.strictEqual(disabledHost.plugins.length, 0);
    assert.deepStrictEqual(disabledWarnings, []);

    // Import timeout (Promise.race) warns and skips without hanging
    writePlugin(
      "slow-import",
      `await new Promise((r) => setTimeout(r, 400)); export default { name: "./slow-import", apiVersion: 1 };\n`,
    );
    const timeoutWarnings: string[] = [];
    const timedOutHost = await PluginHost.load(
      { version: 1, entry: "scripts/human/job-queue.js", plugins: [{ name: "./slow-import" }] },
      tempDir,
      (m) => timeoutWarnings.push(m),
      {},
      { importTimeoutMs: 50 },
    );
    assert.strictEqual(timedOutHost.plugins.length, 0);
    assert.ok(timeoutWarnings.some((w) => w.includes("timed out")));

    // Slow hook (> slowHookMs) emits a one-line warning
    const slowWarnings: string[] = [];
    const slowPlugin: QueuePlugin = {
      name: "slow-hook",
      apiVersion: PluginApiVersion,
      classify: () => {
        const end = Date.now() + 20;
        while (Date.now() < end) {
          // busy wait
        }
        return { test: true };
      },
    };
    const slowHost = new PluginHost([slowPlugin], [undefined], tempDir, (m) => slowWarnings.push(m), 5);
    assert.deepStrictEqual(slowHost.classify("cmd", null, ctx), { test: true });
    assert.ok(slowWarnings.some((w) => w.includes("slow-hook.classify took")));
  } finally {
    removeDirectory(tempDir);
  }
}

/**
 * Version-skew checks (in-process): old scheduler (granted without new fields, or error code: "version") with client,
 * and JobServer loading --config without loading plugin code.
 */
async function checkVersionSkewAndServeConfig(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-skew-"));
  const cfgPath = path.join(dir, "job-queue.config.json");
  fs.writeFileSync(
    cfgPath,
    JSON.stringify({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./does-not-exist-plugin" }],
      resources: { gpu: { slots: 1 } },
    }),
  );
  try {
    // Scheduler reads data portion only; missing/non-existent plugin code does not cause plugin warnings on JobServer
    const serverLogs: string[] = [];
    const server = new JobServer({
      slots: 1,
      memoryMb: 1e9,
      policy: "fifo",
      port: 0,
      stateDir: dir,
      configPath: cfgPath,
      log: (text) => serverLogs.push(text),
    });
    assert.strictEqual(server.config.resources?.gpu?.slots, 1);
    assert.strictEqual(
      serverLogs.some((l) => l.includes("does-not-exist-plugin")),
      false,
      "scheduler never loads plugin code",
    );

    // Fake RunHost against an old-protocol scheduler returning granted without new fields, and error code: "version"
    let mode: "granted" | "version-error" = "granted";
    const serverSockets = new Set<net.Socket>();
    const fakeServer = net.createServer((socket) => {
      serverSockets.add(socket);
      socket.on("close", () => serverSockets.delete(socket));
      socket.on("error", () => socket.destroy());
      socket.on("data", (buf) => {
        const lines = buf.toString("utf8").split("\n").filter(Boolean);
        for (const line of lines) {
          const msg = JSON.parse(line) as { type?: string };
          if (msg.type === "request") {
            if (mode === "version-error") {
              socket.write(`${JSON.stringify({ type: "error", code: "version", message: "unsupported version" })}\n`);
            } else {
              socket.write(
                `${JSON.stringify({ type: "granted", id: 1, cpus: 1, capacity: 2, protocol: ProtocolVersion })}\n`,
              );
            }
          } else if (msg.type === "done") {
            socket.write(`${JSON.stringify({ type: "done-ok", id: 1 })}\n`);
            socket.end();
          }
        }
      });
    });
    await new Promise<void>((resolve) => fakeServer.listen(0, "127.0.0.1", resolve));
    const port = (fakeServer.address() as net.AddressInfo).port;
    try {
      const runFakeClient = async (gpuTurn = false): Promise<{ exits: number[]; gpuLockCalls: number }> => {
        const exits: number[] = [];
        const clientSockets: net.Socket[] = [];
        let gpuLockCalls = 0;
        let resolveExit: () => void = () => {};
        const exited = new Promise<void>((resolve) => (resolveExit = resolve));
        const host: RunHost = {
          connect: () =>
            new Promise((resolve) => {
              const s = net.connect(port, "127.0.0.1", () => {
                clientSockets.push(s);
                resolve(s);
              });
              s.on("error", () => s.destroy());
            }),
          reportEnd: () => Promise.resolve(),
          runCommand: async () => ({ code: 0, signaled: false }) satisfies ShellResult,
          runUnderGpuLock: async () => {
            gpuLockCalls++;
            return { code: 0, signaled: false };
          },
          stop: () => {},
          exit: (code) => {
            exits.push(code);
            resolveExit();
          },
          onSignal: () => {},
        };
        const runOptions: RunOptions = {
          port,
          slots: 2,
          memoryMb: 1024,
          policy: "hrrn",
          command: "node -e 1",
          commandWords: null,
          weight: 1,
          gpu: gpuTurn,
          shell: "",
          timeoutMinutes: 0,
          after: 0,
          keepIfFailed: false,
        };
        const origWrite = process.stderr.write;
        process.stderr.write = (() => true) as typeof process.stderr.write;
        let timeoutId: ReturnType<typeof setTimeout> | undefined;
        try {
          await new QueuedRun(runOptions, { weight: 1, pin: null, before: null }, false, gpuTurn, host).run();
          await Promise.race([
            exited,
            new Promise<void>((_resolve, reject) => {
              timeoutId = setTimeout(() => reject(new Error("timed out waiting for FakeHost.exit")), 5000);
            }),
          ]);
        } finally {
          if (timeoutId !== undefined) {
            clearTimeout(timeoutId);
          }
          for (const s of clientSockets) {
            s.destroy();
          }
          process.stderr.write = origWrite;
        }
        return { exits, gpuLockCalls };
      };

      assert.deepStrictEqual(
        (await runFakeClient(false)).exits,
        [0],
        "client completes cleanly against scheduler granted without new fields",
      );

      mode = "version-error";
      assert.deepStrictEqual(
        (await runFakeClient(false)).exits,
        [1],
        'non-GPU client exits 1 cleanly on error code: "version"',
      );
      const gpuFallback = await runFakeClient(true);
      assert.deepStrictEqual(
        gpuFallback,
        { exits: [0], gpuLockCalls: 1 },
        'GPU turn falls back to GPU lock on error code: "version"',
      );
    } finally {
      for (const s of serverSockets) {
        s.destroy();
      }
      await new Promise<void>((resolve) => fakeServer.close(() => resolve()));
    }
  } finally {
    removeDirectory(dir);
  }
}

/**
 * Hardening & cross-cwd config forwarding checks:
 * 1. prepareCommand env uses a null-prototype object so "__proto__" keys do not mutate prototypes and work with Object.entries / JSON.
 * 2. resolvePluginFolder rejects bare plugin names containing /, \, or .. so they cannot traverse out of <core>/plugins/.
 * 3. Relative --config and relative JOB_QUEUE_CONFIG are resolved against caller's cwd before SchedulerLauncher spawns serve in stateDirectory.
 */
async function checkHardeningAndConfigForwarding(assert: AssertModule): Promise<void> {
  // 1. __proto__ safety in prepareCommand env
  const protoWarnings: string[] = [];
  const pProto1: QueuePlugin = {
    name: "proto1",
    apiVersion: PluginApiVersion,
    prepareCommand: () => ({
      env: JSON.parse('{"__proto__":"from-p1","KEEP":"1"}') as Record<string, string>,
    }),
  };
  const pProto2: QueuePlugin = {
    name: "proto2",
    apiVersion: PluginApiVersion,
    prepareCommand: () => ({
      env: JSON.parse('{"__proto__":"from-p2"}') as Record<string, string>,
    }),
  };
  const protoHost = new PluginHost([pProto1, pProto2], [undefined, undefined], process.cwd(), (m) =>
    protoWarnings.push(m),
  );
  const preparedProto = protoHost.prepareCommand(
    "cmd",
    { jobId: 1, cpus: 1, nested: false },
    baseContext(process.cwd(), protoWarnings),
  );
  assert.strictEqual(Object.getPrototypeOf(preparedProto.env), null);
  assert.strictEqual((Object.prototype as Record<string, unknown>).polluted, undefined);
  assert.deepStrictEqual(Object.entries(preparedProto.env), [
    ["__proto__", "from-p2"],
    ["KEEP", "1"],
  ]);
  assert.strictEqual(JSON.stringify(preparedProto.env), '{"__proto__":"from-p2","KEEP":"1"}');
  assert.deepStrictEqual({ ...preparedProto.env }, JSON.parse('{"__proto__":"from-p2","KEEP":"1"}'));

  // 2. Bare plugin name traversal guard
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-forward-check-"));
  try {
    const traversalWarnings: string[] = [];
    const traversalNames = ["../outside", "..\\outside", "sub/plug", "sub\\plug", "..", "."];
    const traversalHost = await PluginHost.load(
      {
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: traversalNames.map((name) => ({ name })),
      },
      tempDir,
      (m) => traversalWarnings.push(m),
      {},
    );
    assert.strictEqual(traversalHost.plugins.length, 0);
    assert.strictEqual(traversalWarnings.length, traversalNames.length);
    assert.ok(
      traversalWarnings.every((w) => w.includes("invalid bare name")),
      "every bare traversal name is rejected before filesystem probing",
    );

    // 3. Cross-cwd relative --config and relative JOB_QUEUE_CONFIG forwarding via SchedulerLauncher
    const callerDir = path.join(tempDir, "caller");
    fs.mkdirSync(callerDir, { recursive: true });
    fs.writeFileSync(
      path.join(callerDir, "rel.config.json"),
      JSON.stringify({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        callerConfigWitness: true,
      }),
    );

    const runCli = (
      args: string[],
      cwd: string,
      env: NodeJS.ProcessEnv,
    ): Promise<{ status: number | null; stderr: string }> =>
      new Promise((resolve, reject) => {
        const child = spawn(process.execPath, [EntryScript, ...args], {
          cwd,
          env,
          stdio: ["ignore", "ignore", "pipe"],
          windowsHide: true,
        });
        let stderr = "";
        child.stderr.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
        const timer = setTimeout(() => {
          child.kill("SIGKILL");
          reject(new Error(`timed out running job-queue ${args.join(" ")}`));
        }, 25000);
        child.on("error", (err) => {
          clearTimeout(timer);
          reject(err);
        });
        child.on("close", (status) => {
          clearTimeout(timer);
          resolve({ status, stderr });
        });
      });

    const basePort = firstTestPort();
    const cleanBaseEnv = (stateDir: string, extraConfigEnv?: string): NodeJS.ProcessEnv => {
      const env: NodeJS.ProcessEnv = { ...process.env };
      delete env.JOB_QUEUE_CONFIG;
      delete env[JobVariable];
      delete env[InGpuLockVariable];
      delete env[CpusVariable];
      delete env[TurnVariable];
      delete env[GpuLockHeldVariable];
      env[StateDirectoryVariable] = stateDir;
      env[ShellVariable] = process.platform === "win32" ? "cmd" : "sh";
      if (extraConfigEnv !== undefined) {
        env.JOB_QUEUE_CONFIG = extraConfigEnv;
      }
      return env;
    };

    const stopAndAwaitExit = async (port: number): Promise<void> => {
      const status = await request(port, { type: "status" }).catch(() => null);
      const pid = typeof status?.pid === "number" ? status.pid : 0;
      await request(port, { type: "stop" }).catch(() => null);
      for (let i = 0; i < 50; i++) {
        const probe = await connect(port).catch(() => null);
        if (probe !== null) {
          probe.destroy();
        } else if (pid <= 0 || !ProcessTable.isAlive(pid)) {
          await delay(100);
          return;
        }
        await delay(100);
      }
    };

    // Case A: relative --config pointing to existing file in callerDir while stateDir holds an invalid trap
    const stateA = path.join(tempDir, "state-a");
    fs.mkdirSync(stateA, { recursive: true });
    fs.writeFileSync(
      path.join(stateA, "rel.config.json"),
      JSON.stringify({ version: 2, entry: "scripts/human/job-queue.js", plugins: [] }),
    );
    const portA = basePort;
    try {
      const resA = await runCli(
        ["run", "--port", String(portA), "--config", "./rel.config.json", "--", process.execPath, "-e", "0"],
        callerDir,
        cleanBaseEnv(stateA),
      );
      assert.strictEqual(resA.status, 0, `relative --config run exited cleanly: ${resA.stderr}`);
      const logA = fs.readFileSync(path.join(stateA, "server.log"), "utf8");
      assert.ok(!logA.includes("invalid config"), `serve must not read stateDir trap: ${logA}`);
      assert.ok(logA.includes("callerConfigWitness"), `serve must read callerDir rel.config.json: ${logA}`);
    } finally {
      await stopAndAwaitExit(portA);
    }

    // Case B: relative --config pointing to missing file in callerDir while stateDir holds an invalid trap
    const stateB = path.join(tempDir, "state-b");
    fs.mkdirSync(stateB, { recursive: true });
    fs.writeFileSync(
      path.join(stateB, "missing-rel.json"),
      JSON.stringify({ version: 2, entry: "scripts/human/job-queue.js", plugins: [] }),
    );
    const portB = basePort + 1;
    try {
      const resB = await runCli(
        ["run", "--port", String(portB), "--config", "./missing-rel.json", "--", process.execPath, "-e", "0"],
        callerDir,
        cleanBaseEnv(stateB),
      );
      assert.strictEqual(resB.status, 0, `missing relative --config run exited cleanly: ${resB.stderr}`);
      const logB = fs.readFileSync(path.join(stateB, "server.log"), "utf8");
      assert.ok(!logB.includes("invalid config"), `serve must not resolve missing --config in stateDir: ${logB}`);
    } finally {
      await stopAndAwaitExit(portB);
    }

    // Case C: relative JOB_QUEUE_CONFIG pointing to missing file in callerDir while stateDir holds an invalid trap
    const stateC = path.join(tempDir, "state-c");
    fs.mkdirSync(stateC, { recursive: true });
    fs.writeFileSync(
      path.join(stateC, "env-rel.json"),
      JSON.stringify({ version: 2, entry: "scripts/human/job-queue.js", plugins: [] }),
    );
    const portC = basePort + 2;
    try {
      const resC = await runCli(
        ["run", "--port", String(portC), "--", process.execPath, "-e", "0"],
        callerDir,
        cleanBaseEnv(stateC, "./env-rel.json"),
      );
      assert.strictEqual(resC.status, 0, `missing relative JOB_QUEUE_CONFIG run exited cleanly: ${resC.stderr}`);
      const logC = fs.readFileSync(path.join(stateC, "server.log"), "utf8");
      assert.ok(!logC.includes("invalid config"), `serve must not resolve JOB_QUEUE_CONFIG in stateDir: ${logC}`);
    } finally {
      await stopAndAwaitExit(portC);
    }
  } finally {
    removeDirectory(tempDir);
  }
}

export async function checkPlugins(assert: AssertModule): Promise<void> {
  await checkConfig(assert);
  checkHostClassify(assert);
  checkHostExceptions(assert);
  checkHostSequential(assert);
  checkHostValidation(assert);
  await checkServerMemoryMb(assert);
  checkHistoryKeys(assert);
  await checkStateFileKeys(assert);
  await checkPluginLoading(assert);
  await checkVersionSkewAndServeConfig(assert);
  await checkHardeningAndConfigForwarding(assert);
}
