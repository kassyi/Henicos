#!/usr/bin/env node
'use strict';

// Runs one command while holding the shared GPU test lock, so that only one agent or worktree uses the GPU at a time.
//
// The lock is the directory <main checkout>/tmp/gpu-test-lock, the same for every worktree: the main checkout is the
// parent of `git rev-parse --git-common-dir`. Without git, the repository root found from this script is used.
// The script creates the directory (mkdir is atomic), writes owner.txt (pid, time, command) and owner.json (pid, host,
// platform, start time, command, and once started the command's process ids) into it, runs the command and removes the
// directory afterwards. It removes only a lock that it created, also on Ctrl+C (SIGINT / SIGTERM / SIGHUP), after
// stopping the command.
//
// Stale locks: on every wait poll the owner.json is checked. A lock is taken over only when both the owner (this
// script) and the command it started are gone, because a hard kill of the owner (agent tool timeout, Task Manager,
// SIGKILL) leaves the command running:
//   - The owner is on this host and platform and its pid is gone (process.kill(pid, 0) fails with ESRCH; EPERM means
//     alive).
//   - Linux / macOS: the command runs detached, as the leader of its own session and process group, whose id is
//     recorded. The group is gone when process.kill(-pgid, 0) fails with ESRCH. A process that leaves the group
//     (setsid) is not seen.
//   - Windows: the pid of the shell (cmd.exe) and the time it was spawned are recorded. The command is gone when no
//     live process is that shell or a child of it (Win32_Process keeps a dead parent's pid in ParentProcessId), read
//     through PowerShell. Creation times guard against a reused pid: a process that started more than a few seconds
//     before the spawn is never ours, and a process created later with the shell's pid is not the shell. A grandchild
//     whose parent has also exited cannot be traced back and is not seen.
// Then the lock is renamed away only if it still holds that owner's record, one line says whose lock it was, and the
// lock is acquired as usual. Anything uncertain counts as alive and is waited for: an owner on another host or platform
// (a WSL pid cannot be checked from Windows), a record that cannot be read, a record without the command's ids (written
// by an older version of this script, or an owner killed between starting the command and recording it), a failed
// process query. A reused pid therefore only delays a takeover; it never steals a lock.
//
// Kernel pools: so that a run shows whether the Windows kernel pools grew while the GPU faulted (the counters say how
// much, not what holds it; see gpu-fault-watch.js), the script reads the pools and the nvlddmkm Event 153 entries
// (scripts/human/gpu-fault-watch.js, about 0.1 s) twice, without blocking (Ctrl+C still stops it): while the command
// starts, after it has been recorded in owner.json, so that a hard kill during the reading leaves a lock that can be
// taken over; and after the command has ended, before the lock is released. It prints one line, appends a row to
// gpu-pool-log.tsv next to the lock directory, and warns when the nonpaged pool is above 6 GB before the run, or after
// the run when the pools grew by more than 1 GB or the nonpaged pool is above 6 GB. GSHARP_GPU_POOL_WATCH=0 turns the
// reading off. A reading that fails says why; it never changes the exit code.
//
// Usage:
//   node scripts/agent/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] [--lock-path <dir>] -- <command> [args...]
//   node scripts/agent/gpu-lock.js --help
//   node scripts/agent/gpu-lock.js --self-test
//
// The command runs through the shell, so quote it as one string when it needs pipes or redirection:
//   node scripts/agent/gpu-lock.js -- "dotnet test X.csproj --filter Foo > tmp/log.txt 2>&1"
//
// Options:
//   --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).
//   --timeout-minutes <n>  Give up waiting after n minutes, print the owner and exit with 3 (default 0 = wait forever).
//   --lock-path <dir>      Use this lock directory instead of <main checkout>/tmp/gpu-test-lock. For tests of this
//                          script only; real runs must share the default.
//   --help                 Show this help.
//
// Exit codes: the exit code of the command; 2 on a usage error; 3 when the wait timed out; 130 / 143 on SIGINT / SIGTERM.

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn, spawnSync } = require('child_process');

