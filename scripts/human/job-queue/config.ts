// Typed configuration for the job queue (job-queue.config.json): schema validation, unknown-key forward
// compatibility warnings, defaults when missing, and mode-specific error handling.

import * as path from "node:path";
import { createRequire } from "node:module";
import { EntryScript, UsageExitCode } from "./constants.ts";
import { errorMessage } from "./history.ts";
import type { ConfigFileModule, ConfigFileStatus } from "./lib/types.ts";
import {
  findDangerousSshOption,
  loadLocalRemotesConfig as loadLocalRemotesWithProbe,
  LocalRemotesFileName,
  validateRemote,
  type RemoteWorkerConfig,
} from "./remote/config.ts";

export { findDangerousSshOption, LocalRemotesFileName, type RemoteWorkerConfig };

const require = createRequire(import.meta.url);
const { loadConfigFile } = require("./lib/config-file.js") as ConfigFileModule;

export interface PluginEntry {
  name: string;
  options?: Record<string, unknown>;
}

export interface PoolsConfig {
  /** Cores per CPU slot. Default 3. */
  coresPerSlot?: number;
  /** Minimum CPU slots. Default 2. */
  minimumSlots?: number;
  /** Share of physical memory usable by concurrent test budgets. Default 0.5. */
  memoryShare?: number;
  /** Memory budget (MB) for test runs without an explicit memoryMb answer. Default 0. */
  defaultTestMemoryMb?: number;
}

export interface ResourceLockConfig {
  /** Seat directories relative to sharedRoot. seats[0] is the legacy lock directory. */
  seats: string[];
}

export interface ResourceProbeConfig {
  /** Argv launched without a shell. */
  argv: string[];
  /** Minimum numeric value parsed from stdout to grant an additional shared turn. */
  min: number;
  /** Probe timeout in ms. Default 3000. */
  timeoutMs?: number;
  /** Minimum elapsed ms for running shared turns before probing. Default 0. */
  settleMs?: number;
}

export interface ResourceConfig {
  /** Maximum concurrent shared turns. Default 1. */
  slots?: number;
  lock?: ResourceLockConfig;
  probe?: ResourceProbeConfig;
  /** CJS watch modules loaded by the lock wrapper, relative to configRoot. */
  holderWatch?: string[];
}

export interface LegacyClientsConfig {
  /** Regex matching test commands for clients that do not send memoryMb. */
  testPattern?: string;
  /** Memory budget (MB) for commands matching testPattern. */
  testMemoryMb?: number;
}

export interface QueueConfig {
  /** Config format version. Currently 1. */
  version: 1;
  /** Environment variable prefix. Default "JOB_QUEUE". */
  envPrefix?: string;
  /** Legacy env aliases mapped from canonical suffix to old variable names. */
  envAliases?: Record<string, string[]>;
  /** Entry script path relative to configRoot. */
  entry: string;
  /** State directory relative to sharedRoot. Default "tmp/job-queue". */
  stateDir?: string;
  /** How sharedRoot is resolved. Currently only "git-main-checkout". */
  sharedRoot?: "git-main-checkout";
  /** Ordered plugin list (first entry has highest priority). */
  plugins: PluginEntry[];
  pools?: PoolsConfig;
  resources?: Record<string, ResourceConfig>;
  remotes?: Record<string, RemoteWorkerConfig>;
  legacyClients?: LegacyClientsConfig;
}

export interface LoadConfigOptions {
  mode: string;
  configPath?: string;
  fromDirectory?: string;
  /** Shared state directory (main checkout tmp/job-queue) holding untracked remotes.local.json. */
  stateDir?: string;
  warn?: (message: string) => void;
  exit?: (code: number) => never;
}

export interface LoadedConfig {
  config: QueueConfig;
  path: string | null;
  configRoot: string;
  warnings: string[];
  status: ConfigFileStatus;
}

const ResourceNamePattern = /^[a-z][a-z0-9-]{0,31}$/;

const TopLevelKeys = new Set([
  "version",
  "envPrefix",
  "envAliases",
  "entry",
  "stateDir",
  "sharedRoot",
  "plugins",
  "pools",
  "resources",
  "remotes",
  "legacyClients",
]);
const PluginKeys = new Set(["name", "options"]);
const PoolsKeys = new Set(["coresPerSlot", "minimumSlots", "memoryShare", "defaultTestMemoryMb"]);
const ResourceKeys = new Set(["slots", "lock", "probe", "holderWatch"]);
const LockKeys = new Set(["seats"]);
const ProbeKeys = new Set(["argv", "min", "timeoutMs", "settleMs"]);
const LegacyClientsKeys = new Set(["testPattern", "testMemoryMb"]);

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim() !== "";
}

