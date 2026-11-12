#!/usr/bin/env node
'use strict';

// Queues builds and test runs on this machine, so that agents and worktrees running side by side do not oversubscribe
// the CPU and memory.
//
// A scheduler listens on 127.0.0.1 (port 47117, GSHARP_BUILD_QUEUE_PORT) and grants slots of a CPU pool (6 by default,
// GSHARP_BUILD_QUEUE_SLOTS). `run` asks for a slot, waits, runs the command itself in the caller's directory
// (incremental builds keep working), and gives the slot back when the command ends. The scheduler starts on the first
// `run`, in <main checkout>/tmp/build-queue (so that it never holds a worktree open), and exits after 30 minutes
// without jobs.
//
// Order (--policy, GSHARP_BUILD_QUEUE_POLICY): the scheduler records how long each command ran (history.json in the
// state directory, the last 9 runs per command text). With `hrrn` (the default) the waiting jobs are ordered by
// response ratio, (waited + typical run time) / typical run time: a short job goes first, and a long one rises the
// longer it waits, so that none starves. A later job may start in free slots when it does not delay the first job
// (backfilling): it ends before the first job could start, counting every running job at its longest recorded time,
// or it fits in the slots the first job leaves free. `backfill` keeps arrival order with backfilling; `fifo` keeps
// arrival order without it.
//
// Job ids: `run` prints `queued #<id>` as soon as the scheduler took the job, with the jobs ahead of it and its
// expected start and end; `status <id> [--json]` tells them again at any time. The expected times play the grants
// forward with each command's typical (median) run time. Ids count up and are never reused while the state directory
// lives (the next id is saved in jobs-<port>.json and history.json).
//
// Slots follow the command, not only the connection:
//   - The client reports the process id of the command it started. When the client dies but the command still runs
//     (a tool timeout that kills only the client), the scheduler keeps the slot until no process of the command is
//     left (on Windows: the shell or a child of it, matched by creation time, since the shell dies with the client).
//   - A cancelled running job keeps its slot until its client has stopped the command and closed.
//   - When the scheduler dies, a running client reconnects and re-registers its job, and a waiting client queues
//     again with its original arrival time, both with their job id, so that ids stay the same across a restart. For
//     the first seconds after it starts, the scheduler accepts those re-registrations and grants nothing, so that
//     waiting clients do not take the slots of running jobs that have not re-registered yet. Jobs whose client was
//     already gone are restored from jobs-<port>.json, which also keeps the next job id.
//
// Weight: a test run (dotnet test, just test*, the long test scripts) weighs 2 slots, anything else 1; --weight
// overrides it. A granted job gets cores / slots * weight MSBuild nodes: `-m:<n>` is added to each `dotnet build`,
// `test`, `msbuild`, `publish` and `pack` of the command that has no -m of its own, and GSHARP_BUILD_QUEUE_MSBUILD_ARGS
// carries it into the justfile recipes. MSBuild ignores the processor count variables and the affinity of its
// process; only the command line limits its nodes.
//
// GPU: --gpu takes the GPU lock first (scripts/agent/gpu-lock.js, which also reads the kernel pools), then asks for its
// CPU slot, which is granted at once, even over the capacity. Only one job holds the GPU lock, so the pool is exceeded
// by one job at most, and a job that holds the GPU lock never waits for a CPU slot: a job that holds a CPU slot and
// waits for the GPU lock (a nested `run --gpu`, or a script that takes the lock itself, such as long-checks.js or the
// fuzzer) cannot deadlock with it.
//
// Shell: the command runs in the caller's shell, so that it is written as for the tool that runs `run`: Git Bash on
// Windows when MSYSTEM or SHELL is set (the Claude Code Bash tool), PowerShell otherwise on Windows (the Claude Code
// PowerShell tool, Antigravity), /bin/sh elsewhere; --shell overrides it. PowerShell passes the exit code of the last
// native program on.
//
// A command run by a queued job (GSHARP_BUILD_QUEUE_JOB is set) that calls `run` again runs at once, without a second
// slot, with the same MSBuild node limit.
//
// Usage:
//   node scripts/agent/build-queue.js run [--weight <n>] [--gpu] [--shell <name>] [--timeout-minutes <n>] -- "<command>"
//   node scripts/agent/build-queue.js status [<id>] [--json]
//   node scripts/agent/build-queue.js cancel <id>
//   node scripts/agent/build-queue.js cancel-all
//   node scripts/agent/build-queue.js stop
//   node scripts/agent/build-queue.js serve [--slots <n>] [--policy hrrn|backfill|fifo]
//   node scripts/agent/build-queue.js --self-test
//
// Exit codes (run): the exit code of the command; 2 on a usage error; 3 when the wait timed out; 4 when the job was
// cancelled; 130 / 143 on SIGINT / SIGTERM.

const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const ProtocolVersion = 2;
const DefaultPort = 47117;
const DefaultSlots = 6; // measured on 24 cores / 64 GB (test-execution.md, Build queue)
const IdleExitMs = 30 * 60 * 1000;
const NoticeIntervalMs = 5 * 60 * 1000;
const StartupGraceMs = 1500;
const OrphanPollMs = 2000;
const LogRotateBytes = 5 * 1024 * 1024;
const CancelledExitCode = 4;
const Policies = ['fifo', 'backfill', 'hrrn'];
const DefaultPolicy = 'hrrn';
const UnknownTypicalMs = 60 * 1000; // the typical run time of a command with no recorded run, for its response ratio
const HistoryRunsPerCommand = 9;
const HistoryCommands = 500;
const RegrantMs = 5 * 1000; // how often the scheduler re-orders and grants while jobs wait
const ForecastNoticeMs = 60 * 1000; // a waiting client is told again when its expected start moved this much
const OverrunGuessMs = 30 * 1000; // forecast: the rest of a running job that has run longer than any recorded run
const LongestRunMs = 7 * 24 * 60 * 60 * 1000; // run times and starts beyond this are taken as damaged input
const ForecastSteps = 2000; // the forecast replays at most this many steps; jobs it does not reach get no times

const JobVariable = 'GSHARP_BUILD_QUEUE_JOB';
const InGpuLockVariable = 'GSHARP_BUILD_QUEUE_IN_GPU_LOCK';
const CommandVariable = 'GSHARP_BUILD_QUEUE_COMMAND';
const MsbuildArgsVariable = 'GSHARP_BUILD_QUEUE_MSBUILD_ARGS';
const CpusVariable = 'GSHARP_BUILD_QUEUE_CPUS';
const StateDirectoryVariable = 'GSHARP_BUILD_QUEUE_STATE_DIR';
const ShellVariable = 'GSHARP_BUILD_QUEUE_SHELL';
const PolicyVariable = 'GSHARP_BUILD_QUEUE_POLICY';

const usage = [
  'Usage:',
  '  node scripts/agent/build-queue.js run [--weight <n>] [--gpu] [--shell <name>] [--timeout-minutes <n>] -- "<command>"',
  '  node scripts/agent/build-queue.js status [<id>] [--json]',
  '  node scripts/agent/build-queue.js cancel <id>',
  '  node scripts/agent/build-queue.js cancel-all',
  '  node scripts/agent/build-queue.js stop',
  '  node scripts/agent/build-queue.js serve [--slots <n>] [--policy hrrn|backfill|fifo]',
  '  node scripts/agent/build-queue.js --self-test',
  '',
  'run         Waits for a slot of the build queue, runs the command in this directory, gives the slot back.',
  '            --weight <n>           Slots the job takes (default 2 for test runs, 1 otherwise).',
  '            --gpu                  Take the GPU lock (gpu-lock.js) first; the slot is then granted at once.',
  '            --shell <name>         bash, pwsh, powershell, cmd or sh (default: the caller\'s: Git Bash when MSYSTEM',
  '                                   or SHELL is set, else PowerShell on Windows, /bin/sh elsewhere).',
  '            --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
  'status      Lists the running and waiting jobs with their ids, the jobs ahead and the expected start and end;',
  '            with <id>, only that job (exit 1 when it ended or is unknown); --json prints the same as JSON.',
  'cancel      Cancels one job: a waiting job leaves the queue, a running job is stopped. Its run exits with 4.',
  'cancel-all  Cancels every job and empties the queue.',
  'stop        Stops the scheduler; running jobs keep running and register with the next one.',
  'serve       Runs the scheduler in the foreground (run starts it on demand).',
  '',
  `Environment: GSHARP_BUILD_QUEUE_PORT (default ${DefaultPort}), GSHARP_BUILD_QUEUE_SLOTS (default ${DefaultSlots}),`,
  `GSHARP_BUILD_QUEUE_POLICY (default ${DefaultPolicy}; the scheduler keeps the policy it started with).`,
  'Quote the command as one string when it needs pipes or redirection.',
].join('\n');

function usageError(message) {
  process.stderr.write(`${message}\n${usage}\n`);
  process.exit(2);
}

function parsePositive(name, text, allowZero) {
  const value = Number(text);
  if (text === undefined || !Number.isInteger(value) || value < 0 || (!allowZero && value === 0)) {
    usageError(`${name} needs a ${allowZero ? 'non-negative' : 'positive'} integer.`);
  }
  return value;
}

const Modes = ['run', 'status', 'cancel', 'cancel-all', 'stop', 'serve'];

function parseArgs(argv) {
  const separator = argv.indexOf('--');
  const own = separator < 0 ? argv : argv.slice(0, separator);
  const command = separator < 0 ? [] : argv.slice(separator + 1);
  const options = {
    mode: '',
    weight: 0,
    gpu: false,
    timeoutMinutes: 0,
    slots: Number(process.env.GSHARP_BUILD_QUEUE_SLOTS) || DefaultSlots,
    policy: Policies.includes(process.env[PolicyVariable]) ? process.env[PolicyVariable] : DefaultPolicy,
    port: Number(process.env.GSHARP_BUILD_QUEUE_PORT) || DefaultPort,
    commandFromEnv: false,
    shell: '',
    stateDir: '',
    cancelId: 0,
    statusId: 0,
    json: false,
    command: command.join(' '),
    // Several words after `--` are argv that the calling shell has already split: re-quote them for the shell that
    // runs them (see run), instead of joining them into a different command.
    commandWords: command.length > 1 ? command : null,
  };
  for (let i = 0; i < own.length; i++) {
    const arg = own[i];
    if (Modes.includes(arg)) {
      if (options.mode !== '') {
        usageError(`Unexpected ${arg}.`);
      }
      options.mode = arg;
      if (arg === 'cancel') {
        options.cancelId = parsePositive('cancel', own[++i], false);
      }
      continue;
    }
    if (options.mode === 'status' && options.statusId === 0 && /^\d+$/.test(arg)) {
      options.statusId = parsePositive('status', arg, false); // status <id>, before or after the options
      continue;
    }
    switch (arg) {
      case '--help':
      case '-h':
        process.stdout.write(`${usage}\n`);
        process.exit(0);
        break;
      case '--self-test':
        options.mode = 'self-test';
        break;
      case '--weight':
        options.weight = parsePositive('--weight', own[++i], false);
        break;
      case '--gpu':
        options.gpu = true;
        break;
      case '--json':
        options.json = true;
        break;
      case '--timeout-minutes':
        options.timeoutMinutes = parsePositive('--timeout-minutes', own[++i], true);
        break;
      case '--slots':
        options.slots = parsePositive('--slots', own[++i], false);
        break;
      case '--policy':
        options.policy = own[++i] || '';
        if (!Policies.includes(options.policy)) {
          usageError(`--policy needs ${Policies.join(', ')}.`);
        }
        break;
      case '--port': // for the self-test
        options.port = parsePositive('--port', own[++i], false);
        break;
      case '--state-dir': // serve: a scheduler started through WMI does not inherit GSHARP_BUILD_QUEUE_STATE_DIR
        options.stateDir = own[++i] || '';
        break;
      case '--command-from-env': // set by the GPU wrapper, which cannot quote the command for the shell
        options.commandFromEnv = true;
        break;
      case '--shell':
        options.shell = own[++i] || '';
        if (!['bash', 'pwsh', 'powershell', 'cmd', 'sh'].includes(options.shell)) {
          usageError('--shell needs bash, pwsh, powershell, cmd or sh.');
        }
        break;
      default:
        usageError(`Unknown argument: ${arg}`);
    }
  }
  if (options.mode === '') {
    usageError(`Give a mode: ${Modes.join(', ')}.`);
  }
  if (options.commandFromEnv) {
    options.command = process.env[CommandVariable] || '';
  }
  if (options.mode === 'run' && options.command.trim() === '') {
    usageError('No command given. Put it after "--".');
  }
  return options;
}

