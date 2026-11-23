// Immutable `WorkerProfile` Value Object & validation for remote worker provisioning.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";

export interface StaticNetworkConfig {
  readonly dhcp4: false;
  readonly address: string;
  readonly gateway4: string;
  readonly nameservers?: readonly string[];
}

export interface DhcpNetworkConfig {
  readonly dhcp4?: true;
}

export type WorkerNetworkConfig = StaticNetworkConfig | DhcpNetworkConfig;

export interface WorkerProfile {
  readonly workerName: string;
  readonly hostname: string;
  readonly connectHost: string;
  readonly network: WorkerNetworkConfig;
  readonly adminUser: string;
  readonly runnerUser: string;
  readonly workspace: string;
  readonly cpus: number;
  readonly memoryMb: number;
  readonly maxJobMinutes: number;
  readonly maxProcesses: number;
  readonly sshPublicKey: string;
  readonly sshPublicKeyPath: string;
  readonly blockAllEgress: boolean;
  readonly blockedLanCidrs: readonly string[];
  readonly packages: readonly string[];
  readonly setupCommands: readonly string[];
  readonly cacheDirs: readonly string[];
  readonly timezone: string;
}

const NamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const UserPattern = /^[a-z_][a-z0-9_-]{0,31}$/;
const HostPattern = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;
const CidrPattern = /^(?:\d{1,3}\.){3}\d{1,3}\/\d{1,2}$/;
const PubKeyPattern =
  /^(?:ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(?:256|384|521)|sk-ssh-ed25519@openssh\.com)\s+[A-Za-z0-9+/=]+(?:\s+[^\r\n]+)?$/;
const PackageNamePattern = /^[a-z0-9][a-z0-9.+_-]{0,63}$/;
const WorkspacePattern = /^(?:~\/[A-Za-z0-9_./-]+|\/[A-Za-z0-9_./-]+)$/;
const RelativeCacheDirPattern = /^[A-Za-z0-9_.][A-Za-z0-9_./-]*$/;

const DefaultBlockedLanCidrs = ["192.168.0.0/16", "10.0.0.0/8", "172.16.0.0/12", "169.254.0.0/16"] as const;

