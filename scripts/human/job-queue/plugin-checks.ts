// Checks of the config loader, the plugin host, and the scheduler's optional memoryMb / keys fields.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
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
  ShellVariable,
  StateDirectoryVariable,
  TurnVariable,
  UsageExitCode,
} from "./constants.ts";
import { connect, request } from "./client-connect.ts";
import { loadConfig } from "./config.ts";
import type { ConfigFileModule } from "./lib/types.ts";
import { PluginApiVersion, type OffloadCheckRequest, type QueuePlugin, type ShareMode } from "./plugin-api.ts";
import { PluginHost, runPluginChecks, type BaseClientContext } from "./plugin-host.ts";
import { ProcessTable } from "./system.ts";
import { delay, firstTestPort, removeDirectory } from "./self-test-support.ts";
import {
  checkHistoryKeys,
  checkLegacyClientsMemoryMb,
  checkServerMemoryMb,
  checkStateFileKeys,
  checkVersionSkewAndServeConfig,
} from "./scheduler-intake-checks.ts";

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
export async function withConfigEnv(value: string | undefined, body: () => void | Promise<void>): Promise<void> {
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

  // localReason is accepted only from the plugin deciding offloadable: false, and collapsed to 1 line
  const localHost = new PluginHost(
    [
      { name: "l1", apiVersion: PluginApiVersion, classify: () => ({ offloadable: false, localReason: "a\r\nb" }) },
      { name: "l2", apiVersion: PluginApiVersion, classify: () => ({ offloadable: true, localReason: "ignored" }) },
    ],
    [undefined, undefined],
    process.cwd(),
    (m) => warnings.push(m),
  );
  assert.deepStrictEqual(localHost.classify("cmd", null, ctx), { offloadable: false, localReason: "a b" });

  const allowHost = new PluginHost(
    [
      { name: "a1", apiVersion: PluginApiVersion, classify: () => ({ offloadable: true, localReason: "ignored" }) },
      { name: "a2", apiVersion: PluginApiVersion, classify: () => ({ offloadable: false, localReason: "ignored" }) },
    ],
    [undefined, undefined],
    process.cwd(),
    (m) => warnings.push(m),
  );
  assert.deepStrictEqual(allowHost.classify("cmd", null, ctx), { offloadable: true });
  assert.deepStrictEqual(warnings, []);
}

