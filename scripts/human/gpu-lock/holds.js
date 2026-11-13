'use strict';

// The GPU hold record of gpu-lock.js: how long the lock was held and how busy the GPU was meanwhile, so that the job
// queue can be judged by numbers (which jobs hold the GPU without using it, how much GPU memory a test run takes, and
// whether two GPU jobs could share the device).
//
// While the lock is held, one nvidia-smi process samples the first GPU it lists every SamplePeriodMs
// (`nvidia-smi --query-gpu=index,utilization.gpu,memory.used --format=csv,noheader,nounits -lms <period>`). It starts
// together with the command, without waiting for it, and is stopped once the command has ended. Its first sample,
// taken while the command starts, is the memory baseline. utilization.gpu is the share of the sample period in which
// a kernel ran. At release, one line goes to the console and one row to gpu-hold-log.tsv next to the lock directory:
//
//   start  heldSeconds  exit  periodMs  samples  utilMeanPercent  busySharePercent  memBaselineMiB  memPeakMiB  note
//   command
//
// busySharePercent is the share of samples with a utilization of at least BusyThresholdPercent. When nvidia-smi is
// missing or fails, the row keeps start, heldSeconds, exit and command, leaves the GPU columns empty and says why in
// note. The measurement never changes the exit code and never delays the command. GSHARP_GPU_HOLD_WATCH=0 turns it
// off (no sampler, no row).
//
// `node scripts/human/gpu-lock.js --holds [--since <date>] [--log <file>]` summarizes the rows (summarize/format).
//
// Usage (from gpu-lock.js):
//   const hold = createHoldWatch(lockPath, commandText);
//   hold.start();                once the lock is taken, before the command starts
//   await hold.report(code);     after the command has ended, before the lock is released
//   hold.reportSync(code);       from a signal handler

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

/** Turns the hold record off when set to 0. */
const DisableVariable = 'GSHARP_GPU_HOLD_WATCH';

const SamplePeriodMs = 1000;

/**
 * A sample counts as busy when the GPU ran a kernel for at least this share of its period: above the desktop's own
 * load, since a GPU that also drives the display (an RTX 3070 Ti) reads 3 to 9% with no CUDA work, now and then 10%.
 */
const BusyThresholdPercent = 20;

/** How long the sampler may take to stop and hand over its last output. */
const StopTimeoutMs = 2000;

const MaxCommandLength = 160;

const LogName = 'gpu-hold-log.tsv';

const Columns = [
  'start',
  'heldSeconds',
  'exit',
  'periodMs',
  'samples',
  'utilMeanPercent',
  'busySharePercent',
  'memBaselineMiB',
  'memPeakMiB',
  'note',
  'command',
];

/**
 * Parses the output of the sampler: lines "<index>, <utilization %>, <memory used MiB>". Keeps the GPU of the first
 * line; lines it cannot read ([N/A], a partial last line) are counted in `unreadable`.
 */
function parseSamples(text) {
  const samples = [];
  let unreadable = 0;
  let gpu = null;
  for (const line of text.split(/\r?\n/)) {
    if (line.trim() === '') {
      continue;
    }
    const match = /^\s*(\d+)\s*,\s*(\d+(?:\.\d+)?)\s*,\s*(\d+(?:\.\d+)?)\s*$/.exec(line);
    if (match === null) {
      unreadable++;
      continue;
    }
    gpu = gpu === null ? match[1] : gpu;
    if (match[1] === gpu) {
      samples.push({ utilization: Number(match[2]), memoryMiB: Number(match[3]) });
    }
  }
  return { samples, unreadable };
}

/**
 * The GPU figures of one hold from its samples: { samples, utilMeanPercent, busySharePercent, memBaselineMiB,
 * memPeakMiB }, the figures null when there is no sample.
 */
function summarizeSamples(samples, threshold = BusyThresholdPercent) {
  if (samples.length === 0) {
    return { samples: 0, utilMeanPercent: null, busySharePercent: null, memBaselineMiB: null, memPeakMiB: null };
  }
  let total = 0;
  let busy = 0;
  let peak = samples[0].memoryMiB;
  for (const sample of samples) {
    total += sample.utilization;
    if (sample.utilization >= threshold) {
      busy++;
    }
    peak = Math.max(peak, sample.memoryMiB);
  }
  return {
    samples: samples.length,
    utilMeanPercent: total / samples.length,
    busySharePercent: (100 * busy) / samples.length,
    memBaselineMiB: samples[0].memoryMiB,
    memPeakMiB: peak,
  };
}

