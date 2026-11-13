// The self-test of the job queue (job-queue.js --self-test): the scheduler unit checks, the shell helpers, and
// end-to-end runs against schedulers on private ports with a private state directory, so that it never touches the
// queue the machine uses.

import * as assert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { EntryScript, OrphanPollMs, StartupGraceMs } from "./constants.ts";
import { request } from "./client-connect.ts";
import { RunHistory } from "./history.ts";
import { type StatusJob, type StatusReply } from "./protocol.ts";
import { SchedulerChecks } from "./scheduler-checks.ts";
import { FairnessChecks } from "./scheduler-fairness-checks.ts";
import { JobServerChecks, JobStateChecks } from "./server-job-checks.ts";
import { ClientChecks } from "./self-test-client.ts";
import { checkBuildSplit } from "./self-test-build-split.ts";
import { CommandShell, gitBashPath, isTestCommand, quoteArgument } from "./shell.ts";
import { MachineProfile, MinimumSlots, cpuOnlyTestProjects, parseBudgets, testMemoryMb } from "./machine.ts";
import { delay, stopChild, QueueFixture, registration } from "./self-test-support.ts";
import { checkPlacementAndDependencies } from "./self-test-dependencies.ts";
import { checkGpuTurnsEndToEnd, checkRegrant, checkStart, checkWait } from "./self-test-parts.ts";
import { checkReconnects } from "./self-test-reconnect.ts";
import { checkRestarts } from "./self-test-restart.ts";
import { checkView } from "./self-test-view.ts";

// How long a test waits for the slot of a lost client to come back: the orphan poll reads the processes through
// PowerShell on Windows, which can take seconds on a busy machine (a CI runner)
const OrphanReleaseMs = 30000;

/** Checks of the pure helpers: the node limit, quoting, which commands run tests. */
function checkShellHelpers(): void {
  assert.strictEqual(
    CommandShell.limitMsbuildNodes("dotnet test X.csproj && dotnet build", 6),
    "dotnet test -m:6 X.csproj && dotnet build -m:6",
  );
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet build-server shutdown", 6), "dotnet build-server shutdown");
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet test -m:2", 6), "dotnet test -m:2");
  assert.strictEqual(
    CommandShell.limitMsbuildNodes("dotnet build -m:2 && dotnet test", 6),
    "dotnet build -m:2 && dotnet test -m:6",
  );
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet build --maxcpucount 4", 6), "dotnet build --maxcpucount 4");
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet.exe build", 6), "dotnet.exe build -m:6");
  assert.strictEqual(
    CommandShell.limitMsbuildNodes('"C:\\Program Files\\dotnet\\dotnet.exe" test x', 6),
    '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x',
  );
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet run --project x", 6), "dotnet run --project x");
  assert.strictEqual(
    CommandShell.limitMsbuildNodes('dotnet test --filter "A|B" -m:2', 6),
    'dotnet test --filter "A|B" -m:2',
  );
  assert.strictEqual(CommandShell.limitMsbuildNodes("dotnet test bin/x.Tests.dll", 6), "dotnet test bin/x.Tests.dll");
  assert.strictEqual(quoteArgument("pwsh", "@x.rsp"), "'@x.rsp'", "PowerShell would splat @x");
  assert.strictEqual(quoteArgument("pwsh", "-Dfoo.bar=1"), "'-Dfoo.bar=1'", "PowerShell splits -a.b at the dot");
  assert.strictEqual(quoteArgument("bash", "C:\\dir\\f.txt"), "'C:\\dir\\f.txt'", "bash reads \\ as an escape");
  assert.strictEqual(quoteArgument("cmd", "C:\\dir\\f.txt"), "C:\\dir\\f.txt");
  assert.strictEqual(quoteArgument("pwsh", "x,"), "'x,'", "PowerShell reads a trailing comma as an array");
  assert.strictEqual(quoteArgument("bash", "-p:Foo.Bar=1"), "-p:Foo.Bar=1");
  assert.strictEqual(quoteArgument("bash", "it's"), "'it'\\''s'");
  assert.strictEqual(isTestCommand("just test --filter X"), true);
  assert.strictEqual(isTestCommand("dotnet build Henicos.slnx"), false);
  checkMachineDefaults();
  checkBuildSplit(assert);
}

