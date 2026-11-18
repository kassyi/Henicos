// Self-tests of the Henicos repository plugin: pre-move classification/budget characterization (46 commands),
// moved budget/CPU-only project checks, worktree vs configRoot budget resolution, --gpu refuse/hint checks,
// QueuedRun wire memoryMb (request, adopt, gpuTurn omission, JOB_QUEUE_PLUGINS=0, defaultTestMemoryMb),
// and shortCommand prefix removal.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { createRequire } from "node:module";
import { QueuedRun } from "../job-queue/client-queued-run.ts";
import type { RunOptions } from "../job-queue/client.ts";
import { loadConfig } from "../job-queue/config.ts";
import { EntryScript } from "../job-queue/constants.ts";
import type { TargetDialectModule } from "../job-queue/lib/types.ts";
import { PluginApiVersion, type AssertModule, type QueuePlugin } from "../job-queue/plugin-api.ts";
import { withConfigEnv } from "../job-queue/plugin-checks.ts";
import { PluginHost, type BaseClientContext } from "../job-queue/plugin-host.ts";
import { FakeHost, FakeScheduler, quietly, until } from "../job-queue/self-test-client.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import { shortCommand } from "../job-queue/view.ts";
import { selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { cpuOnlyTestProjects, parseBudgets, testMemoryMb } from "./budgets.ts";
import { isHenicosTest } from "./plugin.ts";

const require = createRequire(import.meta.url);
const { dialectOf } = require("./target-dialect.js") as TargetDialectModule;

const FixedBudgetsYaml = [
  "# fixed characterization budgets",
  "Kassyi.GSharp.Compiler.Generators.Tests: 192",
  "Kassyi.GSharp.Stdlib.GpuMath.Tests: 1536",
  "Kassyi.GSharp.Tests.Cpu: 2880, stress 6720",
  "Kassyi.GSharp.Tests.Cuda: 2496, stress 6784",
  "Kassyi.GSharp.Tests.Integration: 128",
  "",
].join("\n");

const CharacterizationTable: readonly [string, boolean, number][] = [
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", true, 2880],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj -c Release", true, 2880],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --no-build", true, 2496],
  ["dotnet test tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests -c Release", true, 1536],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Integration", true, 128],
  ["dotnet.exe test tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests", true, 192],
  ['"C:\\Program Files\\dotnet\\dotnet.exe" test tests/Compiler/Kassyi.GSharp.Tests.Cpu', true, 2880],
  [
    "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Integration && dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu",
    true,
    2880,
  ],
  [
    "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu && dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda",
    true,
    2880,
  ],
  ["dotnet test", true, 7232],
  ["dotnet test Henicos.slnx -c Release", true, 7232],
  ["dotnet vstest bin/Debug/SomeTests.dll", true, 7232],
  ["dotnet stryker -p Kassyi.GSharp.Compiler", true, 7232],
  ["dotnet-stryker --project Kassyi.GSharp.Tests.Cpu", true, 2880],
  ['dotnet test --filter "Category!=Stress&Category!=Benchmark"', true, 7232],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category!=Stress"', true, 2880],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category!~Benchmark"', true, 2496],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category=Stress"', true, 6720],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category=Benchmark"', true, 6784],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category!=Stress|Category=Benchmark"', true, 6784],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category != Stress&Category!=Benchmark"', true, 2880],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter \"Category!='Stress'\"", true, 6720],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category!=Foo-Stress"', true, 6720],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "FullyQualifiedName~Stressful"', true, 2880],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category=Benchmark.Long"', true, 6720],
  ["just test", true, 7232],
  ["just -f justfile test-category Runtime", true, 7232],
  ['just test-category Stress "--project" kassyi.gsharp.tests.cpu', true, 6720],
  ["just test-differential-cuda", true, 7232],
  ["just a=1 test-coverage", true, 7232],
  ["just coverage-diff", true, 7232],
  ["just mutate", true, 7232],
  ["just long-checks", true, 7232],
  ["just build", false, 0],
  ["just doctest", false, 0],
  ["just queue-watch", false, 0],
  ["node scripts/human/test-stability.js --count 5", true, 7232],
  ["node scripts/human/test-memory.js Kassyi.GSharp.Tests.Cpu", true, 2880],
  ["node scripts/human/test-sanitized.js", true, 7232],
  ["node scripts/human/long-checks.js --dry-run", true, 7232],
  ["node scripts/human/mutate.js --phase compiler", true, 7232],
  ["node scripts/human/coverage-diff.js --base main", true, 7232],
  ["dotnet build Henicos.slnx", false, 0],
  ["dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cpu -c Release", false, 0],
  ["node scripts/human/fuzz-differential.js --minutes 5", false, 0],
  ['node -e "console.log(1)"', false, 0],
];

