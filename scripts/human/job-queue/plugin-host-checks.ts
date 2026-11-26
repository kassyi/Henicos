// Checks of the plugin host: classify composition and validation, checkOffload, failing plugins, and the hooks
// that apply in sequence.

import type * as Assert from "node:assert";
import { PluginApiVersion, type OffloadCheckRequest, type QueuePlugin, type ShareMode } from "./plugin-api.ts";
import { PluginHost, type BaseClientContext } from "./plugin-host.ts";

type AssertModule = typeof Assert;

export function baseContext(configRoot: string, warnings: string[]): BaseClientContext {
  return {
    cwd: configRoot,
    worktree: configRoot,
    configRoot,
    shell: "bash",
    env: {},
    log: (text: string) => warnings.push(`[job-queue] ${text}`),
  };
}

/**
 * Check 2: 2 fake plugins with classify -> per-field first-wins, and forcedExclusive overrides share.
 */
export function checkHostClassify(assert: AssertModule): void {
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
  assert.deepStrictEqual(forced, {
    test: true,
    memoryMb: 2048,
    share: "exclusive",
    shareReason: "forced-exclusive",
  });

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

export async function checkHostCheckOffload(assert: AssertModule): Promise<void> {
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
export function checkHostExceptions(assert: AssertModule): void {
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
export function checkHostSequential(assert: AssertModule): void {
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
export function checkHostValidation(assert: AssertModule): void {
  const warnings: string[] = [];
  const bad: QueuePlugin = {
    name: "bad-values",
    apiVersion: PluginApiVersion,
    classify: () => ({
      test: true,
      memoryMb: -1,
      share: "both" as ShareMode,
      shareReason: "orphan-reason",
    }),
    stepKeys: () => ({ normalized: "x".repeat(2001), shape: "s", kind: "k" }),
    splitBeforeTurn: () => ({ before: "", after: "test", beforeRole: "b", afterRole: "a" }),
  };
  const badType: QueuePlugin = {
    name: "bad-types",
    apiVersion: PluginApiVersion,
    classify: () => ({
      memoryMb: "x" as unknown as number,
      shareReason: "orphan-without-share",
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

  // Invalid or uncoupled shareReason values are discarded while keeping valid share / valid fallback reason
  const orphanWithReason: QueuePlugin = {
    name: "orphan-reason",
    apiVersion: PluginApiVersion,
    classify: () => ({ shareReason: "orphan" }),
  };
  const validWithReason: QueuePlugin = {
    name: "valid-reason",
    apiVersion: PluginApiVersion,
    classify: () => ({ share: "shared", shareReason: "valid" }),
  };
  const coupledHost = new PluginHost(
    [orphanWithReason, validWithReason],
    [undefined, undefined],
    process.cwd(),
    () => {},
  );
  assert.deepStrictEqual(coupledHost.classify("cmd", { name: "gpu", forcedExclusive: false }, ctx), {
    share: "shared",
    shareReason: "valid",
  });
  const badExclReason: QueuePlugin = {
    name: "bad-excl-reason",
    apiVersion: PluginApiVersion,
    classify: () => ({ share: "exclusive", shareReason: "not-shared" }),
  };
  const badCharsReason: QueuePlugin = {
    name: "bad-chars-reason",
    apiVersion: PluginApiVersion,
    classify: () => ({ share: "shared", shareReason: "bad reason" }),
  };
  assert.deepStrictEqual(
    new PluginHost([badExclReason], [undefined], process.cwd(), () => {}).classify("cmd", null, ctx),
    { share: "exclusive" },
  );
  assert.deepStrictEqual(
    new PluginHost([badCharsReason], [undefined], process.cwd(), () => {}).classify("cmd", null, ctx),
    { share: "shared" },
  );
}
