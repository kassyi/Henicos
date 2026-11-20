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

/** A plugin that answers "shared" for every command that asks for the GPU, and names a chat session for every job. */
const SharingPlugin: QueuePlugin = {
  name: "sharing",
  apiVersion: PluginApiVersion,
  classify: (_command, resource) => (resource?.name === "gpu" ? { share: "shared" } : undefined),
  session: () => "chat-of-the-plugin",
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
    assert.strictEqual(
      plain.registration.session,
      "chat-of-the-plugin",
      "the registration carries the plugins' session",
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

/**
 * The asynchronous checkShare hook: asked once, before the first registration, for a turn that classify did not
 * share and that did not ask for --gpu-exclusive; "shared" makes the registration (and the adopt after a scheduler
 * restart) ask for "shared"; a throw, a time out, an invalid value or no answer leaves it exclusive; the next plugin
 * is asked when one answers undefined.
 */
async function checkCheckShare(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  let index = 0;
  try {
    const asked: string[] = [];
    const warnings: string[] = [];
    const hostOf = (
      checkShare: ((command: string) => Promise<unknown>) | undefined,
      classifyShared = false,
      extra: QueuePlugin[] = [],
    ): PluginHost => {
      const plugin: QueuePlugin = {
        name: "late",
        apiVersion: PluginApiVersion,
        classify: () => (classifyShared ? { share: "shared" } : {}),
        ...(checkShare !== undefined
          ? {
              checkShare: (command: string) => {
                asked.push(command);
                return checkShare(command) as Promise<"shared" | undefined>;
              },
            }
          : {}),
      };
      const host = new PluginHost(
        [plugin, ...extra],
        [undefined, ...extra.map(() => undefined)],
        process.cwd(),
        (text) => warnings.push(text),
      );
      host.checkShareTimeoutMs = 100;
      return host;
    };
    const shareOf = async (plugins: PluginHost, options: RunOptions = runOptions()): Promise<unknown> => {
      const run = await startRun(scheduler, index++, options, false, true, plugins);
      run.drop();
      return run.registration.share;
    };

    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve("shared"))), "shared", "checkShare shared");
    assert.deepStrictEqual(asked, ["dotnet test X --filter Name~Foo"], "asked with the command, once");
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve("exclusive"))), "exclusive");
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve(undefined))), "exclusive", "no answer");
    assert.strictEqual(await shareOf(hostOf(undefined)), "exclusive", "no checkShare hook");

    warnings.length = 0;
    assert.strictEqual(await shareOf(hostOf(() => Promise.reject(new Error("boom")))), "exclusive", "a rejection");
    assert.strictEqual(
      await shareOf(
        hostOf(() => {
          throw new Error("sync boom");
        }),
      ),
      "exclusive",
      "a throw",
    );
    assert.strictEqual(await shareOf(hostOf(() => new Promise<never>(() => {}))), "exclusive", "a time out");
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve("Shared"))), "exclusive", "an invalid value");
    assert.strictEqual(warnings.length, 4, "each of the four says why on one line");
    assert.match(warnings.join("\n"), /checkShare failed: boom.*exclusively/);
    assert.match(warnings.join("\n"), /timed out after 100 ms/);
    assert.match(warnings.join("\n"), /invalid value/);

    // The host itself answers "exclusive" (not undefined) for a failure, a time out and an invalid value
    const direct = { cwd: ".", worktree: ".", configRoot: ".", shell: "bash" as const, env: {}, log: () => {} };
    for (const hook of [
      () => Promise.reject(new Error("x")),
      () => new Promise<never>(() => {}),
      () => Promise.resolve(true),
    ]) {
      assert.strictEqual(await hostOf(hook).checkShare("c", direct), "exclusive");
    }
    assert.strictEqual(await hostOf(() => Promise.resolve(undefined)).checkShare("c", direct), undefined);
    asked.length = 0;
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve("shared"), true)), "shared", "classify shared");
    // A failing or invalid answer ends the asking: a later plugin that would say "shared" is not asked
    const later: QueuePlugin = {
      name: "later",
      apiVersion: PluginApiVersion,
      checkShare: () => Promise.resolve("shared"),
    };
    assert.strictEqual(await shareOf(hostOf(() => Promise.reject(new Error("x")), false, [later])), "exclusive");
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve("Shared"), false, [later])), "exclusive");
    asked.length = 0;
    assert.strictEqual(
      await shareOf(
        hostOf(() => Promise.resolve("shared")),
        runOptions({ gpuExclusive: true }),
      ),
      "exclusive",
      "--gpu-exclusive wins",
    );
    assert.deepStrictEqual(asked, [], "not asked when classify shared or --gpu-exclusive");
    const second: QueuePlugin = {
      name: "second",
      apiVersion: PluginApiVersion,
      checkShare: () => Promise.resolve("shared"),
    };
    assert.strictEqual(await shareOf(hostOf(() => Promise.resolve(undefined), false, [second])), "shared", "next");
    const ends: QueuePlugin = { ...second, name: "never", checkShare: () => Promise.reject(new Error("not asked")) };
    assert.strictEqual(
      await shareOf(hostOf(() => Promise.resolve("exclusive"), false, [ends])),
      "exclusive",
      "the first answer ends the asking",
    );

    // A holder (the client inside the lock) and a job without a turn are never asked
    asked.length = 0;
    const holder = await startRun(
      scheduler,
      index++,
      runOptions({ gpu: false }),
      true,
      false,
      hostOf(() => Promise.resolve("shared")),
    );
    holder.drop();
    assert.deepStrictEqual(asked, [], "a GPU holder is not asked");

    // The adopt after a scheduler restart carries the answer without asking again
    const turn = await startRun(
      scheduler,
      index++,
      runOptions(),
      false,
      true,
      hostOf(() => Promise.resolve("shared")),
    );
    assert.strictEqual(turn.registration.share, "shared");
    turn.send(granted(60, "shared"));
    await until("the turn takes the GPU lock", () => turn.host.locks.length === 1);
    turn.drop();
    await until("the turn connects again", () => turn.host.connects.length === 2);
    turn.host.connects[1]?.resolve(await scheduler.connect());
    const reconnected = scheduler.accepted[index];
    index++;
    await until("the re-registration arrives", () => (reconnected?.messages.length ?? 0) > 0);
    assert.deepStrictEqual(
      [reconnected?.messages[0]?.type, reconnected?.messages[0]?.share, asked.length],
      ["adopt", "shared", 1],
      "adopt keeps the share and does not ask again",
    );
    reconnected?.socket.destroy();
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
  checkCheckShare: (assert) => quietly(() => checkCheckShare(assert)),
  checkGpuExclusiveFlag: async (assert) => checkGpuExclusiveFlag(assert),
  checkGpuLockArguments: async (assert) => checkGpuLockArguments(assert),
};
