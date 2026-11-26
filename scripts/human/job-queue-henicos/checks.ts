// Self-tests of the Henicos repository plugin: pre-move classification/budget characterization (46 commands),
// moved budget/CPU-only project checks, worktree vs configRoot budget resolution, --gpu refuse/hint checks,
// QueuedRun wire memoryMb (request, adopt, gpuTurn omission, JOB_QUEUE_PLUGINS=0, defaultTestMemoryMb),
// and shortCommand prefix removal.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { createRequire } from "node:module";
import { loadConfig } from "../job-queue/config.ts";
import { EntryScript } from "../job-queue/constants.ts";
import type { TargetDialectModule } from "../job-queue/lib/types.ts";
import { type AssertModule } from "../job-queue/plugin-api.ts";
import { PluginHost, type BaseClientContext } from "../job-queue/plugin-host.ts";
import { quietly } from "../job-queue/self-test-client.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import { shortCommand } from "../job-queue/view.ts";
import { selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { cpuOnlyTestProjects, parseBudgets, testMemoryMb } from "./budgets.ts";
import { checkNameFilter } from "./name-filter-checks.ts";
import {
  checkHenicosEnvAllowlist,
  checkHenicosOffloadProbe,
  checkOffloadClassification,
  checkQueuedRunMemoryMb,
  checkRepoBaselineConfig,
} from "./offload-checks.ts";
import henicosPlugin, { isHenicosTest } from "./plugin.ts";

const require = createRequire(import.meta.url);
const { dialectOf } = require("./target-dialect.js") as TargetDialectModule;
const justfileReader = require("../job-queue/plugins/just/justfile.js") as {
  justVariables(justfile: string): Map<string, string>;
  recipeBodies(justfile: string, recipe: string): string[];
  recipeParameters(justfile: string, recipe: string): string[];
};

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
  // The test list of the name-filter share names Stress and runs no test: the normal budget
  [
    'dotnet test "tests/Compiler/Kassyi.GSharp.Tests.Cuda" --no-build --list-tests --filter "(Name~A)&(Category=Stress|Category=Benchmark)"',
    true,
    2496,
  ],
  [
    'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --no-build --filter "Category=Stress" --list-tests',
    true,
    2496,
  ],
  // Only when no step runs a test: one that does is a stress run
  [
    'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --list-tests --filter "Category=Stress" && dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category=Stress"',
    true,
    6784,
  ],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category=Stress" ; echo --list-tests', true, 6784],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Category!=Stress|Category=Benchmark"', true, 6784],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category != Stress&Category!=Benchmark"', true, 2880],
  ["dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter \"Category!='Stress'\"", true, 6720],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category!=Foo-Stress"', true, 6720],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "FullyQualifiedName~Stressful"', true, 2880],
  ['dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "Category=Benchmark.Long"', true, 6720],
  ["just test", true, 7232],
  ["just -f justfile test-category Runtime", true, 7232],
  ['just test-category Stress "--project" kassyi.gsharp.tests.cpu', true, 6720],
  ["just test-differential-cuda", true, 2496],
  ["just test-checked", true, 2496],
  ["just test-gpu-trap", true, 2496],
  ["just test-differential-cpu", true, 2880],
  ["just test-sanitized", true, 2880],
  ["just a=1 test-coverage", true, 7232],
  ["just coverage-diff", true, 7232],
  ["just mutate", true, 1536],
  ["just mutate compiler", true, 2880],
  ["just mutate compiler-cuda", true, 2496],
  ["just long-checks", false, 0],
  ["just build", false, 0],
  ["just doctest", false, 0],
  ["just queue-watch", false, 0],
  ["node scripts/human/test-stability.js --count 5", true, 7232],
  ["node scripts/human/test-memory.js Kassyi.GSharp.Tests.Cpu", true, 2880],
  ["node scripts/human/test-sanitized.js", true, 2880],
  ["node scripts/human/long-checks.js --dry-run", false, 0],
  ["node scripts/human/mutate.js --phase compiler", true, 2880],
  ["node scripts/human/mutate.js --phase=compiler", true, 2880],
  ["node scripts/human/mutate.js -- --phase compiler-cuda", true, 1536],
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
 * Check 3 (mutant-verified): --gpu + fuzz-diff -> refuse; long-checks -> refuse (with or without --gpu);
 * --gpu + CPU-only project -> 1 hint, no refuse; non-GPU plain test -> no refuse and no hints.
 */
