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
import type { Incoming } from "./protocol.ts";

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

/**
 * "downgrade": a granted shared turn waits again, under its id, as an exclusive turn: it leaves the running turns at
 * once, is not granted beside the shared turn that still runs, and is granted alone once that one has ended (no
 * "share" in the grant). Any other job gets an error and stays as it is.
 */
export async function checkDowngradeSharedTurn(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ port }) => {
    const a = await queueTurn(port, "A", { share: "shared" });
    const b = await queueTurn(port, "B", { share: "shared" });
    assert.ok(a.granted !== null && b.granted !== null, "two shared turns run");
    assert.strictEqual(a.granted?.downgrade, true, "the grant of a shared turn says that downgrade is known");
    a.client.send({ type: "downgrade" });
    const queued = await a.client.until("queued", 2000);
    assert.strictEqual(Number(queued?.id), a.id, "the same id waits again");
    assert.strictEqual(
      a.client.messages.filter((message) => message.type === "granted").length,
      1,
      "not granted again while the other shared turn runs",
    );
    await end(b.client, 0, assert);
    const again = await waitForGrant(a.client, 2);
    assert.ok(again !== null, "granted alone once the other turn has ended");
    assert.ok(!("share" in again), "an exclusive grant: no share");
    assert.strictEqual(Number(again.id), a.id, "under the same id");
    await end(a.client, 0, assert);

    // Not a granted shared turn: an error, and the job stays what it was
    const waiting = await connectRaw(port);
    waiting.send(turnRequest("W", { share: "shared" }));
    const first = await waiting.until("granted");
    assert.ok(first !== null);
    const exclusive = await connectRaw(port);
    exclusive.send(turnRequest("X", {}));
    await exclusive.until("queued");
    exclusive.send({ type: "downgrade" });
    const refused = await exclusive.until("error", 2000);
    assert.strictEqual(refused?.code, "not-downgradable", "a waiting job cannot be downgraded");
    waiting.send({ type: "downgrade" });
    assert.ok((await waiting.until("queued", 2000)) !== null);
    waiting.close();
    exclusive.close();
  });
  // A granted exclusive turn, and a shared turn that still waits behind it, are neither of them downgradable
  await withGpu({ slots: 2 }, async ({ port }) => {
    const alone = await queueTurn(port, "E", {});
    assert.ok(alone.granted !== null, "the exclusive turn runs");
    assert.ok(!("downgrade" in alone.granted), "an exclusive grant does not offer it");
    alone.client.send({ type: "downgrade" });
    assert.strictEqual((await alone.client.until("error", 2000))?.code, "not-downgradable", "an exclusive turn");
    const behind = await queueTurn(port, "S", { share: "shared" }, 300);
    assert.strictEqual(behind.granted, null, "the shared turn waits for the exclusive one");
    behind.client.send({ type: "downgrade" });
    assert.strictEqual((await behind.client.until("error", 2000))?.code, "not-downgradable", "a waiting shared turn");
    await end(alone.client, 0, assert);
    assert.ok((await behind.client.until("granted", 2000)) !== null, "it runs once the exclusive turn has ended");
    await end(behind.client, 0, assert);
  });
}

/**
 * A shared turn whose connection drops (a downgrade that did not arrive) registers again under its id and asks for no
 * share: the scheduler counts it as exclusive from then on, so no second shared turn is granted beside it.
 */
export async function checkRebindLowersShare(assert: AssertModule): Promise<void> {
  await withGpu({ slots: 2 }, async ({ port }) => {
    const request = { ...turnRequest("A", { share: "shared" }) };
    const first = await connectRaw(port);
    first.send(request);
    const queued = await first.until("queued");
    assert.ok((await first.until("granted")) !== null, "the shared turn runs");
    first.close();
    await first.closed;
    const again = await connectRaw(port);
    again.send({ ...request, id: Number(queued?.id), share: "exclusive" });
    const regranted = await again.until("granted", 3000);
    assert.ok(regranted !== null, "the same record is granted again");
    assert.ok(!("share" in regranted), "and no longer as a shared turn");
    const other = await connectRaw(port);
    other.send(turnRequest("B", { share: "shared" }));
    assert.strictEqual(await other.until("granted", 700), null, "no shared turn runs beside the exclusive one");
    await end(again, 0, assert);
    assert.ok((await other.until("granted", 2000)) !== null, "it runs once the exclusive turn has ended");
    await end(other, 0, assert);
  });
}

