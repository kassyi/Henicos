#!/usr/bin/env node
// Reads the Windows kernel memory pools and the number of NVIDIA driver Event 153 entries, so that a GPU run can show
// whether the pools grew while the GPU faulted.
//
// A first measurement on one Windows 11 machine read only these two counters and took their growth during repeated GPU
// faults for a driver leak (reported as NVIDIA bug 7077284). A second measurement that also read the pool tag table
// found the growth in file system and filter caches from unrelated disk activity: 200 faults of a plain CUDA program
// whose kernel ends in `trap;` (driver 621.24) grew the tags of the GPU driver by 2 MB and the memory outside any tag
// not at all. The counters therefore show only that the pools grew, not why; look at the pool tags (poolmon) before
// blaming a fault. Each fault logs System events of the provider nvlddmkm with the ID 153: one per fault for the plain
// program, two per fault for a G# test process.
//
// This script reads:
//   - the raw pool sizes PoolNonpagedBytes and PoolPagedBytes of Win32_PerfRawData_PerfOS_Memory, through a JScript WMI
//     query run by cscript (about 0.1 s; the query text is written to tmp/gpu-fault-watch/ on first use); when that
//     fails and the caller allows it, typeperf instead, which samples for about a second;
//   - the number of nvlddmkm Event 153 entries since the last boot, through wevtutil (about 0.1 s).
// The two queries run in parallel in measureAsync(). On other platforms the reading is "not supported".
// GSHARP_GPU_POOL_WATCH=0 turns the reading off (it then reports "disabled").
//
// gpu-lock.js reads it around the command it runs; test-stability.js and long-checks.js write it into their summaries.
// They load this file with require(). A reading that fails leaves its value null and says why in `errors`; it never
// throws.
//
// Usage:
//   node scripts/human/gpu-fault-watch.js [--json]
//   node scripts/human/gpu-fault-watch.js --self-test
//   node scripts/human/gpu-fault-watch.js --help

'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const MiB = 1024 * 1024;

/** Warn when the pools grew by more than this over a run (both pools together). */
const DefaultGrowthWarnBytes = 1024 * MiB;

/** Warn when the nonpaged pool is larger than this, before or after a run. */
const DefaultNonpagedWarnBytes = 6 * 1024 * MiB;

/** Two readings belong to the same boot when their boot times differ by less than this. */
const SameBootToleranceMs = 60 * 1000;

const QueryTimeoutMs = 5000;

/** Turns the reading off when set to 0. */
const DisableVariable = 'GSHARP_GPU_POOL_WATCH';

/** Self-tests only: milliseconds every reading waits before it starts. */
const DelayVariable = 'GSHARP_GPU_POOL_WATCH_TEST_DELAY_MS';

const repoRoot = path.resolve(__dirname, '..', '..');

const PoolQuery = [
  'var s = GetObject("winmgmts:").ExecQuery(',
  '  "SELECT PoolNonpagedBytes, PoolPagedBytes FROM Win32_PerfRawData_PerfOS_Memory");',
  'for (var e = new Enumerator(s); !e.atEnd(); e.moveNext()) {',
  '  WScript.Echo(e.item().PoolNonpagedBytes + " " + e.item().PoolPagedBytes);',
  '}',
  '',
].join('\r\n');

const usage = [
  'Usage: node scripts/human/gpu-fault-watch.js [--json] [--self-test] [--help]',
  '',
  '  --json       Print the reading as JSON.',
  '  --self-test  Run the tests of the parsing and comparison logic and exit.',
  '  --help       Show this help.',
  '',
  'Prints the Windows kernel pool sizes (nonpaged, paged) and the number of nvlddmkm Event 153 entries (logged on',
  'GPU faults) since the last boot, in about 0.2 seconds. On other platforms it prints "not supported" and exits 0.',
  `${DisableVariable}=0 turns the reading off.`,
].join('\n');

/** Parses "<nonpaged> <paged>" printed by the WMI query. */
function parseWmiPools(text) {
  const match = /^\s*(\d+)\s+(\d+)\s*$/m.exec(text);
  return match === null ? null : { nonpagedBytes: Number(match[1]), pagedBytes: Number(match[2]) };
}