function checkGpuCheckHook(assert: AssertModule, host: PluginHost): void {
  const expectedLongChecksRefusal =
    "long-checks queues each step through job-queue.js itself: " +
    "run 'just long-checks' directly without wrapping it in job-queue.js run.";
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
    assert.strictEqual(fuzz.refuse, "fuzz-diff takes the GPU lock itself: queue it without --gpu.");
    const longChecksGpu = host.check("just long-checks", gpu, ctx);
    assert.strictEqual(longChecksGpu.refuse, expectedLongChecksRefusal);

    const cpuHint = host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", gpu, ctx);
    assert.strictEqual(cpuHint.refuse, undefined, "CPU-only test project must warn via hint, never refuse");
    assert.strictEqual(cpuHint.hints?.length, 1);
    assert.ok(cpuHint.hints?.[0]?.includes("Kassyi.GSharp.Tests.Cpu"));
    assert.ok(cpuHint.hints?.[0]?.includes("GSharpTargetDialect Cpu"));

    const cudaCheck = host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda", gpu, ctx);
    assert.strictEqual(cudaCheck.refuse, undefined);
    assert.strictEqual(cudaCheck.hints, undefined);

    // Without --gpu (resource = null): long-checks still refuses double-queueing; plain CPU tests pass cleanly
    const longChecksCpu = host.check("just long-checks", null, ctx);
    assert.strictEqual(longChecksCpu.refuse, expectedLongChecksRefusal);
    assert.deepStrictEqual(host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", null, ctx), {});
  } finally {
    removeDirectory(dir);
  }
}

const Gpu = { name: "gpu", forcedExclusive: false };

/** The filter of `just test`: it keeps every exclusive category out, so a test run with it may share. */
const Kept = "Category!=Stress&Category!=Benchmark&Category!=GpuTrap";

/**
 * The share of a --gpu command under the repository configuration: [command, expected]. Shared: a dotnet test whose
 * every filter negates each exclusive category (`Category!=` in any case and order, among other `&` terms, no `|`),
 * and `just test` / `just test-coverage` / `just test-differential-cuda`, whose dotnet test filters in the justfile
 * negate each of them (checkSharedRecipeFilters).
 * Exclusive: anything unreadable, piped, unfiltered, not a test, with a variable the shell expands, that names tests
 * alone or does not negate every category, or that names or sets the trap variable in any case.
 */
