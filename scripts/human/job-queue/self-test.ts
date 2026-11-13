// The self-test of the job queue (job-queue.js --self-test): the scheduler unit checks, the shell helpers, and
// end-to-end runs against schedulers on private ports with a private state directory, so that it never touches the
// queue the machine uses.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as net from "node:net";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import {
  CpusVariable,
  GpuLockHeldVariable,
  EntryScript,
  InGpuLockVariable,
  JobVariable,
  OrphanPollMs,
  ProtocolVersion,
  ShellVariable,
  StartupGraceMs,
  StateDirectoryVariable,
} from "./constants.ts";
import { request } from "./client.ts";
import { RunHistory } from "./history.ts";
import { onMessages, send, type StatusJob, type StatusReply } from "./protocol.ts";
import { SchedulerChecks } from "./scheduler-checks.ts";
import { defaultWeight, gitBashPath, limitMsbuildNodes, quoteArgument } from "./shell.ts";
import {
  delay,
  stopChild,
  removeDirectory,
  type Client,
  startTestScheduler,
  statusOf,
  usedWithin,
  waitFor,
  jobWith,
  runningWith,
  jobsWith,
  registration,
} from "./self-test-support.ts";
import { checkPlacementAndDependencies } from "./self-test-dependencies.ts";
import { checkGpuTurnsEndToEnd, checkRegrant, checkWait } from "./self-test-parts.ts";

// How long a test waits for the slot of a lost client to come back: the orphan poll reads the processes through
// PowerShell on Windows, which can take seconds on a busy machine (a CI runner)
const OrphanReleaseMs = 30000;

/** Checks of the pure helpers: the node limit, quoting, the default weight. */
function checkShellHelpers(): void {
  assert.strictEqual(
    limitMsbuildNodes("dotnet test X.csproj && dotnet build", 6),
    "dotnet test -m:6 X.csproj && dotnet build -m:6",
  );
  assert.strictEqual(limitMsbuildNodes("dotnet build-server shutdown", 6), "dotnet build-server shutdown");
  assert.strictEqual(limitMsbuildNodes("dotnet test -m:2", 6), "dotnet test -m:2");
  assert.strictEqual(limitMsbuildNodes("dotnet build -m:2 && dotnet test", 6), "dotnet build -m:2 && dotnet test -m:6");
  assert.strictEqual(limitMsbuildNodes("dotnet build --maxcpucount 4", 6), "dotnet build --maxcpucount 4");
  assert.strictEqual(limitMsbuildNodes("dotnet.exe build", 6), "dotnet.exe build -m:6");
  assert.strictEqual(
    limitMsbuildNodes('"C:\\Program Files\\dotnet\\dotnet.exe" test x', 6),
    '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x',
  );
  assert.strictEqual(limitMsbuildNodes("dotnet run --project x", 6), "dotnet run --project x");
  assert.strictEqual(limitMsbuildNodes('dotnet test --filter "A|B" -m:2', 6), 'dotnet test --filter "A|B" -m:2');
  assert.strictEqual(limitMsbuildNodes("dotnet test bin/x.Tests.dll", 6), "dotnet test bin/x.Tests.dll");
  assert.strictEqual(quoteArgument("pwsh", "@x.rsp"), "'@x.rsp'", "PowerShell would splat @x");
  assert.strictEqual(quoteArgument("pwsh", "-Dfoo.bar=1"), "'-Dfoo.bar=1'", "PowerShell splits -a.b at the dot");
  assert.strictEqual(quoteArgument("bash", "C:\\dir\\f.txt"), "'C:\\dir\\f.txt'", "bash reads \\ as an escape");
  assert.strictEqual(quoteArgument("cmd", "C:\\dir\\f.txt"), "C:\\dir\\f.txt");
  assert.strictEqual(quoteArgument("pwsh", "x,"), "'x,'", "PowerShell reads a trailing comma as an array");
  assert.strictEqual(quoteArgument("bash", "-p:Foo.Bar=1"), "-p:Foo.Bar=1");
  assert.strictEqual(quoteArgument("bash", "it's"), "'it'\\''s'");
  assert.strictEqual(defaultWeight("just test --filter X"), 2);
  assert.strictEqual(defaultWeight("dotnet build Henicos.slnx"), 1);
}