/** The kernel pool reader, or null when it cannot be loaded; the lock works without it. */
const faultWatch = (() => {
  try {
    return require(path.join(__dirname, '..', 'human', 'gpu-fault-watch.js'));
  } catch (error) {
    process.stderr.write(`[gpu-lock] kernel pool watch unavailable: ${error.message}\n`);
    return null;
  }
})();

const usage = [
  'Usage: node scripts/agent/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] [--lock-path <dir>] -- <command> [args...]',
  '',
  '  --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).',
  '  --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
  '  --lock-path <dir>      Lock directory to use instead of <main checkout>/tmp/gpu-test-lock (for tests of this script only).',
  '  --help                 Show this help.',
  '  --self-test            Check that a run killed while it reads the kernel pools leaves a lock that is taken over.',
  '',
  'Windows: the kernel pools and the nvlddmkm Event 153 entries are read after the command starts and before the lock',
  'is released (about 0.1 s each), printed, appended to gpu-pool-log.tsv next to the lock directory, and a warning is',
  'printed when the pools grew by more than 1 GB or the nonpaged pool is above 6 GB. GSHARP_GPU_POOL_WATCH=0 turns',
  'this off.',
  '',
  'A lock whose owner is on this host and platform and no longer running, together with the command it started, is',
  'taken over while waiting.',
  '',
  'Quote the command as one string when it needs pipes or redirection.',
].join('\n');

function usageError(message) {
  process.stderr.write(`${message}\n${usage}\n`);
  process.exit(2);
}

function parseNumber(name, text) {
  const value = Number(text);
  if (text === undefined || text === '' || !Number.isFinite(value) || value < 0) {
    usageError(`${name} needs a non-negative number.`);
  }
  return value;
}

function parseArgs(argv) {
  const separator = argv.indexOf('--');
  const own = separator < 0 ? argv : argv.slice(0, separator);
  const command = separator < 0 ? [] : argv.slice(separator + 1);
  const options = { pollSeconds: 10, timeoutMinutes: 0, lockPath: '', command, selfTest: false };
  for (let i = 0; i < own.length; i++) {
    switch (own[i]) {
      case '--help':
      case '-h':
        process.stdout.write(`${usage}\n`);
        process.exit(0);
        break;
      case '--self-test':
        options.selfTest = true;
        break;
      case '--poll-seconds':
        options.pollSeconds = parseNumber('--poll-seconds', own[++i]);
        break;
      case '--timeout-minutes':
        options.timeoutMinutes = parseNumber('--timeout-minutes', own[++i]);
        break;
      case '--lock-path':
        options.lockPath = own[++i] || '';
        if (options.lockPath === '') {
          usageError('--lock-path needs a directory.');
        }
        break;
      default:
        usageError(`Unknown option: ${own[i]}`);
    }
  }
  if (options.pollSeconds <= 0) {
    usageError('--poll-seconds must be greater than 0.');
  }
  if (command.length === 0 && !options.selfTest) {
    usageError('No command given. Put it after "--".');
  }
  return options;
}

/** The lock lives in the main checkout's tmp/, whichever worktree runs the script. */
function resolveLockPath() {
  const scriptRoot = path.resolve(__dirname, '..', '..');
  let base = scriptRoot;
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
  return path.join(base, 'tmp', 'gpu-test-lock');
}

function readOwner(lockPath) {
  try {
    return fs.readFileSync(path.join(lockPath, 'owner.txt'), 'utf8').trim();
  } catch (error) {
    return `(owner.txt could not be read: ${error.code || error.message})`;
  }
}

const options = parseArgs(process.argv.slice(2));
// GSHARP_GPU_LOCK_PATH: the same as --lock-path, for a test whose command reaches this script indirectly (the job
// queue's self-test); real runs share the default
const lockOverride = options.lockPath || process.env.GSHARP_GPU_LOCK_PATH || '';
const lockPath = lockOverride === '' ? resolveLockPath() : path.resolve(lockOverride);
const commandText = options.command.join(' ');

