// The self-test of the job queue (job-queue.js --self-test): the scheduler unit checks, the shell helpers, and
// end-to-end runs against schedulers on private ports with a private state directory, so that it never touches the
// queue the machine uses.

import * as assert from 'node:assert';
import * as fs from 'node:fs';
import * as net from 'node:net';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import {
  CpusVariable,
  GpuLockHeldVariable,
  EntryScript,
  InGpuLockVariable,
  JobVariable,
  OrphanPollMs,
  ProtocolVersion,
  RegrantMs,
  ShellVariable,
  StartupGraceMs,
  StateDirectoryVariable,
} from './constants.ts';
import { connect, request } from './client.ts';
import { RunHistory } from './history.ts';
import { onMessages, send, type Incoming, type StatusJob, type StatusReply } from './protocol.ts';
import { SchedulerChecks } from './scheduler-checks.ts';
import { defaultWeight, gitBashPath, limitMsbuildNodes, quoteArgument } from './shell.ts';
import { killProcessTree } from './system.ts';

const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Stops a process the test started, with what it started: Windows ends the tree; on Unix a client stops its command's
 * process group on SIGTERM (the test does not start its children as group leaders, so a group kill would miss them).
 */
function stopChild(child: ChildProcess): void {
  if (process.platform === 'win32') {
    killProcessTree(child.pid);
  } else {
    child.kill('SIGTERM');
  }
}

/**
 * Removes a test directory. A scheduler that has not exited yet can still hold it open on Windows: retry, and only warn
 * when it stays, so that a cleanup failure never hides the failure of the test itself.
 */