const ShareTable: readonly [string, "shared" | "exclusive"][] = [
  ["just test", "shared"],
  ["just test-coverage", "shared"],
  ["just test-differential-cuda", "shared"],
  ['just test-differential-cuda --filter "Category=Differential"', "exclusive"],
  ["just test-gpu-trap", "exclusive"],
  ["just test-stability", "exclusive"],
  ["just -f justfile test", "exclusive"],
  ['just test --filter "FullyQualifiedName~Foo"', "exclusive"],
  [`just test --filter "${Kept}&Name~Foo"`, "shared"],
  [`dotnet test X --filter "${Kept}"`, "shared"],
  [
    'dotnet test X --filter "category != gputrap & Category!=STRESS&FullyQualifiedName~Foo&category!=benchmark"',
    "shared",
  ],
  [`dotnet test X --filter "${Kept}&Category=Stress"`, "exclusive"],
  [`dotnet test X --filter "${Kept}&category=stress"`, "exclusive"],
  [`dotnet test X --filter "${Kept}" --filter:"Name~Foo"`, "exclusive"],
  ['dotnet test X --filter "Category = GpuTrap"', "exclusive"],
  ['dotnet test X --filter "Category=GpuTrap"', "exclusive"],
  ['dotnet test X --filter "Category=Stress"', "exclusive"],
  ['dotnet test X --filter "Category=Benchmark"', "exclusive"],
  [`dotnet test X --filter "${Kept}" -e GSHARP_CUDA_TRAP_TESTS=1`, "exclusive"],
  [`GSHARP_CUDA_TRAP_TESTS=1 dotnet test X --filter "${Kept}"`, "exclusive"],
  ["dotnet run --project X", "exclusive"],
  [`dotnet test $(cat projects.txt) --filter "${Kept}"`, "exclusive"],
  [`dotnet test X --filter "${Kept}`, "exclusive"],
  [`dotnet test X --no-build --filter "${Kept}"`, "shared"],
  [`dotnet build X && dotnet test X --no-build --filter "${Kept}"`, "exclusive"],
  [`cd tests && dotnet test X --filter "${Kept}"; echo done`, "shared"],
  [`Set-Location tests; dotnet test X --filter "${Kept}"; Pop-Location`, "shared"],
  [`$env:DiffEngine_Disabled='true'; dotnet test X --filter "${Kept}"`, "shared"],
  [`dotnet.exe test X --filter "${Kept}"`, "shared"],
  [`dotnet test bin/Tests.dll --filter "${Kept}"`, "exclusive"],
  [`dotnet test X --filter="${Kept}"`, "shared"],
  [`dotnet test X --filter:"${Kept}"`, "exclusive"],
  ['dotnet test X --filter ""', "exclusive"],
  ['dotnet test X --filter " "', "exclusive"],
  ["echo hello", "exclusive"],
  [`node scripts/human/coverage-diff.js --run && dotnet test X --filter "${Kept}"`, "exclusive"],
  // Q2: without a filter a test run also runs Stress and Benchmark
  ["dotnet test X", "exclusive"],
  ["dotnet test X --no-build", "exclusive"],
  // A filter that names tests alone, or negates only some categories, may still pick Stress or Benchmark tests
  ['DiffEngine_Disabled=true dotnet test X --filter "FullyQualifiedName~Foo"', "exclusive"],
  ['dotnet test X --filter "FullyQualifiedName~PoolStressTests"', "exclusive"],
  ['dotnet test X --filter "Name~PoolStressTests"', "exclusive"],
  ['dotnet test X --filter "Category!=Slow"', "exclusive"],
  ['dotnet test X --filter "Category!=Stress"', "exclusive"],
  ['dotnet test X --filter "Category!=Stress&Category!=Benchmark"', "exclusive"],
  ['dotnet test X --filter "Category~Stres"', "exclusive"],
  ['dotnet test X --filter "category=stress"', "exclusive"],
  [`dotnet test X --filter "${Kept}" --filter "Name~Foo"`, "exclusive"],
  // A pipe hides its later stages from readCommand; a | inside the quoted filter is no pipe, but an or term
  [`dotnet test X --filter "${Kept}" | tee log`, "exclusive"],
  [`dotnet test X --filter "${Kept}" || echo failed`, "exclusive"],
  [`dotnet test X --filter "${Kept}&(Name~A|Name~B)"`, "exclusive"],
  // A variable the shell expands: the words are not what runs
  ['dotnet test X --filter "$FILTER"', "exclusive"],
  ["dotnet test X --filter $FILTER", "exclusive"],
  ['export F=Category=Stres; dotnet test X --filter "${F}s"', "exclusive"],
  ["$f = Get-Content f.txt; dotnet test X --filter $f", "exclusive"],
  [`dotnet test X --filter "${Kept}" $EXTRA`, "exclusive"],
  [`dotnet test $P --filter "${Kept}"`, "exclusive"],
  [`dotnet test X --filter "${Kept}" -e $V=1`, "exclusive"],
  [`$DOTNET test X --filter "${Kept}"`, "exclusive"],
  [`$out = dotnet test X --filter "${Kept}"`, "exclusive"],
  [`$DOTNET test X; dotnet test X --filter "${Kept}"`, "exclusive"],
  [`$out = dotnet test Y; dotnet test X --filter "${Kept}"`, "exclusive"],
  [`dotnet test X --filter "${Kept}" -e "V=$V"`, "exclusive"],
  [`dotnet test X --filter "${Kept}" -e 'A=$B'`, "shared"],
  ['dotnet test X --filter "Name~Foo" -e $V=1', "exclusive"],
  ['dotnet test $P --filter "Name~Foo" $EXTRA', "exclusive"],
  // The trap variable in any case, also split by quotes
  [`gsharp_cuda_trap_tests=1 dotnet test X --filter "${Kept}"`, "exclusive"],
  [`dotnet test X --filter "${Kept}" -e gsharp_cuda_trap_tests=1`, "exclusive"],
  [`dotnet test X --filter "${Kept}" -e "GSHARP_CUDA_""TRAP_TESTS=1"`, "exclusive"],
  ['gsharp_cuda_trap_tests=1 dotnet test X --filter "Name~Foo"', "exclusive"],
  ['dotnet test X --filter "Name~Foo" -e gsharp_cuda_trap_tests=1', "exclusive"],
  ['dotnet test X --filter "Name~Foo" -e "GSHARP_CUDA_""TRAP_TESTS=1"', "exclusive"],
  [`dotnet test X --filter "${Kept}" -e GSHARP_CUDA_\\TRAP_TESTS=1`, "exclusive"],
  // A variable set under a name a variable makes
  [`export $V=1; dotnet test X --filter "${Kept}"`, "exclusive"],
  [`export \${P}TRAP_TESTS=1; dotnet test X --filter "${Kept}"`, "exclusive"],
  [`export A=1; dotnet test X --filter "${Kept}"`, "shared"],
  // A test case filter spelled another way: it may pick the tests the --filter keeps out
  [`dotnet test X --filter "${Kept}" -p:VSTestTestCaseFilter=FullyQualifiedName~PoolStressTests`, "exclusive"],
  [
    `dotnet test X --filter "${Kept}" -- RunConfiguration.TestCaseFilter=FullyQualifiedName~PoolStressTests`,
    "exclusive",
  ],
  ["just test -- RunConfiguration.TestCaseFilter=FullyQualifiedName~PoolStressTests", "exclusive"],
];

