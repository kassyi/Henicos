'use strict';

// The lock directory of gpu-lock.js: taking it, the owner record, the takeover of a stale lock and the release.
//
// The script creates the directory (mkdir is atomic), writes owner.txt (pid, time, command) and owner.json (pid, host,
// platform, start time, command, and once started the command's process ids) into it, and removes the directory
// afterwards. It removes only a lock that it created.
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
// Usage (from gpu-lock.js):
//   const lock = createLock(lockPath, commandText);
//   lock.tryAcquire() || (lock.takeOverStale() && lock.tryAcquire()); lock.recordCommandProcess(child, spawnedAt);
//   lock.releaseLock();

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawnSync } = require('child_process');

function readOwner(lockPath) {
  try {
    return fs.readFileSync(path.join(lockPath, 'owner.txt'), 'utf8').trim();
  } catch (error) {
    return `(owner.txt could not be read: ${error.code || error.message})`;
  }
}

/** The owner record that takeover decisions read; owner.txt stays the human-readable form. */
function readOwnerRecord(dir) {
  try {
    const record = JSON.parse(fs.readFileSync(path.join(dir, 'owner.json'), 'utf8'));
    if (
      record &&
      Number.isInteger(record.pid) &&
      record.pid > 0 &&
      typeof record.host === 'string' &&
      typeof record.platform === 'string'
    ) {
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

/** The lock directory `lockPath`, taken for `commandText`; it keeps whether this process holds it. */
function createLock(lockPath, commandText) {
  let ownsLock = false;
  let ownerRecord = null;

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
        process.stderr.write(`[gpu-lock] could not restore ${lockPath} from ${graveyard}: ${error.message}\n`);
      }
      return false;
    }
    fs.rmSync(graveyard, { recursive: true, force: true });
    process.stderr.write(
      `[gpu-lock] took over a stale lock: owner pid ${record.pid} on ${record.host} (started ${record.startedAt}) ` +
        `and its command are gone; the command was: ${record.command}\n`,
    );
    return true;
  }

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

  /** Adds the started command's ids to owner.json, so that a waiter can tell whether it still runs. */
  function recordCommandProcess(child, spawnedAt) {
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

  return { tryAcquire, takeOverStale, releaseLock, recordCommandProcess };
}

module.exports = { createLock, readOwner, readOwnerRecord };
