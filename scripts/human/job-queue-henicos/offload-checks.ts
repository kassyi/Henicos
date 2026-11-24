// Offload, QueuedRun wire memoryMb, repo baseline config, and Data-vs-Command guard checks for job-queue-henicos.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { QueuedRun } from "../job-queue/client-queued-run.ts";
import type { RunOptions } from "../job-queue/client.ts";
import { loadConfig } from "../job-queue/config.ts";
import { EntryScript } from "../job-queue/constants.ts";
import { PluginApiVersion, type AssertModule, type QueuePlugin } from "../job-queue/plugin-api.ts";
import { withConfigEnv } from "../job-queue/plugin-checks.ts";
import { PluginHost, type BaseClientContext } from "../job-queue/plugin-host.ts";
import { translateForRemote } from "../job-queue/remote/command-translator.ts";
import { resolveOffloadPreference } from "../job-queue/remote/executor.ts";
import { buildRemoteFindPruneExpr, isExcludedSyncPath } from "../job-queue/remote/syncer.ts";
import { FakeHost, FakeScheduler, until } from "../job-queue/self-test-client.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";

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
  const cases: readonly [string, boolean][] = [
    [`dotnet test ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj --filter 'FullyQualifiedName~WarpStackModelTests'`, true],
    [`$env:DiffEngine_Disabled='true'; dotnet test ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj`, true],
    [`dotnet build ${cpuTests} && dotnet test --no-build ${cpuTests}`, true],
    [`dotnet build ${cpuTests} -c Release && dotnet test --no-build ${cpuTests} -c Release`, true],
    [`dotnet build ${cpuTests} && dotnet test --no-build ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj`, true],
    [`dotnet test ${genTests}/Kassyi.GSharp.Compiler.Generators.Tests.csproj`, true],
    [`dotnet test ${gpuMathTests} --filter 'Category!=GpuExecution'`, true],
    [`dotnet test ${benchTests} --filter 'Category!=GpuExecution'`, true],
    [`dotnet test ${benchTests} --filter 'Category!=GpuExecution&Category!=Stress'`, true],
    ["just test-differential-cpu", true],
    ["just test-sanitized", true],
    ["just -f justfile test-sanitized", true],
    ["just -d . test-differential-cpu", true],
    ["just --set blame_crash '' test-sanitized", true],
    // Ineligible: bare build, standalone or mismatched --no-build, CUDA, unfiltered GpuMath/Benchmarks, `|` filter
    [`dotnet build ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj`, false],
    [`dotnet test --no-build ${cpuTests}/Kassyi.GSharp.Tests.Cpu.csproj`, false],
    [`dotnet build ${cpuTests} -c Release && dotnet test --no-build ${cpuTests}`, false],
    [`dotnet build ${cpuTests} && dotnet test --no-build ${cpuTests} -c Release`, false],
    [`dotnet build ${genTests} && dotnet test --no-build ${cpuTests}`, false],
    [`dotnet build ${cpuTests} -o tmp/out && dotnet test --no-build ${cpuTests}`, false],
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

  const plan = translateForRemote("just test-differential-cpu", {
    cwd: repoRoot,
    worktree: repoRoot,
    shell: "pwsh",
    places: {},
    cpus: 4,
    keepBuildServersWarm: true,
    env: {},
    remoteRules: rules,
  });
  assert.ok(plan !== null, "Henicos + just + dotnet plugins translate just test-differential-cpu");
  assert.strictEqual(plan.env.GSHARP_TEST_NO_CUDA, "1");
  assert.strictEqual(plan.env.DiffEngine_Disabled, "true");
  assert.strictEqual(plan.env.MSBUILDCORELIMIT, "4");

  // Regression: OffloadableJustRecipes (including with value-taking just flags) resolve to 2880 MB <= 6144 MB.
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
  for (const cmd of [
    "just test-differential-cpu",
    "just test-native-stack",
    "just test-managed-stack",
    "just test-sanitized",
    "just -f justfile test-sanitized",
    "just --set blame_crash '' test-sanitized",
  ]) {
    const cls = host.classify(cmd, null, ctx);
    assert.strictEqual(cls.offloadable, true, `${cmd} must be offloadable`);
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