/** Resolves to the `count`th message of `type` the client has received (waiting for it), or null after 3 s. */
async function waitForMessage(client: RawClient, type: string, count: number): Promise<Incoming | null> {
  const until = Date.now() + 3000;
  while (Date.now() < until) {
    const matched = client.messages.filter((message) => message.type === type);
    if (matched.length >= count) {
      return matched[count - 1] ?? null;
    }
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  return null;
}

/** Resolves to the `count`th grant the client has received (waiting for it), or null after 3 s. */
async function waitForGrant(client: RawClient, count: number): Promise<Incoming | null> {
  return waitForMessage(client, "granted", count);
}

/** Status reply snapshot from the scheduler. */
async function statusSnapshot(
  port: number,
): Promise<{ running: Record<string, unknown>[]; waiting: Record<string, unknown>[] }> {
  const asker = await connectRaw(port);
  asker.send({ type: "status" });
  const reply = await asker.until("status");
  asker.close();
  return {
    running: (reply?.running as Record<string, unknown>[] | undefined) ?? [],
    waiting: (reply?.waiting as Record<string, unknown>[] | undefined) ?? [],
  };
}

/**
 * `grant` log lines, `status --json`, `JobStore` persistence/restore, `downgrade`, and `rebind` carry `shareReason`
 * while preserving `sim2.mts` (`/shared/.test(head)`) and older client/state compatibility.
 */
export async function checkGrantLogAndStatusShareReason(assert: AssertModule): Promise<void> {
  const headOfGrant = (line: string): string => line.split(" pid ")[0] ?? "";
  // Case 1 & 2: grant log formatting, non-turn gating, sanitization, and sim2.mts /shared/.test(head) invariant
  await withGpu({ slots: 2 }, async ({ server, port, lines }) => {
    const cases: {
      command: string;
      extra: Record<string, unknown>;
      turn?: boolean;
      expectedFlag: string | null;
      expectedSimShared: boolean;
      expectedReason: string | undefined;
    }[] = [
      {
        command: "shared-reasoned",
        extra: { share: "shared", shareReason: "categories" },
        expectedFlag: " gpu gpu-turn share=shared(categories)",
        expectedSimShared: true,
        expectedReason: "categories",
      },
      {
        command: "shared-legacy",
        extra: { share: "shared" },
        expectedFlag: " gpu gpu-turn share=shared",
        expectedSimShared: true,
        expectedReason: undefined,
      },
      {
        command: "excl-reasoned",
        extra: { share: "exclusive", shareReason: "list:3" },
        expectedFlag: " gpu gpu-turn share=exclusive(list:3)",
        expectedSimShared: false,
        expectedReason: "list:3",
      },
      {
        command: "excl-omitted-share",
        extra: { shareReason: "env-prefix" },
        expectedFlag: " gpu gpu-turn share=exclusive(env-prefix)",
        expectedSimShared: false,
        expectedReason: "env-prefix",
      },
      {
        command: "excl-legacy",
        extra: {},
        expectedFlag: " gpu gpu-turn share=exclusive",
        expectedSimShared: false,
        expectedReason: undefined,
      },
      {
        command: "bad-excl-shared",
        extra: { share: "exclusive", shareReason: "not-shared" },
        expectedFlag: " gpu gpu-turn share=exclusive",
        expectedSimShared: false,
        expectedReason: undefined,
      },
      {
        command: "bad-chars",
        extra: { share: "exclusive", shareReason: "bad reason\n" },
        expectedFlag: " gpu gpu-turn share=exclusive",
        expectedSimShared: false,
        expectedReason: undefined,
      },
      {
        command: "bad-parens",
        extra: { share: "shared", shareReason: "a(b)" },
        expectedFlag: " gpu gpu-turn share=shared",
        expectedSimShared: true,
        expectedReason: undefined,
      },
      {
        command: "bad-long",
        extra: { share: "exclusive", shareReason: "x".repeat(65) },
        expectedFlag: " gpu gpu-turn share=exclusive",
        expectedSimShared: false,
        expectedReason: undefined,
      },
      {
        command: "non-turn",
        extra: { share: "shared", shareReason: "categories" },
        turn: false,
        expectedFlag: null,
        expectedSimShared: false,
        expectedReason: undefined,
      },
    ];
    for (const c of cases) {
      const client = await connectRaw(port);
      client.send(
        c.turn === false ? { ...registration(c.command, 1, process.pid), ...c.extra } : turnRequest(c.command, c.extra),
      );
      const granted = await client.until("granted");
      assert.ok(granted !== null, `${c.command} granted`);
      const id = Number(granted.id);
      const record = server.find(id) as NonNullable<ReturnType<JobServer["find"]>>;
      const view = describeJob(record);
      assert.strictEqual(view.shareReason, c.expectedReason, `${c.command} describeJob.shareReason`);
      if (c.expectedReason === undefined) {
        assert.strictEqual("shareReason" in view, false, `${c.command} omits undefined shareReason`);
      }
      if (c.turn === false) {
        assert.strictEqual(view.share, undefined, "non-turn drops share");
      }
      const grantLine = lines.find((line) => line.startsWith(`grant ${formatJobId(id)} `)) ?? "";
      assert.ok(grantLine !== "", `grant line logged for ${c.command}`);
      const head = headOfGrant(grantLine);
      if (c.expectedFlag !== null) {
        assert.ok(head.endsWith(c.expectedFlag), `expected '${c.expectedFlag}' in '${grantLine}'`);
        assert.strictEqual(/gpu-turn/.test(head), true);
        assert.strictEqual(/shared/.test(head), c.expectedSimShared, `sim2.mts /shared/.test(head) for ${c.command}`);
      } else {
        assert.strictEqual(head.includes("share="), false, `non-turn omits share= in '${grantLine}'`);
      }
      await end(client, 0, assert);
    }
  });

  // Case 3 & 4: status --json projection (running + waiting) and JobStore persistence/restore
  await withGpu({ slots: 2 }, async ({ server, port }, directory) => {
    const reqA = turnRequest("A", { share: "shared", shareReason: "categories" });
    const clientA = await connectRaw(port);
    clientA.send(reqA);
    const qA = await clientA.until("queued");
    const aId = Number(qA?.id);
    assert.ok((await clientA.until("granted")) !== null);

    const reqB = turnRequest("B", { share: "exclusive", shareReason: "env-prefix" });
    const clientB = await connectRaw(port);
    clientB.send(reqB);
    const qB = await clientB.until("queued");
    const bId = Number(qB?.id);
    assert.strictEqual(await clientB.until("granted", 50), null, "exclusive B waits behind shared A");

    const c = await queueTurn(port, "C", {}, 50);
    assert.strictEqual(c.granted, null, "legacy C waits behind B");

    const beforeStatus = await statusSnapshot(port);
    const runningA = beforeStatus.running.find((j) => Number(j.id) === aId);
    const waitingB = beforeStatus.waiting.find((j) => Number(j.id) === bId);
    const waitingC = beforeStatus.waiting.find((j) => Number(j.id) === c.id);
    assert.strictEqual(runningA?.shareReason, "categories", "status running carries shareReason");
    assert.strictEqual(waitingB?.shareReason, "env-prefix", "status waiting carries shareReason");
    assert.strictEqual(waitingC !== undefined && "shareReason" in waitingC, false, "status omits absent shareReason");

    server.store.persist();
    const next = await startServer(directory, path.join(directory, "job-queue.config.json"));
    try {
      assert.strictEqual(next.server.store.restore(), "current");
      const restoredStatus = await statusSnapshot(next.port);
      assert.strictEqual(
        restoredStatus.running.find((j) => Number(j.id) === aId)?.shareReason,
        "categories",
        "restored running job keeps shareReason",
      );
      assert.strictEqual(
        restoredStatus.waiting.find((j) => Number(j.id) === bId)?.shareReason,
        "env-prefix",
        "restored waiting job keeps shareReason",
      );
      // Reconnect A and end it, reconnect B and grant it -> grant log emits share=exclusive(env-prefix)
      const a2 = await connectRaw(next.port);
      a2.send({ ...reqA, type: "adopt", id: aId });
      assert.ok((await a2.until("adopted")) !== null);
      const b2 = await connectRaw(next.port);
      b2.send({ ...reqB, id: bId });
      assert.ok((await b2.until("queued")) !== null);
      await end(a2, 0, assert);
      assert.ok((await b2.until("granted", 2000)) !== null);
      const grantB = next.lines.find((line) => line.startsWith(`grant ${formatJobId(bId)} `)) ?? "";
      assert.ok(grantB.includes(" share=exclusive(env-prefix) pid "), `restored grant line: ${grantB}`);
      b2.close();
    } finally {
      next.server.stopping = true;
      next.server.server.close();
    }
    clientA.close();
    clientB.close();
    c.client.close();
  });

  // Case 5a: downgrade with explicit shareReason ("stale") and legacy payload-less downgrade ("downgrade")
  await withGpu({ slots: 2 }, async ({ server, port, lines }) => {
    const a = await queueTurn(port, "A", { share: "shared", shareReason: "list:0" });
    const b = await queueTurn(port, "B", { share: "shared", shareReason: "categories" });
    assert.ok(a.granted !== null && b.granted !== null);
    a.client.send({ type: "downgrade", shareReason: "stale" });
    assert.ok((await waitForMessage(a.client, "queued", 2)) !== null);
    assert.strictEqual(
      describeJob(server.find(a.id) as NonNullable<ReturnType<JobServer["find"]>>).shareReason,
      "stale",
      "downgrade updates shareReason to incoming stale",
    );
    await end(b.client, 0, assert);
    assert.ok((await waitForGrant(a.client, 2)) !== null);
    const secondGrantA = lines.filter((line) => line.startsWith(`grant ${formatJobId(a.id)} `))[1] ?? "";
    assert.ok(secondGrantA.includes(" share=exclusive(stale) pid "), `second grant after downgrade: ${secondGrantA}`);
    await end(a.client, 0, assert);

    // Legacy payload-less downgrade falls back to "downgrade"
    const c = await queueTurn(port, "C", { share: "shared", shareReason: "list:0" });
    assert.ok(c.granted !== null);
    c.client.send({ type: "downgrade" });
    assert.ok((await waitForMessage(c.client, "queued", 2)) !== null);
    assert.strictEqual(
      describeJob(server.find(c.id) as NonNullable<ReturnType<JobServer["find"]>>).shareReason,
      "downgrade",
      "payload-less downgrade sets shareReason to downgrade",
    );
    c.client.close();
  });

  // Case 5a-extra: downgrade with share: "shared" and shareReason: "not-shared" must enforce exclusive sanitization
  await withGpu({ slots: 2 }, async ({ server, port, lines }) => {
    const d = await queueTurn(port, "D", { share: "shared", shareReason: "list:0" });
    const blocker = await queueTurn(port, "Blocker", { share: "shared", shareReason: "categories" });
    assert.ok(d.granted !== null && blocker.granted !== null);
    d.client.send({ type: "downgrade", share: "shared", shareReason: "not-shared" });
    assert.ok((await waitForMessage(d.client, "queued", 2)) !== null);
    const jobD = server.find(d.id) as NonNullable<ReturnType<JobServer["find"]>>;
    assert.strictEqual(jobD.shareReason, "downgrade", "downgrade rejects 'shared' reason despite message.share");
    assert.strictEqual(describeJob(jobD).shareReason, "downgrade", "describeJob.shareReason is downgrade");
    await end(blocker.client, 0, assert);
    assert.ok((await waitForGrant(d.client, 2)) !== null);
    const secondGrantD = lines.filter((line) => line.startsWith(`grant ${formatJobId(d.id)} `))[1] ?? "";
    assert.ok(
      secondGrantD.includes(" share=exclusive(downgrade) pid "),
      `re-grant after downgrade with bad reason: ${secondGrantD}`,
    );
    const head = headOfGrant(secondGrantD);
    assert.strictEqual(/shared/.test(head), false, `sim2 head must not match /shared/: ${head}`);
    await end(d.client, 0, assert);
  });

  // Case 5b: rebind lowers shareReason to incoming new reason ("stale") or "rebind" when omitted/unchanged
  await withGpu({ slots: 2 }, async ({ server, port, lines }) => {
    const req1 = turnRequest("A", { share: "shared", shareReason: "list:0" });
    const first = await connectRaw(port);
    first.send(req1);
    const q1 = await first.until("queued");
    assert.ok((await first.until("granted")) !== null);
    first.close();
    await first.closed;
    const again1 = await connectRaw(port);
    again1.send({ ...req1, id: Number(q1?.id), share: "exclusive", shareReason: "stale" });
    const regranted1 = await again1.until("granted", 3000);
    assert.ok(regranted1 !== null && !("share" in regranted1));
    assert.strictEqual(
      describeJob(server.find(Number(q1?.id)) as NonNullable<ReturnType<JobServer["find"]>>).shareReason,
      "stale",
      "rebind preserves new exclusive shareReason",
    );
    await end(again1, 0, assert);

    const req2 = turnRequest("B", { share: "shared", shareReason: "list:0" });
    const second = await connectRaw(port);
    second.send(req2);
    const q2 = await second.until("queued");
    assert.ok((await second.until("granted")) !== null);
    second.close();
    await second.closed;
    const again2 = await connectRaw(port);
    // Spread req2 with share: "exclusive" keeping old shareReason ("list:0"): must replace with "rebind"
    again2.send({ ...req2, id: Number(q2?.id), share: "exclusive" });
    const regranted2 = await again2.until("granted", 3000);
    assert.ok(regranted2 !== null && !("share" in regranted2));
    assert.strictEqual(
      describeJob(server.find(Number(q2?.id)) as NonNullable<ReturnType<JobServer["find"]>>).shareReason,
      "rebind",
      "rebind replaces stale shared reason with rebind when unchanged",
    );
    await end(again2, 0, assert);

    // Waiting rebind: disconnected while waiting behind an exclusive blocker -> reconnects as exclusive -> grant logs new reason
    const blocker = await queueTurn(port, "Blocker", { share: "exclusive", shareReason: "env-prefix" });
    assert.ok(blocker.granted !== null);
    const req3 = turnRequest("W", { share: "shared", shareReason: "list:0" });
    const waiting = await connectRaw(port);
    waiting.send(req3);
    const q3 = await waiting.until("queued");
    assert.strictEqual(await waiting.until("granted", 50), null);
    waiting.close();
    await waiting.closed;
    const again3 = await connectRaw(port);
    again3.send({ ...req3, id: Number(q3?.id), share: "exclusive", shareReason: "stale" });
    assert.ok((await again3.until("queued")) !== null);
    await end(blocker.client, 0, assert);
    assert.ok((await again3.until("granted", 3000)) !== null);
    const waitingRebindGrant = lines.find((line) => line.startsWith(`grant ${formatJobId(Number(q3?.id))} `)) ?? "";
    assert.ok(waitingRebindGrant.includes(" share=exclusive(stale) pid "), waitingRebindGrant);
    await end(again3, 0, assert);
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
  checkDowngradeSharedTurn,
  checkRebindLowersShare,
  checkGrantLogAndStatusShareReason,
};