/** Parses the CSV of `typeperf <nonpaged> <paged> -sc 1`: a header line, then "time","nonpaged","paged". */
function parseTypeperf(text) {
  for (const line of text.split(/\r?\n/)) {
    const fields = line.split('","').map((field) => field.replace(/^"|"$/g, ''));
    if (fields.length === 3) {
      // A locale with a decimal comma writes "3173437440,000000"
      const nonpaged = Number(fields[1].replace(',', '.'));
      const paged = Number(fields[2].replace(',', '.'));
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

/** The JScript file of the WMI query, written on first use. */
function poolQueryFile() {
  const dir = path.join(repoRoot, 'tmp', 'gpu-fault-watch');
  const file = path.join(dir, 'read-pools.jscript');
  let current = null;
  try {
    current = fs.readFileSync(file, 'utf8');
  } catch {
    // Not written yet
  }
  if (current !== PoolQuery) {
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(file, PoolQuery);
  }
  return file;
}

// Each query: the command, its arguments and how to read its output; `run` runs it synchronously or not.
function wmiQuery() {
  return {
    name: 'WMI pool query (cscript)',
    command: 'cscript',
    args: ['//nologo', '//E:jscript', poolQueryFile()],
    parse: parseWmiPools,
  };
}

function typeperfQuery() {
  return {
    name: 'typeperf',
    command: 'typeperf',
    args: ['\\Memory\\Pool Nonpaged Bytes', '\\Memory\\Pool Paged Bytes', '-sc', '1'],
    parse: parseTypeperf,
  };
}

function eventQuery(withinMs) {
  const query =
    "*[System[Provider[@Name='nvlddmkm'] and (EventID=153) and " +
    `TimeCreated[timediff(@SystemTime) <= ${Math.max(1, Math.ceil(withinMs))}]]]`;
  return { name: 'wevtutil', command: 'wevtutil', args: ['qe', 'System', `/q:${query}`, '/f:xml'], parse: countEvents };
}

/** Why a finished query gave no value, or null when it did. */
function failure(query, result, value) {
  if (result.error) {
    return result.error.code === 'ETIMEDOUT'
      ? `${query.name} timed out after ${QueryTimeoutMs} ms`
      : `${query.name} could not run: ${result.error.code || result.error.message}`;
  }
  if (result.status !== 0) {
    const detail = String(result.stderr || result.stdout || '').trim().split(/\r?\n/)[0] || '';
    return `${query.name} failed (exit ${result.status})${detail === '' ? '' : `: ${detail.slice(0, 160)}`}`;
  }
  return value === null ? `${query.name} printed nothing it could read` : null;
}

function runSync(query) {
  const result = spawnSync(query.command, query.args, {
    encoding: 'utf8',
    windowsHide: true,
    timeout: QueryTimeoutMs,
    maxBuffer: 256 * MiB,
  });
  const value = result.error || result.status !== 0 ? null : query.parse(result.stdout);
  return { value, error: failure(query, result, value) };
}

function runAsync(query) {
  return new Promise((resolve) => {
    let child;
    try {
      child = spawn(query.command, query.args, { windowsHide: true });
    } catch (error) {
      resolve({ value: null, error: failure(query, { error }, null) });
      return;
    }
    const out = [];
    const err = [];
    let settled = false;
    const finish = (result) => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      const value = result.error || result.status !== 0 ? null : query.parse(result.stdout);
      resolve({ value, error: failure(query, result, value) });
    };
    const timer = setTimeout(() => {
      child.kill();
      const error = new Error('timed out');
      error.code = 'ETIMEDOUT';
      finish({ error });
    }, QueryTimeoutMs);
    child.stdout.on('data', (chunk) => out.push(chunk));
    child.stderr.on('data', (chunk) => err.push(chunk));
    child.on('error', (error) => finish({ error }));
    child.on('close', (status) =>
      finish({ status, stdout: Buffer.concat(out).toString('utf8'), stderr: Buffer.concat(err).toString('utf8') }),
    );
  });
}

function emptyReading() {
  const now = Date.now();
  const disabled = process.env[DisableVariable] === '0';
  return {
    supported: process.platform === 'win32' && !disabled,
    disabled,
    time: new Date(now).toISOString(),
    bootTime: new Date(now - os.uptime() * 1000).toISOString(),
    nonpagedBytes: null,
    pagedBytes: null,
    event153Entries: null,
    errors: [],
  };
}

function testDelayMs() {
  const delay = Number(process.env[DelayVariable]);
  return Number.isFinite(delay) && delay > 0 ? delay : 0;
}

function applyPools(reading, result) {
  if (result.error !== null) {
    reading.errors.push(result.error);
  } else {
    reading.nonpagedBytes = result.value.nonpagedBytes;
    reading.pagedBytes = result.value.pagedBytes;
  }
}

function applyResult(reading, result, field) {
  if (result.error !== null) {
    reading.errors.push(result.error);
  } else {
    reading[field] = result.value;
  }
}

/** Since the last boot: the pools of this boot are the ones the reading shows, so these are the entries that match. */
function sinceBootMs() {
  return os.uptime() * 1000 + SameBootToleranceMs;
}

/**
 * Reads the pools and the Event 153 entries since the last boot, synchronously. Returns
 * { supported, disabled, time, bootTime, nonpagedBytes, pagedBytes, event153Entries, errors }. With `slowFallback`,
 * typeperf (about a second) replaces a failed WMI query.
 */
function measure({ slowFallback = false } = {}) {
  const reading = emptyReading();
  const delay = testDelayMs();
  if (delay > 0) {
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, delay);
  }
  if (!reading.supported) {
    return reading;
  }
  try {
    let pools = runSync(wmiQuery());
    if (pools.value === null && slowFallback) {
      const fallback = runSync(typeperfQuery());
      pools = fallback.value === null ? { value: null, error: `${pools.error}; ${fallback.error}` } : fallback;
    }
    applyPools(reading, pools);
    applyResult(reading, runSync(eventQuery(sinceBootMs())), 'event153Entries');
  } catch (error) {
    reading.errors.push(`could not read the counters: ${error.message}`);
  }
  return reading;
}

/**
 * The same as measure(), with both queries in parallel and without blocking the event loop (so that a signal handler
 * still runs). `slowFallback` may be a function of the reading's Event 153 entries that decides whether typeperf
 * replaces a failed WMI query.
 */
async function measureAsync({ slowFallback = false } = {}) {
  const reading = emptyReading();
  const delay = testDelayMs();
  if (delay > 0) {
    await new Promise((resolve) => setTimeout(resolve, delay));
  }
  if (!reading.supported) {
    return reading;
  }
  try {
    const [pools, events] = await Promise.all([runAsync(wmiQuery()), runAsync(eventQuery(sinceBootMs()))]);
    applyResult(reading, events, 'event153Entries');
    let finalPools = pools;
    const wantsFallback = typeof slowFallback === 'function' ? slowFallback(reading.event153Entries) : slowFallback;
    if (pools.value === null && wantsFallback) {
      const fallback = await runAsync(typeperfQuery());
      finalPools = fallback.value === null ? { value: null, error: `${pools.error}; ${fallback.error}` } : fallback;
    }
    applyPools(reading, finalPools);
  } catch (error) {
    reading.errors.push(`could not read the counters: ${error.message}`);
  }
  return reading;
}

function sum(a, b) {
  return a === null || b === null ? null : a + b;
}

/**
 * Compares two readings. Returns { growthBytes, nonpagedBytes, event153Entries, sameBoot, warn, suggestReboot }: the
 * growth of both pools together, the nonpaged pool of `after`, the Event 153 entries between them (null across a
 * reboot or when unread), whether a warning is due (growth above `growthWarnBytes` or nonpaged above
 * `nonpagedWarnBytes`) and whether it suggests a reboot (Event 153 entries were logged, or nonpaged is above the
 * limit).
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
  const event153Entries =
    sameBoot && before.event153Entries !== null && after.event153Entries !== null
      ? Math.max(0, after.event153Entries - before.event153Entries)
      : null;
  const grew = growthBytes !== null && growthBytes > growthWarnBytes;
  const nonpagedHigh = nonpagedBytes !== null && nonpagedBytes > nonpagedWarnBytes;
  const warn = grew || nonpagedHigh;
  const suggestReboot = nonpagedHigh || (grew && event153Entries !== null && event153Entries > 0);
  return { growthBytes, nonpagedBytes, event153Entries, sameBoot, warn, suggestReboot };
}

/** Whether a single reading is already above the nonpaged limit (checked before a run starts). */
function isNonpagedHigh(reading, nonpagedWarnBytes = DefaultNonpagedWarnBytes) {
  return reading !== null && reading.nonpagedBytes !== null && reading.nonpagedBytes > nonpagedWarnBytes;
}

function megabytes(bytes) {
  return bytes === null ? 'unknown' : `${Math.round(bytes / MiB)} MB`;
}

/** One line for a reading: "kernel pools: nonpaged 1973 MB, paged 2190 MB; Event 153 entries since boot: 12". */
function describe(reading) {
  if (reading === null) {
    return 'kernel pools: not read';
  }
  if (reading.disabled) {
    return `kernel pools: not read (${DisableVariable}=0)`;
  }
  if (!reading.supported) {
    return 'kernel pools: not supported on this platform';
  }
  const events = reading.event153Entries === null ? 'unknown' : String(reading.event153Entries);
  const line =
    `kernel pools: nonpaged ${megabytes(reading.nonpagedBytes)}, paged ${megabytes(reading.pagedBytes)}; ` +
    `nvlddmkm Event 153 entries since boot: ${events}`;
  return reading.errors.length === 0 ? line : `${line} (${reading.errors.join('; ')})`;
}

const RebootAdvice =
  'The pool counters do not say what holds the memory: before blaming the GPU faults, list the pool tags that grew ' +
  '(poolmon); file system and filter caches are a common cause. A reboot frees the pools; consider one before the ' +
  'machine runs short of memory.';

/** The warning for a comparison whose `warn` is true: the observed values, and the reboot advice when it applies. */
function warningText(comparison) {
  const growth = comparison.growthBytes === null ? 'an unknown amount' : megabytes(comparison.growthBytes);
  const entries =
    comparison.event153Entries === null ? 'an unknown number of' : String(comparison.event153Entries);
  const facts =
    `the kernel pools grew by ${growth} during this run (nonpaged now ${megabytes(comparison.nonpagedBytes)}), ` +
    `with ${entries} nvlddmkm Event 153 entries.`;
  if (comparison.suggestReboot) {
    return `${facts} ${RebootAdvice}`;
  }
  return `${facts} No GPU fault was logged, so the growth has another cause.`;
}

/** The warning for a reading taken before a run whose nonpaged pool is already above the limit. */
function startWarningText(reading) {
  return `the nonpaged kernel pool is already ${megabytes(reading.nonpagedBytes)} before this run ` +
    `(${reading.event153Entries === null ? 'unknown' : reading.event153Entries} nvlddmkm Event 153 entries since ` +
    `boot). ${RebootAdvice}`;
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
  assert.deepStrictEqual(parseTypeperf('"t","3173437440,000000","3112165376,000000"'), {
    nonpagedBytes: 3173437440,
    pagedBytes: 3112165376,
  });
  assert.strictEqual(parseTypeperf('"(PDH-CSV 4.0)","a","b"\r\n"time","",""\r\n'), null);
  assert.strictEqual(parseTypeperf(''), null);
  assert.deepStrictEqual(parseWmiPools('3265765376 3341742080\r\n'), {
    nonpagedBytes: 3265765376,
    pagedBytes: 3341742080,
  });
  assert.strictEqual(parseWmiPools(''), null);
  assert.strictEqual(parseWmiPools('Error 0x80041002'), null);

  const event = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System/><EventData/></Event>";
  assert.strictEqual(countEvents(''), 0);
  assert.strictEqual(countEvents(event + event), 2);
  assert.strictEqual(countEvents('<EventData/>'), 0);

  // Failures say why
  const query = { name: 'q' };
  assert.strictEqual(failure(query, { status: 0 }, 1), null);
  const missing = Object.assign(new Error('x'), { code: 'ENOENT' });
  assert.match(failure(query, { error: missing }, null), /q could not run: ENOENT/);
  assert.match(failure(query, { error: Object.assign(new Error('x'), { code: 'ETIMEDOUT' }) }, null), /timed out/);
  const denied = failure(query, { status: 1, stderr: 'access denied\r\nmore' }, null);
  assert.match(denied, /q failed \(exit 1\): access denied$/);
  assert.match(failure(query, { status: 0, stdout: '' }, null), /printed nothing/);

  const boot = '2026-11-11T03:43:00.000Z';
  const reading = (nonpagedMb, pagedMb, events, bootTime = boot) => ({
    supported: true,
    disabled: false,
    time: '2026-11-11T05:00:00.000Z',
    bootTime,
    nonpagedBytes: nonpagedMb === null ? null : nonpagedMb * MiB,
    pagedBytes: pagedMb === null ? null : pagedMb * MiB,
    event153Entries: events,
    errors: [],
  });

  assert.deepStrictEqual(compare(reading(2000, 2000, 10), reading(2100, 2200, 12)), {
    growthBytes: 300 * MiB,
    nonpagedBytes: 2100 * MiB,
    event153Entries: 2,
    sameBoot: true,
    warn: false,
    suggestReboot: false,
  });
  // Growth just over and at the threshold; a reboot is suggested only when Event 153 entries were logged
  const grewWithFaults = compare(reading(2000, 2000, 0), reading(2000, 3025, 4));
  assert.strictEqual(grewWithFaults.warn, true);
  assert.strictEqual(grewWithFaults.suggestReboot, true);
  const grewWithoutFaults = compare(reading(2000, 2000, 0), reading(2000, 3025, 0));
  assert.strictEqual(grewWithoutFaults.warn, true);
  assert.strictEqual(grewWithoutFaults.suggestReboot, false);
  assert.match(warningText(grewWithoutFaults), /with 0 nvlddmkm Event 153 entries\. No GPU fault was logged/);
  assert.doesNotMatch(warningText(grewWithoutFaults), /reboot/);
  assert.strictEqual(compare(reading(2000, 2000, 0), reading(2000, 3024, 9)).warn, false);
  // Nonpaged above the limit without growth during the run: warn and suggest a reboot
  const high = compare(reading(6200, 2000, 0), reading(6200, 2000, 0));
  assert.strictEqual(high.warn && high.suggestReboot, true);
  assert.strictEqual(compare(reading(6144, 2000, 0), reading(6144, 2000, 0)).warn, false);
  // Thresholds can be lowered
  const lowered = compare(reading(2000, 2000, 0), reading(2000, 2101, 0), { growthWarnBytes: 100 * MiB });
  assert.strictEqual(lowered.warn, true);
  // A reboot between the readings: no growth or entry count, only the nonpaged limit
  const rebooted = compare(reading(8000, 9000, 500), reading(2000, 2000, 3, '2026-11-11T04:30:00.000Z'));
  assert.deepStrictEqual(rebooted, {
    growthBytes: null,
    nonpagedBytes: 2000 * MiB,
    event153Entries: null,
    sameBoot: false,
    warn: false,
    suggestReboot: false,
  });
  // Unread values never warn and never throw
  assert.strictEqual(compare(reading(null, null, null), reading(null, null, null)).warn, false);
  assert.strictEqual(compare(null, reading(7000, 2000, 1)).warn, true);
  assert.strictEqual(compare(reading(2000, 2000, 1), null).warn, false);

  // Before a run
  assert.strictEqual(isNonpagedHigh(reading(6200, 2000, 0)), true);
  assert.strictEqual(isNonpagedHigh(reading(6144, 2000, 0)), false);
  assert.strictEqual(isNonpagedHigh(reading(null, null, 0)), false);
  assert.match(startWarningText(reading(6200, 2000, 40)), /already 6200 MB before this run \(40 nvlddmkm .*poolmon/);

  assert.match(describe(reading(1973, 2190, 12)), /nonpaged 1973 MB, paged 2190 MB; .* since boot: 12$/);
  const unread = describe({ ...reading(null, null, 3), errors: ['wevtutil timed out'] });
  assert.match(unread, /nonpaged unknown.*\(wevtutil timed out\)$/);
  assert.match(describe({ ...reading(1, 1, 0), supported: false }), /not supported/);
  assert.match(describe({ ...reading(1, 1, 0), supported: false, disabled: true }), /GSHARP_GPU_POOL_WATCH=0/);
  const grown = compare(reading(2000, 2000, 0), reading(2500, 3000, 200));
  assert.match(warningText(grown), /grew by 1500 MB .*with 200 nvlddmkm Event 153 entries\. .*poolmon/);

  // The live readings never throw, on any platform
  const live = measure();
  assert.strictEqual(typeof live.supported, 'boolean');
  assert.ok(Array.isArray(live.errors));
  return measureAsync().then((liveAsync) => {
    assert.strictEqual(typeof liveAsync.supported, 'boolean');
    process.stdout.write('gpu-fault-watch self-test passed\n');
  });
}

async function main(argv) {
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
    await selfTest();
    return 0;
  }
  const reading = await measureAsync({ slowFallback: true });
  process.stdout.write(argv.includes('--json') ? `${JSON.stringify(reading, null, 2)}\n` : `${describe(reading)}\n`);
  return 0;
}

module.exports = {
  measure,
  measureAsync,
  compare,
  isNonpagedHigh,
  describe,
  warningText,
  startWarningText,
  DisableVariable,
  DelayVariable,
  DefaultGrowthWarnBytes,
  DefaultNonpagedWarnBytes,
};

if (require.main === module) {
  main(process.argv.slice(2)).then((code) => {
    process.exitCode = code;
  });
}