function removeDirectory(directory: string): void {
  try {
    fs.rmSync(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  } catch (error) {
    process.stderr.write(`could not remove ${directory}: ${error instanceof Error ? error.message : String(error)}
`);
  }
}

/** A child process with a promise of its exit code. */
type Client = ChildProcess & { done: Promise<number | null> };

/** Starts a scheduler on a private port and resolves once it accepts connections. */
async function startTestScheduler(port: number, slots: number, env: NodeJS.ProcessEnv): Promise<ChildProcess> {
  const server = spawn(process.execPath, [EntryScript, 'serve', '--port', String(port), '--slots', String(slots)], {
    stdio: 'ignore',
    windowsHide: true,
    env,
  });
  for (let i = 0; i < 50; i++) {
    const probe = await connect(port);
    if (probe !== null) {
      probe.destroy();
      break;
    }
    await delay(100);
  }
  return server;
}

async function statusOf(port: number): Promise<StatusReply> {
  const reply = await request(port, { type: 'status' });
  assert.ok(reply !== null && reply.type === 'status', `a status reply from port ${port}`);
  return reply as unknown as StatusReply;
}

/** Polls the scheduler until `used` reaches the expected value or the time is up; resolves to the last value. */
async function usedWithin(port: number, expected: number, ms: number): Promise<number> {
  const deadline = Date.now() + ms;
  for (;;) {
    const used = (await statusOf(port)).used;
    if (used === expected || Date.now() > deadline) {
      return used;
    }
    await delay(250);
  }
}

/**
 * Polls the scheduler until `probe` finds what it looks for. The checks wait for states, not for fixed times: a client
 * can take seconds to start where the scripts are slow to load (WSL reading /mnt), and the scheduler a client starts
 * after a stop may not listen yet.
 */
async function waitFor<T>(port: number, what: string, probe: (status: StatusReply) => T | undefined, ms = 15000): Promise<T> {
  const deadline = Date.now() + ms;
  for (;;) {
    const reply = await request(port, { type: 'status' }).catch(() => null);
    const found = reply !== null && reply.type === 'status' ? probe(reply as unknown as StatusReply) : undefined;
    if (found !== undefined) {
      return found;
    }
    assert.ok(Date.now() < deadline, `within ${ms} ms: ${what}`);
    await delay(100);
  }
}

/** Polls until a job with this command is known (running or waiting) and resolves to it. */
function jobWith(port: number, command: string): Promise<StatusJob> {
  return waitFor(port, `a job runs or waits: ${command}`, (s) => s.running.concat(s.waiting).find((j) => j.command === command));
}

/** Polls until a job with this command runs and its client has reported the command's process. */
function runningWith(port: number, command: string): Promise<StatusJob> {
  return waitFor(port, `the command runs: ${command}`, (s) => s.running.find((j) => j.command === command && j.commandPid !== null));
}

/** Polls until `count` jobs with this command run or wait. */
function jobsWith(port: number, command: string, count: number): Promise<StatusJob[]> {
  return waitFor(port, `${count} jobs run or wait: ${command}`, (s) => {
    const jobs = s.running.concat(s.waiting).filter((j) => j.command === command);
    return jobs.length >= count ? jobs : undefined;
  });
}

/** Checks of the pure helpers: the node limit, quoting, the default weight. */
function checkShellHelpers(): void {
  assert.strictEqual(limitMsbuildNodes('dotnet test X.csproj && dotnet build', 6), 'dotnet test -m:6 X.csproj && dotnet build -m:6');
  assert.strictEqual(limitMsbuildNodes('dotnet build-server shutdown', 6), 'dotnet build-server shutdown');
  assert.strictEqual(limitMsbuildNodes('dotnet test -m:2', 6), 'dotnet test -m:2');
  assert.strictEqual(limitMsbuildNodes('dotnet build -m:2 && dotnet test', 6), 'dotnet build -m:2 && dotnet test -m:6');
  assert.strictEqual(limitMsbuildNodes('dotnet build --maxcpucount 4', 6), 'dotnet build --maxcpucount 4');
  assert.strictEqual(limitMsbuildNodes('dotnet.exe build', 6), 'dotnet.exe build -m:6');
  assert.strictEqual(limitMsbuildNodes('"C:\\Program Files\\dotnet\\dotnet.exe" test x', 6), '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x');
  assert.strictEqual(limitMsbuildNodes('dotnet run --project x', 6), 'dotnet run --project x');
  assert.strictEqual(limitMsbuildNodes('dotnet test --filter "A|B" -m:2', 6), 'dotnet test --filter "A|B" -m:2');
  assert.strictEqual(limitMsbuildNodes('dotnet test bin/x.Tests.dll', 6), 'dotnet test bin/x.Tests.dll');
  assert.strictEqual(quoteArgument('pwsh', '@x.rsp'), "'@x.rsp'", 'PowerShell would splat @x');
  assert.strictEqual(quoteArgument('pwsh', '-Dfoo.bar=1'), "'-Dfoo.bar=1'", 'PowerShell splits -a.b at the dot');
  assert.strictEqual(quoteArgument('bash', 'C:\\dir\\f.txt'), "'C:\\dir\\f.txt'", 'bash reads \\ as an escape');
  assert.strictEqual(quoteArgument('cmd', 'C:\\dir\\f.txt'), 'C:\\dir\\f.txt');
  assert.strictEqual(quoteArgument('pwsh', 'x,'), "'x,'", 'PowerShell reads a trailing comma as an array');
  assert.strictEqual(quoteArgument('bash', '-p:Foo.Bar=1'), '-p:Foo.Bar=1');
  assert.strictEqual(quoteArgument('bash', "it's"), "'it'\\''s'");
  assert.strictEqual(defaultWeight('just test --filter X'), 2);
  assert.strictEqual(defaultWeight('dotnet build Henicos.slnx'), 1);
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

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'job-queue-self-test-'));
  const env: NodeJS.ProcessEnv = { ...process.env };
  delete env[JobVariable];
  delete env[InGpuLockVariable];
  delete env[CpusVariable];
  delete env[GpuLockHeldVariable]; // a self-test run under gpu-lock.js must still queue its GPU-less clients
  env[StateDirectoryVariable] = dir; // a scheduler that a client starts logs here, not into the main checkout
  // The test commands below are written for cmd / sh; the shells themselves are checked in part 6.
  env[ShellVariable] = process.platform === 'win32' ? 'cmd' : 'sh';
  // --gpu jobs take a private GPU lock, never the machine's (gpu-lock.js reads GSHARP_GPU_LOCK_PATH), without the pool readings
  env.GSHARP_GPU_LOCK_PATH = path.join(dir, 'gpu-lock');
  env.GSHARP_GPU_POOL_WATCH = '0';
  const children: ChildProcess[] = [];
  // --slots: a scheduler that a client restarts gets the same capacity as the test one.
  const client = (port: number, command: string, extra: string[] = []): Client => {
    const args = [EntryScript, 'run', '--port', String(port), '--slots', '2', '--weight', '1', ...extra, '--', command];
    const child = spawn(process.execPath, args, { stdio: 'ignore', windowsHide: true, env }) as Client;
    children.push(child);
    child.done = new Promise((resolve) => child.on('close', resolve));
    return child;
  };
  const sleeper = (ms: number): string => `"${process.execPath}" -e "setTimeout(function(){},${ms})"`;
  // A command that ends with `code` once `file` exists, so that a check decides when a job ends
  const gated = (code: number, file: string): string =>
    `"${process.execPath}" -e "var f=process.argv[1];setInterval(function(){if(require('fs').existsSync(f))process.exit(${code})},50)" "${file}"`;
  const open = (file: string): void => fs.writeFileSync(file, '');
  // A command that writes `file` once it runs, then sleeps: the client reports its shell's process at once, and a
  // client killed before the shell started the command ends the shell with it, so that no command is left to hold a slot
  const started = (ms: number, file: string): string =>
    `"${process.execPath}" -e "require('fs').writeFileSync(process.argv[1],'');setTimeout(function(){},${ms})" "${file}"`;
  const whenExists = async (file: string): Promise<void> => {
    for (let i = 0; i < 150 && !fs.existsSync(file); i++) {
      await delay(100);
    }
    assert.ok(fs.existsSync(file), `the command started: ${file}`);
  };
  const exiting = (code: number, ms = 0): string => `"${process.execPath}" -e "setTimeout(function(){process.exit(${code})},${ms})"`;
  const servers: ChildProcess[] = [];
  const ports: number[] = [];
  try {
    // 1. At most 2 jobs at once, in arrival order; a client killed with its command frees its slot.
    const port = 20000 + Math.floor(Math.random() * 20000);
    ports.push(port);
    servers.push(await startTestScheduler(port, 2, env));
    await delay(StartupGraceMs);
    const marker = path.join(dir, 'log.txt');
    const script = path.join(dir, 'job.js');
    const gate = path.join(dir, 'gate-1');
    // Logs its start, waits for the gate, logs its end 300 ms later
    fs.writeFileSync(
      script,
      "const fs=require('fs');const [f,i,g]=process.argv.slice(2);" +
        "fs.appendFileSync(f,`start ${i} ${Date.now()}\\n`);" +
        'const t=setInterval(()=>{if(fs.existsSync(g)){clearInterval(t);' +
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
    assert.strictEqual(before.used, 2, 'the holder and one job fill the 2 slots');
    assert.strictEqual(before.waiting.length, 3, 'the other jobs wait');
    before.waiting.forEach((waiting, index) => {
      assert.strictEqual(waiting.ahead, index, 'the jobs ahead of a waiting job');
      assert.ok(waiting.startAt !== null && waiting.endAt !== null && waiting.endAt > waiting.startAt, 'expected start and end');
      const previous = before.waiting[index - 1];
      assert.ok(previous === undefined || (waiting.startAt ?? 0) >= (previous.startAt ?? 0), 'a later job is expected to start later');
    });
    // run prints its id at once; status <id> --json tells the same job
    // Both slots: it waits while the holder runs, however fast the jobs above have ended
    const asking = spawn(process.execPath, [EntryScript, 'run', '--port', String(port), '--slots', '2', '--weight', '2', '--', sleeper(100)], {
      stdio: ['ignore', 'ignore', 'pipe'],
      windowsHide: true,
      env,
    });
    children.push(asking);
    const queuedId = await new Promise<number>((resolve) => {
      let text = '';
      asking.stderr.on('data', (data: Buffer) => {
        text += data.toString();
        const match = /queued #(\d+): (\d+) ahead, expected start/.exec(text);
        if (match) {
          resolve(Number(match[1]));
        }
      });
      asking.on('close', () => resolve(0));
    });
    assert.ok(queuedId > 0, 'run prints "queued #<id>: <n> ahead, expected start ..." while it waits');
    const one = spawnSync(process.execPath, [EntryScript, 'status', String(queuedId), '--json', '--port', String(port)], {
      encoding: 'utf8',
      windowsHide: true,
      env,
    });
    const view = JSON.parse(one.stdout) as StatusJob;
    assert.strictEqual(view.id, queuedId);
    assert.strictEqual(view.state, 'waiting');
    assert.ok(Number.isFinite(view.endAt), 'status <id> --json gives the expected end');
    open(gate);
    stopChild(holder);
    assert.deepStrictEqual(await Promise.all(runs), [0, 0, 0, 0]);
    const events = fs
      .readFileSync(marker, 'utf8')
      .trim()
      .split('\n')
      .map((line) => line.split(' '))
      .map(([kind, i, time]) => ({ kind, i: Number(i), time: Number(time) }))
      .sort((x, y) => x.time - y.time || (x.kind === 'end' ? -1 : 1));
    let concurrent = 0;
    let peak = 0;
    for (const event of events) {
      concurrent += event.kind === 'start' ? 1 : -1;
      peak = Math.max(peak, concurrent);
    }
    assert.ok(peak <= 2, `at most 2 jobs ran at once, saw ${peak}`);
    assert.deepStrictEqual(events.filter((e) => e.kind === 'start').map((e) => e.i), [0, 1, 2, 3], 'arrival order');
    assert.strictEqual(await usedWithin(port, 0, OrphanPollMs * 3), 0, 'every slot came back');

    // 2. A client killed alone (its command keeps running) keeps the slot until the command exits.
    const loneStarted = path.join(dir, 'started-2');
    const lone = client(port, started(8000, loneStarted));
    await whenExists(loneStarted);
    lone.kill('SIGKILL'); // the client only; on Windows its shell ends with it, the command the shell started runs on
    await waitFor(port, 'the client is seen lost', (s) => (s.running[0]?.orphan ? true : undefined));
    await delay(OrphanPollMs + 500); // a poll of the lost client's command has run
    const orphaned = await statusOf(port);
    assert.strictEqual(orphaned.used, 1, 'the slot stays while the command of a lost client runs');
    assert.strictEqual(orphaned.running[0]?.orphan, true);
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanPollMs * 3), 0, 'the slot comes back when it exits');

    // 3. cancel-all stops running jobs and empties the queue; the clients exit with 4.
    const r1 = client(port, sleeper(60000));
    const r2 = client(port, sleeper(60000));
    const w1 = client(port, sleeper(60000));
    await jobsWith(port, sleeper(60000), 3);
    const result = await request(port, { type: 'cancel', all: true });
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
        const s = net.connect(port, '127.0.0.1', () =>
          send(s, { version: ProtocolVersion, ...registration(command, 1, process.pid) }),
        );
        onMessages(s, (m) => {
          if (m.type === 'granted') {
            send(s, { version: ProtocolVersion, type: 'done', completed, exitCode: 0 });
            s.end();
          }
        });
        s.on('close', () => resolve());
        s.on('error', () => resolve());
      });
    await rawRun('stopped early', false);
    await rawRun('ran to its end', true);
    // The scheduler records when it sees the close, after our side closed; the early run's close came before
    let recorded = new RunHistory(path.join(dir, 'history.json'), () => {});
    for (let i = 0; i < 30 && recorded.estimate('ran to its end') === null; i++) {
      await delay(100);
      recorded = new RunHistory(path.join(dir, 'history.json'), () => {});
    }
    for (const i of [0, 1, 2, 3]) {
      assert.ok(recorded.estimate(job(i)) !== null, `job ${i} is recorded`);
    }
    assert.ok(recorded.estimate('ran to its end') !== null, 'a run that ended by itself is recorded');
    assert.strictEqual(recorded.estimate('stopped early'), null, 'a run stopped early is not recorded');
    assert.strictEqual(recorded.estimate(sleeper(60000)), null, 'killed and cancelled runs are not recorded');
    assert.strictEqual(recorded.estimate(started(8000, loneStarted)), null, 'the run of a lost client is not recorded');

    // 4. After a scheduler restart, running jobs re-register before waiting ones are granted: never over capacity.
    const k1 = client(port, sleeper(6000));
    const k2 = client(port, sleeper(6000));
    await waitFor(port, 'two commands run', (s) => (s.running.filter((j) => j.commandPid !== null).length === 2 ? true : undefined));
    const k3 = client(port, sleeper(500));
    await jobWith(port, sleeper(500));
    const restartedAt = Date.now();
    servers[0]?.kill('SIGKILL'); // the clients start the next scheduler themselves
    let maxUsed = 0;
    let adoptedSince = Infinity;
    for (let i = 0; i < 20; i++) {
      const snapshot = await request(port, { type: 'status' }).catch(() => null); // the scheduler may be restarting
      if (snapshot !== null && snapshot.type === 'status') {
        const status = snapshot as unknown as StatusReply;
        maxUsed = Math.max(maxUsed, status.used);
        status.running.forEach((j) => (adoptedSince = Math.min(adoptedSince, j.grantedAt ?? Infinity)));
      }
      await delay(150);
    }
    assert.ok(maxUsed <= 2, `never over capacity after a restart, saw ${maxUsed}`);
    assert.ok(adoptedSince < restartedAt, 'a re-registered job keeps the start of its command, not the restart time');
    assert.deepStrictEqual(await Promise.all([k1.done, k2.done, k3.done]), [0, 0, 0]);

    // 6. The command runs in the chosen shell and its exit code comes back: PowerShell passes the code of the last
    //    native program on; bash and sh understand their own syntax.
    const exitOf = async (shell: string, command: string): Promise<number | null> => client(port, command, ['--shell', shell]).done;
    const node = `"${process.execPath.replace(/\\/g, '/')}"`;
    const hasGitBash = spawnSync(gitBashPath(), ['-c', 'exit 0'], { windowsHide: true }).status === 0;
    if (process.platform === 'win32') {
      assert.strictEqual(await exitOf('pwsh', `$x = 1; & ${node} -e "process.exit(5)"`), 5, 'pwsh: native exit code');
      assert.strictEqual(await exitOf('cmd', 'exit 6'), 6, 'cmd');
    }
    if (hasGitBash) {
      assert.strictEqual(await exitOf('bash', 'FOO=7; true 2>/dev/null; exit $FOO'), 7, 'bash syntax');
    }

    // 7. Several words after `--` arrive as argv already split by the caller's shell; each reaches the command as it was.
    const argvExit = (shell: string): Promise<number | null> =>
      new Promise((resolve) => {
        // The command compares its own argv with the words, so each word makes the round trip through the shell
        const check = 'process.exit(JSON.stringify(process.argv.slice(1))===process.env.BQ_EXPECT?0:9)';
        const words = ['A&B c|d', "it's", '-Dfoo.bar=1', 'C:\\dir\\f.txt', '@x.rsp', 'x,', '-p:A=1', 'Category!=Stress', '$HOME'];
        const args = [EntryScript, 'run', '--port', String(port), '--shell', shell, '--', process.execPath, '-e', check, ...words];
        const child = spawn(process.execPath, args, { stdio: 'ignore', windowsHide: true, env: { ...env, BQ_EXPECT: JSON.stringify(words) } });
        children.push(child);
        child.on('close', resolve);
      });
    const shells = process.platform === 'win32' ? ['pwsh', 'cmd'] : ['sh'];
    if (hasGitBash) {
      shells.push('bash');
    }
    for (const shell of shells) {
      assert.strictEqual(await argvExit(shell), 0, `argv after -- survives ${shell}`);
    }

    // 8. A client that lost only its connection re-registers: its kept slot is replaced under the same id, and a
    //    waiting job does not get it in between (capacity 2: A weighs 2, W waits with 2).
    await usedWithin(port, 0, 5000);
    const raw = (): Promise<net.Socket> =>
      new Promise((resolve) => {
        const socket = net.connect(port, '127.0.0.1', () => resolve(socket));
        socket.on('error', () => socket.destroy());
      });
    // Unix: a group leader, as the command a client starts, so that the scheduler sees its group alive
    const sleeperProcess = spawn(process.execPath, ['-e', 'setTimeout(function(){},20000)'], {
      windowsHide: true,
      detached: process.platform !== 'win32',
    });
    children.push(sleeperProcess);
    const fakeClientPid = process.pid;
    const first = await raw();
    send(first, { version: ProtocolVersion, ...registration('A', 2, fakeClientPid) });
    await delay(300);
    send(first, { version: ProtocolVersion, type: 'started', commandPid: sleeperProcess.pid ?? 0, spawnedAt: Date.now() });
    const waiter = await raw();
    send(waiter, { version: ProtocolVersion, ...registration('W', 2, 1) });
    await delay(300);
    const keptId = (await jobWith(port, 'A')).id;
    first.destroy();
    await delay(500);
    const again = await raw();
    send(again, {
      version: ProtocolVersion,
      ...registration('A', 2, fakeClientPid),
      type: 'adopt',
      commandPid: sleeperProcess.pid ?? 0,
      spawnedAt: Date.now(),
    });
    await delay(500);
    const readopted = await statusOf(port);
    assert.deepStrictEqual(
      [readopted.used, readopted.running.map((j) => j.command), readopted.waiting.map((j) => j.command)],
      [2, ['A'], ['W']],
      're-registration replaces the kept slot without handing it to W',
    );
    assert.strictEqual(readopted.running[0]?.id, keptId, 'the re-registered job keeps its id');
    again.destroy();
    waiter.destroy();
    stopChild(sleeperProcess);

    // 5. A job whose client was lost before a restart still holds its slot after it (restored from jobs-<port>.json).
    await usedWithin(port, 0, 5000);
    const lostStarted = path.join(dir, 'started-5');
    const lost = client(port, started(8000, lostStarted));
    await whenExists(lostStarted);
    lost.kill('SIGKILL'); // the client only
    const beforeRestart = await waitFor(port, 'the client is seen lost', (s) => (s.running[0]?.orphan ? s : undefined));
    await request(port, { type: 'stop' });
    await delay(300);
    servers.push(await startTestScheduler(port, 2, env));
    const restored = await statusOf(port);
    assert.strictEqual(restored.used, 1, 'the job of the lost client is restored after the restart');
    assert.strictEqual(restored.running[0]?.orphan, true);
    assert.strictEqual(restored.running[0]?.id, beforeRestart.running[0]?.id, 'a job keeps its id across a restart');
    const fresh = client(port, sleeper(100));
    await delay(StartupGraceMs + 800);
    assert.ok(fresh.exitCode === 0 || (await fresh.done) === 0);
    const newIds = JSON.parse(fs.readFileSync(path.join(dir, `jobs-${port}.json`), 'utf8')) as { nextId: number };
    assert.ok(newIds.nextId > (beforeRestart.running[0]?.id ?? Infinity), 'new ids continue after the saved ones');
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanPollMs * 3), 0, 'and released when its command exits');

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
    servers.forEach((server) => server.kill('SIGKILL'));
    for (const port of ports) {
      await request(port, { type: 'stop' }).catch(() => null);
    }
    await delay(200);
    removeDirectory(dir);
  }
  process.stdout.write('job-queue self-test passed\n');
}

