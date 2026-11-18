'use strict';

// One-or-two-seat atomic directory lock: acquiring, owner records, per-seat stale takeover, and release.
//
// Each seat is a directory created atomically with mkdirSync, holding owner.json (machine-readable owner state) and
// owner.txt (human-readable diagnostic summary). Exclusive callers must hold all configured seats (acquiring seat 0
// first and releasing it immediately if seat 1 cannot be taken). Shared callers hold any single seat when no
// exclusive owner (including legacy single-seat owners without a `mode` field) is present on the other seat.

const assert = require('node:assert');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { isGone, queryWindowsProcessesSync, commandProcesses } = require('./process-liveness.js');

let graveyardCounter = 0;
const TakeoverGraceMs = 2000;
const RmRetryOptions = { recursive: true, force: true, maxRetries: 5, retryDelay: 20 };

/**
 * Normalizes a seat path for duplicate comparison (case-insensitive on Windows).
 * @param {string} seatPath
 * @returns {string}
 */
function seatPathKey(seatPath) {
  const resolved = path.resolve(seatPath);
  return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
}

/**
 * Atomically vacates a lock or mutex directory before removing it with retries.
 * @param {string} dirPath
 * @param {string} prefix
 */
function releaseDirectory(dirPath, prefix) {
  const releasingPath = `${dirPath}.releasing-${process.pid}-${Date.now()}-${graveyardCounter++}`;
  let targetToRemove = dirPath;
  try {
    fs.renameSync(dirPath, releasingPath);
    targetToRemove = releasingPath;
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return;
    }
  }
  try {
    fs.rmSync(targetToRemove, RmRetryOptions);
  } catch (error) {
    process.stderr.write(`${prefix} could not remove ${targetToRemove}: ${error.message}\n`);
  }
}

/**
 * True when mkdirSync failed on Windows due to NTFS STATUS_DELETE_PENDING contention rather than a read-only parent.
 * @param {unknown} error
 * @param {string} seatPath
 * @returns {boolean}
 */
function isWindowsContendedError(error, seatPath) {
  const code = error && typeof error === 'object' ? /** @type {{ code?: unknown }} */ (error).code : undefined;
  if (process.platform !== 'win32' || (code !== 'EPERM' && code !== 'EACCES')) {
    return false;
  }
  try {
    fs.lstatSync(seatPath);
    return true;
  } catch (statError) {
    const statCode =
      statError && typeof statError === 'object' ? /** @type {{ code?: unknown }} */ (statError).code : undefined;
    if (statCode === 'EPERM' || statCode === 'EACCES') {
      return true;
    }
    if (statCode === 'ENOENT') {
      const probePath = `${seatPath}.probe-${process.pid}-${Date.now()}-${graveyardCounter++}`;
      try {
        fs.mkdirSync(probePath);
        try {
          fs.rmSync(probePath, RmRetryOptions);
        } catch {
          // Best-effort cleanup of probe directory.
        }
        return true;
      } catch {
        return false;
      }
    }
    return false;
  }
}

/**
 * Reads the human-readable owner.txt summary from a seat directory.
 * @param {string} seatPath
 * @returns {string}
 */
function readSeatOwnerText(seatPath) {
  try {
    return fs.readFileSync(path.join(seatPath, 'owner.txt'), 'utf8').trim();
  } catch (error) {
    return `(owner.txt could not be read: ${error.code || error.message})`;
  }
}

/**
 * Formats owner diagnostics for every currently existing seat directory.
 * @param {readonly string[]} seatPaths
 * @returns {string}
 */
function formatSeatOwners(seatPaths) {
  const lines = [];
  for (let i = 0; i < seatPaths.length; i++) {
    if (fs.existsSync(seatPaths[i])) {
      lines.push(`seat ${i} (${seatPaths[i]}): ${readSeatOwnerText(seatPaths[i])}`);
    }
  }
  return lines.length > 0 ? lines.join('\n') : '(no seat directory currently exists)';
}

/**
 * Reads and validates owner.json from a seat directory without mutating legacy fields (such as missing `mode`).
 * Returns null when the file is missing, unreadable, or fails structural validation.
 * @param {string} seatPath
 * @returns {Record<string, unknown> | null}
 */
function readSeatOwner(seatPath) {
  try {
    const record = JSON.parse(fs.readFileSync(path.join(seatPath, 'owner.json'), 'utf8'));
    if (
      record &&
      typeof record === 'object' &&
      !Array.isArray(record) &&
      Number.isInteger(record.pid) &&
      record.pid > 0 &&
      typeof record.host === 'string' &&
      typeof record.platform === 'string'
    ) {
      return record;
    }
  } catch {
    // Missing or half-written record: callers treat an existing directory as occupied by an exclusive owner.
  }
  return null;
}

/**
 * Resolves the effective lock mode of a parsed owner record.
 * Missing or unrecognized modes are treated as `'exclusive'` for backward compatibility with single-seat holders.
 * @param {Record<string, unknown> | null} record
 * @returns {'shared' | 'exclusive'}
 */
