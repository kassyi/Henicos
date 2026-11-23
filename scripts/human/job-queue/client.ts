// The run mode of the job queue: checks the options and runs the command through a QueuedRun
// (client-queued-run.ts), which waits for a slot or the GPU turn, runs the command and stays registered across a
// scheduler restart, with its effects behind RunHost (client-run-host.ts). The other modes are in client-commands.ts,
// the connection to the scheduler in client-connect.ts.

import { type ChildProcess } from "node:child_process";
import {
  CpusVariable,
  GpuLockHeldVariable,
  InGpuLockVariable,
  JobVariable,
  UsageExitCode,
  type ShellName,
} from "./constants.ts";
import { runBuildStep } from "./client-build-step.ts";
import { type Endpoint } from "./client-connect.ts";
import { QueuedRun } from "./client-queued-run.ts";
import { ProcessRunHost, type RunHost } from "./client-run-host.ts";
import type { BaseClientContext, PluginHost } from "./plugin-host.ts";
import { type Placement } from "./protocol.ts";
import type { RemoteSpec } from "./remote/spec.ts";
import { CommandShell } from "./shell.ts";
import { ProcessTable, prefix, resolveWorktree } from "./system.ts";

export interface RunOptions extends Endpoint {
  command: string;
  /** Words after `--` that the caller's shell already split, when there were several; null for one quoted string. */
  commandWords: string[] | null;
  /** 0 for the default weight of the command. */
  weight: number;
  gpu: boolean;
  /** --gpu-exclusive: the GPU run asks for the GPU alone, whatever the plugins classify (gpu is set too). */
  gpuExclusive?: boolean;
  shell: ShellName | "";
  timeoutMinutes: number;
  /** --after: the job that must end first; 0 for none. */
  after: number;
  keepIfFailed: boolean;
  /** What the job is, in its queued line, when one run queues several jobs (run --gpu: "build, no GPU"). */
  role?: string;
  /** Memory budget (MB) from config.pools.defaultTestMemoryMb for classified tests without an explicit memoryMb. */
  defaultTestMemoryMb?: number;
  /** --local: disable transparent remote CPU offload for this run. */
  local?: boolean;
  /** --remote: require execution on a configured remote CPU worker (refuse if ineligible). */
  remote?: boolean;
  /** Configured remote CPU workers from job-queue.config.json. */
  remotes?: RemoteSpec[];
}

/** Ends the process with a usage error. */
export function usageError(message: string): never {
  process.stderr.write(`${message}\nRun with --help for the usage.\n`);
  process.exit(UsageExitCode);
}

function clientContext(shell: ShellName | "", configRoot: string, worktree: string): BaseClientContext {
  return {
    cwd: process.cwd(),
    worktree,
    configRoot,
    shell: CommandShell.resolve(shell).name,
    env: process.env,
    log: prefix,
  };
}

export async function run(options: RunOptions, plugins: PluginHost): Promise<void> {
  if (options.commandWords !== null) {
    const shell = CommandShell.resolve(options.shell);
    if (shell.isPowerShell && options.commandWords.includes("--%")) {
      // PowerShell cannot pass a literal --% to a program, quoted or not: it is its stop-parsing token
      usageError("PowerShell cannot pass --% on; give the command as one string or choose another --shell.");
    }
    options.command = shell.join(options.commandWords);
    options.shell = shell.name; // the --gpu path re-runs this script with the command as one string
  }
  const placement: Placement = {
    weight: options.weight > 0 ? options.weight : 1,
    pin: null,
    before: null,
  };
  // This client runs inside the GPU lock: the --gpu wrapper started it, or gpu-lock.js runs the command that called it
  const inGpuLock = process.env[InGpuLockVariable] === "1" || Boolean(process.env[GpuLockHeldVariable]);

  if (options.after > 0 && (process.env[JobVariable] || inGpuLock)) {
    // Waiting for another job while holding slots or the GPU lock can deadlock: that job may wait for them. (The --gpu
    // wrapper resolves --after before it takes the lock and does not pass it to the client it starts inside.)
    usageError("--after cannot be used inside a queued job or the GPU lock; queue the chain from outside.");
  }
  if (process.env[JobVariable] && (inGpuLock || !options.gpu)) {
    // Inside a queued job: its slot covers this command, with the same node limit.
    const cpus = Number(process.env[CpusVariable]) || 0;
    let command = options.command;
    let env: NodeJS.ProcessEnv = process.env;
    if (cpus > 0) {
      const prepared = plugins.prepareCommand(
        command,
        { jobId: Number(process.env[JobVariable]) || 0, cpus, nested: true },
        clientContext(options.shell, plugins.configRoot, resolveWorktree(process.cwd())),
      );
      command = prepared.command;
      env = { ...process.env, ...prepared.env };
    }
    let child: ChildProcess | null = null;
    const stop = (code: number) => (): void => {
      ProcessTable.killChild(child);
      process.exit(code);
    };
    process.on("SIGINT", stop(130));
    process.on("SIGTERM", stop(143));
    const result = await CommandShell.resolve(options.shell).run(command, env, (c) => (child = c));
    process.exit(result.code);
  }
  const hasPlugins = plugins.plugins.length > 0;
  const context = hasPlugins
    ? clientContext(options.shell, plugins.configRoot, resolveWorktree(process.cwd()))
    : undefined;
  const gpu = { name: "gpu", forcedExclusive: options.gpuExclusive === true };
  if (context !== undefined) {
    const resource = options.gpu && !inGpuLock ? gpu : null;
    const checked = plugins.check(options.command, resource, context);
    for (const hint of checked.hints ?? []) {
      prefix(hint);
    }
    if (checked.refuse !== undefined) {
      usageError(checked.refuse);
    }
  }
  if (options.gpu && !inGpuLock) {
    await runGpuTurn(options, placement, new ProcessRunHost(options), plugins, context);
    return;
  }
  await new QueuedRun(options, placement, inGpuLock, false, new ProcessRunHost(options), plugins).run();
}

/**
 * The --gpu run: the builds of its command first, as a queued job without the GPU (when a plugin splits them off), and
 * only after that job has ended with 0 the turn at the GPU, which classifies its share (and may ask the plugins'
 * checkShare, which reads what the build wrote) from the command after the split.
 */
export async function runGpuTurn(
  options: RunOptions,
  placement: Placement,
  host: RunHost,
  plugins: PluginHost,
  context: BaseClientContext | undefined,
): Promise<void> {
  const gpu = { name: "gpu", forcedExclusive: options.gpuExclusive === true };
  const step = context !== undefined ? plugins.splitBeforeTurn(options.command, gpu, context) : undefined;
  if (step !== undefined) {
    // Build first, on CPU slots: the GPU turn would otherwise be held, with the GPU idle, for the whole build
    prefix(`building outside the GPU turn first, as a job of its own: ${step.before}`);
    const code = await runBuildStep(options, placement, step.before, host, step.beforeRole, plugins);
    if (code !== 0) {
      prefix(`the build ended with ${code}; the tests do not ask for the GPU turn`);
      process.exit(code);
    }
    prefix(`the build succeeded; the tests wait for the GPU turn: ${step.after}`);
    options.command = step.after;
    options.role = step.afterRole;
    options.after = 0; // the build job waited for it
  }
  // Wait for the turn at the GPU in the scheduler (in order, visible and cancellable), then take the lock: the GPU
  // lock alone gives it to whichever waiter polls first. An --after predecessor blocks the turn there too. The turn
  // classifies its share from options.command, which is the command after the split.
  await new QueuedRun(options, placement, false, true, host, plugins).run();
}
