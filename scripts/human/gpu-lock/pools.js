'use strict';

// The kernel pool readings of gpu-lock.js around the command it runs.
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
// Usage (from gpu-lock.js):
//   const pools = createPoolWatch(lockPath, commandText);
//   pools.start();               once the command has started and been recorded
//   await pools.report(code);    after it has ended, before the lock is released

const fs = require('fs');
const path = require('path');

/** The kernel pool reader, or null when it cannot be loaded; the lock works without it. */
const faultWatch = (() => {
  try {
    return require(path.join(__dirname, '..', 'gpu-fault-watch.js'));
  } catch (error) {
    process.stderr.write(`[gpu-lock] kernel pool watch unavailable: ${error.message}\n`);
    return null;
  }
})();

function tsvMegabytes(bytes) {
  return bytes === null || bytes === undefined ? '' : String(Math.round(bytes / (1024 * 1024)));
}

/** The readings of one run of `commandText` under the lock directory `lockPath`. */
function createPoolWatch(lockPath, commandText) {
  /** The reading taken while the command starts; resolves to null when the watch is unavailable or off. */
  let poolsBefore = Promise.resolve(null);

  /**
   * Reads the pools after the command has started and has been recorded in owner.json, and warns when the nonpaged
   * pool is already above the limit. Never rejects.
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

  /** Starts the reading taken while the command starts. */
  function start() {
    poolsBefore = readStartPools();
  }

  /**
   * Reads the pools again without blocking (a signal still stops the script), prints them with the Event 153 entries
   * of the run, warns when they grew past the thresholds, and appends one line to gpu-pool-log.tsv next to the lock
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
        : 'start\tend\texit\tnonpagedBeforeMB\tnonpagedAfterMB\tpagedBeforeMB\tpagedAfterMB\t' +
          'event153Entries\tcommand\n';
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

  return { start, report: reportPools };
}

module.exports = { faultWatch, createPoolWatch };
