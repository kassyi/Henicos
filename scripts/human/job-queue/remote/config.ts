// Layer 3 Remote Configuration: schema validation for remote CPU workers, SSH option hardening checks, and
// machine-local `remotes.local.json` loading.

import * as fs from "node:fs";
import * as path from "node:path";
import type { ResourceProbeConfig } from "../config.ts";
import { isRemoteMode, type RemoteMode } from "../constants.ts";
import { errorMessage } from "../history.ts";
import { MaxRemoteSlots } from "./spec.ts";

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
const RemoteHostPattern = /^(?:[A-Za-z0-9_][A-Za-z0-9._-]*@)?[A-Za-z0-9][A-Za-z0-9._-]*$/;

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

/** Strict OpenSSH short-flag allowlist: standalone boolean flags and safe value-taking flags only. */
const AllowedSshStandaloneFlags = new Set(["4", "6", "q", "v", "n", "T", "C"]);
const AllowedSshValueFlags = new Set(["p", "l", "i", "o"]);

const SshPortPattern = /^\d{1,5}$/;
const SshUserPattern = /^[A-Za-z0-9_][A-Za-z0-9._-]{0,63}$/;
const SshNonNegIntPattern = /^\d+$/;
const SshPosIntPattern = /^[1-9]\d*$/;
const SshYesNoPattern = /^(?:yes|no)$/;

const PinnedSshOptionValues: Readonly<Record<string, string>> = {
  batchmode: "yes",
  permitlocalcommand: "no",
  forwardagent: "no",
  forwardx11: "no",
  forwardx11trusted: "no",
  stricthostkeychecking: "yes",
  clearallforwardings: "yes",
};

function isValidSshPort(raw: string): boolean {
  if (!SshPortPattern.test(raw)) {
    return false;
  }
  const port = Number(raw);
  return port >= 1 && port <= 65535;
}

function isSafeSshPath(raw: string): boolean {
  return raw.trim() !== "" && !/[\r\n\0]/.test(raw) && !raw.trimStart().startsWith("-");
}

const SafeSshOptionValidators: Readonly<Record<string, (rawVal: string) => boolean>> = {
  connecttimeout: (raw) => SshNonNegIntPattern.test(raw),
  serveraliveinterval: (raw) => SshNonNegIntPattern.test(raw),
  serveralivecountmax: (raw) => SshNonNegIntPattern.test(raw),
  connectionattempts: (raw) => SshPosIntPattern.test(raw),
  identitiesonly: (raw) => SshYesNoPattern.test(raw.toLowerCase()),
  userknownhostsfile: (raw) =>
    isSafeSshPath(raw) &&
    !raw
      .trim()
      .split(/\s+/)
      .some((p) => p.toLowerCase() === "none" || p === "/dev/null"),
  preferredauthentications: (raw) => /^[a-z0-9,_-]+$/.test(raw.toLowerCase()),
  compression: (raw) => SshYesNoPattern.test(raw.toLowerCase()),
  ipqos: (raw) => /^[a-z0-9_-]+(?:\s+[a-z0-9_-]+)?$/.test(raw.toLowerCase()),
  addressfamily: (raw) => /^(?:any|inet|inet6)$/.test(raw.toLowerCase()),
  tcpkeepalive: (raw) => SshYesNoPattern.test(raw.toLowerCase()),
  loglevel: (raw) => /^(?:quiet|fatal|error|info|verbose|debug[123]?)$/.test(raw.toLowerCase()),
  hashknownhosts: (raw) => SshYesNoPattern.test(raw.toLowerCase()),
  verifyhostkeydns: (raw) => /^(?:yes|no|ask)$/.test(raw.toLowerCase()),
  pubkeyacceptedalgorithms: (raw) => /^[A-Za-z0-9@.,^+-]+$/.test(raw),
  hostkeyalgorithms: (raw) => /^[A-Za-z0-9@.,^+-]+$/.test(raw),
  port: isValidSshPort,
  user: (raw) => SshUserPattern.test(raw),
  identityfile: isSafeSshPath,
};

