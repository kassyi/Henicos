// Checks of the probe run (server-probe.ts) and of the `serve --gpu-slots` range, with `node` standing in for the
// program that reads the GPU memory: no GPU tool is called.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawnSync } from "node:child_process";
import { EntryScript } from "./constants.ts";
import type { QueueConfig } from "./config.ts";
import { delay, firstTestPort, removeDirectory } from "./self-test-support.ts";

type AssertModule = typeof Assert;

const node = process.execPath;
const probeOf = (argv: string[], min = 3072, timeoutMs = 5000) => ({ argv, min, timeoutMs });

/** Whether a process of this id still exists. */
function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

/** The exit code, the number and the exit of a program decide: ok only for exit 0 with a number of at least `min`. */
export async function checkProbeRun(assert: AssertModule): Promise<void> {
  const { runProbe } = await import("./server-probe.ts");
  const run = (argv: string[], min = 3072, timeoutMs = 5000) => runProbe(probeOf(argv, min, timeoutMs), 7);
  const high = await run([node, "-e", "console.log(4000)"]);
  assert.deepStrictEqual([high.ok, high.value, high.generation], [true, 4000, 7]);
  assert.ok(Math.abs(high.at - Date.now()) < 10000, "the time is when it ended");
  const low = await run([node, "-e", "console.log(100)"]);
  assert.deepStrictEqual([low.ok, low.value, low.why], [false, 100, "100 < 3072"]);
  assert.strictEqual((await run([node, "-e", "console.log(3072)"])).ok, true, "the minimum itself passes");
  assert.strictEqual((await run([node, "-e", "console.log('  4121.5 MiB\\n')"])).value, 4121.5, "the first number");
  const failed = await run([node, "-e", "console.log(9999); process.exit(1)"]);
  assert.deepStrictEqual([failed.ok, failed.why], [false, "exit 1"], "a number does not count after a failure");
  const none = await run([node, "-e", "console.log('no memory')"]);
  assert.deepStrictEqual([none.ok, none.value, none.why], [false, null, "no number"]);
  const missing = await run(["job-queue-self-test-no-such-program", "x"]);
  assert.strictEqual(missing.ok, false, "a program that cannot start is a failed probe, not an exception");
  assert.match(String(missing.why), /^cannot start/);
}

/** A probe that outlasts its timeout fails with "timeout", and its process is gone. */
export async function checkProbeTimeoutKillsTheProcess(assert: AssertModule): Promise<void> {
  const { runProbe } = await import("./server-probe.ts");
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-probe-"));
  try {
    const marker = path.join(directory, "pid.txt");
    const code = "require('fs').writeFileSync(process.argv[1], String(process.pid)); setTimeout(() => {}, 30000)";
    const started = Date.now();
    const result = await runProbe(probeOf([node, "-e", code, marker], 3072, 3000), 1);
    assert.deepStrictEqual([result.ok, result.why], [false, "timeout"]);
    assert.ok(Date.now() - started < 8000, "it does not wait for the program to end");
    const pid = Number(fs.readFileSync(marker, "utf8"));
    assert.ok(Number.isInteger(pid) && pid > 0, "the program started");
    for (let i = 0; i < 40 && alive(pid); i++) {
      await delay(50);
    }
    assert.strictEqual(alive(pid), false, "the program was killed");
  } finally {
    removeDirectory(directory);
  }
}

/** A number that is not finite (a long run of digits reads as Infinity) fails; output beyond the cap is not read. */
export async function checkProbeOutputLimits(assert: AssertModule): Promise<void> {
  const { runProbe, MaxProbeOutput } = await import("./server-probe.ts");
  const infinite = await runProbe(probeOf([node, "-e", "console.log('9'.repeat(400))"]), 1);
  assert.deepStrictEqual([infinite.ok, infinite.value, infinite.why], [false, null, "not a finite number"]);
  const late = `process.stdout.write('x'.repeat(${MaxProbeOutput + 1000}) + ' 99999\\n')`;
  const capped = await runProbe(probeOf([node, "-e", late]), 1);
  assert.deepStrictEqual([capped.ok, capped.why], [false, "no number"], "a number after the cap is never read");
  const early = await runProbe(
    probeOf([node, "-e", `console.log(4000); console.log('x'.repeat(${MaxProbeOutput * 2}))`]),
    1,
  );
  assert.deepStrictEqual([early.ok, early.value], [true, 4000], "what is read before the cap counts");
}

/** One probe per resource, five seconds apart on the runner's own (injectable, monotonic) clock. */
export async function checkProbeRunnerInterval(assert: AssertModule): Promise<void> {
  const { ProbeRunner } = await import("./server-probe.ts");
  const { ProbeIntervalMs } = await import("./constants.ts");
  let clock = 1000;
  const runner = new ProbeRunner(() => clock);
  const spec = probeOf([node, "-e", "console.log(4000)"]);
  const delivered: boolean[] = [];
  const begin = (): boolean => runner.start("gpu", spec, 1, (result) => delivered.push(result.ok));
  const finished = async (count: number): Promise<void> => {
    for (let i = 0; i < 200 && delivered.length < count; i++) {
      await delay(25);
    }
  };
  assert.strictEqual(begin(), true, "the first probe starts");
  assert.strictEqual(begin(), false, "not while it runs");
  await finished(1);
  assert.deepStrictEqual(delivered, [true]);
  assert.strictEqual(begin(), false, "it has ended, but began less than the interval ago");
  clock += ProbeIntervalMs - 1;
  assert.strictEqual(begin(), false, "one millisecond short");
  clock += 1;
  assert.strictEqual(begin(), true, "the interval has passed");
  await finished(2);
  assert.strictEqual(
    runner.start("other", spec, 1, () => {}),
    true,
    "another resource has its own interval",
  );
  runner.stopAll();
}

