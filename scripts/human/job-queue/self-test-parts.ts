// Parts 9, 11, 12 and 13 of the job queue self-test: the hrrn re-grant, GPU turns (and a cancel of a GPU holder),
// wait and start.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawnSync } from "node:child_process";
import { EntryScript, RegrantMs, StartupGraceMs, StateDirectoryVariable } from "./constants.ts";
import { request } from "./client-connect.ts";
import { RunHistory } from "./history.ts";
import { delay, removeDirectory, type QueueFixture } from "./self-test-support.ts";

export async function checkRegrant(fixture: QueueFixture, port: number): Promise<void> {
  const { env, client, sleeper, ports, startScheduler, statusOf, usedWithin } = fixture;
  const hrrnDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-hrrn-"));
  const longRun = sleeper(20000);
  const shortRun = sleeper(300);
  fs.writeFileSync(
    path.join(hrrnDir, "history.json"),
    JSON.stringify({
      commands: {
        [RunHistory.key(longRun)]: { runs: [20000], lastUsed: 1 },
        // typical 20 s, longest 30 s: it cannot be backfilled before the running job ends
        [RunHistory.key(shortRun)]: { runs: [10000, 20000, 30000], lastUsed: 1 },
      },
    }),
  );
  try {
    ports.push(port);
    await startScheduler(port, 2, { ...env, [StateDirectoryVariable]: hrrnDir });
    await delay(StartupGraceMs);
    client(port, longRun);
    assert.strictEqual(await usedWithin(port, 1, 5000), 1, "the long job runs before the wide one arrives");
    client(port, sleeper(301), ["--weight", "2"]); // unknown: one minute; waits for both slots
    await delay(4000);
    // The short job arrives behind the wide one, which has waited about 4 s longer: their ratios cross about 2 s
    // later ((t + 20) / 20 = (t + 4 + 60) / 60), so it first waits, then starts at a re-grant, not at enqueue.
    // Without the re-grant it would start only when the long job ends, at least 13 s after it is seen waiting.
    client(port, shortRun);
    let seenWaitingAt = 0;
    let startedAfterWaiting = Infinity;
    const pollUntil = Date.now() + 20000;
    while (Date.now() < pollUntil) {
      const now = await statusOf(port);
      if (seenWaitingAt === 0 && now.waiting.some((j) => j.command === shortRun)) {
        seenWaitingAt = Date.now();
      }
      if (now.running.some((j) => j.command === shortRun)) {
        startedAfterWaiting = seenWaitingAt === 0 ? -1 : Date.now() - seenWaitingAt;
        break;
      }
      await delay(200);
    }
    assert.ok(seenWaitingAt > 0, "the short job waited behind the wide one first");
    assert.ok(
      startedAfterWaiting >= 0 && startedAfterWaiting < RegrantMs + 5000,
      `the short job started ${startedAfterWaiting} ms after it was seen waiting, not at the next end`,
    );
    await request(port, { type: "cancel", all: true });
    await usedWithin(port, 0, 5000); // the cancelled clients are gone before the stop: none re-registers
    await request(port, { type: "stop" });
  } finally {
    removeDirectory(hrrnDir);
  }
}