/** The owner record that takeover decisions read; owner.txt stays the human-readable form. */
function readOwnerRecord(dir) {
  try {
    const record = JSON.parse(fs.readFileSync(path.join(dir, 'owner.json'), 'utf8'));
    if (record && Number.isInteger(record.pid) && record.pid > 0 && typeof record.host === 'string' && typeof record.platform === 'string') {
      return record;
    }
  } catch {
    // Missing or half-written record: the caller treats it as "owner unknown" and keeps waiting.
  }
  return null;
}

/** True only when kill(target, 0) reports that no such process (or process group) exists; EPERM means alive. */
function isGone(target) {
  try {
    process.kill(target, 0);
    return false;
  } catch (error) {
    return error.code === 'ESRCH';
  }
}

/** Slack for comparing a process creation time with the recorded spawn time (clock granularity, slow process start). */
const SpawnToleranceMs = 5000;

/**
 * Reads every process as "pid ppid creationMs" through PowerShell. Returns null when the query fails or looks
 * incomplete (it must list this process), so that the caller treats the command as alive.
 */
function queryWindowsProcesses() {
  const script =
    'Get-CimInstance Win32_Process | ForEach-Object { $c = -1; ' +
    'if ($_.CreationDate) { $c = ([DateTimeOffset]$_.CreationDate).ToUnixTimeMilliseconds() }; ' +
    '"$($_.ProcessId) $($_.ParentProcessId) $c" }';
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8',
    windowsHide: true,
    timeout: 60000,
  });
  if (result.error || result.status !== 0) {
    return null;
  }
  const processes = new Map();
  for (const line of result.stdout.split(/\r?\n/)) {
    const fields = line.trim().split(' ');
    if (fields.length === 3) {
      const [pid, ppid, created] = fields.map(Number);
      if (Number.isInteger(pid) && Number.isInteger(ppid) && Number.isFinite(created)) {
        processes.set(pid, { ppid, created });
      }
    }
  }
  return processes.has(process.pid) ? processes : null;
}

/** True when the shell recorded in `command` or a live child of it may still run. Anything uncertain is "alive". */
function isWindowsCommandAlive(command) {
  const processes = queryWindowsProcesses();
  if (processes === null) {
    return true;
  }
  const earliest = command.spawnedAt - SpawnToleranceMs;
  const latest = command.spawnedAt + SpawnToleranceMs;
  // A process created after the shell died may have been given the shell's pid; its children are not ours.
  let reusedFrom = Infinity;
  const shell = processes.get(command.pid);
  if (shell !== undefined) {
    if (shell.created < 0 || shell.created <= latest) {
      return true; // the shell itself, or a process whose creation time is unknown
    }
    reusedFrom = shell.created;
  }
  for (const [pid, info] of processes) {
    if (pid !== command.pid && info.ppid === command.pid) {
      if (info.created < 0 || (info.created >= earliest && info.created < reusedFrom)) {
        return true;
      }
    }
  }
  return false;
}

/** True when the command that the owner started (recorded in owner.json) is known to have ended. */
function isCommandGone(record) {
  const command = record.commandProcess;
  if (command === null || typeof command !== 'object' || !Number.isInteger(command.pid) || command.pid <= 0) {
    return false; // older record, or the owner died before recording the command
  }
  if (process.platform === 'win32') {
    return Number.isFinite(command.spawnedAt) && !isWindowsCommandAlive(command);
  }
  return Number.isInteger(command.pgid) && command.pgid > 1 && isGone(-command.pgid);
}

/**
 * True when the owner is on this host and platform, its pid no longer exists and neither does the command it started.
 * Anything uncertain is "alive".
 */
function isDeadLocalOwner(record) {
  if (record === null || record.host !== os.hostname() || record.platform !== process.platform) {
    return false;
  }
  return isGone(record.pid) && isCommandGone(record);
}

