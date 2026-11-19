#!/usr/bin/env node
'use strict';

// Thin GPU lock facade over scripts/human/job-queue/lib/resource-lock.js and seat-lock.js.
//
// Preserves the legacy CLI flags, exit codes (command exit, 2, 3, 129, 130, 143), GSHARP_GPU_LOCK_HELD (seat 0),
// GSHARP_GPU_LOCK_PATH, --holds summary, and --self-test entry point while validating the 1-or-2-seat GPU lock
// configuration from job-queue.config.json (missing config -> seat 0 only; invalid config or wrong seats[0] -> exit 2).
//
// Usage:
//   node scripts/human/gpu-lock.js [--resource <name>] [--mode <exclusive|shared>] [--poll-seconds <n>]
//                                  [--timeout-minutes <n>] [--lock-path <dir>] -- <command> [args...]
//   node scripts/human/gpu-lock.js --holds [--since <date>] [--log <file>]
//   node scripts/human/gpu-lock.js --help
//   node scripts/human/gpu-lock.js --self-test

const assert = require('assert');
const { spawn, spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { runResourceLock, resolveSeats } = require('./job-queue/lib/resource-lock.js');
const { readSeatOwner, seatPathKey } = require('./job-queue/lib/seat-lock.js');
const holds = require('./job-queue/plugins/nvidia-gpu/holds.js');
const faultWatch = require('./gpu-fault-watch.js');

const usage = [
  'Usage: node scripts/human/gpu-lock.js [--resource <name>] [--mode <exclusive|shared>] [--poll-seconds <n>]',
  '                                      [--timeout-minutes <n>] [--lock-path <dir>] -- <command> [args...]',
  '',
  '  --resource <name>      Resource name to lock (default gpu).',
  '  --mode <mode>          Lock mode: exclusive or shared (default exclusive).',
  '  --poll-seconds <n>     Seconds between attempts to take a held lock (default 10).',
  '  --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
  '  --lock-path <dir>      Lock directory to use instead of <main checkout>/tmp/gpu-test-lock ' +
    '(for tests of this script only; seat 1 is <dir>-seat1).',
  '  --holds                Summarize the recorded GPU holds and exit (--since <date>, --log <file>).',
  '  --help                 Show this help.',
  '  --self-test            Check the hold record and summary, and that a run killed while it reads the kernel',
  '                         pools leaves a lock that is taken over.',
  '',
  'Windows: the kernel pools and the nvlddmkm Event 153 entries are read after the command starts and before the lock',
  'is released (about 0.1 s each), printed, appended to gpu-pool-log.tsv next to the lock directory, and a warning is',
  'printed when the pools grew by more than 1 GB or the nonpaged pool is above 6 GB. GSHARP_GPU_POOL_WATCH=0 turns',
  'this off.',
  '',
  'While the lock is held, one nvidia-smi process samples the GPU utilization and memory every second; at release one',
  'line is printed and a row appended to gpu-hold-log.tsv next to the lock directory (held time, mean utilization,',
  `share of samples at or above ${holds.BusyThresholdPercent}%, baseline and peak memory). ` +
    `${holds.DisableVariable}=0 turns this off.`,
  '',
  'A lock whose owner is on this host and platform and no longer running, together with the command it started, is',
  'taken over while waiting.',
  '',
  'Quote the command as one string when it needs pipes or redirection.',
].join('\n');

/** Resolves the deepest existing ancestor via `fs.realpathSync.native` so Windows 8.3 short paths never drift. */
function canonicalizePath(targetPath) {
  let current = path.resolve(targetPath);
  const trailing = [];
  for (;;) {
    try {
      const real = fs.realpathSync.native(current);
      return trailing.length === 0 ? real : path.join(real, ...trailing.reverse());
    } catch (error) {
      if (error && (error.code === 'ENOENT' || error.code === 'ENOTDIR')) {
        const parent = path.dirname(current);
        if (parent === current) {
          return path.resolve(targetPath);
        }
        trailing.push(path.basename(current));
        current = parent;
        continue;
      }
      return path.resolve(targetPath);
    }
  }
}

function normalizeArgvLockPath(argv) {
  const result = [...argv];
  const sep = result.indexOf('--');
  const limit = sep < 0 ? result.length : sep;
  for (let i = 0; i < limit - 1; i++) {
    if (result[i] === '--lock-path' && typeof result[i + 1] === 'string' && result[i + 1] !== '') {
      result[i + 1] = canonicalizePath(result[i + 1]);
      i++;
    }
  }
  return result;
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

function runChildAsync(execArgv, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, execArgv, {
      stdio: options.ipc ? ['ignore', 'pipe', 'pipe', 'ipc'] : ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
      env: options.env || process.env,
    });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => {
      stdout += chunk.toString('utf8');
    });
    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString('utf8');
    });
    child.on('error', reject);
    if (typeof options.onSpawn === 'function') {
      options.onSpawn(child);
    }
    child.on('close', (code, signal) => {
      resolve({ code, signal, stdout, stderr });
    });
  });
}

