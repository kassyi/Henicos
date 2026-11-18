// Checks of the scheduler side of the optional memoryMb and keys fields: the job record, the history, the state
// file, and requests from older and newer clients.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { ProtocolVersion } from "./constants.ts";
import { RunHistory } from "./history.ts";
import { QueuedRun } from "./client-queued-run.ts";
import type { RunHost } from "./client-run-host.ts";
import type { RunOptions } from "./client.ts";
import type { ShellResult } from "./shell.ts";
import { JobServer } from "./server.ts";
import { connectRaw, registration, removeDirectory, type RawClient } from "./self-test-support.ts";

type AssertModule = typeof Assert;

/**
 * Check 6: In-process server accepts valid memoryMb (1234) from request and falls back to machine calculation when
 * absent or out of range; also verifies old client compatibility (raw message without new fields).
 */
export async function checkServerMemoryMb(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-server-mem-"));
  const server = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
  server.scheduler.holdUntil = 0;
  await new Promise<void>((resolve) => server.server.listen(0, "127.0.0.1", resolve));
  const port = (server.server.address() as net.AddressInfo).port;
  const clients: RawClient[] = [];
  try {
    const c1 = await connectRaw(port);
    clients.push(c1);
    c1.send({ ...registration("node -e 1", 1, process.pid), memoryMb: 1234 });
    const g1 = await c1.until("granted");
    assert.ok(g1 !== null);
    assert.strictEqual(server.find(Number(g1.id))?.memoryMb, 1234, "server keeps valid memoryMb from request");
    c1.send({ type: "done", completed: true, exitCode: 0 });
    await c1.until("done-ok");
    c1.close();

    // Without memoryMb (old client message) -> current computed value
    const c2 = await connectRaw(port);
    clients.push(c2);
    c2.send(registration("node -e 2", 1, process.pid));
    const g2 = await c2.until("granted");
    assert.ok(g2 !== null);
    assert.strictEqual(
      server.find(Number(g2.id))?.memoryMb,
      server.machine.testMemoryMb("node -e 2"),
      "without memoryMb falls back to machine.testMemoryMb",
    );
    c2.send({ type: "done", completed: true, exitCode: 0 });
    await c2.until("done-ok");
    c2.close();

    // Out-of-range / wrong-type memoryMb -> fallback
    const c3 = await connectRaw(port);
    clients.push(c3);
    c3.send({ ...registration("node -e 3", 1, process.pid), memoryMb: 1e12 });
    const g3 = await c3.until("granted");
    assert.ok(g3 !== null);
    assert.strictEqual(server.find(Number(g3.id))?.memoryMb, server.machine.testMemoryMb("node -e 3"));
    c3.close();
  } finally {
    server.stopping = true;
    for (const c of clients) {
      c.close();
    }
    for (const job of server.records.values()) {
      job.connection?.socket.destroy();
    }
    await new Promise<void>((resolve) => server.server.close(() => resolve()));
    removeDirectory(dir);
  }
}

/**
 * Checks 7 & 8: History lookup uses passed keys, and re-recording an entry without keys preserves existing keys.
 */
export function checkHistoryKeys(assert: AssertModule): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-history-keys-"));
  try {
    const file = path.join(dir, "history.json");
    const history = new RunHistory(file, () => {});
    const ctx = { cwd: "", worktree: "" };
    const customKeys = { normalized: "custom:norm-key", shape: "custom:shape-key", kind: "custom" };

    // Check 7: record command A with keys K, then look up different command B with keys K -> from: "normalized"
    history.record("command alpha", 4200, 1, 1000, ctx, customKeys);
    const estimate = history.estimateWithFallback("completely different command beta", ctx, customKeys);
    assert.ok(estimate !== null);
    assert.strictEqual(estimate.from, "normalized");
    assert.strictEqual(estimate.typicalMs, 4200);

    // Check 8: re-recording command A without keys preserves K
    history.record("command alpha", 4800, 2, 2000, ctx);
    const reloaded = new RunHistory(file, () => {});
    assert.deepStrictEqual(reloaded.commands[RunHistory.key("command alpha")]?.keys, customKeys);
    const updatedEstimate = reloaded.estimateWithFallback("completely different command beta", ctx, customKeys);
    assert.strictEqual(updatedEstimate?.from, "normalized");
    assert.strictEqual(updatedEstimate?.typicalMs, 4500);
  } finally {
    removeDirectory(dir);
  }
}