/** A request message as a client sends it, for the raw protocol checks. */
function registration(command: string, weight: number, pid: number) {
  return {
    type: 'request' as const,
    weight,
    gpu: false,
    pid,
    commandPid: 0,
    spawnedAt: 0,
    cwd: '',
    command,
    queuedAt: Date.now(),
    id: 0,
    pin: null,
    before: null,
    after: null,
    keepIfFailed: false,
    gpuTurn: false,
  };
}

type ClientFactory = (port: number, command: string, extra?: string[]) => Client;

async function checkRegrant(
  port: number,
  env: NodeJS.ProcessEnv,
  client: ClientFactory,
  sleeper: (ms: number) => string,
  servers: ChildProcess[],
  ports: number[],
): Promise<void> {
  const hrrnDir = fs.mkdtempSync(path.join(os.tmpdir(), 'job-queue-self-test-hrrn-'));
  const longRun = sleeper(20000);
  const shortRun = sleeper(300);
  fs.writeFileSync(
    path.join(hrrnDir, 'history.json'),
    JSON.stringify({
      commands: {
        [RunHistory.key(longRun)]: { runs: [20000], lastUsed: 1 },
        // typical 20 s, longest 30 s: it cannot be backfilled before the running job ends
        [RunHistory.key(shortRun)]: { runs: [10000, 20000, 30000], lastUsed: 1 },
      },
    }),
  );
  try {
    ports.push(port);
    servers.push(await startTestScheduler(port, 2, { ...env, [StateDirectoryVariable]: hrrnDir }));
    await delay(StartupGraceMs);
    client(port, longRun);
    assert.strictEqual(await usedWithin(port, 1, 5000), 1, 'the long job runs before the wide one arrives');
    client(port, sleeper(301), ['--weight', '2']); // unknown: one minute; waits for both slots
    await delay(4000);
    // The short job arrives behind the wide one, which has waited about 4 s longer: their ratios cross about 2 s
    // later ((t + 20) / 20 = (t + 4 + 60) / 60), so it first waits, then starts at a re-grant, not at enqueue.
    // Without the re-grant it would start only when the long job ends, at least 13 s after it is seen waiting.
    client(port, shortRun);
    let seenWaitingAt = 0;
    let startedAfterWaiting = Infinity;
    const pollUntil = Date.now() + 20000;
    while (Date.now() < pollUntil) {
      const now = await statusOf(port);
      if (seenWaitingAt === 0 && now.waiting.some((j) => j.command === shortRun)) {
        seenWaitingAt = Date.now();
      }
      if (now.running.some((j) => j.command === shortRun)) {
        startedAfterWaiting = seenWaitingAt === 0 ? -1 : Date.now() - seenWaitingAt;
        break;
      }
      await delay(200);
    }
    assert.ok(seenWaitingAt > 0, 'the short job waited behind the wide one first');
    assert.ok(
      startedAfterWaiting >= 0 && startedAfterWaiting < RegrantMs + 5000,
      `the short job started ${startedAfterWaiting} ms after it was seen waiting, not at the next end`,
    );
    await request(port, { type: 'cancel', all: true });
    await usedWithin(port, 0, 5000); // the cancelled clients are gone before the stop: none re-registers
    await request(port, { type: 'stop' });
  } finally {
    removeDirectory(hrrnDir);
  }
}

