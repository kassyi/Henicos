#!/usr/bin/env node
// Reads the Windows kernel memory pools and the count of NVIDIA driver fault events, so that a long GPU run can tell
// whether GPU faults leaked kernel memory.
//
// On Windows every CUDA device fault (a `trap;` or an illegal address) leaves kernel pool memory allocated in the
// driver until the machine reboots, about 2.3 MB nonpaged and 5.5 MB paged per fault (NVIDIA bug 7077284,
// https://developer.nvidia.com/bugs/7077284). Each fault also logs one System event of the provider nvlddmkm with the
// ID 153. This script reads:
//   - \Memory\Pool Nonpaged Bytes and \Memory\Pool Paged Bytes (performance counters, through typeperf);
//   - the number of nvlddmkm Event 153 entries since the last boot (through wevtutil).
// One reading takes about 1.5 seconds. On other platforms it prints "not supported" and exits with 0.
//
// gpu-lock.js reads it when it takes and releases the lock, test-stability.js and long-checks.js write it into their
// summaries; they load this file with require() and call measure(), compare() and describe(). A reading that fails
// leaves its fields null; it never throws.
//
// Usage:
//   node scripts/human/gpu-fault-watch.js [--json]
//   node scripts/human/gpu-fault-watch.js --self-test
//   node scripts/human/gpu-fault-watch.js --help

'use strict';

const os = require('os');
const { spawnSync } = require('child_process');

const MiB = 1024 * 1024;

/** Warn when the pools grew by more than this over a run (both pools together). */
const DefaultGrowthWarnBytes = 1024 * MiB;

/** Warn when the nonpaged pool is larger than this at the end of a run. */
const DefaultNonpagedWarnBytes = 6 * 1024 * MiB;

/** Two readings belong to the same boot when their boot times differ by less than this. */
const SameBootToleranceMs = 60 * 1000;

const BugUrl = 'https://developer.nvidia.com/bugs/7077284';

const QueryTimeoutMs = 5000;

const usage = [
  'Usage: node scripts/human/gpu-fault-watch.js [--json] [--self-test] [--help]',
  '',
  '  --json       Print the reading as JSON.',
  '  --self-test  Run the tests of the parsing and comparison logic and exit.',
  '  --help       Show this help.',
  '',
  'Prints the Windows kernel pool sizes (nonpaged, paged) and the number of NVIDIA driver fault events',
  '(nvlddmkm Event 153) since the last boot. On other platforms it prints "not supported" and exits with 0.',
].join('\n');

/** Parses the CSV of `typeperf <nonpaged> <paged> -sc 1`: a header line, then "time","nonpaged","paged". */
function parseTypeperf(text) {
  for (const line of text.split(/\r?\n/)) {
    const fields = line.split('","').map((field) => field.replace(/^"|"$/g, ''));
    if (fields.length === 3) {
      const nonpaged = Number(fields[1]);
      const paged = Number(fields[2]);
      if (fields[1] !== '' && fields[2] !== '' && Number.isFinite(nonpaged) && Number.isFinite(paged)) {
        return { nonpagedBytes: Math.round(nonpaged), pagedBytes: Math.round(paged) };
      }
    }
  }
  return null;
}

/** Counts the events of `wevtutil qe ... /f:xml`, one <Event> element each. */
function countEvents(xml) {
  const matches = xml.match(/<Event[\s>]/g);
  return matches === null ? 0 : matches.length;
}

function readPools() {
  const result = spawnSync(
    'typeperf',
    ['\\Memory\\Pool Nonpaged Bytes', '\\Memory\\Pool Paged Bytes', '-sc', '1'],
    { encoding: 'utf8', windowsHide: true, timeout: QueryTimeoutMs },
  );
  if (result.error || result.status !== 0) {
    return null;
  }
  return parseTypeperf(result.stdout);
}

/** The nvlddmkm Event 153 entries of the System log in the last `withinMs` milliseconds, or null. */
function readFaultEvents(withinMs) {
  const query =
    "*[System[Provider[@Name='nvlddmkm'] and (EventID=153) and " +
    `TimeCreated[timediff(@SystemTime) <= ${Math.max(1, Math.ceil(withinMs))}]]]`;
  const result = spawnSync('wevtutil', ['qe', 'System', `/q:${query}`, '/f:xml'], {
    encoding: 'utf8',
    windowsHide: true,
    timeout: QueryTimeoutMs,
    maxBuffer: 256 * MiB,
  });
  if (result.error || result.status !== 0) {
    return null;
  }
  return countEvents(result.stdout);
}