function warnUnknownKeys(
  obj: Record<string, unknown>,
  known: ReadonlySet<string>,
  scope: string,
  warn: (message: string) => void,
): void {
  for (const key of Object.keys(obj)) {
    if (!known.has(key)) {
      warn(`unknown config key ${scope ? `${scope}.${key}` : key} ignored`);
    }
  }
}

export function defaultConfig(): QueueConfig {
  return {
    version: 1,
    envPrefix: "JOB_QUEUE",
    entry: "scripts/human/job-queue.js",
    stateDir: "tmp/job-queue",
    sharedRoot: "git-main-checkout",
    plugins: [],
    pools: {
      coresPerSlot: 3,
      minimumSlots: 2,
      memoryShare: 0.5,
      defaultTestMemoryMb: 0,
    },
    resources: {
      gpu: {
        slots: 1,
      },
    },
  };
}

function validatePools(raw: unknown, warn: (message: string) => void): PoolsConfig {
  if (!isPlainObject(raw)) {
    throw new Error("config pools must be an object");
  }
  warnUnknownKeys(raw, PoolsKeys, "pools", warn);
  const pools: PoolsConfig = {};
  if (raw.coresPerSlot !== undefined) {
    if (typeof raw.coresPerSlot !== "number" || !Number.isInteger(raw.coresPerSlot) || raw.coresPerSlot < 1) {
      throw new Error("config pools.coresPerSlot must be a positive integer");
    }
    pools.coresPerSlot = raw.coresPerSlot;
  }
  if (raw.minimumSlots !== undefined) {
    if (typeof raw.minimumSlots !== "number" || !Number.isInteger(raw.minimumSlots) || raw.minimumSlots < 1) {
      throw new Error("config pools.minimumSlots must be a positive integer");
    }
    pools.minimumSlots = raw.minimumSlots;
  }
  if (raw.memoryShare !== undefined) {
    if (
      typeof raw.memoryShare !== "number" ||
      !Number.isFinite(raw.memoryShare) ||
      raw.memoryShare <= 0 ||
      raw.memoryShare > 1
    ) {
      throw new Error("config pools.memoryShare must be in (0, 1]");
    }
    pools.memoryShare = raw.memoryShare;
  }
  if (raw.defaultTestMemoryMb !== undefined) {
    if (
      typeof raw.defaultTestMemoryMb !== "number" ||
      !Number.isFinite(raw.defaultTestMemoryMb) ||
      raw.defaultTestMemoryMb < 0
    ) {
      throw new Error("config pools.defaultTestMemoryMb must be a non-negative finite number");
    }
    pools.defaultTestMemoryMb = raw.defaultTestMemoryMb;
  }
  return pools;
}

/** The most slots a resource without a lock may have. */
export const MaxResourceSlots = 8;

/**
 * Why `slots` cannot be the shared slots of the `gpu` resource (`serve --gpu-slots`), or null: from 1 up to the seats
 * of its lock, or MaxResourceSlots without one.
 */
export function gpuSlotsProblem(slots: number, config: QueueConfig): string | null {
  const seats = config.resources?.gpu?.lock?.seats.length;
  const max = seats ?? MaxResourceSlots;
  if (Number.isInteger(slots) && slots >= 1 && slots <= max) {
    return null;
  }
  const why = seats === undefined ? "" : ` (its lock has ${seats} seat${seats === 1 ? "" : "s"})`;
  return `--gpu-slots needs a whole number from 1 to ${max}${why}.`;
}