/** `stopAll` kills the probe that runs, and its result is not delivered. */
export async function checkProbeRunnerStopAll(assert: AssertModule): Promise<void> {
  const { ProbeRunner } = await import("./server-probe.ts");
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-probe-"));
  try {
    const marker = path.join(directory, "pid.txt");
    const code = "require('fs').writeFileSync(process.argv[1], String(process.pid)); setTimeout(() => {}, 30000)";
    let delivered = 0;
    const runner = new ProbeRunner();
    assert.ok(runner.start("gpu", probeOf([node, "-e", code, marker], 3072, 20000), 1, () => delivered++));
    for (let i = 0; i < 200 && !fs.existsSync(marker); i++) {
      await delay(25);
    }
    await delay(50);
    const pid = Number(fs.readFileSync(marker, "utf8"));
    assert.ok(Number.isInteger(pid) && pid > 0 && alive(pid), "the probe runs");
    runner.stopAll();
    for (let i = 0; i < 80 && alive(pid); i++) {
      await delay(50);
    }
    assert.strictEqual(alive(pid), false, "stopAll killed the program");
    await delay(200);
    assert.strictEqual(delivered, 0, "and no result is delivered");
    runner.stopAll();
  } finally {
    removeDirectory(directory);
  }
}

/** Windows: a probe run through cmd.exe starts a grandchild; the timeout ends the whole tree. Skipped elsewhere. */
export async function checkProbeTimeoutKillsTheTree(assert: AssertModule): Promise<void> {
  if (process.platform !== "win32") {
    return;
  }
  const { runProbe } = await import("./server-probe.ts");
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-probe-"));
  let pid = 0;
  try {
    const marker = path.join(directory, "pid.txt");
    // No character that cmd.exe reads as syntax (& | < > ^), so that the line reaches node as it is
    const code = "require('fs').writeFileSync(process.argv[1],String(process.pid));setInterval(function(){},1000)";
    const result = await runProbe(probeOf(["cmd.exe", "/d", "/c", "node", "-e", code, marker], 3072, 4000), 1);
    assert.deepStrictEqual([result.ok, result.why], [false, "timeout"]);
    assert.ok(fs.existsSync(marker), "the grandchild started");
    pid = Number(fs.readFileSync(marker, "utf8"));
    for (let i = 0; i < 200 && alive(pid); i++) {
      await delay(50);
    }
    assert.strictEqual(alive(pid), false, "the grandchild was killed with its parent");
  } finally {
    if (pid > 0 && alive(pid)) {
      process.kill(pid);
    }
    removeDirectory(directory);
  }
}

/** The configuration of the checks: a GPU lock of `seats` seats. */
const configWith = (seats: number): QueueConfig => ({
  version: 1,
  entry: "job-queue.js",
  plugins: [],
  resources: { gpu: { lock: { seats: ["a", "b"].slice(0, seats) } } },
});

/** `--gpu-slots` takes 1 up to the seats of the lock (8 without a lock); anything else is a usage error. */
export async function checkGpuSlotsRange(assert: AssertModule): Promise<void> {
  const { gpuSlotsProblem } = await import("./config.ts");
  const two = configWith(2);
  assert.strictEqual(gpuSlotsProblem(1, two), null);
  assert.strictEqual(gpuSlotsProblem(2, two), null, "two seats: two slots");
  assert.match(String(gpuSlotsProblem(3, two)), /1 to 2/);
  assert.match(String(gpuSlotsProblem(0, two)), /1 to 2/);
  assert.match(String(gpuSlotsProblem(2, configWith(1))), /1 to 1/, "one seat holds one shared turn");
  assert.strictEqual(gpuSlotsProblem(8, { version: 1, entry: "x", plugins: [] }), null, "no lock: up to 8");
  assert.match(String(gpuSlotsProblem(9, { version: 1, entry: "x", plugins: [] })), /1 to 8/);
}

/** The command line refuses a bad `--gpu-slots` with exit 2 before it listens (a private port, in case it did). */
export async function checkGpuSlotsUsageErrors(assert: AssertModule): Promise<void> {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-self-test-slots-"));
  try {
    const configure = (seats: number): string => {
      const file = path.join(directory, `seats-${seats}.json`);
      fs.writeFileSync(file, JSON.stringify(configWith(seats)));
      return file;
    };
    const exitOf = (slots: string, seats: number, timeout = 10000): number | null =>
      spawnSync(
        node,
        [EntryScript, "serve", "--gpu-slots", slots, "--config", configure(seats), "--state-dir", directory].concat([
          "--port",
          String(firstTestPort()),
        ]),
        { timeout, windowsHide: true, encoding: "utf8" },
      ).status;
    assert.strictEqual(exitOf("0", 2), 2, "0 slots");
    assert.strictEqual(exitOf("3", 2), 2, "more slots than the two seats");
    assert.strictEqual(exitOf("2", 1), 2, "two slots on one seat");
    // The same option in range is not a usage error: the scheduler starts (on its private port) and is stopped by the
    // timeout, so that the three exits above are not the exit of an option nobody knows
    assert.notStrictEqual(exitOf("2", 2, 4000), 2, "two slots on two seats start the scheduler");
  } finally {
    removeDirectory(directory);
  }
}

export const ProbeChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  checkProbeRun,
  checkProbeTimeoutKillsTheProcess,
  checkProbeOutputLimits,
  checkProbeRunnerInterval,
  checkProbeRunnerStopAll,
  checkProbeTimeoutKillsTheTree,
  checkGpuSlotsRange,
  checkGpuSlotsUsageErrors,
};
