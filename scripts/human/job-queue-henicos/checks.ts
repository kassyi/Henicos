// Self-tests of the Henicos repository plugin: pre-move classification/budget characterization (46 commands),
// moved budget/CPU-only project checks, worktree vs configRoot budget resolution, --gpu refuse/hint checks,
// QueuedRun wire memoryMb (request, adopt, gpuTurn omission, JOB_QUEUE_PLUGINS=0, defaultTestMemoryMb),
// and shortCommand prefix removal.

import { spawnSync } from "node:child_process";
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
import {
  DefaultExample,
  MaxCommandLength,
  formatOutsideLanguageRefusal,
  parseSharedCommand,
} from "./command-language.ts";
import { sharedTurnRefusal } from "./share-refusal.ts";
import { checkCommandLanguage } from "./command-language-checks.ts";
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

    // §9.3 & §10.1: --gpu outside the command language is refused (3 lines unsplit, 4 lines when build-split)
    const outsideUnsplit = host.check(
      "dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --no-build --filter Name~Foo",
      gpu,
      ctx,
    );
    assert.ok(outsideUnsplit.refuse !== undefined, "unquoted filter under --gpu must be refused");
    assert.strictEqual(outsideUnsplit.refuse.split("\n").length, 3, "unsplit refusal must be 3 lines");
    assert.ok(outsideUnsplit.refuse.includes("Accepted form:"), "refusal shows accepted form");
    assert.ok(outsideUnsplit.refuse.includes("--gpu-exclusive"), "refusal mentions --gpu-exclusive");
    // When planBuildStep splits build+test, prepend build-split line and omit split-added --no-build from example
    const outsideSplit = host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter Name~Foo", gpu, ctx);
    assert.ok(outsideSplit.refuse !== undefined, "split command with unquoted filter must be refused");
    const splitLines = outsideSplit.refuse.split("\n");
    assert.strictEqual(splitLines.length, 4, "split refusal must be 4 lines");
    assert.ok(
      splitLines[0]?.includes("dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cuda") &&
        splitLines[0]?.includes("would run first as a CPU job"),
      "line 0 names the CPU build job",
    );
    assert.ok(splitLines[1]?.includes("in test part at pos"), "line 1 states position is within the test part");
    assert.strictEqual(
      splitLines[2],
      'Accepted form: dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter "Name~Foo"',
      "split-added --no-build is omitted from example and not listed as dropped",
    );
    // Same command with --gpu-exclusive is allowed
    const gpuExcl = { name: "gpu", forcedExclusive: true };
    assert.strictEqual(
      host.check("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --filter Name~Foo", gpuExcl, ctx).refuse,
      undefined,
      "--gpu-exclusive accepts commands outside the language",
    );
    // Build-split chain whose post-split test step is in the language is allowed under --gpu
    const buildSplitCmd =
      "dotnet build tests/Compiler/Kassyi.GSharp.Tests.Cuda && " +
      `dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --no-build --filter "${Kept}"`;
    assert.strictEqual(host.check(buildSplitCmd, gpu, ctx).refuse, undefined, "build-split into language passes");
    // Non-shared recipe under --gpu is refused and falls back to DefaultExample (does not suggest itself)
    const nonSharedRefusal = host.check("just test-coverage", gpu, ctx).refuse;
    assert.ok(nonSharedRefusal !== undefined, "non-shared recipe refused under --gpu");
    assert.ok(nonSharedRefusal.includes(`Accepted form: ${DefaultExample}`), "non-shared recipe falls back to default");

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
  ["just test-coverage", "exclusive"],
  ["just test-differential-cuda", "shared"],
  ['just test-differential-cuda --filter "Category=Differential"', "exclusive"],
  ["just test-gpu-trap", "exclusive"],
  ["just test-stability", "exclusive"],
  ["just -f justfile test", "exclusive"],
  ['just test --filter "FullyQualifiedName~Foo"', "exclusive"],
  [`just test --filter "${Kept}&Name~Foo"`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}"`, "shared"],
  [
    'dotnet test tests/X --filter "Category!=gputrap&Category!=STRESS&FullyQualifiedName~Foo&Category!=benchmark"',
    "shared",
  ],
  [`dotnet test tests/X --filter "${Kept}&Category=Stress"`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}&Category=stress"`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" --filter:"Name~Foo"`, "exclusive"],
  ['dotnet test tests/X --filter "Category=GpuTrap"', "exclusive"],
  ['dotnet test tests/X --filter "Category=Stress"', "exclusive"],
  ['dotnet test tests/X --filter "Category=Benchmark"', "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" -e GSHARP_CUDA_TRAP_TESTS=1`, "exclusive"],
  [`GSHARP_CUDA_TRAP_TESTS=1 dotnet test tests/X --filter "${Kept}"`, "exclusive"],
  ["dotnet run --project tests/X", "exclusive"],
  [`dotnet test $(cat projects.txt) --filter "${Kept}"`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}`, "exclusive"],
  [`dotnet test tests/X --no-build --filter "${Kept}"`, "shared"],
  [`dotnet build tests/X && dotnet test tests/X --no-build --filter "${Kept}"`, "exclusive"],
  [`cd tests && dotnet test tests/X --filter "${Kept}"; echo done`, "exclusive"],
  [`Set-Location tests; dotnet test tests/X --filter "${Kept}"; Pop-Location`, "exclusive"],
  [`$env:DiffEngine_Disabled='true'; dotnet test tests/X --filter "${Kept}"`, "exclusive"],
  [`DiffEngine_Disabled=true dotnet test tests/X --filter "${Kept}"`, "shared"],
  [`DOTNET_CLI_UI_LANGUAGE=en dotnet test tests/X --filter "${Kept}"`, "shared"],
  [`dotnet.exe test tests/X --filter "${Kept}"`, "exclusive"],
  [`dotnet test tests/bin/Tests.dll --filter "${Kept}"`, "exclusive"],
  [`dotnet test tests/X --filter="${Kept}"`, "exclusive"],
  [`dotnet test tests/X --filter:"${Kept}"`, "exclusive"],
  ['dotnet test tests/X --filter ""', "exclusive"],
  ['dotnet test tests/X --filter " "', "exclusive"],
  ["echo hello", "exclusive"],
  [`node scripts/human/coverage-diff.js --run && dotnet test tests/X --filter "${Kept}"`, "exclusive"],
  ["dotnet test tests/X", "exclusive"],
  ["dotnet test tests/X --no-build", "exclusive"],
  ['DiffEngine_Disabled=true dotnet test tests/X --filter "FullyQualifiedName~Foo"', "exclusive"],
  ['dotnet test tests/X --filter "FullyQualifiedName~PoolStressTests"', "exclusive"],
  ['dotnet test tests/X --filter "Name~PoolStressTests"', "exclusive"],
  ['dotnet test tests/X --filter "Category!=Slow"', "exclusive"],
  ['dotnet test tests/X --filter "Category!=Stress"', "exclusive"],
  ['dotnet test tests/X --filter "Category!=Stress&Category!=Benchmark"', "exclusive"],
  ['dotnet test tests/X --filter "Category~Stres"', "exclusive"],
  ['dotnet test tests/X --filter "Category=stress"', "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" --filter "Name~Foo"`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" | tee log`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" || echo failed`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}&(Name~A|Name~B)"`, "exclusive"],
  ['dotnet test tests/X --filter "$FILTER"', "exclusive"],
  ["dotnet test tests/X --filter $FILTER", "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" -p:Configuration=Release`, "exclusive"],
  [`dotnet test tests/X --filter "${Kept}" -p:VSTestTestCaseFilter=FullyQualifiedName~PoolStressTests`, "exclusive"],
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
      sharedRecipes: ["test", "test-differential-cuda"],
    };
    const reasonCtx = { ...ctx, options: repoOptions };
    const reasonCases: readonly [string, string][] = [
      [`dotnet test tests/X --filter "${Kept}"`, "categories"],
      ["just test", "shared-recipe"],
      ["just test-stability", "exclusive-recipe"],
      ["dotnet test tests/X --no-build", "no-filter"],
      [`dotnet test tests/bin/Tests.dll --filter "${Kept}"`, "binary-test"],
      ['dotnet test tests/X --filter "Category!=Stress"', "filter-categories"],
      [`dotnet test tests/X --filter "${Kept}&Category=Stress"`, "exclusive-category"],
      ['dotnet test tests/X --filter "Category=stress"', "exclusive-category"],
      // Commands outside the allow-listed language all report "not-language"
      ['just test --filter "FullyQualifiedName~Foo"', "not-language"],
      [`dotnet test tests/X --filter "${Kept}" -p:Configuration=Release`, "not-language"],
      [`dotnet test tests/X --filter "${Kept}" --filter:"Name~Foo"`, "not-language"],
      ["$env:DiffEngine_Disabled='true'; dotnet test tests/X --no-build --filter \"Name~Foo\"", "not-language"],
      ['ROWBATCH_ROOTS=1 dotnet test tests/X --no-build --filter "Name~Foo"', "not-language"],
      [`dotnet test tests/X --filter "${Kept}" | tee log`, "not-language"],
      [`dotnet test tests/X --filter "${Kept}`, "not-language"],
      ["echo hello", "not-language"],
      ['dotnet test tests/X --filter "$FILTER"', "not-language"],
      ["dotnet build tests/X", "not-language"],
    ];
    for (const [command, expectedReason] of reasonCases) {
      assert.strictEqual(
        host.classify(command, Gpu, ctx).shareReason,
        expectedReason,
        `classify shareReason for: ${command}`,
      );
      assert.strictEqual(
        shareDecision(command, reasonCtx)?.reason,
        expectedReason,
        `shareDecision reason for: ${command}`,
      );
    }
    // PowerShell env prefixes are accepted when shell is pwsh or powershell
    for (const psShell of ["pwsh", "powershell"] as const) {
      const psCtx = { ...ctx, shell: psShell };
      const psShared = host.classify(
        `$env:DiffEngine_Disabled='true'; dotnet test tests/X --filter "${Kept}"`,
        Gpu,
        psCtx,
      );
      assert.deepStrictEqual([psShared.share, psShared.shareReason], ["shared", "categories"]);
      const psNamedCmd =
        `$env:DiffEngine_Disabled="true"; $env:DOTNET_CLI_UI_LANGUAGE='en'; ` +
        'dotnet test tests/X/X.csproj --no-build --filter "FullyQualifiedName~Foo"';
      const psNamed = host.classify(psNamedCmd, Gpu, psCtx);
      assert.deepStrictEqual([psNamed.share, psNamed.shareReason], [undefined, undefined]);
      const psBare = host.classify(
        `$env:DiffEngine_Disabled= hostname; dotnet test tests/X --filter "${Kept}"`,
        Gpu,
        psCtx,
      );
      assert.deepStrictEqual([psBare.share, psBare.shareReason], ["exclusive", "not-language"]);
    }
    // A command that nameFilterCheck reads has no opinion (checkShare judges it by the test list); a veto stays one
    const named = 'dotnet test tests/X/X.csproj --no-build --filter "FullyQualifiedName~Foo"';
    const namedClassified = host.classify(named, Gpu, ctx);
    assert.deepStrictEqual([namedClassified.share, namedClassified.shareReason], [undefined, undefined]);
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
    const filtered = `dotnet test tests/X --filter "${Kept}"`;
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
    const queued = /--gpu(?:" \} else \{ "--gpu-exclusive" \}\})? -- ([\s\S]*)$/.exec(body ?? "");
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

/** Checks GPU share classification for `just mutate <phase>` and inner `node scripts/human/mutate.js --phase <phase> --`. */
function checkMutateShare(assert: AssertModule, host: PluginHost, repoRoot: string): void {
  const repoCtx = contextFor(repoRoot, repoRoot);
  // Real repository configs: gpumath shares via both outer `just mutate` and inner `node scripts/human/mutate.js`
  for (const cmd of ["just mutate gpumath", "node scripts/human/mutate.js --phase gpumath --"]) {
    const res = host.classify(cmd, Gpu, repoCtx);
    assert.deepStrictEqual([res.share, res.shareReason], ["shared", "mutate-config"], `shared mutate: ${cmd}`);
    assert.strictEqual(host.check(cmd, Gpu, repoCtx).refuse, undefined, `--gpu check accepts: ${cmd}`);
  }
  assert.deepStrictEqual(
    [
      host.classify("just mutate compiler", Gpu, repoCtx).share,
      host.classify("just mutate compiler", Gpu, repoCtx).shareReason,
    ],
    ["exclusive", "mutate-config-unreadable"],
    "compiler stryker-config uses parentheses outside FILTER",
  );
  assert.deepStrictEqual(
    [
      host.classify("just mutate compiler-cuda", Gpu, repoCtx).share,
      host.classify("just mutate compiler-cuda", Gpu, repoCtx).shareReason,
    ],
    ["exclusive", "mutate-filter-categories"],
  );
  assert.deepStrictEqual(
    [
      host.classify("just test-stability", Gpu, repoCtx).share,
      host.classify("just test-stability", Gpu, repoCtx).shareReason,
    ],
    ["exclusive", "exclusive-recipe"],
  );
  for (const cmd of [
    "just mutate",
    "just mutate gpumath main",
    "just mutate gpumath -- -m Foo",
    "node scripts/human/mutate.js --phase gpumath -- --since main",
  ]) {
    const res = host.classify(cmd, Gpu, repoCtx);
    assert.deepStrictEqual([res.share, res.shareReason], ["exclusive", "not-language"], `non-language: ${cmd}`);
  }
  const trapRes = host.classify("just mutate gpumath", Gpu, { ...repoCtx, env: { GSHARP_CUDA_TRAP_TESTS: "1" } });
  assert.deepStrictEqual([trapRes.share, trapRes.shareReason], ["exclusive", "exclusive-env"]);

  // Synthetic worktree checks: malformed, empty filter, unknown key, lowercase category=stress, cwd != worktree, junction
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-henicos-mutate-"));
  try {
    const base = fs.realpathSync.native(dir);
    const wt = path.join(base, "wt");
    const otherCwd = path.join(base, "other-cwd");
    const outsideDir = path.join(base, "outside");
    const cfgDir = path.join(wt, "tests", "Stdlib", "Kassyi.GSharp.Stdlib.GpuMath.Tests");
    fs.mkdirSync(cfgDir, { recursive: true });
    fs.mkdirSync(otherCwd, { recursive: true });
    fs.mkdirSync(outsideDir, { recursive: true });
    const cfgFile = path.join(cfgDir, "stryker-config.json");
    const wtCtx = contextFor(wt, repoRoot);

    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "missing stryker-config.json",
    );
    fs.writeFileSync(cfgFile, "{not json");
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "malformed JSON",
    );
    fs.writeFileSync(cfgFile, JSON.stringify({ "stryker-config": { "test-case-filter": "" } }));
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "empty test-case-filter",
    );
    fs.writeFileSync(
      cfgFile,
      JSON.stringify({
        "stryker-config": { "test-case-filter": `FullyQualifiedName~Stdlib&${Kept}`, "test-runner": "mtp" },
      }),
    );
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "unknown stryker-config key (test-runner) fails closed",
    );
    fs.writeFileSync(
      cfgFile,
      JSON.stringify({ "stryker-config": { "test-case-filter": `(${Kept})&FullyQualifiedName~Stdlib` } }),
    );
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "filter outside closed FILTER type (parentheses)",
    );
    fs.writeFileSync(
      cfgFile,
      JSON.stringify({ "stryker-config": { "test-case-filter": "Category!=Stress&Category!=Benchmark" } }),
    );
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "mutate-filter-categories"],
      "missing GpuTrap exclusion",
    );
    fs.writeFileSync(cfgFile, JSON.stringify({ "stryker-config": { "test-case-filter": `${Kept}&category=stress` } }));
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["exclusive", "exclusive-category"],
      "lowercase category=stress reports exclusive-category",
    );
    fs.writeFileSync(
      cfgFile,
      JSON.stringify({ "stryker-config": { "test-case-filter": `FullyQualifiedName~Stdlib&${Kept}` } }),
    );
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, wtCtx).share,
        host.classify("just mutate gpumath", Gpu, wtCtx).shareReason,
      ],
      ["shared", "mutate-config"],
      "all 3 exclusions present",
    );
    // When cwd differs from worktree, read stryker-config.json from worktree (not cwd)
    const diffCwdCtx = { ...wtCtx, cwd: otherCwd };
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, diffCwdCtx).share,
        host.classify("just mutate gpumath", Gpu, diffCwdCtx).shareReason,
      ],
      ["shared", "mutate-config"],
      "cwd != worktree reads stryker-config from worktree",
    );
    // Junction/symlink pointing outside the worktree must fail closed as mutate-config-unreadable
    fs.writeFileSync(
      path.join(outsideDir, "stryker-config.json"),
      JSON.stringify({ "stryker-config": { "test-case-filter": `FullyQualifiedName~Stdlib&${Kept}` } }),
    );
    const wtLink = path.join(base, "wt-link");
    const linkParent = path.join(wtLink, "tests", "Stdlib");
    fs.mkdirSync(linkParent, { recursive: true });
    fs.symlinkSync(
      outsideDir,
      path.join(linkParent, "Kassyi.GSharp.Stdlib.GpuMath.Tests"),
      process.platform === "win32" ? "junction" : "dir",
    );
    const linkCtx = contextFor(wtLink, repoRoot);
    assert.deepStrictEqual(
      [
        host.classify("just mutate gpumath", Gpu, linkCtx).share,
        host.classify("just mutate gpumath", Gpu, linkCtx).shareReason,
      ],
      ["exclusive", "mutate-config-unreadable"],
      "junction/symlink outside worktree fails closed",
    );
  } finally {
    removeDirectory(dir);
  }

  // Verify `just --dry-run mutate <phase>`: --gpu only on gpumath, absent on compiler and compiler-cuda
  const justCheck = spawnSync("just", ["--version"], { windowsHide: true });
  if (!justCheck.error && justCheck.status === 0) {
    const dryRun = (phase: string): string => {
      const r = spawnSync("just", ["--dry-run", "mutate", phase], {
        cwd: repoRoot,
        encoding: "utf8",
        windowsHide: true,
      });
      assert.strictEqual(r.status, 0, `just --dry-run mutate ${phase} succeeded`);
      return `${r.stdout ?? ""}\n${r.stderr ?? ""}`;
    };
    assert.ok(dryRun("gpumath").includes("--gpu"), "just mutate gpumath passes --gpu");
    assert.strictEqual(dryRun("compiler").includes("--gpu"), false, "just mutate compiler omits --gpu");
    assert.strictEqual(dryRun("compiler-cuda").includes("--gpu"), false, "just mutate compiler-cuda omits --gpu");
  }
}

