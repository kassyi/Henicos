// Checks of what a job is told while it waits and what the scheduler logs of it: what it waits for (Scheduler.waitFor),
// the latest expected end, status --here, the worktree digest and the environment hash.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import type * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { describeOutlook, describeWaitFor } from "./client-commands.ts";
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

export const QueueHintChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  checkWaitFor,
  checkDescribeWaitFor,
  checkLatestEnd,
  checkWaitForMessages,
};