/** Checks 3 to 7 of the share rules: the table, the environment, the plugin options. */
function checkShareRules(assert: AssertModule, host: PluginHost): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-share-"));
  try {
    const wt = fs.realpathSync.native(dir);
    const ctx = contextFor(wt, wt);
    for (const [command, expected] of ShareTable) {
      assert.strictEqual(host.classify(command, Gpu, ctx).share, expected, `share of: ${command}`);
    }
    // A command that nameFilterCheck reads has no opinion (checkShare judges it by the test list); a veto stays one
    const named = 'dotnet test tests/X/X.csproj --no-build --filter "FullyQualifiedName~Foo"';
    assert.strictEqual(host.classify(named, Gpu, ctx).share, undefined, "a readable name filter: no opinion");
    assert.strictEqual(
      host.classify(named.replace("~Foo", "!~Foo"), Gpu, ctx).share,
      "exclusive",
      "a negated name term is no name filter: exclusive",
    );
    assert.strictEqual(host.classify(named, { ...Gpu, forcedExclusive: true }, ctx).share, "exclusive");
    const filtered = `dotnet test X --filter "${Kept}"`;
    assert.strictEqual(host.classify(filtered, null, ctx).share, undefined, "no share without a resource");
    assert.strictEqual(host.classify(filtered, { name: "cpu", forcedExclusive: false }, ctx).share, undefined);
    assert.strictEqual(host.classify(filtered, { name: "gpu", forcedExclusive: true }, ctx).share, "exclusive");

    const withEnv = (value: string, name = "GSHARP_CUDA_TRAP_TESTS") => ({ ...ctx, env: { [name]: value } });
    assert.strictEqual(host.classify(filtered, Gpu, withEnv("1")).share, "exclusive", "trap tests on in the env");
    const lowerEnv = withEnv("1", "gsharp_cuda_trap_tests");
    assert.strictEqual(host.classify(filtered, Gpu, lowerEnv).share, "exclusive", "Windows reads names in any case");
    const cmd = { ...ctx, shell: "cmd" as const };
    assert.strictEqual(host.classify(filtered, Gpu, cmd).share, "shared", "cmd without a variable");
    assert.strictEqual(host.classify(`${filtered} %EXTRA%`, Gpu, cmd).share, "exclusive", "cmd expands %EXTRA%");
    assert.strictEqual(host.classify(filtered, Gpu, withEnv("0")).share, "shared", "0 leaves them off");
    assert.strictEqual(host.classify(filtered, Gpu, withEnv("")).share, "shared", "empty leaves them off");

    const full = {
      exclusiveCategories: ["Stress", "Benchmark", "GpuTrap"],
      exclusiveEnv: ["GSHARP_CUDA_TRAP_TESTS"],
      sharedRecipes: ["test", "test-coverage"],
    };
    const shareWith = (options: Record<string, unknown> | undefined, command = filtered) =>
      new PluginHost([henicosPlugin], [options], wt, () => {}).classify(command, Gpu, ctx).share;
    assert.strictEqual(shareWith(full), "shared", "the full options share");
    assert.notStrictEqual(shareWith(undefined), "shared", "no options: exclusive");
    for (const key of Object.keys(full)) {
      const missing: Record<string, unknown> = { ...full };
      delete missing[key];
      assert.notStrictEqual(shareWith(missing), "shared", `without ${key}: exclusive`);
      assert.notStrictEqual(shareWith({ ...full, [key]: "Stress" }), "shared", `${key} not a list: exclusive`);
      assert.notStrictEqual(shareWith({ ...full, [key]: [1] }), "shared", `${key} not names: exclusive`);
    }
  } finally {
    removeDirectory(dir);
  }
}

