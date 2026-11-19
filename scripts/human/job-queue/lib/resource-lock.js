'use strict';

// Generic resource lock runner: argument parsing, configuration loading, wait backoff, holder watch lifecycle,
// shared-mode overlap reporting, signal cleanup, and per-seat owner diagnostics.

const assert = require('node:assert');
const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { loadConfigFile } = require('./config-file.js');
const { mainCheckout } = require('./main-checkout.js');
const {
  createSeatLock,
  readSeatOwner,
  formatSeatOwners,
  seatPathKey,
  selfTest: seatLockSelfTest,
} = require('./seat-lock.js');

const ResourceNamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const OverlapPollIntervalMs = 2000;
const NoticeIntervalMs = 300000;
const FastWaitWindowMs = 10000;

/** A job id as people read it: j and the last four digits. A copy of formatJobId in ../job-id.ts, which a .js file cannot import. */
function formatJobId(id) {
  return `j${String(id % 10000).padStart(4, '0')}`;
}

class UsageError extends Error {
  constructor(message) {
    super(message);
    this.name = 'UsageError';
  }
}

/**
 * Builds neutral default usage text when a facade does not supply its own.
 * @param {string} defaultResource
 * @returns {string}
 */
function buildDefaultUsage(defaultResource) {
  return [
    `Usage: node resource-lock.js [--resource <name>] [--mode shared|exclusive] [--poll-seconds <n>]`,
    '                              [--timeout-minutes <n>] [--lock-path <dir>] -- <command> [args...]',
    '',
    `  --resource <name>      Resource name to lock (default ${defaultResource}).`,
    '  --mode <mode>          Lock mode: exclusive or shared (default exclusive).',
    '  --poll-seconds <n>     Seconds between attempts after the initial wait window (default 10).',
    '  --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever).',
    '  --lock-path <dir>      Seat 0 lock directory override (seat 1 is <dir>-seat1).',
    '  --holds                Summarize recorded holds when supported by the caller.',
    '  --since <date>         With --holds: include holds at or after this date.',
    '  --log <file>           With --holds: read this log file instead.',
    '  --help                 Show this help.',
    '  --self-test            Run the built-in self-tests.',
  ].join('\n');
}

function parseNonNegativeNumber(name, text) {
  const value = Number(text);
  if (text === undefined || text === '' || !Number.isFinite(value) || value < 0) {
    throw new UsageError(`${name} needs a non-negative number.`);
  }
  return value;
}

/**
 * Parses CLI arguments for the resource lock runner.
 * @param {readonly string[]} argv
 * @param {{ defaultResource?: string }} [config]
 * @returns {{
 *   resource: string,
 *   mode: 'exclusive' | 'shared',
 *   pollSeconds: number,
 *   timeoutMinutes: number,
 *   lockPath: string,
 *   command: string[],
 *   selfTest: boolean,
 *   holds: boolean,
 *   since: string,
 *   log: string,
 *   help: boolean,
 * }}
 */