export async function selfTest(): Promise<void> {
  for (const [name, check] of Object.entries(SchedulerChecks)) {
    try {
      check(assert);
    } catch (error) {
      process.stderr.write(`${name} failed\n`);
      throw error;
    }
  }
  checkShellHelpers();

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-"));
  const env: NodeJS.ProcessEnv = { ...process.env };
  delete env[JobVariable];
  delete env[InGpuLockVariable];
  delete env[CpusVariable];
  delete env[GpuLockHeldVariable]; // a self-test run under gpu-lock.js must still queue its GPU-less clients
  env[StateDirectoryVariable] = dir; // a scheduler that a client starts logs here, not into the main checkout
  // The test commands below are written for cmd / sh; the shells themselves are checked in part 6.
  env[ShellVariable] = process.platform === "win32" ? "cmd" : "sh";
  // --gpu jobs take a private GPU lock, never the machine's (gpu-lock.js reads GSHARP_GPU_LOCK_PATH), without the pool
  // readings
  env.GSHARP_GPU_LOCK_PATH = path.join(dir, "gpu-lock");
  env.GSHARP_GPU_POOL_WATCH = "0";
  const children: ChildProcess[] = [];
  // --slots: a scheduler that a client restarts gets the same capacity as the test one.
  const client = (port: number, command: string, extra: string[] = []): Client => {
    const args = [EntryScript, "run", "--port", String(port), "--slots", "2", "--weight", "1", ...extra, "--", command];
    const child = spawn(process.execPath, args, { stdio: "ignore", windowsHide: true, env }) as Client;
    children.push(child);
    child.done = new Promise((resolve) => child.on("close", resolve));
    return child;
  };
  const sleeper = (ms: number): string => `"${process.execPath}" -e "setTimeout(function(){},${ms})"`;
  // A command that ends with `code` once `file` exists, so that a check decides when a job ends
  const gated = (code: number, file: string): string =>
    `"${process.execPath}" -e "var f=process.argv[1];` +
    `setInterval(function(){if(require('fs').existsSync(f))process.exit(${code})},50)" "${file}"`;
  const open = (file: string): void => fs.writeFileSync(file, "");
  // A command that writes `file` once it runs, then sleeps: the client reports its shell's process at once, and a
  // client killed before the shell started the command ends the shell with it, so that no command is left to hold a
  // slot
  const started = (ms: number, file: string): string =>
    `"${process.execPath}" -e "require('fs').writeFileSync(process.argv[1],'');` +
    `setTimeout(function(){},${ms})" "${file}"`;
  const whenExists = async (file: string): Promise<void> => {
    for (let i = 0; i < 150 && !fs.existsSync(file); i++) {
      await delay(100);
    }
    assert.ok(fs.existsSync(file), `the command started: ${file}`);
  };
  const exiting = (code: number, ms = 0): string =>
    `"${process.execPath}" -e "setTimeout(function(){process.exit(${code})},${ms})"`;
  const servers: ChildProcess[] = [];
  const ports: number[] = [];
  try {
    // 1. At most 2 jobs at once, in arrival order; a client killed with its command frees its slot.
    const port = 20000 + Math.floor(Math.random() * 20000);
    ports.push(port);
    servers.push(await startTestScheduler(port, 2, env));
    await delay(StartupGraceMs);
    const marker = path.join(dir, "log.txt");
    const script = path.join(dir, "job.js");
    const gate = path.join(dir, "gate-1");
    // Logs its start, waits for the gate, logs its end 300 ms later
    fs.writeFileSync(
      script,
      "const fs=require('fs');const [f,i,g]=process.argv.slice(2);" +
        "fs.appendFileSync(f,`start ${i} ${Date.now()}\\n`);" +
        "const t=setInterval(()=>{if(fs.existsSync(g)){clearInterval(t);" +
        "setTimeout(()=>{fs.appendFileSync(f,`end ${i} ${Date.now()}\\n`)},300)}},50);",
    );
    const job = (i: number): string => `"${process.execPath}" "${script}" "${marker}" ${i} "${gate}"`;
    // Its own command: a kill on Windows can let the client see the command end first and record the run (`taskkill
    // /F` ends a command with exit code 1, like a failure)
    const holder = client(port, sleeper(59999));
    await runningWith(port, sleeper(59999));
    const runs: Promise<number | null>[] = [];
    for (const i of [0, 1, 2, 3]) {
      runs.push(client(port, job(i)).done);
      await jobWith(port, job(i)); // one at a time, so that they arrive in this order
    }
    const before = await statusOf(port);
    assert.strictEqual(before.used, 2, "the holder and one job fill the 2 slots");
    assert.strictEqual(before.waiting.length, 3, "the other jobs wait");
    before.waiting.forEach((waiting, index) => {
      assert.strictEqual(waiting.ahead, index, "the jobs ahead of a waiting job");
      assert.ok(
        waiting.startAt !== null && waiting.endAt !== null && waiting.endAt > waiting.startAt,
        "expected start and end",
      );
      const previous = before.waiting[index - 1];
      assert.ok(
        previous === undefined || (waiting.startAt ?? 0) >= (previous.startAt ?? 0),
        "a later job is expected to start later",
      );
    });
    // run prints its id at once; status <id> --json tells the same job
    // Both slots: it waits while the holder runs, however fast the jobs above have ended
    const asking = spawn(
      process.execPath,
      [EntryScript, "run", "--port", String(port), "--slots", "2", "--weight", "2", "--", sleeper(100)],
      {
        stdio: ["ignore", "ignore", "pipe"],
        windowsHide: true,
        env,
      },
    );
    children.push(asking);
    const queuedId = await new Promise<number>((resolve) => {
      let text = "";
      asking.stderr.on("data", (data: Buffer) => {
        text += data.toString();
        const match = /queued #(\d+): (\d+) ahead, expected start/.exec(text);
        if (match) {
          resolve(Number(match[1]));
        }
      });
      asking.on("close", () => resolve(0));
    });
    assert.ok(queuedId > 0, 'run prints "queued #<id>: <n> ahead, expected start ..." while it waits');
    const one = spawnSync(
      process.execPath,
      [EntryScript, "status", String(queuedId), "--json", "--port", String(port)],
      {
        encoding: "utf8",
        windowsHide: true,
        env,
      },
    );
    const view = JSON.parse(one.stdout) as StatusJob;
    assert.strictEqual(view.id, queuedId);
    assert.strictEqual(view.state, "waiting");
    assert.ok(Number.isFinite(view.endAt), "status <id> --json gives the expected end");
    open(gate);
    stopChild(holder);
    assert.deepStrictEqual(await Promise.all(runs), [0, 0, 0, 0]);
    const events = fs
      .readFileSync(marker, "utf8")
      .trim()
      .split("\n")
      .map((line) => line.split(" "))
      .map(([kind, i, time]) => ({ kind, i: Number(i), time: Number(time) }))
      .sort((x, y) => x.time - y.time || (x.kind === "end" ? -1 : 1));
    let concurrent = 0;
    let peak = 0;
    for (const event of events) {
      concurrent += event.kind === "start" ? 1 : -1;
      peak = Math.max(peak, concurrent);
    }
    assert.ok(peak <= 2, `at most 2 jobs ran at once, saw ${peak}`);
    assert.deepStrictEqual(
      events.filter((e) => e.kind === "start").map((e) => e.i),
      [0, 1, 2, 3],
      "arrival order",
    );
    assert.strictEqual(await usedWithin(port, 0, OrphanReleaseMs), 0, "every slot came back");

    // 2. A client killed alone (its command keeps running) keeps the slot until the command exits.
    const loneStarted = path.join(dir, "started-2");
    const lone = client(port, started(8000, loneStarted));
    await whenExists(loneStarted);
    lone.kill("SIGKILL"); // the client only; on Windows its shell ends with it, the command the shell started runs on
    await waitFor(port, "the client is seen lost", (s) => (s.running[0]?.orphan ? true : undefined));
    await delay(OrphanPollMs + 500); // a poll of the lost client's command has run
    const orphaned = await statusOf(port);
    assert.strictEqual(orphaned.used, 1, "the slot stays while the command of a lost client runs");
    assert.strictEqual(orphaned.running[0]?.orphan, true);
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanReleaseMs), 0, "the slot comes back when it exits");

    // 3. cancel-all stops running jobs and empties the queue; the clients exit with 4.
    const r1 = client(port, sleeper(60000));
    const r2 = client(port, sleeper(60000));
    const w1 = client(port, sleeper(60000));
    await jobsWith(port, sleeper(60000), 3);
    const result = await request(port, { type: "cancel", all: true });
    assert.strictEqual((result?.cancelled as number[]).length, 3);
    assert.deepStrictEqual(await Promise.all([r1.done, r2.done, w1.done]), [4, 4, 4]);
    // A cancelled running job keeps its slot until its client has stopped the command and closed
    assert.strictEqual(await usedWithin(port, 0, 5000), 0);
    const empty = await statusOf(port);
    assert.deepStrictEqual([empty.used, empty.running.length, empty.waiting.length], [0, 0, 0]);
    // Run times are recorded for the commands that ended by themselves, not for killed, lost or cancelled ones, nor
    // for one stopped early (done with completed: false, as a client sends after a signal)
    const rawRun = (command: string, completed: boolean): Promise<void> =>
      new Promise((resolve) => {
        const s = net.connect(port, "127.0.0.1", () =>
          send(s, { version: ProtocolVersion, ...registration(command, 1, process.pid) }),
        );
        onMessages(s, (m) => {
          if (m.type === "granted") {
            send(s, { version: ProtocolVersion, type: "done", completed, exitCode: 0 });
            s.end();
          }
        });
        s.on("close", () => resolve());
        s.on("error", () => resolve());
      });
    await rawRun("stopped early", false);
    await rawRun("ran to its end", true);
    // The scheduler records when it sees the close, after our side closed; the early run's close came before
    let recorded = new RunHistory(path.join(dir, "history.json"), () => {});
    for (let i = 0; i < 30 && recorded.estimate("ran to its end") === null; i++) {
      await delay(100);
      recorded = new RunHistory(path.join(dir, "history.json"), () => {});
    }
    for (const i of [0, 1, 2, 3]) {
      assert.ok(recorded.estimate(job(i)) !== null, `job ${i} is recorded`);
    }
    assert.ok(recorded.estimate("ran to its end") !== null, "a run that ended by itself is recorded");
    assert.strictEqual(recorded.estimate("stopped early"), null, "a run stopped early is not recorded");
    assert.strictEqual(recorded.estimate(sleeper(60000)), null, "killed and cancelled runs are not recorded");
    assert.strictEqual(recorded.estimate(started(8000, loneStarted)), null, "the run of a lost client is not recorded");

    // 4. After a scheduler restart, running jobs re-register before waiting ones are granted: never over capacity.
    const k1 = client(port, sleeper(6000));
    const k2 = client(port, sleeper(6000));
    await waitFor(port, "two commands run", (s) =>
      s.running.filter((j) => j.commandPid !== null).length === 2 ? true : undefined,
    );
    const k3 = client(port, sleeper(500));
    await jobWith(port, sleeper(500));
    const restartedAt = Date.now();
    servers[0]?.kill("SIGKILL"); // the clients start the next scheduler themselves
    let maxUsed = 0;
    let adoptedSince = Infinity;
    for (let i = 0; i < 20; i++) {
      const snapshot = await request(port, { type: "status" }).catch(() => null); // the scheduler may be restarting
      if (snapshot !== null && snapshot.type === "status") {
        const status = snapshot as unknown as StatusReply;
        maxUsed = Math.max(maxUsed, status.used);
        status.running.forEach((j) => (adoptedSince = Math.min(adoptedSince, j.grantedAt ?? Infinity)));
      }
      await delay(150);
    }
    assert.ok(maxUsed <= 2, `never over capacity after a restart, saw ${maxUsed}`);
    assert.ok(adoptedSince < restartedAt, "a re-registered job keeps the start of its command, not the restart time");
    assert.deepStrictEqual(await Promise.all([k1.done, k2.done, k3.done]), [0, 0, 0]);

    // 6. The command runs in the chosen shell and its exit code comes back: PowerShell passes the code of the last
    //    native program on; bash and sh understand their own syntax.
    const exitOf = async (shell: string, command: string): Promise<number | null> =>
      client(port, command, ["--shell", shell]).done;
    const node = `"${process.execPath.replace(/\\/g, "/")}"`;
    const hasGitBash = spawnSync(gitBashPath(), ["-c", "exit 0"], { windowsHide: true }).status === 0;
    if (process.platform === "win32") {
      assert.strictEqual(await exitOf("pwsh", `$x = 1; & ${node} -e "process.exit(5)"`), 5, "pwsh: native exit code");
      assert.strictEqual(await exitOf("cmd", "exit 6"), 6, "cmd");
    }
    if (hasGitBash) {
      assert.strictEqual(await exitOf("bash", "FOO=7; true 2>/dev/null; exit $FOO"), 7, "bash syntax");
    }

    // 7. Several words after `--` arrive as argv already split by the caller's shell; each reaches the command as it
    // was.
    const argvExit = (shell: string): Promise<number | null> =>
      new Promise((resolve) => {
        // The command compares its own argv with the words, so each word makes the round trip through the shell
        const check = "process.exit(JSON.stringify(process.argv.slice(1))===process.env.BQ_EXPECT?0:9)";
        const words = [
          "A&B c|d",
          "it's",
          "-Dfoo.bar=1",
          "C:\\dir\\f.txt",
          "@x.rsp",
          "x,",
          "-p:A=1",
          "Category!=Stress",
          "$HOME",
        ];
        const args = [
          EntryScript,
          "run",
          "--port",
          String(port),
          "--shell",
          shell,
          "--",
          process.execPath,
          "-e",
          check,
          ...words,
        ];
        const child = spawn(process.execPath, args, {
          stdio: "ignore",
          windowsHide: true,
          env: { ...env, BQ_EXPECT: JSON.stringify(words) },
        });
        children.push(child);
        child.on("close", resolve);
      });
    const shells = process.platform === "win32" ? ["pwsh", "cmd"] : ["sh"];
    if (hasGitBash) {
      shells.push("bash");
    }
    for (const shell of shells) {
      assert.strictEqual(await argvExit(shell), 0, `argv after -- survives ${shell}`);
    }

    // 8. A client that lost only its connection re-registers: its kept slot is replaced under the same id, and a
    //    waiting job does not get it in between (capacity 2: A weighs 2, W waits with 2).
    await usedWithin(port, 0, 5000);
    const raw = (): Promise<net.Socket> =>
      new Promise((resolve) => {
        const socket = net.connect(port, "127.0.0.1", () => resolve(socket));
        socket.on("error", () => socket.destroy());
      });
    // Unix: a group leader, as the command a client starts, so that the scheduler sees its group alive
    const sleeperProcess = spawn(process.execPath, ["-e", "setTimeout(function(){},20000)"], {
      windowsHide: true,
      detached: process.platform !== "win32",
    });
    children.push(sleeperProcess);
    const fakeClientPid = process.pid;
    const first = await raw();
    send(first, { version: ProtocolVersion, ...registration("A", 2, fakeClientPid) });
    await delay(300);
    send(first, {
      version: ProtocolVersion,
      type: "started",
      commandPid: sleeperProcess.pid ?? 0,
      spawnedAt: Date.now(),
    });
    const waiter = await raw();
    send(waiter, { version: ProtocolVersion, ...registration("W", 2, 1) });
    await delay(300);
    const keptId = (await jobWith(port, "A")).id;
    first.destroy();
    await delay(500);
    const again = await raw();
    send(again, {
      version: ProtocolVersion,
      ...registration("A", 2, fakeClientPid),
      type: "adopt",
      commandPid: sleeperProcess.pid ?? 0,
      spawnedAt: Date.now(),
    });
    await delay(500);
    const readopted = await statusOf(port);
    assert.deepStrictEqual(
      [readopted.used, readopted.running.map((j) => j.command), readopted.waiting.map((j) => j.command)],
      [2, ["A"], ["W"]],
      "re-registration replaces the kept slot without handing it to W",
    );
    assert.strictEqual(readopted.running[0]?.id, keptId, "the re-registered job keeps its id");
    again.destroy();
    waiter.destroy();
    stopChild(sleeperProcess);

    // 5. A job whose client was lost before a restart still holds its slot after it (restored from jobs-<port>.json).
    await usedWithin(port, 0, 5000);
    const lostStarted = path.join(dir, "started-5");
    const lost = client(port, started(8000, lostStarted));
    await whenExists(lostStarted);
    lost.kill("SIGKILL"); // the client only
    const beforeRestart = await waitFor(port, "the client is seen lost", (s) => (s.running[0]?.orphan ? s : undefined));
    await request(port, { type: "stop" });
    await delay(300);
    servers.push(await startTestScheduler(port, 2, env));
    const restored = await statusOf(port);
    assert.strictEqual(restored.used, 1, "the job of the lost client is restored after the restart");
    assert.strictEqual(restored.running[0]?.orphan, true);
    assert.strictEqual(restored.running[0]?.id, beforeRestart.running[0]?.id, "a job keeps its id across a restart");
    const fresh = client(port, sleeper(100));
    await delay(StartupGraceMs + 800);
    assert.ok(fresh.exitCode === 0 || (await fresh.done) === 0);
    const newIds = JSON.parse(fs.readFileSync(path.join(dir, `jobs-${port}.json`), "utf8")) as { nextId: number };
    assert.ok(newIds.nextId > (beforeRestart.running[0]?.id ?? Infinity), "new ids continue after the saved ones");
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanReleaseMs), 0, "and released when its command exits");

    // 9. hrrn re-grants while jobs wait, not only when a job ends: a short job that overtakes the waiting wide one
    //    starts within RegrantMs, although nothing ends before the running job (20 s).
    await checkRegrant(port + 1, env, client, sleeper, servers, ports);

    // 10. move, set-weight and --after end to end, and their survival across a scheduler restart.
    await checkPlacementAndDependencies(port + 2, env, client, { sleeper, exiting, gated, open, dir }, servers, ports);

    // 11. --gpu jobs wait for their GPU turn in the scheduler: in order, visible and cancellable while they wait.
    await checkGpuTurnsEndToEnd(port + 3, env, client, { gated, open, dir }, servers, ports);

    // 12. wait <id> exits with the outcome of a job: when a running job ends (also across a restart), at once for an
    //     ended one, with 2 for an unknown id.
    await checkWait(port + 4, env, client, { gated, open, exiting, dir }, servers, ports);
  } finally {
    children.forEach((child) => child.exitCode === null && stopChild(child));
    servers.forEach((server) => server.kill("SIGKILL"));
    for (const port of ports) {
      await request(port, { type: "stop" }).catch(() => null);
    }
    await delay(200);
    removeDirectory(dir);
  }
  process.stdout.write("job-queue self-test passed\n");
}