function validateResource(name: string, raw: unknown, warn: (message: string) => void): ResourceConfig {
  if (!ResourceNamePattern.test(name)) {
    throw new Error(`invalid config resource name "${name}"`);
  }
  if (!isPlainObject(raw)) {
    throw new Error(`config resources.${name} must be an object`);
  }
  warnUnknownKeys(raw, ResourceKeys, `resources.${name}`, warn);
  const resource: ResourceConfig = {};
  if (raw.lock !== undefined) {
    if (!isPlainObject(raw.lock)) {
      throw new Error(`config resources.${name}.lock must be an object`);
    }
    warnUnknownKeys(raw.lock, LockKeys, `resources.${name}.lock`, warn);
    if (!Array.isArray(raw.lock.seats) || !raw.lock.seats.every(isNonEmptyString)) {
      throw new Error(`config resources.${name}.lock.seats must be an array of non-empty strings`);
    }
    if (raw.lock.seats.length !== 1 && raw.lock.seats.length !== 2) {
      throw new Error(`config resources.${name}.lock.seats must have 1 or 2 entries`);
    }
    resource.lock = { seats: [...raw.lock.seats] };
  }
  const maxSlots = resource.lock !== undefined ? resource.lock.seats.length : MaxResourceSlots;
  if (raw.slots !== undefined) {
    if (typeof raw.slots !== "number" || !Number.isInteger(raw.slots) || raw.slots < 1 || raw.slots > maxSlots) {
      throw new Error(`config resources.${name}.slots must be an integer between 1 and ${maxSlots}`);
    }
    resource.slots = raw.slots;
  }
  if (raw.probe !== undefined) {
    if (!isPlainObject(raw.probe)) {
      throw new Error(`config resources.${name}.probe must be an object`);
    }
    warnUnknownKeys(raw.probe, ProbeKeys, `resources.${name}.probe`, warn);
    if (!Array.isArray(raw.probe.argv) || raw.probe.argv.length === 0 || !raw.probe.argv.every(isNonEmptyString)) {
      throw new Error(`config resources.${name}.probe.argv must be a non-empty array of strings`);
    }
    if (typeof raw.probe.min !== "number" || !Number.isFinite(raw.probe.min)) {
      throw new Error(`config resources.${name}.probe.min must be a finite number`);
    }
    const probe: ResourceProbeConfig = { argv: [...raw.probe.argv], min: raw.probe.min };
    if (raw.probe.timeoutMs !== undefined) {
      if (
        typeof raw.probe.timeoutMs !== "number" ||
        !Number.isFinite(raw.probe.timeoutMs) ||
        raw.probe.timeoutMs <= 0
      ) {
        throw new Error(`config resources.${name}.probe.timeoutMs must be a positive finite number`);
      }
      probe.timeoutMs = raw.probe.timeoutMs;
    }
    if (raw.probe.settleMs !== undefined) {
      if (typeof raw.probe.settleMs !== "number" || !Number.isFinite(raw.probe.settleMs) || raw.probe.settleMs < 0) {
        throw new Error(`config resources.${name}.probe.settleMs must be a non-negative finite number`);
      }
      probe.settleMs = raw.probe.settleMs;
    }
    resource.probe = probe;
  }
  if (raw.holderWatch !== undefined) {
    if (!Array.isArray(raw.holderWatch) || !raw.holderWatch.every(isNonEmptyString)) {
      throw new Error(`config resources.${name}.holderWatch must be an array of non-empty strings`);
    }
    resource.holderWatch = [...raw.holderWatch];
  }
  return resource;
}

function validateProbe(scope: string, raw: unknown, warn: (message: string) => void): ResourceProbeConfig {
  if (!isPlainObject(raw)) {
    throw new Error(`config ${scope} must be an object`);
  }
  warnUnknownKeys(raw, ProbeKeys, scope, warn);
  if (!Array.isArray(raw.argv) || raw.argv.length === 0 || !raw.argv.every(isNonEmptyString)) {
    throw new Error(`config ${scope}.argv must be a non-empty array of strings`);
  }
  if (typeof raw.min !== "number" || !Number.isFinite(raw.min)) {
    throw new Error(`config ${scope}.min must be a finite number`);
  }
  const probe: ResourceProbeConfig = { argv: [...raw.argv], min: raw.min };
  if (raw.timeoutMs !== undefined) {
    if (typeof raw.timeoutMs !== "number" || !Number.isFinite(raw.timeoutMs) || raw.timeoutMs <= 0) {
      throw new Error(`config ${scope}.timeoutMs must be a positive finite number`);
    }
    probe.timeoutMs = raw.timeoutMs;
  }
  if (raw.settleMs !== undefined) {
    if (typeof raw.settleMs !== "number" || !Number.isFinite(raw.settleMs) || raw.settleMs < 0) {
      throw new Error(`config ${scope}.settleMs must be a non-negative finite number`);
    }
    probe.settleMs = raw.settleMs;
  }
  return probe;
}

