// Typed configuration for the job queue (job-queue.config.json): schema validation, unknown-key forward
// compatibility warnings, defaults when missing, and mode-specific error handling.

import * as path from "node:path";
import { createRequire } from "node:module";
import { EntryScript, UsageExitCode } from "./constants.ts";
import type { ConfigFileModule, ConfigFileStatus } from "./lib/types.ts";

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
  legacyClients?: LegacyClientsConfig;
}

export interface LoadConfigOptions {
  mode: string;
  configPath?: string;
  fromDirectory?: string;
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
  const maxSlots = resource.lock !== undefined ? resource.lock.seats.length : 8;
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
      throw new Error(
        `config legacyClients.testPattern is not a valid regex: ${error instanceof Error ? error.message : String(error)}`,
        { cause: error },
      );
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
  if (raw.legacyClients !== undefined) {
    config.legacyClients = validateLegacyClients(raw.legacyClients, warn);
  }
  return config;
}

/**
 * Loads and validates job-queue.config.json.
 * - Missing config: returns defaults with status "missing".
 * - Invalid config: in `run` mode warns and exits with UsageExitCode (2); in all other modes warns and falls back to defaults.
 */
export function loadConfig(options: LoadConfigOptions): LoadedConfig {
  const fromDirectory = options.fromDirectory ?? path.dirname(EntryScript);
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
  if (loaded.status === "missing") {
    return {
      config: defaultConfig(),
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
      config: defaultConfig(),
      path: null,
      configRoot: fromDirectory,
      warnings,
      status: "invalid",
    };
  }
  try {
    const config = validateConfig(loaded.raw, warn);
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
    const detail = error instanceof Error ? error.message : String(error);
    if (options.mode === "run") {
      warn(`invalid config${location}: ${detail}`);
      return exit(UsageExitCode);
    }
    warn(`invalid config${location}: ${detail}; using defaults`);
    return {
      config: defaultConfig(),
      path: null,
      configRoot: fromDirectory,
      warnings,
      status: "invalid",
    };
  }
}
