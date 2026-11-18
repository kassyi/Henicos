'use strict';

// Process liveness checks and Windows process table queries.

const { spawnSync } = require('node:child_process');

/** Slack for comparing a process creation time with the recorded spawn time. */
const SpawnToleranceMs = 5000;

/** PowerShell query returning "pid ppid creationMs" per process. */
const WindowsProcessQuery =
  'Get-CimInstance Win32_Process | ForEach-Object { $c = -1; ' +
  'if ($_.CreationDate) { $c = ([DateTimeOffset]$_.CreationDate).ToUnixTimeMilliseconds() }; ' +
  '"$($_.ProcessId) $($_.ParentProcessId) $c" }';

/**
 * True only when kill(target, 0) reports that no such process (or process group) exists;
 * EPERM means the process exists but is not permitted to signal.
 * @param {number} target - PID or negative PGID
 * @returns {boolean}
 */
function isGone(target) {
  try {
    process.kill(target, 0);
    return false;
  } catch (error) {
    return error.code === 'ESRCH';
  }
}

/**
 * Parses the whitespace-separated output of WindowsProcessQuery into a Map of ProcessEntry objects.
 * @param {string} text
 * @returns {Map<number, { ppid: number, created: number }>}
 */
function parseProcessTable(text) {
  const processes = new Map();
  for (const line of text.split(/\r?\n/)) {
    const fields = line.trim().split(' ');
    if (fields.length === 3) {
      const [pid, ppid, created] = fields.map(Number);
      if (Number.isInteger(pid) && Number.isInteger(ppid) && Number.isFinite(created)) {
        processes.set(pid, { ppid, created });
      }
    }
  }
  return processes;
}

/**
 * Synchronously reads every process as pid -> { ppid, created } through PowerShell.
 * Returns null when the query fails or looks incomplete (it must list this process),
 * so that callers treat commands as alive.
 * @returns {Map<number, { ppid: number, created: number }> | null}
 */
function queryWindowsProcessesSync() {
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', WindowsProcessQuery], {
    encoding: 'utf8',
    windowsHide: true,
    timeout: 60000,
  });
  if (result.error || result.status !== 0) {
    return null;
  }
  const processes = parseProcessTable(result.stdout);
  return processes.has(process.pid) ? processes : null;
}

/**
 * The live processes that belong to a command started as `pid` at `spawnedAt`:
 * the shell itself and its direct children.
 * Creation times exclude processes that only reuse the pid after the shell died.
 * @param {ReadonlyMap<number, { ppid: number, created: number }>} table
 * @param {number} pid
 * @param {number} spawnedAt
 * @returns {number[]}
 */
function commandProcesses(table, pid, spawnedAt) {
  const earliest = spawnedAt - SpawnToleranceMs;
  const latest = spawnedAt + SpawnToleranceMs;
  const found = [];
  let reusedFrom = Infinity;
  const shell = table.get(pid);
  if (shell !== undefined) {
    if (shell.created < 0 || shell.created <= latest) {
      found.push(pid);
    } else {
      reusedFrom = shell.created;
    }
  }
  for (const [child, info] of table) {
    if (
      child !== pid &&
      info.ppid === pid &&
      (info.created < 0 || (info.created >= earliest && info.created < reusedFrom))
    ) {
      found.push(child);
    }
  }
  return found;
}

module.exports = {
  SpawnToleranceMs,
  WindowsProcessQuery,
  isGone,
  parseProcessTable,
  queryWindowsProcessesSync,
  commandProcesses,
};