function parseArgs(argv, config = {}) {
  const defaultResource =
    typeof config.defaultResource === 'string' && config.defaultResource !== ''
      ? config.defaultResource
      : 'resource';
  const separator = argv.indexOf('--');
  const own = separator < 0 ? argv : argv.slice(0, separator);
  const command = separator < 0 ? [] : argv.slice(separator + 1);
  const options = {
    resource: defaultResource,
    mode: 'exclusive',
    pollSeconds: 10,
    timeoutMinutes: 0,
    lockPath: '',
    command,
    selfTest: false,
    holds: false,
    since: '',
    log: '',
    help: false,
  };

  for (let i = 0; i < own.length; i++) {
    switch (own[i]) {
      case '--help':
      case '-h':
        options.help = true;
        return options;
      case '--self-test':
        options.selfTest = true;
        break;
      case '--holds':
        options.holds = true;
        break;
      case '--since':
      case '--log': {
        const name = own[i];
        const value = own[++i] || '';
        if (value === '') {
          throw new UsageError(`${name} needs a value.`);
        }
        options[name.slice(2)] = value;
        break;
      }
      case '--resource': {
        const value = own[++i] || '';
        if (!ResourceNamePattern.test(value)) {
          throw new UsageError(`--resource must match ${ResourceNamePattern.source}.`);
        }
        options.resource = value;
        break;
      }
      case '--mode': {
        const value = own[++i] || '';
        if (value !== 'exclusive' && value !== 'shared') {
          throw new UsageError('--mode must be "exclusive" or "shared".');
        }
        options.mode = value;
        break;
      }
      case '--poll-seconds':
        options.pollSeconds = parseNonNegativeNumber('--poll-seconds', own[++i]);
        break;
      case '--timeout-minutes':
        options.timeoutMinutes = parseNonNegativeNumber('--timeout-minutes', own[++i]);
        break;
      case '--lock-path': {
        const value = own[++i] || '';
        if (value === '') {
          throw new UsageError('--lock-path needs a directory.');
        }
        options.lockPath = value;
        break;
      }
      default:
        throw new UsageError(`Unknown option: ${own[i]}`);
    }
  }

  if (options.pollSeconds <= 0) {
    throw new UsageError('--poll-seconds must be greater than 0.');
  }
  if ((options.since !== '' || options.log !== '') && !options.holds) {
    throw new UsageError('--since and --log go with --holds.');
  }
  if (command.length === 0 && !options.selfTest && !options.holds) {
    throw new UsageError('No command given. Put it after "--".');
  }
  return options;
}

/**
 * Validates resource lock and holderWatch entries in a loaded configuration object.
 * Returns an error message string when invalid, or null when valid.
 * @param {{ status: string, path: string | null, raw: Record<string, unknown> | null, error: string | null }} loaded
 * @param {string} resource
 * @param {string} [baseRoot]
 * @returns {string | null}
 */
function validateResourceSection(loaded, resource, baseRoot) {
  if (loaded.status !== 'ok' || !loaded.raw) {
    return null;
  }
  const resources = loaded.raw.resources;
  if (resources === undefined) {
    return null;
  }
  if (resources === null || typeof resources !== 'object' || Array.isArray(resources)) {
    return 'config resources must be an object.';
  }
  const section = resources[resource];
  if (section === undefined) {
    return null;
  }
  if (section === null || typeof section !== 'object' || Array.isArray(section)) {
    return `config resources.${resource} must be an object.`;
  }
  if (section.lock !== undefined) {
    if (section.lock === null || typeof section.lock !== 'object' || Array.isArray(section.lock)) {
      return `config resources.${resource}.lock must be an object.`;
    }
    if (section.lock.seats !== undefined) {
      const seats = section.lock.seats;
      if (
        !Array.isArray(seats) ||
        (seats.length !== 1 && seats.length !== 2) ||
        seats.some((s) => typeof s !== 'string' || s.trim() === '')
      ) {
        return `config resources.${resource}.lock.seats must be 1 or 2 non-empty strings.`;
      }
      if (seats.length === 2) {
        const rootDir = baseRoot || (loaded.path ? path.dirname(loaded.path) : process.cwd());
        if (seatPathKey(path.resolve(rootDir, seats[0])) === seatPathKey(path.resolve(rootDir, seats[1]))) {
          return `config resources.${resource}.lock.seats must resolve to distinct directories.`;
        }
      }
    }
  }
  if (section.holderWatch !== undefined) {
    if (
      !Array.isArray(section.holderWatch) ||
      section.holderWatch.some((entry) => typeof entry !== 'string' || entry.trim() === '')
    ) {
      return `config resources.${resource}.holderWatch must be an array of non-empty strings.`;
    }
  }
  return null;
}

/**
 * Resolves the 1 or 2 seat directories from CLI/environment overrides, custom callback, config, or defaults.
 * @param {{
 *   options: { resource: string, lockPath: string },
 *   loaded?: { status: string, path: string | null, raw: Record<string, unknown> | null },
 *   config?: {
 *     scriptDir?: string,
 *     repoRoot?: string,
 *     envNames?: { lockPath?: string },
 *     resolveDefaultSeats?: (loaded: unknown, ctx: {
 *       resource: string,
 *       sharedRoot: string,
 *       scriptDir: string,
 *       repoRoot: string,
 *     }) => string[],
 *   },
 *   sharedRoot?: string,
 * }} params
 * @returns {string[]}
 */