const ShortCharacterizationTable: readonly [string, string][] = [
  [
    "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj -c Release",
    "test Tests.Cpu.csproj -c Release",
  ],
  [
    "export DiffEngine_Disabled=true; dotnet build tests/A -nodeReuse:false > tmp/b.log 2>&1 && " +
      "dotnet test tests/A --no-build > tmp/t.log 2>&1; dotnet build tests/B >x 2>&1",
    "build A && test A --no-build ; build B",
  ],
  ["CI=true DiffEngine_Disabled=true dotnet test tests/X", "test X"],
  ["a=1 b=2", "a=1 b=2"],
  ["just test-category Runtime", "test-category Runtime"],
  ["just doctest", "doctest"],
  ["just test-differential-cuda", "test-differential-cuda"],
  ["dotnet build src/Compiler/Kassyi.GSharp.Compiler", "build Compiler"],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda", "test Tests.Cuda"],
  ["dotnet build Henicos.slnx -c Release", "build Henicos.slnx -c Release"],
  ["node scripts/human/fuzz-differential.js --minutes 20", "fuzz-differential.js --minutes 20"],
  ["export CI=true; just test-differential-cpu > tmp/diff.log 2>&1", "test-differential-cpu"],
  ["echo hello", "echo hello"],
];

function contextFor(worktree: string, configRoot: string): BaseClientContext {
  return {
    cwd: worktree,
    worktree,
    configRoot,
    shell: "bash",
    env: {},
    log: () => {},
  };
}

function baseRunOptions(command: string, defaultTestMemoryMb = 0): RunOptions {
  return {
    port: 0,
    slots: 2,
    memoryMb: 1024,
    policy: "hrrn",
    command,
    commandWords: null,
    weight: 1,
    gpu: false,
    shell: "",
    timeoutMinutes: 0,
    after: 0,
    keepIfFailed: false,
    defaultTestMemoryMb,
  };
}

/** Moved from self-test.ts: parseBudgets, 2-assembly budget assertions, and empty-budget fallback. */
function checkMovedBudgetBasics(assert: AssertModule): void {
  const budgets = parseBudgets(
    ["# comment", "Kassyi.GSharp.Tests.Cpu: 2880, stress 6720", "Kassyi.GSharp.Tests.Integration: 128", ""].join("\n"),
  );
  assert.deepStrictEqual(budgets.get("Kassyi.GSharp.Tests.Cpu"), { normal: 2880, stress: 6720 });
  assert.deepStrictEqual(budgets.get("Kassyi.GSharp.Tests.Integration"), { normal: 128, stress: 128 });
  const stress = (cmd: string): boolean => selectsCategory(cmd, ["Stress", "Benchmark"]);
  const memory = (command: string): number => testMemoryMb(command, isHenicosTest(command), budgets, stress);
  assert.strictEqual(memory("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu"), 2880);
  assert.strictEqual(memory('just test-category Stress "--project" kassyi.gsharp.tests.cpu'), 6720);
  assert.strictEqual(memory("just test"), 2880 + 128, "a whole-solution run counts every test host");
  assert.strictEqual(
    memory('dotnet test --filter "Category!=Stress&Category!=Benchmark"'),
    2880 + 128,
    "a filter that leaves the stress tests out counts the normal budgets",
  );
  assert.strictEqual(memory("dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cpu"), 0, "a build takes no memory");
  assert.strictEqual(testMemoryMb("just test", true, new Map(), stress), 0, "without budgets nothing is held back");
}