/** The defaults that follow the machine, and the memory a test run needs. */
function checkMachineDefaults(): void {
  assert.strictEqual(new MachineProfile(24).slots, 8);
  assert.strictEqual(new MachineProfile(8).slots, 3);
  assert.strictEqual(new MachineProfile(4).slots, MinimumSlots, "a small machine still runs a build beside a test run");
  assert.strictEqual(new MachineProfile(undefined, 64 * 1024 ** 3).memoryPoolMb, 32 * 1024);
  const budgets = parseBudgets(
    ["# comment", "Kassyi.GSharp.Tests.Cpu: 2880, stress 6720", "Kassyi.GSharp.Tests.Integration: 128", ""].join("\n"),
  );
  assert.deepStrictEqual(budgets.get("Kassyi.GSharp.Tests.Cpu"), { normal: 2880, stress: 6720 });
  assert.deepStrictEqual(budgets.get("Kassyi.GSharp.Tests.Integration"), { normal: 128, stress: 128 });
  const memory = (command: string): number => testMemoryMb(command, isTestCommand(command), budgets);
  assert.strictEqual(memory("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu"), 2880);
  assert.strictEqual(memory('just test-category Stress "--project" kassyi.gsharp.tests.cpu'), 6720);
  assert.strictEqual(memory("just test"), 2880 + 128, "a whole-solution run counts every test host");
  assert.strictEqual(
    memory('dotnet test --filter "Category!=Stress&Category!=Benchmark"'),
    2880 + 128,
    "a filter that leaves the stress tests out counts the normal budgets",
  );
  assert.strictEqual(memory("dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cpu"), 0, "a build takes no memory");
  assert.strictEqual(testMemoryMb("just test", true, new Map()), 0, "without budgets nothing is held back");
  checkCpuOnlyProjects();
}

/** The hint of run --gpu: only when every named test project's csproj says it runs on the CPU. */
function checkCpuOnlyProjects(): void {
  const tests = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-tests-"));
  try {
    const project = (category: string, name: string, body: string): void => {
      fs.mkdirSync(path.join(tests, category, name), { recursive: true });
      fs.writeFileSync(
        path.join(tests, category, name, `${name}.csproj`),
        `<Project><PropertyGroup>${body}</PropertyGroup></Project>`,
      );
    };
    project("Compiler", "Suite.Cpu", "<GSharpTargetDialect>Cpu</GSharpTargetDialect>");
    project("Compiler", "Suite.Cuda", "<GSharpTargetDialect>Cuda</GSharpTargetDialect>");
    project("Stdlib", "Suite.Math", "<CudaCapability>89</CudaCapability>");
    const names = ["Suite.Cpu", "Suite.Cuda", "Suite.Math", "Suite.Missing"];
    const cpuOnly = (command: string): string[] => cpuOnlyTestProjects(command, names, tests);
    assert.deepStrictEqual(cpuOnly("dotnet test tests/Compiler/Suite.Cpu --no-build"), ["Suite.Cpu"]);
    assert.deepStrictEqual(
      cpuOnly("dotnet test tests/Compiler/Suite.Cpu && dotnet test tests/Compiler/Suite.Cuda"),
      [],
    );
    assert.deepStrictEqual(cpuOnly("dotnet test tests/Stdlib/Suite.Math"), [], "no dialect in the csproj: no hint");
    assert.deepStrictEqual(cpuOnly("dotnet test Suite.Missing"), [], "no csproj: no hint");
    assert.deepStrictEqual(cpuOnly("just test"), [], "a command that names no project: no hint");
  } finally {
    fs.rmSync(tests, { recursive: true, force: true });
  }
}

