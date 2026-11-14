// Part 16 of the job queue self-test: pause and resume end to end. The scheduler starts no waiting job while paused
// (a freed slot stays free), running jobs finish, status shows the pause, it survives a scheduler restart, start <id>
// still works, pause --until-idle waits for the running jobs, and resume grants the waiting job.

import * as assert from "node:assert";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { EntryScript, StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import type { StatusReply } from "./protocol.ts";
import { delay, type QueueFixture } from "./self-test-support.ts";

export async function checkPause(fixture: QueueFixture, port: number): Promise<void> {
  const { env, client, gated, open, dir, ports, startScheduler, jobWith, runningWith, statusOf, waitFor } = fixture;
  const cli = (...args: string[]) =>
    spawnSync(process.execPath, [EntryScript, ...args, "--port", String(port)], {
      env,
      encoding: "utf8",
      windowsHide: true,
    });
  ports.push(port);
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const holdGate = path.join(dir, "gate-16a");
  const waitGate = path.join(dir, "gate-16b");
  const startGate = path.join(dir, "gate-16c");
  // The holder takes both slots: the next job waits
  const holder = client(port, gated(0, holdGate), ["--weight", "2"]);
  await runningWith(port, gated(0, holdGate));
  const waiter = client(port, gated(0, waitGate));
  assert.strictEqual((await jobWith(port, gated(0, waitGate))).state, "waiting", "both slots are taken");

  const paused = cli("pause");
  assert.strictEqual(paused.status, 0, paused.stdout + paused.stderr);
  assert.match(paused.stdout, /^paused: 1 running jobs finish, 1 waiting jobs wait/);
  // The running job ends and frees both slots; the waiting job is not started in them
  open(holdGate);
  assert.strictEqual(await holder.done, 0, "a running job finishes while paused");
  await waitFor(port, "the holder has ended", (s) => (s.running.length === 0 ? true : undefined));
  await delay(1500);
  const idle = await statusOf(port);
  assert.deepStrictEqual(
    [idle.paused, idle.running.length, idle.waiting.length, idle.used],
    [true, 0, 1, 0],
    "free slots, but the waiting job stays",
  );
  assert.match(cli("status").stdout, /PAUSED since \d\d:\d\d: no waiting job starts/);
  const shown = JSON.parse(cli("status", "--json").stdout) as StatusReply;
  assert.deepStrictEqual([shown.paused, typeof shown.pausedAt], [true, "number"]);
  const untilIdle = cli("pause", "--until-idle");
  assert.strictEqual(untilIdle.status, 0, "nothing runs: --until-idle returns at once");
  assert.match(untilIdle.stdout, /^already paused: .*\nno job runs\n$/);

  // The pause is in the state file: the next scheduler is paused too, and the waiting job is still waiting
  await stopScheduler(port);
  await startScheduler(port, 2);
  await waitFor(port, "the waiting job registers again", (s) =>
    s.paused === true && s.waiting.length === 1 && s.waiting.every((j) => !j.orphan) ? true : undefined,
  );
  assert.strictEqual((await statusOf(port)).running.length, 0, "a restarted scheduler is paused as well");

  // start <id> is the deliberate exception
  const starter = client(port, gated(0, startGate));
  const queued = await jobWith(port, gated(0, startGate));
  assert.strictEqual(queued.state, "waiting");
  const started = cli("start", String(queued.id));
  assert.strictEqual(started.status, 0, started.stdout + started.stderr);
  await runningWith(port, gated(0, startGate));
  assert.strictEqual((await statusOf(port)).waiting.length, 1, "only the job started by hand runs");

  // --until-idle blocks while a job runs, and ends when it has ended
  const blocking = spawn(process.execPath, [EntryScript, "pause", "--until-idle", "--port", String(port)], {
    env,
    stdio: "ignore",
    windowsHide: true,
  });
  fixture.track(blocking);
  const blockingEnd = new Promise<number | null>((resolve) => blocking.on("close", resolve));
  await delay(2500);
  assert.strictEqual(blocking.exitCode, null, "pause --until-idle waits while a job runs");
  open(startGate);
  assert.strictEqual(await starter.done, 0);
  assert.strictEqual(await blockingEnd, 0, "and exits 0 once none runs");

  // resume: the waiting job runs
  const resumed = cli("resume");
  assert.strictEqual(resumed.status, 0, resumed.stdout + resumed.stderr);
  assert.strictEqual(resumed.stdout.trim(), "resumed");
  await runningWith(port, gated(0, waitGate));
  assert.strictEqual((await statusOf(port)).paused, false);
  open(waitGate);
  assert.strictEqual(await waiter.done, 0);
  assert.strictEqual(cli("resume").stdout.trim(), "was not paused");
  await stopScheduler(port);
}

/** Stops the scheduler on `port`; the state file is then the last one it wrote. */
async function stopScheduler(port: number): Promise<void> {
  await request(port, { type: "stop" });
  await delay(300);
}