async function checkPlacementAndDependencies(
  port: number,
  env: NodeJS.ProcessEnv,
  client: ClientFactory,
  commands: {
    sleeper: (ms: number) => string;
    exiting: (code: number, ms?: number) => string;
    gated: (code: number, file: string) => string;
    open: (file: string) => void;
    dir: string;
  },
  servers: ChildProcess[],
  ports: number[],
): Promise<void> {
  const { sleeper, exiting, gated, open, dir } = commands;
  ports.push(port);
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const cli = (...args: string[]) =>
    spawnSync(process.execPath, [EntryScript, ...args, '--port', String(port)], { encoding: 'utf8', windowsHide: true, env });
  const waitingIds = async (): Promise<number[]> => (await statusOf(port)).waiting.map((j) => j.id);

  // A holder takes both slots; x, y, z wait in arrival order
  const holder = client(port, sleeper(60000), ['--weight', '2']);
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
  assert.deepStrictEqual(await waitingIds(), [x.id, y.id, z.id], 'arrival order');
  assert.strictEqual(cli('move', String(z.id), '--first').status, 0, 'move --first');
  assert.deepStrictEqual(await waitingIds(), [z.id, x.id, y.id], 'the moved job is first');
  assert.strictEqual(cli('move', String(y.id), '--before', String(z.id)).status, 0, 'move --before');
  assert.deepStrictEqual(await waitingIds(), [y.id, z.id, x.id], 'right before the target');
  assert.strictEqual(cli('move', String(z.id), '--before', String(y.id)).status, 1, 'a cycle is refused');
  assert.strictEqual(cli('move', String(holderJob.id), '--first').status, 1, 'only a waiting job moves');
  assert.strictEqual(cli('set-weight', String(x.id), '2').status, 0, 'set-weight');
  assert.strictEqual((await jobWith(port, waitingCommands[0] as string)).weight, 2);
  // The places and weights survive a restart: the clients bring them when they queue again. The stop ends the waiting
  // clients' connections, which is no outcome of their jobs (a waiting job that ends records "cancelled")
  await request(port, { type: 'stop' });
  await delay(300);
  const saved = JSON.parse(fs.readFileSync(path.join(dir, `jobs-${port}.json`), 'utf8')) as { ended: [number, string][] };
  assert.deepStrictEqual(
    saved.ended.filter(([id]) => [x.id, y.id, z.id].includes(id)),
    [],
    'a stop records no outcome for the waiting jobs',
  );
  servers.push(await startTestScheduler(port, 2, env));
  // The holder re-registers too, so that the cancel below reaches it
  await waitFor(port, 'the jobs register again', (s) => (s.running.length === 1 && s.waiting.length === 3 ? true : undefined));
  assert.deepStrictEqual(await waitingIds(), [y.id, z.id, x.id], 'the order survives a restart');
  assert.strictEqual((await jobWith(port, waitingCommands[0] as string)).weight, 2, 'the weight survives a restart');
  await request(port, { type: 'cancel', all: true });
  assert.deepStrictEqual(await Promise.all([holder.done, ...waiters.map((w) => w.done)]), [4, 4, 4, 4]);
  await usedWithin(port, 0, 5000);

  // --after: waits for its predecessor although a slot is free, and runs once it succeeded
  const firstGate = path.join(dir, 'gate-10a');
  const first = client(port, gated(0, firstGate));
  const firstJob = await jobWith(port, gated(0, firstGate));
  const second = client(port, exiting(0, 1), ['--after', String(firstJob.id)]);
  const secondJob = await jobWith(port, exiting(0, 1));
  assert.strictEqual(secondJob.state, 'waiting', 'waits although a slot is free');
  assert.strictEqual(secondJob.blockedBy, firstJob.id);
  open(firstGate);
  assert.deepStrictEqual(await Promise.all([first.done, second.done]), [0, 0], 'runs after the predecessor succeeded');
  // A failed predecessor cancels its dependents, along the chain; --keep-if-failed runs anyway
  const failingGate = path.join(dir, 'gate-10b');
  const failing = client(port, gated(5, failingGate));
  const failingJob = await jobWith(port, gated(5, failingGate));
  const dependent = client(port, exiting(0, 2), ['--after', String(failingJob.id)]);
  const dependentJob = await jobWith(port, exiting(0, 2));
  const chained = client(port, exiting(0, 3), ['--after', String(dependentJob.id)]);
  const kept = client(port, exiting(0, 4), ['--after', String(failingJob.id), '--keep-if-failed']);
  await jobWith(port, exiting(0, 3));
  await jobWith(port, exiting(0, 4));
  open(failingGate);
  assert.deepStrictEqual(await Promise.all([failing.done, dependent.done, chained.done, kept.done]), [5, 4, 4, 0]);
  // --after is refused inside a queued job and inside the GPU lock, --gpu or not: waiting there could deadlock
  const refused = (variable: string, extra: string[]) =>
    spawnSync(process.execPath, [EntryScript, 'run', '--port', String(port), ...extra, '--after', String(firstJob.id), '--', exiting(0)], {
      windowsHide: true,
      env: { ...env, [variable]: '7' },
    }).status;
  assert.strictEqual(refused(JobVariable, []), 2, '--after inside a queued job');
  assert.strictEqual(refused(JobVariable, ['--gpu']), 2, '--gpu --after inside a queued job');
  assert.strictEqual(refused(GpuLockHeldVariable, []), 2, '--after inside the GPU lock');
  // A predecessor that already ended is known by its outcome; an unknown one is a usage error
  assert.strictEqual(await client(port, exiting(0, 5), ['--after', String(firstJob.id)]).done, 0, 'after a job that succeeded');
  assert.strictEqual(await client(port, exiting(0, 6), ['--after', String(failingJob.id)]).done, 4, 'after a job that failed');
  assert.strictEqual(await client(port, exiting(0, 7), ['--after', '999999']).done, 2, 'after an unknown job');
  // The outcomes survive a restart
  await request(port, { type: 'stop' });
  await delay(300);
  servers.push(await startTestScheduler(port, 2, env));
  // During the startup grace a predecessor is not checked yet, but one queued later than the job never is a
  // predecessor: refused at once, not left waiting for ever
  assert.strictEqual(await client(port, exiting(0, 9), ['--after', '999999']).done, 2, 'a later id is refused in the grace');
  await delay(StartupGraceMs + 100);
  assert.strictEqual(await client(port, exiting(0, 8), ['--after', String(failingJob.id)]).done, 4, 'outcome kept across a restart');
  const ended = await request(port, { type: 'await', id: firstJob.id });
  assert.deepStrictEqual([ended?.type, (ended as Incoming | null)?.outcome], ['ended', 'succeeded'], 'await an ended job');

  // A dependent that comes back after a restart before its predecessor, past the startup grace, still waits for it;
  // the predecessor's client, whose command ended while no scheduler listened, reports the outcome afterwards
  const connection = async (message: Record<string, unknown>): Promise<{ socket: net.Socket; messages: Incoming[] }> => {
    const socket = await new Promise<net.Socket>((resolve) => {
      const s = net.connect(port, '127.0.0.1', () => resolve(s));
      s.on('error', () => s.destroy());
    });
    const messages: Incoming[] = [];
    onMessages(socket, (m) => messages.push(m));
    socket.write(`${JSON.stringify({ version: ProtocolVersion, ...message })}\n`);
    return { socket, messages };
  };
  const idOf = async (c: { messages: Incoming[] }): Promise<number> => {
    for (let i = 0; i < 50 && !c.messages.some((m) => m.type === 'queued'); i++) {
      await delay(100);
    }
    return Number(c.messages.find((m) => m.type === 'queued')?.id);
  };
  const holderRaw = await connection(registration('R-holder', 2, 1));
  const predecessor = await connection(registration('R-predecessor', 1, 1));
  const predecessorId = await idOf(predecessor);
  const dependentRequest = { ...registration('R-dependent', 1, 1), after: predecessorId };
  const dependentRaw = await connection(dependentRequest);
  const dependentId = await idOf(dependentRaw);
  await request(port, { type: 'stop' });
  [holderRaw, predecessor, dependentRaw].forEach((c) => c.socket.destroy());
  await delay(300);
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs + 1000);
  const back = await connection({ ...dependentRequest, id: dependentId });
  const backView = await jobWith(port, 'R-dependent');
  assert.deepStrictEqual([backView.id, backView.state, backView.blockedBy], [dependentId, 'waiting', predecessorId], 'waits past the grace');
  const report = await request(port, { type: 'report', id: predecessorId, completed: true, exitCode: 0 });
  assert.strictEqual(report?.type, 'reported');
  await waitFor(port, 'the dependent starts once the outcome is reported', (s) => (s.running.some((j) => j.id === dependentId) ? true : undefined));
  back.socket.destroy();

  // A job held for a lost client (only its connection dropped) ends with its client's report, and its dependent starts
  const command = spawn(process.execPath, ['-e', 'setTimeout(function(){},20000)'], { windowsHide: true, detached: process.platform !== 'win32' });
  const lostRaw = await connection(registration('R-lost', 1, 1));
  const lostId = await idOf(lostRaw);
  lostRaw.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: 'started', commandPid: command.pid ?? 0, spawnedAt: Date.now() })}\n`);
  const afterLost = await connection({ ...registration('R-after-lost', 1, 1), after: lostId });
  await jobWith(port, 'R-after-lost');
  lostRaw.socket.destroy();
  await waitFor(port, 'the client is seen lost', (s) => (s.running.some((j) => j.id === lostId && j.orphan) ? true : undefined));
  await request(port, { type: 'report', id: lostId, completed: true, exitCode: 0 });
  await waitFor(port, 'the reported job ends and its dependent starts', (s) =>
    s.running.some((j) => j.command === 'R-after-lost') && !s.running.some((j) => j.id === lostId) ? true : undefined,
  );
  afterLost.socket.destroy();
  if (process.platform === 'win32') {
    killProcessTree(command.pid);
  } else {
    command.kill('SIGTERM');
  }
  await request(port, { type: 'stop' });

  // A command that ends while its client reconnects to the next scheduler: the client reports the end instead of
  // queueing its job again, and the job after it runs (end-to-end, with real clients)
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs + 100);
  const reconnectGate = path.join(dir, 'gate-10c');
  const ending = client(port, gated(0, reconnectGate));
  const endingJob = await runningWith(port, gated(0, reconnectGate));
  const following = client(port, exiting(0, 11), ['--after', String(endingJob.id)]);
  await jobWith(port, exiting(0, 11));
  await request(port, { type: 'stop' });
  open(reconnectGate); // the clients start the next scheduler themselves; the command ends meanwhile
  assert.deepStrictEqual(await Promise.all([ending.done, following.done]), [0, 0], 'the job after one that ended during a restart runs');
  // and its outcome stays "succeeded": no job is queued again under its id once it ended
  const endingOutcome = await request(port, { type: 'await', id: endingJob.id });
  assert.strictEqual(endingOutcome?.outcome, 'succeeded', 'the outcome of a job that ended during a restart');
  await usedWithin(port, 0, 15000);

  // The same with no other client to start the next scheduler: the client that reports starts it, and a job queued
  // --after it later runs
  const aloneGate = path.join(dir, 'gate-10d');
  const alone = client(port, gated(0, aloneGate));
  const aloneJob = await runningWith(port, gated(0, aloneGate));
  await request(port, { type: 'stop' });
  open(aloneGate);
  assert.strictEqual(await alone.done, 0);
  await waitFor(port, 'the reported job is known as ended', () => true);
  assert.strictEqual(await client(port, exiting(0, 12), ['--after', String(aloneJob.id)]).done, 0, 'the outcome reached the next scheduler');

  // Only the connection of a waiting job drops while its client lives: the job is expected back, not cancelled, and the
  // job that waits for it keeps waiting
  await usedWithin(port, 0, 15000);
  const blocker = await connection(registration('R-blocker', 2, 1));
  await jobWith(port, 'R-blocker');
  const dropped = await connection(registration('R-dropped', 1, process.pid));
  const droppedId = await idOf(dropped);
  const waitingOnDropped = await connection({ ...registration('R-waits-on-dropped', 1, 1), after: droppedId });
  await jobWith(port, 'R-waits-on-dropped');
  dropped.socket.destroy();
  await delay(500);
  const afterDrop = await statusOf(port);
  const stillWaiting = afterDrop.waiting.find((j) => j.command === 'R-waits-on-dropped');
  assert.deepStrictEqual([stillWaiting?.blockedBy, waitingOnDropped.messages.some((m) => m.type === 'cancelled')], [droppedId, false], 'not cancelled');
  const returned = await connection({ ...registration('R-dropped', 1, process.pid), id: droppedId });
  assert.strictEqual(await idOf(returned), droppedId, 'it queues again under its id');
  // A waiting job whose client left on its own (it sent "done": a timeout, Ctrl+C) has ended, although its process
  // may still run for a moment: its dependent is cancelled at once
  const leaving = await connection(registration('R-leaving', 1, process.pid));
  const leavingId = await idOf(leaving);
  const waitingOnLeaving = await connection({ ...registration('R-waits-on-leaving', 1, 1), after: leavingId });
  await jobWith(port, 'R-waits-on-leaving');
  leaving.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: 'done', completed: false, exitCode: 3 })}\n`);
  leaving.socket.end();
  for (let i = 0; i < 50 && !waitingOnLeaving.messages.some((m) => m.type === 'cancelled'); i++) {
    await delay(100);
  }
  assert.ok(waitingOnLeaving.messages.some((m) => m.type === 'cancelled'), 'the job after one that left is cancelled');
  [blocker, waitingOnDropped, returned, waitingOnLeaving].forEach((c) => c.socket.destroy());
  await request(port, { type: 'stop' });

  // A "done" gets a receipt once the outcome is saved; a stopping scheduler saves nothing and gives none, so that the
  // client reports the outcome to the next one
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const receipt = async (stopFirst: boolean): Promise<boolean> => {
    const c = await connection(registration(`R-receipt-${stopFirst}`, 1, 1));
    for (let i = 0; i < 50 && !c.messages.some((m) => m.type === 'granted'); i++) {
      await delay(100);
    }
    if (stopFirst) {
      await request(port, { type: 'stop' }).catch(() => null);
    }
    c.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: 'done', completed: true, exitCode: 0 })}\n`);
    await delay(500);
    c.socket.destroy();
    return c.messages.some((m) => m.type === 'done-ok');
  };
  assert.strictEqual(await receipt(false), true, 'a done gets its receipt');
  assert.strictEqual(await receipt(true), false, 'a stopping scheduler gives no receipt');
  await delay(300);

  // A cancel decides the outcome where the command did not succeed, also when the scheduler restarted in between (the
  // client says so in "done"); a command that succeeded before the cancel reached it stays succeeded
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const outcomeOf = async (id: number): Promise<unknown> => (await request(port, { type: 'await', id }))?.outcome;
  const crossing = await connection(registration('R-crossing', 1, process.pid));
  const crossingId = await idOf(crossing);
  await request(port, { type: 'cancel', id: crossingId });
  crossing.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: 'done', completed: true, exitCode: 0 })}\n`);
  await delay(300);
  assert.strictEqual(await outcomeOf(crossingId), 'succeeded', 'a success that crosses the cancel wins');
  crossing.socket.destroy();
  // The same through a report, while the old connection is still open here
  const reporting = await connection(registration('R-crossing-report', 1, process.pid));
  const reportingId = await idOf(reporting);
  await request(port, { type: 'cancel', id: reportingId });
  await request(port, { type: 'report', id: reportingId, completed: true, exitCode: 0 });
  reporting.socket.destroy();
  await delay(300);
  assert.strictEqual(await outcomeOf(reportingId), 'succeeded', 'a reported success that crosses the cancel wins');
  const restarted = await connection(registration('R-cancel-restart', 1, process.pid));
  const restartedId = await idOf(restarted);
  await request(port, { type: 'stop' });
  restarted.socket.destroy();
  await delay(300);
  servers.push(await startTestScheduler(port, 2, env));
  const again = await connection({ ...registration('R-cancel-restart', 1, process.pid), type: 'adopt', id: restartedId, spawnedAt: Date.now() });
  await delay(300);
  again.socket.write(`${JSON.stringify({ version: ProtocolVersion, type: 'done', completed: false, exitCode: 4, cancelled: true })}\n`);
  await delay(StartupGraceMs + 300);
  assert.strictEqual(await outcomeOf(restartedId), 'cancelled', 'a cancel carried across a restart is recorded as cancelled');
  again.socket.destroy();
  await request(port, { type: 'stop' });
  await delay(300);

  // In the startup grace of a restart, a failed predecessor reports before its dependents queue again: when the grace
  // ends, every dependent is cancelled and none is granted (a cancel there used to grant the next ones)
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const failedEarly = await connection(registration('R-failed-early', 1, 1));
  const failedEarlyId = await idOf(failedEarly);
  await request(port, { type: 'stop' });
  failedEarly.socket.destroy();
  await delay(300);
  servers.push(await startTestScheduler(port, 2, env));
  await request(port, { type: 'report', id: failedEarlyId, completed: true, exitCode: 1 });
  const dependents = await Promise.all(
    [1, 2, 3].map((n) => connection({ ...registration(`R-after-failed-${n}`, 1, 1), after: failedEarlyId })),
  );
  await delay(StartupGraceMs + 500);
  for (const dependent of dependents) {
    const types = dependent.messages.map((m) => m.type);
    assert.ok(types.includes('cancelled') && !types.includes('granted'), `cancelled, never granted: ${types.join(' ')}`);
    dependent.socket.destroy();
  }
  await request(port, { type: 'stop' });
}