function seatOwnerMode(record) {
  return record !== null && record.mode === 'shared' ? 'shared' : 'exclusive';
}

/**
 * True when the Windows shell recorded in `command` or a live child of it may still run.
 * @param {{ pid: number, spawnedAt: number }} command
 * @returns {boolean}
 */
function isWindowsCommandAlive(command) {
  const processes = queryWindowsProcessesSync();
  if (processes === null) {
    return true;
  }
  return commandProcesses(processes, command.pid, command.spawnedAt).length > 0;
}

/**
 * True when the command started by the lock owner (recorded in owner.json) is known to have ended.
 * @param {Record<string, unknown>} record
 * @returns {boolean}
 */
function isCommandGone(record) {
  const command = record.commandProcess;
  if (command === null || typeof command !== 'object' || !Number.isInteger(command.pid) || command.pid <= 0) {
    return false;
  }
  if (process.platform === 'win32') {
    return Number.isFinite(command.spawnedAt) && !isWindowsCommandAlive(command);
  }
  return Number.isInteger(command.pgid) && command.pgid > 1 && isGone(-command.pgid);
}

/**
 * True when the owner is on this host and platform, its pid no longer exists, and either it died before spawning
 * (`stage === 'acquiring'`) or the command it started is also gone. Anything uncertain counts as alive.
 * @param {Record<string, unknown> | null} record
 * @returns {boolean}
 */
function isDeadLocalOwner(record) {
  if (record === null || record.host !== os.hostname() || record.platform !== process.platform) {
    return false;
  }
  return isGone(record.pid) && (record.stage === 'acquiring' || isCommandGone(record));
}

/**
 * Inspects a seat directory for existence, owner record, and effective mode.
 * @param {string} seatPath
 * @returns {{ exists: boolean, record: Record<string, unknown> | null, mode: 'shared' | 'exclusive' | null }}
 */
function inspectSeat(seatPath) {
  try {
    fs.statSync(seatPath);
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return { exists: false, record: null, mode: null };
    }
    if (process.platform === 'win32' && error && (error.code === 'EPERM' || error.code === 'EACCES')) {
      return { exists: true, record: null, mode: 'exclusive' };
    }
    throw error;
  }
  const record = readSeatOwner(seatPath);
  return { exists: true, record, mode: seatOwnerMode(record) };
}

/**
 * Reads lstatSync(..., { bigint: true }) or returns null on ENOENT/EPERM/EACCES.
 * @param {string} targetPath
 * @returns {import('node:fs').BigIntStats | null}
 */
function statDirBigInt(targetPath) {
  try {
    return fs.lstatSync(targetPath, { bigint: true });
  } catch (error) {
    const code = error && typeof error === 'object' ? /** @type {{ code?: unknown }} */ (error).code : undefined;
    if (code === 'ENOENT' || code === 'EPERM' || code === 'EACCES') {
      return null;
    }
    throw error;
  }
}

/**
 * Re-reads owner.json and lstatSync to confirm the directory identity and owner match before renaming/removing.
 * @param {string} dirPath
 * @param {import('node:fs').BigIntStats} beforeStat
 * @param {Record<string, unknown> | null} expectedOwner
 * @returns {boolean}
 */
function confirmSameDirOwner(dirPath, beforeStat, expectedOwner) {
  const confirm = readSeatOwner(dirPath);
  const cur = statDirBigInt(dirPath);
  if (cur === null || cur.ino !== beforeStat.ino || cur.birthtimeNs !== beforeStat.birthtimeNs) {
    return false;
  }
  return expectedOwner === null
    ? confirm === null
    : confirm !== null && confirm.pid === expectedOwner.pid && confirm.startedAt === expectedOwner.startedAt;
}

/**
 * Removes an orphaned .takeover mutex directory when its owner is dead or it lacks owner.json past TakeoverGraceMs.
 * @param {string} takeoverPath
 * @param {string} prefix
 * @returns {boolean}
 */
function cleanStaleTakeoverDir(takeoverPath, prefix) {
  const cleanPath = `${takeoverPath}.clean`;
  try {
    fs.mkdirSync(cleanPath);
  } catch (error) {
    if ((error && error.code === 'EEXIST') || isWindowsContendedError(error, cleanPath)) {
      const cleanStat = statDirBigInt(cleanPath);
      if (cleanStat !== null && Date.now() - Number(cleanStat.mtimeMs) >= TakeoverGraceMs) {
        releaseDirectory(cleanPath, prefix);
      }
      return false;
    }
    throw error;
  }
  try {
    const beforeStat = statDirBigInt(takeoverPath);
    if (beforeStat === null) {
      return false;
    }
    const owner = readSeatOwner(takeoverPath);
    const isDeadOwner =
      owner !== null && owner.host === os.hostname() && owner.platform === process.platform && isGone(owner.pid);
    const isExpiredEmpty = owner === null && Date.now() - Number(beforeStat.mtimeMs) >= TakeoverGraceMs;
    const targetOwner = isDeadOwner ? owner : null;
    if ((!isDeadOwner && !isExpiredEmpty) || !confirmSameDirOwner(takeoverPath, beforeStat, targetOwner)) {
      return false;
    }
    releaseDirectory(takeoverPath, prefix);
    return true;
  } finally {
    releaseDirectory(cleanPath, prefix);
  }
}