function validateLegacyClients(raw: unknown, warn: (message: string) => void): LegacyClientsConfig {
  if (!isPlainObject(raw)) {
    throw new Error("config legacyClients must be an object");
  }
  warnUnknownKeys(raw, LegacyClientsKeys, "legacyClients", warn);
  const legacy: LegacyClientsConfig = {};
  if (raw.testPattern !== undefined) {
    if (typeof raw.testPattern !== "string") {
      throw new Error("config legacyClients.testPattern must be a string");
    }
    try {
      new RegExp(raw.testPattern);
    } catch (error) {
      throw new Error(`config legacyClients.testPattern is not a valid regex: ${errorMessage(error)}`, {
        cause: error,
      });
    }
    legacy.testPattern = raw.testPattern;
  }
  if (raw.testMemoryMb !== undefined) {
    if (typeof raw.testMemoryMb !== "number" || !Number.isFinite(raw.testMemoryMb) || raw.testMemoryMb < 0) {
      throw new Error("config legacyClients.testMemoryMb must be a non-negative finite number");
    }
    legacy.testMemoryMb = raw.testMemoryMb;
  }
  return legacy;
}

/**
 * Validates a raw parsed config object.
 * Unknown keys emit warnings via `warn` and are ignored for forward compatibility.
 * Throws on invalid types, missing required fields, or invalid values.
 */
export function validateConfig(raw: unknown, warn: (message: string) => void = () => {}): QueueConfig {
  if (!isPlainObject(raw)) {
    throw new Error("config must be a JSON object");
  }
  warnUnknownKeys(raw, TopLevelKeys, "", warn);
  if (raw.version !== 1) {
    throw new Error("config version must be 1");
  }
  if (!isNonEmptyString(raw.entry)) {
    throw new Error("config entry must be a non-empty string");
  }
  if (!Array.isArray(raw.plugins)) {
    throw new Error("config plugins must be an array");
  }
  const plugins: PluginEntry[] = raw.plugins.map((item, index) => {
    if (!isPlainObject(item)) {
      throw new Error(`config plugins[${index}] must be an object`);
    }
    warnUnknownKeys(item, PluginKeys, `plugins[${index}]`, warn);
    if (!isNonEmptyString(item.name)) {
      throw new Error(`config plugins[${index}].name must be a non-empty string`);
    }
    if (item.options !== undefined && !isPlainObject(item.options)) {
      throw new Error(`config plugins[${index}].options must be an object`);
    }
    return item.options !== undefined ? { name: item.name, options: { ...item.options } } : { name: item.name };
  });

  const config: QueueConfig = {
    version: 1,
    entry: raw.entry,
    plugins,
  };
  if (raw.envPrefix !== undefined) {
    if (!isNonEmptyString(raw.envPrefix)) {
      throw new Error("config envPrefix must be a non-empty string");
    }
    config.envPrefix = raw.envPrefix;
  }
  if (raw.envAliases !== undefined) {
    if (!isPlainObject(raw.envAliases)) {
      throw new Error("config envAliases must be an object");
    }
    const aliases: Record<string, string[]> = {};
    for (const [k, v] of Object.entries(raw.envAliases)) {
      if (!Array.isArray(v) || !v.every(isNonEmptyString)) {
        throw new Error(`config envAliases.${k} must be an array of non-empty strings`);
      }
      aliases[k] = [...v];
    }
    config.envAliases = aliases;
  }
  if (raw.stateDir !== undefined) {
    if (!isNonEmptyString(raw.stateDir)) {
      throw new Error("config stateDir must be a non-empty string");
    }
    config.stateDir = raw.stateDir;
  }
  if (raw.sharedRoot !== undefined) {
    if (raw.sharedRoot !== "git-main-checkout") {
      throw new Error('config sharedRoot must be "git-main-checkout"');
    }
    config.sharedRoot = raw.sharedRoot;
  }
  if (raw.pools !== undefined) {
    config.pools = validatePools(raw.pools, warn);
  }
  if (raw.resources !== undefined) {
    if (!isPlainObject(raw.resources)) {
      throw new Error("config resources must be an object");
    }
    const resources: Record<string, ResourceConfig> = {};
    for (const [name, value] of Object.entries(raw.resources)) {
      resources[name] = validateResource(name, value, warn);
    }
    config.resources = resources;
  }
  if (raw.remotes !== undefined) {
    if (!isPlainObject(raw.remotes)) {
      throw new Error("config remotes must be an object");
    }
    const remotes: Record<string, RemoteWorkerConfig> = {};
    for (const [name, value] of Object.entries(raw.remotes)) {
      remotes[name] = validateRemote(name, value, validateProbe, warn);
    }
    config.remotes = remotes;
  }
  if (raw.legacyClients !== undefined) {
    config.legacyClients = validateLegacyClients(raw.legacyClients, warn);
  }
  return config;
}

