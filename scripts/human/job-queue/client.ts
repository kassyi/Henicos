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
import { planBuildStep } from "./build-split.ts";
import { runBuildStep } from "./client-build-step.ts";
import { type Endpoint } from "./client-connect.ts";
import { QueuedRun } from "./client-queued-run.ts";
import { ProcessRunHost } from "./client-run-host.ts";
import { MachineProfile } from "./machine.ts";
import { type Placement } from "./protocol.ts";
import { CommandShell } from "./shell.ts";
import { ProcessTable, prefix } from "./system.ts";

export interface RunOptions extends Endpoint {
  command: string;
  /** Words after `--` that the caller's shell already split, when there were several; null for one quoted string. */
  commandWords: string[] | null;
  /** 0 for the default weight of the command. */
  weight: number;
  gpu: boolean;
  shell: ShellName | "";
  timeoutMinutes: number;
  /** --after: the job that must end first; 0 for none. */
  after: number;
  keepIfFailed: boolean;
  /** What the job is, in its queued line, when one run queues several jobs (run --gpu: "build, no GPU"). */
  role?: string;
}

/** Ends the process with a usage error. */
export function usageError(message: string): never {
  process.stderr.write(`${message}\nRun with --help for the usage.\n`);
  process.exit(UsageExitCode);
}

export async function run(options: RunOptions): Promise<void> {
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
    const command = cpus > 0 ? CommandShell.limitMsbuildNodes(options.command, cpus) : options.command;
    let child: ChildProcess | null = null;
    const stop = (code: number) => (): void => {
      ProcessTable.killChild(child);
      process.exit(code);
    };
    process.on("SIGINT", stop(130));
    process.on("SIGTERM", stop(143));
    const result = await CommandShell.resolve(options.shell).run(command, process.env, (c) => (child = c));
    process.exit(result.code);
  }
  if (options.gpu && !inGpuLock) {
    if (/fuzz-diff|fuzz-differential|long-checks/.test(options.command)) {
      // They take the GPU lock themselves for their GPU steps; holding it here would make them wait for ever
      usageError("fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.");
    }
    const cpuOnly = new MachineProfile().cpuOnlyTestProjects(options.command);
    if (cpuOnly.length > 0) {
      // A hint, not a refusal: the command may use the GPU in a way the project file does not show
      prefix(
        `--gpu, but ${cpuOnly.join(", ")} ${cpuOnly.length === 1 ? "runs" : "run"} on the CPU only ` +
          "(GSharpTargetDialect Cpu): " +
          "this job waits for the GPU and keeps the GPU jobs behind it waiting; queue it without --gpu",
      );
    }
    const host = new ProcessRunHost(options);
    const step = planBuildStep(options.command, CommandShell.resolve(options.shell).name);
    if (step !== null) {
      // Build first, on CPU slots: the GPU turn would otherwise be held, with the GPU idle, for the whole build
      prefix(`building outside the GPU turn first, as a job of its own: ${step.build}`);
      const code = await runBuildStep(options, placement, step.build, host);
      if (code !== 0) {
        prefix(`the build ended with ${code}; the tests do not ask for the GPU turn`);
        process.exit(code);
      }
      prefix(`the build succeeded; the tests wait for the GPU turn: ${step.tests}`);
      options.command = step.tests;
      options.role = "tests, GPU turn";
      options.after = 0; // the build job waited for it
    }
    // Wait for the turn at the GPU in the scheduler (in order, visible and cancellable), then take the lock: the GPU
    // lock alone gives it to whichever waiter polls first. An --after predecessor blocks the turn there too.
    await new QueuedRun(options, placement, false, true, host).run();
    return;
  }
  await new QueuedRun(options, placement, inGpuLock, false, new ProcessRunHost(options)).run();
}