/**
 * The main checkout's tmp/build-queue, whichever worktree runs the script (as gpu-lock.js finds its lock);
 * GSHARP_BUILD_QUEUE_STATE_DIR overrides it (self-test).
 */
function resolveStateDirectory() {
  if (process.env[StateDirectoryVariable]) {
    return path.resolve(process.env[StateDirectoryVariable]);
  }
  let base = path.resolve(__dirname, '..', '..');
  const git = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
    cwd: __dirname,
    encoding: 'utf8',
    windowsHide: true,
  });
  if (!git.error && git.status === 0) {
    const commonDir = git.stdout.trim();
    if (commonDir !== '' && path.isAbsolute(commonDir)) {
      base = path.dirname(path.resolve(commonDir));
    }
  }
  return path.join(base, 'tmp', 'build-queue');
}

/** Splits a socket stream into JSON messages, one per line. */
function onMessages(socket, handler) {
  let buffer = '';
  socket.setEncoding('utf8');
  socket.on('data', (chunk) => {
    buffer += chunk;
    let newline = buffer.indexOf('\n');
    while (newline >= 0) {
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + 1);
      newline = buffer.indexOf('\n');
      if (line.trim() === '') {
        continue;
      }
      let message;
      try {
        message = JSON.parse(line);
      } catch (error) {
        socket.destroy(new Error(`invalid message: ${error.message}`));
        return;
      }
      handler(message);
    }
  });
}

function send(socket, message) {
  if (socket !== null && !socket.destroyed) {
    socket.write(`${JSON.stringify(message)}\n`);
  }
}

/** Slack for comparing a process creation time with the recorded spawn time (as in gpu-lock.js). */
const SpawnToleranceMs = 5000;

/**
 * Windows: reads every process as pid -> { ppid, created } through PowerShell, without blocking the scheduler.
 * Resolves to null when the query fails or looks incomplete; callers then treat commands as alive.
 */
function queryWindowsProcesses() {
  const script =
    'Get-CimInstance Win32_Process | ForEach-Object { $c = -1; ' +
    'if ($_.CreationDate) { $c = ([DateTimeOffset]$_.CreationDate).ToUnixTimeMilliseconds() }; ' +
    '"$($_.ProcessId) $($_.ParentProcessId) $c" }';
  return new Promise((resolve) => {
    let output = '';
    const query = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { windowsHide: true });
    const timer = setTimeout(() => query.kill(), 60000);
    query.stdout.setEncoding('utf8');
    query.stdout.on('data', (chunk) => (output += chunk));
    query.on('error', () => resolve(null));
    query.on('close', (code) => {
      clearTimeout(timer);
      if (code !== 0) {
        resolve(null);
        return;
      }
      const processes = new Map();
      for (const line of output.split(/\r?\n/)) {
        const [pid, ppid, created] = line.trim().split(' ').map(Number);
        if (Number.isInteger(pid) && Number.isInteger(ppid) && Number.isFinite(created)) {
          processes.set(pid, { ppid, created });
        }
      }
      resolve(processes.has(process.pid) ? processes : null);
    });
  });
}

/**
 * The live processes that belong to a command started as `pid` at `spawnedAt`: the shell itself and its direct
 * children (Win32_Process keeps a dead parent's pid in ParentProcessId; when the client dies, Windows ends the shell
 * but not what it started). Creation times exclude processes that only reuse the pid. Null when unsure.
 */
function windowsCommandProcesses(processes, pid, spawnedAt) {
  if (processes === null) {
    return null;
  }
  const earliest = spawnedAt - SpawnToleranceMs;
  const latest = spawnedAt + SpawnToleranceMs;
  const found = [];
  let reusedFrom = Infinity;
  const shell = processes.get(pid);
  if (shell !== undefined) {
    if (shell.created < 0 || shell.created <= latest) {
      found.push(pid);
    } else {
      reusedFrom = shell.created;
    }
  }
  for (const [child, info] of processes) {
    if (child !== pid && info.ppid === pid && (info.created < 0 || (info.created >= earliest && info.created < reusedFrom))) {
      found.push(child);
    }
  }
  return found;
}

/** True unless the client process is known to be gone. EPERM means alive. */
function isClientAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error.code === 'EPERM';
  }
}

/** Unix: true unless the command's process group is known to be gone. EPERM means alive. */
function isProcessGroupAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }
  try {
    process.kill(-pid, 0);
    return true;
  } catch (error) {
    return error.code === 'EPERM';
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheduler

/**
 * The scheduler state, without sockets, so that the order of grants can be reasoned about apart from the network.
 * A job that holds the GPU lock is granted at once. The other waiting jobs are ordered by the policy, and the first one
 * is granted while it fits:
 *   - fifo: arrival order; a later job never overtakes the first one.
 *   - backfill: arrival order, and a later job may start in the free slots when it does not delay the first one
 *     (EASY backfilling): it ends before the first job could start, or it fits in the slots the first job leaves free.
 *   - hrrn: the same backfilling, with the jobs ordered by response ratio (waited time + typical run time) / typical
 *     run time, highest first: a short job goes first, and a long one rises the longer it waits, so that none starves.
 * The run times come from `job.estimate` ({ typicalMs, longestMs } of the command's earlier runs, or null). Backfilling
 * reserves the start of the first job as if every running job took its longest recorded time, and backfills nothing
 * while a running job has no recorded time or has run longer than all of them. Nothing is granted before `holdUntil`
 * (re-registration after a restart).
 */
class Scheduler {
  constructor(capacity, holdUntil = 0, policy = DefaultPolicy) {
    this.capacity = capacity;
    this.holdUntil = holdUntil;
    this.policy = policy;
    this.used = 0;
    this.running = [];
    this.waiting = [];
  }

  clampWeight(weight) {
    return Math.min(Math.max(1, Math.trunc(weight) || 1), this.capacity);
  }

  /** Adds a waiting job and returns the jobs granted by it. */
  enqueue(job, now = Date.now()) {
    job.weight = this.clampWeight(job.weight);
    this.waiting.push(job);
    return this.grant(now);
  }

  /** Registers a job that already runs (its client reconnected after the scheduler restarted). */
  adopt(job, now = Date.now()) {
    job.weight = this.clampWeight(job.weight);
    // The start the client reported, unless it is missing, in the future or older than any run can be
    job.grantedAt = job.grantedAt > now - LongestRunMs && job.grantedAt <= now ? job.grantedAt : now;
    job.adopted = true; // its run started before this scheduler: no run time is recorded for it
    this.running.push(job);
    this.used += job.weight;
  }

  /** Removes a job, waiting or running, and returns the jobs granted by the freed slots. */
  remove(job, now = Date.now()) {
    const running = this.running.indexOf(job);
    if (running >= 0) {
      this.running.splice(running, 1);
      this.used -= job.weight;
    }
    const waiting = this.waiting.indexOf(job);
    if (waiting >= 0) {
      this.waiting.splice(waiting, 1);
    }
    return this.grant(now);
  }

  /** Sorts the waiting jobs into the order of the policy (also the positions the clients are told). */
  order(now = Date.now()) {
    const arrival = (a, b) => a.queuedAt - b.queuedAt || a.id - b.id;
    if (this.policy !== 'hrrn') {
      this.waiting.sort(arrival);
      return;
    }
    const ratio = (job) => {
      const typical = job.estimate ? Math.max(job.estimate.typicalMs, 1000) : UnknownTypicalMs;
      return (Math.max(0, now - job.queuedAt) + typical) / typical;
    };
    const ratios = new Map(this.waiting.map((job) => [job, ratio(job)]));
    this.waiting.sort((a, b) => ratios.get(b) - ratios.get(a) || arrival(a, b));
  }

  /** Grants GPU holders at once, then the first waiting job while it fits, then backfills (unless fifo). */
  grant(now = Date.now()) {
    const granted = [];
    if (now < this.holdUntil) {
      this.order(now); // the positions the clients are told
      return granted;
    }
    const take = (job) => {
      this.waiting.splice(this.waiting.indexOf(job), 1);
      job.grantedAt = now;
      this.running.push(job);
      this.used += job.weight;
      granted.push(job);
    };
    this.waiting.filter((job) => job.gpu).forEach(take);
    this.order(now);
    while (this.waiting.length > 0 && this.used + this.waiting[0].weight <= this.capacity) {
      take(this.waiting[0]);
    }
    if (this.policy !== 'fifo' && this.waiting.length > 1) {
      this.backfill(now).forEach((job) => {
        job.backfilled = true;
        take(job);
      });
    }
    return granted;
  }

  /** The waiting jobs after the first that may start now without delaying the first one's reserved start. */
  backfill(now) {
    const first = this.waiting[0];
    // When the running jobs end, at the latest; unknown when one has no recorded time or ran past all of them
    const ends = [];
    for (const job of this.running) {
      const end = job.estimate ? job.grantedAt + job.estimate.longestMs : NaN;
      if (!(end > now)) {
        return [];
      }
      ends.push({ end, weight: job.weight });
    }
    ends.sort((a, b) => a.end - b.end);
    // The reserved start: the first time enough slots are free for the first job, and what it leaves free then
    let free = this.capacity - this.used;
    let reserved = now;
    for (const { end, weight } of ends) {
      if (free >= first.weight) {
        break;
      }
      free += weight;
      reserved = end;
    }
    // Once every running job has ended, all slots are free: the first job (weight clamped to the capacity) fits
    let spare = free - first.weight;
    let idle = this.capacity - this.used;
    const chosen = [];
    for (const job of this.waiting.slice(1)) {
      if (job.gpu || job.weight > idle) {
        continue;
      }
      const endsInTime = job.estimate !== null && job.estimate !== undefined && now + job.estimate.longestMs <= reserved;
      if (endsInTime || job.weight <= spare) {
        if (!endsInTime) {
          spare -= job.weight;
        }
        idle -= job.weight;
        chosen.push(job);
      }
    }
    return chosen;
  }

  /**
   * When each job is expected to start and end ({ startAt, endAt } per job): the grants of this scheduler played
   * forward on copies of the jobs, each taking its typical run time (UnknownTypicalMs without a record). A running job
   * past its typical time is expected to end at its longest recorded time, and past that, OverrunGuessMs from now.
   */
  forecast(now = Date.now()) {
    const duration = (job) => (job.estimate ? job.estimate.typicalMs : UnknownTypicalMs);
    const simulation = new Scheduler(this.capacity, 0, this.policy);
    const originals = new Map();
    const ends = new Map();
    const result = new Map();
    for (const job of this.running) {
      const copy = { ...job };
      let endAt = job.grantedAt + duration(job);
      if (endAt <= now && job.estimate) {
        endAt = job.grantedAt + job.estimate.longestMs;
      }
      if (endAt <= now) {
        endAt = now + OverrunGuessMs;
      }
      simulation.running.push(copy);
      simulation.used += copy.weight;
      ends.set(copy, endAt);
      result.set(job, { startAt: job.grantedAt, endAt });
    }
    for (const job of this.waiting) {
      const copy = { ...job };
      originals.set(copy, job);
      simulation.waiting.push(copy);
    }
    let time = now;
    for (let step = 0; step < ForecastSteps && simulation.waiting.length > 0; step++) {
      for (const copy of simulation.grant(time)) {
        ends.set(copy, time + duration(copy));
        result.set(originals.get(copy), { startAt: time, endAt: time + duration(copy) });
      }
      if (simulation.waiting.length === 0 || simulation.running.length === 0) {
        break;
      }
      // The next end, or with free slots under hrrn the next re-grant (RegrantMs; a minute here, a coarser step that
      // keeps the replay short), when a waiting job may have overtaken the first one
      const regrant = this.policy === 'hrrn' && simulation.used < this.capacity ? time + 60 * 1000 : Infinity;
      time = Math.max(time, Math.min(regrant, ...simulation.running.map((copy) => ends.get(copy))));
      for (const copy of simulation.running.filter((other) => ends.get(other) <= time)) {
        simulation.running.splice(simulation.running.indexOf(copy), 1);
        simulation.used -= copy.weight;
      }
    }
    return result;
  }

  isIdle() {
    return this.running.length === 0 && this.waiting.length === 0;
  }
}

/**
 * The run times of earlier commands, by command text, in <state dir>/history.json: the last runs of each command that
 * ran to the end (not cancelled, not lost), so that the scheduler can order and backfill by them.
 */
class RunHistory {
  constructor(file, log) {
    this.file = file;
    this.log = log;
    this.commands = Object.create(null); // keys are command texts: "constructor" or "__proto__" must stay plain keys
    this.nextId = 1; // a second copy of the next job id, for when jobs-<port>.json is lost
    try {
      // Keep only well-formed entries, so that a damaged file costs its records, never the scheduler
      const saved = JSON.parse(fs.readFileSync(file, 'utf8'));
      const commands = saved !== null && typeof saved === 'object' ? saved.commands : null;
      if (commands !== null && typeof commands === 'object') {
        for (const [key, entry] of Object.entries(commands)) {
          const runs = entry !== null && typeof entry === 'object' && Array.isArray(entry.runs) ? entry.runs : [];
          const valid = runs.filter((ms) => Number.isFinite(ms) && ms >= 0 && ms <= LongestRunMs).slice(-HistoryRunsPerCommand);
          if (valid.length > 0) {
            this.commands[key] = { runs: valid, lastUsed: Number.isFinite(entry.lastUsed) ? entry.lastUsed : 0 };
          }
        }
      }
      if (Number.isInteger(saved && saved.nextId) && saved.nextId > 0) {
        this.nextId = saved.nextId;
      }
    } catch (error) {
      if (error.code !== 'ENOENT') {
        log(`could not read ${file}: ${error.message}`);
      }
    }
  }

  static key(command) {
    return command.trim().replace(/\s+/g, ' ');
  }

  /** { typicalMs (median), longestMs } of the command's recorded runs, or null when it has none. */
  estimate(command) {
    const entry = this.commands[RunHistory.key(command)];
    if (!entry || !Array.isArray(entry.runs) || entry.runs.length === 0) {
      return null;
    }
    const runs = entry.runs.filter((ms) => Number.isFinite(ms) && ms >= 0).sort((a, b) => a - b);
    if (runs.length === 0) {
      return null;
    }
    const middle = runs.length >> 1;
    const typicalMs = runs.length % 2 === 1 ? runs[middle] : (runs[middle - 1] + runs[middle]) / 2;
    return { typicalMs, longestMs: runs[runs.length - 1] };
  }

  record(command, ms, nextId = this.nextId, now = Date.now()) {
    this.nextId = Math.max(this.nextId, nextId);
    const key = RunHistory.key(command);
    const entry = this.commands[key] || { runs: [], lastUsed: 0 };
    entry.runs = entry.runs.concat(Math.min(Math.round(ms), LongestRunMs)).slice(-HistoryRunsPerCommand);
    entry.lastUsed = now;
    this.commands[key] = entry;
    const keys = Object.keys(this.commands);
    if (keys.length > HistoryCommands) {
      keys
        .sort((a, b) => this.commands[a].lastUsed - this.commands[b].lastUsed)
        .slice(0, keys.length - HistoryCommands)
        .forEach((old) => delete this.commands[old]);
    }
    const temporary = `${this.file}.${process.pid}.tmp`;
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      fs.writeFileSync(temporary, JSON.stringify({ nextId: this.nextId, commands: this.commands }));
      // Windows refuses to replace a file that another process has open (a reader, a virus scanner): retry briefly
      for (let attempt = 1; ; attempt++) {
        try {
          fs.renameSync(temporary, this.file);
          break;
        } catch (error) {
          if (attempt >= 10 || (error.code !== 'EPERM' && error.code !== 'EACCES' && error.code !== 'EBUSY')) {
            throw error;
          }
          Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 20 * attempt);
        }
      }
    } catch (error) {
      this.log(`could not write ${this.file}: ${error.message}`);
      fs.rmSync(temporary, { force: true });
    }
  }
}

