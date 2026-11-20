// Checks of the shared GPU turn on the wire, in the state file and in status, against a scheduler in this process:
// the `share` of a request and of a grant, the probe before a second shared turn, the restore of the shared turns,
// how status counts the holders, and the end of a failed shared turn in the log.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { formatJobId } from "./job-id.ts";
import { describeJob } from "./server-job.ts";
import { JobServer } from "./server.ts";
import { connectRaw, registration, removeDirectory, type RawClient } from "./self-test-support.ts";

type AssertModule = typeof Assert;

/** A probe that prints `text` on stdout; node itself, so that no GPU tool is needed. */
const printing = (text: string): string[] => [process.execPath, "-e", `console.log(${text})`];

interface Running {
  server: JobServer;
  port: number;
  lines: string[];
}

/** Writes the configuration of a scheduler whose `gpu` resource is `gpu`. */
function writeConfig(directory: string, gpu: Record<string, unknown>): string {
  const file = path.join(directory, "job-queue.config.json");
  fs.writeFileSync(file, JSON.stringify({ version: 1, entry: "job-queue.js", plugins: [], resources: { gpu } }));
  return file;
}

/** A scheduler in this process on a free port, started over `directory` (its state file included); nothing is held. */
async function startServer(directory: string, configPath: string, gpuSlots?: number): Promise<Running> {
  const lines: string[] = [];
  const server = new JobServer({
    slots: 4,
    memoryMb: 1e9,
    policy: "fifo",
    port: 0,
    stateDir: directory,
    configPath,
    ...(gpuSlots !== undefined ? { gpuSlots } : {}),
    log: (text) => lines.push(text),
  });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  return { server, port: (server.server.address() as net.AddressInfo).port, lines };
}

/** Runs `body` against a scheduler whose `gpu` resource is `gpu`, and stops it (no state file is written then). */
async function withGpu(
  gpu: Record<string, unknown>,
  body: (running: Running, directory: string) => Promise<void>,
  gpuSlots?: number,
): Promise<void> {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-shared-"));
  const running = await startServer(directory, writeConfig(directory, gpu), gpuSlots);
  try {
    await body(running, directory);
  } finally {
    running.server.stopping = true;
    running.server.server.close();
    removeDirectory(directory);
  }
}

/** A --gpu run that asks for its turn at the GPU: `extra` carries the fields under test (share, resource). */
const turnRequest = (command: string, extra: Record<string, unknown> = {}) => ({
  ...registration(command, 1, process.pid),
  gpu: true,
  gpuTurn: true,
  ...extra,
});

/** Queues a turn and resolves to its client, the id it got and its grant, when one comes within `grantWaitMs`. */
async function queueTurn(port: number, command: string, extra: Record<string, unknown> = {}, grantWaitMs = 3000) {
  const client = await connectRaw(port);
  client.send(turnRequest(command, extra));
  const queued = await client.until("queued");
  const granted = await client.until("granted", grantWaitMs);
  return { client, id: Number((queued ?? granted)?.id), granted };
}

/** Ends a client's job with an exit code and waits for the receipt. */
async function end(client: RawClient, exitCode: number, assert: AssertModule): Promise<void> {
  client.send({ type: "done", completed: true, exitCode });
  assert.ok((await client.until("done-ok")) !== null, "the end is recorded");
  client.close();
}

/** The `gpu` entry of a status reply. */
async function gpuStatus(port: number): Promise<Record<string, unknown> | undefined> {
  const asker = await connectRaw(port);
  asker.send({ type: "status" });
  const reply = await asker.until("status");
  asker.close();
  const resources = reply?.resources as Record<string, unknown>[] | undefined;
  return resources?.find((resource) => resource.name === "gpu");
}