export async function checkGpuTurnsEndToEnd(fixture: QueueFixture, port: number): Promise<void> {
  const { client, gated, open, dir, ports, startScheduler, statusOf, usedWithin, waitFor } = fixture;
  ports.push(port);
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const gate = (name: string): string => path.join(dir, `gate-11${name}`);
  const turnOf = (command: string) =>
    waitFor(port, `the GPU turn waits: ${command}`, (s) => s.waiting.find((j) => j.command === command && j.gpuTurn));
  // The first --gpu job takes the GPU: its turn runs, and the command under the lock registers as a GPU holder
  const first = client(port, gated(0, gate("a")), ["--gpu"]);
  await waitFor(port, "the first job holds the GPU", (s) =>
    s.running.some((j) => j.command === gated(0, gate("a")) && j.gpu) ? true : undefined,
  );
  // Later --gpu jobs wait in the scheduler, in arrival order
  const second = client(port, gated(0, gate("b")), ["--gpu"]);
  const secondTurn = await turnOf(gated(0, gate("b")));
  const third = client(port, gated(0, gate("c")), ["--gpu"]);
  const thirdTurn = await turnOf(gated(0, gate("c")));
  assert.strictEqual(secondTurn.weight, 0, "a waiting turn takes no slots");
  // A job waiting for the GPU can be cancelled (it is known to the scheduler before it holds the lock)
  const cancelled = await request(port, { type: "cancel", id: thirdTurn.id });
  assert.deepStrictEqual(cancelled?.cancelled, [thirdTurn.id]);
  assert.strictEqual(await third.done, 4, "a cancelled waiting --gpu job exits with 4");
  // A later job does not pass the one that waited longer
  const fourth = client(port, gated(0, gate("d")), ["--gpu"]);
  await turnOf(gated(0, gate("d")));
  open(gate("a"));
  assert.strictEqual(await first.done, 0);
  await waitFor(port, "the second job holds the GPU next", (s) =>
    s.running.some((j) => j.command === gated(0, gate("b")) && j.gpu) ? true : undefined,
  );
  assert.ok(
    (await statusOf(port)).waiting.some((j) => j.command === gated(0, gate("d"))),
    "the later job still waits",
  );
  // A cancel of the running turn reaches its GPU holder too, so the holder's client stops the command itself and
  // gpu-lock.js releases the lock (a tree kill under gpu-lock.js alone could leave the holder running)
  const secondHolder = (await statusOf(port)).running.find((j) => j.command === gated(0, gate("b")) && j.gpu);
  const cancelledTurn = await request(port, { type: "cancel", id: secondTurn.id });
  assert.deepStrictEqual(
    (cancelledTurn?.cancelled as number[]).sort((x, y) => x - y),
    [secondTurn.id, secondHolder?.id].sort((x = 0, y = 0) => x - y),
    "a cancel of the running turn lists its holder",
  );
  assert.strictEqual(await second.done, 4, "a cancelled running --gpu job exits with 4");
  open(gate("d"));
  assert.strictEqual(await fourth.done, 0);
  assert.strictEqual(fs.existsSync(path.join(dir, "gpu-lock")), false, "no GPU lock is left behind");
  // One run time per --gpu run: the command's, not also the turn's (which includes the wait for the lock)
  const history = JSON.parse(fs.readFileSync(path.join(dir, "history.json"), "utf8")) as {
    commands: Record<string, { runs: number[] }>;
  };
  assert.strictEqual(
    history.commands[RunHistory.key(gated(0, gate("a")))]?.runs.length,
    1,
    "one run recorded per --gpu run",
  );
  await usedWithin(port, 0, 15000);
  await checkCancelGpuHolder(fixture, port);
  await request(port, { type: "stop" });
}

/**
 * A cancel of the GPU holder that a --gpu run's command registered cancels the run too: both ids are listed, run exits
 * with 4 and wait tells "cancelled" for the run, not a failure of gpu-lock.js.
 */
async function checkCancelGpuHolder(fixture: QueueFixture, port: number): Promise<void> {
  const { client, sleeper, waitFor, waiter } = fixture;
  const command = sleeper(60001);
  const run = client(port, command, ["--gpu"]);
  const [turn, holder] = await waitFor(port, "the turn and its GPU holder run", (s) => {
    const jobs = s.running.filter((j) => j.command === command && j.commandPid !== null);
    const turnJob = jobs.find((j) => j.gpuTurn);
    const holderJob = jobs.find((j) => j.gpu);
    return turnJob !== undefined && holderJob !== undefined ? [turnJob, holderJob] : undefined;
  });
  const waiting = waiter(port, turn.id);
  const cancelled = await request(port, { type: "cancel", id: holder.id });
  assert.deepStrictEqual(
    (cancelled?.cancelled as number[]).sort(),
    [turn.id, holder.id].sort(),
    "the holder and the run whose turn started it",
  );
  assert.strictEqual(await run.done, 4, "run --gpu exits with 4");
  assert.strictEqual(await waiting.done, 4, "wait tells cancelled for the run");
  assert.deepStrictEqual(JSON.parse(waiting.output()), { id: turn.id, outcome: "cancelled" });
}