export async function selfTest(): Promise<void> {
  for (const [name, check] of Object.entries({ ...FairnessChecks, ...SchedulerChecks, ...JobStateChecks })) {
    try {
      check(assert);
    } catch (error) {
      process.stderr.write(`${name} failed\n`);
      throw error;
    }
  }
  checkShellHelpers();
  for (const [name, check] of Object.entries({ ...JobServerChecks, ...ClientChecks })) {
    try {
      await check(assert);
    } catch (error) {
      process.stderr.write(`${name} failed\n`);
      throw error;
    }
  }
  checkView();

  const fixture = new QueueFixture();
  const { dir, env, children, servers, ports } = fixture;
  const { client, sleeper, open, started, whenExists, startScheduler } = fixture;
  const { statusOf, usedWithin, waitFor, jobWith, runningWith, jobsWith, rawClient } = fixture;
  try {
    // 1. At most 2 jobs at once, in arrival order; a client killed with its command frees its slot.
    const port = 20000 + Math.floor(Math.random() * 20000);
    ports.push(port);
    await startScheduler(port, 2);
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
    const rawRun = async (command: string, completed: boolean): Promise<void> => {
      const c = await rawClient(port);
      c.send(registration(command, 1, process.pid));
      assert.ok((await c.until("granted")) !== null, `granted: ${command}`);
      c.send({ type: "done", completed, exitCode: 0 });
      c.socket.end();
      await c.closed;
    };
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
    // Unix: a group leader, as the command a client starts, so that the scheduler sees its group alive
    const sleeperProcess = spawn(process.execPath, ["-e", "setTimeout(function(){},20000)"], {
      windowsHide: true,
      detached: process.platform !== "win32",
    });
    children.push(sleeperProcess);
    const fakeClientPid = process.pid;
    const first = await rawClient(port);
    const requestA = registration("A", 2, fakeClientPid);
    first.send(requestA);
    assert.ok((await first.until("granted")) !== null, "A is granted");
    const spawnedAt = Date.now();
    first.send({ type: "started", commandPid: sleeperProcess.pid ?? 0, spawnedAt });
    const waiter = await rawClient(port);
    waiter.send(registration("W", 2, 1));
    await waiter.until("queued");
    const keptId = (await runningWith(port, "A")).id;
    first.close();
    await waitFor(port, "A is seen lost", (s) =>
      s.running.some((j) => j.id === keptId && j.orphan) ? true : undefined,
    );
    const again = await rawClient(port);
    again.send({ ...requestA, type: "adopt", id: keptId, commandPid: sleeperProcess.pid ?? 0, spawnedAt });
    assert.ok((await again.until("adopted")) !== null, "A re-registers");
    const readopted = await statusOf(port);
    assert.deepStrictEqual(
      [readopted.used, readopted.running.map((j) => j.command), readopted.waiting.map((j) => j.command)],
      [2, ["A"], ["W"]],
      "re-registration replaces the kept slot without handing it to W",
    );
    assert.strictEqual(readopted.running[0]?.id, keptId, "the re-registered job keeps its id");
    again.close();
    waiter.close();
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
    await startScheduler(port, 2);
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
    await checkRegrant(fixture, port + 1);

    // 10. move, set-weight and --after end to end, and their survival across a scheduler restart.
    await checkPlacementAndDependencies(fixture, port + 2);

    // 11. --gpu jobs wait for their GPU turn in the scheduler: in order, visible and cancellable while they wait.
    await checkGpuTurnsEndToEnd(fixture, port + 3);

    // 12. wait <id> exits with the outcome of a job: when a running job ends (also across a restart), at once for an
    //     ended one, with 2 for an unknown id.
    await checkWait(fixture, port + 4);

    // 13. start <id> runs a waiting job beside the running ones, over the pool.
    await checkStart(fixture, port + 5);

    // 14. A client that comes back: re-registrations that race with the old connection's close or the observation of
    //     its processes, and another process that reuses its pid.
    await checkReconnects(fixture, port + 6);

    // 15. A restarted scheduler holds every job of its state file from the first event; an older file keeps the grace.
    await checkRestarts(fixture, port + 10);
  } finally {
    await fixture.cleanup();
  }
  process.stdout.write("job-queue self-test passed\n");
}