/**
 * Moves a stale seat directory away atomically under a per-seat .takeover mutex if it still holds the dead owner.
 * @param {string} seatPath
 * @param {string} prefix
 * @returns {boolean}
 */
function takeOverStaleSeat(seatPath, prefix) {
  const targetPath = path.resolve(seatPath);
  if (!isDeadLocalOwner(readSeatOwner(targetPath))) {
    return false;
  }
  const takeoverPath = `${targetPath}.takeover`;
  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      fs.mkdirSync(takeoverPath);
      break;
    } catch (error) {
      if ((error && error.code === 'EEXIST') || isWindowsContendedError(error, takeoverPath)) {
        if (attempt === 0 && cleanStaleTakeoverDir(takeoverPath, prefix)) {
          continue;
        }
        return false;
      }
      throw error;
    }
  }
  try {
    try {
      const mutexOwner = {
        pid: process.pid,
        host: os.hostname(),
        platform: process.platform,
        startedAt: new Date().toISOString(),
      };
      fs.writeFileSync(path.join(takeoverPath, 'owner.json'), JSON.stringify(mutexOwner));
    } catch {
      return false;
    }
    const record = readSeatOwner(targetPath);
    const beforeStat = statDirBigInt(targetPath);
    if (beforeStat === null || !isDeadLocalOwner(record) || !confirmSameDirOwner(targetPath, beforeStat, record)) {
      return false;
    }
    const graveyard = `${targetPath}.stale-${process.pid}-${Date.now()}-${graveyardCounter++}`;
    try {
      fs.renameSync(targetPath, graveyard);
    } catch {
      return false;
    }
    const moved = readSeatOwner(graveyard);
    if (moved === null || moved.pid !== record.pid || moved.startedAt !== record.startedAt) {
      try {
        fs.renameSync(graveyard, targetPath);
      } catch (error) {
        process.stderr.write(`${prefix} could not restore ${targetPath} from ${graveyard}: ${error.message}\n`);
      }
      return false;
    }
    try {
      fs.rmSync(graveyard, RmRetryOptions);
    } catch (error) {
      process.stderr.write(`${prefix} could not remove ${graveyard}: ${error.message}\n`);
    }
    process.stderr.write(
      `${prefix} took over a stale lock: owner pid ${record.pid} on ${record.host} (started ${record.startedAt}) ` +
        `and its command are gone; the command was: ${record.command}\n`,
    );
    return true;
  } finally {
    releaseDirectory(takeoverPath, prefix);
  }
}

/**
 * Creates a seat lock controller for 1 or 2 seat directories.
 * @param {readonly string[]} seatPaths
 * @param {string} commandText
 * @param {{
 *   mode?: 'exclusive' | 'shared',
 *   turn?: number,
 *   prefix?: string,
 *   onSeatAcquired?: (seatIndex: number, seatPath: string) => void,
 * }} [options]
 */
