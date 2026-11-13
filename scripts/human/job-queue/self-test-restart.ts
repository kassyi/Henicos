// Part 14 of the job queue self-test: what a restarted scheduler restores from its state file. Every job of a current
// file is held from the first event (no startup grace); an older scheduler's file and a missing one keep the grace.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as path from "node:path";
import { spawn } from "node:child_process";
import { StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import { delay, registration, stopChild, type QueueFixture, type RawClient } from "./self-test-support.ts";

/** Stops the scheduler on `port` and starts the next one there, from the state file the first one wrote. */
async function restart(fixture: QueueFixture, port: number, slots: number, clients: RawClient[]): Promise<void> {
  await request(port, { type: "stop" });
  await Promise.all(clients.map((c) => c.closed));
  await delay(300);
  await fixture.startScheduler(port, slots);
}

/**
 * A restart on a 1-slot scheduler while a job runs and its client lives but has not re-registered: the job keeps its
 * slot (a new job waits), the waiting job of the earlier scheduler keeps its place without being granted while its
 * client is away, and the running job's client ends it by a report or takes it back by adopt.
 */
async function checkRestartHoldsTheJobs(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient, statusOf, waitFor } = fixture;
  fixture.ports.push(port);
  await fixture.startScheduler(port, 1);
  await delay(StartupGraceMs);
  const command = fixture.track(
    spawn(process.execPath, ["-e", "setTimeout(function(){},60000)"], {
      windowsHide: true,
      detached: process.platform !== "win32",
    }),
  );
  const requestA = registration("S-A", 1, process.pid);
  const a = await rawClient(port);
  a.send(requestA);
  const granted = await a.until("granted");
  assert.ok(granted !== null);
  a.send({ type: "started", commandPid: command.pid ?? 0, spawnedAt: Date.now() });
  const requestW = registration("S-W", 1, process.pid);
  const w = await rawClient(port);
  w.send(requestW);
  const queuedW = await w.until("queued");
  assert.ok(queuedW !== null);
  await waitFor(port, "the command is known", (s) => (s.running.some((j) => j.commandPid !== null) ? true : undefined));
  await restart(fixture, port, 1, [a, w]);

  // A new job waits at once: the slot of the running job counts from the first event
  const b = await rawClient(port);
  b.send(registration("S-B", 1, process.pid));
  assert.ok((await b.until("queued")) !== null);
  const restored = await statusOf(port);
  assert.deepStrictEqual(
    [
      restored.used,
      restored.running.map((j) => [j.id, j.orphan]),
      restored.waiting.map((j) => [j.command, j.orphan]).sort(),
    ],
    [
      1,
      [[granted.id, true]],
      [
        ["S-B", false],
        ["S-W", true],
      ],
    ],
    "the running job holds its slot and the waiting one its place, before their clients are back",
  );

  // The running job's client reports instead of adopting: its slot goes to B, not to W, whose client is away
  const reported = await request(port, {
    type: "report",
    id: Number(granted.id),
    completed: true,
    exitCode: 0,
    pid: process.pid,
    queuedAt: requestA.queuedAt,
  });
  assert.strictEqual(reported?.type, "reported");
  assert.ok((await b.until("granted")) !== null, "the slot goes to the job whose client is here");
  assert.ok(
    (await statusOf(port)).waiting.some((j) => j.command === "S-W" && j.orphan),
    "a waiting job is not granted while its client is away",
  );
  assert.strictEqual((await request(port, { type: "await", id: Number(granted.id) }))?.outcome, "succeeded");
  // W's client comes back and runs after B
  const wBack = await rawClient(port);
  wBack.send({ ...requestW, id: queuedW.id });
  assert.strictEqual((await wBack.until("queued"))?.id, queuedW.id, "it queues again under its id");
  b.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await wBack.until("granted")) !== null);
  wBack.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await wBack.until("done-ok")) !== null);

  // A running job's client adopts it after the restart: the slot it held is the one it takes back
  const requestC = registration("S-C", 1, process.pid);
  const c = await rawClient(port);
  c.send(requestC);
  const grantedC = await c.until("granted");
  assert.ok(grantedC !== null);
  const spawnedAt = Date.now();
  c.send({ type: "started", commandPid: command.pid ?? 0, spawnedAt });
  await waitFor(port, "the command is known", (s) => (s.running.some((j) => j.commandPid !== null) ? true : undefined));
  await restart(fixture, port, 1, [b, wBack, c]);
  const cBack = await rawClient(port);
  cBack.send({ ...requestC, type: "adopt", id: grantedC.id, commandPid: command.pid ?? 0, spawnedAt });
  assert.strictEqual((await cBack.until("adopted"))?.id, grantedC.id);
  const adopted = await statusOf(port);
  assert.deepStrictEqual(
    [adopted.used, adopted.running.map((j) => [j.id, j.orphan])],
    [1, [[grantedC.id, false]]],
    "re-registered in the slot it held, never over the capacity",
  );
  cBack.send({ type: "done", completed: true, exitCode: 0 });
  assert.ok((await cBack.until("done-ok")) !== null);
  cBack.close();
  stopChild(command);
  await request(port, { type: "stop" });
}

