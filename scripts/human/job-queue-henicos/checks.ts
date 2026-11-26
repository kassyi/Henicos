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
import { type AssertModule, type ShareMode } from "../job-queue/plugin-api.ts";
import { PluginHost, type BaseClientContext } from "../job-queue/plugin-host.ts";
import { quietly } from "../job-queue/self-test-client.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import { shortCommand } from "../job-queue/view.ts";
import { selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { MutatePhaseDirectories, cpuOnlyTestProjects, parseBudgets, testMemoryMb } from "./budgets.ts";
import { checkNameFilter } from "./name-filter-checks.ts";
import { checkHenicosOffloadSources } from "./offload-source-checks.ts";
import {
  checkHenicosEnvAllowlist,
  checkHenicosOffloadProbe,
  checkOffloadClassification,
  checkQueuedRunMemoryMb,
  checkRepoBaselineConfig,
} from "./offload-checks.ts";
import henicosPlugin, { isHenicosTest } from "./plugin.ts";
import { shareDecision } from "./share-rules.ts";

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
  [`$env:DOTNET_CLI_UI_LANGUAGE='en'; dotnet test X --filter "${Kept}"`, "shared"],
  [`$env:DiffEngine_Disabled='true'; $env:DOTNET_CLI_UI_LANGUAGE='en'; dotnet test X --filter "${Kept}"`, "shared"],
  ['$env:DiffEngine_Disabled="true"; dotnet test X --filter "FullyQualifiedName~Foo"', "exclusive"],
  ["$env:ROWBATCH_ROOTS='1'; dotnet test X --no-build --filter \"Name~Foo\"", "exclusive"],
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
      const classified = host.classify(command, Gpu, ctx);
      assert.strictEqual(classified.share, expected, `share of: ${command}`);
      assert.ok(
        classified.shareReason !== undefined && /^[a-z0-9:_-]{1,64}$/.test(classified.shareReason),
        `shareReason token format for: ${command} (got ${String(classified.shareReason)})`,
      );
      if (expected === "exclusive") {
        assert.strictEqual(
          classified.shareReason.includes("shared"),
          false,
          `exclusive shareReason must never contain 'shared': ${command} -> ${classified.shareReason}`,
        );
      }
    }
    // Verify shareReason codes per branch of shareDecision and classify
    const repoOptions = {
      exclusiveCategories: ["Stress", "Benchmark", "GpuTrap"],
      exclusiveEnv: ["GSHARP_CUDA_TRAP_TESTS"],
      sharedRecipes: ["test", "test-coverage", "test-differential-cuda"],
    };
    const reasonCtx = { ...ctx, options: repoOptions };
    const reasonCases: readonly [string, ShareMode, string][] = [
      [`dotnet test X --filter "${Kept}"`, "shared", "categories"],
      ["just test", "shared", "shared-recipe"],
      ['just test --filter "FullyQualifiedName~Foo"', "exclusive", "filter-categories"],
      ["just test -- RunConfiguration.TestCaseFilter=Name~Foo", "exclusive", "filter-unreadable"],
      ["just test-stability", "exclusive", "exclusive-recipe"],
      ["dotnet test X --no-build", "exclusive", "no-filter"],
      [`dotnet test bin/Tests.dll --filter "${Kept}"`, "exclusive", "binary-test"],
      [`dotnet test X --filter "${Kept}" --filter:"Name~Foo"`, "exclusive", "filter-unreadable"],
      [`dotnet test X --filter:"${Kept}"`, "exclusive", "filter-unreadable"],
      ['dotnet test X --filter:v "Name~Foo"', "exclusive", "filter-unreadable"],
      ["dotnet test X -p:VSTestTestCaseFilter=Name~Foo", "exclusive", "filter-unreadable"],
      ["dotnet test X -- RunConfiguration.TestCaseFilter=Name~Foo", "exclusive", "filter-unreadable"],
      ['dotnet test X --filter "Category!=Stress"', "exclusive", "filter-categories"],
      [
        "$env:DiffEngine_Disabled='true'; dotnet test X --no-build --filter \"Name~Foo\"",
        "exclusive",
        "filter-categories",
      ],
      [
        "$env:DOTNET_CLI_UI_LANGUAGE='en'; dotnet test X --no-build --filter \"Name~Foo\"",
        "exclusive",
        "filter-categories",
      ],
      [
        "$env:DiffEngine_Disabled='true'; cd tests && dotnet test X.csproj --no-build --filter \"Name~Foo\"",
        "exclusive",
        "filter-categories",
      ],
      [
        "$env:DiffEngine_Disabled='true'; echo hi; dotnet test X.csproj --no-build --filter \"Name~Foo\"",
        "exclusive",
        "filter-categories",
      ],
      [
        '$env:DiffEngine_Disabled=\'true\'; "C:\\Program Files\\dotnet\\dotnet.exe" test X --no-build --filter "Name~Foo"',
        "exclusive",
        "filter-categories",
      ],
      [
        "$env:DiffEngine_Disabled='true'; dotnet test X.csproj --no-build --filter \"Category=Stress\"",
        "exclusive",
        "filter-categories",
      ],
      [
        "$env:DiffEngine_Disabled='true'; dotnet test X.csproj --no-build --filter \"Category!=Stress&FullyQualifiedName~A\"",
        "exclusive",
        "filter-categories",
      ],
      ['export X=1; dotnet test X.csproj --filter "Category!=Stress"', "exclusive", "filter-categories"],
      [
        "$env:DiffEngine_Disabled='true'; dotnet test X.csproj --no-build --filter \"Name~Foo\"; $env:DiffEngine_Disabled='false'",
        "exclusive",
        "filter-categories",
      ],
      ['export X=1; dotnet test X.csproj --no-build --filter "Name~Foo"', "exclusive", "env-prefix"],
      ['export A=1 B=2; dotnet test X.csproj --no-build --filter "Name~Foo"', "exclusive", "env-prefix"],
      ["$env:ROWBATCH_ROOTS='1'; dotnet test X.csproj --no-build --filter \"Name~Foo\"", "exclusive", "env-prefix"],
      ["$env:Configuration='Debug'; dotnet test X.csproj --no-build --filter \"Name~Foo\"", "exclusive", "env-prefix"],
      [
        "$env:DiffEngine_Disabled='true'; $env:ROWBATCH_ROOTS='1'; dotnet test X.csproj --no-build --filter \"Name~Foo\"",
        "exclusive",
        "env-prefix",
      ],
      [
        "$env:DiffEngine_Disabled='true'; ROWBATCH_ROOTS=1 dotnet test X.csproj --no-build --filter \"Name~Foo\"",
        "exclusive",
        "env-prefix",
      ],
      [
        'export DiffEngine_Disabled=true; dotnet test X.csproj --no-build --filter "Name~Foo"',
        "exclusive",
        "env-prefix",
      ],
      ['ROWBATCH_ROOTS=1 dotnet test X.csproj --no-build --filter "Name~Foo"', "exclusive", "env-prefix"],
      [`dotnet test X --filter "${Kept}&Category=Stress"`, "exclusive", "exclusive-category"],
      [`dotnet test X --filter "${Kept}" -e GSHARP_CUDA_TRAP_TESTS=1`, "exclusive", "exclusive-env"],
      [`dotnet test X --filter "${Kept}" | tee log`, "exclusive", "piped"],
      [`dotnet test X --filter "${Kept}`, "exclusive", "unreadable"],
      ["echo hello", "exclusive", "no-work"],
      ['dotnet test X --filter "$FILTER"', "exclusive", "hidden"],
      ["dotnet build X", "exclusive", "not-test-command"],
    ];
    for (const [command, expectedShare, expectedReason] of reasonCases) {
      const classified = host.classify(command, Gpu, ctx);
      assert.deepStrictEqual(
        [classified.share, classified.shareReason],
        [expectedShare, expectedReason],
        `classify share/shareReason for: ${command}`,
      );
      const decision = shareDecision(command, reasonCtx);
      assert.deepStrictEqual(
        [decision?.share, decision?.reason],
        [expectedShare, expectedReason],
        `shareDecision share/reason for: ${command}`,
      );
    }
    // A command that nameFilterCheck reads has no opinion (checkShare judges it by the test list); a veto stays one
    const named = 'dotnet test tests/X/X.csproj --no-build --filter "FullyQualifiedName~Foo"';
    const namedClassified = host.classify(named, Gpu, ctx);
    assert.deepStrictEqual([namedClassified.share, namedClassified.shareReason], [undefined, undefined]);
    for (const prefix of [
      "$env:DiffEngine_Disabled='true'; ",
      "$env:DOTNET_CLI_UI_LANGUAGE='en'; ",
      "$env:DiffEngine_Disabled='true'; $env:DOTNET_CLI_UI_LANGUAGE='en'; ",
      "DOTNET_CLI_UI_LANGUAGE=en ",
    ]) {
      const prefixed = host.classify(`${prefix}${named}`, Gpu, ctx);
      assert.deepStrictEqual(
        [prefixed.share, prefixed.shareReason],
        [undefined, undefined],
        `allowlisted env prefix passes to nameFilterCheck: ${prefix}`,
      );
    }
    for (const prefix of ["$env:ROWBATCH_ROOTS='1'; ", '$env:DiffEngine_Disabled="$FOO"; ']) {
      const prefixed = host.classify(`${prefix}${named}`, Gpu, ctx);
      assert.deepStrictEqual(
        [prefixed.share, prefixed.shareReason],
        ["exclusive", "env-prefix"],
        `disallowed env prefix stays exclusive(env-prefix): ${prefix}`,
      );
    }
    assert.deepStrictEqual(
      [
        host.classify(named.replace("~Foo", "!~Foo"), Gpu, ctx).share,
        host.classify(named.replace("~Foo", "!~Foo"), Gpu, ctx).shareReason,
      ],
      ["exclusive", "filter-categories"],
      "a negated name term is no name filter: exclusive with filter-categories",
    );
    assert.deepStrictEqual(
      [
        host.classify(named, { ...Gpu, forcedExclusive: true }, ctx).share,
        host.classify(named, { ...Gpu, forcedExclusive: true }, ctx).shareReason,
      ],
      ["exclusive", "forced-exclusive"],
    );
    const filtered = `dotnet test X --filter "${Kept}"`;
    assert.deepStrictEqual(
      [host.classify(filtered, null, ctx).share, host.classify(filtered, null, ctx).shareReason],
      [undefined, undefined],
      "no share or shareReason without a resource",
    );
    assert.strictEqual(host.classify(filtered, { name: "cpu", forcedExclusive: false }, ctx).share, undefined);
    assert.deepStrictEqual(
      [
        host.classify(filtered, { name: "gpu", forcedExclusive: true }, ctx).share,
        host.classify(filtered, { name: "gpu", forcedExclusive: true }, ctx).shareReason,
      ],
      ["exclusive", "forced-exclusive"],
    );

    const withEnv = (value: string, name = "GSHARP_CUDA_TRAP_TESTS") => ({ ...ctx, env: { [name]: value } });
    assert.strictEqual(host.classify(filtered, Gpu, withEnv("1")).share, "exclusive", "trap tests on in the env");
    const lowerEnv = withEnv("1", "gsharp_cuda_trap_tests");
    assert.strictEqual(host.classify(filtered, Gpu, lowerEnv).share, "exclusive", "Windows reads names in any case");
    const cmd = { ...ctx, shell: "cmd" as const };
    assert.strictEqual(host.classify(filtered, Gpu, cmd).share, "shared", "cmd without a variable");
    assert.strictEqual(host.classify(`${filtered} %EXTRA%`, Gpu, cmd).share, "exclusive", "cmd expands %EXTRA%");
    for (const cmdCommand of [
      "$env:DiffEngine_Disabled='a&hostname'; dotnet test tests/X --no-build --filter \"Name~Foo\"",
      "DiffEngine_Disabled='a&hostname' dotnet test tests/X --no-build --filter \"Name~Foo\"",
      "$env:DiffEngine_Disabled='true'; dotnet test tests/X --no-build --filter \"Name~Foo\"",
      `DiffEngine_Disabled='a&hostname' dotnet test X --filter "${Kept}"`,
    ]) {
      assert.strictEqual(host.classify(cmdCommand, Gpu, cmd).share, "exclusive", `cmd rejects: ${cmdCommand}`);
      assert.strictEqual(
        shareDecision(cmdCommand, { ...reasonCtx, shell: "cmd" })?.share,
        "exclusive",
        `shareDecision cmd rejects: ${cmdCommand}`,
      );
    }
    assert.strictEqual(
      host.classify(
        "$env:DiffEngine_Disabled='true'; dotnet test tests/X/X.csproj --no-build --filter \"Name~Foo\"",
        Gpu,
        cmd,
      ).shareReason,
      "env-prefix",
      "cmd rejects $env: prefix as env-prefix",
    );
    assert.strictEqual(
      shareDecision('DiffEngine_Disabled=true dotnet test tests/X/X.csproj --no-build --filter "Name~Foo"', {
        ...reasonCtx,
        shell: "cmd",
      })?.reason,
      "env-prefix",
      "cmd rejects bash env prefix as env-prefix",
    );
    // Challenger fixes (a) & (b): cmd quote desync ('x"' ", '(a)'), unquoted ^ escapes, and quoted CR/LF truncation
    for (const cmdHidden of [
      `dotnet test tests/X/X.csproj --no-build --filter "${Kept}" && echo 'x"' " & calc & "`,
      `dotnet test tests/X/X.csproj --no-build --filter "Name~Foo" && echo 'x"' " & calc & "`,
      `dotnet test tests/X/X.csproj --no-build --filter "${Kept}" && echo '(a)'`,
      `dotnet test tests/X/X.csproj --no-build --logger 'a\nb' --filter "${Kept}"`,
      `dotnet test tests/X/X.csproj --no-build --logger 'a\rb' --filter "${Kept}"`,
      `dotnet test tests/X/X.csproj --no-build --logger "a\nb" --filter "${Kept}"`,
      `dotnet test tests/X/X.csproj --no-build --logger "a\rb" --filter "${Kept}"`,
      'dotnet test tests/X/X.csproj --no-build --logger "a\nb" --filter "Name~Foo"',
      `dotnet test tests/X/X.csproj --no-build --filter "${Kept}" ^-p:VSTestTestCaseFilter=Category=Stress`,
      `dotnet test tests/X/X.csproj --no-build --filter "Name~Foo" ^-v q`,
      `dotnet test tests/X/X.csproj --no-build ^--filter "Name~Foo"`,
      `dotnet test tests/X/X.csproj --no-build --filter "Name~Foo" && echo ^" & calc ^"`,
    ]) {
      const classified = host.classify(cmdHidden, Gpu, cmd);
      assert.deepStrictEqual(
        [classified.share, classified.shareReason],
        ["exclusive", "hidden"],
        `cmd rejects unsafe quoted syntax as hidden: ${JSON.stringify(cmdHidden)}`,
      );
      const decision = shareDecision(cmdHidden, { ...reasonCtx, shell: "cmd" });
      assert.deepStrictEqual(
        [decision?.share, decision?.reason],
        ["exclusive", "hidden"],
        `shareDecision cmd rejects unsafe quoted syntax as hidden: ${JSON.stringify(cmdHidden)}`,
      );
    }
    // Challenger fix (c): backslash inside double-quoted $env: value is rejected in bash (\" desync) and pwsh
    for (const escapedBash of [
      `$env:DiffEngine_Disabled="a\\"; ${named}; echo " & calc # "`,
      `$env:DiffEngine_Disabled="a\\\\"; ${named}`,
    ]) {
      const classified = host.classify(escapedBash, Gpu, ctx);
      assert.deepStrictEqual(
        [classified.share, classified.shareReason],
        ["exclusive", "env-prefix"],
        `bash rejects backslash in double-quoted $env: ${escapedBash}`,
      );
    }
    const pwsh = { ...ctx, shell: "pwsh" as const };
    assert.deepStrictEqual(
      [
        host.classify(`$env:DiffEngine_Disabled='true'; ${named}`, Gpu, pwsh).share,
        host.classify(`$env:DiffEngine_Disabled='true'; ${named}`, Gpu, pwsh).shareReason,
      ],
      [undefined, undefined],
      "pwsh allowlisted $env: prefix passes to nameFilterCheck",
    );
    assert.deepStrictEqual(
      [
        host.classify(`$env:DiffEngine_Disabled="a\\b"; ${named}`, Gpu, pwsh).share,
        host.classify(`$env:DiffEngine_Disabled="a\\b"; ${named}`, Gpu, pwsh).shareReason,
      ],
      ["exclusive", "env-prefix"],
      "pwsh double-quoted backslash in $env: RHS stays exclusive(env-prefix)",
    );
    assert.deepStrictEqual(
      [
        host.classify(`$env:DiffEngine_Disabled='a\\b'; ${named}`, Gpu, pwsh).share,
        host.classify(`$env:DiffEngine_Disabled='a\\b'; ${named}`, Gpu, pwsh).shareReason,
      ],
      [undefined, undefined],
      "pwsh single-quoted backslash in $env: RHS passes to nameFilterCheck",
    );
    assert.deepStrictEqual(
      [
        host.classify(`$env:DiffEngine_Disabled=hostname; ${named}`, Gpu, pwsh).share,
        host.classify(`$env:DiffEngine_Disabled=hostname; ${named}`, Gpu, pwsh).shareReason,
      ],
      ["exclusive", "env-prefix"],
      "pwsh bareword command in $env: RHS stays exclusive(env-prefix)",
    );
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