/**
 * Reads machine-local remote worker definitions from `<stateDir>/remotes.local.json` (untracked, shared across all
 * git worktrees of the main checkout). Returns undefined when absent or invalid (fail-closed to local execution).
 */
export function loadLocalRemotesConfig(
  stateDir: string,
  warn: (message: string) => void = () => {},
): Record<string, RemoteWorkerConfig> | undefined {
  return loadLocalRemotesWithProbe(stateDir, validateProbe, warn);
}

/**
 * Loads and validates job-queue.config.json.
 * - Missing config: returns defaults with status "missing".
 * - Invalid config: in `run` mode warns and exits with UsageExitCode (2); in all other modes warns and falls back to
 *   defaults.
 * - When `stateDir` is provided and `JOB_QUEUE_CONFIG` is not set, `remotes` come strictly from
 *   `<stateDir>/remotes.local.json` (untrusted `remotes` in job-queue.config.json are ignored, even with `--config`).
 */
export function loadConfig(options: LoadConfigOptions): LoadedConfig {
  const fromDirectory = options.fromDirectory ?? path.dirname(EntryScript);
  const envExplicit = process.env.JOB_QUEUE_CONFIG !== undefined && process.env.JOB_QUEUE_CONFIG !== "";
  const warnings: string[] = [];
  const warn = (message: string): void => {
    warnings.push(message);
    if (options.warn) {
      options.warn(message);
    } else {
      process.stderr.write(`[job-queue] ${message}\n`);
    }
  };
  const exit = options.exit ?? ((code: number): never => process.exit(code));
  const loaded = loadConfigFile(fromDirectory, options.configPath);
  const attachLocalRemotes = (config: QueueConfig): QueueConfig => {
    if (envExplicit || options.stateDir === undefined || options.stateDir === "") {
      return config;
    }
    if (config.remotes !== undefined && Object.keys(config.remotes).length > 0) {
      const localPath = path.join(options.stateDir, LocalRemotesFileName);
      warn(`ignoring remotes defined in repo job-queue.config.json; configure machine-local workers in ${localPath}`);
      delete config.remotes;
    }
    const localRemotes = loadLocalRemotesConfig(options.stateDir, warn);
    if (localRemotes !== undefined) {
      config.remotes = localRemotes;
    }
    return config;
  };
  if (loaded.status === "missing") {
    return {
      config: attachLocalRemotes(defaultConfig()),
      path: null,
      configRoot: fromDirectory,
      warnings,
      status: "missing",
    };
  }
  if (loaded.status === "invalid") {
    const location = loaded.path ? ` (${loaded.path})` : "";
    const detail = loaded.error ?? "invalid config";
    if (options.mode === "run") {
      warn(`invalid config${location}: ${detail}`);
      return exit(UsageExitCode);
    }
    warn(`invalid config${location}: ${detail}; using defaults`);
    return {
      config: attachLocalRemotes(defaultConfig()),
      path: null,
      configRoot: fromDirectory,
      warnings,
      status: "invalid",
    };
  }
  try {
    const config = attachLocalRemotes(validateConfig(loaded.raw, warn));
    const resolvedPath = loaded.path as string;
    return {
      config,
      path: resolvedPath,
      configRoot: path.dirname(resolvedPath),
      warnings,
      status: "ok",
    };
  } catch (error) {
    const location = loaded.path ? ` (${loaded.path})` : "";
    const detail = errorMessage(error);
    if (options.mode === "run") {
      warn(`invalid config${location}: ${detail}`);
      return exit(UsageExitCode);
    }
    warn(`invalid config${location}: ${detail}; using defaults`);
    return {
      config: attachLocalRemotes(defaultConfig()),
      path: null,
      configRoot: fromDirectory,
      warnings,
      status: "invalid",
    };
  }
}
