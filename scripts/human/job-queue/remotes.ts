// The remote CPU workers of the scheduler, with no I/O: configuration to RemoteSpec, live usage counted from the
// running jobs, health/probe freshness, and eligibility checks.

import type { QueueConfig } from "./config.ts";
import { ProbeTimeoutMs, RemoteProbeFreshMs, RemoteProbeRetryMs, type RemoteMode } from "./constants.ts";
import type { RemoteStatus } from "./protocol.ts";
import { isResourceName, type ProbeResult } from "./resources.ts";

/** How a job interacts with remote offload: auto (eligible), force (--remote), never (--local / ineligible). */
export type OffloadPreference = "auto" | "force" | "never";

/** Default hard wall-clock timeout (minutes) for a remote command. */
export const DefaultMaxJobMinutes = 30;

/** Mandatory OpenSSH hardening and dead-connection keepalive flags enforced on every remote SSH invocation. */
export const MandatorySshOptions: readonly string[] = [
  "-o",
  "BatchMode=yes",
  "-o",
  "StrictHostKeyChecking=yes",
  "-o",
  "ForwardAgent=no",
  "-o",
  "ForwardX11=no",
  "-o",
  "ClearAllForwardings=yes",
  "-o",
  "PermitLocalCommand=no",
  "-o",
  "ServerAliveInterval=5",
  "-o",
  "ServerAliveCountMax=3",
];

/** Builds hardened argv for `ssh`, always prepending mandatory security options and `--` before `remote.host`. */
export function buildSshArgs(remote: Pick<RemoteSpec, "host" | "sshOptions">, remoteCommand: string): string[] {
  return [...MandatorySshOptions, ...remote.sshOptions, "--", remote.host, remoteCommand];
}

/** One remote worker, as the scheduler reads it from configuration at start. */
export interface RemoteSpec {
  name: string;
  host: string;
  workspace: string;
  /** Maximum concurrent jobs on this remote worker. */
  slots: number;
  /** CPU cores granted to a job on this remote worker (-m:<cpus>). */
  cpus: number;
  /** Maximum memory budget (MB) of a job eligible for this remote worker. */
  memoryMb: number;
  mode: RemoteMode;
  /** Hard wall-clock timeout (minutes) enforced on the remote worker via `timeout`. */
  maxJobMinutes: number;
  sshOptions: string[];
  keepBuildServersWarm: boolean;
  /** Health/free-memory probe run by ProbeRunner; null for no probe. */
  probe: { argv: string[]; min: number; timeoutMs: number; settleMs: number } | null;
}

export interface RemoteState {
  spec: RemoteSpec;
  probe: ProbeResult | null;
  /** The forecast replay: every probe counts as healthy. */
  assumeProbeOk?: boolean;
}

/** What remote scheduling decisions read from a job record. */
export interface RemoteJob {
  id: number;
  gpu: boolean;
  gpuTurn?: boolean;
  offload?: OffloadPreference;
  remote?: string;
  memoryMb?: number;
}

export interface RemoteUsage<J extends RemoteJob> {
  jobs: J[];
  memoryUsed: number;
}

export function isRemoteName(value: unknown): value is string {
  return isResourceName(value);
}

/** Converts config.remotes into RemoteSpec[]. */
export function remoteSpecsFromConfig(config: QueueConfig): RemoteSpec[] {
  return Object.entries(config.remotes ?? {}).map(
    ([name, remote]): RemoteSpec => ({
      name,
      host: remote.host,
      workspace: remote.workspace,
      slots: remote.slots ?? 1,
      cpus: remote.cpus,
      memoryMb: remote.memoryMb,
      mode: remote.mode ?? "prefer-remote",
      maxJobMinutes: remote.maxJobMinutes ?? DefaultMaxJobMinutes,
      sshOptions: remote.sshOptions ? [...remote.sshOptions] : ["-o", "ConnectTimeout=5"],
      keepBuildServersWarm: remote.keepBuildServersWarm ?? true,
      probe:
        remote.probe === undefined
          ? null
          : {
              argv: [remote.probe.argv[0] as string, ...MandatorySshOptions, ...remote.probe.argv.slice(1)],
              min: remote.probe.min,
              timeoutMs: remote.probe.timeoutMs ?? ProbeTimeoutMs,
              settleMs: remote.probe.settleMs ?? 0,
            },
    }),
  );
}