/** Moves a stale lock away if it still holds the dead owner's record. Returns true when this call removed it. */
function takeOverStale() {
  const record = readOwnerRecord(lockPath);
  if (!isDeadLocalOwner(record)) {
    return false;
  }
  const graveyard = `${lockPath}.stale-${process.pid}-${Date.now()}`;
  try {
    fs.renameSync(lockPath, graveyard); // atomic: only one waiter wins
  } catch {
    return false; // released or taken over by someone else meanwhile
  }
  const moved = readOwnerRecord(graveyard);
  if (moved === null || moved.pid !== record.pid || moved.startedAt !== record.startedAt) {
    // A different lock slipped in between the check and the rename: put it back.
    try {
      fs.renameSync(graveyard, lockPath);
    } catch (error) {
      process.stderr.write(`[gpu-lock] could not restore ${lockPath} from ${graveyard}: ${error.message}
`);
    }
    return false;
  }
  fs.rmSync(graveyard, { recursive: true, force: true });
  process.stderr.write(
    `[gpu-lock] took over a stale lock: owner pid ${record.pid} on ${record.host} (started ${record.startedAt}) and its command are gone; the command was: ${record.command}
`,
  );
  return true;
}

let ownsLock = false;
let ownerRecord = null;
let child = null;

function releaseLock() {
  if (!ownsLock) {
    return;
  }
  ownsLock = false;
  try {
    fs.rmSync(lockPath, { recursive: true, force: true });
  } catch (error) {
    process.stderr.write(`[gpu-lock] could not remove ${lockPath}: ${error.message}\n`);
  }
}

/** Takes the lock if it is free. Returns false when somebody else holds it. */
function tryAcquire() {
  try {
    fs.mkdirSync(path.dirname(lockPath), { recursive: true });
    fs.mkdirSync(lockPath);
  } catch (error) {
    if (error.code === 'EEXIST') {
      return false;
    }
    throw error;
  }
  ownsLock = true;
  ownerRecord = {
    pid: process.pid,
    host: os.hostname(),
    platform: process.platform,
    startedAt: new Date().toISOString(),
    cwd: process.cwd(),
    command: commandText,
    commandProcess: null,
  };
  fs.writeFileSync(path.join(lockPath, 'owner.json'), JSON.stringify(ownerRecord));
  fs.writeFileSync(
    path.join(lockPath, 'owner.txt'),
    `pid ${process.pid} ${new Date().toISOString()} cwd ${process.cwd()} ${commandText}\n`,
  );
  return true;
}

/** The reading taken while the command starts; resolves to null when the watch is unavailable or off. */
let poolsBefore = Promise.resolve(null);

/**
 * Reads the pools after the command has started and has been recorded in owner.json, and warns when the nonpaged pool
 * is already above the limit. Never rejects.
 */
async function readStartPools() {
  if (faultWatch === null) {
    return null;
  }
  try {
    const reading = await faultWatch.measureAsync();
    if (!reading.supported) {
      return null;
    }
    if (reading.errors.length > 0) {
      process.stderr.write(`[gpu-lock] ${faultWatch.describe(reading)}\n`);
    }
    if (faultWatch.isNonpagedHigh(reading)) {
      process.stderr.write(`[gpu-lock] WARNING: ${faultWatch.startWarningText(reading)}\n`);
    }
    return reading;
  } catch (error) {
    process.stderr.write(`[gpu-lock] could not read the kernel pools: ${error.message}\n`);
    return null;
  }
}

function tsvMegabytes(bytes) {
  return bytes === null || bytes === undefined ? '' : String(Math.round(bytes / (1024 * 1024)));
}

/**
 * Reads the pools again without blocking (a signal still stops the script), prints them with the Event 153 entries of
 * the run, warns when they grew past the thresholds, and appends one line to gpu-pool-log.tsv next to the lock
 * directory. Never rejects.
 */