/** `granted` carries share "shared" only for a turn that asked for exactly "shared"; a bad field is as if absent. */
export async function checkGrantEchoesShare(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ server, port }) => {
    const grantOf = async (command: string, extra: Record<string, unknown>, turn = true) => {
      const client = await connectRaw(port);
      client.send(turn ? turnRequest(command, extra) : { ...registration(command, 1, process.pid), ...extra });
      const granted = await client.until("granted");
      assert.ok(granted !== null, `${command} is granted`);
      await end(client, 0, assert);
      return granted;
    };
    assert.strictEqual((await grantOf("shared", { share: "shared" })).share, "shared");
    assert.ok(!("share" in (await grantOf("plain", {}))), "no share asked: none granted");
    assert.ok(!("share" in (await grantOf("exclusive", { share: "exclusive" }))));
    assert.ok(!("share" in (await grantOf("both", { share: "both" }))), "only the exact string shares");
    assert.ok(!("share" in (await grantOf("number", { share: 1 }))));
    assert.ok(!("share" in (await grantOf("not a turn", { share: "shared" }, false))), "only a turn is told");
    // Fields out of shape are read as absent: the job is queued and granted as if they were not there
    const odd = await grantOf("odd", { share: { a: 1 }, resource: 5 });
    assert.ok(!("share" in odd));
    const named = await queueTurn(port, "named", { share: "shared", resource: "GPU 1!" });
    assert.ok(named.granted !== null, "a malformed resource name means the default resource");
    assert.strictEqual(server.find(named.id)?.resource, undefined, "and is not kept");
    await end(named.client, 0, assert);
  });
}

/** With slots 2 the second shared turn waits for a probe result; a failed probe grants nothing. */
export async function checkSecondSharedWaitsForProbe(assert: AssertModule): Promise<void> {
  const probe = (text: string) => ({ argv: printing(text), min: 3072, timeoutMs: 5000, settleMs: 0 });
  await withGpu({ slots: 2, probe: probe("4000") }, async ({ port, lines }) => {
    const a = await queueTurn(port, "A", { share: "shared" });
    assert.ok(a.granted !== null, "the first shared turn needs no probe");
    const b = await queueTurn(port, "B", { share: "shared" }, 50);
    assert.ok((await b.client.until("granted")) !== null, "the second is granted once the probe has passed");
    assert.ok(
      lines.some((line) => line.includes("probe gpu: 4000 >= 3072 ok")),
      lines.join("\n"),
    );
    await end(a.client, 0, assert);
    await end(b.client, 0, assert);
  });
  await withGpu({ slots: 2, probe: probe("100") }, async ({ port, lines, server }) => {
    const a = await queueTurn(port, "A", { share: "shared" });
    const b = await queueTurn(port, "B", { share: "shared" }, 50);
    assert.strictEqual(await b.client.until("granted", 1500), null, "a probe below the minimum grants nothing");
    assert.strictEqual(server.scheduler.stateOf("gpu").probe?.ok, false);
    assert.ok(
      lines.some((line) => /probe gpu: failed \(100 < 3072\)/.test(line)),
      lines.join("\n"),
    );
    await end(a.client, 0, assert);
    assert.ok((await b.client.until("granted")) !== null, "the first one ended: the second needs no probe");
    await end(b.client, 0, assert);
  });
}

/** `--gpu-slots` (the ServeOptions field) replaces the slots of the configuration. */
export async function checkGpuSlotsOverride(assert: AssertModule): Promise<void> {
  await withGpu(
    { slots: 1 },
    async ({ port }) => {
      const a = await queueTurn(port, "A", { share: "shared" });
      const b = await queueTurn(port, "B", { share: "shared" }, 50);
      assert.ok(a.granted !== null && (await b.client.until("granted")) !== null, "two slots: both run");
      assert.strictEqual((await gpuStatus(port))?.slots, 2);
      await end(a.client, 0, assert);
      await end(b.client, 0, assert);
    },
    2,
  );
}

/** Two running shared turns are saved with their share and restored by a new scheduler as shared turns. */
export async function checkSharedTurnsAreRestored(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ server, port }, directory) => {
    const a = await queueTurn(port, "A", { share: "shared", resource: "gpu" });
    const b = await queueTurn(port, "B", { share: "shared" }, 50);
    assert.ok(a.granted !== null && (await b.client.until("granted")) !== null);
    const view = describeJob(server.find(a.id) as NonNullable<ReturnType<JobServer["find"]>>);
    assert.deepStrictEqual([view.share, view.resource], ["shared", "gpu"]);
    server.store.persist();
    const next = await startServer(directory, path.join(directory, "job-queue.config.json"));
    try {
      next.server.store.restore();
      const status = await gpuStatus(next.port);
      assert.deepStrictEqual(status?.shared, [a.id, b.id], "both come back as shared turns");
      assert.deepStrictEqual(status?.exclusive, []);
    } finally {
      next.server.stopping = true;
      next.server.server.close();
    }
    await end(a.client, 0, assert);
    await end(b.client, 0, assert);
  });
}

