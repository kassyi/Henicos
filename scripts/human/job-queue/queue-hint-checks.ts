// Checks of what a job is told while it waits and what the scheduler logs of it: what it waits for (Scheduler.waitFor),
// the latest expected end, status --here, the worktree digest and the environment hash.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import type * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { parseArgs } from "./cli.ts";
import { describeOutlook, describeWaitFor, isJobHere } from "./client-commands.ts";
import type { RunOptions } from "./client.ts";
import { QueuedRun } from "./client-queued-run.ts";
import { JobVariable, ProtocolVersion } from "./constants.ts";
import { differingNames, environmentHash, VolatileEnv } from "./environment-hash.ts";
import { PluginApiVersion, type QueuePlugin } from "./plugin-api.ts";
import { PluginHost } from "./plugin-host.ts";
import type { StatusJob } from "./protocol.ts";
import { FakeHost, FakeScheduler, until } from "./self-test-client.ts";
import { worktreeKey } from "./system.ts";
import type { ResourceSpec } from "./resources.ts";
import { Scheduler, type SchedulerJob } from "./scheduler.ts";
import { job, times } from "./scheduler-checks.ts";
import { connectRaw, registration, removeDirectory } from "./self-test-support.ts";
import { JobServer } from "./server.ts";

type AssertModule = typeof Assert;

const gpu = (slots: number, probe: boolean): ResourceSpec => ({
  name: "gpu",
  slots,
  probe: probe ? { argv: ["probe"], min: 3072, timeoutMs: 2000, settleMs: 15000 } : null,
});

const turn = (id: number, share: "shared" | "exclusive"): SchedulerJob =>
  Object.assign(job(id, 2, id), { gpuTurn: true, share });

/** Each answer of waitFor, in the order grant decides: predecessor, client, restart, pause, turn, slots, memory. */
function checkWaitFor(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(2, 0, "fifo", 1000);
  const [a, b, c] = [job(1, 2, 1), job(2, 1, 2), job(3, 1, 3)];
  s.enqueue(a, 0);
  s.enqueue(b, 0);
  assert.strictEqual(s.waitFor(a, 0), undefined, "a running job waits for nothing");
  assert.strictEqual(s.waitFor(b, 0), "slots", "every slot in use");
  c.after = 1;
  s.enqueue(c, 0);
  assert.strictEqual(s.waitFor(c, 0), "after", "its predecessor runs, ahead of the slots");
  c.after = null;
  c.detached = true;
  assert.strictEqual(s.waitFor(c, 0), "away", "its client is away");
  c.detached = false;
  s.paused = true;
  assert.strictEqual(s.waitFor(c, 0), "paused");
  s.paused = false;
  s.holdUntil = 100;
  assert.strictEqual(s.waitFor(c, 50), "restart", "the startup grace");
  assert.strictEqual(s.waitFor(c, 100), "slots", "after the grace");
  s.holdUntil = 0;

  const memory = new Scheduler<SchedulerJob>(4, 0, "fifo", 1000);
  const [m1, m2, m3] = [
    Object.assign(job(1, 1, 1), { memoryMb: 800 }),
    Object.assign(job(2, 1, 2), { memoryMb: 800 }),
    job(3, 1, 3),
  ];
  memory.enqueue(m1, 0);
  memory.enqueue(m2, 0);
  memory.add(m3);
  assert.strictEqual(memory.waitFor(m2, 0), "memory", "slots are free, the memory pool is not");
  assert.strictEqual(memory.waitFor(m3, 0), "in-line", "room for it, but fifo keeps it behind m2");

  const turns = new Scheduler<SchedulerJob>(4, 0, "fifo", Infinity, [gpu(2, true)]);
  const [t1, t2, t3, t4] = [turn(1, "shared"), turn(2, "shared"), turn(3, "exclusive"), turn(4, "shared")];
  [t1, t2, t3, t4].forEach((t) => turns.enqueue(t, 0));
  assert.strictEqual(turns.waitFor(t2, 0), "gpu-memory", "the first waiting shared turn waits for the probe");
  assert.strictEqual(turns.waitFor(t3, 0), "gpu", "a turn behind the first waits for the GPU");
  assert.strictEqual(turns.waitFor(t4, 0), "gpu", "so does a shared one: only the first is measured for");
  const exclusive = new Scheduler<SchedulerJob>(4, 0, "fifo", Infinity, [gpu(2, true)]);
  const [e1, e2] = [turn(1, "exclusive"), turn(2, "shared")];
  exclusive.enqueue(e1, 0);
  exclusive.enqueue(e2, 0);
  assert.strictEqual(exclusive.waitFor(e2, 0), "gpu", "an exclusive turn runs: no probe helps");
}