async function reportPools(code) {
  const before = await poolsBefore;
  if (faultWatch === null || before === null) {
    return;
  }
  try {
    // typeperf (about a second) stands in for a failed WMI query only when the GPU logged a fault meanwhile
    const after = await faultWatch.measureAsync({
      slowFallback: (entries) =>
        entries !== null && before.event153Entries !== null && entries > before.event153Entries,
    });
    const comparison = faultWatch.compare(before, after);
    const entries = comparison.event153Entries === null ? 'unknown' : String(comparison.event153Entries);
    process.stderr.write(`[gpu-lock] ${faultWatch.describe(after)}; during this run: ${entries}\n`);
    if (comparison.warn) {
      process.stderr.write(`[gpu-lock] WARNING: ${faultWatch.warningText(comparison)}\n`);
    }
    const logPath = path.join(path.dirname(lockPath), 'gpu-pool-log.tsv');
    const header = fs.existsSync(logPath)
      ? ''
      : 'start\tend\texit\tnonpagedBeforeMB\tnonpagedAfterMB\tpagedBeforeMB\tpagedAfterMB\tevent153Entries\tcommand\n';
    const row = [
      before.time,
      after.time,
      code === null ? '' : String(code),
      tsvMegabytes(before.nonpagedBytes),
      tsvMegabytes(after.nonpagedBytes),
      tsvMegabytes(before.pagedBytes),
      tsvMegabytes(after.pagedBytes),
      comparison.event153Entries === null ? '' : String(comparison.event153Entries),
      commandText.replace(/[\t\r\n]+/g, ' '),
    ].join('\t');
    fs.appendFileSync(logPath, `${header}${row}\n`);
  } catch (error) {
    process.stderr.write(`[gpu-lock] could not record the kernel pools: ${error.message}\n`);
  }
}

function killChild() {
  if (child === null || child.exitCode !== null || child.pid === undefined) {
    return;
  }
  if (process.platform === 'win32') {
    // Only the process tree that this script started.
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
    return;
  }
  // The command leads its own process group (detached), so a terminal's Ctrl+C reaches only this script: stop the
  // whole group, not only the shell.
  try {
    process.kill(-child.pid, 'SIGTERM');
  } catch (error) {
    if (error.code !== 'ESRCH') {
      process.stderr.write(`[gpu-lock] could not stop the command's process group ${child.pid}: ${error.message}\n`);
    }
  }
}

function onSignal(signal, code) {
  process.stderr.write(`[gpu-lock] ${signal}: stopping the command and releasing the lock\n`);
  killChild();
  releaseLock();
  process.exit(code);
}

process.on('SIGINT', () => onSignal('SIGINT', 130));
process.on('SIGTERM', () => onSignal('SIGTERM', 143));
// A detached command no longer gets the terminal's hangup itself.
process.on('SIGHUP', () => onSignal('SIGHUP', 129));
process.on('exit', releaseLock);

/** Adds the started command's ids to owner.json, so that a waiter can tell whether it still runs. */
function recordCommandProcess(spawnedAt) {
  if (child.pid === undefined) {
    return; // spawn failed; the 'error' handler releases the lock
  }
  ownerRecord.commandProcess =
    process.platform === 'win32' ? { pid: child.pid, spawnedAt } : { pid: child.pid, pgid: child.pid };
  try {
    // A reader that sees a half-written file treats the owner as alive, so a plain write is enough.
    fs.writeFileSync(path.join(lockPath, 'owner.json'), JSON.stringify(ownerRecord));
  } catch (error) {
    process.stderr.write(`[gpu-lock] could not record the command in owner.json: ${error.message}\n`);
  }
}

function runCommand() {
  const spawnedAt = Date.now();
  // Unix: detached makes the command the leader of a new session and process group (pgid = its pid), so the group
  // outlives a hard kill of this script and a waiter can see that the command still runs. Windows keeps the console.
  // GSHARP_GPU_LOCK_HELD tells the command that it holds the lock: job-queue.js run --gpu inside it (a just recipe
  // that long-checks.js runs under this lock) asks for its CPU slot without taking the lock a second time
  const env = { ...process.env, GSHARP_GPU_LOCK_HELD: lockPath };
  child = spawn(commandText, { stdio: 'inherit', shell: true, env, detached: process.platform !== 'win32' });
  recordCommandProcess(spawnedAt);
  // After the record, so that a hard kill during the reading leaves a lock whose owner and command can be checked; in
  // parallel with the start of the command, which takes longer than the reading before it can reach the GPU
  poolsBefore = readStartPools();
  child.on('error', (error) => {
    process.stderr.write(`[gpu-lock] could not start the command: ${error.message}\n`);
    releaseLock();
    process.exit(1);
  });
  child.on('close', (code) => {
    reportPools(code).then(() => {
      releaseLock();
      process.exit(code === null ? 1 : code);
    });
  });
}