/** Unit and property checks for `formatOutsideLanguageRefusal` (all 4 rules of §10.1). */
function checkOutsideLanguageRefusal(assert: AssertModule): void {
  const extractExample = (refusal: string): string => {
    const line = refusal.split("\n").find((l) => l.startsWith("Accepted form: "));
    assert.ok(line !== undefined, `refusal must contain Accepted form line:\n${refusal}`);
    return line.slice("Accepted form: ".length).replace(/ \(dropped: .*\)$/, "");
  };

  // Rule 1: build-split notice, "in test part" position, omit split-added --no-build, keep user-written --no-build
  const splitAdded = formatOutsideLanguageRefusal("dotnet test --no-build tests/X -p:Foo=1", "bash", {
    splitBuild: { build: "dotnet build tests/X", originalCommand: "dotnet test tests/X -p:Foo=1" },
  });
  const splitAddedLines = splitAdded.split("\n");
  assert.strictEqual(splitAddedLines.length, 4, "split refusal has 4 lines");
  assert.ok(splitAddedLines[0]?.includes("`dotnet build tests/X` would run first as a CPU job"));
  assert.ok(splitAddedLines[1]?.includes("in test part at pos"));
  assert.strictEqual(splitAddedLines[2], "Accepted form: dotnet test tests/X (dropped: -p:Foo=1)");

  const splitKept = formatOutsideLanguageRefusal("dotnet test tests/X --no-build -p:Foo=1", "bash", {
    splitBuild: {
      build: "dotnet build tests/X",
      originalCommand: "dotnet build tests/X && dotnet test tests/X --no-build -p:Foo=1",
    },
  });
  assert.strictEqual(
    splitKept.split("\n")[2],
    "Accepted form: dotnet test tests/X --no-build (dropped: -p:Foo=1)",
    "user-written --no-build is kept when split",
  );

  // Rule 2: keeps all in-language items in original order (prefixes, blame, verbosity, logger, redirect) and drops only out-of-language
  const richBash =
    "ROWBATCH_ROOTS=1 DiffEngine_Disabled=true dotnet test --blame-crash tests/X --blame-crash-dump-type mini " +
    '-v minimal -p:Configuration=Release --blame-hang-timeout 30s --logger "console;verbosity=detailed" ' +
    '--filter "Name~Foo" > tmp/out.log 2>&1';
  const richBashRefusal = formatOutsideLanguageRefusal(richBash, "bash");
  assert.strictEqual(
    richBashRefusal.split("\n")[1],
    "Accepted form: DiffEngine_Disabled=true dotnet test --blame-crash tests/X --blame-crash-dump-type mini " +
      '-v minimal --blame-hang-timeout 30s --logger "console;verbosity=detailed" --filter "Name~Foo" ' +
      "> tmp/out.log 2>&1 (dropped: ROWBATCH_ROOTS=1, -p:Configuration=Release)",
  );

  const richPwsh =
    "$env:DiffEngine_Disabled='true'; $env:ROWBATCH_ROOTS='1'; dotnet test tests/X --no-build " +
    "--logger 'console;verbosity=normal' --filter Name~Bar | Tee-Object log.txt";
  const richPwshRefusal = formatOutsideLanguageRefusal(richPwsh, "pwsh");
  assert.strictEqual(
    richPwshRefusal.split("\n")[1],
    "Accepted form: $env:DiffEngine_Disabled='true'; dotnet test tests/X --no-build " +
      '--logger "console;verbosity=normal" --filter "Name~Bar" ' +
      "(dropped: $env:ROWBATCH_ROOTS='1';, |, Tee-Object, log.txt)",
  );

  // Separator stops item collection so tokens from a trailing command are not pulled in
  // Quoted tokens are cut to 80 characters; the length failure is not put in parentheses twice
  const longWord = `-p:X=${"a".repeat(5000)}`;
  const longLines = formatOutsideLanguageRefusal(`dotnet test tests/X ${longWord}`, "bash").split("\n");
  assert.ok(
    longLines.every((line) => line.length <= 200),
    `refusal lines stay short: ${longLines.map((l) => l.length)}`,
  );
  const overlong = formatOutsideLanguageRefusal(`dotnet test tests/X --filter "${"a".repeat(5000)}"`, "bash");
  assert.ok(/ \(\d+ chars\): /.test(overlong) && !overlong.includes("(("), `one pair of parentheses: ${overlong}`);
  assert.ok(
    formatOutsideLanguageRefusal("dotnet test tests/X (Name~A)", "bash").includes(" ((Name~A)): "),
    "a token that starts with ( keeps its parentheses",
  );
  const noRedirect = sharedTurnRefusal("dotnet test tests/X -p:A=1", "bash", {
    exclusiveCategories: ["Stress"],
    exclusiveEnv: [],
    sharedRecipes: ["test"],
  });
  assert.ok(noRedirect !== null && !noRedirect.includes(">>"), `no redirect, no append note: ${noRedirect}`);
  const withRedirect = sharedTurnRefusal("dotnet test tests/X -p:A=1 > tmp/x.log", "bash", {
    exclusiveCategories: ["Stress"],
    exclusiveEnv: [],
    sharedRecipes: ["test"],
  });
  assert.ok(withRedirect?.split("\n")[0]?.includes("append (>>)"), `the split redirect is named: ${withRedirect}`);
  const chainedRefusal = formatOutsideLanguageRefusal('dotnet test tests/X --filter "Name~A"; echo --no-build', "bash");
  assert.strictEqual(
    chainedRefusal.split("\n")[1],
    'Accepted form: dotnet test tests/X --filter "Name~A" (dropped: ;, echo, --no-build)',
  );

  // Rule 3: head dispatch (mid-line dotnet test -> DefaultExample; mutate -> just mutate <phase>; just <recipe>)
  assert.strictEqual(
    extractExample(formatOutsideLanguageRefusal("echo hi && dotnet test tests/X --no-build", "bash")),
    DefaultExample,
    "mid-line dotnet test must not produce a dotnet test example from the tail",
  );
  assert.strictEqual(
    extractExample(formatOutsideLanguageRefusal("dotnet build tests/X && dotnet test tests/X --no-build", "bash")),
    DefaultExample,
    "dotnet build && dotnet test without build-split falls back to DefaultExample",
  );
  assert.strictEqual(extractExample(formatOutsideLanguageRefusal("just mutate", "bash")), "just mutate gpumath");
  assert.strictEqual(extractExample(formatOutsideLanguageRefusal("just mutate other", "bash")), "just mutate gpumath");
  assert.strictEqual(
    extractExample(formatOutsideLanguageRefusal("just mutate compiler -- -m Foo", "bash")),
    "just mutate compiler",
  );
  assert.strictEqual(
    extractExample(
      formatOutsideLanguageRefusal("node scripts/human/mutate.js --phase compiler-cuda -- --since main", "bash"),
    ),
    "just mutate compiler-cuda",
  );
  assert.strictEqual(
    extractExample(formatOutsideLanguageRefusal("node scripts/human/mutate.js --phase=gpumath", "pwsh")),
    "just mutate gpumath",
  );
  assert.strictEqual(
    extractExample(formatOutsideLanguageRefusal("node scripts/human/mutate.js --phase unknown", "bash")),
    "just mutate gpumath",
  );
  assert.strictEqual(extractExample(formatOutsideLanguageRefusal('just test --filter "Name~A"', "bash")), "just test");
  assert.strictEqual(
    extractExample(
      formatOutsideLanguageRefusal("just test-coverage", "bash", { sharedRecipes: ["test", "test-differential-cuda"] }),
    ),
    DefaultExample,
  );
  assert.strictEqual(extractExample(formatOutsideLanguageRefusal("just -f justfile test", "bash")), DefaultExample);
  assert.strictEqual(extractExample(formatOutsideLanguageRefusal("echo hello", "bash")), DefaultExample);

  // Rule 4: every generated example across all shells and negative/overlong inputs is accepted and <= 4000 chars
  const allShells = ["bash", "sh", "pwsh", "powershell", "cmd"] as const;
  const corpus = [
    "",
    "   ",
    "echo hello",
    "dotnet build tests/X",
    "dotnet.exe test tests/X",
    `dotnet test tests/${"a".repeat(3990)}`,
    `dotnet test tests/X --filter "Name~${"a".repeat(8200)}"`,
    `dotnet test tests/X --no-build ${"--nologo ".repeat(600)}`,
    'dotnet test tests/X --filter "Name~A"\necho pwn',
    'dotnet test\ttests/X --filter "Name~A"',
    '$env:DiffEngine_Disabled= hostname; dotnet test tests/X --filter "Name~A"',
    "$env:DiffEngine_Disabled=\u2018true\u2019; dotnet test tests/X",
    "dotnet test tests/X --filter \u201CName~A\u201D",
    'dotnet test tests/日本語 --filter "Name~A"',
    'dotnet test tests/X Category=Stress --filter "Name~A"',
    "dotnet test tests/X -a: -x:--filter Category=Stress",
    "dotnet test -a/b",
    'just test --filter "Category!=Stress&Category!=Benchmark&Category!=GpuTrap"',
    "just mutate",
    "just mutate other",
    "just mutate gpumath -- -m Foo",
    "node scripts/human/mutate.js --phase gpumath -- --since main",
    "dotnet test tests/X -p Configuration=Release",
    "dotnet test tests/X --filter FullyQualifiedName~Foo",
    'dotnet test tests/X --filter "(Name~A|Name~B)"',
    'dotnet test tests/X --filter "Name~A" --filter "Name~B"',
    "dotnet test tests/X --no-build --no-build",
    "dotnet test tests/X -c Debug --configuration Release",
    "dotnet test tests/A tests/B",
    'dotnet test tests/X --filter "$FILTER"',
    'dotnet test tests/X --filter "Name~A" | tee log',
    "dotnet test tests/X > C:\\x.log",
    "dotnet test tests/X > /c/x.log",
    "dotnet test tests/X > ../x.log",
    "dotnet test tests/X > NUL",
    "dotnet test tests/X > 1kb",
    "dotnet test tests/X > tmp/out.log --no-build",
    'ROWBATCH_ROOTS=1 dotnet test tests/X --no-build --filter "Name~A"',
    "$env:ROWBATCH_ROOTS='1'; dotnet test tests/X --no-build --filter \"Name~A\"",
    "DiffEngine_Disabled=true dotnet test tests/X",
    "$env:DiffEngine_Disabled='true'; dotnet test tests/X",
    "dotnet test tests/X --filter 'Name~A'",
    "dotnet test tests/X --logger 'console;verbosity=normal'",
    "Set-Item Env:DiffEngine_Disabled true; dotnet test tests/X",
    "set DiffEngine_Disabled=true && dotnet test tests/X",
  ];
  for (const shell of allShells) {
    assert.ok(parseSharedCommand(DefaultExample, shell) !== null, `DefaultExample must parse on ${shell}`);
    for (const cmd of corpus) {
      const refusal = formatOutsideLanguageRefusal(cmd, shell);
      const ex = extractExample(refusal);
      assert.ok(
        ex.length <= MaxCommandLength,
        `example must be <= ${MaxCommandLength} chars on ${shell} for ${JSON.stringify(cmd.slice(0, 60))}`,
      );
      assert.ok(
        parseSharedCommand(ex, shell) !== null,
        `example must be accepted on ${shell}: got ${JSON.stringify(ex)} for ${JSON.stringify(cmd.slice(0, 60))}`,
      );
    }
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
  checkOutsideLanguageRefusal(assert);
  checkShareRules(assert, host);
  checkMutateShare(assert, host, loaded.configRoot);
  const henicosOptions = loaded.config.plugins?.find((p) => p.name === "./scripts/human/job-queue-henicos")?.options;
  checkSharedRecipeFilters(assert, host, loaded.configRoot, henicosOptions?.sharedRecipes);
  await quietly(() => checkQueuedRunMemoryMb(assert, host, FixedBudgetsYaml));
  checkShortCommandWithPlugins(assert, host);
  checkOffloadClassification(assert, host, loaded.configRoot);
  checkHenicosEnvAllowlist(assert, host, loaded.configRoot);
  await checkHenicosOffloadProbe(assert, loaded.configRoot);
  await checkHenicosOffloadSources(assert, loaded.configRoot);
  await checkNameFilter(assert);
  await checkCommandLanguage(assert);
}