function expandHome(filePath: string): string {
  if (filePath === "~") {
    return os.homedir();
  }
  if (filePath.startsWith("~/") || filePath.startsWith("~\\")) {
    return path.join(os.homedir(), filePath.slice(2));
  }
  return filePath;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

/** Validates a raw JSON object and returns an immutable `WorkerProfile` Value Object. */
export function validateProfile(raw: unknown): WorkerProfile {
  if (!isPlainObject(raw)) {
    throw new Error("profile must be a JSON object");
  }
  if (typeof raw.workerName !== "string" || !NamePattern.test(raw.workerName)) {
    throw new Error("workerName must match /^[a-z][a-z0-9-]{0,31}$/");
  }
  if (typeof raw.hostname !== "string" || !/^[A-Za-z0-9][A-Za-z0-9-]{0,62}$/.test(raw.hostname)) {
    throw new Error("hostname must be a valid RFC 1123 hostname label");
  }
  if (typeof raw.connectHost !== "string" || !HostPattern.test(raw.connectHost)) {
    throw new Error("connectHost must be a valid hostname or IPv4 address");
  }
  if (typeof raw.adminUser !== "string" || !UserPattern.test(raw.adminUser)) {
    throw new Error("adminUser must be a valid Linux username");
  }
  if (typeof raw.runnerUser !== "string" || !UserPattern.test(raw.runnerUser)) {
    throw new Error("runnerUser must be a valid Linux username");
  }
  if (raw.runnerUser === "root" || raw.runnerUser === raw.adminUser) {
    throw new Error("runnerUser must be distinct from root and adminUser");
  }
  if (typeof raw.cpus !== "number" || !Number.isInteger(raw.cpus) || raw.cpus < 1) {
    throw new Error("cpus must be a positive integer");
  }
  if (typeof raw.memoryMb !== "number" || !Number.isFinite(raw.memoryMb) || raw.memoryMb < 512) {
    throw new Error("memoryMb must be >= 512");
  }
  const maxJobMinutes = raw.maxJobMinutes === undefined ? 30 : raw.maxJobMinutes;
  if (
    typeof maxJobMinutes !== "number" ||
    !Number.isInteger(maxJobMinutes) ||
    maxJobMinutes < 1 ||
    maxJobMinutes > 1440
  ) {
    throw new Error("maxJobMinutes must be an integer between 1 and 1440");
  }
  const maxProcesses = raw.maxProcesses === undefined ? 2048 : raw.maxProcesses;
  if (typeof maxProcesses !== "number" || !Number.isInteger(maxProcesses) || maxProcesses < 128) {
    throw new Error("maxProcesses must be an integer >= 128");
  }
  const blockedLanCidrs = Array.isArray(raw.blockedLanCidrs) ? raw.blockedLanCidrs : [...DefaultBlockedLanCidrs];
  for (const cidr of blockedLanCidrs) {
    if (typeof cidr !== "string" || !CidrPattern.test(cidr)) {
      throw new Error(`invalid blockedLanCidrs entry: ${String(cidr)}`);
    }
  }
  const rawNet = isPlainObject(raw.network) ? raw.network : { dhcp4: true };
  let network: WorkerNetworkConfig;
  if (rawNet.dhcp4 === false) {
    if (typeof rawNet.address !== "string" || !CidrPattern.test(rawNet.address)) {
      throw new Error("network.address (CIDR) is required when network.dhcp4 is false");
    }
    if (typeof rawNet.gateway4 !== "string" || !/^(?:\d{1,3}\.){3}\d{1,3}$/.test(rawNet.gateway4)) {
      throw new Error("network.gateway4 is required when network.dhcp4 is false");
    }
    const nameservers = Array.isArray(rawNet.nameservers)
      ? rawNet.nameservers.filter((n): n is string => typeof n === "string" && n !== "")
      : undefined;
    network = { dhcp4: false, address: rawNet.address, gateway4: rawNet.gateway4, nameservers };
  } else {
    network = { dhcp4: true };
  }
  const workspace =
    typeof raw.workspace === "string" && raw.workspace.trim() !== "" ? raw.workspace.trim() : "~/workspace";
  if (!WorkspacePattern.test(workspace) || workspace.includes("..")) {
    throw new Error('workspace must be a safe ~/... or /... POSIX path without ".."');
  }
  const packages: string[] = [];
  if (raw.packages !== undefined) {
    if (!Array.isArray(raw.packages)) {
      throw new Error("packages must be an array of apt package names");
    }
    for (const pkg of raw.packages) {
      if (typeof pkg !== "string" || !PackageNamePattern.test(pkg)) {
        throw new Error(`invalid packages entry: ${String(pkg)}`);
      }
      packages.push(pkg);
    }
  }
  const setupCommands: string[] = [];
  if (raw.setupCommands !== undefined) {
    if (!Array.isArray(raw.setupCommands)) {
      throw new Error("setupCommands must be an array of shell command strings");
    }
    for (const cmd of raw.setupCommands) {
      if (typeof cmd !== "string" || cmd.trim() === "" || /[\r\n\0]/.test(cmd)) {
        throw new Error("setupCommands entries must be non-empty single-line strings");
      }
      setupCommands.push(cmd);
    }
  } else if (typeof raw.dotnetChannel === "string" && /^\d+\.\d+$/.test(raw.dotnetChannel)) {
    setupCommands.push(
      `curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --channel ${raw.dotnetChannel} --install-dir /usr/local/share/dotnet && ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet && rm -f /tmp/dotnet-install.sh`,
    );
  }
  const cacheDirs: string[] = [];
  if (raw.cacheDirs !== undefined) {
    if (!Array.isArray(raw.cacheDirs)) {
      throw new Error("cacheDirs must be an array of relative directory paths");
    }
    for (const dir of raw.cacheDirs) {
      if (typeof dir !== "string" || !RelativeCacheDirPattern.test(dir) || dir.includes("..")) {
        throw new Error(`invalid cacheDirs entry: ${String(dir)}`);
      }
      cacheDirs.push(dir.replace(/\/+$/, ""));
    }
  }

  return {
    workerName: raw.workerName,
    hostname: raw.hostname,
    connectHost: raw.connectHost,
    network,
    adminUser: raw.adminUser,
    runnerUser: raw.runnerUser,
    workspace,
    cpus: raw.cpus,
    memoryMb: raw.memoryMb,
    maxJobMinutes,
    maxProcesses,
    sshPublicKey: typeof raw.sshPublicKey === "string" ? raw.sshPublicKey.trim() : "",
    sshPublicKeyPath: typeof raw.sshPublicKeyPath === "string" ? raw.sshPublicKeyPath : "~/.ssh/id_ed25519.pub",
    blockAllEgress: raw.blockAllEgress !== false,
    blockedLanCidrs: blockedLanCidrs as string[],
    packages,
    setupCommands,
    cacheDirs,
    timezone: typeof raw.timezone === "string" && /^[A-Za-z0-9_/-]+$/.test(raw.timezone) ? raw.timezone : "UTC",
  };
}

/** Resolves and validates the OpenSSH public key line from `profile`. */
export function resolvePublicKey(profile: WorkerProfile): string {
  const key =
    profile.sshPublicKey !== ""
      ? profile.sshPublicKey
      : fs.readFileSync(expandHome(profile.sshPublicKeyPath), "utf8").trim();
  if (!PubKeyPattern.test(key)) {
    throw new Error("SSH public key is missing or not a valid OpenSSH public key line");
  }
  return key;
}