async function checkGpuTurnsEndToEnd(
  port: number,
  env: NodeJS.ProcessEnv,
  client: ClientFactory,
  commands: { gated: (code: number, file: string) => string; open: (file: string) => void; dir: string },
  servers: ChildProcess[],
  ports: number[],
): Promise<void> {
  const { gated, open, dir } = commands;
  ports.push(port);
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const gate = (name: string): string => path.join(dir, `gate-11${name}`);
  const turnOf = (command: string) =>
    waitFor(port, `the GPU turn waits: ${command}`, (s) => s.waiting.find((j) => j.command === command && j.gpuTurn));
  // The first --gpu job takes the GPU: its turn runs, and the command under the lock registers as a GPU holder
  const first = client(port, gated(0, gate('a')), ['--gpu']);
  await waitFor(port, 'the first job holds the GPU', (s) =>
    s.running.some((j) => j.command === gated(0, gate('a')) && j.gpu) ? true : undefined,
  );
  // Later --gpu jobs wait in the scheduler, in arrival order
  const second = client(port, gated(0, gate('b')), ['--gpu']);
  const secondTurn = await turnOf(gated(0, gate('b')));
  const third = client(port, gated(0, gate('c')), ['--gpu']);
  const thirdTurn = await turnOf(gated(0, gate('c')));
  assert.strictEqual(secondTurn.weight, 0, 'a waiting turn takes no slots');
  // A job waiting for the GPU can be cancelled (it is known to the scheduler before it holds the lock)
  const cancelled = await request(port, { type: 'cancel', id: thirdTurn.id });
  assert.deepStrictEqual(cancelled?.cancelled, [thirdTurn.id]);
  assert.strictEqual(await third.done, 4, 'a cancelled waiting --gpu job exits with 4');
  // A later job does not pass the one that waited longer
  const fourth = client(port, gated(0, gate('d')), ['--gpu']);
  await turnOf(gated(0, gate('d')));
  open(gate('a'));
  assert.strictEqual(await first.done, 0);
  await waitFor(port, 'the second job holds the GPU next', (s) =>
    s.running.some((j) => j.command === gated(0, gate('b')) && j.gpu) ? true : undefined,
  );
  assert.ok((await statusOf(port)).waiting.some((j) => j.command === gated(0, gate('d'))), 'the later job still waits');
  // A cancel of the running turn stops the command under the lock, and gpu-lock.js releases the lock
  await request(port, { type: 'cancel', id: secondTurn.id });
  assert.strictEqual(await second.done, 4, 'a cancelled running --gpu job exits with 4');
  open(gate('d'));
  assert.strictEqual(await fourth.done, 0);
  assert.strictEqual(fs.existsSync(path.join(dir, 'gpu-lock')), false, 'no GPU lock is left behind');
  // One run time per --gpu run: the command's, not also the turn's (which includes the wait for the lock)
  const history = JSON.parse(fs.readFileSync(path.join(dir, 'history.json'), 'utf8')) as { commands: Record<string, { runs: number[] }> };
  assert.strictEqual(history.commands[RunHistory.key(gated(0, gate('a')))]?.runs.length, 1, 'one run recorded per --gpu run');
  await usedWithin(port, 0, 15000);
  await request(port, { type: 'stop' });
}

