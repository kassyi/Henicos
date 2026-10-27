#!/usr/bin/env node
'use strict';

// Runs one command while holding the shared GPU test lock, so that only one agent or worktree uses the GPU at a time.
//
// The lock is the directory <main checkout>/tmp/gpu-test-lock, the same for every worktree: the main checkout is the
// parent of `git rev-parse --git-common-dir`. Without git, the repository root found from this script is used.
// The script creates the directory (mkdir is atomic), writes owner.txt (pid, time, command) into it, runs the command
// and removes the directory afterwards. It removes only a lock that it created, also on Ctrl+C (SIGINT / SIGTERM).
//
// Usage:
//   node scripts/agent/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] -- <command> [args...]
//   node scripts/agent/gpu-lock.js --help
//
// The command runs through the shell, so quote it as one string when it needs pipes or redirection:
//   node scripts/agent/gpu-lock.js -- "dotnet test X.csproj --filter Foo > tmp/log.txt 2>&1"
//
// Options:
//   --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).
//   --timeout-minutes <n>  Give up waiting after n minutes, print the owner and exit with 3 (default 0 = wait forever).
//   --help                 Show this help.
//
// Exit codes: the exit code of the command; 2 on a usage error; 3 when the wait timed out; 130 / 143 on SIGINT / SIGTERM.

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const usage = [
  'Usage: node scripts/agent/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] -- <command> [args...]',
  '',
  '  --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).',
  '  --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
  '  --help                 Show this help.',
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
  const options = { pollSeconds: 10, timeoutMinutes: 0, command };
  for (let i = 0; i < own.length; i++) {
    switch (own[i]) {
      case '--help':
      case '-h':
        process.stdout.write(`${usage}\n`);
        process.exit(0);
        break;
      case '--poll-seconds':
        options.pollSeconds = parseNumber('--poll-seconds', own[++i]);
        break;
      case '--timeout-minutes':
        options.timeoutMinutes = parseNumber('--timeout-minutes', own[++i]);
        break;
      default:
        usageError(`Unknown option: ${own[i]}`);
    }
  }
  if (options.pollSeconds <= 0) {
    usageError('--poll-seconds must be greater than 0.');
  }
  if (command.length === 0) {
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
const lockPath = resolveLockPath();
const commandText = options.command.join(' ');

let ownsLock = false;
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
  fs.writeFileSync(
    path.join(lockPath, 'owner.txt'),
    `pid ${process.pid} ${new Date().toISOString()} cwd ${process.cwd()} ${commandText}\n`,
  );
  return true;
}

function killChild() {
  if (child === null || child.exitCode !== null || child.pid === undefined) {
    return;
  }
  if (process.platform === 'win32') {
    // Only the process tree that this script started.
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
  } else {
    child.kill('SIGTERM');
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
process.on('exit', releaseLock);

function runCommand() {
  child = spawn(commandText, { stdio: 'inherit', shell: true });
  child.on('error', (error) => {
    process.stderr.write(`[gpu-lock] could not start the command: ${error.message}\n`);
    releaseLock();
    process.exit(1);
  });
  child.on('close', (code) => {
    releaseLock();
    process.exit(code === null ? 1 : code);
  });
}

const startedAt = Date.now();
const timeoutMs = options.timeoutMinutes * 60 * 1000;
let lastNotice = -Infinity;

function attempt() {
  if (tryAcquire()) {
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

attempt();