/** Fixture checks for `just mutate` / `node scripts/human/mutate.js` GPU share classification. */
function checkMutateShareRules(assert: AssertModule, host: PluginHost): void {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-mutate-"));
  try {
    const base = fs.realpathSync.native(dir);
    const wt = path.join(base, "wt");
    const wtEmpty = path.join(base, "wt-empty");
    const cfg = path.join(base, "cfg");
    fs.mkdirSync(path.join(wt, "tests"), { recursive: true });
    fs.mkdirSync(wtEmpty, { recursive: true });
    fs.mkdirSync(cfg, { recursive: true });

    const writeConfig = (rootDir: string, phase: string, content: string): void => {
      const rel = MutatePhaseDirectories[phase];
      assert.ok(rel !== undefined, `unknown fixture phase: ${phase}`);
      const target = path.join(rootDir, rel);
      fs.mkdirSync(target, { recursive: true });
      fs.writeFileSync(path.join(target, "stryker-config.json"), content, "utf8");
    };

    const sharedCfg = JSON.stringify({
      "stryker-config": {
        "test-case-filter": `FullyQualifiedName~Stdlib&${Kept}`,
      },
    });
    writeConfig(wt, "gpumath", sharedCfg);
    writeConfig(cfg, "gpumath", sharedCfg);

    const repoOptions = {
      exclusiveCategories: ["Stress", "Benchmark", "GpuTrap"],
      exclusiveEnv: ["GSHARP_CUDA_TRAP_TESTS"],
      sharedRecipes: ["test", "test-coverage", "test-differential-cuda"],
    };
    const ctx = contextFor(wt, cfg);
    const assertDecision = (
      command: string,
      expectedShare: ShareMode,
      expectedReason: string,
      overrideCtx: BaseClientContext = ctx,
    ): void => {
      const classified = host.classify(command, Gpu, overrideCtx);
      assert.deepStrictEqual(
        [classified.share, classified.shareReason],
        [expectedShare, expectedReason],
        `classify mutate share for: ${command}`,
      );
      const decision = shareDecision(command, { ...overrideCtx, options: repoOptions });
      assert.deepStrictEqual(
        [decision?.share, decision?.reason],
        [expectedShare, expectedReason],
        `shareDecision mutate share for: ${command}`,
      );
    };

    const sharedCommands = [
      "just mutate",
      "just mutate gpumath",
      "just mutate gpumath main",
      "just mutate gpumath HEAD^",
      'just mutate gpumath "" -m "**/Layout/**/*.cs"',
      'just mutate gpumath "" -c 4',
      "node scripts/human/mutate.js",
      "node scripts/human/mutate.js --phase gpumath",
      "node scripts/human/mutate.js --phase=gpumath --since=main",
      "node scripts/human/mutate.js --since main --phase gpumath --dry-run",
      'node scripts/human/mutate.js --phase gpumath -- -c 4 -m "**/Foo.cs"',
      "node scripts/human/mutate.js --phase gpumath -- --dry-run",
      "node ./scripts/human/mutate.js --phase gpumath",
      "$env:DiffEngine_Disabled='true'; just mutate gpumath",
      "just mutate gpumath && echo done",
      "cd tests && node ../scripts/human/mutate.js --phase gpumath",
      "Push-Location tests; Pop-Location; node scripts/human/mutate.js --phase gpumath",
      "Set-Location tests; just mutate gpumath; Pop-Location",
      "Set-Location -Path tests; node ../scripts/human/mutate.js --phase gpumath",
    ];
    if (process.platform === "win32") {
      sharedCommands.push("node scripts\\human\\mutate.js --phase gpumath");
    }
    for (const command of sharedCommands) {
      assertDecision(command, "shared", "categories");
    }
    const subCwdCtx = { ...ctx, cwd: path.join(wt, "tests") };
    assertDecision("just mutate gpumath", "shared", "categories", subCwdCtx);
    assertDecision("node ../scripts/human/mutate.js --phase gpumath", "shared", "categories", subCwdCtx);

    writeConfig(
      wt,
      "gpumath",
      `\uFEFF${JSON.stringify({
        "stryker-config": {
          "test-case-filter": "category != gputrap & Category!=STRESS & category!=benchmark",
        },
      })}`,
    );
    assertDecision("just mutate gpumath", "shared", "categories");

    // Category filter vetoes and stryker-config parsing failures
    const filterFailureCases: readonly [string, ShareMode, string][] = [
      [
        JSON.stringify({
          "stryker-config": { "test-case-filter": "FullyQualifiedName~Stdlib&Category!=Stress&Category!=Benchmark" },
        }),
        "exclusive",
        "filter-categories",
      ],
      [
        JSON.stringify({
          "stryker-config": { "test-case-filter": "FullyQualifiedName~Stdlib&Category!=Stress&Category!=GpuTrap" },
        }),
        "exclusive",
        "filter-categories",
      ],
      [
        JSON.stringify({
          "stryker-config": { "test-case-filter": "FullyQualifiedName~Stdlib&Category!=Benchmark&Category!=GpuTrap" },
        }),
        "exclusive",
        "filter-categories",
      ],
      [
        JSON.stringify({ "stryker-config": { "test-case-filter": `(${Kept})|Name~Foo` } }),
        "exclusive",
        "filter-categories",
      ],
      [
        JSON.stringify({ "stryker-config": { "test-case-filter": "FullyQualifiedName~Stdlib" } }),
        "exclusive",
        "filter-categories",
      ],
      [
        JSON.stringify({ "stryker-config": { "test-case-filter": `${Kept}&Category=Stress` } }),
        "exclusive",
        "exclusive-category",
      ],
      ["{broken", "exclusive", "filter-unreadable"],
      ["[]", "exclusive", "filter-unreadable"],
      ["null", "exclusive", "filter-unreadable"],
      ["{}", "exclusive", "filter-unreadable"],
      ['{"stryker-config":[]}', "exclusive", "filter-unreadable"],
      ['{"stryker-config":{"test-case-filter":123}}', "exclusive", "filter-unreadable"],
      ['{"stryker-config":{}}', "exclusive", "no-filter"],
      ['{"stryker-config":{"test-case-filter":"  "}}', "exclusive", "no-filter"],
    ];
    for (const [content, expectedShare, expectedReason] of filterFailureCases) {
      writeConfig(wt, "gpumath", content);
      assertDecision("just mutate gpumath", expectedShare, expectedReason);
      assertDecision("node scripts/human/mutate.js --phase gpumath", expectedShare, expectedReason);
    }

    // Env prefix on a category-incomplete mutate config must report filter-categories, not env-prefix
    writeConfig(
      wt,
      "gpumath",
      JSON.stringify({ "stryker-config": { "test-case-filter": "Category!=Stress&Category!=Benchmark" } }),
    );
    assertDecision("export X=1; just mutate gpumath", "exclusive", "filter-categories");

    // Worktree config missing while configRoot has a valid config: no configRoot fallback
    assertDecision("just mutate gpumath", "exclusive", "filter-unreadable", contextFor(wtEmpty, cfg));
    assertDecision("just mutate gpumath", "exclusive", "filter-unreadable", { ...ctx, cwd: "", worktree: "" });

    // Restore valid config in wt for CLI override, non-mutate, env, and cd-escape checks
    writeConfig(wt, "gpumath", sharedCfg);

    const exclusiveCases: readonly [string, string][] = [
      ["just test-stability", "exclusive-recipe"],
      ["just test-stability 3h true", "exclusive-recipe"],
      ["just -f justfile mutate gpumath", "exclusive-recipe"],
      ["just a=1 mutate gpumath", "exclusive-recipe"],
      ["node scripts/human/test-stability.js", "not-test-command"],
      ["node /tmp/other/mutate.js --phase gpumath", "not-test-command"],
      ['node -e "console.log(1)" scripts/human/mutate.js', "not-test-command"],
      ["just mutate unknown-phase", "filter-unreadable"],
      ['just mutate ""', "filter-unreadable"],
      ["just mutate toString", "filter-unreadable"],
      ["just mutate gpumath --test-case-filter", "filter-unreadable"],
      ["just mutate gpumath @override.rsp", "filter-unreadable"],
      ['just mutate gpumath "" --test-case-filter "Category=Stress"', "filter-unreadable"],
      ['just mutate gpumath "" -f other.json', "filter-unreadable"],
      ['just mutate gpumath "" --config-file=other.json', "filter-unreadable"],
      ['just mutate gpumath "" -p Other.csproj', "filter-unreadable"],
      ['just mutate gpumath "" --project=Other.csproj', "filter-unreadable"],
      ['just mutate gpumath "" -tp OtherTests.csproj', "filter-unreadable"],
      ['just mutate gpumath "" --test-project=OtherTests.csproj', "filter-unreadable"],
      ['just mutate gpumath "" --test-projects=OtherTests.csproj', "filter-unreadable"],
      ['just mutate gpumath "" -s Other.slnx', "filter-unreadable"],
      ['just mutate gpumath "" --solution=Other.slnx', "filter-unreadable"],
      ['just mutate gpumath "" --filter "Name~Foo"', "filter-unreadable"],
      ['just mutate gpumath "" -t 2', "filter-unreadable"],
      ['just mutate gpumath "" @args.rsp', "filter-unreadable"],
      ['just mutate gpumath "" -p:VSTestTestCaseFilter=Name~Foo', "filter-unreadable"],
      ['just mutate gpumath "" RunConfiguration.TestCaseFilter=Name~Foo', "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --test-case-filter=Name~Foo", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- RunConfiguration.TestCaseFilter=Name~Foo", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- -f other.json", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --test-project Other.csproj", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --project Other.csproj", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --solution Other.slnx", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase", "filter-unreadable"],
      ["node scripts/human/mutate.js --since", "filter-unreadable"],
      ["node scripts/human/mutate.js --unknown", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase bogus", "filter-unreadable"],
      ['node scripts/human/mutate.js --phase gpumath -- -m "**/Stress/*.cs"', "exclusive-category"],
      ["GSHARP_CUDA_TRAP_TESTS=1 just mutate gpumath", "exclusive-env"],
      ["just mutate gpumath | tee log", "piped"],
      ["just mutate gpumath $EXTRA", "hidden"],
      ["cd ../outside && just mutate gpumath", "filter-unreadable"],
      ["cd ../wt2 && just mutate gpumath", "filter-unreadable"],
      ["cd && just mutate gpumath", "filter-unreadable"],
      ["cd && cd tests && just mutate gpumath", "filter-unreadable"],
      ["cd tests && node scripts/human/mutate.js", "not-test-command"],
      ['just mutate gpumath "main --phase compiler-cuda"', "filter-unreadable"],
      ['just mutate gpumath "main --phase compiler"', "filter-unreadable"],
      ['just mutate gpumath "main -- --test-case-filter=FullyQualifiedName~Foo"', "filter-unreadable"],
      ['just mutate gpumath "main -- -f other.json"', "filter-unreadable"],
      ["just mutate gpumath 'main\" -- -f other.json'", "filter-unreadable"],
      ["just mutate gpumath 'main$(calc)'", "filter-unreadable"],
      ['just mutate gpumath "" --test-runner mtp', "filter-unreadable"],
      ['just mutate gpumath "" --test-runner=mtp', "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --test-runner mtp", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --test-runner=mtp", "filter-unreadable"],
      ["node scripts/human/mutate.js --phase gpumath -- --test-runner:mtp", "filter-unreadable"],
      ["cd ~ && just mutate gpumath", "filter-unreadable"],
      ["cd ~/other && just mutate gpumath", "filter-unreadable"],
      ["pushd +1 && just mutate gpumath", "filter-unreadable"],
      ["cd /d tests && just mutate gpumath", "filter-unreadable"],
    ];
    for (const [command, expectedReason] of exclusiveCases) {
      assertDecision(command, "exclusive", expectedReason);
    }

    assertDecision("just mutate gpumath", "exclusive", "exclusive-env", {
      ...ctx,
      env: { GSHARP_CUDA_TRAP_TESTS: "1" },
    });
    assertDecision("just mutate gpumath %EXTRA%", "exclusive", "hidden", { ...ctx, shell: "cmd" });
    assertDecision('just mutate gpumath "" ^-f other.json', "exclusive", "hidden", { ...ctx, shell: "cmd" });
    assertDecision("node scripts/human/mutate.js --phase gpumath -- ^-f other.json", "exclusive", "hidden", {
      ...ctx,
      shell: "cmd",
    });
    assertDecision('just mutate gpumath "HEAD^"', "shared", "categories", { ...ctx, shell: "cmd" });

    const pwshCtx = { ...ctx, shell: "pwsh" as const };
    assertDecision("Set-Location -LiteralPath tests; just mutate gpumath", "shared", "categories", pwshCtx);
    assertDecision("Set-Location -Path tests; just mutate gpumath", "shared", "categories", pwshCtx);
    for (const pwshReject of [
      "Set-Location ~; just mutate gpumath",
      "Set-Location ~/foo; just mutate gpumath",
      "Set-Location ~\\foo; just mutate gpumath",
      "Push-Location ~; just mutate gpumath",
      "Set-Location +; just mutate gpumath",
      "Set-Location env:; just mutate gpumath",
      "Set-Location -Path ~; just mutate gpumath",
      "Set-Location -PassThru tests; just mutate gpumath",
      "Push-Location -StackName s -Path ../outside; just mutate gpumath",
      "Push-Location -StackName s ../outside; just mutate gpumath",
      "Push-Location .. -StackName s; Pop-Location; just mutate gpumath",
      "Push-Location tests; Push-Location .. -StackName s; Pop-Location; just mutate gpumath",
      "Push-Location tests; Pop-Location -StackName other; just mutate gpumath",
      "Push-Location ../outside; Pop-Location -StackName other; just mutate gpumath",
    ]) {
      assertDecision(pwshReject, "exclusive", "filter-unreadable", pwshCtx);
    }

    const cmdCtx = { ...ctx, shell: "cmd" as const };
    assertDecision("cd /d tests && just mutate gpumath", "shared", "categories", cmdCtx);
    assertDecision("chdir /d tests && just mutate gpumath", "shared", "categories", cmdCtx);
    assertDecision("cd /d C:\\outside && just mutate gpumath", "exclusive", "filter-unreadable", cmdCtx);
    assertDecision("cd /d && just mutate gpumath", "exclusive", "filter-unreadable", cmdCtx);

    const linkType = process.platform === "win32" ? "junction" : "dir";
    const outside = path.join(base, "outside", "nested");
    fs.mkdirSync(path.join(outside, "human"), { recursive: true });
    fs.writeFileSync(path.join(outside, "stryker-config.json"), sharedCfg, "utf8");
    fs.writeFileSync(path.join(outside, "human", "mutate.js"), "", "utf8");

    fs.symlinkSync(outside, path.join(wt, "escape"), linkType);
    assertDecision("cd escape && just mutate gpumath", "exclusive", "filter-unreadable");
    assertDecision("Set-Location escape; just mutate gpumath", "exclusive", "filter-unreadable", pwshCtx);
    assertDecision("cd escape && node ../wt/scripts/human/mutate.js --phase gpumath", "exclusive", "not-test-command");

    const gpumathRelDir = MutatePhaseDirectories.gpumath;
    assert.ok(gpumathRelDir !== undefined, "gpumath phase directory exists");
    const wtJunctionCfg = path.join(base, "wt-junction-cfg");
    fs.mkdirSync(path.dirname(path.join(wtJunctionCfg, gpumathRelDir)), { recursive: true });
    fs.symlinkSync(outside, path.join(wtJunctionCfg, gpumathRelDir), linkType);
    assertDecision("just mutate gpumath", "exclusive", "filter-unreadable", contextFor(wtJunctionCfg, cfg));
    assertDecision(
      "node scripts/human/mutate.js --phase gpumath",
      "exclusive",
      "filter-unreadable",
      contextFor(wtJunctionCfg, cfg),
    );

    const wtJunctionScripts = path.join(base, "wt-junction-scripts");
    writeConfig(wtJunctionScripts, "gpumath", sharedCfg);
    fs.symlinkSync(outside, path.join(wtJunctionScripts, "scripts"), linkType);
    assertDecision(
      "node scripts/human/mutate.js --phase gpumath",
      "exclusive",
      "not-test-command",
      contextFor(wtJunctionScripts, cfg),
    );

    fs.symlinkSync(path.join(base, "missing-dir"), path.join(wt, "dangling"), linkType);
    assertDecision("cd dangling && just mutate gpumath", "exclusive", "filter-unreadable");

    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", { ...Gpu, forcedExclusive: true }, ctx).share,
        host.classify("just mutate gpumath", { ...Gpu, forcedExclusive: true }, ctx).shareReason,
      ],
      ["exclusive", "forced-exclusive"],
    );
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", null, ctx).share,
        host.classify("just mutate gpumath", null, ctx).shareReason,
      ],
      [undefined, undefined],
    );
  } finally {
    removeDirectory(dir);
  }
}