function describeJob(job) {
  return {
    id: job.id,
    weight: job.weight,
    gpu: job.gpu,
    pid: job.pid,
    commandPid: job.commandPid || null,
    orphan: job.socket === null,
    cwd: job.cwd,
    command: job.command,
    queuedAt: job.queuedAt,
    grantedAt: job.grantedAt || null,
    typicalMs: job.estimate ? job.estimate.typicalMs : null,
  };
}

function serve(options) {
  // nothing is granted before listening (see listen)
  const scheduler = new Scheduler(options.slots, Infinity, options.policy || DefaultPolicy);
  // Several schedulers may write to one log for a moment (one stopping, the next starting): tag each line
  const log = (text) => process.stdout.write(`${new Date().toISOString()} [${process.pid}] ${text}\n`);
  const stateDirectory = options.stateDir || resolveStateDirectory();
  const history = new RunHistory(path.join(stateDirectory, 'history.json'), log);
  let nextId = history.nextId;
  let idleSince = Date.now();

  // Tells each waiting client its place and expected times when the place changed or the start moved by a minute
  const notifyWaiting = () => {
    if (scheduler.waiting.length === 0) {
      return;
    }
    const forecast = scheduler.forecast();
    scheduler.waiting.forEach((job, index) => {
      const view = outlook(job, forecast);
      if (job.lastPosition !== index || Math.abs(view.startAt - (job.lastStartAt || 0)) >= ForecastNoticeMs) {
        job.lastPosition = index;
        job.lastStartAt = view.startAt;
        send(job.socket, { type: 'waiting', id: job.id, ...view, used: scheduler.used, capacity: scheduler.capacity });
      }
    });
  };
  /** A job's place and expected times: ahead = waiting jobs before it (0 once it runs). */
  const outlook = (job, forecast) => {
    const expected = forecast.get(job) || { startAt: null, endAt: null };
    const index = scheduler.waiting.indexOf(job);
    return {
      state: index >= 0 ? 'waiting' : 'running',
      ahead: Math.max(0, index),
      position: index + 1,
      startAt: expected.startAt,
      endAt: expected.endAt,
    };
  };
  const announce = (granted) => {
    for (const job of granted) {
      log(`grant #${job.id} weight ${job.weight}${job.gpu ? ' gpu' : ''}${job.backfilled ? ' backfilled' : ''} pid ${job.pid}: ${job.command}`);
      send(job.socket, { type: 'granted', id: job.id, cpus: job.cpus(), capacity: scheduler.capacity });
    }
    notifyWaiting();
    if (!scheduler.isIdle()) {
      idleSince = Date.now();
    }
  };
  // Job ids stay the same across a scheduler restart, so that `cancel <id>` read from an earlier `status` never hits
  // another job: a re-registering client brings its id, and the next new id continues from the saved one.
  const chooseId = (wanted) => {
    const id = Number(wanted);
    const taken = (candidate) => scheduler.running.concat(scheduler.waiting).some((job) => job.id === candidate);
    if (Number.isInteger(id) && id > 0 && !taken(id)) {
      nextId = Math.max(nextId, id + 1);
      return id;
    }
    return nextId++;
  };
  const createJob = (socket, message) => ({
    id: chooseId(message.id),
    socket,
    weight: Number(message.weight) || 1,
    gpu: message.gpu === true,
    pid: Number(message.pid) || 0,
    commandPid: Number(message.commandPid) || 0,
    spawnedAt: Number(message.spawnedAt) || 0,
    cwd: String(message.cwd || ''),
    command: String(message.command || ''),
    queuedAt: Number(message.queuedAt) || Date.now(),
    grantedAt: Number(message.grantedAt) || 0,
    estimate: history.estimate(String(message.command || '')),
    lastPosition: -1,
    done: false,
    cancelled: false,
    cpus() {
      return Math.max(1, Math.floor((os.cpus().length / scheduler.capacity) * this.weight));
    },
  });

  // The running jobs with a started command, so that a restarted scheduler still counts the ones whose client is gone
  // (nobody re-registers them). Jobs whose client lives re-register themselves.
  const jobsPath = path.join(stateDirectory, `jobs-${options.port}.json`);
  const persist = () => {
    const records = scheduler.running
      .filter((job) => job.commandPid > 0)
      .map((job) => ({ ...describeJob(job), spawnedAt: job.spawnedAt }));
    try {
      fs.mkdirSync(path.dirname(jobsPath), { recursive: true });
      fs.writeFileSync(jobsPath, JSON.stringify({ nextId, jobs: records }));
    } catch (error) {
      log(`could not write ${jobsPath}: ${error.message}`);
    }
  };
  const restore = () => {
    let saved = null;
    try {
      saved = JSON.parse(fs.readFileSync(jobsPath, 'utf8'));
    } catch (error) {
      if (error.code !== 'ENOENT') {
        log(`could not read ${jobsPath}: ${error.message}`);
      }
      return;
    }
    if (saved !== null && Number.isInteger(saved.nextId)) {
      nextId = Math.max(nextId, saved.nextId);
    }
    const records = saved !== null && Array.isArray(saved.jobs) ? saved.jobs : [];
    for (const record of records) {
      if (isClientAlive(Number(record.pid))) {
        continue; // its client re-registers it
      }
      const job = createJob(null, record);
      scheduler.adopt(job);
      log(`restored #${job.id} weight ${job.weight} (client gone, command ${job.commandPid}): ${job.command}`);
    }
    // No persist here: the records of live clients stay in the file until they re-register (the next persist), so
    // that a crash in between does not lose them.
  };

  const release = (job, why) => {
    log(`end #${job.id} (${why})`);
    // Only a run seen from its grant to its end: the command ended by itself (not stopped by a signal or a cancel), and
    // the job was not lost or adopted after a restart
    if (why === 'client closed' && job.completed && !job.cancelled && !job.adopted && job.grantedAt > 0) {
      history.record(job.command, Date.now() - job.grantedAt, nextId);
    }
    announce(scheduler.remove(job));
    if (scheduler.isIdle()) {
      idleSince = Date.now();
    }
    persist();
  };

  /** The live processes of each orphan's command (Unix: its process group id), or null per job when unsure. */
  const orphanProcesses = async (orphans) => {
    if (orphans.length === 0) {
      return [];
    }
    const processes = process.platform === 'win32' ? await queryWindowsProcesses() : null;
    return orphans.map((job) => {
      if (job.commandPid <= 0) {
        return [];
      }
      if (process.platform !== 'win32') {
        return isProcessGroupAlive(job.commandPid) ? [job.commandPid] : [];
      }
      return windowsCommandProcesses(processes, job.commandPid, job.spawnedAt);
    });
  };

  // A running job whose client is gone keeps its slot until its command has exited.
  let polling = false;
  setInterval(async () => {
    const orphans = scheduler.running.filter((job) => job.socket === null);
    if (polling || orphans.length === 0) {
      return;
    }
    polling = true;
    try {
      const live = await orphanProcesses(orphans);
      orphans.forEach((job, index) => {
        if (live[index] !== null && live[index].length === 0 && scheduler.running.includes(job)) {
          release(job, `the command of a lost client exited`);
        }
      });
    } finally {
      polling = false;
    }
  }, OrphanPollMs).unref();

  /** Cancels jobs and resolves to the ids it cancelled: a job that ended meanwhile (while the processes of lost
   * clients were read) is left out. */
  const cancel = async (requested) => {
    const orphans = requested.filter((job) => job.socket === null && scheduler.running.includes(job));
    const live = await orphanProcesses(orphans);
    orphans.forEach((job, index) => (live[index] || []).forEach(killProcessTree));
    const jobs = requested.filter((job) => !job.cancelled && (scheduler.running.includes(job) || scheduler.waiting.includes(job)));
    for (const job of jobs) {
      log(`cancel #${job.id}: ${job.command}`);
      send(job.socket, { type: 'cancelled', id: job.id });
      if (scheduler.running.includes(job) && job.socket !== null && !job.socket.destroyed) {
        // The client stops its command and then closes: keep the slot until then, so that the next job does not start
        // beside a command that is still being stopped
        job.cancelPending = true;
        continue;
      }
      job.cancelled = true;
      if (scheduler.running.includes(job) || scheduler.waiting.includes(job)) {
        release(job, 'cancelled');
      }
    }
    return jobs.map((job) => job.id);
  };

  const server = net.createServer((socket) => {
    let job = null;
    socket.on('error', () => socket.destroy());
    socket.on('close', () => {
      if (job === null || job.cancelled) {
        return;
      }
      if (job.cancelPending) {
        const cancelled = job;
        job = null;
        cancelled.cancelled = true;
        release(cancelled, 'cancelled');
        return;
      }
      const lost = job;
      job = null;
      lost.socket = null;
      if (scheduler.running.includes(lost) && !lost.done && lost.commandPid > 0) {
        // The poll releases the slot once no process of the command is left.
        log(`client of #${lost.id} is gone; holding its slot while command ${lost.commandPid} runs`);
        return;
      }
      release(lost, 'client closed');
    });
    onMessages(socket, (message) => {
      if (message.version !== ProtocolVersion) {
        send(socket, {
          type: 'error',
          code: 'version',
          message: `protocol ${message.version} does not match the scheduler's ${ProtocolVersion}; run "build-queue.js stop" when no job runs`,
        });
        socket.end();
        return;
      }
      switch (message.type) {
        case 'request':
        case 'adopt': {
          if (job !== null) {
            return;
          }
          // A re-registering job started its command before this scheduler: its run time counts from that start
          // (backfilling reserves by it), not from the restart
          job = createJob(socket, { ...message, grantedAt: message.type === 'adopt' ? Number(message.spawnedAt) || 0 : 0 });
          if (message.type === 'adopt') {
            // The same client lost only its connection: the slot the scheduler kept for it is this job. Adopt first,
            // so that removing the kept job hands no slot to a waiting job.
            // The old connection may not have closed yet (its close can arrive after this adopt): match by client pid
            // whatever the socket state, and mark the old job so that its later close does nothing.
            const kept = scheduler.running.filter((other) => other !== job && other.pid === job.pid);
            scheduler.adopt(job);
            kept.forEach((other) => {
              other.cancelled = true;
              release(other, `re-registered as #${job.id}`);
            });
            persist();
            log(`adopt #${job.id} weight ${job.weight} pid ${job.pid} command ${job.commandPid}: ${job.command}`);
            send(socket, { type: 'adopted', id: job.id });
            idleSince = Date.now();
          } else {
            log(`queue #${job.id} weight ${job.weight}${job.gpu ? ' gpu' : ''} pid ${job.pid}: ${job.command}`);
            const granted = scheduler.enqueue(job);
            // The id first, so that the caller can ask about its job (status <id>) whatever happens next
            send(socket, { type: 'queued', id: job.id, ...outlook(job, scheduler.forecast()) });
            announce(granted);
            persist(); // keeps the next id
          }
          break;
        }
        case 'started':
          if (job !== null) {
            job.commandPid = Number(message.commandPid) || 0;
            job.spawnedAt = Number(message.spawnedAt) || Date.now();
            persist();
          }
          break;
        case 'done':
          if (job !== null) {
            job.done = true;
            job.completed = message.completed === true;
          }
          break;
        case 'status': {
          scheduler.order();
          const forecast = scheduler.forecast();
          const view = (j) => ({ ...describeJob(j), ...outlook(j, forecast) });
          send(socket, {
            type: 'status',
            pid: process.pid,
            now: Date.now(),
            policy: scheduler.policy,
            capacity: scheduler.capacity,
            used: scheduler.used,
            running: scheduler.running.map(view),
            waiting: scheduler.waiting.map(view),
          });
          socket.end();
          break;
        }
        case 'cancel': {
          const all = scheduler.running.concat(scheduler.waiting);
          const targets = message.all === true ? all : all.filter((j) => j.id === message.id);
          cancel(targets).then((cancelled) => {
            send(socket, { type: 'cancel-result', cancelled });
            socket.end();
          });
          break;
        }
        case 'stop': {
          log('stop requested');
          send(socket, { type: 'stopping', running: scheduler.running.length, waiting: scheduler.waiting.length });
          // Deliver what was sent to the clients (a cancel just before the stop) before leaving: end every connection
          // and exit when the clients have closed their side too, or after a second. Exiting once our data is only
          // flushed is not enough: on Windows a process that exits with unread data from a client (a "started" sent
          // right after a grant) resets the connection, and the client can lose the messages it has not read yet.
          server.close();
          const open = scheduler.running.concat(scheduler.waiting).map((j) => j.socket).filter((s) => s !== null);
          let pending = open.length + 1;
          const closed = () => {
            pending--;
            if (pending === 0) {
              process.exit(0);
            }
          };
          setTimeout(() => process.exit(0), 1000).unref();
          for (const s of [socket, ...open]) {
            if (s.destroyed) {
              closed();
              continue;
            }
            s.once('close', closed);
            s.end();
          }
          break;
        }
        default:
          send(socket, { type: 'error', message: `unknown message type ${message.type}` });
          socket.end();
      }
    });
  });

  server.on('error', (error) => {
    if (error.code === 'EADDRINUSE') {
      log(`port ${options.port} is in use; another scheduler runs`);
      process.exit(0);
    }
    log(`server error: ${error.message}`);
    process.exit(1);
  });
  server.listen(options.port, '127.0.0.1', () => {
    log(`scheduler pid ${process.pid} on 127.0.0.1:${options.port}, ${scheduler.capacity} slots, ${os.cpus().length} cores, policy ${scheduler.policy}`);
    // The grace for re-registration starts once clients can connect; only the scheduler that owns the port restores.
    scheduler.holdUntil = Date.now() + StartupGraceMs;
    restore();
    setTimeout(() => announce(scheduler.grant()), StartupGraceMs + 10);
  });
  // The response ratios change with time alone: a waiting job can overtake the first one and fit in the free slots
  // between two events, so grant again now and then while jobs wait
  setInterval(() => {
    if (scheduler.waiting.length > 0) {
      announce(scheduler.grant());
    }
  }, RegrantMs).unref();
  setInterval(() => {
    if (scheduler.isIdle() && Date.now() - idleSince >= IdleExitMs) {
      log('idle; exiting');
      process.exit(0);
    }
  }, 60 * 1000).unref();
}