function validateGpuConfig(loaded, { resource }) {
  if (loaded.status !== 'ok' || resource !== 'gpu') {
    return null;
  }
  const raw = loaded.raw;
  const gpu = raw && raw.resources && typeof raw.resources === 'object' ? raw.resources.gpu : null;
  const seats = gpu && gpu.lock && typeof gpu.lock === 'object' ? gpu.lock.seats : null;
  if (!Array.isArray(seats) || (seats.length !== 1 && seats.length !== 2)) {
    return 'config resources.gpu.lock.seats must have 1 or 2 entries.';
  }
  if (seats.some((s) => typeof s !== 'string' || s.trim() === '')) {
    return 'config resources.gpu.lock.seats must contain non-empty strings.';
  }
  if (seats[0] !== 'tmp/gpu-test-lock') {
    return 'config resources.gpu.lock.seats[0] must be "tmp/gpu-test-lock".';
  }
  if (seats.length === 2) {
    const baseRoot = loaded.path ? path.dirname(loaded.path) : path.resolve(__dirname, '..', '..');
    if (seatPathKey(path.resolve(baseRoot, seats[0])) === seatPathKey(path.resolve(baseRoot, seats[1]))) {
      return 'config resources.gpu.lock.seats must resolve to distinct directories.';
    }
  }
  return null;
}

const gpuLockConfig = {
  scriptDir: __dirname,
  repoRoot: path.resolve(__dirname, '..', '..'),
  defaultResource: 'gpu',
  displayName: 'GPU',
  olderHolderLabel: 'an older gpu-lock.js',
  envNames: {
    held: 'GSHARP_GPU_LOCK_HELD',
    lockPath: 'GSHARP_GPU_LOCK_PATH',
    turn: 'GSHARP_JOB_QUEUE_GPU_TURN',
  },
  usageText: usage,
  validateConfig: validateGpuConfig,
  resolveDefaultSeats(_loaded, ctx) {
    return [path.join(canonicalizePath(ctx.sharedRoot), 'tmp', `${ctx.resource}-test-lock`)];
  },
  handleSpecialFlags(options, ctx) {
    if (!options.holds) {
      return null;
    }
    const logPath =
      options.log === '' ? path.join(path.dirname(ctx.lockPath), holds.LogName) : path.resolve(options.log);
    return holds.printHolds(logPath, options.since);
  },
  runExtraSelfTests,
};