function shortCommand(commandText) {
  const flat = commandText.replace(/[\t\r\n]+/g, ' ');
  return flat.length <= MaxCommandLength ? flat : `${flat.slice(0, MaxCommandLength - 3)}...`;
}

function cell(value, digits = 0) {
  return value === null || value === undefined ? '' : value.toFixed(digits);
}

/** One TSV row (without the line break) of a hold. */
function formatRow(hold) {
  return [
    hold.start,
    cell(hold.heldSeconds, 1),
    hold.exit === null ? '' : String(hold.exit),
    String(hold.periodMs),
    String(hold.samples),
    cell(hold.utilMeanPercent, 1),
    cell(hold.busySharePercent, 1),
    cell(hold.memBaselineMiB),
    cell(hold.memPeakMiB),
    hold.note.replace(/[\t\r\n]+/g, ' '),
    shortCommand(hold.command),
  ].join('\t');
}

function numberOrNull(text) {
  return text === undefined || text === '' ? null : Number(text);
}

/** Reads the rows of gpu-hold-log.tsv; skips the header and rows it cannot read. */
function parseLog(text) {
  const holds = [];
  for (const line of text.split(/\r?\n/)) {
    const fields = line.split('\t');
    if (fields.length !== Columns.length || fields[0] === 'start' || Number.isNaN(Date.parse(fields[0]))) {
      continue;
    }
    holds.push({
      start: fields[0],
      heldSeconds: numberOrNull(fields[1]) ?? 0,
      exit: numberOrNull(fields[2]),
      periodMs: numberOrNull(fields[3]),
      samples: numberOrNull(fields[4]) ?? 0,
      utilMeanPercent: numberOrNull(fields[5]),
      busySharePercent: numberOrNull(fields[6]),
      memBaselineMiB: numberOrNull(fields[7]),
      memPeakMiB: numberOrNull(fields[8]),
      note: fields[9],
      command: fields[10],
    });
  }
  return holds;
}