// ---------------------------------------------------------------------------------------------------------------------
// Client

// just options and variable assignments may stand between just and the recipe: just -f justfile test, just a=b test
const TestCommandPattern = new RegExp(
  [
    String.raw`\bdotnet(\.exe)?"?\s+(test|vstest|stryker)\b`,
    String.raw`\bdotnet-stryker\b`,
    String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate|long-checks)(\s|$)`,
    String.raw`\b(test-stability|test-memory|test-sanitized|long-checks|mutate|coverage-diff)\.js\b`,
  ].join('|'),
);

function defaultWeight(command) {
  return TestCommandPattern.test(command) ? 2 : 1;
}

const NodeLimitPattern = /(^|\s)(--?|\/)(m|maxcpucount|max-cpu-count)([:=\s]|$)/i;

/**
 * Adds -m:<cpus> after each dotnet build / test / msbuild / publish / pack of the command whose own arguments (up to the
 * next &&, ||, ;, | or line break) set no -m.
 */
function limitMsbuildNodes(command, cpus) {
  const invocation = /((?:"[^"]*[\\/])?\bdotnet(?:\.exe)?"?)(\s+)(build|test|msbuild|publish|pack)(?![-\w.])/gi;
  return command.replace(invocation, (match, program, space, verb, offset) => {
    const rest = invocationArguments(command, offset + match.length);
    // dotnet test <assembly>.dll runs VSTest without MSBuild
    const vstest = verb.toLowerCase() === 'test' && /(^|\s)"?[^\s"]+\.(dll|exe)"?(\s|$)/i.test(rest);
    return NodeLimitPattern.test(rest) || vstest ? match : `${program}${space}${verb} -m:${cpus}`;
  });
}

/** The arguments of an invocation that starts at `start`: up to the next &&, ||, ;, | or line break outside quotes. */
function invocationArguments(command, start) {
  let quote = '';
  for (let i = start; i < command.length; i++) {
    const ch = command[i];
    if (quote !== '') {
      quote = ch === quote ? '' : quote;
    } else if (ch === '"' || ch === "'") {
      quote = ch;
    } else if (ch === ';' || ch === '|' || ch === '\n' || ch === '\r' || (ch === '&' && command[i + 1] === '&')) {
      return command.slice(start, i);
    }
  }
  return command.slice(start);
}

/** Starts the scheduler detached in the state directory (never a worktree), its output appended to server.log. */
function startScheduler(options) {
  const stateDirectory = resolveStateDirectory();
  fs.mkdirSync(stateDirectory, { recursive: true });
  const logPath = path.join(stateDirectory, 'server.log');
  try {
    if (fs.statSync(logPath).size > LogRotateBytes) {
      fs.renameSync(logPath, `${logPath}.1`);
    }
  } catch (error) {
    if (error.code !== 'ENOENT') {
      prefix(`could not rotate ${logPath}: ${error.message}`);
    }
  }
  const args = [__filename, 'serve', '--port', String(options.port), '--slots', String(options.slots), '--policy', options.policy];
  args.push('--state-dir', stateDirectory);
  if (process.platform === 'win32' && startThroughWmi(args, stateDirectory, logPath)) {
    return;
  }
  const logFd = fs.openSync(logPath, 'a');
  const child = spawn(process.execPath, args, {
    cwd: stateDirectory,
    detached: true,
    stdio: ['ignore', logFd, logFd],
    windowsHide: true,
  });
  child.unref();
  fs.closeSync(logFd);
}

/**
 * Windows: starts the scheduler through WMI (Win32_Process.Create), so that it does not belong to the caller's job
 * object. Antigravity runs each command in a job object and ends every process in it when the command ends, which
 * would stop a scheduler started as a plain child. Returns false when WMI fails; the caller then spawns it directly.
 */
function startThroughWmi(args, stateDirectory, logPath) {
  const quote = (text) => `"${text}"`;
  // cmd.exe treats & ^ ( ) and spaces in a path specially: quote every argument (it still expands %VAR% in quotes)
  const serve = [quote(process.execPath), ...args.map(quote)].join(' ');
  const commandLine = `cmd.exe /d /s /c "${serve} >> ${quote(logPath)} 2>&1"`;
  // A CIM error must fail the script (not exit 0 through `exit $null`), so that the caller falls back
  const script =
    "$ErrorActionPreference = 'Stop'; " +
    '$s = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }; ' +
    '$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ ' +
    'CommandLine = $env:GSHARP_BUILD_QUEUE_WMI_COMMAND; CurrentDirectory = $env:GSHARP_BUILD_QUEUE_WMI_DIRECTORY; ' +
    'ProcessStartupInformation = $s }; if ($null -eq $r -or $null -eq $r.ReturnValue) { exit 1 }; exit [int]$r.ReturnValue';
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    windowsHide: true,
    timeout: 8000, // well inside the 15 s that connectOrStart waits, so that the fallback still has time
    env: { ...process.env, GSHARP_BUILD_QUEUE_WMI_COMMAND: commandLine, GSHARP_BUILD_QUEUE_WMI_DIRECTORY: stateDirectory },
  });
  if (result.error || result.status !== 0) {
    prefix(`could not start the scheduler through WMI (${result.error ? result.error.message : `status ${result.status}`}); starting it as a child`);
    return false;
  }
  return true;
}

/** Connects to the scheduler; resolves to the socket, or null when no scheduler listens. */
function connect(port) {
  return new Promise((resolve, reject) => {
    const socket = net.connect(port, '127.0.0.1');
    socket.once('connect', () => {
      socket.removeAllListeners('error');
      // A reset after the caller is done with the socket must not crash the process; callers add their own handlers.
      socket.on('error', () => socket.destroy());
      resolve(socket);
    });
    socket.once('error', (error) => {
      if (error.code === 'ECONNREFUSED') {
        resolve(null);
      } else {
        reject(error);
      }
    });
  });
}

/** Connects, starting the scheduler when none listens, for up to 15 seconds. */
async function connectOrStart(options) {
  let deadline = Date.now() + 15000;
  let extended = false;
  let started = 0;
  for (;;) {
    const socket = await connect(options.port);
    if (socket !== null) {
      return socket;
    }
    if (Date.now() - started > 5000) {
      startScheduler(options);
      started = Date.now();
      // Starting can take seconds (WMI, then a fallback): give the first scheduler time to listen, once only, so that
      // a scheduler that never listens still ends the wait
      if (!extended) {
        extended = true;
        deadline = Math.max(deadline, started + 8000);
      }
    }
    if (Date.now() > deadline) {
      throw new Error(`could not reach or start the scheduler on 127.0.0.1:${options.port}`);
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
}

function prefix(text) {
  process.stderr.write(`[build-queue] ${text}\n`);
}

/** Stops a process and everything it started (Unix: its process group, which the command leads). */
function killProcessTree(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return;
  }
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
    return;
  }
  try {
    process.kill(-pid, 'SIGTERM');
  } catch (error) {
    if (error.code !== 'ESRCH') {
      prefix(`could not stop the command's process group ${pid}: ${error.message}`);
    }
  }
}