/** The words of each answer, the predecessor and the resource named; nothing for a scheduler that did not say. */
function checkDescribeWaitFor(assert: AssertModule): void {
  assert.strictEqual(describeWaitFor({ waitFor: "after", blockedBy: 821 }), "waits for j0821 to end");
  assert.strictEqual(describeWaitFor({ waitFor: "gpu", waitResource: "gpu" }), "waits for a turn at gpu");
  assert.strictEqual(describeWaitFor({ waitFor: "gpu-memory", waitResource: "x" }), "waits for free memory on x");
  assert.strictEqual(describeWaitFor({ waitFor: "slots" }), "waits for CPU slots");
  assert.strictEqual(describeWaitFor({ waitFor: "memory" }), "waits for the memory budget of the test runs");
  assert.strictEqual(describeWaitFor({}), "", "an older scheduler");
  assert.strictEqual(describeWaitFor({ waitFor: "something new" }), "", "a newer one");
}

/** The latest end: from the longest recorded time, for running and waiting jobs; none without a record. */
function checkLatestEnd(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(1, 0, "fifo");
  const [a, b, c] = [job(1, 1, 1, false, times(60, 300)), job(2, 1, 2, false, times(60, 90)), job(3, 1, 3)];
  s.enqueue(a, 5000);
  s.add(b);
  s.add(c);
  const forecast = s.forecast(5000);
  assert.deepStrictEqual(forecast.get(a), { startAt: 5000, endAt: 65000, latestEndAt: 305000 }, "a running job");
  assert.deepStrictEqual(forecast.get(b), { startAt: 65000, endAt: 125000, latestEndAt: 155000 }, "from its start");
  assert.strictEqual(forecast.get(c)?.latestEndAt, undefined, "no record, no latest end");
  // Said only a minute or more after the expected end
  const clock = (time: number): string => new Date(time).toTimeString().slice(0, 5);
  const view = { state: "running", ahead: 0, startAt: 0, endAt: 600000 };
  assert.strictEqual(
    describeOutlook({ ...view, latestEndAt: 660000 }, 0),
    `expected end ~${clock(600000)} (in 10m), at the latest ~${clock(660000)}`,
  );
  assert.strictEqual(describeOutlook({ ...view, latestEndAt: 659999 }, 0), `expected end ~${clock(600000)} (in 10m)`);
  assert.strictEqual(describeOutlook(view, 0), `expected end ~${clock(600000)} (in 10m)`, "an older scheduler");
}

/** A scheduler of one slot on a private port and state directory, with its log lines kept, for `body`. */
async function withServer(body: (port: number, log: string[], server: JobServer) => Promise<void>): Promise<void> {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-hints-"));
  const log: string[] = [];
  const server = new JobServer({
    slots: 1,
    memoryMb: 1e9,
    policy: "fifo",
    port: 0,
    stateDir: directory,
    log: (text) => log.push(text),
  });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  try {
    await body((server.server.address() as net.AddressInfo).port, log, server);
  } finally {
    server.stopping = true;
    server.server.close();
    removeDirectory(directory);
  }
}

/** The scheduler tells a queued job, its waiting notices and status what it waits for. */
async function checkWaitForMessages(assert: AssertModule): Promise<void> {
  await withServer(async (port, _log, server) => {
    const first = await connectRaw(port);
    first.send(registration("A", 1, process.pid));
    const granted = await first.until("granted");
    const running = server.records.get(Number(granted?.id));
    assert.ok(running);
    running.estimate = times(60, 300);
    const second = await connectRaw(port);
    second.send(registration("B", 1, process.pid));
    const queued = await second.until("queued");
    assert.strictEqual(queued?.waitFor, "slots", "the queued answer says it");
    const ask = await connectRaw(port);
    ask.send({ type: "status" });
    const status = await ask.until("status");
    const waiting = (status?.waiting as { waitFor?: string }[] | undefined) ?? [];
    assert.deepStrictEqual(
      waiting.map((j) => j.waitFor),
      ["slots"],
      "and status",
    );
    const ran = (status?.running as { grantedAt: number; latestEndAt?: number }[] | undefined) ?? [];
    assert.strictEqual(ran[0]?.latestEndAt, (ran[0]?.grantedAt ?? 0) + 300000, "the latest end of the running job");
    [first, second, ask].forEach((client) => client.close());
  });
}