const startedAt = Date.now();
const timeoutMs = options.timeoutMinutes * 60 * 1000;
let lastNotice = -Infinity;

function attempt() {
  if (tryAcquire() || (takeOverStale() && tryAcquire())) {
    runCommand();
    return;
  }
  const waitedMs = Date.now() - startedAt;
  if (timeoutMs > 0 && waitedMs >= timeoutMs) {
    process.stderr.write(
      `[gpu-lock] gave up after ${options.timeoutMinutes} minutes; ${lockPath} is held by:\n${readOwner(lockPath)}\n` +
        '[gpu-lock] if that process is gone, remove the directory by hand.\n',
    );
    process.exit(3);
  }
  if (waitedMs - lastNotice >= 300000 || lastNotice === -Infinity) {
    lastNotice = waitedMs;
    process.stderr.write(
      `[gpu-lock] waiting for ${lockPath} (${Math.round(waitedMs / 1000)}s), held by: ${readOwner(lockPath)}\n`,
    );
  }
  setTimeout(attempt, options.pollSeconds * 1000);
}

/** Polls until `check` returns a value other than null, or fails after `timeoutMs`. */
async function waitFor(what, timeoutMs, check) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const value = check();
    if (value !== null) {
      return value;
    }
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for ${what}`);
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

/**
 * Starts this script on a temporary lock with a kernel pool reading that takes 30 s, kills it hard together with its
 * command while it reads, and checks that a second run takes the lock over and runs its command.
 */
async function selfTest() {
  const assert = require('assert');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gpu-lock-self-test-'));
  const testLock = path.join(dir, 'lock');
  const delayVariable = faultWatch === null ? 'GSHARP_GPU_POOL_WATCH_TEST_DELAY_MS' : faultWatch.DelayVariable;
  const first = spawn(
    process.execPath,
    [__filename, '--lock-path', testLock, '--', 'node -e "setTimeout(function () {}, 60000)"'],
    {
      stdio: 'ignore',
      windowsHide: true,
      env: { ...process.env, [delayVariable]: '30000', GSHARP_GPU_POOL_WATCH: '1' },
    },
  );
  try {
    const record = await waitFor('the command in owner.json', 20000, () => {
      const owner = readOwnerRecord(testLock);
      return owner !== null && owner.commandProcess !== null ? owner : null;
    });
    assert.strictEqual(record.pid, first.pid);
    // Hard kill of the owner and its command while the owner is inside the start reading
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(first.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
    } else {
      process.kill(-record.commandProcess.pgid, 'SIGKILL');
      process.kill(first.pid, 'SIGKILL');
    }
    await waitFor('the killed owner to exit', 10000, () =>
      first.exitCode !== null || first.signalCode !== null ? true : null,
    );
    assert.ok(fs.existsSync(path.join(testLock, 'owner.json')), 'the killed run left its owner record');

    const second = spawnSync(
      process.execPath,
      [
        __filename,
        '--lock-path',
        testLock,
        '--poll-seconds',
        '1',
        '--timeout-minutes',
        '1',
        '--',
        'node -e "process.exit(5)"',
      ],
      { encoding: 'utf8', windowsHide: true, env: { ...process.env, GSHARP_GPU_POOL_WATCH: '0' } },
    );
    assert.strictEqual(second.status, 5, second.stderr);
    assert.match(second.stderr, /took over a stale lock/);
    assert.ok(!fs.existsSync(testLock), 'the second run released the lock');
  } finally {
    if (first.exitCode === null && first.signalCode === null) {
      first.kill('SIGKILL');
    }
    fs.rmSync(dir, { recursive: true, force: true });
  }
  process.stdout.write('gpu-lock self-test passed\n');
}

if (options.selfTest) {
  selfTest().then(
    () => process.exit(0),
    (error) => {
      process.stderr.write(`gpu-lock self-test failed: ${error.stack || error.message}\n`);
      process.exit(1);
    },
  );
} else {
  attempt();
}
