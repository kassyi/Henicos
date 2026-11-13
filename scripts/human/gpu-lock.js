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
// Modules:
//   gpu-lock/lock.js   the lock directory: acquire, owner record, takeover of a stale lock (when both its owner and the
//                      command it started are gone), release
//   gpu-lock/pools.js  the kernel pool readings while the command starts and before the lock is released
//                      (gpu-fault-watch.js, appended to gpu-pool-log.tsv next to the lock directory)
//
// Usage:
//   node scripts/human/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] [--lock-path <dir>]
//                                  -- <command> [args...]
//   node scripts/human/gpu-lock.js --help
//   node scripts/human/gpu-lock.js --self-test
//
// The command runs through the shell, so quote it as one string when it needs pipes or redirection:
//   node scripts/human/gpu-lock.js -- "dotnet test X.csproj --filter Foo > tmp/log.txt 2>&1"
//
// Options:
//   --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).
//   --timeout-minutes <n>  Give up waiting after n minutes, print the owner and exit with 3 (default 0 = wait forever).
//   --lock-path <dir>      Use this lock directory instead of <main checkout>/tmp/gpu-test-lock. For tests of this
//                          script only; real runs must share the default.
//   --help                 Show this help.
//
// Exit codes: the exit code of the command; 2 on a usage error; 3 when the wait timed out; 130 / 143 on SIGINT /
// SIGTERM.

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn, spawnSync } = require('child_process');
const { faultWatch, createPoolWatch } = require('./gpu-lock/pools.js');
const { createLock, readOwner, readOwnerRecord } = require('./gpu-lock/lock.js');

const usage = [
  'Usage: node scripts/human/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] [--lock-path <dir>] ' +
    '-- <command> [args...]',
  '',
  '  --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).',
  '  --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
  '  --lock-path <dir>      Lock directory to use instead of <main checkout>/tmp/gpu-test-lock ' +
    '(for tests of this script only).',
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

const options = parseArgs(process.argv.slice(2));
// GSHARP_GPU_LOCK_PATH: the same as --lock-path, for a test whose command reaches this script indirectly (the job
// queue's self-test); real runs share the default
const lockOverride = options.lockPath || process.env.GSHARP_GPU_LOCK_PATH || '';
const lockPath = lockOverride === '' ? resolveLockPath() : path.resolve(lockOverride);
const commandText = options.command.join(' ');
const lock = createLock(lockPath, commandText);
const pools = createPoolWatch(lockPath, commandText);

let child = null;

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
  lock.releaseLock();
  process.exit(code);
}

process.on('SIGINT', () => onSignal('SIGINT', 130));
process.on('SIGTERM', () => onSignal('SIGTERM', 143));
// A detached command no longer gets the terminal's hangup itself.
process.on('SIGHUP', () => onSignal('SIGHUP', 129));
process.on('exit', lock.releaseLock);

function runCommand() {
  const spawnedAt = Date.now();
  // Unix: detached makes the command the leader of a new session and process group (pgid = its pid), so the group
  // outlives a hard kill of this script and a waiter can see that the command still runs. Windows keeps the console.
  // GSHARP_GPU_LOCK_HELD tells the command that it holds the lock: job-queue.js run --gpu inside it (a just recipe
  // that long-checks.js runs under this lock) asks for its CPU slot without taking the lock a second time
  const env = { ...process.env, GSHARP_GPU_LOCK_HELD: lockPath };
  child = spawn(commandText, { stdio: 'inherit', shell: true, env, detached: process.platform !== 'win32' });
  lock.recordCommandProcess(child, spawnedAt);
  // After the record, so that a hard kill during the reading leaves a lock whose owner and command can be checked; in
  // parallel with the start of the command, which takes longer than the reading before it can reach the GPU
  pools.start();
  child.on('error', (error) => {
    process.stderr.write(`[gpu-lock] could not start the command: ${error.message}\n`);
    lock.releaseLock();
    process.exit(1);
  });
  child.on('close', (code) => {
    pools.report(code).then(() => {
      lock.releaseLock();
      process.exit(code === null ? 1 : code);
    });
  });
}

const startedAt = Date.now();
const timeoutMs = options.timeoutMinutes * 60 * 1000;
let lastNotice = -Infinity;

function attempt() {
  if (lock.tryAcquire() || (lock.takeOverStale() && lock.tryAcquire())) {
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
