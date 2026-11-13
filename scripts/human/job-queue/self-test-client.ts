// Checks of the client (QueuedRun) in the self-test's own process: a fake RunHost hands out the connections and the
// command's end when a check says so, and a net.Server on an ephemeral port plays the scheduler. No child processes.

import type * as Assert from "node:assert";
import { EventEmitter } from "node:events";
import * as net from "node:net";
import type { ChildProcess } from "node:child_process";
import { QueuedRun, commandEnd, type RunHost, type RunOptions } from "./client.ts";
import { CancelledExitCode, ProtocolVersion } from "./constants.ts";
import { onMessages, type Incoming } from "./protocol.ts";
import type { ShellResult } from "./shell.ts";

type AssertModule = typeof Assert;

/** A promise with its resolve, for the steps a check holds back. */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((done) => (resolve = done));
  return { promise, resolve };
}

/** Polls until `ready` holds (an event of the client or the fake scheduler has arrived). */
async function until(what: string, ready: () => boolean): Promise<void> {
  for (let i = 0; i < 400 && !ready(); i++) {
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  if (!ready()) {
    throw new Error(`within 10 s: ${what}`);
  }
}

/** One connection the fake scheduler accepted: what it received, and whether it has closed. */
interface Accepted {
  socket: net.Socket;
  messages: Incoming[];
  closed: boolean;
}

/** The fake scheduler: accepts connections and records them; the check answers on them. */
class FakeScheduler {
  readonly server = net.createServer((socket) => {
    const accepted: Accepted = { socket, messages: [], closed: false };
    socket.on("error", () => socket.destroy());
    socket.on("close", () => (accepted.closed = true));
    onMessages(socket, (message) => accepted.messages.push(message));
    this.accepted.push(accepted);
  });
  readonly accepted: Accepted[] = [];
  port = 0;

  async listen(): Promise<void> {
    await new Promise<void>((resolve) => this.server.listen(0, "127.0.0.1", resolve));
    this.port = (this.server.address() as net.AddressInfo).port;
  }

  /** A client socket to this scheduler, for the fake host to hand out. */
  connect(): Promise<net.Socket> {
    return new Promise((resolve) => {
      const socket = net.connect(this.port, "127.0.0.1", () => resolve(socket));
      socket.on("error", () => socket.destroy());
    });
  }

  static send(accepted: Accepted, message: Record<string, unknown>): void {
    accepted.socket.write(`${JSON.stringify(message)}\n`);
  }

  close(): void {
    this.accepted.forEach((a) => a.socket.destroy());
    this.server.close();
  }
}

/** A stand-in for the command's process: a pid, and the "close" a real child emits when it ends. */
function fakeChild(): ChildProcess {
  return Object.assign(new EventEmitter(), {
    pid: 424242,
    exitCode: null,
    signalCode: null,
  }) as unknown as ChildProcess;
}

/** A RunHost whose connections and command end come when the check releases them, and that records every effect. */
class FakeHost implements RunHost {
  readonly connects: { promise: Promise<net.Socket>; resolve: (socket: net.Socket) => void }[] = [];
  readonly commands: { child: ChildProcess; end: (result: ShellResult) => void }[] = [];
  readonly reports: unknown[][] = [];
  readonly stops: (ChildProcess | null)[] = [];
  readonly exits: number[] = [];

  connect(): Promise<net.Socket> {
    const next = deferred<net.Socket>();
    this.connects.push(next);
    return next.promise;
  }

  reportEnd(...args: unknown[]): Promise<void> {
    this.reports.push(args.slice(0, 4));
    return Promise.resolve();
  }

  runCommand(_command: string, _env: NodeJS.ProcessEnv, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    const child = fakeChild();
    const ended = deferred<ShellResult>();
    onChild(child);
    // As a real command ends: its exit code, the "close" event, then the promise of CommandShell.run
    const end = (result: ShellResult): void => {
      Object.assign(child, { exitCode: result.code });
      child.emit("close", result.code, null);
      ended.resolve(result);
    };
    this.commands.push({ child, end });
    return ended.promise;
  }

  runUnderGpuLock(): Promise<ShellResult> {
    throw new Error("no GPU turn in these checks");
  }

  stop(child: ChildProcess | null): void {
    this.stops.push(child);
  }

  exit(code: number): void {
    this.exits.push(code);
  }

  onSignal(): void {}
}

const options: RunOptions = {
  port: 0,
  slots: 2,
  memoryMb: 1024,
  policy: "hrrn",
  command: "the command",
  commandWords: null,
  weight: 1,
  gpu: false,
  shell: "",
  timeoutMinutes: 0,
  after: 0,
  keepIfFailed: false,
};

/** Runs `body` with the client's notices (stderr) held back, so that they do not read as the self-test's output. */
async function quietly(body: () => Promise<void>): Promise<void> {
  const write = process.stderr.write;
  process.stderr.write = (() => true) as typeof process.stderr.write;
  try {
    await body();
  } finally {
    process.stderr.write = write;
  }
}

/** Starts a client against the fake scheduler, through its first connection, and grants it twice. */
async function grantedClient(scheduler: FakeScheduler, host: FakeHost, id: number): Promise<Accepted> {
  void new QueuedRun({ ...options }, { weight: 1, pin: null, before: null }, false, false, host).run();
  await until("the client connects", () => host.connects.length === 1);
  host.connects[0]?.resolve(await scheduler.connect());
  await until("the fake scheduler accepts", () => scheduler.accepted.length === 1);
  const first = scheduler.accepted[0] as Accepted;
  await until("the request arrives", () => first.messages.some((m) => m.type === "request"));
  const granted = { type: "granted", id, cpus: 1, capacity: 2, protocol: ProtocolVersion };
  FakeScheduler.send(first, { type: "queued", id, state: "waiting", ahead: 0, position: 1 });
  FakeScheduler.send(first, granted);
  FakeScheduler.send(first, granted); // sent again, as to a client that may not have seen it
  await until("the command starts", () => host.commands.length === 1);
  return first;
}

/**
 * The command ends while the client reconnects after its connection dropped: the client reports the end and exits,
 * and the connection that comes afterwards stays silent (a request now would queue a job that no longer exists).
 * A grant that arrives twice starts the command once.
 */
export async function checkClientReconnect(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  try {
    const host = new FakeHost();
    const first = await grantedClient(scheduler, host, 5);
    first.socket.destroy();
    await until("the client connects again", () => host.connects.length === 2);
    host.commands[0]?.end({ code: 0, signaled: false });
    await until("the client exits", () => host.exits.length > 0);
    assert.deepStrictEqual(host.reports, [[5, true, 0, false]], "the end is reported once");
    assert.deepStrictEqual(host.exits, [0]);
    host.connects[1]?.resolve(await scheduler.connect());
    await until("the late connection arrives", () => scheduler.accepted.length === 2);
    const late = scheduler.accepted[1] as Accepted;
    await until("the client ends the late connection", () => late.closed);
    assert.deepStrictEqual(late.messages, [], "nothing is registered after the command ended");
    assert.strictEqual(host.commands.length, 1, "a grant that arrives twice starts the command once");
  } finally {
    scheduler.close();
  }
}

/** The command's result decides how a job ends after a cancel: only a success by itself is not the cancel's. */
export function checkCommandEnd(assert: AssertModule): void {
  const end = (code: number, signaled: boolean, cancelRequested: boolean) =>
    commandEnd({ code, signaled }, cancelRequested);
  assert.deepStrictEqual(end(0, false, false), { exitCode: 0, completed: true, cancelled: false });
  assert.deepStrictEqual(end(3, false, false), { exitCode: 3, completed: true, cancelled: false });
  assert.deepStrictEqual(end(1, true, false), { exitCode: 1, completed: false, cancelled: false }, "a signal");
  assert.deepStrictEqual(
    end(0, false, true),
    { exitCode: 0, completed: true, cancelled: false },
    "a success that the cancel crossed",
  );
  assert.deepStrictEqual(
    end(1, false, true),
    { exitCode: CancelledExitCode, completed: false, cancelled: true },
    "taskkill /F ends a command with 1",
  );
  assert.deepStrictEqual(end(0, true, true), { exitCode: CancelledExitCode, completed: false, cancelled: true });
}

/**
 * A cancel reaches the client after its command ended with 0 by itself, before the client saw the end: the command's
 * result decides, so the job succeeded and run exits with 0.
 */
export async function checkCancelCrossesEnd(assert: AssertModule): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  try {
    const host = new FakeHost();
    const first = await grantedClient(scheduler, host, 6);
    FakeScheduler.send(first, { type: "cancelled", id: 6, reason: "cancel requested" });
    await until("the client stops the command", () => host.stops.length === 1);
    host.commands[0]?.end({ code: 0, signaled: false });
    await until("the client sends done", () => first.messages.some((m) => m.type === "done"));
    FakeScheduler.send(first, { type: "done-ok", id: 6 });
    await until("the client exits", () => host.exits.length > 0);
    const done = first.messages.find((m) => m.type === "done");
    assert.deepStrictEqual(
      [done?.completed, done?.exitCode, done?.cancelled],
      [true, 0, false],
      "the command's success decides",
    );
    assert.strictEqual(host.exits[0], 0, "run exits with 0");
  } finally {
    scheduler.close();
  }
}

export const ClientChecks: Record<string, (assert: AssertModule) => Promise<void>> = {
  checkClientReconnect: (assert) => quietly(() => checkClientReconnect(assert)),
  checkCancelCrossesEnd: (assert) => quietly(() => checkCancelCrossesEnd(assert)),
  checkCommandEnd: async (assert) => checkCommandEnd(assert),
};