function isAllowedSshOptionDirective(optSpec: string): boolean {
  if (/[\r\n\0]/.test(optSpec)) {
    return false;
  }
  const trimmed = optSpec.trimStart();
  const sepIdx = trimmed.search(/[=\s]/);
  if (sepIdx <= 0) {
    return false;
  }
  const key = trimmed.slice(0, sepIdx).toLowerCase();
  const rawVal = trimmed
    .slice(sepIdx)
    .replace(/^[\s]*(?:=[\s]*)?/, "")
    .trim();
  if (rawVal === "") {
    return false;
  }
  const required = PinnedSshOptionValues[key];
  if (required !== undefined) {
    return rawVal.toLowerCase() === required;
  }
  const validate = SafeSshOptionValidators[key];
  return validate !== undefined ? validate(rawVal) : false;
}

function consumeAllowedSshOption(args: readonly string[], index: number): { nextIndex: number; bad: string | null } {
  const token = args[index] as string;
  if (/[\r\n\0]/.test(token) || !token.startsWith("-") || token.length < 2 || token === "--") {
    return { nextIndex: index, bad: token };
  }
  for (let j = 1; j < token.length; j++) {
    const ch = token.charAt(j);
    if (AllowedSshStandaloneFlags.has(ch)) {
      continue;
    }
    if (!AllowedSshValueFlags.has(ch)) {
      return { nextIndex: index, bad: token };
    }
    let nextIndex = index;
    let val: string;
    if (j + 1 < token.length) {
      val = token.slice(j + 1);
    } else if (index + 1 < args.length) {
      nextIndex = index + 1;
      val = args[nextIndex] as string;
    } else {
      return { nextIndex: index, bad: token };
    }
    const ok =
      ch === "p"
        ? isValidSshPort(val)
        : ch === "l"
          ? SshUserPattern.test(val) && !/[\r\n\0]/.test(val)
          : ch === "i"
            ? isSafeSshPath(val)
            : isAllowedSshOptionDirective(val);
    return { nextIndex, bad: ok ? null : ch === "o" ? val : token };
  }
  return { nextIndex: index, bad: null };
}

/**
 * Strict allowlist validator for `sshOptions` tokens. Rejects `--`, non-flag positional tokens, disallowed or
 * bundled unsafe flags, and any `-o` directive outside the pinned/safe allowlist.
 */
export function findDangerousSshOption(args: readonly string[]): string | null {
  for (let i = 0; i < args.length; i++) {
    const res = consumeAllowedSshOption(args, i);
    if (res.bad !== null) {
      return res.bad;
    }
    i = res.nextIndex;
  }
  return null;
}

/**
 * Strict allowlist validator for `probe.argv.slice(1)` (`[...sshOptions, "--", host, ...remoteCmd]` or
 * `[...sshOptions, host, ...remoteCmd]` without leading `-` in `remoteCmd`).
 */
export function findDangerousProbeArgv(args: readonly string[]): string | null {
  for (let i = 0; i < args.length; i++) {
    const token = args[i] as string;
    if (/[\r\n\0]/.test(token)) {
      return token;
    }
    if (token === "--") {
      const host = args[i + 1];
      if (host === undefined || !RemoteHostPattern.test(host)) {
        return host ?? "--";
      }
      if (i + 2 >= args.length) {
        return "--";
      }
      for (let k = i + 2; k < args.length; k++) {
        if (/[\r\n\0]/.test(args[k] as string)) {
          return args[k] as string;
        }
      }
      return null;
    }
    if (!token.startsWith("-")) {
      if (!RemoteHostPattern.test(token) || i + 1 >= args.length) {
        return token;
      }
      for (let k = i + 1; k < args.length; k++) {
        const cmdArg = args[k] as string;
        if (/[\r\n\0]/.test(cmdArg) || cmdArg.startsWith("-")) {
          return cmdArg;
        }
      }
      return null;
    }
    const res = consumeAllowedSshOption(args, i);
    if (res.bad !== null) {
      return res.bad;
    }
    i = res.nextIndex;
  }
  return args[args.length - 1] ?? "<missing host>";
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
  if (!isNonEmptyString(raw.workspace) || raw.workspace.trimStart().startsWith("-") || /[\r\n\0]/.test(raw.workspace)) {
    throw new Error(
      `config remotes.${name}.workspace must be a non-empty POSIX path without leading "-" or control characters`,
    );
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
      throw new Error(
        `config remotes.${name}.slots must be 1 (per-slot workspaces are not yet implemented; max ${MaxRemoteSlots})`,
      );
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
    const dangerous = findDangerousProbeArgv(probe.argv.slice(1));
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