function resolveSeats(params) {
  const { options, loaded, config = {}, sharedRoot, env = process.env } = params;
  const lockPathVar = config.envNames && config.envNames.lockPath ? config.envNames.lockPath : '';
  const envLockPath = lockPathVar !== '' ? env[lockPathVar] || '' : '';
  const lockOverride = options.lockPath || envLockPath || '';
  if (lockOverride !== '') {
    const seat0 = path.resolve(lockOverride);
    return [seat0, `${seat0}-seat1`];
  }

  const scriptDir = config.scriptDir || path.resolve(__dirname, '..', '..');
  const repoRoot = config.repoRoot || path.resolve(__dirname, '..', '..', '..', '..');
  const baseRoot = sharedRoot || mainCheckout(scriptDir, repoRoot);

  const configSeats =
    loaded &&
    loaded.status === 'ok' &&
    loaded.raw &&
    loaded.raw.resources &&
    loaded.raw.resources[options.resource] &&
    loaded.raw.resources[options.resource].lock &&
    loaded.raw.resources[options.resource].lock.seats;
  if (Array.isArray(configSeats) && configSeats.length > 0) {
    return configSeats.map((s) => path.resolve(baseRoot, s));
  }

  if (typeof config.resolveDefaultSeats === 'function') {
    const custom = config.resolveDefaultSeats(loaded, {
      resource: options.resource,
      sharedRoot: baseRoot,
      scriptDir,
      repoRoot,
    });
    if (Array.isArray(custom) && custom.length > 0) {
      return custom.map((s) => (path.isAbsolute(s) ? s : path.resolve(baseRoot, s)));
    }
  }

  return [path.join(baseRoot, 'tmp', `${options.resource}-test-lock`)];
}

/**
 * Dynamically loads CJS holder-watch factories listed under `resources.<resource>.holderWatch`.
 * Load errors log a single line to stderr and are skipped so locking continues without watches.
 * @param {string} prefix
 * @param {string} resource
 * @param {{ status: string, path: string | null, raw: Record<string, unknown> | null }} loaded
 * @param {string} scriptDir
 * @returns {Array<{ relPath: string, createWatch: Function }>}
 */
function loadWatchFactories(prefix, resource, loaded, scriptDir) {
  const entries =
    loaded.status === 'ok' &&
    loaded.raw &&
    loaded.raw.resources &&
    loaded.raw.resources[resource] &&
    Array.isArray(loaded.raw.resources[resource].holderWatch)
      ? loaded.raw.resources[resource].holderWatch
      : [];
  const configRoot = loaded.path ? path.dirname(loaded.path) : scriptDir;
  const factories = [];
  for (const relPath of entries) {
    try {
      const resolved = path.resolve(configRoot, relPath);
      const mod = require(resolved);
      if (!mod || typeof mod.createWatch !== 'function') {
        throw new Error('missing createWatch export');
      }
      factories.push({ relPath, createWatch: mod.createWatch });
    } catch (error) {
      process.stderr.write(`${prefix} holder watch unavailable (${relPath}): ${error.message}\n`);
    }
  }
  return factories;
}

/**
 * Formats the one-line shared-mode failure overlap summary to stderr.
 * @param {Map<string, { pid: number, turn: unknown, command: unknown, hasMode: boolean }>} observedHolders
 * @param {string} prefix
 * @param {string} displayName
 * @param {string} olderHolderLabel
 */
function writeSharedFailureReport(observedHolders, prefix, displayName, olderHolderLabel) {
  if (observedHolders.size === 0) {
    process.stderr.write(`${prefix} the command failed; it shared the ${displayName} with no other job\n`);
    return;
  }
  const items = [];
  for (const holder of observedHolders.values()) {
    if (!holder.hasMode) {
      items.push(`${olderHolderLabel} (pid ${holder.pid})`);
      continue;
    }
    const cmd =
      typeof holder.command === 'string' && holder.command.trim() !== ''
        ? holder.command.replace(/\s+/g, ' ').trim()
        : '(unknown)';
    if (Number.isInteger(holder.turn) && holder.turn > 0) {
      items.push(`${formatJobId(holder.turn)} ${cmd} (pid ${holder.pid})`);
    } else {
      items.push(`${cmd} (pid ${holder.pid})`);
    }
  }
  process.stderr.write(`${prefix} the command failed while it shared the ${displayName} with: ${items.join(', ')}\n`);
}

