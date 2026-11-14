// The build step of run --gpu: before the job asks for its GPU turn, the builds that its command would run first
// (planBuildStep, build-split.ts) run as a job of their own on CPU slots, so that the GPU is held only for the tests.
// A build that fails or is cancelled ends the run with its exit code; the GPU turn is then never asked for.

import type * as net from "node:net";
import type { ChildProcess } from "node:child_process";
import { CpusVariable, JobVariable } from "./constants.ts";
import { QueuedRun } from "./client-queued-run.ts";
import type { RunHost } from "./client-run-host.ts";
import type { RunOptions } from "./client.ts";
import type { Placement } from "./protocol.ts";
import { CommandShell, type ShellResult } from "./shell.ts";
import { ProcessTable } from "./system.ts";

/**
 * The RunHost of the build step: the run's own host, except that the end of the step resolves `ended` instead of
 * ending the process, and that the step's signal handlers stop once it has ended (the test run registers its own).
 */
class StepRunHost implements RunHost {
  private readonly inner: RunHost;
  private active = true;
  private resolveEnd: (code: number) => void = () => {};
  readonly ended = new Promise<number>((resolve) => (this.resolveEnd = resolve));

  constructor(inner: RunHost) {
    this.inner = inner;
  }

  connect(): Promise<net.Socket> {
    return this.inner.connect();
  }

  reportEnd(
    id: number,
    completed: boolean,
    exitCode: number,
    cancelled: boolean,
    reporter: { pid: number; queuedAt: number },
  ): Promise<void> {
    return this.inner.reportEnd(id, completed, exitCode, cancelled, reporter);
  }

  runCommand(command: string, env: NodeJS.ProcessEnv, onChild: (child: ChildProcess) => void): Promise<ShellResult> {
    return this.inner.runCommand(command, env, onChild);
  }

  runUnderGpuLock(): Promise<ShellResult> {
    throw new Error("the build step never takes the GPU");
  }

  stop(child: ChildProcess | null, underGpuLock: boolean): void {
    this.inner.stop(child, underGpuLock);
  }

  exit(code: number): void {
    this.active = false;
    this.resolveEnd(code);
  }

  onSignal(signal: NodeJS.Signals, handler: () => void): void {
    this.inner.onSignal(signal, () => {
      if (this.active) {
        handler();
      }
    });
  }
}

/**
 * Runs the builds of a run --gpu as a queued job without the GPU (its own id, its own run time in history.json, CPU
 * slots with their MSBuild node limit) and resolves to its exit code. The --after of the run goes to this job: the
 * tests follow it only when it succeeded. Inside a queued job the builds run at once on that job's slots.
 */
export async function runBuildStep(
  options: RunOptions,
  placement: Placement,
  build: string,
  host: RunHost,
): Promise<number> {
  if (process.env[JobVariable]) {
    const cpus = Number(process.env[CpusVariable]) || 0;
    const command = cpus > 0 ? CommandShell.limitMsbuildNodes(build, cpus) : build;
    let child: ChildProcess | null = null;
    const stop = (): void => ProcessTable.killChild(child);
    process.once("SIGINT", stop);
    process.once("SIGTERM", stop);
    const result = await host.runCommand(command, process.env, (c) => (child = c));
    process.off("SIGINT", stop);
    process.off("SIGTERM", stop);
    return result.code;
  }
  const step = new StepRunHost(host);
  const stepOptions: RunOptions = { ...options, command: build, commandWords: null, gpu: false, role: "build, no GPU" };
  void new QueuedRun(stepOptions, { ...placement }, false, false, step).run();
  return step.ended;
}
