// Offload, QueuedRun wire memoryMb, repo baseline config, and Data-vs-Command guard checks for job-queue-henicos.

import { createRequire } from "node:module";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { QueuedRun } from "../job-queue/client-queued-run.ts";
import type { RunOptions } from "../job-queue/client.ts";
import { loadConfig } from "../job-queue/config.ts";
import { EntryScript } from "../job-queue/constants.ts";
import type { TargetDialectModule } from "../job-queue/lib/types.ts";
import { PluginApiVersion, type AssertModule, type ClientContext, type QueuePlugin } from "../job-queue/plugin-api.ts";
import { withConfigEnv } from "../job-queue/plugin-checks.ts";
import { PluginHost, type BaseClientContext } from "../job-queue/plugin-host.ts";
import { translateForRemote } from "../job-queue/remote/command-translator.ts";
import { resolveCallerEnv } from "../job-queue/remote/env-policy.ts";
import { resolveOffloadPreference } from "../job-queue/remote/executor.ts";
import { buildRemoteFindPruneExpr, isExcludedSyncPath } from "../job-queue/remote/syncer.ts";
import { FakeHost, FakeScheduler, until } from "../job-queue/self-test-client.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import type { ListResult } from "./name-filter.ts";
import {
  buildOffloadListJobArgs,
  checkHenicosOffload,
  combineFilterWithWindowsPlatform,
  offloadListVerdict,
  type OffloadListRunner,
} from "./offload-probe.ts";

const require = createRequire(import.meta.url);
const { remoteOffloadOf } = require("./target-dialect.js") as TargetDialectModule;

