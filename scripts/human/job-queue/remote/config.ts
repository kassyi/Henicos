// Layer 3 Remote Configuration: schema validation for remote CPU workers, SSH option hardening checks, and
// machine-local `remotes.local.json` loading.

import * as fs from "node:fs";
import * as path from "node:path";
import type { ResourceProbeConfig } from "../config.ts";
import { isRemoteMode, type RemoteMode } from "../constants.ts";
import { errorMessage } from "../history.ts";

export interface RemoteWorkerConfig {
  /** SSH host (e.g. "jobrunner@ubuntu-server.lan"). */
  host: string;
  /** Remote workspace directory (e.g. "~/henicos"). */
  workspace: string;
  /** Maximum concurrent jobs on this remote worker. Default 1. */
  slots?: number;
  /** CPU cores granted to a job on this remote worker. */
  cpus: number;
  /** Maximum memory budget (MB) of a job eligible for this remote worker. */
  memoryMb: number;
  /** Routing mode: prefer-remote (default), remote-only, spillover, or off. */
  mode?: RemoteMode;
  /** Hard wall-clock timeout (minutes) enforced on the remote worker via `timeout`. Default 30. */
  maxJobMinutes?: number;
  /** Extra ssh CLI arguments (e.g. ["-o", "ConnectTimeout=5"]). Security flags are always enforced. */
  sshOptions?: string[];
  /** Strip Windows build-server workarounds on remote Linux runs to keep build servers warm. Default true. */
  keepBuildServersWarm?: boolean;
  /** Optional health/free-memory probe run by the scheduler via ProbeRunner (must invoke ssh). */
  probe?: ResourceProbeConfig;
}

export const LocalRemotesFileName = "remotes.local.json";

const RemoteNamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const RemoteHostPattern = /^(?:[A-Za-z0-9._-]+@)?[A-Za-z0-9][A-Za-z0-9._-]*$/;
const MaxRemoteSlots = 8;

const RemoteKeys = new Set([
  "host",
  "workspace",
  "slots",
  "cpus",
  "memoryMb",
  "mode",
  "maxJobMinutes",
  "sshOptions",
  "keepBuildServersWarm",
  "probe",
]);

/** Dangerous OpenSSH short flags that enable forwarding, arbitrary config files, or local command execution. */
const DangerousSshShortFlags = new Set(["-A", "-X", "-Y", "-L", "-R", "-D", "-W", "-F", "-J", "-S"]);

const ForbiddenSshOptions = new Set([
  "proxycommand",
  "localcommand",
  "remotecommand",
  "knownhostscommand",
  "proxyjump",
  "userknownhostsfile",
  "globalknownhostsfile",
  "localforward",
  "remoteforward",
  "dynamicforward",
]);

const PinnedSshOptionValues: Readonly<Record<string, string>> = {
  permitlocalcommand: "no",
  forwardagent: "no",
  forwardx11: "no",
  forwardx11trusted: "no",
  stricthostkeychecking: "yes",
  clearallforwardings: "yes",
};

/**
 * Dangerous OpenSSH -o option rules. Returns the offending option string if any entry in `args` attempts to weaken
 * host-key verification, forward agent/X11/ports, or execute local commands (ProxyCommand / LocalCommand).
 */
export function findDangerousSshOption(args: readonly string[]): string | null {
  for (let i = 0; i < args.length; i++) {
    const token = args[i] as string;
    if (token === "--") {
      break;
    }
    if (DangerousSshShortFlags.has(token) || /^-[AXYLDRWFJS]/.test(token)) {
      return token;
    }
    let optSpec = "";
    if (token === "-o" && i + 1 < args.length) {
      optSpec = args[++i] as string;
    } else if (token.startsWith("-o") && token.length > 2) {
      optSpec = token.slice(2);
    } else {
      continue;
    }
    const eq = optSpec.search(/[=\s]/);
    const key = (eq >= 0 ? optSpec.slice(0, eq) : optSpec).trim().toLowerCase();
    const val = (eq >= 0 ? optSpec.slice(eq + 1) : "").trim().toLowerCase();
    if (ForbiddenSshOptions.has(key)) {
      return optSpec;
    }
    const required = PinnedSshOptionValues[key];
    if (required !== undefined && val !== required) {
      return optSpec;
    }
  }
  return null;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim() !== "";
}

export type ProbeValidator = (scope: string, raw: unknown, warn: (message: string) => void) => ResourceProbeConfig;