async function checkHostCheckOffload(assert: AssertModule): Promise<void> {
  const warnings: string[] = [];
  const ctx = baseContext(process.cwd(), warnings);
  const req = { remoteForced: false, places: {} };
  const neverCalled: QueuePlugin = {
    name: "later",
    apiVersion: PluginApiVersion,
    checkOffload: () => Promise.reject(new Error("should not be called")),
  };
  const hostOf = (hook: QueuePlugin["checkOffload"], extra: QueuePlugin[] = []): PluginHost => {
    const p: QueuePlugin = { name: "probe", apiVersion: PluginApiVersion, checkOffload: hook };
    const host = new PluginHost([p, ...extra], [undefined, ...extra.map(() => undefined)], process.cwd(), (m) =>
      warnings.push(m),
    );
    host.checkOffloadTimeoutMs = 50;
    return host;
  };

  assert.deepStrictEqual(await hostOf(undefined).checkOffload("c", req, ctx), undefined);
  assert.deepStrictEqual(await hostOf(() => Promise.resolve(undefined)).checkOffload("c", req, ctx), undefined);
  assert.deepStrictEqual(await hostOf(() => Promise.resolve("allow"), [neverCalled]).checkOffload("c", req, ctx), {
    offload: "allow",
  });
  assert.deepStrictEqual(
    await hostOf(() => Promise.resolve({ offload: "allow" }), [neverCalled]).checkOffload("c", req, ctx),
    { offload: "allow" },
  );
  assert.deepStrictEqual(await hostOf(() => Promise.resolve("local"), [neverCalled]).checkOffload("c", req, ctx), {
    offload: "local",
  });
  assert.deepStrictEqual(
    await hostOf(() => Promise.resolve({ offload: "local", reason: "line1\nline2" }), [neverCalled]).checkOffload(
      "c",
      req,
      ctx,
    ),
    { offload: "local", reason: "line1 line2" },
  );
  // undefined passes to next plugin
  assert.deepStrictEqual(
    await hostOf(
      () => Promise.resolve(undefined),
      [{ name: "second", apiVersion: PluginApiVersion, checkOffload: () => Promise.resolve("allow") }],
    ).checkOffload("c", req, ctx),
    { offload: "allow" },
  );

  // Failures, timeouts, and invalid return values fail closed to "local" without calling later plugins
  warnings.length = 0;
  const rejectRes = await hostOf(() => Promise.reject(new Error("boom")), [neverCalled]).checkOffload("c", req, ctx);
  assert.strictEqual(rejectRes?.offload, "local");
  assert.ok(rejectRes?.reason?.includes("boom"));

  const throwRes = await hostOf(() => {
    throw new Error("sync-boom");
  }, [neverCalled]).checkOffload("c", req, ctx);
  assert.strictEqual(throwRes?.offload, "local");
  assert.ok(throwRes?.reason?.includes("sync-boom"));

  const timeoutRes = await hostOf(() => new Promise<never>(() => {}), [neverCalled]).checkOffload("c", req, ctx);
  assert.strictEqual(timeoutRes?.offload, "local");
  assert.ok(timeoutRes?.reason?.includes("timed out after 50 ms"));

  const invalidRes = await hostOf(() => Promise.resolve("Allow" as unknown as "allow"), [neverCalled]).checkOffload(
    "c",
    req,
    ctx,
  );
  assert.strictEqual(invalidRes?.offload, "local");
  assert.ok(invalidRes?.reason?.includes("invalid value"));
  assert.strictEqual(warnings.length, 4);

  // The plugin is told the limit, and its signal aborts when the host stops waiting (not before)
  let seenRequest: OffloadCheckRequest | undefined;
  let abortedEarly = true;
  const lostRes = await hostOf((_command, request) => {
    seenRequest = request;
    abortedEarly = request.signal?.aborted ?? true;
    return new Promise<never>(() => {});
  }).checkOffload("c", req, ctx);
  assert.strictEqual(lostRes?.offload, "local");
  assert.strictEqual(seenRequest?.timeoutMs, 50);
  assert.strictEqual(abortedEarly, false);
  assert.strictEqual(seenRequest?.signal?.aborted, true, "a lost race aborts the plugin's signal");
  assert.strictEqual(seenRequest?.remoteForced, false);
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
 * Dynamic `checks.ts` loader verification:
 * 1. Path-configured plugin's `checks.ts` runs (verified via `fs.realpathSync.native`).
 * 2. Bundled `plugins/<name>/checks.ts` folders run even when omitted from `config.plugins[]`.
 * 3. Throwing `checks()` rejects with the plugin name in the error message.
 * 4. An enabled plugin in `config.plugins[]` missing `checks.ts` rejects with the plugin name.
 * 5. `JOB_QUEUE_PLUGINS=0` does not suppress `runPluginChecks`.
 */
async function checkPluginChecksLoader(assert: AssertModule): Promise<void> {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-checks-loader-"));
  try {
    const writeJson = (filePath: string, data: unknown): void => {
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      fs.writeFileSync(filePath, JSON.stringify(data));
    };
    const writeFile = (filePath: string, content: string): string => {
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      fs.writeFileSync(filePath, content);
      return filePath;
    };
    const writeRecorder = (filePath: string, logPath: string): string =>
      writeFile(
        filePath,
        `import * as fs from "node:fs";\n` +
          `export async function checks(): Promise<void> {\n` +
          `  fs.appendFileSync(${JSON.stringify(logPath)}, import.meta.filename + "\\n");\n` +
          `}\n`,
      );
    const readLines = (filePath: string): string[] =>
      fs.existsSync(filePath) ? fs.readFileSync(filePath, "utf8").trim().split(/\r?\n/).filter(Boolean) : [];

    // 1. Path-configured plugin runs
    const case1Root = path.join(tempDir, "case1", "repo");
    const case1Bundled = path.join(tempDir, "case1", "bundled");
    const log1 = path.join(tempDir, "case1", "ran.log");
    fs.mkdirSync(case1Bundled, { recursive: true });
    writeJson(path.join(case1Root, "job-queue.config.json"), {
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./repo-plug" }],
    });
    const repoChecks1 = writeRecorder(path.join(case1Root, "repo-plug", "checks.ts"), log1);
    await runPluginChecks(assert, { configRoot: case1Root, pluginsRoot: case1Bundled });
    assert.deepStrictEqual(
      readLines(log1).map((p) => fs.realpathSync.native(p)),
      [fs.realpathSync.native(repoChecks1)],
      "path-configured plugin checks.ts runs",
    );

    // 2. Two unlisted bundled folders both run in sorted order
    const case2Root = path.join(tempDir, "case2", "repo");
    const case2Bundled = path.join(tempDir, "case2", "bundled");
    const log2 = path.join(tempDir, "case2", "ran.log");
    writeJson(path.join(case2Root, "job-queue.config.json"), {
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
    });
    const betaChecks2 = writeRecorder(path.join(case2Bundled, "beta", "checks.ts"), log2);
    const alphaChecks2 = writeRecorder(path.join(case2Bundled, "alpha", "checks.ts"), log2);
    await runPluginChecks(assert, { configRoot: case2Root, pluginsRoot: case2Bundled });
    assert.deepStrictEqual(
      readLines(log2).map((p) => fs.realpathSync.native(p)),
      [fs.realpathSync.native(alphaChecks2), fs.realpathSync.native(betaChecks2)],
      "all bundled checks.ts run in order even when omitted from config.plugins",
    );

    // 3. Throwing checks() rejects with plugin name
    const case3Root = path.join(tempDir, "case3", "repo");
    const case3Bundled = path.join(tempDir, "case3", "bundled");
    fs.mkdirSync(case3Bundled, { recursive: true });
    writeJson(path.join(case3Root, "job-queue.config.json"), {
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./boom-plug" }],
    });
    writeFile(
      path.join(case3Root, "boom-plug", "checks.ts"),
      `export async function checks(): Promise<void> { throw new Error("boom-from-checks"); }\n`,
    );
    await assert.rejects(
      () => runPluginChecks(assert, { configRoot: case3Root, pluginsRoot: case3Bundled }),
      (err: unknown) =>
        err instanceof Error && err.message.includes("./boom-plug") && err.message.includes("boom-from-checks"),
      "throwing plugin checks.ts rejects with plugin name",
    );

    // 4. Enabled plugin without checks.ts rejects
    const case4Root = path.join(tempDir, "case4", "repo");
    const case4Bundled = path.join(tempDir, "case4", "bundled");
    fs.mkdirSync(case4Bundled, { recursive: true });
    writeJson(path.join(case4Root, "job-queue.config.json"), {
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./no-checks" }],
    });
    writeFile(
      path.join(case4Root, "no-checks", "plugin.ts"),
      `export default { name: "./no-checks", apiVersion: 1 };\n`,
    );
    await assert.rejects(
      () => runPluginChecks(assert, { configRoot: case4Root, pluginsRoot: case4Bundled }),
      (err: unknown) => err instanceof Error && err.message.includes("./no-checks"),
      "enabled plugin lacking checks.ts rejects",
    );

    // 5. JOB_QUEUE_PLUGINS=0 does not skip plugin checks
    const case5Root = path.join(tempDir, "case5", "repo");
    const case5Bundled = path.join(tempDir, "case5", "bundled");
    const log5 = path.join(tempDir, "case5", "ran.log");
    writeJson(path.join(case5Root, "job-queue.config.json"), {
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./repo-plug" }],
    });
    const bundledChecks5 = writeRecorder(path.join(case5Bundled, "plug-a", "checks.ts"), log5);
    const repoChecks5 = writeRecorder(path.join(case5Root, "repo-plug", "checks.ts"), log5);
    const previousPluginsEnv = process.env.JOB_QUEUE_PLUGINS;
    process.env.JOB_QUEUE_PLUGINS = "0";
    try {
      await runPluginChecks(assert, { configRoot: case5Root, pluginsRoot: case5Bundled });
    } finally {
      if (previousPluginsEnv === undefined) {
        delete process.env.JOB_QUEUE_PLUGINS;
      } else {
        process.env.JOB_QUEUE_PLUGINS = previousPluginsEnv;
      }
    }
    assert.deepStrictEqual(
      readLines(log5).map((p) => fs.realpathSync.native(p)),
      [fs.realpathSync.native(bundledChecks5), fs.realpathSync.native(repoChecks5)],
      "JOB_QUEUE_PLUGINS=0 does not skip plugin checks",
    );
  } finally {
    removeDirectory(tempDir);
  }
}

/**
 * Hardening & cross-cwd config forwarding checks:
 * 1. prepareCommand env uses a null-prototype object, so "__proto__" keys do not change prototypes and survive
 *    Object.entries and JSON.
 * 2. resolvePluginFolder rejects bare plugin names containing /, \ or .., so they cannot leave <core>/plugins/.
 * 3. A relative --config and a relative JOB_QUEUE_CONFIG are resolved against the caller's cwd before
 *    SchedulerLauncher starts serve in the state directory.
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
  await checkHostCheckOffload(assert);
  checkHostExceptions(assert);
  checkHostSequential(assert);
  checkHostValidation(assert);
  await checkServerMemoryMb(assert);
  await checkLegacyClientsMemoryMb(assert);
  checkHistoryKeys(assert);
  await checkStateFileKeys(assert);
  await checkPluginLoading(assert);
  await checkPluginChecksLoader(assert);
  await checkVersionSkewAndServeConfig(assert);
  await checkHardeningAndConfigForwarding(assert);
}
