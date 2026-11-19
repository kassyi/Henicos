// Checks of how a --gpu run shares the GPU (QueuedRun with a fake RunHost and a fake scheduler, as in
// self-test-client.ts): the turn asks for the share its plugins classify, once, on request and adopt alike; it takes
// the GPU lock as shared only when it asked for "shared" and the grant echoes exactly "shared"; every other case,
// an older scheduler included, takes it exclusively.

import type * as Assert from "node:assert";
import { parseArgs } from "./cli.ts";
import type { RunOptions } from "./client.ts";
import { QueuedRun } from "./client-queued-run.ts";
import { gpuLockArguments } from "./client-run-host.ts";
import { ProtocolVersion } from "./constants.ts";
import { PluginApiVersion, type QueuePlugin } from "./plugin-api.ts";
import { PluginHost } from "./plugin-host.ts";
import type { Incoming } from "./protocol.ts";
import { FakeHost, FakeScheduler, quietly, until } from "./self-test-client.ts";

type AssertModule = typeof Assert;

/** A plugin that answers "shared" for every command that asks for the GPU, and nothing else. */
const SharingPlugin: QueuePlugin = {
  name: "sharing",
  apiVersion: PluginApiVersion,
  classify: (_command, resource) => (resource?.name === "gpu" ? { share: "shared" } : undefined),
};

function sharingHost(): PluginHost {
  return new PluginHost([SharingPlugin], [undefined], process.cwd(), () => {});
}

function runOptions(extra: Partial<RunOptions> = {}): RunOptions {
  return {
    port: 0,
    slots: 2,
    memoryMb: 1024,
    policy: "hrrn",
    command: "dotnet test X --filter Name~Foo",
    commandWords: null,
    weight: 1,
    gpu: true,
    shell: "",
    timeoutMinutes: 0,
    after: 0,
    keepIfFailed: false,
    ...extra,
  };
}

interface Started {
  host: FakeHost;
  messages: Incoming[];
  registration: Incoming;
  send: (message: Record<string, unknown>) => void;
  drop: () => void;
}

/** Starts a QueuedRun against the fake scheduler and returns its first registration (connection `index`). */
async function startRun(
  scheduler: FakeScheduler,
  index: number,
  options: RunOptions,
  inGpuLock: boolean,
  gpuTurn: boolean,
  plugins: PluginHost | undefined,
): Promise<Started> {
  const host = new FakeHost();
  void new QueuedRun(options, { weight: 1, pin: null, before: null }, inGpuLock, gpuTurn, host, plugins).run();
  await until("the client connects", () => host.connects.length === 1);
  host.connects[0]?.resolve(await scheduler.connect());
  await until("the fake scheduler accepts", () => scheduler.accepted.length === index + 1);
  const accepted = scheduler.accepted[index];
  if (accepted === undefined) {
    throw new Error("no connection");
  }
  await until("the registration arrives", () => accepted.messages.length > 0);
  return {
    host,
    messages: accepted.messages,
    registration: accepted.messages[0] as Incoming,
    send: (message) => FakeScheduler.send(accepted, message),
    drop: () => accepted.socket.destroy(),
  };
}

function granted(id: number, share?: unknown): Record<string, unknown> {
  return {
    type: "granted",
    id,
    cpus: 1,
    capacity: 2,
    protocol: ProtocolVersion,
    ...(share !== undefined ? { share } : {}),
  };
}

/** The mode of the GPU lock a turn takes once granted with `grantShare` (absent when undefined). */
async function lockModeAfterGrant(
  scheduler: FakeScheduler,
  index: number,
  options: RunOptions,
  grantShare: unknown,
): Promise<{ registration: Incoming; mode: string | undefined; turn: number | undefined }> {
  const run = await startRun(scheduler, index, options, false, true, sharingHost());
  run.send(granted(30 + index, grantShare));
  await until("the turn takes the GPU lock", () => run.host.locks.length === 1);
  run.drop();
  return { registration: run.registration, mode: run.host.locks[0]?.mode, turn: run.host.locks[0]?.turn };
}

/**
 * A turn that asked for "shared" takes the lock as shared only when the grant echoes exactly "shared": no echo (an
 * older scheduler, or one that counts it as exclusive) or any other value takes it exclusively. --gpu-exclusive asks
 * for "exclusive" although the plugin answers "shared", and an echo then does not make it shared.
 */
async function checkTurnShare(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  try {
    const plain = await lockModeAfterGrant(scheduler, 0, runOptions(), undefined);
    assert.deepStrictEqual(
      [plain.registration.type, plain.registration.resource, plain.registration.share],
      ["request", "gpu", "shared"],
      "the turn asks for the share its plugins classify, for the GPU",
    );
    assert.deepStrictEqual([plain.mode, plain.turn], ["exclusive", 30], "no echo: the lock is exclusive");
    const echoed = await lockModeAfterGrant(scheduler, 1, runOptions(), "shared");
    assert.strictEqual(echoed.mode, "shared", "echo of shared: the lock is shared");
    for (const [index, value] of [
      [2, "exclusive"],
      [3, "Shared"],
      [4, true],
    ] as const) {
      const odd = await lockModeAfterGrant(scheduler, index, runOptions(), value);
      assert.strictEqual(odd.mode, "exclusive", `an echo of ${JSON.stringify(value)} is not shared`);
    }
    const forced = await lockModeAfterGrant(scheduler, 5, runOptions({ gpuExclusive: true }), "shared");
    assert.strictEqual(forced.registration.share, "exclusive", "--gpu-exclusive asks for exclusive");
    assert.strictEqual(forced.mode, "exclusive", "an echo it did not ask for does not make the lock shared");
  } finally {
    scheduler.close();
  }
}

