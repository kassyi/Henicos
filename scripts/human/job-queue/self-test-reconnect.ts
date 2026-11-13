// Part 13 of the job queue self-test: a client that comes back. The checks drive the scheduler with raw connections in
// the orders that raced before records were held and re-bound: a report after the observation of the processes, a
// connection that drops between the grant and "started", a re-registration before the old connection's close, a pid
// that another process reuses.

import * as assert from "node:assert";
import { spawn } from "node:child_process";
import { StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import { deadPid, delay, registration, stopChild, type QueueFixture } from "./self-test-support.ts";

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
 * The connection of a running job drops and its command ends while the client is away. The observation of the
 * processes frees the slots first; the outcome waits for the client's report while the client lives (its dependent
 * and wait keep waiting), and is lost at once when the client is gone.
 */
async function checkReportAfterPoll(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient, waitFor } = fixture;
  const commandGone = deadPid();
  const stage = async (name: string, clientPid: number) => {
    const requestA = registration(`R1-${name}`, 1, clientPid);
    const a = await rawClient(port);
    a.send(requestA);
    const granted = await a.until("granted");
    assert.ok(granted !== null, `${name} is granted`);
    const b = await rawClient(port);
    b.send({ ...registration(`R1-${name}-after`, 1, process.pid), after: granted.id });
    assert.ok((await b.until("queued")) !== null);
    a.send({ type: "started", commandPid: commandGone, spawnedAt: Date.now() });
    await waitFor(port, "the command is known", (s) =>
      s.running.some((j) => j.id === granted.id && j.commandPid !== null) ? true : undefined,
    );
    a.close();
    // The observation of the processes reads the process table (PowerShell on Windows): seconds on a busy machine
    const freed = await waitFor(
      port,
      "the slot of the ended command is free",
      (s) => (s.used === 0 ? s : undefined),
      30000,
    );
    return { requestA, id: Number(granted.id), b, freed };
  };

  // The client lives: its report decides
  const alive = await stage("A", process.pid);
  assert.ok(
    alive.freed.waiting.some((j) => j.command === "R1-A-after" && j.blockedBy === alive.id),
    "the dependent still waits once the slot is free",
  );
  const waiting = await rawClient(port);
  waiting.send({ type: "await", id: alive.id });
  const reported = await request(port, {
    type: "report",
    id: alive.id,
    completed: true,
    exitCode: 0,
    pid: process.pid,
    queuedAt: alive.requestA.queuedAt,
  });
  assert.strictEqual(reported?.type, "reported");
  assert.strictEqual((await waiting.until("ended"))?.outcome, "succeeded", "wait tells the reported outcome");
  assert.ok((await alive.b.until("granted")) !== null, "the dependent runs after the reported success");
  assert.ok(!alive.b.messages.some((m) => m.type === "cancelled"), "and was never cancelled");
  alive.b.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await alive.b.until("done-ok")) !== null);

  // The client is gone: nobody can report, so lost is final in the same step that frees the slot
  const gone = await stage("C", deadPid());
  assert.ok(
    !gone.freed.waiting.some((j) => j.command === "R1-C-after"),
    "the dependent is cancelled as the slot is freed, without waiting for the grace",
  );
  assert.match(String((await gone.b.until("cancelled"))?.reason), /lost/);
  assert.strictEqual((await request(port, { type: "await", id: gone.id }))?.outcome, "lost");
  [waiting, alive.b, gone.b].forEach((c) => c.close());
}

/**
 * The connection of a running job drops between the grant and "started": the job is held, its dependent keeps waiting,
 * and the client takes it back under its id, by adopt once its command runs, or by request when it never saw the
 * grant.
 */
async function checkDropBeforeStarted(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient, waitFor } = fixture;
  const command = commandProcess(fixture);
  const stage = async (name: string) => {
    const requestA = registration(`R2-${name}`, 1, process.pid);
    const a = await rawClient(port);
    a.send(requestA);
    const granted = await a.until("granted");
    assert.ok(granted !== null, `${name} is granted`);
    const b = await rawClient(port);
    b.send({ ...registration(`R2-${name}-after`, 1, process.pid), after: granted.id });
    assert.ok((await b.until("queued")) !== null);
    a.close();
    const held = await waitFor(port, `${name} is held for its client`, (s) =>
      s.running.some((j) => j.id === granted.id && j.orphan) ? s : undefined,
    );
    assert.strictEqual(held.used, 1, "its slot stays counted");
    assert.ok(
      held.waiting.some((j) => j.command === `R2-${name}-after` && j.blockedBy === granted.id),
      "its dependent keeps waiting",
    );
    assert.ok(!b.messages.some((m) => m.type === "cancelled"), "and is not cancelled");
    return { requestA, id: Number(granted.id), b };
  };

  // (a) The client saw the grant: it adopts with its command
  const adopting = await stage("A");
  const back = await rawClient(port);
  const spawnedAt = Date.now();
  back.send({ ...adopting.requestA, type: "adopt", id: adopting.id, commandPid: command.pid ?? 0, spawnedAt });
  assert.strictEqual((await back.until("adopted"))?.id, adopting.id, "adopted under its id");
  back.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await adopting.b.until("granted")) !== null, "the dependent runs after it succeeded");
  adopting.b.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await adopting.b.until("done-ok")) !== null);

  // (b) The client never saw the grant: it queues again under its id and is granted again
  const requesting = await stage("B");
  const again = await rawClient(port);
  again.send({ ...requesting.requestA, id: requesting.id });
  assert.strictEqual((await again.until("granted"))?.id, requesting.id, "granted again under its id");
  assert.ok(!again.messages.some((m) => m.type === "queued"), "not queued as a new job");
  again.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await requesting.b.until("granted")) !== null, "the dependent runs after it succeeded");
  [back, again, adopting.b, requesting.b].forEach((c) => c.close());
  stopChild(command);
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
  await delay(300); // its close reaches the scheduler a moment later: it must change nothing
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
  // Its own id, not one the scheduler gave out yet (ids continue across the self-test through history.json)
  const ownId = Number(granted.id) + 1000;
  const other = await rawClient(port);
  other.send({
    ...registration("R6-second", 1, process.pid),
    type: "adopt",
    queuedAt: 2000,
    id: ownId,
    commandPid: otherCommand.pid ?? 0,
    spawnedAt: Date.now(),
  });
  const adopted = await other.until("adopted");
  assert.strictEqual(adopted?.id, ownId, "the other client keeps the id it asked for");
  const status = await statusOf(port);
  assert.deepStrictEqual(
    [status.used, status.running.map((j) => [j.id, j.command, j.orphan]).sort()],
    [
      2,
      [
        [ownId, "R6-second", false],
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
    [2, checkReportAfterPoll],
    [2, checkDropBeforeStarted],
    [1, checkReRequestBeforeClose],
    [2, checkAdoptOfAnotherClient],
  ];
  for (const [index, [slots, check]] of checks.entries()) {
    await freshScheduler(fixture, port + index, slots);
    await check(fixture, port + index);
    await request(port + index, { type: "stop" });
  }
}