/** Starts a scheduler on `port` over a state file with this content. */
async function startOver(fixture: QueueFixture, port: number, saved: Record<string, unknown>): Promise<void> {
  fs.writeFileSync(path.join(fixture.dir, `jobs-${port}.json`), JSON.stringify(saved));
  fixture.ports.push(port);
  await fixture.startScheduler(port, 2);
}

/**
 * A file of an older scheduler: its pending ids become stubs that --after and wait know, and the startup grace stays
 * (nothing is known about running jobs whose client lives). A current file without jobs needs no grace.
 */
async function checkStateFileFormats(fixture: QueueFixture, port: number): Promise<void> {
  const { rawClient } = fixture;
  // An older file: no "format", the ids that had not ended in "pending"
  await startOver(fixture, port, { nextId: 10, jobs: [], ended: [[3, "failed"]], pending: [7] });
  const unknown = await rawClient(port);
  unknown.send({ ...registration("F-after-unknown", 1, process.pid), after: 5 });
  assert.ok((await unknown.until("queued")) !== null, "in the grace an unknown predecessor is accepted");
  const awaiting = await rawClient(port);
  awaiting.send({ type: "await", id: 7 });
  const afterStub = await rawClient(port);
  afterStub.send({ ...registration("F-after-stub", 1, process.pid), after: 7 });
  assert.ok((await afterStub.until("queued")) !== null);
  assert.match(String((await unknown.until("cancelled"))?.reason), /#5 lost/, "and counts as lost after it");
  // The job of the stub comes back under its id, and its outcome reaches wait and its dependent
  const back = await rawClient(port);
  back.send({ ...registration("F-stub", 1, process.pid), id: 7 });
  assert.strictEqual((await back.until("granted"))?.id, 7, "the stub's job takes its id back");
  assert.ok(!afterStub.messages.some((m) => m.type === "cancelled"), "the stub kept its dependent waiting");
  back.send({ type: "done", completed: true, exitCode: 0 });
  assert.strictEqual((await awaiting.until("ended"))?.outcome, "succeeded");
  assert.ok((await afterStub.until("granted")) !== null);
  [unknown, awaiting, afterStub, back].forEach((c) => c.close());
  await request(port, { type: "stop" });

  // A current file without jobs: no grace, an unknown predecessor is refused at once and a job is granted at once
  await startOver(fixture, port + 1, {
    format: 2,
    nextId: 20,
    jobs: [],
    waiting: [],
    provisional: [],
    ended: [],
    pending: [],
  });
  const refused = await rawClient(port + 1);
  refused.send({ ...registration("F-refused", 1, process.pid), after: 5 });
  assert.strictEqual((await refused.until("error"))?.code, "unknown-job", "no grace: an unknown id is a usage error");
  const plain = await rawClient(port + 1);
  plain.send(registration("F-plain", 1, process.pid));
  assert.ok((await plain.until("granted")) !== null, "granted at once");
  [refused, plain].forEach((c) => c.close());
  await request(port + 1, { type: "stop" });
}

export async function checkRestarts(fixture: QueueFixture, port: number): Promise<void> {
  await checkRestartHoldsTheJobs(fixture, port);
  await checkStateFileFormats(fixture, port + 1);
}
