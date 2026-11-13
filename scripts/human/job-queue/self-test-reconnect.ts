// Part 13 of the job queue self-test: a client that comes back. The checks drive the scheduler with raw connections in
// the orders that raced before records were re-bound (a re-registration before the old connection's close, a pid that
// another process reuses).

import * as assert from "node:assert";
import { spawn } from "node:child_process";
import { StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import { delay, registration, stopChild, type QueueFixture } from "./self-test-support.ts";

/** A process that stands for a running command (Unix: a group leader, as the command a client starts). */
function commandProcess(fixture: QueueFixture) {
  return fixture.track(
    spawn(process.execPath, ["-e", "setTimeout(function(){},60000)"], {
      windowsHide: true,
      detached: process.platform !== "win32",
    }),
  );
}

/**
 * A waiting client whose connection dropped queues again before the scheduler sees the old connection close: it gets
 * its own record back under its id, and the late close changes nothing.
 */
async function checkReRequestBeforeClose(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient, statusOf } = fixture;
  const holder = await rawClient(port);
  holder.send(registration("R3-holder", 1, process.pid));
  assert.ok((await holder.until("granted")) !== null, "the holder fills the slot");
  const requestA = registration("R3-A", 1, process.pid);
  const a = await rawClient(port);
  a.send(requestA);
  const queued = await a.until("queued");
  assert.ok(queued !== null);
  const again = await rawClient(port);
  again.send({ ...requestA, id: queued.id });
  const requeued = await again.until("queued");
  assert.strictEqual(requeued?.id, queued.id, "the same client queues again under its id");
  await a.closed; // the scheduler ended the old connection
  const status = await statusOf(port);
  const jobs = status.running.concat(status.waiting).filter((j) => j.command === "R3-A");
  assert.deepStrictEqual(
    jobs.map((j) => [j.id, j.orphan]),
    [[queued.id, false]],
    "one job, bound to the new connection",
  );
  again.close();
  holder.close();
}

/**
 * A process that reuses the pid of a lost client re-registers its own running job: the lost client's job keeps its
 * slot and its command, and the new one gets the id it asked for.
 */
async function checkAdoptOfAnotherClient(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient, statusOf, waitFor } = fixture;
  const orphanCommand = commandProcess(fixture);
  const otherCommand = commandProcess(fixture);
  const first = await rawClient(port);
  first.send({ ...registration("R6-first", 1, process.pid), queuedAt: 1000 });
  const granted = await first.until("granted");
  assert.ok(granted !== null);
  first.send({ type: "started", commandPid: orphanCommand.pid ?? 0, spawnedAt: Date.now() });
  await waitFor(port, "the command is known", (s) => (s.running.some((j) => j.commandPid !== null) ? true : undefined));
  first.close();
  await waitFor(port, "the first client is seen lost", (s) =>
    s.running.some((j) => j.id === granted.id && j.orphan) ? true : undefined,
  );
  const other = await rawClient(port);
  other.send({
    ...registration("R6-second", 1, process.pid),
    type: "adopt",
    queuedAt: 2000,
    id: 77,
    commandPid: otherCommand.pid ?? 0,
    spawnedAt: Date.now(),
  });
  const adopted = await other.until("adopted");
  assert.strictEqual(adopted?.id, 77, "the other client keeps the id it asked for");
  const status = await statusOf(port);
  assert.deepStrictEqual(
    [status.used, status.running.map((j) => [j.id, j.command, j.orphan]).sort()],
    [
      2,
      [
        [77, "R6-second", false],
        [granted.id, "R6-first", true],
      ].sort(),
    ],
    "the lost client's job keeps its slot",
  );
  assert.strictEqual(orphanCommand.exitCode, null, "and its command runs on");
  other.close();
  stopChild(orphanCommand);
  stopChild(otherCommand);
}

/** Starts a scheduler with `slots` on a port of its own, past its startup grace. */
async function freshScheduler(fixture: QueueFixture, port: number, slots: number): Promise<void> {
  fixture.ports.push(port);
  await fixture.startScheduler(port, slots);
  await delay(StartupGraceMs);
}

/** Each check on a scheduler of its own, on `port` and the ports after it. */
export async function checkReconnects(fixture: QueueFixture, port: number): Promise<void> {
  const checks: [number, (fixture: QueueFixture, port: number) => Promise<void>][] = [
    [1, checkReRequestBeforeClose],
    [2, checkAdoptOfAnotherClient],
  ];
  for (const [index, [slots, check]] of checks.entries()) {
    await freshScheduler(fixture, port + index, slots);
    await check(fixture, port + index);
    await request(port + index, { type: "stop" });
  }
}