export function baseRunOptions(command: string, defaultTestMemoryMb = 0): RunOptions {
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

/**
 * Check 5 (mutant-verified): QueuedRun sends plugin-computed memoryMb on request and adopt, omits it on gpuTurn and
 * JOB_QUEUE_PLUGINS=0, and uses defaultTestMemoryMb when a plugin classifies test: true without memoryMb.
 */
export async function checkQueuedRunMemoryMb(
  assert: AssertModule,
  host: PluginHost,
  fixedBudgetsYaml: string,
): Promise<void> {
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-run-"));
  const prevCwd = process.cwd();
  try {
    const wt = fs.realpathSync.native(dir);
    fs.mkdirSync(path.join(wt, "tests"), { recursive: true });
    fs.writeFileSync(path.join(wt, "tests", "memory-budgets.yml"), fixedBudgetsYaml);
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

/** Moved from plugin-checks.ts: real repo baseline config check. */
export async function checkRepoBaselineConfig(assert: AssertModule): Promise<void> {
  await withConfigEnv(undefined, () => {
    const repoConfig = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
    assert.deepStrictEqual(repoConfig.config.plugins, [
      {
        name: "./scripts/human/job-queue-henicos",
        options: {
          exclusiveCategories: ["Stress", "Benchmark", "GpuTrap"],
          exclusiveEnv: ["GSHARP_CUDA_TRAP_TESTS"],
          sharedRecipes: ["test", "test-coverage", "test-differential-cuda"],
        },
      },
      { name: "dotnet" },
      {
        name: "just",
        options: {
          hookRecipes: "^(test\\S*|coverage-diff|fuzz-\\S+|mutate|bench\\S*|doctest)$",
          hookDryRunRecipes: "^(mutate)$",
        },
      },
      { name: "nvidia-gpu" },
      { name: "git" },
      {
        name: "agents",
        options: {
          sessionEnv: ["CLAUDE_CODE_SESSION_ID", "ANTIGRAVITY_CONVERSATION_ID"],
          chatStatus: "scripts/human/chat-status.js",
        },
      },
    ]);
    assert.strictEqual(repoConfig.config.resources?.gpu?.slots, 2);
    assert.strictEqual(repoConfig.config.resources?.gpu?.lock?.seats.length, 2);
    assert.deepStrictEqual(repoConfig.config.resources?.gpu?.probe, {
      argv: ["nvidia-smi", "--query-gpu=memory.free", "--format=csv,noheader,nounits"],
      min: 3072,
      timeoutMs: 3000,
      settleMs: 15000,
    });
    assert.deepStrictEqual(repoConfig.config.legacyClients, {
      testPattern: [
        String.raw`\bdotnet(\.exe)?"?\s+(test|vstest)\b`,
        String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate)(\s|$)`,
      ].join("|"),
      testMemoryMb: 2880,
    });
  });
}

export function checkOffloadClassification(assert: AssertModule, host: PluginHost, repoRoot: string): void {
  const ctx: BaseClientContext = {
    cwd: repoRoot,
    worktree: repoRoot,
    configRoot: repoRoot,
    shell: "pwsh",
    env: {},
    log: () => {},
  };
  const cpuTests = "tests/Compiler/Kassyi.GSharp.Tests.Cpu";
  const genTests = "tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests";
  const gpuMathTests = "tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests";
  const benchTests = "tests/Apps/Henicos.Benchmarks.Tests";
  const collectEnvTests = "tests/Tools/Kassyi.GSharp.Tools.CollectEnv.Tests";
  const cases: readonly [string, boolean][] = [
    [`dotnet test ${genTests}/Kassyi.GSharp.Compiler.Generators.Tests.csproj`, true],
    [`cd ${genTests} && dotnet test`, true],
    [`dotnet build ${genTests} && dotnet test --no-build ${genTests}`, true],
    [`dotnet build ${genTests} -c Release && dotnet test --no-build ${genTests} -c Release`, true],
    [`dotnet build ${collectEnvTests} && dotnet test ${collectEnvTests} --no-build`, true],
    [`dotnet build tests/Shared/Kassyi.GSharp.Tests.Infrastructure && dotnet test ${collectEnvTests}`, true],
    [`dotnet test ${benchTests} --filter 'Category!=GpuExecution'`, true],
    [`dotnet test ${benchTests} --filter 'Category!=GpuExecution&Category!=Stress'`, true],
    // Regex-free: non-local project with "Windows" in filter stays eligible for checkOffload --list-tests
    [`dotnet test ${collectEnvTests} --filter 'FullyQualifiedName~WindowsHelper'`, true],
    // Option A: just test-sanitized stays offloadable in classify; checkOffload enforces local auto vs --remote Linux ASan+UBSan
    ["just test-sanitized", true],
    ["just -f justfile test-sanitized", true],
    ["just --set blame_crash '' test-sanitized", true],
    // Marked <GSharpRemoteOffload>local</GSharpRemoteOffload> projects stay local with 1-line reason
    [`dotnet test ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj --filter 'FullyQualifiedName~WarpStackModelTests'`, false],
    [`$env:DiffEngine_Disabled='true'; dotnet test ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj`, false],
    [`dotnet build ${cpuTests} && dotnet test --no-build ${cpuTests}`, false],
    [`dotnet test ${gpuMathTests} --filter 'Category!=GpuExecution'`, false],
    ["just test-differential-cpu", false],
    ["just test-native-stack", false],
    ["just test-managed-stack", false],
    ["just -d . test-differential-cpu", false],
    // Whole-solution runs stay local with 1-line reason
    ["dotnet test", false],
    ["dotnet test Henicos.slnx", false],
    // Ineligible: bare build, standalone or mismatched --no-build, CUDA, unfiltered GpuMath/Benchmarks, `|` filter
    [`dotnet build ${genTests}/Kassyi.GSharp.Compiler.Generators.Tests.csproj`, false],
    [`dotnet test --no-build ${genTests}/Kassyi.GSharp.Compiler.Generators.Tests.csproj`, false],
    [`dotnet build ${genTests} -c Release && dotnet test --no-build ${genTests}`, false],
    [`dotnet build ${genTests} && dotnet test --no-build ${genTests} -c Release`, false],
    [`dotnet build ${cpuTests} && dotnet test --no-build ${genTests}`, false],
    [`dotnet build ${genTests} -o tmp/out && dotnet test --no-build ${genTests}`, false],
    [`dotnet build ${cpuTests} && dotnet test ${collectEnvTests}`, false],
    [`dotnet build ${gpuMathTests} && dotnet test ${collectEnvTests}`, false],
    [`dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cuda && dotnet test ${collectEnvTests}`, false],
    [`dotnet build Henicos.slnx && dotnet test ${collectEnvTests}`, false],
    ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj", false],
    [`dotnet test ${gpuMathTests}/Kassyi.GSharp.Stdlib.GpuMath.Tests.csproj`, false],
    [`dotnet test ${gpuMathTests} --filter 'Category!=GpuExecution|Category=GpuExecution'`, false],
    [`dotnet test ${benchTests}/Henicos.Benchmarks.Tests.csproj`, false],
    [`dotnet test ${benchTests} --filter 'Category!=GpuExecution|Category=GpuExecution'`, false],
    [`dotnet test ${cpuTests} --filter 'FullyQualifiedName~WindowsJobObjectTests'`, false],
    ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Integration/Kassyi.GSharp.Tests.Integration.csproj", false],
    ["just test", false],
  ];
  for (const [cmd, expected] of cases) {
    assert.strictEqual(Boolean(host.classify(cmd, null, ctx).offloadable), expected, `offloadable for: ${cmd}`);
  }
  assert.strictEqual(
    Boolean(
      host.classify("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", { name: "gpu", forcedExclusive: false }, ctx)
        .offloadable,
    ),
    false,
  );

  // Marked-local projects, CUDA builds, and whole-solution runs provide 1-line localReason
  for (const [cmd, expectedToken] of [
    [`dotnet test ${cpuTests}`, "GSharpRemoteOffload=local"],
    [`dotnet test ${gpuMathTests} --filter 'Category!=GpuExecution'`, "Kassyi.GSharp.Stdlib.GpuMath.Tests"],
    ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Integration", "Kassyi.GSharp.Tests.Integration"],
    ["just test-differential-cpu", "Kassyi.GSharp.Tests.Cpu"],
    ["dotnet test", "whole-solution"],
    ["dotnet test Henicos.slnx", "Henicos.slnx"],
    [`dotnet build ${cpuTests} && dotnet test ${collectEnvTests}`, "GSharpRemoteOffload=local"],
    [`dotnet build ${gpuMathTests} && dotnet test ${collectEnvTests}`, "Kassyi.GSharp.Stdlib.GpuMath.Tests"],
    [
      `dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cuda && dotnet test ${collectEnvTests}`,
      "Kassyi.GSharp.Tests.Cuda",
    ],
    [`dotnet build Henicos.slnx && dotnet test ${collectEnvTests}`, "Henicos.slnx"],
  ] as const) {
    const cls = host.classify(cmd, null, ctx);
    assert.strictEqual(cls.offloadable, false);
    assert.ok(cls.localReason?.includes(expectedToken), `expected localReason for ${cmd} to mention ${expectedToken}`);
    assert.ok(cls.localReason !== undefined && !/[\r\n]/.test(cls.localReason));
  }

  // Full repo plugin stack contributes Henicos + dotnet + just remote offload rules
  const rules = host.remoteOffload(ctx);
  assert.strictEqual(rules.defaultEnv.GSHARP_TEST_NO_CUDA, "1");
  assert.strictEqual(rules.defaultEnv.DiffEngine_Disabled, "true");
  assert.ok(rules.allowedPrograms.has("dotnet"));
  assert.ok(rules.allowedPrograms.has("just"));
  const syncRules = { excludePrefixes: rules.syncExcludePrefixes, excludeFindNames: rules.syncExcludeFindNames };
  assert.strictEqual(
    isExcludedSyncPath("src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/Linq.cs", syncRules),
    true,
  );
  const prune = buildRemoteFindPruneExpr(syncRules);
  assert.ok(prune.includes("-path './src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded'"));
  assert.ok(prune.includes("! -iname '*.received.*'"));
  assert.ok(prune.includes("! -iname '*.user'"));
  assert.ok(prune.includes("! -iname '*.log'"));

  const plan = translateForRemote("just test-sanitized", {
    cwd: repoRoot,
    worktree: repoRoot,
    shell: "pwsh",
    places: {},
    cpus: 4,
    keepBuildServersWarm: true,
    env: {},
    remoteRules: rules,
  });
  assert.ok(plan !== null, "Henicos + just + dotnet plugins translate just test-sanitized");
  assert.strictEqual(plan.env.GSHARP_TEST_NO_CUDA, "1");
  assert.strictEqual(plan.env.DiffEngine_Disabled, "true");
  assert.strictEqual(plan.env.MSBUILDCORELIMIT, "4");

  const remote6g = {
    name: "ubuntu",
    host: "jobrunner@worker.example",
    workspace: "~/henicos",
    slots: 1,
    cpus: 4,
    memoryMb: 6144,
    mode: "prefer-remote" as const,
    maxJobMinutes: 30,
    sshOptions: [],
    keepBuildServersWarm: true,
    probe: null,
  };

  // Marked-local Tests.Cpu recipes route locally with 1-line reason and fail --remote
  for (const cmd of ["just test-differential-cpu", "just test-native-stack", "just test-managed-stack"]) {
    const cls = host.classify(cmd, null, ctx);
    assert.strictEqual(cls.offloadable, false, `${cmd} must stay local`);
    assert.strictEqual(cls.memoryMb, 2880, `${cmd} must resolve to Tests.Cpu (2880 MB)`);
    const autoPref = resolveOffloadPreference({
      options: { ...baseRunOptions(cmd), remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: Boolean(cls.offloadable),
      classifiedLocalReason: cls.localReason,
      memoryMb: cls.memoryMb,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
    });
    assert.strictEqual(autoPref.ok, true);
    if (autoPref.ok) {
      assert.strictEqual(autoPref.offload, "never");
      assert.ok(autoPref.reason?.includes("running locally:"));
    }
    const forcePref = resolveOffloadPreference({
      options: { ...baseRunOptions(cmd), remote: true, remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: Boolean(cls.offloadable),
      classifiedLocalReason: cls.localReason,
      memoryMb: cls.memoryMb,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
    });
    assert.strictEqual(forcePref.ok, false, `${cmd} with --remote must fail closed`);
  }

  // Option A: just test-sanitized resolves to 2880 MB <= 6144 MB; checkOffload gates auto (local) vs --remote (allow)
  for (const cmd of [
    "just test-sanitized",
    "just -f justfile test-sanitized",
    "just --set blame_crash '' test-sanitized",
  ]) {
    const cls = host.classify(cmd, null, ctx);
    assert.strictEqual(cls.offloadable, true, `${cmd} must be offloadable in classify`);
    assert.strictEqual(cls.memoryMb, 2880, `${cmd} must resolve to Tests.Cpu (2880 MB), not whole-solution sum`);
    const autoPref = resolveOffloadPreference({
      options: { ...baseRunOptions(cmd), remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: Boolean(cls.offloadable),
      memoryMb: cls.memoryMb,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
    });
    assert.deepStrictEqual(autoPref, { ok: true, offload: "auto" }, `${cmd} must qualify for 6144 MB remote worker`);
    const forcePref = resolveOffloadPreference({
      options: { ...baseRunOptions(cmd), remote: true, remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: Boolean(cls.offloadable),
      memoryMb: cls.memoryMb,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
    });
    assert.deepStrictEqual(forcePref, { ok: true, offload: "force" }, `${cmd} with --remote must qualify`);
  }

  // Regression: compound commands must never under-estimate an implicit heavy step or over-estimate a build step.
  assert.strictEqual(
    host.classify(`dotnet test ${genTests} && just mutate compiler`, null, ctx).memoryMb,
    2880,
    "compound explicit lightweight + implicit heavy step must take max(64, 2880) = 2880 MB",
  );
  assert.strictEqual(
    host.classify("dotnet build src/Compiler/Kassyi.GSharp.Compiler && just test-sanitized", null, ctx).memoryMb,
    2880,
    "build step before implicit single-project test must not fall back to whole-solution sum",
  );

  // Regression: implicit CPU-only recipes emit --gpu hint symmetrically with explicit Tests.Cpu runs.
  const gpu = { name: "gpu", forcedExclusive: false };
  for (const cpuRecipe of ["just test-sanitized", "just mutate compiler", "node scripts/human/test-sanitized.js"]) {
    const res = host.check(cpuRecipe, gpu, ctx);
    assert.strictEqual(res.refuse, undefined, `${cpuRecipe} under --gpu must hint, not refuse`);
    assert.strictEqual(res.hints?.length, 1, `${cpuRecipe} under --gpu must emit CPU-only hint`);
  }

  // Regression: Data-vs-Command — paths, filters, and redirections naming `long-checks` must never be refused.
  const benchProj = "src/Apps/Henicos.Benchmarks";
  for (const allowed of [
    `dotnet run --project ${benchProj} -c Release -- generator-bench --artifacts tmp/long-checks/run/bench`,
    `dotnet run --project ${benchProj} -c Release -- bench-compare tmp/long-checks/a tmp/long-checks/b`,
    `dotnet test ${cpuTests} > tmp/long-checks/probe.log 2>&1`,
    `dotnet test ${cpuTests} --filter "FullyQualifiedName~long-checks"`,
  ]) {
    assert.deepStrictEqual(host.check(allowed, null, ctx), {}, `data mentioning long-checks must pass: ${allowed}`);
  }
  for (const refused of [
    "just long-checks",
    "just -f justfile long-checks --dry-run",
    "node scripts/human/long-checks.js --steps fuzz",
    "echo ok | just long-checks",
  ]) {
    assert.ok(host.check(refused, null, ctx).refuse !== undefined, `invoking long-checks must refuse: ${refused}`);
  }
}

export function checkHenicosEnvAllowlist(assert: AssertModule, host: PluginHost, repoRoot: string): void {
  const ctx: BaseClientContext = {
    cwd: repoRoot,
    worktree: repoRoot,
    configRoot: repoRoot,
    shell: "pwsh",
    env: {},
    log: () => {},
  };
  const rules = host.remoteOffload(ctx);

  for (const allowed of [
    "TEST_SEED",
    "CI",
    "DiffEngine_Disabled",
    "GSHARP_TEST_NO_CUDA",
    "GSHARP_TEST_NO_CPU",
    "GSHARP_TEST_VERBOSE",
    "GSHARP_TEST_MEMORY_BUDGET_MB",
    "GSHARP_TEST_MEMORY_DUMP",
    "GSHARP_TEST_MEMORY_DUMP_AT_END",
    "GSHARP_TEST_BUILD_TIMEOUT_SECONDS",
    "GSHARP_MEMORY_FIXTURE_MB",
    "GSharpNativeSanitizers",
    "GSharpTestHeapHardLimit",
    "VSTEST_CONNECTION_TIMEOUT",
    "DOTNET_CLI_UI_LANGUAGE",
    "DOTNET_NOLOGO",
    "DOTNET_CLI_TELEMETRY_OPTOUT",
    "VSLANG",
  ]) {
    assert.ok(rules.allowedEnv.has(allowed), `expected ${allowed} in allowedEnv`);
  }

  for (const localOnly of [
    "GSHARP_TEST_MEMORY_REPORT",
    "GSHARP_TEST_BUILD_LOG_DIR",
    "GSHARP_CUDA_INCLUDE_DIR",
    "GSHARP_SIGNING_KEY",
    "GH_TOKEN",
    "GSHARP_CUDA_TRAP_TESTS",
    "GSHARP_CUDA_TRAP_PAUSE_MS",
    "GSHARP_DIFF_FULL_CUDA_TRAPS",
    "GSHARP_DIFF_CUDA_TRAP_TARGET",
    "GSHARP_DIFF_CUDA_TRAP_RESULT_FILE",
    "GSHARP_TEST_ISOLATED_METHOD",
    "FUZZ_DIFF_LAUNCHER_PIDS",
    "CUDA_VISIBLE_DEVICES",
    "CXX",
    "CPATH",
    "CPLUS_INCLUDE_PATH",
    "CCC_OVERRIDE_OPTIONS",
    "GCC_EXEC_PREFIX",
    "COMPILER_PATH",
    "CL",
    "_CL_",
    "INCLUDE",
    "ASAN_OPTIONS",
    "UBSAN_OPTIONS",
    "LD_PRELOAD",
    "MSBUILDCORELIMIT",
    "MSBUILDNODECOREALLOCATIONWEIGHT",
  ]) {
    assert.ok(rules.localOnlyEnv.has(localOnly), `expected ${localOnly} in localOnlyEnv`);
    assert.strictEqual(rules.allowedEnv.has(localOnly), false, `${localOnly} must not be in allowedEnv`);
  }

  for (const excluded of [
    "CUDA_PATH",
    "CUDA_PATH_V13_3",
    "DOTNET_ROOT",
    "MSBUILDDISABLENODEREUSE",
    "PATH",
    "GSHARP_JOB_QUEUE_STATE_DIR",
  ]) {
    assert.strictEqual(rules.allowedEnv.has(excluded), false, `${excluded} must not be in allowedEnv`);
    assert.strictEqual(rules.localOnlyEnv.has(excluded), false, `${excluded} must not be in localOnlyEnv`);
  }

  const testCmd = "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu";
  const fwdPlan = translateForRemote(testCmd, {
    cwd: repoRoot,
    worktree: repoRoot,
    shell: "pwsh",
    places: {},
    cpus: 4,
    keepBuildServersWarm: true,
    env: {
      TEST_SEED: "42",
      CI: "true",
      GSharpNativeSanitizers: "address,undefined",
      GSHARP_TEST_VERBOSE: "1",
      MSBUILDDISABLENODEREUSE: "1",
    },
    remoteRules: rules,
  });
  assert.ok(fwdPlan !== null);
  assert.strictEqual(fwdPlan.env.TEST_SEED, "42");
  assert.strictEqual(fwdPlan.env.CI, "true");
  assert.strictEqual(fwdPlan.env.GSharpNativeSanitizers, "address,undefined");
  assert.strictEqual(fwdPlan.env.GSHARP_TEST_VERBOSE, "1");
  assert.strictEqual(fwdPlan.env.MSBUILDDISABLENODEREUSE, undefined);
  assert.strictEqual(fwdPlan.env.DiffEngine_Disabled, "true");
  assert.strictEqual(fwdPlan.env.GSHARP_TEST_NO_CUDA, "1");

  const defaultKeepPlan = translateForRemote(testCmd, {
    cwd: repoRoot,
    worktree: repoRoot,
    shell: "pwsh",
    places: {},
    cpus: 4,
    keepBuildServersWarm: true,
    env: { DiffEngine_Disabled: "false", GSHARP_TEST_NO_CUDA: "0" },
    remoteRules: rules,
  });
  assert.ok(defaultKeepPlan !== null);
  assert.strictEqual(defaultKeepPlan.env.DiffEngine_Disabled, "true");
  assert.strictEqual(defaultKeepPlan.env.GSHARP_TEST_NO_CUDA, "1");

  const remote6g = {
    name: "ubuntu",
    host: "jobrunner@worker.example",
    workspace: "~/henicos",
    slots: 1,
    cpus: 4,
    memoryMb: 6144,
    mode: "prefer-remote" as const,
    maxJobMinutes: 30,
    sshOptions: [],
    keepBuildServersWarm: true,
    probe: null,
  };

  // Global machine/queue infra env vars do not trigger local fallback
  const hostSafePref = resolveOffloadPreference({
    options: { ...baseRunOptions(testCmd), remotes: [remote6g] },
    inGpuLock: false,
    classifiedOffloadable: true,
    memoryMb: 2880,
    cwd: repoRoot,
    worktree: repoRoot,
    shell: "pwsh",
    places: {},
    remoteRules: rules,
    env: {
      CUDA_PATH: "C:\\Program Files\\NVIDIA GPU Computing Toolkit\\CUDA\\v13.3",
      CUDA_PATH_V13_3: "C:\\Program Files\\NVIDIA GPU Computing Toolkit\\CUDA\\v13.3",
      DOTNET_ROOT: "C:\\Program Files\\dotnet",
      MSBUILDDISABLENODEREUSE: "1",
      PATH: "C:\\Windows\\System32",
      GSHARP_JOB_QUEUE_STATE_DIR: "C:\\tmp\\state",
      GSHARP_JOB_QUEUE_REMOTE_WARM_SERVERS: "0",
      CI: "true",
    },
  });
  assert.deepStrictEqual(hostSafePref, { ok: true, offload: "auto" });

  // Local-only vars and path-valued allowlisted vars fall back locally (and refuse --remote)
  for (const [envObj, expectedVar] of [
    [{ GSHARP_SIGNING_KEY: "k" }, "GSHARP_SIGNING_KEY"],
    [{ GSHARP_TEST_MEMORY_REPORT: "tmp/mem" }, "GSHARP_TEST_MEMORY_REPORT"],
    [{ GSHARP_TEST_BUILD_LOG_DIR: "tmp/logs" }, "GSHARP_TEST_BUILD_LOG_DIR"],
    [{ CXX: "clang++-18" }, "CXX"],
    [{ GSHARP_CUDA_TRAP_TESTS: "1" }, "GSHARP_CUDA_TRAP_TESTS"],
    [{ MSBUILDCORELIMIT: "8" }, "MSBUILDCORELIMIT"],
    [{ TEST_SEED: "C:\\out" }, "TEST_SEED"],
    [{ GSharpNativeSanitizers: "/tmp/a.so" }, "GSharpNativeSanitizers"],
  ] as const) {
    const localPref = resolveOffloadPreference({
      options: { ...baseRunOptions(testCmd), remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: true,
      memoryMb: 2880,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
      env: envObj,
    });
    assert.strictEqual(localPref.ok, true, `expected local fallback for ${expectedVar}`);
    if (localPref.ok) {
      assert.strictEqual(localPref.offload, "never");
      assert.ok(localPref.reason?.includes(expectedVar), `expected reason to name ${expectedVar}`);
    }
    const remotePref = resolveOffloadPreference({
      options: { ...baseRunOptions(testCmd), remote: true, remotes: [remote6g] },
      inGpuLock: false,
      classifiedOffloadable: true,
      memoryMb: 2880,
      cwd: repoRoot,
      worktree: repoRoot,
      shell: "pwsh",
      places: {},
      remoteRules: rules,
      env: envObj,
    });
    assert.strictEqual(remotePref.ok, false, `expected --remote refusal for ${expectedVar}`);
    if (!remotePref.ok) {
      assert.ok(remotePref.error.includes(expectedVar));
    }
  }

  // Caller variables read by MSBuild or the runtime: the local-only prefixes match case-insensitively on every
  // platform, the allowlist wins over a prefix, and the ignore lists neither forward nor force local.
  for (const platform of ["win32", "linux"] as const) {
    for (const name of [
      "GSharpSkipCpuCompilation",
      "gsharpskipcpucompilation",
      "DOTNET_gcServer",
      "COMPlus_TieredCompilation",
      "VSTEST_HOST_DEBUG",
      "MSBuildSDKsPath",
      "NUGET_PACKAGES",
    ]) {
      const res = resolveCallerEnv({ [name]: "1" }, rules, platform);
      assert.ok(!res.ok && res.variable === name, `${platform}: ${name} must force local`);
    }
    for (const name of [
      "MSBUILDDISABLENODEREUSE",
      "DOTNET_SKIP_FIRST_TIME_EXPERIENCE",
      "DOTNET_ROOT",
      "GSHARP_JOB_QUEUE_OFFLOAD",
      "GSHARP_GPU_HOLD_WATCH",
      "GSHARP_GPU_TEST_SEAT",
      "gsharp_job_queue_state_dir",
    ]) {
      const res = resolveCallerEnv({ [name]: "1" }, rules, platform);
      assert.ok(res.ok && Object.keys(res.forwarded).length === 0, `${platform}: ${name} is ignored`);
    }
    const sanitizers = resolveCallerEnv({ GSharpNativeSanitizers: "address" }, rules, platform);
    assert.ok(sanitizers.ok && sanitizers.forwarded.GSharpNativeSanitizers === "address", `${platform}: allowlist`);
  }
  for (const [name, platform] of [
    ["Configuration", "win32"],
    ["configuration", "win32"],
    ["Configuration", "linux"],
    ["Platform", "win32"],
    ["OutDir", "win32"],
  ] as const) {
    assert.strictEqual(resolveCallerEnv({ [name]: "Release" }, rules, platform).ok, false, `${name} (${platform})`);
  }

  // Assignments written in the command go through the same policy: local-only names and path values keep the job
  // local, other names pass as written, and win32 respells an allowlisted or default name canonically.
  const proj = "tests/Shared/Kassyi.GSharp.Tests.Infrastructure.Tests/Kassyi.GSharp.Tests.Infrastructure.Tests.csproj";
  const plan = (command: string, shell: "pwsh" | "bash", env: Record<string, string> = {}) =>
    translateForRemote(command, {
      cwd: repoRoot,
      worktree: repoRoot,
      shell,
      places: {},
      cpus: 2,
      keepBuildServersWarm: true,
      env,
      remoteRules: rules,
      platform: "win32",
    });
  assert.ok(plan(`dotnet test ${proj}`, "bash") !== null, "the plain command offloads");
  for (const command of [
    `GSHARP_TEST_BUILD_LOG_DIR=C:/logs dotnet test ${proj}`,
    `LD_PRELOAD=/x.so dotnet test ${proj}`,
    `GSHARP_CUDA_TRAP_TESTS=1 dotnet test ${proj}`,
    `dotnet test ${proj} -e GSHARP_TEST_BUILD_LOG_DIR=C:/logs`,
    `dotnet test ${proj} --environment GSHARP_CUDA_TRAP_TESTS=1`,
    `dotnet test ${proj} --environment=DOTNET_gcServer=1`,
    `GSharpSkipCpuCompilation=true dotnet test ${proj}`,
    `TEST_SEED=C:/seed dotnet test ${proj}`,
    `MY_TOOL_DIR=/opt/tool dotnet test ${proj}`,
  ]) {
    assert.strictEqual(plan(command, "bash"), null, `stays local: ${command}`);
  }
  assert.strictEqual(plan(`$env:DOTNET_gcServer='1'; dotnet test ${proj}`, "pwsh"), null, "pwsh leading local-only");
  const passPlan = plan(`MY_FLAG=1 dotnet test ${proj} -e OTHER_FLAG=2`, "bash");
  assert.ok(
    passPlan !== null && passPlan.bashCommand.includes("MY_FLAG=1") && passPlan.bashCommand.includes("OTHER_FLAG=2"),
  );

  const keysLike = (env: Record<string, string>, name: string) =>
    Object.keys(env).filter((key) => key.toLowerCase() === name.toLowerCase());
  const seedPlan = plan(`$env:test_seed='2'; dotnet test ${proj}`, "pwsh", { TEST_SEED: "1" });
  assert.ok(seedPlan !== null);
  assert.deepStrictEqual(keysLike(seedPlan.env, "TEST_SEED"), ["TEST_SEED"]);
  assert.strictEqual(seedPlan.env.TEST_SEED, "2");
  const noCudaPlan = plan(`$env:gsharp_test_no_cuda='1'; dotnet test ${proj}`, "pwsh");
  assert.ok(noCudaPlan !== null);
  assert.deepStrictEqual(keysLike(noCudaPlan.env, "GSHARP_TEST_NO_CUDA"), ["GSHARP_TEST_NO_CUDA"]);
  assert.strictEqual(noCudaPlan.env.GSHARP_TEST_NO_CUDA, "1");
}
/**
 * Checks for `<GSharpRemoteOffload>local</GSharpRemoteOffload>` parsing, VSTest filter parenthesization,
 * `--local` list-job args, `test-sanitized` Option A gating, and fail-closed `--list-tests` `&Platform=Windows` probing.
 */
export async function checkHenicosOffloadProbe(assert: AssertModule, repoRoot: string): Promise<void> {
  // 1. remoteOffloadOf strips XML comments, ignores whitespace/case, returns null when absent
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-remote-offload-"));
  try {
    const localCsproj = path.join(tempDir, "Local.csproj");
    fs.writeFileSync(
      localCsproj,
      "<Project><PropertyGroup><GSharpTargetDialect>Cpu</GSharpTargetDialect><GSharpRemoteOffload> LOCAL </GSharpRemoteOffload></PropertyGroup></Project>",
    );
    assert.strictEqual(remoteOffloadOf(localCsproj), "local");

    const commentedCsproj = path.join(tempDir, "Commented.csproj");
    fs.writeFileSync(
      commentedCsproj,
      "<Project><PropertyGroup><!-- <GSharpRemoteOffload>local</GSharpRemoteOffload> --></PropertyGroup></Project>",
    );
    assert.strictEqual(remoteOffloadOf(commentedCsproj), null, "XML comments must be ignored");
    assert.strictEqual(remoteOffloadOf(path.join(tempDir, "Missing.csproj")), null);
  } finally {
    removeDirectory(tempDir);
  }

  // Real repository marked-local .csproj check
  assert.strictEqual(
    remoteOffloadOf(path.join(repoRoot, "tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj")),
    "local",
  );
  assert.strictEqual(
    remoteOffloadOf(
      path.join(repoRoot, "tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests/Kassyi.GSharp.Stdlib.GpuMath.Tests.csproj"),
    ),
    "local",
  );
  assert.strictEqual(
    remoteOffloadOf(
      path.join(repoRoot, "tests/Compiler/Kassyi.GSharp.Tests.Integration/Kassyi.GSharp.Tests.Integration.csproj"),
    ),
    "local",
  );
  assert.strictEqual(
    remoteOffloadOf(
      path.join(
        repoRoot,
        "tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests/Kassyi.GSharp.Compiler.Generators.Tests.csproj",
      ),
    ),
    null,
  );

  // 2. combineFilterWithWindowsPlatform + buildOffloadListJobArgs
  assert.strictEqual(combineFilterWithWindowsPlatform([], false), "Platform=Windows");
  assert.strictEqual(combineFilterWithWindowsPlatform(["A|B"], false), "(A|B)&Platform=Windows");
  assert.strictEqual(
    combineFilterWithWindowsPlatform(["Platform!=Windows"], false),
    "(Platform!=Windows)&Platform=Windows",
  );
  assert.strictEqual(combineFilterWithWindowsPlatform(["A", "B"], false), null);
  assert.strictEqual(combineFilterWithWindowsPlatform([" "], false), null);
  assert.strictEqual(combineFilterWithWindowsPlatform(["A"], true), null);

  const listJobArgs = buildOffloadListJobArgs(["dotnet", "test", "X.csproj", "--no-build", "--list-tests"], "pwsh");
  assert.deepStrictEqual(listJobArgs, [
    EntryScript,
    "run",
    "--local",
    "--shell",
    "pwsh",
    "--",
    "dotnet",
    "test",
    "X.csproj",
    "--no-build",
    "--list-tests",
  ]);

  // 3. offloadListVerdict fail-closed cases
  const noMatch: ListResult = {
    exitCode: 0,
    output:
      "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\r\n" +
      "No test matches the given testcase filter `Platform=Windows` in X.dll\r\n",
  };
  assert.deepStrictEqual(offloadListVerdict(noMatch, repoRoot), { offload: "allow" });
  assert.strictEqual(
    offloadListVerdict({ exitCode: 0, output: `${noMatch.output}    MyTests.WindowsOnlyTest\n` }, repoRoot).offload,
    "local",
    "mixed multi-TFM output with listed Windows test must stay local",
  );
  assert.strictEqual(offloadListVerdict({ exitCode: 1, output: noMatch.output }, repoRoot).offload, "local");
  assert.strictEqual(offloadListVerdict({ exitCode: null, output: noMatch.output }, repoRoot).offload, "local");
  assert.strictEqual(
    offloadListVerdict(
      { exitCode: 0, output: "Test run for X.dll (.NETCoreApp,Version=v10.0)\nNo test is available in X.dll\n" },
      repoRoot,
    ).offload,
    "local",
  );
  assert.strictEqual(
    offloadListVerdict({ exitCode: 0, output: "No test matches the given testcase filter `x`\n" }, repoRoot).offload,
    "local",
    "missing Test run for line must fail closed",
  );

  // 4. checkHenicosOffload with fake OffloadListRunner
  const ctx: ClientContext = {
    cwd: repoRoot,
    worktree: repoRoot,
    configRoot: repoRoot,
    shell: "pwsh",
    env: {},
    options: {},
    log: () => {},
  };
  const calls: string[][] = [];
  let nextResult: ListResult | Error = noMatch;
  const fakeRunner: OffloadListRunner = (args) => {
    calls.push([...args]);
    return nextResult instanceof Error ? Promise.reject(nextResult) : Promise.resolve(nextResult);
  };

  // Option A: just test-sanitized
  assert.strictEqual(
    (await checkHenicosOffload("just test-sanitized", { remoteForced: false, places: {} }, ctx, fakeRunner)).offload,
    "local",
  );
  assert.deepStrictEqual(
    await checkHenicosOffload("just test-sanitized", { remoteForced: true, places: {} }, ctx, fakeRunner),
    { offload: "allow" },
  );
  assert.strictEqual(
    (
      await checkHenicosOffload(
        "just test-sanitized --filter FullyQualifiedName~Msvc",
        { remoteForced: true, places: {} },
        ctx,
        fakeRunner,
      )
    ).offload,
    "local",
  );
  assert.strictEqual(calls.length, 0, "just test-sanitized must not run dotnet --list-tests");

  // Eligible non-local dotnet test with A|B filter forwards -c Release, omits -v n, parenthesizes (A|B)&Platform=Windows
  const genProj = "tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests";
  const allowRes = await checkHenicosOffload(
    `dotnet test ${genProj} -c Release -v n --filter "Name~A|Name~B"`,
    { remoteForced: false, places: {} },
    ctx,
    fakeRunner,
  );
  assert.deepStrictEqual(allowRes, { offload: "allow" });
  assert.strictEqual(calls.length, 1);
  assert.ok(calls[0]?.includes("-c") && calls[0]?.includes("Release"));
  assert.ok(!calls[0]?.includes("-v"), "verbosity must not be forwarded to --list-tests");
  assert.strictEqual(calls[0]?.at(-1), "(Name~A|Name~B)&Platform=Windows");

  // Matching Windows test routes locally with 1-line reason
  nextResult = {
    exitCode: 0,
    output:
      "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\n" +
      "The following Tests are available:\n    N.WinTests.OnlyOnWindows\n",
  };
  const winMatch = await checkHenicosOffload(
    `dotnet test ${genProj}`,
    { remoteForced: false, places: {} },
    ctx,
    fakeRunner,
  );
  assert.strictEqual(winMatch.offload, "local");
  assert.ok(winMatch.reason?.includes("Platform=Windows"));

  // Runner rejection fails closed
  nextResult = new Error("spawn failed");
  const errRes = await checkHenicosOffload(
    `dotnet test ${genProj}`,
    { remoteForced: false, places: {} },
    ctx,
    fakeRunner,
  );
  assert.strictEqual(errRes.offload, "local");
  assert.ok(errRes.reason?.includes("spawn failed"));

  // Fail-closed filters never invoke the runner
  const beforeUnreadable = calls.length;
  for (const badCmd of [
    `dotnet test ${genProj} --filter:Name~A`,
    `dotnet test ${genProj} --filter ""`,
    `dotnet test ${genProj} --filter Name~A --filter Name~B`,
    `dotnet test ${genProj} -p:VSTestTestCaseFilter=Name~A`,
    `dotnet test ${genProj} -- RunConfiguration.TestCaseFilter=Name~A`,
  ]) {
    const res = await checkHenicosOffload(badCmd, { remoteForced: false, places: {} }, ctx, fakeRunner);
    assert.strictEqual(res.offload, "local", `unreadable filter must stay local: ${badCmd}`);
  }
  assert.strictEqual(
    calls.length,
    beforeUnreadable,
    "unreadable filters must fail closed without spawning --list-tests",
  );

  // Compound command: if second step matches Platform=Windows, whole command stays local
  let stepIdx = 0;
  const compoundRunner: OffloadListRunner = () => {
    stepIdx++;
    return Promise.resolve(
      stepIdx === 1
        ? noMatch
        : {
            exitCode: 0,
            output: "Test run for B.dll (.NETCoreApp,Version=v10.0)\n    B.WinTest\n",
          },
    );
  };
  const compoundRes = await checkHenicosOffload(
    `dotnet test ${genProj} && dotnet test tests/ZeroAlloc/Kassyi.ZeroAlloc.Linq.Tests`,
    { remoteForced: false, places: {} },
    ctx,
    compoundRunner,
  );
  assert.strictEqual(compoundRes.offload, "local");
  assert.strictEqual(stepIdx, 2);

  // Compound build+test: local/CUDA/solution build fails closed without invoking --list-tests; pure managed build allows
  const collectProj = "tests/Tools/Kassyi.GSharp.Tools.CollectEnv.Tests";
  const infraProj = "tests/Shared/Kassyi.GSharp.Tests.Infrastructure";
  let buildProbeCalls = 0;
  const buildProbeRunner: OffloadListRunner = () => {
    buildProbeCalls++;
    return Promise.resolve(noMatch);
  };
  for (const blockedBuildCmd of [
    `dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cpu && dotnet test ${collectProj}`,
    `dotnet build tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests && dotnet test ${collectProj}`,
    `dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cuda && dotnet test ${collectProj}`,
    `dotnet build Henicos.slnx && dotnet test ${collectProj}`,
  ]) {
    const res = await checkHenicosOffload(blockedBuildCmd, { remoteForced: false, places: {} }, ctx, buildProbeRunner);
    assert.strictEqual(res.offload, "local", `compound blocked build must stay local: ${blockedBuildCmd}`);
    assert.ok(res.reason !== undefined && res.reason !== "" && !/[\r\n]/.test(res.reason));
  }
  assert.strictEqual(buildProbeCalls, 0, "blocked compound build must fail closed without spawning --list-tests");
  const allowedCompoundBuild = await checkHenicosOffload(
    `dotnet build ${infraProj} && dotnet test ${collectProj}`,
    { remoteForced: false, places: {} },
    ctx,
    buildProbeRunner,
  );
  assert.deepStrictEqual(allowedCompoundBuild, { offload: "allow" });
  assert.strictEqual(buildProbeCalls, 1);
}