/** Validates a single remote worker entry from config or `remotes.local.json`. */
export function validateRemote(
  name: string,
  raw: unknown,
  validateProbe: ProbeValidator,
  warn: (message: string) => void,
): RemoteWorkerConfig {
  if (!RemoteNamePattern.test(name)) {
    throw new Error(`invalid config remote name "${name}"`);
  }
  if (!isPlainObject(raw)) {
    throw new Error(`config remotes.${name} must be an object`);
  }
  for (const key of Object.keys(raw)) {
    if (!RemoteKeys.has(key)) {
      warn(`unknown config key remotes.${name}.${key} ignored`);
    }
  }
  if (!isNonEmptyString(raw.host) || !RemoteHostPattern.test(raw.host)) {
    throw new Error(`config remotes.${name}.host must be a valid [user@]hostname without leading "-"`);
  }
  if (!isNonEmptyString(raw.workspace)) {
    throw new Error(`config remotes.${name}.workspace must be a non-empty string`);
  }
  if (typeof raw.cpus !== "number" || !Number.isInteger(raw.cpus) || raw.cpus < 1) {
    throw new Error(`config remotes.${name}.cpus must be a positive integer`);
  }
  if (typeof raw.memoryMb !== "number" || !Number.isFinite(raw.memoryMb) || raw.memoryMb < 0) {
    throw new Error(`config remotes.${name}.memoryMb must be a non-negative finite number`);
  }
  const remote: RemoteWorkerConfig = {
    host: raw.host,
    workspace: raw.workspace,
    cpus: raw.cpus,
    memoryMb: raw.memoryMb,
  };
  if (raw.slots !== undefined) {
    if (typeof raw.slots !== "number" || !Number.isInteger(raw.slots) || raw.slots < 1 || raw.slots > MaxRemoteSlots) {
      throw new Error(`config remotes.${name}.slots must be an integer between 1 and ${MaxRemoteSlots}`);
    }
    remote.slots = raw.slots;
  }
  if (raw.mode !== undefined) {
    if (!isRemoteMode(raw.mode)) {
      throw new Error(`config remotes.${name}.mode must be one of prefer-remote, remote-only, spillover, off`);
    }
    remote.mode = raw.mode;
  }
  if (raw.maxJobMinutes !== undefined) {
    if (
      typeof raw.maxJobMinutes !== "number" ||
      !Number.isInteger(raw.maxJobMinutes) ||
      raw.maxJobMinutes < 1 ||
      raw.maxJobMinutes > 1440
    ) {
      throw new Error(`config remotes.${name}.maxJobMinutes must be an integer between 1 and 1440`);
    }
    remote.maxJobMinutes = raw.maxJobMinutes;
  }
  if (raw.sshOptions !== undefined) {
    if (!Array.isArray(raw.sshOptions) || !raw.sshOptions.every(isNonEmptyString)) {
      throw new Error(`config remotes.${name}.sshOptions must be an array of non-empty strings`);
    }
    const dangerous = findDangerousSshOption(raw.sshOptions);
    if (dangerous !== null) {
      throw new Error(`config remotes.${name}.sshOptions forbids unsafe SSH option "${dangerous}"`);
    }
    remote.sshOptions = [...raw.sshOptions];
  }
  if (raw.keepBuildServersWarm !== undefined) {
    if (typeof raw.keepBuildServersWarm !== "boolean") {
      throw new Error(`config remotes.${name}.keepBuildServersWarm must be a boolean`);
    }
    remote.keepBuildServersWarm = raw.keepBuildServersWarm;
  }
  if (raw.probe !== undefined) {
    const probe = validateProbe(`remotes.${name}.probe`, raw.probe, warn);
    const prog = path.basename(probe.argv[0] as string).toLowerCase();
    if (prog !== "ssh" && prog !== "ssh.exe") {
      throw new Error(`config remotes.${name}.probe.argv[0] must be "ssh"`);
    }
    const dangerous = findDangerousSshOption(probe.argv.slice(1));
    if (dangerous !== null) {
      throw new Error(`config remotes.${name}.probe.argv forbids unsafe SSH option "${dangerous}"`);
    }
    remote.probe = probe;
  }
  return remote;
}

/**
 * Reads machine-local remote worker definitions from `<stateDir>/remotes.local.json` (untracked, shared across all
 * git worktrees of the main checkout). Returns undefined when absent or invalid (fail-closed to local execution).
 */
export function loadLocalRemotesConfig(
  stateDir: string,
  validateProbe: ProbeValidator,
  warn: (message: string) => void = () => {},
): Record<string, RemoteWorkerConfig> | undefined {
  const filePath = path.join(stateDir, LocalRemotesFileName);
  let text: string;
  try {
    text = fs.readFileSync(filePath, "utf8");
  } catch (error) {
    if (error && typeof error === "object" && "code" in error && (error as { code?: string }).code === "ENOENT") {
      return undefined;
    }
    warn(`could not read ${filePath}: ${errorMessage(error)}`);
    return undefined;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    warn(`invalid ${filePath}: ${errorMessage(error)}; ignoring remote workers`);
    return undefined;
  }
  if (!isPlainObject(parsed)) {
    warn(`invalid ${filePath}: root must be a JSON object; ignoring remote workers`);
    return undefined;
  }
  const rawRemotes = parsed.remotes !== undefined ? parsed.remotes : parsed;
  if (!isPlainObject(rawRemotes)) {
    warn(`invalid ${filePath}: remotes must be a JSON object; ignoring remote workers`);
    return undefined;
  }
  try {
    const remotes: Record<string, RemoteWorkerConfig> = {};
    for (const [name, value] of Object.entries(rawRemotes)) {
      remotes[name] = validateRemote(name, value, validateProbe, warn);
    }
    return Object.keys(remotes).length > 0 ? remotes : undefined;
  } catch (error) {
    warn(`invalid ${filePath}: ${errorMessage(error)}; ignoring remote workers`);
    return undefined;
  }
}