/**
 * Runs the resource lock CLI/runner with caller-supplied configuration and hooks.
 * @param {{
 *   argv?: readonly string[],
 *   scriptDir?: string,
 *   repoRoot?: string,
 *   configPath?: string,
 *   defaultResource?: string,
 *   displayName?: string,
 *   olderHolderLabel?: string,
 *   envNames?: { held?: string, lockPath?: string, turn?: string },
 *   usageText?: string,
 *   validateConfig?: (loaded: unknown, ctx: { resource: string, options: unknown }) => unknown,
 *   resolveDefaultSeats?: (loaded: unknown, ctx: unknown) => string[],
 *   handleSpecialFlags?: (options: unknown, ctx: {
 *     seatPaths: string[],
 *     lockPath: string,
 *     loaded: unknown,
 *     configRoot: string,
 *   }) => number | Promise<number> | null | undefined,
 *   runExtraSelfTests?: () => void | Promise<void>,
 * }} [config]
 */
async function runResourceLock(config = {}) {
  const argv = config.argv || process.argv.slice(2);
  const defaultResource =
    typeof config.defaultResource === 'string' && config.defaultResource !== ''
      ? config.defaultResource
      : 'resource';
  const usageText =
    typeof config.usageText === 'string' && config.usageText !== ''
      ? config.usageText
      : buildDefaultUsage(defaultResource);

  let options;
  try {
    options = parseArgs(argv, { defaultResource });
  } catch (error) {
    if (error instanceof UsageError) {
      process.stderr.write(`${error.message}\n${usageText}\n`);
      process.exit(2);
    }
    throw error;
  }

  if (options.help) {
    process.stdout.write(`${usageText}\n`);
    process.exit(0);
  }

  const prefix = `[${options.resource}-lock]`;

  if (options.selfTest) {
    try {
      await seatLockSelfTest();
      await selfTest();
      if (typeof config.runExtraSelfTests === 'function') {
        await config.runExtraSelfTests();
      }
      process.stdout.write(`${options.resource}-lock self-test passed\n`);
      process.exit(0);
    } catch (error) {
      process.stderr.write(`${options.resource}-lock self-test failed: ${error.stack || error.message}\n`);
      process.exit(1);
    }
  }

  const scriptDir = config.scriptDir || path.resolve(__dirname, '..', '..');
  const repoRoot = config.repoRoot || path.resolve(__dirname, '..', '..', '..', '..');
  const loaded = loadConfigFile(scriptDir, config.configPath);

  if (loaded.status === 'invalid') {
    const loc = loaded.path ? ` (${loaded.path})` : '';
    process.stderr.write(`${prefix} invalid config${loc}: ${loaded.error}\n`);
    process.exit(2);
  }

  const sharedRoot = loaded.status === 'ok' ? mainCheckout(scriptDir, repoRoot) : undefined;
  const sectionError = validateResourceSection(loaded, options.resource, sharedRoot);
  if (sectionError !== null) {
    process.stderr.write(`${prefix} ${sectionError}\n`);
    process.exit(2);
  }

  if (typeof config.validateConfig === 'function') {
    try {
      const verdict = config.validateConfig(loaded, { resource: options.resource, options });
      if (typeof verdict === 'string' && verdict !== '') {
        process.stderr.write(`${prefix} ${verdict}\n`);
        process.exit(2);
      }
      if (verdict && typeof verdict === 'object' && verdict.ok === false) {
        process.stderr.write(`${prefix} ${verdict.message || 'invalid config'}\n`);
        process.exit(2);
      }
    } catch (error) {
      process.stderr.write(`${prefix} ${error.message}\n`);
      process.exit(2);
    }
  }

  const seatPaths = resolveSeats({ options, loaded, config, sharedRoot });

  if (options.holds) {
    if (typeof config.handleSpecialFlags === 'function') {
      const configRoot = loaded.path ? path.dirname(loaded.path) : scriptDir;
      const code = await config.handleSpecialFlags(options, {
        seatPaths,
        lockPath: seatPaths[0],
        loaded,
        configRoot,
      });
      if (typeof code === 'number') {
        process.exit(code);
      }
    }
    process.stderr.write(`--holds is not supported for this resource.\n${usageText}\n`);
    process.exit(2);
  }

  const displayName =
    typeof config.displayName === 'string' && config.displayName !== '' ? config.displayName : options.resource;
  const olderHolderLabel =
    typeof config.olderHolderLabel === 'string' && config.olderHolderLabel !== ''
      ? config.olderHolderLabel
      : 'an older lock holder';
  const turnVar = config.envNames && config.envNames.turn ? config.envNames.turn : '';
  const rawTurn = turnVar !== '' ? Number(process.env[turnVar] || '') : 0;
  const turn = Number.isInteger(rawTurn) && rawTurn > 0 ? rawTurn : 0;

  const commandText = options.command.join(' ');
  const seatLock = createSeatLock(seatPaths, commandText, { mode: options.mode, turn, prefix });
  const watchFactories = loadWatchFactories(prefix, options.resource, loaded, scriptDir);

  /** @type {Array<{ relPath: string, watch: Record<string, Function> }>} */
  const activeWatches = [];
  let child = null;
  let overlapTimer = null;
  let waitTimer = null;
  let signalHandled = false;
  const observedHolders = new Map();
  let heldSeatIndex = 0;

  function stopOverlapTimer() {
    if (overlapTimer !== null) {
      clearInterval(overlapTimer);
      overlapTimer = null;
    }
  }

  function pollOtherSeats() {
    if (options.mode !== 'shared' || seatPaths.length < 2) {
      return;
    }
    for (let i = 0; i < seatPaths.length; i++) {
      if (i === heldSeatIndex || !fs.existsSync(seatPaths[i])) {
        continue;
      }
      const record = readSeatOwner(seatPaths[i]);
      if (record === null) {
        continue;
      }
      const hasMode = typeof record.mode === 'string' && record.mode !== '';
      const key = `${record.pid}:${record.startedAt || ''}:${record.turn ?? ''}:${record.command || ''}:${
        hasMode ? record.mode : 'legacy'
      }`;
      if (!observedHolders.has(key)) {
        observedHolders.set(key, {
          pid: record.pid,
          turn: record.turn,
          command: record.command,
          hasMode,
        });
      }
    }
  }

  function safeWatchSync(relPath, fn) {
    try {
      fn();
    } catch (error) {
      process.stderr.write(`${prefix} holder watch error (${relPath}): ${error.message}\n`);
    }
  }

  async function safeWatchAsync(relPath, fn) {
    try {
      await fn();
    } catch (error) {
      process.stderr.write(`${prefix} holder watch error (${relPath}): ${error.message}\n`);
    }
  }

  function killChild() {
    if (child === null || child.exitCode !== null || child.pid === undefined) {
      return;
    }
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
      return;
    }
    try {
      process.kill(-child.pid, 'SIGTERM');
    } catch (error) {
      if (error.code !== 'ESRCH') {
        process.stderr.write(`${prefix} could not stop the command's process group ${child.pid}: ${error.message}\n`);
      }
    }
  }

  function onSignal(signal, code) {
    if (signalHandled) {
      return;
    }
    signalHandled = true;
    if (waitTimer !== null) {
      clearTimeout(waitTimer);
      waitTimer = null;
    }
    stopOverlapTimer();
    process.stderr.write(`${prefix} ${signal}: stopping the command and releasing the lock\n`);
    killChild();
    for (const { relPath, watch } of activeWatches) {
      if (typeof watch.reportSync === 'function') {
        safeWatchSync(relPath, () => watch.reportSync(code));
      }
    }
    seatLock.releaseLock();
    process.exit(code);
  }

  process.on('SIGINT', () => onSignal('SIGINT', 130));
  process.on('SIGTERM', () => onSignal('SIGTERM', 143));
  process.on('SIGHUP', () => onSignal('SIGHUP', 129));
  process.on('exit', () => {
    stopOverlapTimer();
    for (const { relPath, watch } of activeWatches) {
      if (typeof watch.stop === 'function') {
        safeWatchSync(relPath, () => watch.stop());
      }
    }
    seatLock.releaseLock();
  });

  function runCommand() {
    const heldSeats = seatLock.getHeldSeats();
    heldSeatIndex = heldSeats.length > 0 ? heldSeats[0].seatIndex : 0;

    for (const { relPath, createWatch } of watchFactories) {
      safeWatchSync(relPath, () => {
        const watch = createWatch({
          lockPath: seatPaths[0],
          commandText,
          resource: options.resource,
          mode: options.mode,
          seat: heldSeatIndex,
        });
        if (watch) {
          activeWatches.push({ relPath, watch });
        }
      });
    }

    seatLock.markRunning();

    for (const { relPath, watch } of activeWatches) {
      if (typeof watch.start === 'function') {
        safeWatchSync(relPath, () => watch.start());
      }
    }

    if (options.mode === 'shared') {
      pollOtherSeats();
      if (seatPaths.length > 1) {
        overlapTimer = setInterval(pollOtherSeats, OverlapPollIntervalMs);
        if (typeof overlapTimer.unref === 'function') {
          overlapTimer.unref();
        }
      }
    }

    const env = { ...process.env };
    if (config.envNames && config.envNames.held) {
      env[config.envNames.held] = seatPaths[0];
    }

    const spawnedAt = Date.now();
    child = spawn(commandText, { stdio: 'inherit', shell: true, env, detached: process.platform !== 'win32' });
    seatLock.recordCommandProcess(child, spawnedAt);

    for (const { relPath, watch } of activeWatches) {
      if (typeof watch.afterSpawn === 'function') {
        safeWatchSync(relPath, () => watch.afterSpawn());
      }
    }

    child.on('error', (error) => {
      stopOverlapTimer();
      process.stderr.write(`${prefix} could not start the command: ${error.message}\n`);
      for (const { relPath, watch } of activeWatches) {
        if (typeof watch.stop === 'function') {
          safeWatchSync(relPath, () => watch.stop());
        }
      }
      seatLock.releaseLock();
      process.exit(1);
    });

    child.on('close', (code) => {
      pollOtherSeats();
      stopOverlapTimer();
      Promise.all(
        activeWatches.map(({ relPath, watch }) =>
          typeof watch.report === 'function' ? safeWatchAsync(relPath, () => watch.report(code)) : Promise.resolve(),
        ),
      ).then(() => {
        if (options.mode === 'shared' && code !== 0) {
          writeSharedFailureReport(observedHolders, prefix, displayName, olderHolderLabel);
        }
        seatLock.releaseLock();
        process.exit(code === null ? 1 : code);
      });
    });
  }

  const startedAt = Date.now();
  const timeoutMs = options.timeoutMinutes * 60 * 1000;
  let lastNotice = -Infinity;

  function attempt() {
    if (signalHandled) {
      return;
    }
    const acq = seatLock.tryAcquire();
    if (acq.acquired) {
      runCommand();
      return;
    }
    const waitedMs = Date.now() - startedAt;
    if (timeoutMs > 0 && waitedMs >= timeoutMs) {
      process.stderr.write(
        `${prefix} gave up after ${options.timeoutMinutes} minutes; ${seatPaths[0]} is held by:\n` +
          `${formatSeatOwners(seatPaths)}\n` +
          `${prefix} if that process is gone, remove the directory by hand.\n`,
      );
      process.exit(3);
    }
    if (waitedMs - lastNotice >= NoticeIntervalMs || lastNotice === -Infinity) {
      lastNotice = waitedMs;
      process.stderr.write(
        `${prefix} waiting for ${seatPaths[0]} (${Math.round(waitedMs / 1000)}s), held by:\n` +
          `${formatSeatOwners(seatPaths)}\n`,
      );
    }
    const delayMs =
      waitedMs < FastWaitWindowMs
        ? options.pollSeconds < 1
          ? options.pollSeconds * 1000
          : 500 + Math.floor(Math.random() * 1001)
        : options.pollSeconds * 1000;
    waitTimer = setTimeout(attempt, delayMs);
  }

  attempt();
}