/** Counts running jobs currently assigned to remote `name` and their total memory budget. */
export function remoteUsageOf<J extends RemoteJob>(running: readonly J[], name: string): RemoteUsage<J> {
  const jobs = running.filter((job) => job.remote === name);
  const memoryUsed = jobs.reduce((sum, job) => sum + Math.max(0, job.memoryMb ?? 0), 0);
  return { jobs, memoryUsed };
}

/** True when the remote worker is enabled and its health probe is currently valid (or no probe is configured). */
export function remoteHealthy(state: RemoteState, now: number): boolean {
  if (state.spec.mode === "off") {
    return false;
  }
  if (state.spec.probe === null || state.assumeProbeOk === true) {
    return true;
  }
  const probe = state.probe;
  return probe !== null && probe.ok && now - probe.at <= RemoteProbeFreshMs;
}

/**
 * True when an eligible job should briefly wait for the remote worker's probe before spilling over to local:
 * on the initial probe (probe === null) or when a previously healthy probe just expired. Never waits after a
 * failed probe, so an offline server falls back to local with zero delay.
 */
export function remoteAwaitingProbe(state: RemoteState, now: number): boolean {
  if (state.spec.mode === "off" || state.spec.probe === null || state.assumeProbeOk === true) {
    return false;
  }
  const probe = state.probe;
  return probe === null || (probe.ok && now - probe.at > RemoteProbeFreshMs);
}

/** True when the server should launch a probe for this remote worker now. */
export function remoteNeedsProbe(state: RemoteState, now: number): boolean {
  if (state.spec.mode === "off" || state.spec.probe === null || state.assumeProbeOk === true) {
    return false;
  }
  const probe = state.probe;
  if (probe === null) {
    return true;
  }
  return probe.ok ? now - probe.at > RemoteProbeFreshMs : now - probe.at >= RemoteProbeRetryMs;
}

/** True when `job` can be placed on `state` given its offload preference and memory budget (ignoring current occupancy). */
export function remoteAcceptsJob(job: RemoteJob, spec: RemoteSpec): boolean {
  if (spec.mode === "off" || job.gpu || job.gpuTurn === true) {
    return false;
  }
  if (job.offload !== "auto" && job.offload !== "force") {
    return false;
  }
  const memory = Math.max(0, job.memoryMb ?? 0);
  return memory <= spec.memoryMb;
}

/** True when `job` can be granted on `state` right now. */
export function fitsRemote<J extends RemoteJob>(
  job: RemoteJob,
  state: RemoteState,
  running: readonly J[],
  now: number,
): boolean {
  if (!remoteAcceptsJob(job, state.spec) || !remoteHealthy(state, now)) {
    return false;
  }
  const usage = remoteUsageOf(running, state.spec.name);
  if (usage.jobs.length >= state.spec.slots) {
    return false;
  }
  const memory = Math.max(0, job.memoryMb ?? 0);
  return memory === 0 || usage.memoryUsed + memory <= state.spec.memoryMb;
}

/** What `status` reports for one remote worker. */
export function describeRemote<J extends RemoteJob>(state: RemoteState, running: readonly J[]): RemoteStatus {
  const usage = remoteUsageOf(running, state.spec.name);
  const probe = state.probe;
  return {
    name: state.spec.name,
    host: state.spec.host,
    slots: state.spec.slots,
    cpus: state.spec.cpus,
    memoryMb: state.spec.memoryMb,
    mode: state.spec.mode,
    running: usage.jobs.map((job) => job.id),
    ...(probe !== null
      ? {
          probe: {
            ok: probe.ok,
            value: probe.value,
            at: probe.at,
            ...(probe.why !== undefined ? { why: probe.why } : {}),
          },
        }
      : {}),
  };
}