/** Moved from self-test.ts: CPU-only test project detection for run --gpu hints. */
function checkMovedCpuOnlyProjects(assert: AssertModule): void {
  const tests = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-tests-"));
  try {
    const project = (category: string, name: string, body: string): void => {
      fs.mkdirSync(path.join(tests, category, name), { recursive: true });
      fs.writeFileSync(
        path.join(tests, category, name, `${name}.csproj`),
        `<Project><PropertyGroup>${body}</PropertyGroup></Project>`,
      );
    };
    project("Compiler", "Suite.Cpu", "<GSharpTargetDialect>Cpu</GSharpTargetDialect>");
    project("Compiler", "Suite.Cuda", "<GSharpTargetDialect>Cuda</GSharpTargetDialect>");
    project("Stdlib", "Suite.Math", "<CudaCapability>89</CudaCapability>");
    const names = ["Suite.Cpu", "Suite.Cuda", "Suite.Math", "Suite.Missing"];
    const cpuOnly = (command: string): string[] => cpuOnlyTestProjects(command, names, tests, () => {});
    assert.deepStrictEqual(cpuOnly("dotnet test tests/Compiler/Suite.Cpu --no-build"), ["Suite.Cpu"]);
    assert.deepStrictEqual(
      cpuOnly("dotnet test tests/Compiler/Suite.Cpu && dotnet test tests/Compiler/Suite.Cuda"),
      [],
    );
    assert.deepStrictEqual(cpuOnly("dotnet test tests/Stdlib/Suite.Math"), [], "no dialect in the csproj: no hint");
    assert.deepStrictEqual(cpuOnly("dotnet test Suite.Missing"), [], "no csproj: no hint");
    assert.deepStrictEqual(cpuOnly("just test"), [], "a command that names no project: no hint");
  } finally {
    removeDirectory(tests);
  }
}

/**
 * Checks 1 & 2 (mutant-verified): 46-command characterization table via PluginHost against a fixed temporary
 * budgets file, plus worktree vs configRoot fallback and whole-solution sum.
 */
function checkCharacterizationAndBudgets(assert: AssertModule, host: PluginHost): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-char-"));
  try {
    const base = fs.realpathSync.native(dir);
    const wt = path.join(base, "wt");
    const cfg = path.join(base, "cfg");
    const wtEmpty = path.join(base, "wt-empty");
    fs.mkdirSync(path.join(wt, "tests"), { recursive: true });
    fs.mkdirSync(path.join(cfg, "tests"), { recursive: true });
    fs.mkdirSync(wtEmpty, { recursive: true });
    fs.writeFileSync(path.join(wt, "tests", "memory-budgets.yml"), FixedBudgetsYaml);
    fs.writeFileSync(path.join(cfg, "tests", "memory-budgets.yml"), "Kassyi.GSharp.Tests.Cpu: 1111\n");

    const ctx = contextFor(wt, cfg);
    for (const [command, expectedTest, expectedMemoryMb] of CharacterizationTable) {
      const classified = host.classify(command, null, ctx);
      assert.strictEqual(Boolean(classified.test), expectedTest, `classify.test for: ${command}`);
      assert.strictEqual(classified.memoryMb ?? 0, expectedMemoryMb, `classify.memoryMb for: ${command}`);
    }

    // Worktree budgets override configRoot; missing worktree budgets fall back to configRoot; missing both -> 0
    assert.strictEqual(
      host.classify("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", null, contextFor(wtEmpty, cfg)).memoryMb,
      1111,
      "falls back to configRoot budgets when worktree lacks the file",
    );
    assert.strictEqual(
      host.classify("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", null, contextFor(wtEmpty, wtEmpty)).memoryMb,
      0,
      "missing budgets in both worktree and configRoot -> 0",
    );
  } finally {
    removeDirectory(dir);
  }
}

/**
 * Check 3 (mutant-verified): --gpu + fuzz-diff/long-checks -> refuse; --gpu + CPU-only project -> 1 hint, no refuse;
 * non-GPU -> no refuse and no hints.
 */
