// The probe of a turn resource: a program from the configuration (data only, never plugin code) that reads how much of
// the resource is free, run without a shell and without waiting, so that the scheduler's event loop is never stopped. A
// second shared turn is granted only on a probe that passed (turnVerdict); everything that goes wrong is "not ok".

import { spawn, type ChildProcess } from "node:child_process";
import { performance } from "node:perf_hooks";
import { ProbeIntervalMs } from "./constants.ts";
import type { ProbeResult, ResourceSpec } from "./resources.ts";

/** What a probe is: the program with its arguments, the least number that passes, and how long it may run. */
export type ProbeSpec = Pick<NonNullable<ResourceSpec["probe"]>, "argv" | "min" | "timeoutMs">;

/** The first number of the output; a unit or text around it does not matter. */
const NumberPattern = /-?\d+(\.\d+)?/;

/** The most output a probe is read for; what a program prints after it is dropped. */
export const MaxProbeOutput = 64 * 1024;

/** Ends a probe program and, on Windows, what it started (a program run through cmd.exe leaves a child behind). */
function killTree(child: ChildProcess): void {
  if (process.platform === "win32" && child.pid !== undefined) {
    spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" }).on("error", () =>
      child.kill(),
    );
  } else {
    child.kill();
  }
}

/** One running probe, with a way to stop it. */
interface RunningProbe {
  result: Promise<ProbeResult>;
  stop(): void;
}

/** Starts the probe: ok only for exit 0 and a number of at least `min`; `generation` is the one it began in. */
function startProbe(probe: ProbeSpec, generation: number): RunningProbe {
  let stop = (): void => {};
  const result = new Promise<ProbeResult>((resolve) => {
    const finish = (ok: boolean, value: number | null, why?: string): void =>
      resolve({ ok, value, at: Date.now(), generation, ...(why !== undefined ? { why } : {}) });
    let child: ChildProcess;
    try {
      child = spawn(probe.argv[0] as string, probe.argv.slice(1), {
        windowsHide: true,
        stdio: ["ignore", "pipe", "ignore"],
      });
    } catch (error) {
      finish(false, null, `cannot start (${error instanceof Error ? error.message : String(error)})`);
      return;
    }
    let output = "";
    let settled = false;
    const settle = (ok: boolean, value: number | null, why?: string): void => {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        finish(ok, value, why);
      }
    };
    const timer = setTimeout(() => {
      killTree(child);
      settle(false, null, "timeout");
    }, probe.timeoutMs);
    stop = () => {
      killTree(child);
      settle(false, null, "stopped");
    };
    child.stdout?.setEncoding("utf8");
    child.stdout?.on("data", (chunk: string) => {
      if (output.length < MaxProbeOutput) {
        output += chunk.slice(0, MaxProbeOutput - output.length);
      }
    });
    child.once("error", (error) => settle(false, null, `cannot start (${error.message})`));
    child.once("close", (code) => {
      const text = NumberPattern.exec(output)?.[0];
      const parsed = text === undefined ? null : Number(text);
      const value = parsed !== null && Number.isFinite(parsed) ? parsed : null;
      if (code !== 0) {
        settle(false, value, `exit ${String(code)}`);
      } else if (value === null) {
        settle(false, null, parsed === null ? "no number" : "not a finite number");
      } else if (value < probe.min) {
        settle(false, value, `${value} < ${probe.min}`);
      } else {
        settle(true, value);
      }
    });
    child.unref();
  });
  return { result, stop };
}

/** Runs one probe to its end. */
export function runProbe(probe: ProbeSpec, generation: number): Promise<ProbeResult> {
  return startProbe(probe, generation).result;
}

/** The log line of a result: `probe gpu: 4121 >= 3072 ok` or `probe gpu: failed (timeout)`. */
export function describeProbe(name: string, probe: ProbeSpec, result: ProbeResult): string {
  return result.ok
    ? `probe ${name}: ${String(result.value)} >= ${probe.min} ok`
    : `probe ${name}: failed (${result.why ?? "unknown"})`;
}

/** The probes a scheduler runs: at most one per resource, and not twice within ProbeIntervalMs. */
export class ProbeRunner {
  private readonly running = new Map<string, RunningProbe>();
  private readonly lastStart = new Map<string, number>();
  private readonly clock: () => number;

  /** `clock`: milliseconds on a monotonic scale (the default), so that a change of the wall clock moves nothing. */
  constructor(clock: () => number = () => performance.now()) {
    this.clock = clock;
  }

  /**
   * Starts the probe of `name` unless one runs or the last began less than ProbeIntervalMs ago; `done` gets the
   * result, never after `stopAll`. True when it started.
   */
  start(name: string, probe: ProbeSpec, generation: number, done: (result: ProbeResult) => void): boolean {
    const now = this.clock();
    if (this.running.has(name) || now - (this.lastStart.get(name) ?? -Infinity) < ProbeIntervalMs) {
      return false;
    }
    this.lastStart.set(name, now);
    const running = startProbe(probe, generation);
    this.running.set(name, running);
    void running.result.then((result) => {
      if (this.running.get(name) === running) {
        this.running.delete(name);
        done(result);
      }
    });
    return true;
  }

  /** The scheduler stops: the programs that run are killed and no result is delivered. */
  stopAll(): void {
    const running = [...this.running.values()];
    this.running.clear();
    running.forEach((probe) => probe.stop());
  }
}