/** status --here: a job of the worktree, by its worktree, or for an older client by its directory inside it. */
function checkHere(assert: AssertModule): void {
  const root = process.platform === "win32" ? "X:__dummy__" : "/__dummy__";
  const here = worktreeKey(path.join(root, "repo"));
  const at = (worktree: string | undefined, cwd: string): StatusJob =>
    ({ worktree: worktree === undefined ? undefined : worktreeKey(worktree), cwd }) as unknown as StatusJob;
  const inside = path.join(root, "repo", "src");
  assert.strictEqual(isJobHere(at(path.join(root, "repo"), inside), here), true, "its worktree");
  assert.strictEqual(
    isJobHere(at(path.join(root, "repo", "tmp", "worktree", "x"), inside), here),
    false,
    "a worktree inside",
  );
  assert.strictEqual(isJobHere(at(path.join(root, "repo2"), path.join(root, "repo2")), here), false, "a sibling");
  assert.strictEqual(isJobHere(at(undefined, inside), here), true, "an older client: its directory is inside");
  assert.strictEqual(isJobHere(at(undefined, path.join(root, "repo2")), here), false, "not a prefix match");
  const parsed = parseArgs(["status", "--here", "--json"]);
  assert.deepStrictEqual([parsed.here, parsed.json], [true, true], "--here goes with --json");
}

/** What `body` writes to stderr, kept off the console. */
async function stderrOf(body: () => Promise<void>): Promise<string> {
  const write = process.stderr.write;
  let text = "";
  process.stderr.write = ((chunk: string | Uint8Array) => {
    text += String(chunk);
    return true;
  }) as typeof process.stderr.write;
  try {
    await body();
  } finally {
    process.stderr.write = write;
  }
  return text;
}

/**
 * The digest at the queue and the one at the start: one line when they differ, nothing when they match or either is
 * missing, and the command starts without waiting for the digest. A GPU holder takes none.
 */
async function checkWorktreeChange(assert: AssertModule): Promise<void> {
  const options: RunOptions = {
    port: 0,
    slots: 2,
    memoryMb: 1024,
    policy: "hrrn",
    command: "dotnet build X",
    commandWords: null,
    weight: 1,
    gpu: false,
    shell: "",
    timeoutMinutes: 0,
    after: 0,
    keepIfFailed: false,
  };
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  process.env.JOB_QUEUE_CHECK_SESSION = "a session";
  try {
    const cases: [(string | undefined)[], boolean, boolean][] = [
      [["a", "b"], false, true],
      [["a", "a"], false, false],
      [["a", undefined], false, false],
      [[undefined, "b"], false, false],
      [["a", "b"], true, false],
    ];
    for (const [index, [digests, inGpuLock, told]] of cases.entries()) {
      const calls: number[] = [];
      // The digest at the start resolves only once the command has started
      let release = (): void => {};
      const started = new Promise<void>((resolve) => (release = resolve));
      const plugin: QueuePlugin = {
        name: "digests",
        apiVersion: PluginApiVersion,
        volatileEnv: () => ["JOB_QUEUE_CHECK_SESSION"],
        contentDigest: async () => {
          const call = calls.push(calls.length) - 1;
          if (call === 1) {
            await started;
          }
          const digest = digests[call];
          return digest === undefined ? undefined : { digest, ms: 1 };
        },
      };
      const plugins = new PluginHost([plugin], [undefined], process.cwd(), () => {});
      const host = new FakeHost();
      const text = await stderrOf(async () => {
        void new QueuedRun(
          { ...options },
          { weight: 1, pin: null, before: null },
          inGpuLock,
          false,
          host,
          plugins,
        ).run();
        await until("the client connects", () => host.connects.length === 1);
        host.connects[0]?.resolve(await scheduler.connect());
        await until("the registration arrives", () => (scheduler.accepted[index]?.messages.length ?? 0) > 0);
        const accepted = scheduler.accepted[index];
        if (accepted === undefined) {
          throw new Error("no connection");
        }
        // The registration carries the environment's hash, without the variables the plugins name
        const keys = (accepted.messages[0]?.envKeys ?? {}) as Record<string, string>;
        assert.match(String(accepted.messages[0]?.envHash), /^[0-9a-f]{12}$/);
        assert.ok(Object.keys(keys).length > 0, "the variables by name");
        assert.ok(!("JOB_QUEUE_CHECK_SESSION" in keys), "a plugin's volatile variable is left out");
        FakeScheduler.send(accepted, { type: "granted", id: 18739, cpus: 1, capacity: 2, protocol: ProtocolVersion });
        await until("the command starts before the digest", () => host.commands.length === 1);
        release();
        if (!inGpuLock) {
          await until("both digests were taken", () => calls.length === 2);
        }
        await new Promise((resolve) => setTimeout(resolve, 50));
        accepted.socket.destroy();
        // The client notices the drop and tries to come back (to a host that never connects): keep its note here
        await new Promise((resolve) => setTimeout(resolve, 100));
      });
      const line = /j8739: the worktree changed after it was queued \(\d\d:\d\d\); it builds what is there now/;
      assert.strictEqual(line.test(text), told, `case ${index}: ${JSON.stringify(digests)}, in the lock ${inGpuLock}`);
      if (inGpuLock) {
        assert.deepStrictEqual(calls, [], "a GPU holder takes no digest");
      }
    }
  } finally {
    delete process.env.JOB_QUEUE_CHECK_SESSION;
    scheduler.close();
  }
}