function median(values) {
  if (values.length === 0) {
    return null;
  }
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * Sums up holds (from parseLog) that started at or after `since` (a time in ms, or null): counts, the held time, the
 * busy share and mean utilization weighted by held time (measured holds only), the memory peaks and the growth over
 * the baseline (median, max), and the `top` busiest and idlest measured holds (by busy share; the longer first on a
 * tie).
 */
function summarize(holds, since = null, top = 3) {
  const selected = holds.filter((hold) => since === null || Date.parse(hold.start) >= since);
  const measured = selected.filter((hold) => hold.samples > 0 && hold.busySharePercent !== null);
  let heldSeconds = 0;
  for (const hold of selected) {
    heldSeconds += hold.heldSeconds;
  }
  let measuredSeconds = 0;
  let busySeconds = 0;
  let utilSeconds = 0;
  for (const hold of measured) {
    measuredSeconds += hold.heldSeconds;
    busySeconds += (hold.heldSeconds * hold.busySharePercent) / 100;
    utilSeconds += (hold.heldSeconds * hold.utilMeanPercent) / 100;
  }
  const peaks = measured.map((hold) => hold.memPeakMiB);
  const growths = measured.map((hold) => hold.memPeakMiB - hold.memBaselineMiB);
  const byBusy = (a, b) => b.busySharePercent - a.busySharePercent || b.heldSeconds - a.heldSeconds;
  const byIdle = (a, b) => a.busySharePercent - b.busySharePercent || b.heldSeconds - a.heldSeconds;
  return {
    count: selected.length,
    measured: measured.length,
    heldSeconds,
    measuredSeconds,
    busySharePercent: measuredSeconds > 0 ? (100 * busySeconds) / measuredSeconds : null,
    utilMeanPercent: measuredSeconds > 0 ? (100 * utilSeconds) / measuredSeconds : null,
    idleSeconds: measuredSeconds - busySeconds,
    memPeakMedianMiB: median(peaks),
    memPeakMaxMiB: peaks.length === 0 ? null : Math.max(...peaks),
    memGrowthMedianMiB: median(growths),
    memGrowthMaxMiB: growths.length === 0 ? null : Math.max(...growths),
    busiest: [...measured].sort(byBusy).slice(0, top),
    idlest: [...measured].sort(byIdle).slice(0, top),
  };
}

function duration(seconds) {
  const total = Math.round(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  return hours > 0 ? `${hours}h ${minutes}m` : minutes > 0 ? `${minutes}m ${rest}s` : `${rest}s`;
}

function holdLine(hold) {
  return `  ${hold.start}  ${duration(hold.heldSeconds)}, busy ${hold.busySharePercent.toFixed(0)}%, ` +
    `mean ${hold.utilMeanPercent.toFixed(0)}%, memory ${hold.memBaselineMiB} -> ${hold.memPeakMiB} MiB, ` +
    `exit ${hold.exit === null ? '?' : hold.exit}: ${hold.command}`;
}

function mib(value) {
  return value === null ? 'unknown' : `${Math.round(value)} MiB`;
}

/** The summary as text for people. */
function formatSummary(summary, source) {
  if (summary.count === 0) {
    return `No GPU holds recorded in ${source}.\n`;
  }
  const lines = [
    `GPU holds in ${source}: ${summary.count} (${summary.measured} with GPU samples), ` +
      `held ${duration(summary.heldSeconds)} in total`,
  ];
  if (summary.measured > 0) {
    lines.push(
      `busy (samples >= ${BusyThresholdPercent}%): ${summary.busySharePercent.toFixed(1)}% of the measured held time ` +
        `(${duration(summary.measuredSeconds)}), idle ${duration(summary.idleSeconds)}; ` +
        `mean utilization ${summary.utilMeanPercent.toFixed(1)}% (weighted by held time)`,
      `memory peak: median ${mib(summary.memPeakMedianMiB)}, max ${mib(summary.memPeakMaxMiB)}; ` +
        `above the baseline: median ${mib(summary.memGrowthMedianMiB)}, max ${mib(summary.memGrowthMaxMiB)}`,
      'busiest:',
      ...summary.busiest.map(holdLine),
      'idlest:',
      ...summary.idlest.map(holdLine),
    );
  }
  return `${lines.join('\n')}\n`;
}

/** The console line of one hold. */
function describeHold(hold) {
  const held = `GPU hold ${hold.heldSeconds.toFixed(1)} s`;
  if (hold.samples === 0) {
    return `${held}, GPU not sampled${hold.note === '' ? '' : ` (${hold.note})`}`;
  }
  return `${held}: busy ${hold.busySharePercent.toFixed(0)}% of ${hold.samples} samples ` +
    `(>= ${BusyThresholdPercent}%), mean utilization ${hold.utilMeanPercent.toFixed(0)}%, ` +
    `memory ${hold.memBaselineMiB} -> peak ${hold.memPeakMiB} MiB` +
    (hold.note === '' ? '' : ` (${hold.note})`);
}

/** The hold record of one run of `commandText` under the lock directory `lockPath`. */
function createHoldWatch(lockPath, commandText) {
  const enabled = process.env[DisableVariable] !== '0';
  const logPath = path.join(path.dirname(lockPath), LogName);
  let startedAt = 0;
  let sampler = null;
  let output = '';
  let errorText = '';
  let failure = '';
  let closed = null;
  let recorded = false;
  let stopping = false;

  function start() {
    startedAt = Date.now();
    if (!enabled) {
      return;
    }
    try {
      sampler = spawn(
        'nvidia-smi',
        [
          '--query-gpu=index,utilization.gpu,memory.used',
          '--format=csv,noheader,nounits',
          '-lms',
          String(SamplePeriodMs),
        ],
        { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] },
      );
    } catch (error) {
      failure = `nvidia-smi could not run: ${error.code || error.message}`;
      return;
    }
    closed = new Promise((resolve) => {
      sampler.on('error', (error) => {
        failure = `nvidia-smi could not run: ${error.code || error.message}`;
        resolve();
      });
      sampler.on('close', (code, signal) => {
        if (code !== null && code !== 0 && failure === '') {
          const detail = errorText.trim().split(/\r?\n/)[0] || output.trim().split(/\r?\n/)[0] || '';
          failure = `nvidia-smi failed (exit ${code})${detail === '' ? '' : `: ${detail.slice(0, 120)}`}`;
        } else if (signal === null && failure === '' && !stopping) {
          failure = `nvidia-smi ended early (exit ${code})`;
        }
        resolve();
      });
    });
    sampler.stdout.on('data', (chunk) => {
      output += chunk;
    });
    sampler.stderr.on('data', (chunk) => {
      errorText += chunk;
    });
  }

  function stopSampler() {
    stopping = true;
    if (sampler !== null && sampler.exitCode === null && sampler.signalCode === null) {
      try {
        sampler.kill();
      } catch (error) {
        failure = failure || `could not stop nvidia-smi: ${error.message}`;
      }
    }
  }

  function record(code) {
    if (!enabled || recorded) {
      return;
    }
    recorded = true;
    try {
      const parsed = parseSamples(output);
      const notes = [failure, parsed.unreadable > 0 ? `${parsed.unreadable} unreadable lines` : ''];
      const hold = {
        start: new Date(startedAt).toISOString(),
        heldSeconds: (Date.now() - startedAt) / 1000,
        exit: code,
        periodMs: SamplePeriodMs,
        ...summarizeSamples(parsed.samples),
        note: notes.filter((note) => note !== '').join('; '),
        command: commandText,
      };
      process.stderr.write(`[gpu-lock] ${describeHold(hold)}\n`);
      const header = fs.existsSync(logPath) ? '' : `${Columns.join('\t')}\n`;
      fs.appendFileSync(logPath, `${header}${formatRow(hold)}\n`);
    } catch (error) {
      process.stderr.write(`[gpu-lock] could not record the GPU hold: ${error.message}\n`);
    }
  }

  /** Stops the sampler, waits up to StopTimeoutMs for its last output and records the hold. Never rejects. */
  async function report(code) {
    stopSampler();
    if (closed !== null) {
      await Promise.race([closed, new Promise((resolve) => setTimeout(resolve, StopTimeoutMs))]);
    }
    record(code);
  }

  /** For a signal handler: stops the sampler and records what it printed so far. */
  function reportSync(code) {
    stopSampler();
    record(code);
  }

  return { start, report, reportSync, stop: stopSampler };
}

/** `gpu-lock.js --holds`: prints the summary of the log. Returns the exit code. */
function printHolds(logPath, sinceText) {
  let since = null;
  if (sinceText !== '') {
    since = Date.parse(sinceText);
    if (Number.isNaN(since)) {
      process.stderr.write(`--since needs a date such as 2026-11-14 or 2026-11-14T09:00, not "${sinceText}".\n`);
      return 2;
    }
  }
  let text = '';
  try {
    text = fs.readFileSync(logPath, 'utf8');
  } catch (error) {
    if (error.code !== 'ENOENT') {
      process.stderr.write(`could not read ${logPath}: ${error.message}\n`);
      return 1;
    }
  }
  const source = since === null ? logPath : `${logPath} since ${new Date(since).toISOString()}`;
  process.stdout.write(formatSummary(summarize(parseLog(text), since), source));
  return 0;
}

/** Tests of the pure functions; throws on a failure. */
function selfTest() {
  const assert = require('assert');
  // Sampler output: one GPU, a [N/A] line, a second GPU that is ignored, a partial last line
  const at = BusyThresholdPercent;
  const text =
    `0, 7, 3517\r\n1, 99, 9000\r\n0, [N/A], 3515\r\n0, ${at}, 3614\r\n0, ${at - 1}, 5000\r\n` + '0, 94, 4100\r\n0, 3';
  const parsed = parseSamples(text);
  assert.deepStrictEqual(parsed.samples, [
    { utilization: 7, memoryMiB: 3517 },
    { utilization: at, memoryMiB: 3614 },
    { utilization: at - 1, memoryMiB: 5000 },
    { utilization: 94, memoryMiB: 4100 },
  ]);
  assert.strictEqual(parsed.unreadable, 2);
  assert.deepStrictEqual(parseSamples(''), { samples: [], unreadable: 0 });

  // A sample at exactly the threshold is busy; the peak is neither the first nor the last sample
  const figures = summarizeSamples(parsed.samples);
  assert.deepStrictEqual(figures, {
    samples: 4,
    utilMeanPercent: (100 + 2 * at) / 4,
    busySharePercent: 50,
    memBaselineMiB: 3517,
    memPeakMiB: 5000,
  });
  assert.strictEqual(summarizeSamples(parsed.samples, 95).busySharePercent, 0);
  assert.strictEqual(summarizeSamples([]).memPeakMiB, null);

  // Rows round-trip through the log; a failed sampler leaves the GPU columns empty
  const hold = {
    start: '2026-11-14T09:00:00.000Z',
    heldSeconds: 200,
    exit: 0,
    periodMs: 1000,
    ...figures,
    note: '',
    command: `dotnet test\tX ${'y'.repeat(300)}`,
  };
  const row = formatRow(hold);
  assert.strictEqual(row.split('\t').length, Columns.length);
  assert.ok(row.endsWith('y...'));
  const failed = {
    ...hold,
    start: '2026-11-14T10:00:00.000Z',
    heldSeconds: 50,
    exit: 1,
    ...summarizeSamples([]),
    note: 'nvidia-smi could not run: ENOENT',
    command: 'just test',
  };
  assert.match(formatRow(failed), /\t1\t1000\t0\t\t\t\t\tnvidia-smi could not run: ENOENT\tjust test$/);
  assert.match(describeHold(failed), /GPU hold 50\.0 s, GPU not sampled \(nvidia-smi could not run: ENOENT\)$/);
  assert.match(describeHold(hold), /busy 50% of 4 samples .*memory 3517 -> peak 5000 MiB$/);
  const idle = {
    ...hold,
    start: '2026-11-14T11:00:00.000Z',
    heldSeconds: 600,
    busySharePercent: 10,
    utilMeanPercent: 5,
    memBaselineMiB: 3500,
    memPeakMiB: 6000,
    command: 'idle',
  };
  const log = [Columns.join('\t'), formatRow(hold), 'garbage', formatRow(failed), formatRow(idle), ''].join('\r\n');
  const holds = parseLog(log);
  assert.strictEqual(holds.length, 3);
  assert.strictEqual(holds[1].busySharePercent, null);

  // Weighted by held time: (200 * 50 + 600 * 10) / 800 = 20; the failed hold counts only in the held time
  const summary = summarize(holds);
  assert.strictEqual(summary.count, 3);
  assert.strictEqual(summary.measured, 2);
  assert.strictEqual(summary.heldSeconds, 850);
  assert.strictEqual(summary.busySharePercent, 20);
  assert.strictEqual(summary.utilMeanPercent, (200 * figures.utilMeanPercent + 600 * 5) / 800);
  assert.strictEqual(summary.idleSeconds, 640);
  // The largest peak and growth are in the last measured row, not the first
  assert.strictEqual(summary.memPeakMedianMiB, 5500);
  assert.strictEqual(summary.memPeakMaxMiB, 6000);
  assert.strictEqual(summary.memGrowthMedianMiB, (1483 + 2500) / 2);
  assert.strictEqual(summary.memGrowthMaxMiB, 2500);
  assert.deepStrictEqual(summary.busiest.map((h) => h.command), [holds[0].command, 'idle']);
  assert.deepStrictEqual(summary.idlest.map((h) => h.command), ['idle', holds[0].command]);
  assert.strictEqual(summarize(holds, Date.parse('2026-11-14T10:30:00Z')).count, 1);
  assert.strictEqual(median([3, 1, 2]), 2);
  const textSummary = formatSummary(summary, 'log');
  assert.match(textSummary, /3 \(2 with GPU samples\), held 14m 10s/);
  assert.match(textSummary, /busy \(samples >= \d+%\): 20\.0% of the measured held time \(13m 20s\), idle 10m 40s/);
  assert.match(formatSummary(summarize([]), 'log'), /No GPU holds/);
}

module.exports = {
  BusyThresholdPercent,
  DisableVariable,
  LogName,
  createHoldWatch,
  printHolds,
  selfTest,
};