/**
 * Reads the pools and the fault events since the last boot. Returns
 * { supported, time, bootTime, nonpagedBytes, pagedBytes, faultEvents }; a field that could not be read is null, and
 * on a platform other than Windows `supported` is false and every reading is null.
 */
function measure() {
  const now = Date.now();
  const reading = {
    supported: process.platform === 'win32',
    time: new Date(now).toISOString(),
    bootTime: new Date(now - os.uptime() * 1000).toISOString(),
    nonpagedBytes: null,
    pagedBytes: null,
    faultEvents: null,
  };
  if (!reading.supported) {
    return reading;
  }
  try {
    const pools = readPools();
    if (pools !== null) {
      reading.nonpagedBytes = pools.nonpagedBytes;
      reading.pagedBytes = pools.pagedBytes;
    }
    // The leak lasts until a reboot, so the events since the boot are the faults whose memory is still held
    reading.faultEvents = readFaultEvents(os.uptime() * 1000 + SameBootToleranceMs);
  } catch (error) {
    process.stderr.write(`[gpu-fault-watch] could not read the counters: ${error.message}\n`);
  }
  return reading;
}

function sum(a, b) {
  return a === null || b === null ? null : a + b;
}

/**
 * Compares two readings of measure(). Returns { growthBytes, nonpagedBytes, faultEvents, sameBoot, warn }: the growth
 * of both pools together, the nonpaged pool of `after`, the fault events between them (null across a reboot or when
 * unread) and whether a warning is due (growth above `growthWarnBytes` or nonpaged above `nonpagedWarnBytes`).
 */
function compare(before, after, thresholds = {}) {
  const growthWarnBytes = thresholds.growthWarnBytes ?? DefaultGrowthWarnBytes;
  const nonpagedWarnBytes = thresholds.nonpagedWarnBytes ?? DefaultNonpagedWarnBytes;
  const sameBoot =
    before !== null && after !== null &&
    Math.abs(Date.parse(after.bootTime) - Date.parse(before.bootTime)) < SameBootToleranceMs;
  const totalBefore = before === null ? null : sum(before.nonpagedBytes, before.pagedBytes);
  const totalAfter = after === null ? null : sum(after.nonpagedBytes, after.pagedBytes);
  const growthBytes = sameBoot && totalBefore !== null && totalAfter !== null ? totalAfter - totalBefore : null;
  const nonpagedBytes = after === null ? null : after.nonpagedBytes;
  const faultEvents =
    sameBoot && before.faultEvents !== null && after.faultEvents !== null
      ? Math.max(0, after.faultEvents - before.faultEvents)
      : null;
  const warn =
    (growthBytes !== null && growthBytes > growthWarnBytes) ||
    (nonpagedBytes !== null && nonpagedBytes > nonpagedWarnBytes);
  return { growthBytes, nonpagedBytes, faultEvents, sameBoot, warn };
}

function megabytes(bytes) {
  return bytes === null ? '?' : `${Math.round(bytes / MiB)} MB`;
}

/** One line for a reading: "nonpaged 1973 MB, paged 2190 MB, 12 GPU fault events since boot". */
function describe(reading) {
  if (reading === null || !reading.supported) {
    return 'kernel pools: not supported on this platform';
  }
  const events = reading.faultEvents === null ? '?' : String(reading.faultEvents);
  return `kernel pools: nonpaged ${megabytes(reading.nonpagedBytes)}, paged ${megabytes(reading.pagedBytes)}; ` +
    `GPU fault events (nvlddmkm 153) since boot: ${events}`;
}

/** The warning for a comparison whose `warn` is true. */
function warningText(comparison) {
  const growth = comparison.growthBytes === null ? 'an unknown amount' : megabytes(comparison.growthBytes);
  const faults = comparison.faultEvents === null ? 'an unknown number of' : String(comparison.faultEvents);
  const nonpaged = megabytes(comparison.nonpagedBytes);
  return `the kernel memory pools grew by ${growth} during this run (nonpaged now ${nonpaged}; ` +
    `${faults} GPU fault events). On Windows every GPU fault (CUDA trap or illegal address) leaks kernel memory in ` +
    `the NVIDIA driver until the machine reboots (NVIDIA bug 7077284, ${BugUrl}). Reboot before the machine runs ` +
    'short of memory.';
}