/**
 * Check 9: State file persists waiting job's keys across scheduler restart and keeps the same estimate.
 */
export async function checkStateFileKeys(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-state-keys-"));
  const customKeys = { normalized: "custom:state-norm", shape: "custom:state-shape", kind: "custom" };
  const s1 = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
  s1.scheduler.holdUntil = 0;
  s1.history.record("recorded command alpha", 5500, 1, Date.now(), { cwd: "", worktree: "" }, customKeys);
  await new Promise<void>((resolve) => s1.server.listen(0, "127.0.0.1", resolve));
  const port = (s1.server.address() as net.AddressInfo).port;
  const clients: RawClient[] = [];
  try {
    const holder = await connectRaw(port);
    clients.push(holder);
    holder.send(registration("holder", 1, process.pid));
    assert.ok((await holder.until("granted")) !== null);

    const waiter = await connectRaw(port);
    clients.push(waiter);
    waiter.send({ ...registration("different waiting beta", 1, process.pid), keys: customKeys });
    const queued = await waiter.until("queued");
    assert.ok(queued !== null);
    const waitingId = Number(queued.id);
    assert.strictEqual(s1.find(waitingId)?.estimate?.from, "normalized");
    assert.strictEqual(s1.find(waitingId)?.estimate?.typicalMs, 5500);

    s1.store.persist();
    const s2 = new JobServer({ slots: 1, memoryMb: 1e9, policy: "fifo", port: 0, stateDir: dir, log: () => {} });
    assert.strictEqual(s2.store.restore(), "current");
    const restored = s2.find(waitingId);
    assert.deepStrictEqual(restored?.keys, customKeys, "restored job keeps keys");
    assert.strictEqual(restored?.estimate?.from, "normalized", "restored job keeps fallback estimate from keys");
    assert.strictEqual(restored?.estimate?.typicalMs, 5500);

    holder.close();
    waiter.close();
  } finally {
    s1.stopping = true;
    for (const c of clients) {
      c.close();
    }
    for (const job of s1.records.values()) {
      job.connection?.socket.destroy();
    }
    await new Promise<void>((resolve) => s1.server.close(() => resolve()));
    removeDirectory(dir);
  }
}

/**
 * Version-skew checks (in-process): old scheduler (granted without new fields, or error code: "version") with client,
 * and JobServer loading --config without loading plugin code.
 */