function checkGpuCheckHook(assert: AssertModule, host: PluginHost): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-check-"));
  try {
    const wt = fs.realpathSync.native(dir);
    fs.mkdirSync(path.join(wt, "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu"), { recursive: true });
    fs.mkdirSync(path.join(wt, "tests", "Compiler", "Kassyi.GSharp.Tests.Cuda"), { recursive: true });
    fs.writeFileSync(
      path.join(wt, "tests", "memory-budgets.yml"),
      ["Kassyi.GSharp.Tests.Cpu: 2880", "Kassyi.GSharp.Tests.Cuda: 2496", ""].join("\n"),
    );
    fs.writeFileSync(
      path.join(wt, "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Kassyi.GSharp.Tests.Cpu.csproj"),
      "<Project><PropertyGroup><GSharpTargetDialect>Cpu</GSharpTargetDialect></PropertyGroup></Project>",
    );
    fs.writeFileSync(
      path.join(wt, "tests", "Compiler", "Kassyi.GSharp.Tests.Cuda", "Kassyi.GSharp.Tests.Cuda.csproj"),
      "<Project><PropertyGroup><GSharpTargetDialect>Cuda</GSharpTargetDialect></PropertyGroup></Project>",
    );
    const ctx = contextFor(wt, wt);
    const gpu = { name: "gpu", forcedExclusive: false };

    const fuzz = host.check("node scripts/human/fuzz-differential.js --minutes 5", gpu, ctx);
    assert.strictEqual(
      fuzz.refuse,
      "fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.",
    );
    const longChecks = host.check("just long-checks", gpu, ctx);
    assert.strictEqual(
      longChecks.refuse,
      "fuzz-diff and long-checks take the GPU lock themselves: queue them without --gpu.",
    );

    const cpuHint = host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", gpu, ctx);
    assert.strictEqual(cpuHint.refuse, undefined, "CPU-only test project must warn via hint, never refuse");
    assert.strictEqual(cpuHint.hints?.length, 1);
    assert.ok(cpuHint.hints?.[0]?.includes("Kassyi.GSharp.Tests.Cpu"));
    assert.ok(cpuHint.hints?.[0]?.includes("GSharpTargetDialect Cpu"));

    const cudaCheck = host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda", gpu, ctx);
    assert.strictEqual(cudaCheck.refuse, undefined);
    assert.strictEqual(cudaCheck.hints, undefined);

    // Without --gpu (resource = null): neither refuse nor hints
    assert.deepStrictEqual(host.check("just long-checks", null, ctx), {});
    assert.deepStrictEqual(host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", null, ctx), {});
  } finally {
    removeDirectory(dir);
  }
}

/**
 * Check 5 (mutant-verified): QueuedRun sends plugin-computed memoryMb on request and adopt, omits it on gpuTurn and
 * JOB_QUEUE_PLUGINS=0, and uses defaultTestMemoryMb when a plugin classifies test: true without memoryMb.
 */