function createSeatLock(seatPaths, commandText, options = {}) {
  if (!Array.isArray(seatPaths) || (seatPaths.length !== 1 && seatPaths.length !== 2)) {
    throw new Error('seatPaths must be an array of 1 or 2 seat directories.');
  }
  for (const seatPath of seatPaths) {
    if (typeof seatPath !== 'string' || seatPath.trim() === '') {
      throw new Error('Each seat path must be a non-empty string.');
    }
  }
  const resolvedSeatPaths = seatPaths.map((p) => path.resolve(p));
  if (resolvedSeatPaths.length === 2 && seatPathKey(resolvedSeatPaths[0]) === seatPathKey(resolvedSeatPaths[1])) {
    throw new Error('Seat 0 and seat 1 must resolve to distinct directories.');
  }
  const opts = options === undefined || options === null ? {} : options;
  if (typeof opts !== 'object' || Array.isArray(opts)) {
    throw new Error('Lock options must be an object.');
  }
  const normalizedCommand = typeof commandText === 'string' ? commandText : String(commandText || '');
  const mode = opts.mode === undefined ? 'exclusive' : opts.mode;
  if (mode !== 'exclusive' && mode !== 'shared') {
    throw new Error(`Invalid lock mode: ${String(mode)}`);
  }
  const turn = Number.isInteger(opts.turn) && opts.turn > 0 ? opts.turn : 0;
  const prefix = typeof opts.prefix === 'string' && opts.prefix !== '' ? opts.prefix : '[seat-lock]';
  const onSeatAcquired = typeof opts.onSeatAcquired === 'function' ? opts.onSeatAcquired : null;

  /** @type {Map<number, { path: string, record: Record<string, unknown> }>} */
  const ownedSeats = new Map();

  function releaseSeat(seatIndex) {
    const entry = ownedSeats.get(seatIndex);
    if (entry === undefined) {
      return;
    }
    ownedSeats.delete(seatIndex);
    releaseDirectory(entry.path, prefix);
  }

  function acquireSeat(seatIndex) {
    const seatPath = resolvedSeatPaths[seatIndex];
    fs.mkdirSync(path.dirname(seatPath), { recursive: true });
    try {
      fs.mkdirSync(seatPath);
    } catch (error) {
      if ((error && error.code === 'EEXIST') || isWindowsContendedError(error, seatPath)) {
        return false;
      }
      throw error;
    }
    const nowIso = new Date().toISOString();
    const record = {
      pid: process.pid,
      host: os.hostname(),
      platform: process.platform,
      startedAt: nowIso,
      cwd: process.cwd(),
      command: normalizedCommand,
      commandProcess: null,
      mode,
      stage: 'acquiring',
      turn,
      seat: seatIndex,
    };
    ownedSeats.set(seatIndex, { path: seatPath, record });
    try {
      fs.writeFileSync(path.join(seatPath, 'owner.json'), JSON.stringify(record));
      const turnPart = turn > 0 ? ` turn ${turn}` : '';
      fs.writeFileSync(
        path.join(seatPath, 'owner.txt'),
        `pid ${process.pid} ${nowIso} mode ${mode} seat ${seatIndex}${turnPart} ` +
          `cwd ${process.cwd()} ${normalizedCommand}\n`,
      );
      return true;
    } catch (error) {
      releaseSeat(seatIndex);
      throw error;
    }
  }

  function trySharedRound() {
    for (let k = 0; k < 2; k++) {
      const other = 1 - k;
      const otherInfo = inspectSeat(resolvedSeatPaths[other]);
      if (otherInfo.exists && otherInfo.mode !== 'shared') {
        return { acquired: false, seat: null, seatsHeld: [] };
      }
      if (!acquireSeat(k)) {
        continue;
      }
      try {
        if (onSeatAcquired !== null) {
          onSeatAcquired(k, resolvedSeatPaths[k]);
        }
        const recheck = inspectSeat(resolvedSeatPaths[other]);
        if (recheck.exists && recheck.mode !== 'shared') {
          releaseSeat(k);
          return { acquired: false, seat: null, seatsHeld: [] };
        }
        return { acquired: true, seat: k, seatsHeld: [resolvedSeatPaths[k]] };
      } catch (error) {
        releaseSeat(k);
        throw error;
      }
    }
    return { acquired: false, seat: null, seatsHeld: [] };
  }

  /**
   * Attempts one acquisition pass (with per-seat stale takeover on conflict).
   * @returns {{ acquired: boolean, staleTaken: boolean, seat: number | null, seatsHeld: string[] }}
   */
  function tryAcquire() {
    if (ownedSeats.size > 0) {
      return { acquired: false, staleTaken: false, seat: null, seatsHeld: [] };
    }
    if (resolvedSeatPaths.length === 1) {
      let staleTaken = false;
      let acquired = acquireSeat(0);
      if (!acquired && takeOverStaleSeat(resolvedSeatPaths[0], prefix)) {
        staleTaken = true;
        acquired = acquireSeat(0);
      }
      return acquired
        ? { acquired: true, staleTaken, seat: 0, seatsHeld: [resolvedSeatPaths[0]] }
        : { acquired: false, staleTaken, seat: null, seatsHeld: [] };
    }

    if (mode === 'exclusive') {
      let staleTaken = false;
      let gotSeat0 = acquireSeat(0);
      if (!gotSeat0 && takeOverStaleSeat(resolvedSeatPaths[0], prefix)) {
        staleTaken = true;
        gotSeat0 = acquireSeat(0);
      }
      if (!gotSeat0) {
        return { acquired: false, staleTaken, seat: null, seatsHeld: [] };
      }
      try {
        let gotSeat1 = acquireSeat(1);
        if (!gotSeat1 && takeOverStaleSeat(resolvedSeatPaths[1], prefix)) {
          staleTaken = true;
          gotSeat1 = acquireSeat(1);
        }
        if (!gotSeat1) {
          releaseSeat(0);
          return { acquired: false, staleTaken, seat: null, seatsHeld: [] };
        }
        return { acquired: true, staleTaken, seat: 0, seatsHeld: [resolvedSeatPaths[0], resolvedSeatPaths[1]] };
      } catch (error) {
        releaseSeat(0);
        throw error;
      }
    }

    let round = trySharedRound();
    if (round.acquired) {
      return { acquired: true, staleTaken: false, seat: round.seat, seatsHeld: round.seatsHeld };
    }
    const stale0 = takeOverStaleSeat(resolvedSeatPaths[0], prefix);
    const stale1 = takeOverStaleSeat(resolvedSeatPaths[1], prefix);
    const staleTaken = stale0 || stale1;
    if (staleTaken) {
      round = trySharedRound();
    }
    return { acquired: round.acquired, staleTaken, seat: round.seat, seatsHeld: round.seatsHeld };
  }

  /**
   * Checks all configured seats and removes any stale locks.
   * @returns {boolean}
   */
  function takeOverStale() {
    let any = false;
    for (const seatPath of resolvedSeatPaths) {
      if (takeOverStaleSeat(seatPath, prefix)) {
        any = true;
      }
    }
    return any;
  }

  /**
   * Transitions all held seats from `'acquiring'` to `'running'` before spawning the command.
   */
  function markRunning() {
    for (const [, entry] of ownedSeats) {
      entry.record.stage = 'running';
      fs.writeFileSync(path.join(entry.path, 'owner.json'), JSON.stringify(entry.record));
    }
  }

  /**
   * Records the spawned command process identifiers into owner.json on all held seats.
   * @param {{ pid?: number }} child
   * @param {number} spawnedAt
   */
  function recordCommandProcess(child, spawnedAt) {
    if (!child || child.pid === undefined) {
      return;
    }
    const commandProcess =
      process.platform === 'win32' ? { pid: child.pid, spawnedAt } : { pid: child.pid, pgid: child.pid };
    for (const [, entry] of ownedSeats) {
      entry.record.commandProcess = commandProcess;
      try {
        fs.writeFileSync(path.join(entry.path, 'owner.json'), JSON.stringify(entry.record));
      } catch (error) {
        process.stderr.write(`${prefix} could not record the command in owner.json: ${error.message}\n`);
      }
    }
  }

  /**
   * Releases all seats created by this instance. Idempotent.
   */
  function releaseLock() {
    for (const seatIndex of [...ownedSeats.keys()]) {
      releaseSeat(seatIndex);
    }
  }

  /**
   * Lists the seats currently held by this lock instance.
   * @returns {Array<{ seatIndex: number, path: string }>}
   */
  function getHeldSeats() {
    return [...ownedSeats.entries()].map(([seatIndex, entry]) => ({ seatIndex, path: entry.path }));
  }

  return {
    tryAcquire,
    takeOverStale,
    markRunning,
    recordCommandProcess,
    releaseLock,
    getHeldSeats,
  };
}