function selfTest() {
  const assert = require('assert');
  const csv = [
    '',
    '"(PDH-CSV 4.0)","\\\\HOST\\Memory\\Pool Nonpaged Bytes","\\\\HOST\\Memory\\Pool Paged Bytes"',
    '"11/11/2026 05:22:41.843","3173437440.000000","3112165376.000000"',
    '',
  ].join('\r\n');
  assert.deepStrictEqual(parseTypeperf(csv), { nonpagedBytes: 3173437440, pagedBytes: 3112165376 });
  assert.strictEqual(parseTypeperf('"(PDH-CSV 4.0)","a","b"\r\n"time","",""\r\n'), null);
  assert.strictEqual(parseTypeperf(''), null);

  const event = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System/><EventData/></Event>";
  assert.strictEqual(countEvents(''), 0);
  assert.strictEqual(countEvents(event + event), 2);
  assert.strictEqual(countEvents('<EventData/>'), 0);

  const boot = '2026-11-11T03:43:00.000Z';
  const reading = (nonpagedMb, pagedMb, events, bootTime = boot) => ({
    supported: true,
    time: '2026-11-11T05:00:00.000Z',
    bootTime,
    nonpagedBytes: nonpagedMb === null ? null : nonpagedMb * MiB,
    pagedBytes: pagedMb === null ? null : pagedMb * MiB,
    faultEvents: events,
  });

  const small = compare(reading(2000, 2000, 10), reading(2100, 2200, 12));
  assert.deepStrictEqual(small, {
    growthBytes: 300 * MiB,
    nonpagedBytes: 2100 * MiB,
    faultEvents: 2,
    sameBoot: true,
    warn: false,
  });
  // Growth just over and at the threshold
  assert.strictEqual(compare(reading(2000, 2000, 0), reading(2000, 3025, 0)).warn, true);
  assert.strictEqual(compare(reading(2000, 2000, 0), reading(2000, 3024, 0)).warn, false);
  // Nonpaged above the limit without growth during the run
  assert.strictEqual(compare(reading(6200, 2000, 0), reading(6200, 2000, 0)).warn, true);
  assert.strictEqual(compare(reading(6144, 2000, 0), reading(6144, 2000, 0)).warn, false);
  // Thresholds can be lowered
  const lowered = compare(reading(2000, 2000, 0), reading(2000, 2101, 0), { growthWarnBytes: 100 * MiB });
  assert.strictEqual(lowered.warn, true);
  // A reboot between the readings: no growth or event count, only the nonpaged limit
  const rebooted = compare(reading(8000, 9000, 500), reading(2000, 2000, 3, '2026-11-11T04:30:00.000Z'));
  assert.deepStrictEqual(rebooted, {
    growthBytes: null,
    nonpagedBytes: 2000 * MiB,
    faultEvents: null,
    sameBoot: false,
    warn: false,
  });
  // Unread fields never warn and never throw
  assert.strictEqual(compare(reading(null, null, null), reading(null, null, null)).warn, false);
  assert.strictEqual(compare(null, reading(7000, 2000, 1)).warn, true);
  assert.strictEqual(compare(reading(2000, 2000, 1), null).warn, false);

  assert.match(describe(reading(1973, 2190, 12)), /nonpaged 1973 MB, paged 2190 MB; .* since boot: 12$/);
  assert.match(describe({ ...reading(1, 1, 0), supported: false }), /not supported/);
  const grown = compare(reading(2000, 2000, 0), reading(2500, 3000, 200));
  assert.match(warningText(grown), /grew by 1500 MB .*200 GPU fault events.*7077284/);

  // The live reading never throws, on any platform
  const live = measure();
  assert.strictEqual(typeof live.supported, 'boolean');
  process.stdout.write('gpu-fault-watch self-test passed\n');
}

function main(argv) {
  if (argv.includes('--help') || argv.includes('-h')) {
    process.stdout.write(`${usage}\n`);
    return 0;
  }
  const unknown = argv.filter((arg) => arg !== '--json' && arg !== '--self-test');
  if (unknown.length > 0) {
    process.stderr.write(`Unknown option: ${unknown[0]}\n${usage}\n`);
    return 2;
  }
  if (argv.includes('--self-test')) {
    selfTest();
    return 0;
  }
  if (process.platform !== 'win32') {
    process.stdout.write(argv.includes('--json') ? '{"supported":false}\n' : 'not supported on this platform\n');
    return 0;
  }
  const reading = measure();
  process.stdout.write(argv.includes('--json') ? `${JSON.stringify(reading, null, 2)}\n` : `${describe(reading)}\n`);
  return 0;
}

module.exports = { measure, compare, describe, warningText, DefaultGrowthWarnBytes, DefaultNonpagedWarnBytes };

if (require.main === module) {
  process.exitCode = main(process.argv.slice(2));
}