async function checkQueuedRunMemoryMb(assert: AssertModule, host: PluginHost): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-run-"));
  const prevCwd = process.cwd();
  try {
    const wt = fs.realpathSync.native(dir);
    fs.mkdirSync(path.join(wt, "tests"), { recursive: true });
    fs.writeFileSync(path.join(wt, "tests", "memory-budgets.yml"), FixedBudgetsYaml);
    process.chdir(wt);

    // 1. Normal test run: request and adopt carry memoryMb: 2880
    const testCmd = "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu";
    const fake1 = new FakeHost();
    void new QueuedRun(
      baseRunOptions(testCmd),
      { weight: 1, pin: null, before: null },
      false,
      false,
      fake1,
      host,
    ).run();
    await until("client 1 connects", () => fake1.connects.length === 1);
    fake1.connects[0]?.resolve(await scheduler.connect());
    await until("scheduler accepts client 1", () => scheduler.accepted.length === 1);
    const conn1 = scheduler.accepted[0];
    assert.ok(conn1 !== undefined);
    await until("request 1 arrives", () => conn1.messages.some((m) => m.type === "request"));
    const req1 = conn1.messages.find((m) => m.type === "request");
    assert.strictEqual(req1?.memoryMb, 2880, "non-turn request sends plugin memoryMb");

    FakeScheduler.send(conn1, { type: "queued", id: 20, state: "waiting", ahead: 0, position: 1 });
    FakeScheduler.send(conn1, { type: "granted", id: 20, cpus: 2, capacity: 4, protocol: 3 });
    await until("command 1 starts", () => fake1.commands.length === 1);
    conn1.socket.destroy();
    await until("client 1 reconnects", () => fake1.connects.length === 2);
    fake1.connects[1]?.resolve(await scheduler.connect());
    await until("scheduler accepts adopt 1", () => scheduler.accepted.length === 2);
    const conn1b = scheduler.accepted[1];
    assert.ok(conn1b !== undefined);
    await until("adopt 1 arrives", () => conn1b.messages.some((m) => m.type === "adopt"));
    const adopt1 = conn1b.messages.find((m) => m.type === "adopt");
    assert.strictEqual(adopt1?.memoryMb, 2880, "adopt carries identical memoryMb");
    fake1.commands[0]?.end({ code: 0, signaled: false });
    await until("done 1 arrives", () => conn1b.messages.some((m) => m.type === "done"));
    FakeScheduler.send(conn1b, { type: "done-ok", id: 20 });
    await until("client 1 exits", () => fake1.exits.length === 1);

    // 2. gpuTurn: true -> omits memoryMb (the inner GPU holder sends its own memoryMb)
    const fake2 = new FakeHost();
    void new QueuedRun(baseRunOptions(testCmd), { weight: 1, pin: null, before: null }, false, true, fake2, host).run();
    await until("client 2 connects", () => fake2.connects.length === 1);
    fake2.connects[0]?.resolve(await scheduler.connect());
    await until("scheduler accepts client 2", () => scheduler.accepted.length === 3);
    const conn2 = scheduler.accepted[2];
    assert.ok(conn2 !== undefined);
    await until("request 2 arrives", () => conn2.messages.some((m) => m.type === "request"));
    const req2 = conn2.messages.find((m) => m.type === "request");
    assert.strictEqual(req2 !== undefined && "memoryMb" in req2, false, "gpuTurn omits memoryMb from request");
    conn2.socket.destroy();

    // 3. JOB_QUEUE_PLUGINS=0 (empty host) -> omits memoryMb
    const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
    const disabledHost = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
      ...process.env,
      JOB_QUEUE_PLUGINS: "0",
    });
    const fake3 = new FakeHost();
    void new QueuedRun(
      baseRunOptions(testCmd),
      { weight: 1, pin: null, before: null },
      false,
      false,
      fake3,
      disabledHost,
    ).run();
    await until("client 3 connects", () => fake3.connects.length === 1);
    fake3.connects[0]?.resolve(await scheduler.connect());
    await until("scheduler accepts client 3", () => scheduler.accepted.length === 4);
    const conn3 = scheduler.accepted[3];
    assert.ok(conn3 !== undefined);
    await until("request 3 arrives", () => conn3.messages.some((m) => m.type === "request"));
    const req3 = conn3.messages.find((m) => m.type === "request");
    assert.strictEqual(req3 !== undefined && "memoryMb" in req3, false, "JOB_QUEUE_PLUGINS=0 omits memoryMb");
    conn3.socket.destroy();

    // 4. Non-test command with active plugins sends explicit 0 (not omitted, so legacyClients does not fire)
    const fake4 = new FakeHost();
    void new QueuedRun(
      baseRunOptions("dotnet build Henicos.slnx"),
      { weight: 1, pin: null, before: null },
      false,
      false,
      fake4,
      host,
    ).run();
    await until("client 4 connects", () => fake4.connects.length === 1);
    fake4.connects[0]?.resolve(await scheduler.connect());
    await until("scheduler accepts client 4", () => scheduler.accepted.length === 5);
    const conn4 = scheduler.accepted[4];
    assert.ok(conn4 !== undefined);
    await until("request 4 arrives", () => conn4.messages.some((m) => m.type === "request"));
    const req4 = conn4.messages.find((m) => m.type === "request");
    assert.strictEqual(req4?.memoryMb, 0, "non-test command with plugins sends explicit memoryMb: 0");
    conn4.socket.destroy();

    // 5. Plugin returning test: true without memoryMb uses pools.defaultTestMemoryMb
    const testOnlyPlugin: QueuePlugin = {
      name: "test-only",
      apiVersion: PluginApiVersion,
      classify: () => ({ test: true }),
    };
    const fallbackHost = new PluginHost([testOnlyPlugin], [undefined], wt, () => {});
    const fake5 = new FakeHost();
    void new QueuedRun(
      baseRunOptions("custom-test", 777),
      { weight: 1, pin: null, before: null },
      false,
      false,
      fake5,
      fallbackHost,
    ).run();
    await until("client 5 connects", () => fake5.connects.length === 1);
    fake5.connects[0]?.resolve(await scheduler.connect());
    await until("scheduler accepts client 5", () => scheduler.accepted.length === 6);
    const conn5 = scheduler.accepted[5];
    assert.ok(conn5 !== undefined);
    await until("request 5 arrives", () => conn5.messages.some((m) => m.type === "request"));
    const req5 = conn5.messages.find((m) => m.type === "request");
    assert.strictEqual(req5?.memoryMb, 777, "defaultTestMemoryMb fills missing memoryMb when test: true");
    conn5.socket.destroy();
  } finally {
    process.chdir(prevCwd);
    scheduler.close();
    removeDirectory(dir);
  }
}