/** Check 6 (mutant-verified): shortCommand with plugins matches pre-move display including Kassyi.GSharp. removal. */
function checkShortCommandWithPlugins(assert: AssertModule, host: PluginHost): void {
  for (const [command, expected] of ShortCharacterizationTable) {
    assert.strictEqual(shortCommand(command, host), expected, `shortCommand for: ${command}`);
  }
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

    const attrProj = path.join(tempDir, "attr.csproj");
    fs.writeFileSync(
      attrProj,
      '<Project><PropertyGroup><GSharpTargetDialect Condition="true">Cpu</GSharpTargetDialect></PropertyGroup></Project>',
    );
    assert.strictEqual(dialectOf(attrProj), "cpu", "dialect tag with attributes must return 'cpu'");

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

/**
 * Every recipe of sharedRecipes in the repository config is shared by name only, so its command in the justfile must
 * be one the share rules call shared: the `dotnet test` it queues under `--gpu`, as just expands it. A recipe parameter
 * is removed (just gives it precedence over a variable of the same name), a top-level variable is replaced by its
 * value, and any other interpolation fails the check, since a value the check cannot read could select any tests.
 */
function checkSharedRecipeFilters(assert: AssertModule, host: PluginHost, configRoot: string, recipes: unknown): void {
  assert.ok(Array.isArray(recipes) && recipes.length > 0, "the repository config names shared recipes");
  const justfile = fs.readFileSync(path.join(configRoot, "justfile"), "utf8");
  const variables = justfileReader.justVariables(justfile);
  const ctx = contextFor(configRoot, configRoot);
  for (const recipe of recipes as string[]) {
    const [body] = justfileReader.recipeBodies(justfile, recipe);
    assert.ok(body !== undefined, `the justfile has the shared recipe ${recipe}`);
    const queued = /--gpu -- ([\s\S]*)$/.exec(body ?? "");
    assert.ok(queued !== null, `${recipe} queues its command with --gpu`);
    const parameters = new Set(justfileReader.recipeParameters(justfile, recipe));
    const command = (queued?.[1] ?? "")
      .replace(/\{\{\s*(\w+)\s*\}\}/g, (all, name: string) =>
        parameters.has(name) ? "" : (variables.get(name) ?? all),
      )
      .replace(/\s+/g, " ")
      .trim();
    assert.ok(!command.includes("{{"), `${recipe}: the check reads every interpolation of the command: ${command}`);
    assert.strictEqual(
      host.classify(command, Gpu, ctx).share,
      "shared",
      `the justfile command of ${recipe}: ${command}`,
    );
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  await checkRepoBaselineConfig(assert);
  checkDialectOf(assert);
  const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
  assert.deepStrictEqual(
    loaded.config.resources?.gpu?.lock?.seats,
    ["tmp/gpu-test-lock", "tmp/gpu-test-lock-seat1"],
    "seats stay tmp/gpu-test-lock and tmp/gpu-test-lock-seat1 so older gpu-lock.js and checkouts agree on both",
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
  checkShareRules(assert, host);
  const henicosOptions = loaded.config.plugins?.find((p) => p.name === "./scripts/human/job-queue-henicos")?.options;
  checkSharedRecipeFilters(assert, host, loaded.configRoot, henicosOptions?.sharedRecipes);
  await quietly(() => checkQueuedRunMemoryMb(assert, host, FixedBudgetsYaml));
  checkShortCommandWithPlugins(assert, host);
  checkOffloadClassification(assert, host, loaded.configRoot);
  checkHenicosEnvAllowlist(assert, host, loaded.configRoot);
  await checkHenicosOffloadProbe(assert, loaded.configRoot);
  await checkNameFilter(assert);
}