/** A state file without `share` (written before it existed) restores its running turn as an exclusive one. */
export async function checkOlderStateIsExclusive(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ server }, directory) => {
    const record = { id: 3, gpu: true, gpuTurn: true, pid: process.pid, command: "old", cwd: "", weight: 1 };
    const file = { format: 2, nextId: 4, jobs: [record], waiting: [], provisional: [], ended: [], pending: [3] };
    fs.writeFileSync(server.store.path, JSON.stringify(file));
    const next = await startServer(directory, path.join(directory, "job-queue.config.json"));
    try {
      assert.strictEqual(next.server.store.restore(), "current");
      const status = await gpuStatus(next.port);
      assert.deepStrictEqual([status?.shared, status?.exclusive], [[], [3]], "no share: exclusive");
    } finally {
      next.server.stopping = true;
      next.server.server.close();
    }
  });
}

/** Status: a holder no running turn started is exclusive; one that a shared turn started is in neither list. */
export async function checkStatusCountsHolders(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ port }) => {
    const turn = await queueTurn(port, "T", { share: "shared" });
    assert.ok(turn.granted !== null);
    assert.deepStrictEqual(await gpuStatus(port), { name: "gpu", slots: 2, shared: [turn.id], exclusive: [] });
    const tied = await connectRaw(port);
    tied.send({ ...registration("T", 1, process.pid), gpu: true, turn: turn.id });
    assert.ok((await tied.until("granted")) !== null, "a holder is granted at once");
    const afterTied = await gpuStatus(port);
    assert.deepStrictEqual([afterTied?.shared, afterTied?.exclusive], [[turn.id], []], "tied: in neither list");
    const loose = await connectRaw(port);
    loose.send({ ...registration("by hand", 1, process.pid), gpu: true });
    const granted = await loose.until("granted");
    const afterLoose = await gpuStatus(port);
    assert.deepStrictEqual([afterLoose?.shared, afterLoose?.exclusive], [[turn.id], [Number(granted?.id)]]);
    await end(loose, 0, assert);
    await end(tied, 0, assert);
    await end(turn.client, 0, assert);
  });
}

/** The end line of a failed shared turn names the shared turns that ran with it; no other end line changes. */
export async function checkFailedSharedTurnIsLogged(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ port, lines }) => {
    const a = await queueTurn(port, "A", { share: "shared" });
    const b = await queueTurn(port, "B", { share: "shared" }, 50);
    assert.ok(a.granted !== null && (await b.client.until("granted")) !== null);
    await end(a.client, 1, assert);
    assert.ok(
      lines.some((line) => line.endsWith(`end ${formatJobId(a.id)}: failed (done; shared with ${formatJobId(b.id)})`)),
      lines.join("\n"),
    );
    await end(b.client, 1, assert);
    assert.ok(
      lines.some((line) => line.endsWith(`end ${formatJobId(b.id)}: failed (done)`)),
      "alone: the line as before",
    );
    const c = await queueTurn(port, "C", {});
    assert.ok((await c.client.until("granted")) !== null);
    await end(c.client, 1, assert);
    assert.ok(
      lines.some((line) => line.endsWith(`end ${formatJobId(c.id)}: failed (done)`)),
      "exclusive: the line as before",
    );
    const d = await queueTurn(port, "D", { share: "shared" });
    const e = await queueTurn(port, "E", { share: "shared" }, 50);
    assert.ok(d.granted !== null && (await e.client.until("granted")) !== null);
    await end(d.client, 0, assert);
    assert.ok(
      lines.some((line) => line.endsWith(`end ${formatJobId(d.id)}: succeeded (done)`)),
      "a success: the line as before",
    );
    await end(e.client, 0, assert);
  });
}

export const SharedServerChecks: Record<string, (assert: AssertModule) => Promise<void>> = {
  checkGrantEchoesShare,
  checkSecondSharedWaitsForProbe,
  checkGpuSlotsOverride,
  checkSharedTurnsAreRestored,
  checkOlderStateIsExclusive,
  checkStatusCountsHolders,
  checkFailedSharedTurnIsLogged,
};