/** Check 6 (mutant-verified): shortCommand with plugins matches pre-move display including Kassyi.GSharp. removal. */
function checkShortCommandWithPlugins(assert: AssertModule, host: PluginHost): void {
  for (const [command, expected] of ShortCharacterizationTable) {
    assert.strictEqual(shortCommand(command, host), expected, `shortCommand for: ${command}`);
  }
}

/** Moved from plugin-checks.ts: real repo baseline config check. */
async function checkRepoBaselineConfig(assert: AssertModule): Promise<void> {
  await withConfigEnv(undefined, () => {
    const repoWarnings: string[] = [];
    const repoConfig = loadConfig({
      mode: "status",
      fromDirectory: path.dirname(EntryScript),
      warn: (msg) => repoWarnings.push(msg),
    });
    assert.strictEqual(repoConfig.status, "ok", "repo job-queue.config.json must load cleanly");
    assert.deepStrictEqual(repoWarnings, [], "repo job-queue.config.json must have no warnings");
    assert.deepStrictEqual(repoConfig.config.plugins, [
      { name: "./scripts/human/job-queue-henicos" },
      { name: "dotnet" },
      {
        name: "just",
        options: {
          hookRecipes: "^(test\\S*|coverage-diff|fuzz-\\S+|mutate|long-checks|bench\\S*|doctest)$",
          hookDryRunRecipes: "^(mutate|long-checks)$",
        },
      },
      { name: "nvidia-gpu" },
      { name: "agents" },
    ]);
    assert.strictEqual(repoConfig.config.resources?.gpu?.slots, 1);
    assert.strictEqual(repoConfig.config.resources?.gpu?.lock?.seats.length, 1);
    assert.strictEqual(repoConfig.config.resources?.gpu?.probe, undefined);
    assert.deepStrictEqual(repoConfig.config.legacyClients, {
      testPattern: [
        String.raw`\bdotnet(\.exe)?"?\s+(test|vstest)\b`,
        String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate|long-checks)(\s|$)`,
      ].join("|"),
      testMemoryMb: 2880,
    });
  });
}

/**
 * Moved from lib-checks.ts: dialectOf reads `<GSharpTargetDialect> cpu </GSharpTargetDialect>` (lowercase, whitespace)
 * -> "cpu", missing -> null.
 */
function checkDialectOf(assert: AssertModule): void {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dialect-test-"));
  try {
    const cpuProj = path.join(tempDir, "cpu.csproj");
    fs.writeFileSync(
      cpuProj,
      "<Project><PropertyGroup>\r\n  <GSharpTargetDialect> cpu </GSharpTargetDialect>\r\n</PropertyGroup></Project>",
    );
    assert.strictEqual(dialectOf(cpuProj), "cpu", "lowercase dialect with whitespace must return 'cpu'");

    const noneProj = path.join(tempDir, "none.csproj");
    fs.writeFileSync(
      noneProj,
      "<Project><PropertyGroup>\r\n  <TargetFramework>net9.0</TargetFramework>\r\n</PropertyGroup></Project>",
    );
    assert.strictEqual(dialectOf(noneProj), null, "project without GSharpTargetDialect must return null");
  } finally {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // Cleanup tolerance
    }
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  await checkRepoBaselineConfig(assert);
  checkDialectOf(assert);
  const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
  assert.strictEqual(
    loaded.config.resources?.gpu?.lock?.seats[0],
    "tmp/gpu-test-lock",
    "seats[0] stays tmp/gpu-test-lock for older gpu-lock.js and fuzz-diff",
  );
  const host = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
    ...process.env,
    JOB_QUEUE_PLUGINS: undefined,
  });
  assert.ok(
    host.plugins.some((p) => p.name === "./scripts/human/job-queue-henicos"),
    "henicos plugin loaded from repo config",
  );

  checkMovedBudgetBasics(assert);
  checkMovedCpuOnlyProjects(assert);
  checkCharacterizationAndBudgets(assert, host);
  checkGpuCheckHook(assert, host);
  await quietly(() => checkQueuedRunMemoryMb(assert, host));
  checkShortCommandWithPlugins(assert, host);
}