export async function checkWait(fixture: QueueFixture, port: number): Promise<void> {
  const { client, gated, open, exiting, dir, ports, startScheduler, jobWith, runningWith } = fixture;
  ports.push(port);
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const waiter = (id: number) => fixture.waiter(port, id);
  // A running job: wait returns when it ends, also when the scheduler restarted in between
  const gate = path.join(dir, "gate-12");
  const running = client(port, gated(0, gate));
  const runningJob = await runningWith(port, gated(0, gate));
  const waiting = waiter(runningJob.id);
  await delay(1500);
  assert.strictEqual(waiting.exitCode, null, "wait blocks while the job runs");
  await request(port, { type: "stop" });
  await delay(500);
  open(gate);
  assert.strictEqual(await running.done, 0);
  assert.strictEqual(await waiting.done, 0, "wait exits 0 for a job that succeeded, across a restart");
  assert.deepStrictEqual(JSON.parse(waiting.output()), { id: runningJob.id, outcome: "succeeded" });
  // A waiting job: a stop is not its end; wait returns when it has run after the restart
  const holdGate = path.join(dir, "gate-12b");
  const queuedGate = path.join(dir, "gate-12c");
  const holder = client(port, gated(0, holdGate), ["--weight", "2"]);
  await runningWith(port, gated(0, holdGate));
  const queued = client(port, gated(0, queuedGate));
  const queuedJob = await jobWith(port, gated(0, queuedGate));
  const waitingOnQueued = waiter(queuedJob.id);
  await delay(1000);
  await request(port, { type: "stop" });
  await delay(1500);
  assert.strictEqual(waitingOnQueued.exitCode, null, "a stop does not end a waiting job for wait");
  open(holdGate);
  open(queuedGate);
  assert.deepStrictEqual(await Promise.all([holder.done, queued.done, waitingOnQueued.done]), [0, 0, 0]);
  // A running job cancelled: its client stops the command and reports the end; wait tells "cancelled", not a failure
  const cancelGate = path.join(dir, "gate-12d");
  const toCancel = client(port, gated(0, cancelGate));
  const toCancelJob = await runningWith(port, gated(0, cancelGate));
  const waitingOnCancel = waiter(toCancelJob.id);
  await delay(500);
  await request(port, { type: "cancel", id: toCancelJob.id });
  assert.deepStrictEqual(
    await Promise.all([toCancel.done, waitingOnCancel.done]),
    [4, 4],
    "wait exits 4 for a cancelled job",
  );
  // An ended job answers from the record; an unknown id is a usage error
  const failing = client(port, exiting(3, 200));
  const failingJob = await jobWith(port, exiting(3, 200));
  assert.strictEqual(await failing.done, 3);
  assert.strictEqual(await waiter(failingJob.id).done, 1, "wait exits 1 for a job that failed");
  assert.strictEqual(await waiter(999999).done, 2, "wait exits 2 for an unknown id");
  await request(port, { type: "stop" });
}

/** start <id> grants a waiting job over the full pool; it refuses an unknown id. */
export async function checkStart(fixture: QueueFixture, port: number): Promise<void> {
  const { env, client, gated, open, dir, ports, startScheduler, jobWith, runningWith, statusOf } = fixture;
  ports.push(port);
  await startScheduler(port, 1);
  await delay(StartupGraceMs);
  const holdGate = path.join(dir, "gate-13a");
  const queuedGate = path.join(dir, "gate-13b");
  const holder = client(port, gated(0, holdGate));
  await runningWith(port, gated(0, holdGate));
  const queued = client(port, gated(0, queuedGate));
  const queuedJob = await jobWith(port, gated(0, queuedGate));
  assert.strictEqual(queuedJob.state, "waiting", "one slot: the second job waits");
  const started = spawnSync(process.execPath, [EntryScript, "start", String(queuedJob.id), "--port", String(port)], {
    env,
    encoding: "utf8",
    windowsHide: true,
  });
  assert.strictEqual(started.status, 0, started.stdout + started.stderr);
  assert.strictEqual(started.stdout.trim(), `#${queuedJob.id} started over the pool`);
  await runningWith(port, gated(0, queuedGate));
  assert.strictEqual((await statusOf(port)).used, 2, "it runs beside the first job, over the one slot");
  const unknown = await request(port, { type: "start", id: 999999 });
  assert.strictEqual(unknown?.code, "not-waiting");
  open(holdGate);
  open(queuedGate);
  assert.deepStrictEqual(await Promise.all([holder.done, queued.done]), [0, 0]);
  await request(port, { type: "stop" });
}