/**
 * Writes a legacy 7-field v1 owner record matching lock.js:156-164 (commit 61dc45fda).
 * @param {string} seatDir
 * @param {string} commandText
 * @param {number} [pid]
 * @returns {Record<string, unknown>}
 */
function writeLegacyV1OwnerFixture(seatDir, commandText, pid = process.pid) {
  fs.mkdirSync(seatDir, { recursive: true });
  const record = {
    pid,
    host: os.hostname(),
    platform: process.platform,
    startedAt: new Date().toISOString(),
    cwd: process.cwd(),
    command: commandText,
    commandProcess: null,
  };
  fs.writeFileSync(path.join(seatDir, 'owner.json'), JSON.stringify(record));
  fs.writeFileSync(
    path.join(seatDir, 'owner.txt'),
    `pid ${pid} ${record.startedAt} cwd ${record.cwd} ${commandText}\n`,
  );
  return record;
}

/**
 * Obtains a deterministic exited PID via spawnSync for stale takeover tests.
 * @returns {number}
 */
function findDeadPid() {
  const res = spawnSync(process.execPath, ['-e', ''], { windowsHide: true });
  assert.ok(Number.isInteger(res.pid) && res.pid > 0, 'spawnSync did not return a child pid');
  assert.ok(isGone(res.pid), `expected exited pid ${res.pid} to be gone`);
  return res.pid;
}

/**
 * Runs Checks #1 through #7 against temporary directories using in-process lock actors.
 */
