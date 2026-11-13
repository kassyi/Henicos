// Part 10 of the job queue self-test: move, set-weight, --after, restarts, reports and the "done" receipt.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { GpuLockHeldVariable, EntryScript, JobVariable, ProtocolVersion, StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import { onMessages, type Incoming, type StatusJob } from "./protocol.ts";
import { ProcessTable } from "./system.ts";
import { delay, type Client, registration, type QueueFixture } from "./self-test-support.ts";

export async function checkPlacementAndDependencies(fixture: QueueFixture, port: number): Promise<void> {
  const { env, client, sleeper, exiting, gated, open, dir, ports, startScheduler } = fixture;
  const { statusOf, usedWithin, waitFor, jobWith, runningWith } = fixture;
  ports.push(port);
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const cli = (...args: string[]) =>
    spawnSync(process.execPath, [EntryScript, ...args, "--port", String(port)], {
      encoding: "utf8",
      windowsHide: true,
      env,
    });
  const waitingIds = async (): Promise<number[]> => (await statusOf(port)).waiting.map((j) => j.id);

  // A holder takes both slots; x, y, z wait in arrival order
  const holder = client(port, sleeper(60000), ["--weight", "2"]);
  const holderJob = await jobWith(port, sleeper(60000));
  const waitingCommands = [sleeper(201), sleeper(202), sleeper(203)];
  const waiters: Client[] = [];
  const queued: StatusJob[] = [];
  for (const command of waitingCommands) {
    // One at a time, so that they arrive in this order
    waiters.push(client(port, command));
    queued.push(await jobWith(port, command));
  }
  const [x, y, z] = queued;
  assert.ok(x !== undefined && y !== undefined && z !== undefined);
  assert.deepStrictEqual(await waitingIds(), [x.id, y.id, z.id], "arrival order");
  assert.strictEqual(cli("move", String(z.id), "--first").status, 0, "move --first");
  assert.deepStrictEqual(await waitingIds(), [z.id, x.id, y.id], "the moved job is first");
  assert.strictEqual(cli("move", String(y.id), "--before", String(z.id)).status, 0, "move --before");
  assert.deepStrictEqual(await waitingIds(), [y.id, z.id, x.id], "right before the target");
  assert.strictEqual(cli("move", String(z.id), "--before", String(y.id)).status, 1, "a cycle is refused");
  assert.strictEqual(cli("move", String(holderJob.id), "--first").status, 1, "only a waiting job moves");
  assert.strictEqual(cli("set-weight", String(x.id), "2").status, 0, "set-weight");
  assert.strictEqual((await jobWith(port, waitingCommands[0] as string)).weight, 2);
  // The places and weights survive a restart: the clients bring them when they queue again. The stop ends the waiting
  // clients' connections, which is no outcome of their jobs (a waiting job that ends records "cancelled")
  await request(port, { type: "stop" });
  await delay(300);
  const saved = JSON.parse(fs.readFileSync(path.join(dir, `jobs-${port}.json`), "utf8")) as {
    ended: [number, string][];
  };
  assert.deepStrictEqual(
    saved.ended.filter(([id]) => [x.id, y.id, z.id].includes(id)),
    [],
    "a stop records no outcome for the waiting jobs",
  );
  await startScheduler(port, 2);
  // The holder re-registers too, so that the cancel below reaches it
  await waitFor(port, "the jobs register again", (s) =>
    s.running.length === 1 && s.waiting.length === 3 ? true : undefined,
  );
  assert.deepStrictEqual(await waitingIds(), [y.id, z.id, x.id], "the order survives a restart");
  assert.strictEqual((await jobWith(port, waitingCommands[0] as string)).weight, 2, "the weight survives a restart");
  await request(port, { type: "cancel", all: true });
  assert.deepStrictEqual(await Promise.all([holder.done, ...waiters.map((w) => w.done)]), [4, 4, 4, 4]);
  await usedWithin(port, 0, 5000);

  // --after: waits for its predecessor although a slot is free, and runs once it succeeded
  const firstGate = path.join(dir, "gate-10a");
  const first = client(port, gated(0, firstGate));
  const firstJob = await jobWith(port, gated(0, firstGate));
  const second = client(port, exiting(0, 1), ["--after", String(firstJob.id)]);
  const secondJob = await jobWith(port, exiting(0, 1));
  assert.strictEqual(secondJob.state, "waiting", "waits although a slot is free");
  assert.strictEqual(secondJob.blockedBy, firstJob.id);
  open(firstGate);
  assert.deepStrictEqual(await Promise.all([first.done, second.done]), [0, 0], "runs after the predecessor succeeded");
  // A failed predecessor cancels its dependents, along the chain; --keep-if-failed runs anyway
  const failingGate = path.join(dir, "gate-10b");
  const failing = client(port, gated(5, failingGate));
  const failingJob = await jobWith(port, gated(5, failingGate));
  const dependent = client(port, exiting(0, 2), ["--after", String(failingJob.id)]);
  const dependentJob = await jobWith(port, exiting(0, 2));
  const chained = client(port, exiting(0, 3), ["--after", String(dependentJob.id)]);
  const kept = client(port, exiting(0, 4), ["--after", String(failingJob.id), "--keep-if-failed"]);
  await jobWith(port, exiting(0, 3));
  await jobWith(port, exiting(0, 4));
  open(failingGate);
  assert.deepStrictEqual(await Promise.all([failing.done, dependent.done, chained.done, kept.done]), [5, 4, 4, 0]);
  // --after is refused inside a queued job and inside the GPU lock, --gpu or not: waiting there could deadlock
  const refused = (variable: string, extra: string[]) =>
    spawnSync(
      process.execPath,
      [EntryScript, "run", "--port", String(port), ...extra, "--after", String(firstJob.id), "--", exiting(0)],
      {
        windowsHide: true,
        env: { ...env, [variable]: "7" },
      },
    ).status;
  assert.strictEqual(refused(JobVariable, []), 2, "--after inside a queued job");
  assert.strictEqual(refused(JobVariable, ["--gpu"]), 2, "--gpu --after inside a queued job");
  assert.strictEqual(refused(GpuLockHeldVariable, []), 2, "--after inside the GPU lock");
  // A predecessor that already ended is known by its outcome; an unknown one is a usage error
  assert.strictEqual(
    await client(port, exiting(0, 5), ["--after", String(firstJob.id)]).done,
    0,
    "after a job that succeeded",
  );
  assert.strictEqual(
    await client(port, exiting(0, 6), ["--after", String(failingJob.id)]).done,
    4,
    "after a job that failed",
  );
  assert.strictEqual(await client(port, exiting(0, 7), ["--after", "999999"]).done, 2, "after an unknown job");
  // The outcomes survive a restart
  await request(port, { type: "stop" });
  await delay(300);
  await startScheduler(port, 2);
  // During the startup grace a predecessor is not checked yet, but one queued later than the job never is a
  // predecessor: refused at once, not left waiting for ever
  assert.strictEqual(
    await client(port, exiting(0, 9), ["--after", "999999"]).done,
    2,
    "a later id is refused in the grace",
  );
  await delay(StartupGraceMs + 100);
  assert.strictEqual(
    await client(port, exiting(0, 8), ["--after", String(failingJob.id)]).done,
    4,
    "outcome kept across a restart",
  );
  const ended = await request(port, { type: "await", id: firstJob.id });
  assert.deepStrictEqual(
    [ended?.type, (ended as Incoming | null)?.outcome],
    ["ended", "succeeded"],
    "await an ended job",
  );

  // A dependent that comes back after a restart before its predecessor, past the startup grace, still waits for it;
  // the predecessor's client, whose command ended while no scheduler listened, reports the outcome afterwards
  const connection = async (
    message: Record<string, unknown>,
  ): Promise<{ socket: net.Socket; messages: Incoming[] }> => {
    const socket = await new Promise<net.Socket>((resolve) => {
      const s = net.connect(port, "127.0.0.1", () => resolve(s));
      s.on("error", () => s.destroy());
    });
    const messages: Incoming[] = [];
    onMessages(socket, (m) => messages.push(m));
    socket.write(`${JSON.stringify({ version: ProtocolVersion, ...message })}\n`);
    return { socket, messages };
  };
  const idOf = async (c: { messages: Incoming[] }): Promise<number> => {
    for (let i = 0; i < 50 && !c.messages.some((m) => m.type === "queued"); i++) {
      await delay(100);
    }
    return Number(c.messages.find((m) => m.type === "queued")?.id);
  };
  const holderRaw = await connection(registration("R-holder", 2, 1));
  const predecessor = await connection(registration("R-predecessor", 1, 1));
  const predecessorId = await idOf(predecessor);
  const dependentRequest = { ...registration("R-dependent", 1, 1), after: predecessorId };
  const dependentRaw = await connection(dependentRequest);
  const dependentId = await idOf(dependentRaw);
  await request(port, { type: "stop" });
  [holderRaw, predecessor, dependentRaw].forEach((c) => c.socket.destroy());
  await delay(300);
  await startScheduler(port, 2);
  await delay(StartupGraceMs + 1000);
  const back = await connection({ ...dependentRequest, id: dependentId });
  const backView = await jobWith(port, "R-dependent");
  assert.deepStrictEqual(
    [backView.id, backView.state, backView.blockedBy],
    [dependentId, "waiting", predecessorId],
    "waits past the grace",
  );
  const report = await request(port, { type: "report", id: predecessorId, completed: true, exitCode: 0 });
  assert.strictEqual(report?.type, "reported");
  await waitFor(port, "the dependent starts once the outcome is reported", (s) =>
    s.running.some((j) => j.id === dependentId) ? true : undefined,
  );
  back.socket.destroy();

  // A job held for a lost client (only its connection dropped) ends with its client's report, and its dependent starts
  const command = spawn(process.execPath, ["-e", "setTimeout(function(){},20000)"], {
    windowsHide: true,
    detached: process.platform !== "win32",
  });
  const lostRaw = await connection(registration("R-lost", 1, 1));
  const lostId = await idOf(lostRaw);
  lostRaw.socket.write(
    `${JSON.stringify({
      version: ProtocolVersion,
      type: "started",
      commandPid: command.pid ?? 0,
      spawnedAt: Date.now(),
    })}\n`,
  );
  const afterLost = await connection({ ...registration("R-after-lost", 1, 1), after: lostId });
  await jobWith(port, "R-after-lost");
  lostRaw.socket.destroy();
  await waitFor(port, "the client is seen lost", (s) =>
    s.running.some((j) => j.id === lostId && j.orphan) ? true : undefined,
  );
  await request(port, { type: "report", id: lostId, completed: true, exitCode: 0 });
  await waitFor(port, "the reported job ends and its dependent starts", (s) =>
    s.running.some((j) => j.command === "R-after-lost") && !s.running.some((j) => j.id === lostId) ? true : undefined,
  );
  afterLost.socket.destroy();
  if (process.platform === "win32") {
    ProcessTable.killTree(command.pid);
  } else {
    command.kill("SIGTERM");
  }
  await request(port, { type: "stop" });

  // A command that ends while its client reconnects to the next scheduler: the client reports the end instead of
  // queueing its job again, and the job after it runs (end-to-end, with real clients)
  await startScheduler(port, 2);
  await delay(StartupGraceMs + 100);
  const reconnectGate = path.join(dir, "gate-10c");
  const ending = client(port, gated(0, reconnectGate));
  const endingJob = await runningWith(port, gated(0, reconnectGate));
  const following = client(port, exiting(0, 11), ["--after", String(endingJob.id)]);
  await jobWith(port, exiting(0, 11));
  await request(port, { type: "stop" });
  open(reconnectGate); // the clients start the next scheduler themselves; the command ends meanwhile
  assert.deepStrictEqual(
    await Promise.all([ending.done, following.done]),
    [0, 0],
    "the job after one that ended during a restart runs",
  );
  // and its outcome stays "succeeded": no job is queued again under its id once it ended
  const endingOutcome = await request(port, { type: "await", id: endingJob.id });
  assert.strictEqual(endingOutcome?.outcome, "succeeded", "the outcome of a job that ended during a restart");
  await usedWithin(port, 0, 15000);

  // The same with no other client to start the next scheduler: the client that reports starts it, and a job queued
  // --after it later runs
  const aloneGate = path.join(dir, "gate-10d");
  const alone = client(port, gated(0, aloneGate));
  const aloneJob = await runningWith(port, gated(0, aloneGate));
  await request(port, { type: "stop" });
  open(aloneGate);
  assert.strictEqual(await alone.done, 0);
  await waitFor(port, "the reported job is known as ended", () => true);
  assert.strictEqual(
    await client(port, exiting(0, 12), ["--after", String(aloneJob.id)]).done,
    0,
    "the outcome reached the next scheduler",
  );

  // Only the connection of a waiting job drops while its client lives: the job is expected back, not cancelled, and the
  // job that waits for it keeps waiting
  await usedWithin(port, 0, 15000);
  const blocker = await connection(registration("R-blocker", 2, 1));
  await jobWith(port, "R-blocker");
  const dropped = await connection(registration("R-dropped", 1, process.pid));
  const droppedId = await idOf(dropped);
  const waitingOnDropped = await connection({ ...registration("R-waits-on-dropped", 1, 1), after: droppedId });
  await jobWith(port, "R-waits-on-dropped");
  dropped.socket.destroy();
  await delay(500);
  const afterDrop = await statusOf(port);
  const stillWaiting = afterDrop.waiting.find((j) => j.command === "R-waits-on-dropped");
  assert.deepStrictEqual(
    [stillWaiting?.blockedBy, waitingOnDropped.messages.some((m) => m.type === "cancelled")],
    [droppedId, false],
    "not cancelled",
  );
  const returned = await connection({ ...registration("R-dropped", 1, process.pid), id: droppedId });
  assert.strictEqual(await idOf(returned), droppedId, "it queues again under its id");
  // A waiting job whose client left on its own (it sent "done": a timeout, Ctrl+C) has ended, although its process
  // may still run for a moment: its dependent is cancelled at once
  const leaving = await connection(registration("R-leaving", 1, process.pid));
  const leavingId = await idOf(leaving);
  const waitingOnLeaving = await connection({ ...registration("R-waits-on-leaving", 1, 1), after: leavingId });
  await jobWith(port, "R-waits-on-leaving");
  leaving.socket.write(
    `${JSON.stringify({ version: ProtocolVersion, type: "done", completed: false, exitCode: 3 })}\n`,
  );
  leaving.socket.end();
  for (let i = 0; i < 50 && !waitingOnLeaving.messages.some((m) => m.type === "cancelled"); i++) {
    await delay(100);
  }
  assert.ok(
    waitingOnLeaving.messages.some((m) => m.type === "cancelled"),
    "the job after one that left is cancelled",
  );
  [blocker, waitingOnDropped, returned, waitingOnLeaving].forEach((c) => c.socket.destroy());
  await request(port, { type: "stop" });

  // A "done" gets a receipt once the outcome is saved; a stopping scheduler saves nothing and gives none, so that the
  // client reports the outcome to the next one
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const receipt = async (stopFirst: boolean): Promise<boolean> => {
    const c = await connection(registration(`R-receipt-${stopFirst}`, 1, 1));
    for (let i = 0; i < 50 && !c.messages.some((m) => m.type === "granted"); i++) {
      await delay(100);
    }
    if (stopFirst) {
      await request(port, { type: "stop" }).catch(() => null);
    }
    c.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: "done", completed: true, exitCode: 0 })}\n`);
    await delay(500);
    c.socket.destroy();
    return c.messages.some((m) => m.type === "done-ok");
  };
  assert.strictEqual(await receipt(false), true, "a done gets its receipt");
  assert.strictEqual(await receipt(true), false, "a stopping scheduler gives no receipt");
  await delay(300);

  // A cancel decides the outcome where the command did not succeed, also when the scheduler restarted in between (the
  // client says so in "done"); a command that succeeded before the cancel reached it stays succeeded
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const outcomeOf = async (id: number): Promise<unknown> => (await request(port, { type: "await", id }))?.outcome;
  const crossing = await connection(registration("R-crossing", 1, process.pid));
  const crossingId = await idOf(crossing);
  await request(port, { type: "cancel", id: crossingId });
  crossing.socket.write(
    `${JSON.stringify({ version: ProtocolVersion, type: "done", completed: true, exitCode: 0 })}\n`,
  );
  await delay(300);
  assert.strictEqual(await outcomeOf(crossingId), "succeeded", "a success that crosses the cancel wins");
  crossing.socket.destroy();
  // The same through a report, while the old connection is still open here
  const reporting = await connection(registration("R-crossing-report", 1, process.pid));
  const reportingId = await idOf(reporting);
  await request(port, { type: "cancel", id: reportingId });
  await request(port, { type: "report", id: reportingId, completed: true, exitCode: 0 });
  reporting.socket.destroy();
  await delay(300);
  assert.strictEqual(await outcomeOf(reportingId), "succeeded", "a reported success that crosses the cancel wins");
  const restarted = await connection(registration("R-cancel-restart", 1, process.pid));
  const restartedId = await idOf(restarted);
  await request(port, { type: "stop" });
  restarted.socket.destroy();
  await delay(300);
  await startScheduler(port, 2);
  const again = await connection({
    ...registration("R-cancel-restart", 1, process.pid),
    type: "adopt",
    id: restartedId,
    spawnedAt: Date.now(),
  });
  await delay(300);
  again.socket.write(
    `${JSON.stringify({ version: ProtocolVersion, type: "done", completed: false, exitCode: 4, cancelled: true })}\n`,
  );
  await delay(StartupGraceMs + 300);
  assert.strictEqual(
    await outcomeOf(restartedId),
    "cancelled",
    "a cancel carried across a restart is recorded as cancelled",
  );
  again.socket.destroy();
  await request(port, { type: "stop" });
  await delay(300);

  // In the startup grace of a restart, a failed predecessor reports before its dependents queue again: when the grace
  // ends, every dependent is cancelled and none is granted (a cancel there used to grant the next ones)
  await startScheduler(port, 2);
  await delay(StartupGraceMs);
  const failedEarly = await connection(registration("R-failed-early", 1, 1));
  const failedEarlyId = await idOf(failedEarly);
  await request(port, { type: "stop" });
  failedEarly.socket.destroy();
  await delay(300);
  await startScheduler(port, 2);
  await request(port, { type: "report", id: failedEarlyId, completed: true, exitCode: 1 });
  const dependents = await Promise.all(
    [1, 2, 3].map((n) => connection({ ...registration(`R-after-failed-${n}`, 1, 1), after: failedEarlyId })),
  );
  await delay(StartupGraceMs + 500);
  for (const dependent of dependents) {
    const types = dependent.messages.map((m) => m.type);
    assert.ok(
      types.includes("cancelled") && !types.includes("granted"),
      `cancelled, never granted: ${types.join(" ")}`,
    );
    dependent.socket.destroy();
  }
  await request(port, { type: "stop" });
}