/** The hash per name and in all: values change it, the volatile names and the given ones do not. */
function checkEnvironmentHash(assert: AssertModule): void {
  const env = { PATH: "/bin", HOME: "/home/x", [JobVariable]: "12", SHLVL: "2", CHAT: "a" };
  const base = environmentHash(env, [...VolatileEnv, "CHAT"]);
  assert.deepStrictEqual(Object.keys(base.keys), ["HOME", "PATH"], "sorted, without the volatile names");
  assert.match(base.hash, /^[0-9a-f]{12}$/);
  assert.ok(
    Object.values(base.keys).every((value) => /^[0-9a-f]{8}$/.test(value)),
    "no value is sent",
  );
  const same = environmentHash({ ...env, [JobVariable]: "13", SHLVL: "3", CHAT: "b" }, [...VolatileEnv, "CHAT"]);
  assert.strictEqual(same.hash, base.hash, "the left-out variables change nothing");
  const other = environmentHash({ ...env, PATH: "/usr/bin" }, [...VolatileEnv, "CHAT"]);
  assert.notStrictEqual(other.hash, base.hash, "a value changes the hash");
  assert.deepStrictEqual(differingNames(base.keys, other.keys), ["PATH"]);
  const added = environmentHash({ ...env, EXTRA: "1" }, [...VolatileEnv, "CHAT"]);
  assert.deepStrictEqual(differingNames(base.keys, added.keys), ["EXTRA"], "a name in one of them only");
  if (process.platform === "win32") {
    assert.deepStrictEqual(
      Object.keys(environmentHash({ Path: "x", shlvl: "1" }, VolatileEnv).keys),
      ["Path"],
      "names without case on Windows",
    );
  }
}

/** The log line of a queued job: cwd, worktree and env; the next run of the same command names what differs. */
async function checkEnvironmentLog(assert: AssertModule): Promise<void> {
  await withServer(async (port, log) => {
    const queue = async (command: string, envKeys: unknown): Promise<void> => {
      const client = await connectRaw(port);
      client.send({ ...registration(command, 1, process.pid), cwd: "here", envHash: "abc", envKeys });
      await client.until("queued");
      client.close();
    };
    await queue("A", { PATH: "11111111", HOME: "22222222" });
    assert.match(log.find((line) => line.startsWith("queue")) ?? "", /pid \d+ cwd here worktree \S+ env abc: A$/);
    await queue("A", { PATH: "33333333", HOME: "22222222", EXTRA: "44444444" });
    const differs = log.filter((line) => line.includes("env differs"));
    assert.strictEqual(differs.length, 1);
    assert.match(differs[0] ?? "", /^j\d{4}: env differs from j\d{4} \(same command and worktree\): EXTRA, PATH$/);
    await queue("A", { PATH: "33333333", HOME: "22222222", EXTRA: "44444444" });
    await queue("B", { PATH: "55555555" });
    assert.strictEqual(log.filter((line) => line.includes("env differs")).length, 1, "the same, or another command");
    await queue("C", { PATH: "not a hash" });
    assert.match(log.filter((line) => line.startsWith("queue")).at(-1) ?? "", /worktree \S+: C$/, "malformed: no env");
  });
}

export const QueueHintChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  checkWaitFor,
  checkDescribeWaitFor,
  checkLatestEnd,
  checkWaitForMessages,
  checkHere,
  checkWorktreeChange,
  checkEnvironmentHash,
  checkEnvironmentLog,
};