function selfTest() {
  function withTempSeats(fn) {
    const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'seat-lock-self-test-')));
    const seat0 = path.join(root, 'lock');
    const seat1 = `${seat0}-seat1`;
    try {
      fn(seat0, seat1);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }

  // Check 1: exclusive does not return while seat 1 is held and does not retain seat 0.
  withTempSeats((seat0, seat1) => {
    writeLegacyV1OwnerFixture(seat1, 'node -e "holder"');
    const excl = createSeatLock([seat0, seat1], 'node -e "work"', { mode: 'exclusive' });
    const blocked = excl.tryAcquire();
    assert.strictEqual(blocked.acquired, false);
    assert.strictEqual(fs.existsSync(seat0), false);
    assert.strictEqual(excl.getHeldSeats().length, 0);
    assert.strictEqual(fs.existsSync(seat1), true);

    fs.rmSync(seat1, { recursive: true, force: true });
    const acquired = excl.tryAcquire();
    assert.strictEqual(acquired.acquired, true);
    assert.strictEqual(acquired.seatsHeld.length, 2);
    assert.strictEqual(fs.realpathSync.native(acquired.seatsHeld[0]), fs.realpathSync.native(seat0));
    assert.strictEqual(fs.realpathSync.native(acquired.seatsHeld[1]), fs.realpathSync.native(seat1));
    assert.strictEqual(readSeatOwner(seat0).stage, 'acquiring');
    assert.strictEqual(readSeatOwner(seat1).stage, 'acquiring');
    excl.markRunning();
    assert.strictEqual(readSeatOwner(seat0).stage, 'running');
    assert.strictEqual(readSeatOwner(seat1).stage, 'running');
    excl.releaseLock();
    assert.strictEqual(fs.existsSync(seat0), false);
    assert.strictEqual(fs.existsSync(seat1), false);
  });

  // Check 2: legacy v1 owner on seat 0 (no `mode`) cannot coexist with new exclusive.
  withTempSeats((seat0, seat1) => {
    writeLegacyV1OwnerFixture(seat0, 'node -e "legacy"');
    const excl = createSeatLock([seat0, seat1], 'node -e "work"', { mode: 'exclusive' });
    const res = excl.tryAcquire();
    assert.strictEqual(res.acquired, false);
    assert.strictEqual(fs.existsSync(seat1), false);
    assert.strictEqual(readSeatOwner(seat0).mode, undefined);
    assert.strictEqual(seatOwnerMode(readSeatOwner(seat0)), 'exclusive');
  });

  // Check 3: live owner on seat 0 without `mode` (and half-written/empty dir) blocks shared from taking seat 1.
  withTempSeats((seat0, seat1) => {
    writeLegacyV1OwnerFixture(seat0, 'node -e "legacy"');
    const shared = createSeatLock([seat0, seat1], 'node -e "work"', { mode: 'shared' });
    const res = shared.tryAcquire();
    assert.strictEqual(res.acquired, false);
    assert.strictEqual(fs.existsSync(seat1), false);
    assert.strictEqual(seatOwnerMode(readSeatOwner(seat0)), 'exclusive');

    fs.rmSync(path.join(seat0, 'owner.json'), { force: true });
    const resNoJson = shared.tryAcquire();
    assert.strictEqual(resNoJson.acquired, false);
    assert.strictEqual(fs.existsSync(seat1), false);
  });

  // Check 4: shared releases seat 1 when exclusive appears on seat 0 before post-acquire re-check.
  withTempSeats((seat0, seat1) => {
    const s1 = createSeatLock([seat0, seat1], 'node -e "s1"', { mode: 'shared' });
    assert.strictEqual(s1.tryAcquire().seat, 0);
    const s2 = createSeatLock([seat0, seat1], 'node -e "s2"', {
      mode: 'shared',
      onSeatAcquired(seatIndex) {
        if (seatIndex === 1) {
          s1.releaseLock();
          fs.mkdirSync(seat0, { recursive: true });
          fs.writeFileSync(
            path.join(seat0, 'owner.json'),
            JSON.stringify({
              pid: process.pid,
              host: os.hostname(),
              platform: process.platform,
              startedAt: new Date().toISOString(),
              cwd: process.cwd(),
              command: 'node -e "excl"',
              commandProcess: null,
              mode: 'exclusive',
              stage: 'running',
              turn: 0,
              seat: 0,
            }),
          );
        }
      },
    });
    const res = s2.tryAcquire();
    assert.strictEqual(res.acquired, false);
    assert.strictEqual(fs.existsSync(seat1), false);
    assert.strictEqual(s2.getHeldSeats().length, 0);
    assert.strictEqual(seatOwnerMode(readSeatOwner(seat0)), 'exclusive');
  });

  // Check 5: shared acquires 1 seat, 2nd shared takes remaining seat, 3rd waits; release only removes own seat.
  withTempSeats((seat0, seat1) => {
    const s1 = createSeatLock([seat0, seat1], 'node -e "s1"', { mode: 'shared' });
    const s2 = createSeatLock([seat0, seat1], 'node -e "s2"', { mode: 'shared' });
    const s3 = createSeatLock([seat0, seat1], 'node -e "s3"', { mode: 'shared' });

    const r1 = s1.tryAcquire();
    assert.strictEqual(r1.acquired, true);
    assert.strictEqual(r1.seat, 0);
    assert.strictEqual(r1.seatsHeld.length, 1);
    assert.strictEqual(fs.existsSync(seat0), true);
    assert.strictEqual(fs.existsSync(seat1), false);

    const r2 = s2.tryAcquire();
    assert.strictEqual(r2.acquired, true);
    assert.strictEqual(r2.seat, 1);
    assert.strictEqual(r2.seatsHeld.length, 1);
    assert.strictEqual(fs.existsSync(seat1), true);

    const r3 = s3.tryAcquire();
    assert.strictEqual(r3.acquired, false);
    assert.strictEqual(r3.seatsHeld.length, 0);
    assert.strictEqual(s3.getHeldSeats().length, 0);

    s1.releaseLock();
    assert.strictEqual(fs.existsSync(seat0), false);
    assert.strictEqual(fs.existsSync(seat1), true);

    const r3Retry = s3.tryAcquire();
    assert.strictEqual(r3Retry.acquired, true);
    assert.strictEqual(r3Retry.seat, 0);
    s2.releaseLock();
    s3.releaseLock();
    assert.strictEqual(fs.existsSync(seat0), false);
    assert.strictEqual(fs.existsSync(seat1), false);
  });

  // Check 6: stale dead owner with stage 'acquiring' is taken over; stage 'running' with commandProcess null is not.
  withTempSeats((seat0, seat1) => {
    const deadPid = findDeadPid();
    fs.mkdirSync(seat0, { recursive: true });
    fs.writeFileSync(
      path.join(seat0, 'owner.json'),
      JSON.stringify({
        pid: deadPid,
        host: os.hostname(),
        platform: process.platform,
        startedAt: new Date().toISOString(),
        cwd: process.cwd(),
        command: 'node -e "acquiring"',
        commandProcess: null,
        mode: 'exclusive',
        stage: 'acquiring',
        turn: 0,
        seat: 0,
      }),
    );
    const excl = createSeatLock([seat0, seat1], 'node -e "fresh"', { mode: 'exclusive' });
    const res = excl.tryAcquire();
    assert.strictEqual(res.acquired, true);
    assert.strictEqual(res.staleTaken, true);
    assert.strictEqual(readSeatOwner(seat0).pid, process.pid);
    excl.releaseLock();

    fs.mkdirSync(seat0, { recursive: true });
    fs.writeFileSync(
      path.join(seat0, 'owner.json'),
      JSON.stringify({
        pid: deadPid,
        host: os.hostname(),
        platform: process.platform,
        startedAt: new Date().toISOString(),
        cwd: process.cwd(),
        command: 'node -e "running"',
        commandProcess: null,
        mode: 'exclusive',
        stage: 'running',
        turn: 0,
        seat: 0,
      }),
    );
    const excl2 = createSeatLock([seat0, seat1], 'node -e "blocked"', { mode: 'exclusive' });
    const res2 = excl2.tryAcquire();
    assert.strictEqual(res2.acquired, false);
    assert.strictEqual(res2.staleTaken, false);
    assert.strictEqual(fs.existsSync(seat0), true);
    assert.strictEqual(readSeatOwner(seat0).pid, deadPid);
  });

  // Check 7: stale dead same PID on both seat 0 and seat 1 taken over independently + 1-seat fallback.
  withTempSeats((seat0, seat1) => {
    const deadPid = findDeadPid();
    const sharedStartedAt = new Date().toISOString();
    for (const [idx, seatDir] of [[0, seat0], [1, seat1]]) {
      fs.mkdirSync(seatDir, { recursive: true });
      fs.writeFileSync(
        path.join(seatDir, 'owner.json'),
        JSON.stringify({
          pid: deadPid,
          host: os.hostname(),
          platform: process.platform,
          startedAt: sharedStartedAt,
          cwd: process.cwd(),
          command: 'node -e "both-stale"',
          commandProcess: null,
          mode: 'exclusive',
          stage: 'acquiring',
          turn: 0,
          seat: idx,
        }),
      );
    }
    const excl = createSeatLock([seat0, seat1], 'node -e "take-both"', { mode: 'exclusive' });
    const res = excl.tryAcquire();
    assert.strictEqual(res.acquired, true);
    assert.strictEqual(res.staleTaken, true);
    assert.strictEqual(readSeatOwner(seat0).pid, process.pid);
    assert.strictEqual(readSeatOwner(seat1).pid, process.pid);
    excl.releaseLock();

    const oneShared = createSeatLock([seat0], 'node -e "one-shared"', { mode: 'shared' });
    const oneRes = oneShared.tryAcquire();
    assert.strictEqual(oneRes.acquired, true);
    assert.strictEqual(oneRes.seat, 0);
    assert.strictEqual(oneRes.seatsHeld.length, 1);
    assert.strictEqual(fs.existsSync(seat1), false);

    const oneExcl = createSeatLock([seat0], 'node -e "one-excl"', { mode: 'exclusive' });
    assert.strictEqual(oneExcl.tryAcquire().acquired, false);
    assert.strictEqual(fs.existsSync(seat1), false);
    oneShared.releaseLock();
  });

  // Regression checks: duplicate paths (including case-only on win32), null/invalid options, and re-entrant acquire.
  withTempSeats((seat0, seat1) => {
    const parent = path.dirname(seat0);
    assert.throws(() => createSeatLock([seat0, `${seat0}/`], 'cmd'), /distinct directories/);
    assert.throws(() => createSeatLock([path.join(parent, 'dup'), path.join(parent, '.', 'dup')], 'cmd'), /distinct/);
    if (process.platform === 'win32') {
      assert.throws(
        () => createSeatLock([path.join(parent, 'CaseSeat'), path.join(parent, 'caseseat')], 'cmd'),
        /distinct directories/,
      );
    }

    const nullOptsLock = createSeatLock([seat0], 'node -e "null-opts"', null);
    assert.strictEqual(nullOptsLock.tryAcquire().acquired, true);
    nullOptsLock.releaseLock();
    assert.throws(() => createSeatLock([seat0], 'cmd', 'bad'), /options must be an object/);
    assert.throws(() => createSeatLock([seat0], 'cmd', []), /options must be an object/);

    const s1 = createSeatLock([seat0, seat1], 'node -e "s1"', { mode: 'shared' });
    const first = s1.tryAcquire();
    assert.strictEqual(first.acquired, true);
    assert.strictEqual(first.seat, 0);
    const second = s1.tryAcquire();
    assert.strictEqual(second.acquired, false);
    assert.strictEqual(second.seatsHeld.length, 0);
    assert.strictEqual(s1.getHeldSeats().length, 1);
    assert.strictEqual(fs.existsSync(seat1), false);

    const s2 = createSeatLock([seat0, seat1], 'node -e "s2"', { mode: 'shared' });
    const s2First = s2.tryAcquire();
    assert.strictEqual(s2First.acquired, true);
    assert.strictEqual(s2First.seat, 1);
    s1.releaseLock();
    assert.strictEqual(fs.existsSync(seat0), false);
    assert.strictEqual(fs.existsSync(seat1), true);
    s2.releaseLock();
    assert.strictEqual(fs.existsSync(seat1), false);

    const excl = createSeatLock([seat0, seat1], 'node -e "excl"', { mode: 'exclusive' });
    assert.strictEqual(excl.tryAcquire().acquired, true);
    const exclSecond = excl.tryAcquire();
    assert.strictEqual(exclSecond.acquired, false);
    assert.strictEqual(excl.getHeldSeats().length, 2);
    excl.releaseLock();
    assert.strictEqual(fs.readdirSync(parent).filter((n) => n.includes('.releasing-')).length, 0);
  });

  // Regression checks: .takeover mutex cleanup (dead owner, expired empty grace period, and live-owner blocking).
  withTempSeats((seat0, seat1) => {
    const deadPid = findDeadPid();
    const deadOwner = {
      pid: deadPid, host: os.hostname(), platform: process.platform, startedAt: new Date().toISOString(),
      cwd: process.cwd(), command: 'node -e "dead"', commandProcess: null, mode: 'exclusive',
      stage: 'acquiring', turn: 0, seat: 0,
    };
    const takeoverDir = `${seat0}.takeover`;
    const seedDeadSeat = () => {
      fs.mkdirSync(seat0, { recursive: true });
      fs.writeFileSync(path.join(seat0, 'owner.json'), JSON.stringify(deadOwner));
    };

    seedDeadSeat();
    fs.mkdirSync(takeoverDir, { recursive: true });
    fs.writeFileSync(path.join(takeoverDir, 'owner.json'), JSON.stringify({ ...deadOwner }));
    const taker = createSeatLock([seat0, seat1], 'node -e "taker"', { mode: 'exclusive' });
    const deadTakeoverRes = taker.tryAcquire();
    assert.strictEqual(deadTakeoverRes.acquired, true);
    assert.strictEqual(deadTakeoverRes.staleTaken, true);
    assert.strictEqual(fs.existsSync(takeoverDir), false);
    taker.releaseLock();

    seedDeadSeat();
    fs.mkdirSync(takeoverDir, { recursive: true });
    assert.strictEqual(taker.tryAcquire().acquired, false);
    assert.strictEqual(fs.existsSync(takeoverDir), true);
    const expired = new Date(Date.now() - TakeoverGraceMs - 1000);
    fs.utimesSync(takeoverDir, expired, expired);
    const expiredRes = taker.tryAcquire();
    assert.strictEqual(expiredRes.acquired, true);
    assert.strictEqual(expiredRes.staleTaken, true);
    assert.strictEqual(fs.existsSync(takeoverDir), false);
    taker.releaseLock();

    seedDeadSeat();
    fs.mkdirSync(takeoverDir, { recursive: true });
    const liveMutex = { ...deadOwner, pid: process.pid };
    fs.writeFileSync(path.join(takeoverDir, 'owner.json'), JSON.stringify(liveMutex));
    const blockedRes = taker.tryAcquire();
    assert.strictEqual(blockedRes.acquired, false);
    assert.strictEqual(blockedRes.staleTaken, false);
    assert.strictEqual(readSeatOwner(takeoverDir).pid, process.pid);
    assert.strictEqual(readSeatOwner(seat0).pid, deadPid);
    fs.rmSync(takeoverDir, RmRetryOptions);
    fs.rmSync(seat0, RmRetryOptions);
  });
}

module.exports = {
  createSeatLock,
  seatPathKey,
  readSeatOwner,
  seatOwnerMode,
  readSeatOwnerText,
  formatSeatOwners,
  isCommandGone,
  isDeadLocalOwner,
  selfTest,
};