/** A scheduler of an older checkout (error code "version"): the turn waits for the GPU lock directly, exclusively. */
async function checkOlderSchedulerLock(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  try {
    const run = await startRun(scheduler, 0, runOptions(), false, true, sharingHost());
    assert.strictEqual(run.registration.share, "shared");
    run.send({ type: "error", code: "version", message: "unsupported protocol version" });
    await until("the turn takes the GPU lock directly", () => run.host.locks.length === 1);
    assert.deepStrictEqual(
      [run.host.locks[0]?.mode, run.host.locks[0]?.turn],
      ["exclusive", 0],
      "an older scheduler: exclusive, without a turn",
    );
  } finally {
    scheduler.close();
  }
}

/**
 * The turn's re-registration after a scheduler restart (adopt) carries the same resource and share. A GPU holder
 * (the client inside the lock) and a job without a turn carry neither, and without plugins (JOB_QUEUE_PLUGINS=0) the
 * turn asks for no share, which the scheduler reads as exclusive.
 */
async function checkShareRegistrations(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  try {
    const turn = await startRun(scheduler, 0, runOptions(), false, true, sharingHost());
    turn.send(granted(40, "shared"));
    await until("the turn takes the GPU lock", () => turn.host.locks.length === 1);
    turn.drop();
    await until("the turn connects again", () => turn.host.connects.length === 2);
    turn.host.connects[1]?.resolve(await scheduler.connect());
    await until("the re-registration arrives", () => (scheduler.accepted[1]?.messages.length ?? 0) > 0);
    const adopt = scheduler.accepted[1]?.messages[0];
    assert.deepStrictEqual(
      [adopt?.type, adopt?.resource, adopt?.share],
      ["adopt", "gpu", "shared"],
      "adopt carries the same resource and share",
    );
    scheduler.accepted[1]?.socket.destroy();

    const holder = await startRun(scheduler, 2, runOptions({ gpu: false }), true, false, sharingHost());
    assert.deepStrictEqual(
      ["resource" in holder.registration, "share" in holder.registration],
      [false, false],
      "a GPU holder carries neither",
    );
    holder.drop();
    const plain = await startRun(scheduler, 3, runOptions({ gpu: false }), false, false, sharingHost());
    assert.deepStrictEqual(
      ["resource" in plain.registration, "share" in plain.registration],
      [false, false],
      "a job without a turn carries neither",
    );
    plain.drop();
    const none = new PluginHost([], [], process.cwd(), () => {});
    const bare = await startRun(scheduler, 4, runOptions(), false, true, none);
    assert.deepStrictEqual(
      [bare.registration.resource, "share" in bare.registration],
      ["gpu", false],
      "without plugins: no share",
    );
    bare.drop();
    const absent = await startRun(scheduler, 5, runOptions(), false, true, undefined);
    assert.strictEqual("share" in absent.registration, false, "no plugin host: no share");
    absent.drop();
    const silentPlugin: QueuePlugin = { name: "silent", apiVersion: PluginApiVersion, classify: () => ({}) };
    const silentHost = new PluginHost([silentPlugin], [undefined], process.cwd(), () => {});
    const silent = await startRun(scheduler, 6, runOptions(), false, true, silentHost);
    assert.strictEqual(silent.registration.share, "exclusive", "plugins that answer no share: exclusive");
    silent.drop();
  } finally {
    scheduler.close();
  }
}

/** gpu-lock.js gets --mode shared for a shared lock alone; without --mode it takes every seat. */
function checkGpuLockArguments(assert: AssertModule): void {
  const options = runOptions({ timeoutMinutes: 5 });
  const shared = gpuLockArguments(options, "shared", "inner");
  assert.deepStrictEqual(shared.slice(1), ["--timeout-minutes", "5", "--mode", "shared", "--", "inner"]);
  const exclusive = gpuLockArguments(options, "exclusive", "inner");
  assert.deepStrictEqual(exclusive.slice(1), ["--timeout-minutes", "5", "--", "inner"], "exclusive: no --mode");
}

/** run --gpu-exclusive: a GPU run (--gpu set too) that asks for the GPU exclusively. */
function checkGpuExclusiveFlag(assert: AssertModule): void {
  const options = parseArgs(["run", "--gpu-exclusive", "--", "dotnet test X"]);
  assert.deepStrictEqual([options.gpu, options.gpuExclusive], [true, true], "--gpu-exclusive implies --gpu");
  const plain = parseArgs(["run", "--gpu", "--", "dotnet test X"]);
  assert.deepStrictEqual([plain.gpu, plain.gpuExclusive === true], [true, false]);
}

export const ShareClientChecks: Record<string, (assert: AssertModule) => Promise<void>> = {
  checkTurnShare: (assert) => quietly(() => checkTurnShare(assert)),
  checkOlderSchedulerLock: (assert) => quietly(() => checkOlderSchedulerLock(assert)),
  checkShareRegistrations: (assert) => quietly(() => checkShareRegistrations(assert)),
  checkGpuExclusiveFlag: async (assert) => checkGpuExclusiveFlag(assert),
  checkGpuLockArguments: async (assert) => checkGpuLockArguments(assert),
};