/** Polls until `check` returns a non-null value or throws on timeout. */
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
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

/** Spawns a child process and resolves with `{ code, signal, stdout, stderr }`. */
function runChild(execArgv, options = {}) {
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

/**
 * Runs Checks #8 and #9 for the generic resource lock runner.
 */
async function selfTest() {
  const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'resource-lock-self-test-')));
  try {
    // Check 8: exclusive child holding both seats receives SIGTERM -> both seats removed, exit 143.
    {
      const seat0 = path.join(root, 'check8-lock');
      const seat1 = `${seat0}-seat1`;
      let childRef = null;
      const harnessCode =
        "const { runResourceLock } = require(process.argv[1]);\n" +
        "if (process.platform === 'win32') {\n" +
        "  process.on('message', (msg) => { if (msg === 'sigterm') process.emit('SIGTERM'); });\n" +
        "}\n" +
        "runResourceLock({ argv: ['--resource', 'res', '--lock-path', process.argv[2], '--', " +
        "'node -e \"setTimeout(function () {}, 60000)\"'], defaultResource: 'res' });\n";

      const resultPromise = runChild(['-e', harnessCode, __filename, seat0], {
        ipc: process.platform === 'win32',
        onSpawn(child) {
          childRef = child;
        },
      });

      await waitFor('both seats running with commandProcess', 15000, () => {
        const o0 = readSeatOwner(seat0);
        const o1 = readSeatOwner(seat1);
        return o0 !== null &&
          o1 !== null &&
          o0.stage === 'running' &&
          o1.stage === 'running' &&
          o0.commandProcess !== null &&
          o1.commandProcess !== null
          ? true
          : null;
      });

      if (process.platform === 'win32') {
        childRef.send('sigterm');
      } else {
        childRef.kill('SIGTERM');
      }

      const res = await resultPromise;
      assert.strictEqual(res.code, 143, `expected exit 143, got ${res.code}; stderr: ${res.stderr}`);
      assert.match(res.stderr, /\[res-lock\] SIGTERM: stopping the command and releasing the lock/);
      assert.strictEqual(fs.existsSync(seat0), false, 'seat 0 should be removed after SIGTERM');
      assert.strictEqual(fs.existsSync(seat1), false, 'seat 1 should be removed after SIGTERM');
    }

    // Check 9: shared overlap reporting (modern turn holder + legacy holder, exit 0 silent, no-other-job).
    {
      const seat0 = path.join(root, 'check9-lock');
      const seat1 = `${seat0}-seat1`;
      const gateFile = path.join(root, 'check9-gate.txt');

      fs.mkdirSync(seat0, { recursive: true });
      fs.writeFileSync(
        path.join(seat0, 'owner.json'),
        JSON.stringify({
          pid: process.pid,
          host: os.hostname(),
          platform: process.platform,
          startedAt: new Date().toISOString(),
          cwd: process.cwd(),
          command: 'other-shared-cmd',
          commandProcess: null,
          mode: 'shared',
          stage: 'running',
          turn: 42,
          seat: 0,
        }),
      );

      const childScript =
        "const fs = require('node:fs'); const g = process.env.TEST_GATE_FILE; " +
        'const t = setInterval(() => { if (fs.existsSync(g)) { clearInterval(t); process.exit(1); } }, 50);';
      const harness9a =
        'const { runResourceLock } = require(process.argv[1]);\n' +
        "runResourceLock({ argv: ['--resource', 'res', '--mode', 'shared', '--lock-path', process.argv[2], '--', " +
        'process.argv[3]], defaultResource: "res", displayName: "device", ' +
        'olderHolderLabel: "an older lock holder" });\n';

      const res9aPromise = runChild(
        ['-e', harness9a, __filename, seat0, `node -e ${JSON.stringify(childScript)}`],
        { env: { ...process.env, TEST_GATE_FILE: gateFile } },
      );

      await waitFor('seat 1 running in check 9a', 15000, () => {
        const o1 = readSeatOwner(seat1);
        return o1 !== null && o1.stage === 'running' && o1.commandProcess !== null ? o1 : null;
      });

      // Swap seat 0 to a legacy 7-field v1 owner record (lock.js:156-164, no `mode` field).
      fs.writeFileSync(
        path.join(seat0, 'owner.json'),
        JSON.stringify({
          pid: process.pid,
          host: os.hostname(),
          platform: process.platform,
          startedAt: new Date(Date.now() + 1).toISOString(),
          cwd: process.cwd(),
          command: 'legacy-cmd',
          commandProcess: null,
        }),
      );

      await new Promise((resolve) => setTimeout(resolve, 2200));
      fs.writeFileSync(gateFile, 'go');

      const res9a = await res9aPromise;
      assert.strictEqual(res9a.code, 1, res9a.stderr);
      assert.match(
        res9a.stderr,
        new RegExp(
          `\\[res-lock\\] the command failed while it shared the device with: ` +
            `#42 other-shared-cmd \\(pid ${process.pid}\\), an older lock holder \\(pid ${process.pid}\\)`,
        ),
      );
      const reportLines = res9a.stderr
        .split(/\r?\n/)
        .filter((line) => line.includes('the command failed while it shared'));
      assert.strictEqual(reportLines.length, 1);

      // 9b: exit 0 suppresses shared overlap report even when another shared holder is present.
      fs.writeFileSync(
        path.join(seat0, 'owner.json'),
        JSON.stringify({
          pid: process.pid,
          host: os.hostname(),
          platform: process.platform,
          startedAt: new Date().toISOString(),
          cwd: process.cwd(),
          command: 'other-shared-cmd',
          commandProcess: null,
          mode: 'shared',
          stage: 'running',
          turn: 42,
          seat: 0,
        }),
      );
      const harness9bc =
        'const { runResourceLock } = require(process.argv[1]);\n' +
        "runResourceLock({ argv: ['--resource', 'res', '--mode', 'shared', '--lock-path', process.argv[2], '--', " +
        'process.argv[3]], defaultResource: "res", displayName: "device", ' +
        'olderHolderLabel: "an older lock holder" });\n';
      const res9b = await runChild(['-e', harness9bc, __filename, seat0, 'node -e "process.exit(0)"']);
      assert.strictEqual(res9b.code, 0, res9b.stderr);
      assert.strictEqual(res9b.stderr.includes('the command failed'), false, res9b.stderr);
      assert.strictEqual(res9b.stderr.includes('shared the'), false, res9b.stderr);

      // 9c: non-zero exit with no other holder reports "with no other job".
      fs.rmSync(seat0, { recursive: true, force: true });
      const res9c = await runChild(['-e', harness9bc, __filename, seat0, 'node -e "process.exit(2)"']);
      assert.strictEqual(res9c.code, 2, res9c.stderr);
      assert.match(res9c.stderr, /\[res-lock\] the command failed; it shared the device with no other job/);

      // Config validation rejects duplicate seat paths (including case-only on win32) with exit 2.
      const dupCfgPath = path.join(root, 'dup-seats.json');
      const dupSeats =
        process.platform === 'win32' ? ['seats/DupSeat', 'seats/dupseat'] : ['seats/dupseat', './seats/dupseat'];
      fs.writeFileSync(dupCfgPath, JSON.stringify({ resources: { res: { lock: { seats: dupSeats } } } }));
      const dupHarness =
        'const { runResourceLock } = require(process.argv[1]);\n' +
        "runResourceLock({ argv: ['--resource', 'res', '--', 'node -e \"\"'], defaultResource: 'res' });\n";
      const resDup = await runChild(['-e', dupHarness, __filename], {
        env: { ...process.env, JOB_QUEUE_CONFIG: dupCfgPath },
      });
      assert.strictEqual(resDup.code, 2, resDup.stderr);
      assert.match(resDup.stderr, /must resolve to distinct directories/);
    }
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

if (require.main === module) {
  runResourceLock().catch((error) => {
    process.stderr.write(`${error.stack || error.message}\n`);
    process.exit(1);
  });
}

module.exports = {
  runResourceLock,
  resolveSeats,
  parseArgs,
  selfTest,
};