function killChild(child) {
  if (child !== null && child.exitCode === null && child.pid !== undefined) {
    killProcessTree(child.pid);
  }
}

const Shells = ['bash', 'pwsh', 'powershell', 'cmd', 'sh'];

/**
 * The shell that runs the command: --shell, else the one an outer run chose (GSHARP_BUILD_QUEUE_SHELL), else the
 * caller's: Git Bash on Windows when MSYSTEM or SHELL is set (the Claude Code Bash tool), PowerShell otherwise on
 * Windows (the Claude Code PowerShell tool, Antigravity), /bin/sh elsewhere.
 */
function resolveShell(requested) {
  if (requested !== '') {
    return requested;
  }
  const inherited = process.env[ShellVariable] || '';
  if (Shells.includes(inherited)) {
    return inherited;
  }
  if (process.platform !== 'win32') {
    return 'sh';
  }
  return process.env.MSYSTEM || process.env.SHELL ? 'bash' : 'pwsh';
}

/**
 * Windows: the bash.exe of Git for Windows. A bare `bash` on the PATH may be the WSL launcher (System32\bash.exe),
 * which would run the command in Linux.
 */
function gitBashPath() {
  if (process.platform !== 'win32') {
    return 'bash';
  }
  const roots = [process.env.ProgramW6432, process.env.ProgramFiles, process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Programs')];
  for (const root of roots) {
    if (root) {
      const candidate = path.join(root, 'Git', 'bin', 'bash.exe');
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }
  return 'bash';
}

/** The program and arguments that run `command` in `shell`, keeping the command's exit code. */
function shellInvocation(shell, command) {
  switch (shell) {
    case 'bash':
      return { file: gitBashPath(), args: ['-c', command] };
    case 'sh':
      return { file: '/bin/sh', args: ['-c', command] };
    case 'cmd':
      return { file: process.env.ComSpec || 'cmd.exe', args: ['/d', '/s', '/c', `"${command}"`], verbatim: true };
    default: {
      // -Command exits with 0 or 1 only; pass the exit code of the last native program on.
      const script = `${command}\n$gsharpOk = $?; if ($LASTEXITCODE) { exit $LASTEXITCODE }; if (-not $gsharpOk) { exit 1 }`;
      return { file: shell, args: ['-NoProfile', '-NonInteractive', '-Command', script] };
    }
  }
}

/**
 * Runs the command in the shell with the queue's environment; resolves to { code, signaled }: signaled when a signal
 * ended the command (Unix; on Windows `taskkill /F` gives exit code 1, which looks like a failure).
 */
function runShell(command, shell, env, onChild) {
  return new Promise((resolve) => {
    const start = (name) => {
      const { file, args, verbatim } = shellInvocation(name, command);
      const child = spawn(file, args, {
        stdio: 'inherit',
        env: { ...env, [ShellVariable]: shell },
        windowsVerbatimArguments: verbatim === true,
        // Unix: the command leads its own process group, so the scheduler can watch and stop it after this client
        // dies.
        detached: process.platform !== 'win32',
      });
      let replaced = false;
      child.on('error', (error) => {
        if (error.code === 'ENOENT' && name === 'pwsh') {
          // Windows PowerShell when PowerShell 7 is not installed. The failed child still emits 'close'; that close
          // must not end the run while the replacement runs.
          replaced = true;
          start('powershell');
          return;
        }
        prefix(`could not start the command with ${file}: ${error.message}`);
        resolve({ code: 1, signaled: false });
      });
      if (child.pid !== undefined) {
        onChild(child);
      }
      child.on('close', (code, signal) => {
        if (!replaced) {
          // A shell reports a command that signal n killed as 128 + n (Unix)
          const bySignal = signal !== null || (process.platform !== 'win32' && code > 128 && code < 160);
          resolve({ code: code === null ? 1 : code, signaled: bySignal });
        }
      });
    };
    start(shell);
  });
}

/** --gpu: runs this script again for the same command under gpu-lock.js, which then asks for the CPU slot. */
function runUnderGpuLock(options, weight) {
  const gpuLock = path.join(__dirname, 'gpu-lock.js');
  const innerArgs = [`"${process.execPath}"`, `"${__filename}"`, 'run', '--weight', String(weight)];
  innerArgs.push('--port', String(options.port));
  if (options.timeoutMinutes > 0) {
    innerArgs.push('--timeout-minutes', String(options.timeoutMinutes));
  }
  innerArgs.push('--command-from-env');
  const env = {
    ...process.env,
    [InGpuLockVariable]: '1',
    [CommandVariable]: options.command,
    [ShellVariable]: resolveShell(options.shell), // gpu-lock.js runs the inner client through cmd.exe
  };
  const args = [gpuLock];
  if (options.timeoutMinutes > 0) {
    args.push('--timeout-minutes', String(options.timeoutMinutes));
  }
  args.push('--', innerArgs.join(' '));
  const child = spawn(process.execPath, args, { stdio: 'inherit', env });
  // Windows: the console delivers Ctrl+C to gpu-lock.js and the inner client themselves; forwarding it with
  // child.kill would terminate gpu-lock.js before it releases its lock. Unix: forward the signal.
  const forward = (signal) => () => {
    if (process.platform !== 'win32') {
      child.kill(signal);
    }
  };
  process.on('SIGINT', forward('SIGINT'));
  process.on('SIGTERM', forward('SIGTERM'));
  child.on('close', (code) => process.exit(code === null ? 1 : code));
}

/** Quotes one argument for a shell, so that the shell passes it on as one unchanged word. */
function quoteArgument(shell, word) {
  const powerShell = shell === 'pwsh' || shell === 'powershell';
  // PowerShell splits -Name.Part=1 at the dot and a word with a leading or trailing comma into an array; bash and sh
  // read \ as an escape outside quotes (C:\dir\file). cmd needs no quotes for \ in a path.
  const needsQuotes =
    (powerShell && ((/^-/.test(word) && word.includes('.')) || /^,|,$/.test(word))) ||
    ((shell === 'bash' || shell === 'sh') && word.includes('\\'));
  if (/^[\w+=:,./\\-]+$/.test(word) && !needsQuotes) {
    return word;
  }
  if (shell === 'cmd') {
    return `"${word.replace(/"/g, '""')}"`;
  }
  if (shell === 'pwsh' || shell === 'powershell') {
    return `'${word.replace(/'/g, "''")}'`;
  }
  return `'${word.replace(/'/g, "'\\''")}'`;
}

async function run(options) {
  if (options.commandWords !== null) {
    const shell = resolveShell(options.shell);
    if ((shell === 'pwsh' || shell === 'powershell') && options.commandWords.includes('--%')) {
      // PowerShell cannot pass a literal --% to a program, quoted or not: it is its stop-parsing token
      usageError('PowerShell cannot pass --% on; give the command as one string or choose another --shell.');
    }
    const words = options.commandWords.map((word) => quoteArgument(shell, word));
    // PowerShell runs a quoted first word only through the call operator
    options.command = (shell === 'pwsh' || shell === 'powershell') && words[0] !== options.commandWords[0] ? `& ${words.join(' ')}` : words.join(' ');
    options.shell = shell; // the --gpu path re-runs this script with the command as one string
  }
  const weight = options.weight > 0 ? options.weight : defaultWeight(options.command);
  const inGpuLock = process.env[InGpuLockVariable] === '1';

  if (process.env[JobVariable] && (inGpuLock || !options.gpu)) {
    // Inside a queued job: its slot covers this command, with the same node limit.
    const cpus = Number(process.env[CpusVariable]) || 0;
    const command = cpus > 0 ? limitMsbuildNodes(options.command, cpus) : options.command;
    let child = null;
    const stop = (code) => () => {
      killChild(child);
      process.exit(code);
    };
    process.on('SIGINT', stop(130));
    process.on('SIGTERM', stop(143));
    process.exit((await runShell(command, resolveShell(options.shell), process.env, (c) => (child = c))).code);
    return;
  }
  if (options.gpu && !inGpuLock) {
    if (/fuzz-diff|fuzz-differential|long-checks/.test(options.command)) {
      // They take the GPU lock themselves for their GPU steps; holding it here would make them wait for ever
      usageError('fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.');
    }
    runUnderGpuLock(options, weight);
    return;
  }

  const queuedAt = Date.now();
  let state = 'waiting'; // waiting -> running -> done
  let child = null;
  let jobId = 0;
  let spawnedAt = 0;
  let socket = null;
  let registered = true; // false when the scheduler refused this client's protocol version
  let lastNotice = -Infinity;
  let lastPosition = 0;
  let lastStartAt = null;

  // completed: the command ran to its own end (not stopped by a signal or a cancel), so its run time may be recorded
  const finish = (code, completed = false) => {
    if (state === 'done') {
      return;
    }
    state = 'done';
    if (socket === null || socket.destroyed) {
      process.exit(code);
    }
    // Exit once "done" has been flushed, so that the scheduler frees the slot at once instead of treating the job as
    // a lost client whose command may still run.
    setTimeout(() => process.exit(code), 1000).unref();
    send(socket, { version: ProtocolVersion, type: 'done', completed });
    socket.end(() => process.exit(code));
  };
  const onSignal = (signal, code) => () => {
    prefix(`${signal}: stopping the command and leaving the queue`);
    killChild(child);
    finish(code);
  };
  process.on('SIGINT', onSignal('SIGINT', 130));
  process.on('SIGTERM', onSignal('SIGTERM', 143));
  process.on('SIGHUP', onSignal('SIGHUP', 129));

  if (options.timeoutMinutes > 0) {
    setTimeout(() => {
      if (state === 'waiting') {
        prefix(`gave up after ${options.timeoutMinutes} minutes of waiting; see "build-queue.js status"`);
        finish(3);
      }
    }, options.timeoutMinutes * 60 * 1000).unref();
  }

  const start = async (granted) => {
    state = 'running';
    jobId = granted.id;
    const cpus = granted.cpus;
    const command = limitMsbuildNodes(options.command, cpus);
    const waited = Math.round((Date.now() - queuedAt) / 1000);
    prefix(`granted #${granted.id} after ${waited}s (weight ${weight}, ${cpus} MSBuild nodes)`);
    const env = {
      ...process.env,
      [JobVariable]: String(granted.id),
      [CpusVariable]: String(cpus),
      [MsbuildArgsVariable]: `-m:${cpus}`,
    };
    const { code, signaled } = await runShell(command, resolveShell(options.shell), env, (c) => {
      child = c;
      spawnedAt = Date.now();
      if (c.pid !== undefined) {
        send(socket, { version: ProtocolVersion, type: 'started', commandPid: c.pid, spawnedAt });
      }
    });
    finish(code, !signaled); // a run a signal cut short is not recorded
  };

  const attach = async () => {
    for (;;) {
      try {
        socket = await connectOrStart(options);
        break;
      } catch (error) {
        if (state === 'running') {
          // The command runs regardless; keep trying to register it so that the next jobs count its weight.
          await new Promise((resolve) => setTimeout(resolve, 5000));
          continue;
        }
        prefix(error.message);
        process.exit(1);
      }
    }
    const current = socket;
    send(current, {
      version: ProtocolVersion,
      type: state === 'running' ? 'adopt' : 'request',
      weight,
      gpu: inGpuLock,
      pid: process.pid,
      commandPid: child !== null && child.pid !== undefined ? child.pid : 0,
      spawnedAt,
      cwd: process.cwd(),
      command: options.command,
      queuedAt,
      id: jobId, // keeps the job id across a scheduler restart
    });
    onMessages(current, (message) => {
      switch (message.type) {
        case 'queued': {
          jobId = Number(message.id) || jobId;
          const outlook = message.state === 'waiting' ? describeOutlook(message, Date.now()) : '';
          // The first waiting notice says the same: skip it
          lastPosition = message.position;
          lastStartAt = message.startAt;
          lastNotice = Date.now();
          prefix(`queued #${message.id}${outlook ? `: ${outlook}` : ''}; "build-queue.js status ${message.id}" shows it`);
          break;
        }
        case 'waiting': {
          jobId = Number(message.id) || jobId;
          const now = Date.now();
          const moved = Math.abs((message.startAt || 0) - (lastStartAt || 0)) >= ForecastNoticeMs;
          if (message.position !== lastPosition || moved || now - lastNotice >= NoticeIntervalMs) {
            lastPosition = message.position;
            lastStartAt = message.startAt;
            lastNotice = now;
            const outlook = describeOutlook(message, now);
            prefix(
              `waiting #${jobId}: position ${message.position}, ${message.used}/${message.capacity} slots in use ` +
                `(${Math.round((now - queuedAt) / 1000)}s)${outlook ? `; ${outlook}` : ''}`,
            );
          }
          break;
        }
        case 'granted':
          if (state === 'waiting') {
            start(message);
          }
          break;
        case 'adopted':
          prefix(`re-registered the running job as #${message.id} (was #${jobId})`);
          jobId = message.id;
          break;
        case 'cancelled':
          if (state === 'done') {
            break; // the command had already ended
          }
          prefix(`job #${message.id} was cancelled${state === 'running' ? '; stopping the command' : ''}`);
          if (child !== null && child.exitCode === null && child.signalCode === null) {
            // Leave (and so free the slot) once the command has really ended
            const leave = () => finish(CancelledExitCode);
            child.once('close', leave);
            setTimeout(leave, 10000).unref();
            killChild(child);
          } else {
            finish(CancelledExitCode);
          }
          break;
        case 'error':
          prefix(`scheduler: ${message.message}`);
          if (state === 'waiting') {
            process.exit(1);
          }
          if (message.code === 'version') {
            registered = false; // the command keeps running, unregistered; do not reconnect again
          }
          break;
        default:
          break;
      }
    });
    current.on('error', () => current.destroy());
    current.on('close', () => {
      if (state !== 'done' && socket === current) {
        socket = null;
        if (!registered) {
          return;
        }
        prefix(`lost the scheduler; ${state === 'running' ? 're-registering the running job' : 'queueing again'}`);
        // Running jobs re-register first, inside the scheduler's startup grace; waiting ones queue a little later.
        setTimeout(attach, state === 'running' ? 100 : 800);
      }
    });
  };

  await attach();
}

async function request(options, message) {
  const socket = await connect(options.port);
  if (socket === null) {
    return null;
  }
  return new Promise((resolve, reject) => {
    let reply = null;
    onMessages(socket, (m) => {
      reply = m;
    });
    socket.on('error', reject);
    socket.on('close', () => resolve(reply));
    send(socket, { version: ProtocolVersion, ...message });
  });
}

function formatJob(job, now) {
  const since = job.grantedAt || job.queuedAt;
  const minutes = Math.round((now - since) / 60000);
  const flags = `${job.gpu ? ' gpu' : ''}${job.orphan ? ' client-lost' : ''}`;
  const outlook = describeOutlook(job, now);
  return `  #${job.id} w${job.weight}${flags} ${minutes}m pid ${job.pid} ${job.cwd}${outlook ? `\n      ${outlook}` : ''}\n      ${job.command}`;
}

/** "2 ahead, start ~20:31 (in 6m), end ~20:35 (in 10m)" for a waiting job, "end ~20:35 (in 4m)" for a running one. */
function describeOutlook(view, now) {
  if (!Number.isFinite(view.startAt) || !Number.isFinite(view.endAt)) {
    return '';
  }
  const clock = (time) => new Date(time).toTimeString().slice(0, 5);
  const minutes = (ms) => `${Math.max(0, Math.round(ms / 60000))}m`;
  const end = `expected end ~${clock(view.endAt)} (in ${minutes(view.endAt - now)})`;
  if (view.state === 'running') {
    return end;
  }
  return `${view.ahead} ahead, expected start ~${clock(view.startAt)} (in ${minutes(view.startAt - now)}), ${end}`;
}

function printReplyError(reply) {
  process.stdout.write(`${reply.message || JSON.stringify(reply)}\n`);
  process.exitCode = 1;
}

async function status(options) {
  const reply = await request(options, { type: 'status' });
  if (reply !== null && reply.type !== 'status') {
    printReplyError(reply);
    return;
  }
  if (options.statusId > 0) {
    // One job: its state, place and expected times; exit 1 when the scheduler does not know it (ended or never queued)
    const job = reply === null ? undefined : reply.running.concat(reply.waiting).find((j) => j.id === options.statusId);
    if (job === undefined) {
      process.stdout.write(options.json ? `${JSON.stringify({ id: options.statusId, state: 'unknown' })}\n` : `no job #${options.statusId} (ended, or never queued)\n`);
      process.exitCode = 1;
      return;
    }
    process.stdout.write(options.json ? `${JSON.stringify({ now: reply.now, ...job })}\n` : `${formatJob(job, reply.now)}\n`);
    return;
  }
  if (reply === null) {
    process.stdout.write(options.json ? `${JSON.stringify({ running: [], waiting: [] })}\n` : `no scheduler on 127.0.0.1:${options.port} (no jobs)\n`);
    return;
  }
  if (options.json) {
    process.stdout.write(`${JSON.stringify(reply)}\n`);
    return;
  }
  const now = reply.now || Date.now();
  const lines = [`scheduler pid ${reply.pid}: ${reply.used}/${reply.capacity} slots in use, policy ${reply.policy}`];
  lines.push(`running (${reply.running.length}):`);
  reply.running.forEach((job) => lines.push(formatJob(job, now)));
  lines.push(`waiting (${reply.waiting.length}):`);
  reply.waiting.forEach((job) => lines.push(formatJob(job, now)));
  process.stdout.write(`${lines.join('\n')}\n`);
}

async function cancel(options, all) {
  const reply = await request(options, all ? { type: 'cancel', all: true } : { type: 'cancel', id: options.cancelId });
  if (reply === null) {
    process.stdout.write('no scheduler running (no jobs)\n');
    return;
  }
  if (reply.type !== 'cancel-result') {
    printReplyError(reply);
    return;
  }
  if (reply.cancelled.length === 0) {
    process.stdout.write(all ? 'no jobs to cancel\n' : `no job #${options.cancelId}\n`);
    process.exitCode = all ? 0 : 1;
    return;
  }
  process.stdout.write(`cancelled ${reply.cancelled.map((id) => `#${id}`).join(' ')}\n`);
}

async function stop(options) {
  const reply = await request(options, { type: 'stop' });
  if (reply === null) {
    process.stdout.write('no scheduler running\n');
    return;
  }
  if (reply.type !== 'stopping') {
    printReplyError(reply);
    return;
  }
  process.stdout.write(
    `scheduler stopped (${reply.running} running jobs re-register with the next one, ${reply.waiting} waiting jobs queue again)\n`,
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Self-test

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Starts a scheduler on a private port and resolves once it accepts connections. */
async function startTestScheduler(port, slots, env) {
  const server = spawn(process.execPath, [__filename, 'serve', '--port', String(port), '--slots', String(slots)], {
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

/** Polls the scheduler until `used` reaches the expected value or the time is up; resolves to the last value. */
async function usedWithin(port, expected, ms) {
  const deadline = Date.now() + ms;
  for (;;) {
    const used = (await request({ port }, { type: 'status' })).used;
    if (used === expected || Date.now() > deadline) {
      return used;
    }
    await delay(250);
  }
}

/** Backfilling: a later job starts only when it ends before the first job's reserved start or fits in what it leaves. */
function checkBackfill(assert, job) {
  const times = (typicalS, longestS) => ({ typicalMs: typicalS * 1000, longestMs: longestS * 1000 });
  for (const policy of ['backfill', 'fifo']) {
    const s = new Scheduler(4, 0, policy);
    const a = job(1, 3, 0, false, times(80, 100));
    const b = job(2, 2, 1);
    const c = job(3, 1, 2, false, times(40, 50));
    const d = job(4, 1, 3, false, times(400, 500));
    assert.deepStrictEqual(s.enqueue(a, 0), [a]);
    assert.deepStrictEqual(s.enqueue(b, 1), []);
    const expected = policy === 'fifo' ? [] : [c];
    assert.deepStrictEqual(s.enqueue(c, 2), expected, `${policy}: a job that ends before the first one can start`);
    assert.deepStrictEqual(s.enqueue(d, 3), [], `${policy}: no free slot left`);
    assert.deepStrictEqual(s.waiting[0], b, `${policy}: the first job stays first`);
  }
  {
    // The first job waits for 3 slots; when a 2-slot job ends, 4 are free: one slot to spare for a long job
    const s = new Scheduler(4, 0, 'backfill');
    const a = job(1, 2, 0, false, times(80, 100));
    const b = job(2, 3, 1);
    const c = job(3, 1, 2, false, times(400, 500));
    const d = job(4, 1, 3);
    s.enqueue(a, 0);
    s.enqueue(b, 1);
    assert.deepStrictEqual(s.enqueue(c, 2), [c], 'a long job fits in the slot the first job leaves free');
    assert.deepStrictEqual(s.enqueue(d, 3), [], 'the spare slot is taken; an unknown job could run past the reserved start');
    assert.deepStrictEqual(s.remove(a, 100000), [b], 'the first job starts at its reserved time');
  }
  {
    // Several jobs backfilled in one pass: the spare slot and the idle slots are each used once
    const running = (weight, longestS) => Object.assign(job(9, weight, 0, false, times(longestS, longestS)), { grantedAt: 1 }); // adopt() reads 0 as unset
    const s = new Scheduler(4, 1000, 'backfill');
    s.adopt(running(2, 100), 500);
    const queued = [job(2, 3, 1), job(3, 1, 2, false, times(500, 500)), job(4, 1, 3, false, times(500, 500))];
    queued.forEach((j) => s.enqueue(j, 500));
    assert.deepStrictEqual(s.grant(1000), [queued[1]], 'one spare slot: only one long job');
    const t = new Scheduler(4, 1000, 'backfill');
    t.adopt(running(3, 100), 500);
    const short = [job(2, 4, 1), job(3, 1, 2, false, times(50, 50)), job(4, 1, 3, false, times(50, 50))];
    short.forEach((j) => t.enqueue(j, 500));
    assert.deepStrictEqual(t.grant(1000), [short[1]], 'one idle slot: only one short job');
    const u = new Scheduler(4, 1000, 'backfill');
    u.adopt(running(3, 100), 500);
    const late = [job(2, 4, 1), job(3, 1, 2, false, times(500, 500))];
    late.forEach((j) => u.enqueue(j, 500));
    assert.deepStrictEqual(u.grant(1000), [], 'a job that would run past the reserved start, with no slot to spare');
    // Two running jobs: the one that ends first frees enough; the reservation leaves no slot to spare
    const v = new Scheduler(4, 1000, 'backfill');
    v.adopt(Object.assign(job(7, 1, 0, false, times(500, 500)), { grantedAt: 1 }), 500);
    v.adopt(Object.assign(job(8, 2, 0, false, times(100, 100)), { grantedAt: 1 }), 500);
    const behind = [job(2, 3, 1), job(3, 1, 2, false, times(1000, 1000))];
    behind.forEach((j) => v.enqueue(j, 500));
    assert.deepStrictEqual(v.grant(1000), [], 'the ends are taken in time order, not in the order the jobs started');
  }
  {
    // A running job with no recorded time, or past its longest one: its end is unknown, nothing is backfilled,
    // not even into a slot that would be spare
    const s = new Scheduler(4, 0, 'backfill');
    s.enqueue(job(1, 2, 0), 0);
    s.enqueue(job(2, 3, 1), 1);
    assert.deepStrictEqual(s.enqueue(job(3, 1, 2, false, times(1, 1)), 2), [], 'unknown end of a running job');
    const t = new Scheduler(4, 0, 'backfill');
    t.enqueue(job(1, 2, 0, false, times(5, 10)), 0);
    t.enqueue(job(2, 3, 1), 1);
    assert.deepStrictEqual(t.enqueue(job(3, 1, 20000, false, times(1, 1)), 20000), [], 'running past its longest time');
  }
}

/** Response ratio order: a short job goes first, and a long one rises the longer it waits. */
function checkResponseRatio(assert, job) {
  const typical = (seconds) => ({ typicalMs: seconds * 1000, longestMs: seconds * 1000 });
  {
    const s = new Scheduler(1, 0, 'hrrn');
    const x = job(1, 1, 0);
    const long = job(2, 1, 0, false, typical(600));
    const short = job(3, 1, 10000, false, typical(30));
    s.enqueue(x, 0);
    s.enqueue(long, 0);
    s.enqueue(short, 10000);
    assert.deepStrictEqual(s.remove(x, 60000), [short], 'the short job overtakes the long one');
  }
  {
    const s = new Scheduler(1, 0, 'hrrn');
    const x = job(1, 1, 0);
    const long = job(2, 1, 0, false, typical(600));
    const short = job(3, 1, 1000000, false, typical(30));
    s.enqueue(x, 0);
    s.enqueue(long, 0);
    s.enqueue(short, 1000000);
    assert.deepStrictEqual(s.remove(x, 1010000), [long], 'a long job that waited long enough goes first');
  }
  {
    const s = new Scheduler(1, 0, 'hrrn');
    const x = job(1, 1, 0);
    const unknown = job(2, 1, 0);
    const known = job(3, 1, 0, false, typical(90));
    s.enqueue(x, 0);
    s.enqueue(known, 0);
    s.enqueue(unknown, 0);
    assert.deepStrictEqual(s.remove(x, 30000), [unknown], 'a job with no record counts as UnknownTypicalMs');
  }
}

/** Forecast: grants played forward with the typical times; a job past its times is expected to end soon. */
function checkForecast(assert, job) {
  const times = (typicalS, longestS) => ({ typicalMs: typicalS * 1000, longestMs: longestS * 1000 });
  const s = new Scheduler(2, 0, 'fifo');
  const a = job(1, 2, 0, false, times(100, 300));
  const b = job(2, 1, 1, false, times(50, 50));
  const c = job(3, 1, 2, false, times(20, 20));
  const d = job(4, 2, 3);
  [a, b, c, d].forEach((j) => s.enqueue(j, 0));
  let f = s.forecast(10000);
  assert.deepStrictEqual(f.get(a), { startAt: 0, endAt: 100000 });
  assert.deepStrictEqual(f.get(b), { startAt: 100000, endAt: 150000 });
  assert.deepStrictEqual(f.get(c), { startAt: 100000, endAt: 120000 });
  assert.deepStrictEqual(f.get(d), { startAt: 150000, endAt: 150000 + UnknownTypicalMs });
  assert.strictEqual(s.waiting.length, 3, 'the forecast does not change the scheduler');
  f = s.forecast(200000);
  assert.strictEqual(f.get(a).endAt, 300000, 'past its typical time: its longest time');
  f = s.forecast(400000);
  assert.strictEqual(f.get(a).endAt, 400000 + OverrunGuessMs, 'past its longest time: soon');
  // hrrn: a short job overtakes the waiting long one while slots are free, with no job ending in between
  const h = new Scheduler(4, 0, 'hrrn');
  const running = job(1, 2, 0, false, times(900, 900));
  const wide = job(2, 4, -600000, false, times(3600, 3600)); // has waited 10 minutes
  const narrow = job(3, 2, 0, false, times(1200, 1200));
  h.enqueue(running, 0);
  h.enqueue(wide, 0);
  h.enqueue(narrow, 0);
  assert.deepStrictEqual(h.waiting, [wide, narrow], 'the wide job waited longer: it is first');
  // The ratios cross at 300 s: (300 + 600 + 3600) / 3600 = (300 + 1200) / 1200
  const expected = h.forecast(0).get(narrow).startAt;
  assert.ok(expected > 300000 && expected < 900000, `the forecast sees the overtaking before the running job ends (${expected})`);
  assert.deepStrictEqual(h.grant(400000), [narrow], 'once its ratio passed the wide one, it fits and starts');
  // During the startup grace the positions already follow the ratio
  const held = new Scheduler(4, 1000, 'hrrn');
  const long = job(4, 1, 0, false, times(600, 600));
  const short = job(5, 1, 1, false, times(10, 10));
  held.enqueue(long, 500);
  held.enqueue(short, 500);
  assert.deepStrictEqual(held.waiting, [short, long], 'ordered while nothing is granted');
  // A replay that would take too many steps stops; the jobs it did not reach have no times
  const slow = new Scheduler(8, 0, 'hrrn');
  slow.enqueue(job(1, 2, 0, false, times(LongestRunMs / 1000, LongestRunMs / 1000)), 0);
  const far = job(2, 8, 0);
  slow.enqueue(far, 0);
  const started = Date.now();
  assert.strictEqual(slow.forecast(0).get(far), undefined, 'not reached within ForecastSteps');
  assert.ok(Date.now() - started < 1000, 'the forecast stays fast');
}

/** Run history: median and longest of the last runs per command, kept on disk. */
function checkRunHistory(assert) {
  const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'build-queue-history-')), 'history.json');
  const history = new RunHistory(file, () => {});
  assert.strictEqual(history.estimate('dotnet build'), null);
  [10, 30, 20].forEach((ms) => history.record('dotnet  build ', ms));
  assert.deepStrictEqual(history.estimate('dotnet build'), { typicalMs: 20, longestMs: 30 }, 'spaces do not matter');
  history.record('dotnet build', 40);
  assert.deepStrictEqual(history.estimate('dotnet build'), { typicalMs: 25, longestMs: 40 });
  for (let i = 0; i < HistoryRunsPerCommand; i++) {
    history.record('dotnet build', 5);
  }
  assert.deepStrictEqual(new RunHistory(file, () => {}).estimate('dotnet build'), { typicalMs: 5, longestMs: 5 }, 'only the last runs count');
  // A damaged file loses its bad entries, never the scheduler
  fs.writeFileSync(file, JSON.stringify({ nextId: -3, commands: { a: { runs: 5 }, b: 7, c: null, d: { runs: [1, 'x', -2, 3] } } }));
  const damaged = new RunHistory(file, () => {});
  assert.strictEqual(damaged.nextId, 1, 'a negative next id is ignored');
  assert.deepStrictEqual(damaged.estimate('d'), { typicalMs: 2, longestMs: 3 }, 'the valid runs of an entry stay');
  ['a', 'b', 'c'].forEach((key) => damaged.record(key, 10));
  assert.deepStrictEqual(damaged.estimate('a'), { typicalMs: 10, longestMs: 10 });
  fs.writeFileSync(file, '[1, 2');
  assert.strictEqual(new RunHistory(file, () => {}).estimate('a'), null, 'unreadable JSON starts empty');
  // Command texts that are names of Object members are plain keys
  const plain = new RunHistory(file, () => {});
  ['constructor', 'toString', '__proto__'].forEach((key) => plain.record(key, 7));
  assert.deepStrictEqual(new RunHistory(file, () => {}).estimate('__proto__'), { typicalMs: 7, longestMs: 7 });
  assert.strictEqual(new RunHistory(file, () => {}).estimate('hasOwnProperty'), null);
  // Times beyond any real run are damaged input
  fs.writeFileSync(file, JSON.stringify({ commands: { huge: { runs: [1e15, 5] } } }));
  assert.deepStrictEqual(new RunHistory(file, () => {}).estimate('huge'), { typicalMs: 5, longestMs: 5 });
}

async function selfTest() {
  const assert = require('assert');

  // Scheduler order: FIFO, the head is never overtaken, GPU holders are granted at once, weights are clamped,
  // nothing is granted during the startup grace.
  const s = new Scheduler(4, 0, 'fifo');
  const job = (id, weight, queuedAt, gpu = false, estimate = null) => ({ id, weight, queuedAt, gpu, estimate });
  const a = job(1, 2, 1);
  const b = job(2, 2, 2);
  const c = job(3, 4, 3);
  const d = job(4, 1, 4);
  assert.deepStrictEqual(s.enqueue(a), [a]);
  assert.deepStrictEqual(s.enqueue(b), [b]);
  assert.deepStrictEqual(s.enqueue(c), []);
  assert.deepStrictEqual(s.enqueue(d), [], 'a light job must not overtake the waiting head');
  assert.strictEqual(c.weight, 4);
  const g = job(5, 9, 5, true);
  assert.deepStrictEqual(s.enqueue(g), [g], 'a GPU holder is granted at once, over the capacity');
  assert.strictEqual(g.weight, 4, 'weight is clamped to the capacity');
  assert.strictEqual(s.used, 8);
  assert.deepStrictEqual(s.remove(a), []);
  assert.deepStrictEqual(s.remove(g), []);
  assert.deepStrictEqual(s.remove(b), [c]);
  assert.deepStrictEqual(s.remove(c), [d]);
  s.adopt(job(6, 4, 0));
  assert.strictEqual(s.used, 5, 'an adopted job counts even over the capacity');
  const held = new Scheduler(2, 1000);
  const h = job(7, 1, 1);
  assert.deepStrictEqual(held.enqueue(h, 500), [], 'nothing is granted during the startup grace');
  assert.deepStrictEqual(held.grant(1000), [h]);
  checkBackfill(assert, job);
  checkResponseRatio(assert, job);
  checkForecast(assert, job);
  checkRunHistory(assert);

  // Node limit: added per dotnet invocation that sets no -m of its own.
  assert.strictEqual(limitMsbuildNodes('dotnet test X.csproj && dotnet build', 6), 'dotnet test -m:6 X.csproj && dotnet build -m:6');
  assert.strictEqual(limitMsbuildNodes('dotnet build-server shutdown', 6), 'dotnet build-server shutdown');
  assert.strictEqual(limitMsbuildNodes('dotnet test -m:2', 6), 'dotnet test -m:2');
  assert.strictEqual(limitMsbuildNodes('dotnet build -m:2 && dotnet test', 6), 'dotnet build -m:2 && dotnet test -m:6');
  assert.strictEqual(limitMsbuildNodes('dotnet build --maxcpucount 4', 6), 'dotnet build --maxcpucount 4');
  assert.strictEqual(limitMsbuildNodes('dotnet.exe build', 6), 'dotnet.exe build -m:6');
  assert.strictEqual(
    limitMsbuildNodes('"C:\\Program Files\\dotnet\\dotnet.exe" test x', 6),
    '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x',
  );
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

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'build-queue-self-test-'));
  const env = { ...process.env };
  delete env[JobVariable];
  delete env[InGpuLockVariable];
  delete env[CpusVariable];
  env[StateDirectoryVariable] = dir; // a scheduler that a client starts logs here, not into the main checkout
  // The test commands below are written for cmd / sh; the shells themselves are checked in part 6.
  env[ShellVariable] = process.platform === 'win32' ? 'cmd' : 'sh';
  const children = [];
  // --slots: a scheduler that a client restarts gets the same capacity as the test one.
  const client = (port, command, extra = []) => {
    const args = [__filename, 'run', '--port', String(port), '--slots', '2', '--weight', '1', ...extra, '--', command];
    const child = spawn(process.execPath, args, {
      stdio: 'ignore',
      windowsHide: true,
      env,
    });
    children.push(child);
    child.done = new Promise((resolve) => child.on('close', resolve));
    return child;
  };
  const sleeper = (ms) => `"${process.execPath}" -e "setTimeout(function(){},${ms})"`;
  const servers = [];
  let testPort = 0;
  try {
    // 1. At most 2 jobs at once, in arrival order; a client killed with its command frees its slot.
    const port = 20000 + Math.floor(Math.random() * 20000);
    testPort = port;
    servers.push(await startTestScheduler(port, 2, env));
    await delay(StartupGraceMs);
    const marker = path.join(dir, 'log.txt');
    const script = path.join(dir, 'job.js');
    fs.writeFileSync(
      script,
      "const fs=require('fs');const [f,i]=process.argv.slice(2);" +
        "fs.appendFileSync(f,`start ${i} ${Date.now()}\\n`);" +
        "setTimeout(()=>{fs.appendFileSync(f,`end ${i} ${Date.now()}\\n`)},700);",
    );
    const holder = client(port, sleeper(60000));
    await delay(1000);
    const runs = [];
    for (const i of [0, 1, 2, 3]) {
      runs.push(client(port, `"${process.execPath}" "${script}" "${marker}" ${i}`).done);
      await delay(400);
    }
    const before = await request({ port }, { type: 'status' });
    assert.strictEqual(before.used, 2, 'the holder and one job fill the 2 slots');
    assert.ok(before.waiting.length >= 1, 'jobs wait');
    before.waiting.forEach((waiting, index) => {
      assert.strictEqual(waiting.ahead, index, 'the jobs ahead of a waiting job');
      assert.ok(Number.isFinite(waiting.startAt) && waiting.endAt > waiting.startAt, 'expected start and end');
      assert.ok(index === 0 || waiting.startAt >= before.waiting[index - 1].startAt, 'a later job is expected to start later');
    });
    // run prints its id at once; status <id> --json tells the same job
    const asking = spawn(process.execPath, [__filename, 'run', '--port', String(port), '--slots', '2', '--', sleeper(100)], {
      stdio: ['ignore', 'ignore', 'pipe'],
      windowsHide: true,
      env,
    });
    children.push(asking);
    const queuedId = await new Promise((resolve) => {
      let text = '';
      asking.stderr.on('data', (data) => {
        text += data;
        const match = /queued #(\d+): (\d+) ahead, expected start/.exec(text);
        if (match) {
          resolve(Number(match[1]));
        }
      });
      asking.on('close', () => resolve(0));
    });
    assert.ok(queuedId > 0, 'run prints "queued #<id>: <n> ahead, expected start ..." while it waits');
    const one = spawnSync(process.execPath, [__filename, 'status', String(queuedId), '--json', '--port', String(port)], {
      encoding: 'utf8',
      windowsHide: true,
      env,
    });
    const view = JSON.parse(one.stdout);
    assert.strictEqual(view.id, queuedId);
    assert.strictEqual(view.state, 'waiting');
    assert.ok(Number.isFinite(view.endAt), 'status <id> --json gives the expected end');
    killProcessTree(holder.pid);
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
    const lone = client(port, sleeper(8000));
    await delay(800);
    lone.kill('SIGKILL'); // the client only; on Windows the command's shell survives as its own process
    await delay(OrphanPollMs + 500);
    const orphaned = await request({ port }, { type: 'status' });
    assert.strictEqual(orphaned.used, 1, 'the slot stays while the command of a lost client runs');
    assert.strictEqual(orphaned.running[0].orphan, true);
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanPollMs * 3), 0, 'the slot comes back when it exits');

    // 3. cancel-all stops running jobs and empties the queue; the clients exit with 4.
    const r1 = client(port, sleeper(60000));
    const r2 = client(port, sleeper(60000));
    await delay(600);
    const w1 = client(port, sleeper(60000));
    await delay(600);
    const result = await request({ port }, { type: 'cancel', all: true });
    assert.strictEqual(result.cancelled.length, 3);
    assert.deepStrictEqual(await Promise.all([r1.done, r2.done, w1.done]), [4, 4, 4]);
    // A cancelled running job keeps its slot until its client has stopped the command and closed
    assert.strictEqual(await usedWithin(port, 0, 5000), 0);
    const empty = await request({ port }, { type: 'status' });
    assert.deepStrictEqual([empty.used, empty.running.length, empty.waiting.length], [0, 0, 0]);
    // Run times are recorded for the commands that ended by themselves, not for killed, lost or cancelled ones, nor
    // for one stopped early (done with completed: false, as a client sends after a signal)
    const rawRun = (command, completed) =>
      new Promise((resolve) => {
        const s = net.connect(port, '127.0.0.1', () =>
          send(s, { version: ProtocolVersion, type: 'request', weight: 1, pid: process.pid, command, queuedAt: Date.now() }),
        );
        onMessages(s, (m) => {
          if (m.type === 'granted') {
            send(s, { version: ProtocolVersion, type: 'done', completed });
            s.end();
          }
        });
        s.on('close', resolve);
        s.on('error', resolve);
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
      assert.ok(recorded.estimate(`"${process.execPath}" "${script}" "${marker}" ${i}`) !== null, `job ${i} is recorded`);
    }
    assert.ok(recorded.estimate('ran to its end') !== null, 'a run that ended by itself is recorded');
    assert.strictEqual(recorded.estimate('stopped early'), null, 'a run stopped early is not recorded');
    assert.strictEqual(recorded.estimate(sleeper(60000)), null, 'killed and cancelled runs are not recorded');
    assert.strictEqual(recorded.estimate(sleeper(8000)), null, 'the run of a lost client is not recorded');

    // 4. After a scheduler restart, running jobs re-register before waiting ones are granted: never over capacity.
    const k1 = client(port, sleeper(6000));
    const k2 = client(port, sleeper(6000));
    await delay(600);
    const k3 = client(port, sleeper(500));
    await delay(600);
    const restartedAt = Date.now();
    servers[0].kill('SIGKILL'); // the clients start the next scheduler themselves
    let maxUsed = 0;
    let adoptedSince = Infinity;
    for (let i = 0; i < 20; i++) {
      const snapshot = await request({ port }, { type: 'status' }).catch(() => null); // the scheduler may be restarting
      if (snapshot !== null && snapshot.type === 'status') {
        maxUsed = Math.max(maxUsed, snapshot.used);
        snapshot.running.forEach((j) => (adoptedSince = Math.min(adoptedSince, j.grantedAt)));
      }
      await delay(150);
    }
    assert.ok(maxUsed <= 2, `never over capacity after a restart, saw ${maxUsed}`);
    assert.ok(adoptedSince < restartedAt, 'a re-registered job keeps the start of its command, not the restart time');
    assert.deepStrictEqual(await Promise.all([k1.done, k2.done, k3.done]), [0, 0, 0]);

    // 6. The command runs in the chosen shell and its exit code comes back: PowerShell passes the code of the last
    //    native program on; bash and sh understand their own syntax.
    const exitOf = async (shell, command) => client(port, command, ['--shell', shell]).done;
    const node = `"${process.execPath.replace(/\\/g, '/')}"`;
    if (process.platform === 'win32') {
      assert.strictEqual(await exitOf('pwsh', `$x = 1; & ${node} -e "process.exit(5)"`), 5, 'pwsh: native exit code');
      assert.strictEqual(await exitOf('cmd', 'exit 6'), 6, 'cmd');
    }
    if (spawnSync(gitBashPath(), ['-c', 'exit 0'], { windowsHide: true }).status === 0) {
      assert.strictEqual(await exitOf('bash', 'FOO=7; true 2>/dev/null; exit $FOO'), 7, 'bash syntax');
    }

    // 7. Several words after `--` arrive as argv already split by the caller's shell; each reaches the command as it was.
    const argvExit = (shell) =>
      new Promise((resolve) => {
        // The command compares its own argv with the words, so each word makes the round trip through the shell
        const check = 'process.exit(JSON.stringify(process.argv.slice(1))===process.env.BQ_EXPECT?0:9)';
        const words = ['A&B c|d', "it's", '-Dfoo.bar=1', 'C:\\dir\\f.txt', '@x.rsp', 'x,', '-p:A=1', 'Category!=Stress', '$HOME'];
        const args = [__filename, 'run', '--port', String(port), '--shell', shell, '--', process.execPath, '-e', check, ...words];
        const childEnv = { ...env, BQ_EXPECT: JSON.stringify(words) };
        const child = spawn(process.execPath, args, { stdio: 'ignore', windowsHide: true, env: childEnv });
        children.push(child);
        child.on('close', resolve);
      });
    const shells = process.platform === 'win32' ? ['pwsh', 'cmd'] : ['sh'];
    if (spawnSync(gitBashPath(), ['-c', 'exit 0'], { windowsHide: true }).status === 0) {
      shells.push('bash');
    }
    for (const shell of shells) {
      assert.strictEqual(await argvExit(shell), 0, `argv after -- survives ${shell}`);
    }

    // 8. A client that lost only its connection re-registers: its kept slot is replaced, and a waiting job does not
    //    get it in between (capacity 2: A weighs 2, W waits with 2).
    await usedWithin(port, 0, 5000);
    const raw = () =>
      new Promise((resolve) => {
        const socket = net.connect(port, '127.0.0.1', () => resolve(socket));
        socket.on('error', () => socket.destroy());
      });
    const sleeperProcess = spawn(process.execPath, ['-e', 'setTimeout(function(){},20000)'], { windowsHide: true });
    children.push(sleeperProcess);
    const fakeClientPid = process.pid;
    const first = await raw();
    send(first, { version: ProtocolVersion, type: 'request', weight: 2, pid: fakeClientPid, command: 'A', queuedAt: Date.now() });
    await delay(300);
    send(first, { version: ProtocolVersion, type: 'started', commandPid: sleeperProcess.pid, spawnedAt: Date.now() });
    const waiter = await raw();
    send(waiter, { version: ProtocolVersion, type: 'request', weight: 2, pid: 1, command: 'W', queuedAt: Date.now() });
    await delay(300);
    first.destroy();
    await delay(500);
    const again = await raw();
    send(again, {
      version: ProtocolVersion,
      type: 'adopt',
      weight: 2,
      pid: fakeClientPid,
      commandPid: sleeperProcess.pid,
      spawnedAt: Date.now(),
      command: 'A',
      queuedAt: Date.now(),
    });
    await delay(500);
    const readopted = await request({ port }, { type: 'status' });
    assert.deepStrictEqual(
      [readopted.used, readopted.running.map((j) => j.command), readopted.waiting.map((j) => j.command)],
      [2, ['A'], ['W']],
      're-registration replaces the kept slot without handing it to W',
    );
    again.destroy();
    waiter.destroy();
    killProcessTree(sleeperProcess.pid);

    // 5. A job whose client was lost before a restart still holds its slot after it (restored from jobs-<port>.json).
    await usedWithin(port, 0, 5000);
    const lost = client(port, sleeper(8000));
    await delay(800);
    lost.kill('SIGKILL'); // the client only
    await delay(300);
    const beforeRestart = await request({ port }, { type: 'status' });
    await request({ port }, { type: 'stop' });
    await delay(300);
    servers.push(await startTestScheduler(port, 2, env));
    const restored = await request({ port }, { type: 'status' });
    assert.strictEqual(restored.used, 1, 'the job of the lost client is restored after the restart');
    assert.strictEqual(restored.running[0].orphan, true);
    assert.strictEqual(restored.running[0].id, beforeRestart.running[0].id, 'a job keeps its id across a restart');
    const fresh = client(port, sleeper(100));
    await delay(StartupGraceMs + 800);
    assert.ok(fresh.exitCode === 0 || (await fresh.done) === 0);
    const newIds = fs.readFileSync(path.join(dir, `jobs-${port}.json`), 'utf8');
    assert.ok(JSON.parse(newIds).nextId > beforeRestart.running[0].id, 'new ids continue after the saved ones');
    assert.strictEqual(await usedWithin(port, 0, 8000 + OrphanPollMs * 3), 0, 'and released when its command exits');

    // 9. hrrn re-grants while jobs wait, not only when a job ends: a short job that overtakes the waiting wide one
    //    starts within RegrantMs, although nothing ends before the running job (20 s).
    const hrrnDir = fs.mkdtempSync(path.join(os.tmpdir(), 'build-queue-self-test-hrrn-'));
    const hrrnPort = port + 1;
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
      servers.push(await startTestScheduler(hrrnPort, 2, { ...env, [StateDirectoryVariable]: hrrnDir }));
      await delay(StartupGraceMs);
      client(hrrnPort, longRun);
      assert.strictEqual(await usedWithin(hrrnPort, 1, 5000), 1, 'the long job runs before the wide one arrives');
      client(hrrnPort, sleeper(301), ['--weight', '2']); // unknown: one minute; waits for both slots
      await delay(4000);
      // The short job arrives behind the wide one, which has waited about 4 s longer: their ratios cross about 2 s
      // later ((t + 20) / 20 = (t + 4 + 60) / 60), so it first waits, then starts at a re-grant, not at enqueue.
      // Without the re-grant it would start only when the long job ends, at least 13 s after it is seen waiting.
      client(hrrnPort, shortRun);
      let seenWaitingAt = 0;
      let startedAfterWaiting = Infinity;
      const pollUntil = Date.now() + 20000;
      while (Date.now() < pollUntil) {
        const now = await request({ port: hrrnPort }, { type: 'status' });
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
      await request({ port: hrrnPort }, { type: 'cancel', all: true });
      await usedWithin(hrrnPort, 0, 5000); // the cancelled clients are gone before the stop: none re-registers
      await request({ port: hrrnPort }, { type: 'stop' });
    } finally {
      fs.rmSync(hrrnDir, { recursive: true, force: true });
    }
  } finally {
    children.forEach((child) => child.exitCode === null && killProcessTree(child.pid));
    servers.forEach((server) => server.kill('SIGKILL'));
    if (testPort !== 0) {
      await request({ port: testPort }, { type: 'stop' }).catch(() => null);
    }
    await delay(200);
    fs.rmSync(dir, { recursive: true, force: true });
  }
  process.stdout.write('build-queue self-test passed\n');
}

// ---------------------------------------------------------------------------------------------------------------------

function main() {
  const options = parseArgs(process.argv.slice(2));
  const modes = {
    run: () => run(options),
    status: () => status(options),
    cancel: () => cancel(options, false),
    'cancel-all': () => cancel(options, true),
    stop: () => stop(options),
    serve: () => serve(options),
    'self-test': selfTest,
  };
  Promise.resolve()
    .then(modes[options.mode])
    .catch((error) => {
      process.stderr.write(`[build-queue] ${error.stack || error.message}\n`);
      process.exit(1);
    });
}

module.exports = { Scheduler, limitMsbuildNodes, defaultWeight };

if (require.main === module) {
  main();
}