/** Repository invariants for `mutate` phases, `justfile`, `sharedRecipes`, and `test-stability`. */
function checkRepoMutateConfigAndJustfile(
  assert: AssertModule,
  host: PluginHost,
  configRoot: string,
  sharedRecipes: unknown,
): void {
  const recipes = Array.isArray(sharedRecipes) ? (sharedRecipes as string[]) : [];
  assert.strictEqual(recipes.includes("mutate"), false, "mutate must not be in sharedRecipes");
  assert.strictEqual(recipes.includes("test-stability"), false, "test-stability must stay exclusive");

  for (const [phase, relDir] of Object.entries(MutatePhaseDirectories)) {
    const configPath = path.join(configRoot, relDir, "stryker-config.json");
    assert.ok(fs.existsSync(configPath), `missing stryker-config.json for mutate phase ${phase}: ${configPath}`);
  }

  const justfile = fs.readFileSync(path.join(configRoot, "justfile"), "utf8");
  const [mutateBody] = justfileReader.recipeBodies(justfile, "mutate");
  assert.ok(mutateBody !== undefined, "the justfile has the mutate recipe");
  assert.ok(
    mutateBody.includes('node scripts/human/mutate.js --phase "{{phase}}"'),
    "justfile mutate recipe invokes mutate.js with --phase",
  );
  assert.ok(
    mutateBody.includes('if phase == "gpumath" { "--gpu" } else { "" }'),
    "justfile mutate recipe acquires --gpu only for gpumath",
  );
  assert.ok(mutateBody.includes("'--since \"' + since + '\"'"), "justfile mutate recipe quotes --since");

  const ctx = contextFor(configRoot, configRoot);
  assert.deepStrictEqual(
    [host.classify("just mutate gpumath", Gpu, ctx).share, host.classify("just mutate gpumath", Gpu, ctx).shareReason],
    ["shared", "categories"],
  );
  assert.deepStrictEqual(
    [
      host.classify("node scripts/human/mutate.js --phase gpumath", Gpu, ctx).share,
      host.classify("node scripts/human/mutate.js --phase gpumath", Gpu, ctx).shareReason,
    ],
    ["shared", "categories"],
  );
  assert.deepStrictEqual(
    [
      host.classify("just mutate compiler", Gpu, ctx).share,
      host.classify("just mutate compiler", Gpu, ctx).shareReason,
    ],
    ["exclusive", "filter-categories"],
  );
  assert.deepStrictEqual(
    [
      host.classify("just mutate compiler-cuda", Gpu, ctx).share,
      host.classify("just mutate compiler-cuda", Gpu, ctx).shareReason,
    ],
    ["exclusive", "filter-categories"],
  );
  assert.deepStrictEqual(
    [host.classify("just test-stability", Gpu, ctx).share, host.classify("just test-stability", Gpu, ctx).shareReason],
    ["exclusive", "exclusive-recipe"],
  );
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
  checkMutateShareRules(assert, host);
  const henicosOptions = loaded.config.plugins?.find((p) => p.name === "./scripts/human/job-queue-henicos")?.options;
  checkSharedRecipeFilters(assert, host, loaded.configRoot, henicosOptions?.sharedRecipes);
  checkRepoMutateConfigAndJustfile(assert, host, loaded.configRoot, henicosOptions?.sharedRecipes);
  await quietly(() => checkQueuedRunMemoryMb(assert, host, FixedBudgetsYaml));
  checkShortCommandWithPlugins(assert, host);
  checkOffloadClassification(assert, host, loaded.configRoot);
  checkHenicosEnvAllowlist(assert, host, loaded.configRoot);
  await checkHenicosOffloadProbe(assert, loaded.configRoot);
  await checkHenicosOffloadSources(assert, loaded.configRoot);
  await checkNameFilter(assert);
}