async function checkWait(
  port: number,
  env: NodeJS.ProcessEnv,
  client: ClientFactory,
  commands: {
    gated: (code: number, file: string) => string;
    open: (file: string) => void;
    exiting: (code: number, ms?: number) => string;
    dir: string;
  },
  servers: ChildProcess[],
  ports: number[],
): Promise<void> {
  const { gated, open, exiting, dir } = commands;
  ports.push(port);
  servers.push(await startTestScheduler(port, 2, env));
  await delay(StartupGraceMs);
  const waiter = (id: number): Client & { output: () => string } => {
    let output = '';
    const child = spawn(process.execPath, [EntryScript, 'wait', String(id), '--json', '--port', String(port), '--slots', '2'], {
      stdio: ['ignore', 'pipe', 'ignore'],
      windowsHide: true,
      env,
    }) as Client & { output: () => string };
    child.stdout?.on('data', (data: Buffer) => (output += data.toString()));
    child.done = new Promise((resolve) => child.on('close', resolve));
    child.output = () => output;
    return child;
  };
  // A running job: wait returns when it ends, also when the scheduler restarted in between
  const gate = path.join(dir, 'gate-12');
  const running = client(port, gated(0, gate));
  const runningJob = await runningWith(port, gated(0, gate));
  const waiting = waiter(runningJob.id);
  await delay(1500);
  assert.strictEqual(waiting.exitCode, null, 'wait blocks while the job runs');
  await request(port, { type: 'stop' });
  await delay(500);
  open(gate);
  assert.strictEqual(await running.done, 0);
  assert.strictEqual(await waiting.done, 0, 'wait exits 0 for a job that succeeded, across a restart');
  assert.deepStrictEqual(JSON.parse(waiting.output()), { id: runningJob.id, outcome: 'succeeded' });
  // A waiting job: a stop is not its end; wait returns when it has run after the restart
  const holdGate = path.join(dir, 'gate-12b');
  const queuedGate = path.join(dir, 'gate-12c');
  const holder = client(port, gated(0, holdGate), ['--weight', '2']);
  await runningWith(port, gated(0, holdGate));
  const queued = client(port, gated(0, queuedGate));
  const queuedJob = await jobWith(port, gated(0, queuedGate));
  const waitingOnQueued = waiter(queuedJob.id);
  await delay(1000);
  await request(port, { type: 'stop' });
  await delay(1500);
  assert.strictEqual(waitingOnQueued.exitCode, null, 'a stop does not end a waiting job for wait');
  open(holdGate);
  open(queuedGate);
  assert.deepStrictEqual(await Promise.all([holder.done, queued.done, waitingOnQueued.done]), [0, 0, 0]);
  // A running job cancelled: its client stops the command and reports the end; wait tells "cancelled", not a failure
  const cancelGate = path.join(dir, 'gate-12d');
  const toCancel = client(port, gated(0, cancelGate));
  const toCancelJob = await runningWith(port, gated(0, cancelGate));
  const waitingOnCancel = waiter(toCancelJob.id);
  await delay(500);
  await request(port, { type: 'cancel', id: toCancelJob.id });
  assert.deepStrictEqual(await Promise.all([toCancel.done, waitingOnCancel.done]), [4, 4], 'wait exits 4 for a cancelled job');
  // An ended job answers from the record; an unknown id is a usage error
  const failing = client(port, exiting(3, 200));
  const failingJob = await jobWith(port, exiting(3, 200));
  assert.strictEqual(await failing.done, 3);
  assert.strictEqual(await waiter(failingJob.id).done, 1, 'wait exits 1 for a job that failed');
  assert.strictEqual(await waiter(999999).done, 2, 'wait exits 2 for an unknown id');
  await request(port, { type: 'stop' });
}