/** Runs Check #13 (holds), Check #8 (2-seat kill/SIGTERM), Check #10 (facade), and Check #11 (fault-watch). */
async function runExtraSelfTests() {
  holds.selfTest();

  const dir = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'gpu-lock-self-test-')));
  const testLock = path.join(dir, 'lock');
  const testSeat1 = `${testLock}-seat1`;
  const delayVariable = faultWatch.DelayVariable;

  try {
    // Check 8a: 2-seat exclusive run hard-killed during 30s pool watch -> both stale seats taken over by next run.
    const first = spawn(
      process.execPath,
      [__filename, '--lock-path', testLock, '--', 'node -e "setTimeout(function () {}, 60000)"'],
      {
        stdio: 'ignore',
        windowsHide: true,
        env: { ...process.env, [delayVariable]: '30000', GSHARP_GPU_POOL_WATCH: '1', [holds.DisableVariable]: '0' },
      },
    );
    try {
      const [o0, o1] = await waitFor('both seats in owner.json with commandProcess', 20000, () => {
        const s0 = readSeatOwner(testLock);
        const s1 = readSeatOwner(testSeat1);
        return s0 !== null &&
          s1 !== null &&
          s0.stage === 'running' &&
          s1.stage === 'running' &&
          s0.commandProcess !== null &&
          s1.commandProcess !== null
          ? [s0, s1]
          : null;
      });
      assert.strictEqual(o0.pid, first.pid);
      assert.strictEqual(o1.pid, first.pid);

      if (process.platform === 'win32') {
        spawnSync('taskkill', ['/PID', String(first.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
      } else {
        process.kill(-o0.commandProcess.pgid, 'SIGKILL');
        process.kill(first.pid, 'SIGKILL');
      }
      await waitFor('the killed owner to exit', 10000, () =>
        first.exitCode !== null || first.signalCode !== null ? true : null,
      );
      assert.ok(fs.existsSync(path.join(testLock, 'owner.json')), 'killed run left seat 0 owner record');
      assert.ok(fs.existsSync(path.join(testSeat1, 'owner.json')), 'killed run left seat 1 owner record');

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
        {
          encoding: 'utf8',
          windowsHide: true,
          env: { ...process.env, GSHARP_GPU_POOL_WATCH: '0', [holds.DisableVariable]: '1' },
        },
      );
      assert.strictEqual(second.status, 5, second.stderr);
      assert.match(second.stderr, /took over a stale lock/);
      assert.ok(!fs.existsSync(testLock), 'second run released seat 0');
      assert.ok(!fs.existsSync(testSeat1), 'second run released seat 1');
      assert.match(second.stderr, /\[gpu-lock\] GPU hold \d+\.\d s/);
      const rows = fs.readFileSync(path.join(dir, holds.LogName), 'utf8').trim().split(/\r?\n/);
      assert.strictEqual(rows.length, 2);
      assert.match(rows[1], /^\S+\t\d+\.\d\t5\t1000\t\d+\t.*\texclusive$/);
    } finally {
      if (first.exitCode === null && first.signalCode === null) {
        first.kill('SIGKILL');
      }
    }

    // Check 8b: 2-seat exclusive child receiving SIGTERM through gpu-lock.js removes both seats and exits 143.
    const sigSeat0 = path.join(dir, 'sig-lock');
    const sigSeat1 = `${sigSeat0}-seat1`;
    let sigRef = null;
    const sigEnv = { ...process.env, GSHARP_GPU_POOL_WATCH: '0', [holds.DisableVariable]: '0' };
    const sigPromise =
      process.platform === 'win32'
        ? runChildAsync(
            [
              '-e',
              "const { runGpuLock } = require(process.argv[1]);\n" +
                "process.on('message', (msg) => { if (msg === 'sigterm') process.emit('SIGTERM'); });\n" +
                "runGpuLock(['--lock-path', process.argv[2], '--', " +
                "'node -e \"setTimeout(function () {}, 60000)\"']);\n",
              __filename,
              sigSeat0,
            ],
            { ipc: true, env: sigEnv, onSpawn: (c) => { sigRef = c; } },
          )
        : runChildAsync([__filename, '--lock-path', sigSeat0, '--', 'node -e "setTimeout(function () {}, 60000)"'], {
            env: sigEnv,
            onSpawn: (c) => { sigRef = c; },
          });

    await waitFor('both seats running before SIGTERM', 15000, () => {
      const s0 = readSeatOwner(sigSeat0);
      const s1 = readSeatOwner(sigSeat1);
      return s0 !== null &&
        s1 !== null &&
        s0.stage === 'running' &&
        s1.stage === 'running' &&
        s0.commandProcess !== null &&
        s1.commandProcess !== null
        ? true
        : null;
    });
    if (process.platform === 'win32') {
      sigRef.send('sigterm');
    } else {
      sigRef.kill('SIGTERM');
    }
    const sigResult = await sigPromise;
    assert.strictEqual(sigResult.code, 143, sigResult.stderr);
    assert.match(sigResult.stderr, /\[gpu-lock\] SIGTERM: stopping the command and releasing the lock/);
    assert.strictEqual(fs.existsSync(sigSeat0), false, 'SIGTERM removed seat 0');
    assert.strictEqual(fs.existsSync(sigSeat1), false, 'SIGTERM removed seat 1');

    // Check 10: facade path canonicalization, --lock-path <dir>-seat1, missing config -> 1 seat, wrong seats[0] -> 2.
    const cliLock = path.join(dir, 'cli-lock');
    const cliSeats = resolveSeats({ options: { resource: 'gpu', lockPath: cliLock }, config: gpuLockConfig, env: {} });
    assert.deepStrictEqual(cliSeats.map(canonicalizePath), [cliLock, `${cliLock}-seat1`]);

    const envSeats = resolveSeats({
      options: { resource: 'gpu', lockPath: '' },
      config: gpuLockConfig,
      env: { GSHARP_GPU_LOCK_PATH: cliLock },
    });
    assert.deepStrictEqual(envSeats.map(canonicalizePath), [cliLock, `${cliLock}-seat1`]);

    const liveCmd =
      "node -e \"const f=require('fs'),e=process.env;" +
      'if(f.realpathSync.native(e.GSHARP_GPU_LOCK_HELD)!==e.E0)process.exit(9);' +
      'if(!f.existsSync(e.E0))process.exit(10);' +
      'if(!f.existsSync(e.E1))process.exit(11)"';
    const liveRun = spawnSync(process.execPath, [__filename, '--lock-path', cliLock, '--', liveCmd], {
      encoding: 'utf8',
      windowsHide: true,
      env: {
        ...process.env,
        GSHARP_GPU_POOL_WATCH: '0',
        [holds.DisableVariable]: '0',
        E0: cliLock,
        E1: `${cliLock}-seat1`,
      },
    });
    assert.strictEqual(liveRun.status, 0, liveRun.stderr);
    assert.strictEqual(fs.existsSync(cliLock), false, 'live run released seat 0');
    assert.strictEqual(fs.existsSync(`${cliLock}-seat1`), false, 'live run released seat 1');

    const missingLoaded = { status: 'missing', path: null, raw: null, error: null };
    assert.strictEqual(validateGpuConfig(missingLoaded, { resource: 'gpu' }), null);
    const missingSeats = resolveSeats({
      options: { resource: 'gpu', lockPath: '' },
      loaded: missingLoaded,
      config: gpuLockConfig,
      sharedRoot: dir,
      env: {},
    });
    assert.deepStrictEqual(missingSeats.map(canonicalizePath), [path.join(dir, 'tmp', 'gpu-test-lock')]);

    const wrongCfg = path.join(dir, 'wrong-seats.json');
    fs.writeFileSync(
      wrongCfg,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [],
        resources: { gpu: { slots: 1, lock: { seats: ['tmp/wrong-lock', 'tmp/wrong-lock-seat1'] } } },
      }),
    );
    const wrongRes = spawnSync(process.execPath, [__filename, '--', 'node -e "process.exit(0)"'], {
      encoding: 'utf8',
      windowsHide: true,
      env: { ...process.env, JOB_QUEUE_CONFIG: wrongCfg },
    });
    assert.strictEqual(wrongRes.status, 2, wrongRes.stderr);
    assert.match(wrongRes.stderr, /tmp\/gpu-test-lock/);

    const noSeatsCfg = path.join(dir, 'no-seats.json');
    fs.writeFileSync(
      noSeatsCfg,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [],
        resources: { gpu: { slots: 1 } },
      }),
    );
    const noSeatsRes = spawnSync(process.execPath, [__filename, '--', 'node -e "process.exit(0)"'], {
      encoding: 'utf8',
      windowsHide: true,
      env: { ...process.env, JOB_QUEUE_CONFIG: noSeatsCfg },
    });
    assert.strictEqual(noSeatsRes.status, 2, noSeatsRes.stderr);
    assert.match(noSeatsRes.stderr, /seats/);

    const brokenCfg = path.join(dir, 'broken.json');
    fs.writeFileSync(brokenCfg, '{');
    const brokenRes = spawnSync(process.execPath, [__filename, '--', 'node -e "process.exit(0)"'], {
      encoding: 'utf8',
      windowsHide: true,
      env: { ...process.env, JOB_QUEUE_CONFIG: brokenCfg },
    });
    assert.strictEqual(brokenRes.status, 2, brokenRes.stderr);
    assert.match(brokenRes.stderr, /invalid config/);

    // Check 11: gpu-fault-watch.js exports exact 11 keys and --json exits 0 with valid JSON.
    const expectedFaultExports = [
      'DefaultGrowthWarnBytes',
      'DefaultNonpagedWarnBytes',
      'DelayVariable',
      'DisableVariable',
      'compare',
      'describe',
      'isNonpagedHigh',
      'measure',
      'measureAsync',
      'startWarningText',
      'warningText',
    ];
    const actualFaultExports = Object.keys(require(path.join(__dirname, 'gpu-fault-watch.js'))).sort();
    assert.deepStrictEqual(actualFaultExports, expectedFaultExports);

    const fwJson = spawnSync(process.execPath, [path.join(__dirname, 'gpu-fault-watch.js'), '--json'], {
      encoding: 'utf8',
      windowsHide: true,
    });
    assert.strictEqual(fwJson.status, 0, fwJson.stderr);
    const parsedFw = JSON.parse(fwJson.stdout);
    assert.strictEqual(typeof parsedFw.supported, 'boolean');
    assert.ok(Array.isArray(parsedFw.errors));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function runGpuLock(argv = process.argv.slice(2)) {
  if (typeof process.env.GSHARP_GPU_LOCK_PATH === 'string' && process.env.GSHARP_GPU_LOCK_PATH !== '') {
    process.env.GSHARP_GPU_LOCK_PATH = canonicalizePath(process.env.GSHARP_GPU_LOCK_PATH);
  }
  return runResourceLock({
    ...gpuLockConfig,
    argv: normalizeArgvLockPath(argv),
  });
}

module.exports = { runGpuLock, gpuLockConfig, canonicalizePath };

if (require.main === module) {
  runGpuLock(process.argv.slice(2)).catch((error) => {
    process.stderr.write(`${error.stack || error.message}\n`);
    process.exit(1);
  });
}
