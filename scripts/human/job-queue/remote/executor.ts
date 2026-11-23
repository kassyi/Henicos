// Layer 2 Strategy + Fallback Decorator for job execution:
// Decouples `QueuedRun` from local vs remote execution details and offload eligibility checks.

import type { ChildProcess } from "node:child_process";
import type { RunHost } from "../client-run-host.ts";
import type { RunOptions } from "../client.ts";
import { CpusVariable, JobVariable, OffloadVariable, type ShellName } from "../constants.ts";
import type { PathPlace } from "../estimate-key.ts";
import { formatJobId } from "../job-id.ts";
import type { BaseClientContext, PluginHost } from "../plugin-host.ts";
import type { ShellResult } from "../shell.ts";
import { prefix } from "../system.ts";
import { translateForRemote } from "./command-translator.ts";
import type { ResolvedRemoteRules } from "./rules.ts";
import type { OffloadPreference, RemoteSpec } from "./spec.ts";

export interface ExecutionRequest {
  readonly jobId: number;
  readonly cpus: number;
  readonly weight: number;
  readonly waitedSec: number;
  readonly cwd: string;
  readonly worktree: string;
  readonly shell: ShellName;
  readonly places: Readonly<Record<string, PathPlace>>;
  readonly options: RunOptions;
  readonly plugins?: PluginHost;
  readonly context?: BaseClientContext;
  readonly remoteName?: string;
}

/** Strategy contract for executing a granted job. */
export interface JobExecutor {
  execute(request: ExecutionRequest, onChild: (child: ChildProcess) => void): Promise<ShellResult>;
}

/** Resolves whether a non-GPU job should offload to a remote worker, stay local, or reject `--remote`. */
export function resolveOffloadPreference(params: {
  options: RunOptions;
  inGpuLock: boolean;
  classifiedOffloadable: boolean;
  cwd: string;
  worktree: string;
  shell: ShellName;
  places: Readonly<Record<string, PathPlace>>;
  remoteRules: ResolvedRemoteRules;
}): { ok: true; offload: OffloadPreference } | { ok: false; error: string } {
  const { options, inGpuLock, classifiedOffloadable, cwd, worktree, shell, places, remoteRules } = params;
  const enabledRemotes = (options.remotes ?? []).filter((r) => r.mode !== "off");
  const disabled =
    inGpuLock || options.local === true || process.env[OffloadVariable] === "0" || enabledRemotes.length === 0;
  if (disabled) {
    if (options.remote === true) {
      return {
        ok: false,
        error: "--remote requested, but no enabled remote worker is configured or offload is disabled",
      };
    }
    return { ok: true, offload: "never" };
  }
  const canTranslate =
    (classifiedOffloadable || options.remote === true) &&
    translateForRemote(options.command, {
      cwd,
      worktree,
      shell,
      places,
      cpus: enabledRemotes[0]?.cpus ?? 1,
      keepBuildServersWarm: enabledRemotes[0]?.keepBuildServersWarm ?? true,
      env: process.env,
      remoteRules,
    }) !== null;
  if (options.remote === true && !canTranslate) {
    return {
      ok: false,
      error: "--remote requested, but this command cannot be safely translated for remote Linux execution",
    };
  }
  return { ok: true, offload: canTranslate ? (options.remote === true ? "force" : "auto") : "never" };
}

/** Local shell execution strategy. */
export class LocalJobExecutor implements JobExecutor {
  private readonly host: RunHost;

  constructor(host: RunHost) {
    this.host = host;
  }

  execute(request: ExecutionRequest, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    const { options, plugins, jobId, cpus, weight, waitedSec, cwd, worktree, shell } = request;
    const prepared = plugins?.prepareCommand(
      options.command,
      { jobId, cpus, nested: false },
      {
        cwd,
        worktree,
        configRoot: plugins.configRoot,
        shell,
        env: process.env,
        log: prefix,
      },
    );
    const command = prepared?.command ?? options.command;
    const note = prepared?.note ?? `${cpus} cpus`;
    prefix(`granted ${formatJobId(jobId)} after ${waitedSec}s (weight ${weight}, ${note})`);
    const env = {
      ...process.env,
      ...prepared?.env,
      [JobVariable]: String(jobId),
      [CpusVariable]: String(cpus),
    };
    return this.host.runCommand(command, env, onChild);
  }
}

/** Remote offload execution strategy with automatic local fallback when transport fails (unless `--remote`). */
export class RemoteWithFallbackExecutor implements JobExecutor {
  private readonly host: RunHost;
  private readonly local: LocalJobExecutor;
  private readonly onRemoteGrantChange: (remoteName: string | null) => void;

  constructor(host: RunHost, onRemoteGrantChange: (remoteName: string | null) => void) {
    this.host = host;
    this.local = new LocalJobExecutor(host);
    this.onRemoteGrantChange = onRemoteGrantChange;
  }

  async execute(request: ExecutionRequest, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    const { remoteName, options, cwd, worktree, shell, places, cpus, jobId, waitedSec, plugins, context } = request;
    if (remoteName !== undefined && this.host.runRemoteCommand !== undefined) {
      const remoteSpec: RemoteSpec | undefined = (options.remotes ?? []).find((r) => r.name === remoteName);
      const remoteRules = plugins !== undefined && context !== undefined ? plugins.remoteOffload(context) : undefined;
      const plan =
        remoteSpec === undefined
          ? null
          : translateForRemote(options.command, {
              cwd,
              worktree,
              shell,
              places,
              cpus,
              keepBuildServersWarm: remoteSpec.keepBuildServersWarm,
              env: process.env,
              remoteRules,
            });
      if (remoteSpec !== undefined && plan !== null) {
        this.onRemoteGrantChange(remoteName);
        const remoteNote = plan.note ?? `${cpus} cpus`;
        prefix(
          `granted ${formatJobId(jobId)} on remote ${remoteName} (${remoteSpec.host}) after ${waitedSec}s (${remoteNote})`,
        );
        const remoteResult = await this.host.runRemoteCommand(remoteSpec, plan, onChild, remoteRules);
        if (!remoteResult.transportFailed || options.remote === true) {
          return remoteResult;
        }
        prefix(`remote ${remoteName} transport failed before execution; falling back to local execution`);
        this.onRemoteGrantChange(null);
      }
    }
    return this.local.execute(request, onChild);
  }
}