export async function checkVersionSkewAndServeConfig(assert: AssertModule): Promise<void> {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-skew-"));
  const cfgPath = path.join(dir, "job-queue.config.json");
  fs.writeFileSync(
    cfgPath,
    JSON.stringify({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [{ name: "./does-not-exist-plugin" }],
      resources: { gpu: { slots: 1 } },
    }),
  );
  try {
    // Scheduler reads data portion only; missing/non-existent plugin code does not cause plugin warnings on JobServer
    const serverLogs: string[] = [];
    const server = new JobServer({
      slots: 1,
      memoryMb: 1e9,
      policy: "fifo",
      port: 0,
      stateDir: dir,
      configPath: cfgPath,
      log: (text) => serverLogs.push(text),
    });
    assert.strictEqual(server.config.resources?.gpu?.slots, 1);
    assert.strictEqual(
      serverLogs.some((l) => l.includes("does-not-exist-plugin")),
      false,
      "scheduler never loads plugin code",
    );

    // Fake RunHost against an old-protocol scheduler returning granted without new fields, and error code: "version"
    let mode: "granted" | "version-error" = "granted";
    const serverSockets = new Set<net.Socket>();
    const fakeServer = net.createServer((socket) => {
      serverSockets.add(socket);
      socket.on("close", () => serverSockets.delete(socket));
      socket.on("error", () => socket.destroy());
      socket.on("data", (buf) => {
        const lines = buf.toString("utf8").split("\n").filter(Boolean);
        for (const line of lines) {
          const msg = JSON.parse(line) as { type?: string };
          if (msg.type === "request") {
            if (mode === "version-error") {
              socket.write(`${JSON.stringify({ type: "error", code: "version", message: "unsupported version" })}\n`);
            } else {
              socket.write(
                `${JSON.stringify({ type: "granted", id: 1, cpus: 1, capacity: 2, protocol: ProtocolVersion })}\n`,
              );
            }
          } else if (msg.type === "done") {
            socket.write(`${JSON.stringify({ type: "done-ok", id: 1 })}\n`);
            socket.end();
          }
        }
      });
    });
    await new Promise<void>((resolve) => fakeServer.listen(0, "127.0.0.1", resolve));
    const port = (fakeServer.address() as net.AddressInfo).port;
    try {
      const runFakeClient = async (gpuTurn = false): Promise<{ exits: number[]; gpuLockCalls: number }> => {
        const exits: number[] = [];
        const clientSockets: net.Socket[] = [];
        let gpuLockCalls = 0;
        let resolveExit: () => void = () => {};
        const exited = new Promise<void>((resolve) => (resolveExit = resolve));
        const host: RunHost = {
          connect: () =>
            new Promise((resolve) => {
              const s = net.connect(port, "127.0.0.1", () => {
                clientSockets.push(s);
                resolve(s);
              });
              s.on("error", () => s.destroy());
            }),
          reportEnd: () => Promise.resolve(),
          runCommand: async () => ({ code: 0, signaled: false }) satisfies ShellResult,
          runUnderGpuLock: async () => {
            gpuLockCalls++;
            return { code: 0, signaled: false };
          },
          stop: () => {},
          exit: (code) => {
            exits.push(code);
            resolveExit();
          },
          onSignal: () => {},
        };
        const runOptions: RunOptions = {
          port,
          slots: 2,
          memoryMb: 1024,
          policy: "hrrn",
          command: "node -e 1",
          commandWords: null,
          weight: 1,
          gpu: gpuTurn,
          shell: "",
          timeoutMinutes: 0,
          after: 0,
          keepIfFailed: false,
        };
        const origWrite = process.stderr.write;
        process.stderr.write = (() => true) as typeof process.stderr.write;
        let timeoutId: ReturnType<typeof setTimeout> | undefined;
        try {
          await new QueuedRun(runOptions, { weight: 1, pin: null, before: null }, false, gpuTurn, host).run();
          await Promise.race([
            exited,
            new Promise<void>((_resolve, reject) => {
              timeoutId = setTimeout(() => reject(new Error("timed out waiting for FakeHost.exit")), 5000);
            }),
          ]);
        } finally {
          if (timeoutId !== undefined) {
            clearTimeout(timeoutId);
          }
          for (const s of clientSockets) {
            s.destroy();
          }
          process.stderr.write = origWrite;
        }
        return { exits, gpuLockCalls };
      };

      assert.deepStrictEqual(
        (await runFakeClient(false)).exits,
        [0],
        "client completes cleanly against scheduler granted without new fields",
      );

      mode = "version-error";
      assert.deepStrictEqual(
        (await runFakeClient(false)).exits,
        [1],
        'non-GPU client exits 1 cleanly on error code: "version"',
      );
      const gpuFallback = await runFakeClient(true);
      assert.deepStrictEqual(
        gpuFallback,
        { exits: [0], gpuLockCalls: 1 },
        'GPU turn falls back to GPU lock on error code: "version"',
      );
    } finally {
      for (const s of serverSockets) {
        s.destroy();
      }
      await new Promise<void>((resolve) => fakeServer.close(() => resolve()));
    }
  } finally {
    removeDirectory(dir);
  }
}
