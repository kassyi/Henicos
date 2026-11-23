// Self-tests of the dotnet plugin: pre-move key characterization, estimate keys/places/fallback, plugin-less core
// generic keys, build/test split via PluginHost, MSBuild node limits, QueuedRun wire keys & JOB_QUEUE_PLUGINS=0,
// and classify / shortName / api helpers.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { CpusVariable, EntryScript, JobVariable, ProtocolVersion, type ShellName } from "../../constants.ts";
import { runBuildStep } from "../../client-build-step.ts";
import { QueuedRun } from "../../client-queued-run.ts";
import type { RunOptions } from "../../client.ts";
import { loadConfig } from "../../config.ts";
import { commandKeys, pathArguments, type KeyContext } from "../../estimate-key.ts";
import { RunHistory } from "../../history.ts";
import { resolvePlaces } from "../../path-places.ts";
import type { AssertModule, StepKeys } from "../../plugin-api.ts";
import { PluginHost, type BaseClientContext } from "../../plugin-host.ts";
import { translateForRemote } from "../../remote/command-translator.ts";
import { validateAndWriteArtifacts } from "../../remote/artifact-collector.ts";
import { isExcludedSyncPath } from "../../remote/syncer.ts";
import { FakeHost, FakeScheduler, quietly, until } from "../../self-test-client.ts";
import { resolveWorktree } from "../../system.ts";
import { shortCommand } from "../../view.ts";
import dotnetPlugin, { isDotnetTest, selectsCategory } from "./plugin.ts";
import { parseSequenceXmlInProgress } from "./remote.ts";

const Cpu = "tests/Compiler/App.Cpu";
const Cuda = "tests/Compiler/App.Cuda";
const CudaFull = "tests/Compiler/Kassyi.GSharp.Tests.Cuda";
const Windows: KeyContext = { cwd: "C:\\work\\repo\\wt\\feat-a", worktree: "c:\\work\\repo\\wt\\feat-a" };
const Unix: KeyContext = { cwd: "/home/dev/wt/feat-a", worktree: "/home/dev/wt/feat-a" };
const Subdir: KeyContext = { cwd: "C:\\work\\repo\\wt\\feat-a\\tests", worktree: Windows.worktree };
const Empty: KeyContext = { cwd: "", worktree: "" };
const Main: KeyContext = { cwd: "C:\\work\\repo", worktree: "c:\\work\\repo" };

const OtherWt = "C:\\work\\repo\\tmp\\worktree\\feat-b";
const ProbeProj = "C:\\work\\repo\\tmp\\probe\\Probe.csproj";
const ScratchProj = "C:\\Temp\\scratch\\Probe.csproj";
const MainPlaces: KeyContext = {
  ...Main,
  places: {
    [`${OtherWt}\\tests\\Compiler\\App.Cpu`]: { root: OtherWt, ignored: false },
    [ProbeProj]: { root: "C:\\work\\repo", ignored: true },
    [ScratchProj]: { root: "", ignored: false },
  },
};

function clientContext(shell: ShellName = "bash", configRoot = process.cwd()): BaseClientContext {
  return { cwd: configRoot, worktree: configRoot, configRoot, shell, env: {}, log: () => {} };
}

function baseRunOptions(command: string): RunOptions {
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
  };
}

/** Exact pre-move { normalized, shape, kind } outputs for all dotnet commands tested before the move. */
function checkCharacterization(assert: AssertModule, host: PluginHost): void {
  const contexts: Record<string, KeyContext> = { Windows, Unix, Subdir, Empty, Main, MainPlaces };
  const table: [string, string, StepKeys][] = [
    ["Windows", `dotnet build ${Cpu}`, { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" }],
    [
      "Windows",
      `dotnet build ${Cpu} -nodeReuse:false > tmp/a.log 2>&1`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build ${Cpu} -m:6 -nodeReuse:false -v:q -nologo --logger trx -l console &> tmp/a.log`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `CI=true DiffEngine_Disabled=true dotnet build ${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `export CI=true; dotnet build ${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `$env:CI='true'; dotnet build ${Cpu} *> tmp/a.log`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build "${Cpu}" 2>&1 | grep -E 'error' | tail -5`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build ${Cpu} # intent: a reason`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    ["Windows", `dotnet build ./${Cpu}`, { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" }],
    [
      "Windows",
      `dotnet build C:/work/repo/wt/feat-a/${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      "dotnet build C:\\work\\repo\\wt\\feat-a\\tests\\Compiler\\App.Cpu",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build /c/work/repo/wt/feat-a/${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build /mnt/c/work/repo/wt/feat-a/${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      "cd tests && dotnet build Compiler/App.Cpu",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Subdir",
      "dotnet build Compiler/App.Cpu",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Unix",
      `dotnet build /home/dev/wt/feat-a/${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet build C:/work/repo/wt/feat-b/${Cpu}`,
      {
        normalized: `dotnet build C:/work/repo/wt/feat-b/${Cpu}`,
        shape: "build:App.Cpu",
        kind: "build:outside",
      },
    ],
    [
      "Windows",
      `dotnet test ${Cpu} --no-build -c Release`,
      { normalized: `dotnet test ${Cpu} --no-build -c Release`, shape: "test:App.Cpu:nb", kind: "test" },
    ],
    [
      "Windows",
      `dotnet test -c Release --no-build ${Cpu}`,
      { normalized: `dotnet test ${Cpu} --no-build -c Release`, shape: "test:App.Cpu:nb", kind: "test" },
    ],
    ["Windows", `dotnet build ${Cuda}`, { normalized: `dotnet build ${Cuda}`, shape: "build:App.Cuda", kind: "build" }],
    [
      "Windows",
      `dotnet build ${Cpu} -c Release`,
      { normalized: `dotnet build ${Cpu} -c Release`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Windows",
      `dotnet test ${Cpu} --no-build`,
      { normalized: `dotnet test ${Cpu} --no-build`, shape: "test:App.Cpu:nb", kind: "test" },
    ],
    ["Windows", `dotnet test ${Cpu}`, { normalized: `dotnet test ${Cpu}`, shape: "test:App.Cpu", kind: "test" }],
    [
      "Windows",
      `dotnet build ${Cpu}; dotnet test ${Cpu} --no-build`,
      {
        normalized: `dotnet build ${Cpu} ; dotnet test ${Cpu} --no-build`,
        shape: "build:App.Cpu&test:App.Cpu:nb",
        kind: "build&test",
      },
    ],
    [
      "Windows",
      `dotnet build ${Cpu} && dotnet test ${Cpu} --no-build`,
      {
        normalized: `dotnet build ${Cpu} ; dotnet test ${Cpu} --no-build`,
        shape: "build:App.Cpu&test:App.Cpu:nb",
        kind: "build&test",
      },
    ],
    [
      "Windows",
      `dotnet test ${Cpu} --filter "A|B"`,
      { normalized: `dotnet test ${Cpu} --filter A|B`, shape: "test:App.Cpu:f", kind: "test" },
    ],
    [
      "Windows",
      `dotnet test ${Cpu} --filter "A|C"`,
      { normalized: `dotnet test ${Cpu} --filter A|C`, shape: "test:App.Cpu:f", kind: "test" },
    ],
    [
      "Windows",
      `CI=true dotnet test ${Cpu} --no-build --filter 'A|B' > x.log`,
      { normalized: `dotnet test ${Cpu} --filter A|B --no-build`, shape: "test:App.Cpu:nb:f", kind: "test" },
    ],
    [
      "Windows",
      `cd tests && dotnet build ${Cpu}; grep error a.log; dotnet test ${Cpu} --no-build`,
      {
        normalized: `dotnet build tests/${Cpu} ; dotnet test tests/${Cpu} --no-build`,
        shape: "build:App.Cpu&test:App.Cpu:nb",
        kind: "build&test",
      },
    ],
    [
      "Windows",
      "dotnet build a.csproj || dotnet build b.csproj",
      { normalized: "dotnet build a.csproj ; dotnet build b.csproj", shape: "build:a&build:b", kind: "build&build" },
    ],
    [
      "Windows",
      "dotnet run --project tools/x",
      { normalized: "dotnet run tools/x --project", shape: "dotnet:run", kind: "dotnet" },
    ],
    [
      "Windows",
      `dotnet build ${Cuda} && dotnet test ${Cuda} --no-build`,
      {
        normalized: `dotnet build ${Cuda} ; dotnet test ${Cuda} --no-build`,
        shape: "build:App.Cuda&test:App.Cuda:nb",
        kind: "build&test",
      },
    ],
    [
      "Windows",
      "dotnet msbuild C:\\scratch\\probe\\test.proj -target:Run",
      {
        normalized: "dotnet msbuild C:/scratch/probe/test.proj -target:Run",
        shape: "dotnet:msbuild",
        kind: "dotnet:outside",
      },
    ],
    [
      "Windows",
      "dotnet build ../other/App.csproj",
      { normalized: "dotnet build ../other/App.csproj", shape: "build:App", kind: "build:outside" },
    ],
    [
      "Empty",
      "dotnet build C:\\scratch\\a.csproj",
      { normalized: "dotnet build C:/scratch/a.csproj", shape: "build:a", kind: "build" },
    ],
    ["Windows", "dotnet build $(pwd)/X", { normalized: "", shape: "", kind: "dotnet" }],
    ["Windows", "dotnet build 'x", { normalized: "", shape: "", kind: "dotnet" }],
    ["Empty", `dotnet build ${Cpu}`, { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" }],
    [
      "Main",
      `Push-Location C:\\work\\repo; dotnet build ${Cpu}; Pop-Location`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      `Set-Location -Path C:\\work\\repo; dotnet build ${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      "pushd tests && dotnet build Compiler/App.Cpu && popd",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      "Push-Location tests; $env:CI='true'; dotnet build Compiler/App.Cpu; Pop-Location",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      `Push-Location tests; Pop-Location; dotnet build ${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      "cd /c/work/repo/tests && dotnet build Compiler/App.Cpu",
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "Main",
      "Push-Location tests; dotnet build Compiler/App.Cpu; node x.js",
      { normalized: `dotnet build ${Cpu} ; node tests/x.js`, shape: "build:App.Cpu&node:x", kind: "build&node" },
    ],
    [
      "MainPlaces",
      `dotnet build tmp/worktree/feat-b/${Cpu}`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "MainPlaces",
      `dotnet build ${OtherWt}\\tests\\Compiler\\App.Cpu`,
      { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" },
    ],
    [
      "MainPlaces",
      "dotnet build tmp/probe/Probe.csproj",
      { normalized: "dotnet build tmp/probe/Probe.csproj", shape: "build:Probe", kind: "build:ignored" },
    ],
    [
      "MainPlaces",
      `dotnet build ${ScratchProj}`,
      { normalized: "dotnet build C:/Temp/scratch/Probe.csproj", shape: "build:Probe", kind: "build:outside" },
    ],
    ["MainPlaces", `dotnet build ${Cpu}`, { normalized: `dotnet build ${Cpu}`, shape: "build:App.Cpu", kind: "build" }],
    [
      "MainPlaces",
      "dotnet build D:\\elsewhere\\x.csproj",
      { normalized: "dotnet build D:/elsewhere/x.csproj", shape: "build:x", kind: "build:outside" },
    ],
  ];

  for (const [ctxName, cmd, expected] of table) {
    const ctx = contexts[ctxName] as KeyContext;
    assert.deepStrictEqual(commandKeys(cmd, ctx, host), expected, `characterization ${ctxName}: ${cmd}`);
  }
}

/**
 * Moved dotnet estimate, fallback, and place checks (exact expectations from estimate-checks & estimate-place-checks).
 */
function checkDotnetEstimateAndPlaces(assert: AssertModule, host: PluginHost): void {
  const keysOf = (command: string, context: KeyContext = Windows) => commandKeys(command, context, host);

  // Moved from estimate-checks.ts (normalize & shape)
  const plain = keysOf(`dotnet build ${Cpu}`).normalized;
  assert.strictEqual(plain, `dotnet build ${Cpu}`);
  assert.strictEqual(
    keysOf(`dotnet test ${Cpu} --no-build -c Release`).normalized,
    keysOf(`dotnet test -c Release --no-build ${Cpu}`).normalized,
  );
  assert.notStrictEqual(keysOf(`dotnet build ${Cuda}`).normalized, plain);
  assert.notStrictEqual(keysOf(`dotnet build ${Cpu} -c Release`).normalized, plain);
  assert.notStrictEqual(keysOf(`dotnet test ${Cpu} --no-build`).normalized, keysOf(`dotnet test ${Cpu}`).normalized);
  assert.notStrictEqual(
    keysOf(`dotnet test ${Cpu} --filter "A|B"`).normalized,
    keysOf(`dotnet test ${Cpu} --filter "A|C"`).normalized,
  );
  assert.strictEqual(
    keysOf(`dotnet test ${Cpu} --filter "A|B"`).shape,
    keysOf(`dotnet test ${Cpu} --filter "A|C"`).shape,
  );
  assert.strictEqual(keysOf(`dotnet build ${Cpu} -c Release`).shape, "build:App.Cpu");

  // Core without plugins falls back to generic dotnet keys
  assert.deepStrictEqual(commandKeys(`dotnet build ${Cpu} -c Release`, Windows), {
    normalized: `dotnet build ${Cpu} -c Release`,
    shape: "dotnet",
    kind: "dotnet",
  });
  assert.deepStrictEqual(commandKeys(`dotnet build ${Cpu}`, Windows), {
    normalized: `dotnet build ${Cpu}`,
    shape: "dotnet",
    kind: "dotnet",
  });
  assert.deepStrictEqual(commandKeys(`dotnet test ${Cpu} -c Release`, Windows), {
    normalized: `dotnet test ${Cpu} -c Release`,
    shape: "dotnet",
    kind: "dotnet",
  });
  assert.deepStrictEqual(commandKeys("dotnet run --project tools/x", Windows), {
    normalized: "dotnet run --project tools/x",
    shape: "dotnet",
    kind: "dotnet",
  });

  // Moved from estimate-place-checks.ts (pathArguments)
  assert.deepStrictEqual(pathArguments("Push-Location tests; dotnet build Compiler/App.Cpu; node x.js", Main, host), [
    "C:\\work\\repo\\tests\\Compiler\\App.Cpu",
    "C:\\work\\repo\\tests\\x.js",
  ]);
  assert.deepStrictEqual(pathArguments("dotnet build $(pwd)/X", Main, host), []);

  // Moved from estimate-checks.ts (checkEstimateFallback with dotnet keys)
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dotnet-est-"));
  try {
    const file = path.join(dir, "history.json");
    const history = new RunHistory(file, () => {});
    const sec = (n: number): number => n * 1000;
    const rec = (command: string, ms: number): void =>
      history.record(command, ms, undefined, 1, Windows, commandKeys(command, Windows, host));
    const est = (command: string) =>
      history.estimateWithFallback(command, Windows, commandKeys(command, Windows, host));
    rec(`dotnet build ${Cpu} > a.log 2>&1`, sec(200));
    rec(`dotnet build ${Cpu} -m:4 > b.log`, sec(300));
    rec(`dotnet build ${Cuda} > c.log`, sec(90));
    rec(`dotnet test ${Cpu} --no-build --filter A`, sec(10));
    assert.strictEqual(est(`dotnet build ${Cpu} > a.log 2>&1`)?.from, "exact");
    assert.strictEqual(est(`dotnet build ${Cpu} > a.log 2>&1`)?.typicalMs, sec(200));
    const normalized = est(`dotnet build ${Cpu} -nodeReuse:false > other.log`);
    assert.strictEqual(normalized?.from, "normalized");
    assert.strictEqual(normalized?.typicalMs, sec(250));
    assert.strictEqual(normalized?.longestMs, sec(250));
    assert.strictEqual(est(`dotnet test ${Cpu} --no-build --filter B`)?.from, "shape");
    assert.strictEqual(est(`dotnet test ${Cpu} --no-build --filter B`)?.typicalMs, sec(10));
    const kind = est("dotnet build tests/Compiler/App.Integration");
    assert.strictEqual(kind?.from, "kind");
    assert.strictEqual(kind?.typicalMs, sec(200));
    assert.strictEqual(est("dotnet build C:\\scratch\\probe.csproj"), null);
    rec("dotnet build C:/work/repo/wt/feat-a/tests/Compiler/App.Other", sec(70));
    const relative = est("dotnet build tests/Compiler/App.Other -m:2");
    assert.strictEqual(relative?.from, "normalized");
    assert.strictEqual(relative?.typicalMs, sec(70));
    assert.strictEqual(
      new RunHistory(file, () => {}).estimateWithFallback(
        "dotnet build ./tests/Compiler/App.Other",
        Windows,
        commandKeys("dotnet build ./tests/Compiler/App.Other", Windows, host),
      )?.from,
      "normalized",
    );
    rec(`dotnet test ${Cuda} --no-build --filter A`, sec(40));
    assert.strictEqual(est(`dotnet test ${Cuda} --no-build --filter Z`)?.from, "shape");
    assert.strictEqual(est(`dotnet test ${Cuda} --no-build --filter Z`)?.typicalMs, sec(40));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }

  // Moved from estimate-place-checks.ts (checkPathPlaces)
  const top = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dotnet-places-")));
  const git = (cwd: string, ...args: string[]): void => {
    const result = spawnSync("git", args, { cwd, windowsHide: true });
    assert.strictEqual(result.status, 0, `git ${args.join(" ")}`);
  };
  try {
    const repo = path.join(top, "repo");
    const nested = path.join(repo, "tmp", "wt");
    const outside = path.join(top, "plain");
    for (const directory of [path.join(repo, "src", "App"), path.join(nested, "src", "App"), outside]) {
      fs.mkdirSync(directory, { recursive: true });
    }
    git(repo, "init", "-q");
    fs.writeFileSync(path.join(repo, ".gitignore"), "tmp/\n");
    git(nested, "init", "-q");
    const context: KeyContext = { cwd: repo, worktree: repo };
    const command =
      `dotnet build src/App && dotnet build tmp/probe/Probe.csproj && dotnet build ${outside}/Probe.csproj` +
      " && dotnet build tmp/wt/src/App";
    const places = resolvePlaces(command, context, host);
    const at = (relative: string): string => path.resolve(repo, relative);
    assert.deepStrictEqual(places[at("src/App")], { root: repo, ignored: false }, "tracked folder");
    assert.deepStrictEqual(places[at("tmp/probe/Probe.csproj")], { root: repo, ignored: true }, "ignored, not there");
    assert.deepStrictEqual(places[at(`${outside}/Probe.csproj`)], { root: "", ignored: false }, "no worktree");
    assert.deepStrictEqual(places[at("tmp/wt/src/App")], { root: nested, ignored: false }, "its own worktree");
    const named = resolvePlaces("dotnet build tmp/試験/P.csproj", context, host);
    assert.deepStrictEqual(named[at("tmp/試験/P.csproj")], { root: repo, ignored: true }, "a name git would quote");
    const kinds = commandKeys(command, { ...context, places }, host).kind;
    assert.strictEqual(kinds, "build&build:ignored&build:outside&build");
  } finally {
    fs.rmSync(top, { recursive: true, force: true });
  }
}

/** Moved from self-test-build-split.ts (all 42 cases) through PluginHost.splitBeforeTurn. */
function checkSplitBeforeTurn(assert: AssertModule, host: PluginHost): void {
  const gpu = { name: "gpu", forcedExclusive: false };
  const planned: [ShellName, string, string, string][] = [
    ["bash", `dotnet test ${CudaFull}`, `dotnet build ${CudaFull}`, `dotnet test --no-build ${CudaFull}`],
    [
      "bash",
      `dotnet test ${CudaFull} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash -nodeReuse:false`,
      `dotnet build ${CudaFull} -c Release -f net10.0 -p:X=1 -nodeReuse:false`,
      `dotnet test --no-build ${CudaFull} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash ` +
        "-nodeReuse:false",
    ],
    [
      "bash",
      `dotnet build ${CudaFull} -nodeReuse:false && dotnet test ${CudaFull} --no-build --filter X`,
      `dotnet build ${CudaFull} -nodeReuse:false`,
      `dotnet test ${CudaFull} --no-build --filter X`,
    ],
    [
      "bash",
      `dotnet build A && dotnet test ${CudaFull} --configuration=Debug -- RunConfiguration.X=1`,
      `dotnet build A && dotnet build ${CudaFull} --configuration=Debug`,
      `dotnet test --no-build ${CudaFull} --configuration=Debug -- RunConfiguration.X=1`,
    ],
    [
      "pwsh",
      `dotnet test ${CudaFull} --filter "Name~it's" && dotnet test ${CudaFull} -c Release`,
      `dotnet build ${CudaFull} && dotnet build ${CudaFull} -c Release`,
      `dotnet test --no-build ${CudaFull} --filter "Name~it's" && dotnet test --no-build ${CudaFull} -c Release`,
    ],
    [
      "cmd",
      `dotnet test "${CudaFull}" -p:Y=%Z%`,
      `dotnet build "${CudaFull}" -p:Y=%Z%`,
      `dotnet test --no-build "${CudaFull}" -p:Y=%Z%`,
    ],
    [
      "bash",
      `dotnet build ${CudaFull} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1 && ` +
        `DiffEngine_Disabled=true dotnet test ${CudaFull} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
      `dotnet build ${CudaFull} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1`,
      `DiffEngine_Disabled=true dotnet test ${CudaFull} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
    ],
    [
      "bash",
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test ${CudaFull} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet build ${CudaFull} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test --no-build ${CudaFull} -v:q -clp:ErrorsOnly ` +
        `&>> "tmp/t.log"`,
    ],
    [
      "bash",
      `dotnet test ${CudaFull} --filter X >> tmp/a.log 2> tmp/err.log`,
      `dotnet build ${CudaFull} >> tmp/a.log 2> tmp/err.log`,
      `dotnet test --no-build ${CudaFull} --filter X >> tmp/a.log 2>> tmp/err.log`,
    ],
    [
      "pwsh",
      `$env:DiffEngine_Disabled='true'; dotnet build ${CudaFull} *> tmp/b.log && ` +
        `dotnet test ${CudaFull} --no-build 2>&1 > x.log`,
      `$env:DiffEngine_Disabled='true'; dotnet build ${CudaFull} *> tmp/b.log`,
      `$env:DiffEngine_Disabled='true'; dotnet test ${CudaFull} --no-build 2>&1 > x.log`,
    ],
    [
      "pwsh",
      `dotnet test ${CudaFull} -c Release *> 'tmp/t.log'`,
      `dotnet build ${CudaFull} -c Release *> 'tmp/t.log'`,
      `dotnet test --no-build ${CudaFull} -c Release *>> 'tmp/t.log'`,
    ],
    [
      "cmd",
      `dotnet test ${CudaFull} > t.log 2>&1`,
      `dotnet build ${CudaFull} > t.log 2>&1`,
      `dotnet test --no-build ${CudaFull} >> t.log 2>&1`,
    ],
  ];
  for (const [shell, command, before, after] of planned) {
    assert.deepStrictEqual(
      host.splitBeforeTurn(command, gpu, clientContext(shell)),
      { before, after, beforeRole: "build, no GPU", afterRole: "tests, GPU turn" },
      `${shell}: ${command}`,
    );
  }
  const kept: [ShellName, string][] = [
    ["bash", `dotnet test ${CudaFull} --no-build`],
    ["bash", "dotnet test"],
    ["bash", "dotnet test --filter X"],
    ["bash", "dotnet test bin/x.Tests.dll"],
    ["bash", `dotnet test ${CudaFull} --unknown-option value`],
    ["bash", `dotnet test --unknown-switch ${CudaFull}`],
    ["bash", `dotnet test ${CudaFull} | tail -5`],
    ["bash", `dotnet test ${CudaFull}; echo done`],
    ["bash", `dotnet test ${CudaFull} || echo failed`],
    ["bash", `dotnet test $(echo ${CudaFull})`],
    ["bash", `dotnet test ${CudaFull} < input.txt`],
    ["bash", `dotnet test ${CudaFull} > log.txt | tee x`],
    ["bash", `dotnet test ${CudaFull} > $(date).log`],
    ["bash", `dotnet test ${CudaFull} >&2`],
    ["bash", `dotnet test ${CudaFull} > log.txt &`],
    ["bash", `cd x && dotnet test ${CudaFull}`],
    ["bash", `FOO=$(pwd) dotnet test ${CudaFull}`],
    ["bash", `echo x; dotnet test ${CudaFull}`],
    ["bash", `export A=$(pwd); dotnet test ${CudaFull}`],
    ["pwsh", `dotnet test ${CudaFull} | Out-File x`],
    ["pwsh", `$env:A = $b; dotnet test ${CudaFull}`],
    ["pwsh", `FOO=1 dotnet test ${CudaFull}`],
    ["bash", `dotnet test ${CudaFull} && dotnet build A`],
    ["bash", `dotnet test ${CudaFull} -p:X="a\\"b"`],
    ["bash", `dotnet test ${CudaFull}\ndotnet build A`],
    ["bash", "just test"],
    ["bash", "dotnet build A"],
    ["pwsh", `dotnet test ${CudaFull} --% -p:X=1`],
    ["pwsh", `dotnet test ${CudaFull} -p:X=$y`],
    ["pwsh", `& dotnet test ${CudaFull}`],
  ];
  for (const [shell, command] of kept) {
    assert.strictEqual(
      host.splitBeforeTurn(command, gpu, clientContext(shell)),
      undefined,
      `kept as it is in ${shell}: ${command}`,
    );
  }
  assert.strictEqual(
    host.splitBeforeTurn(`dotnet test ${CudaFull}`, { name: "cpu", forcedExclusive: false }, clientContext("bash")),
    undefined,
    "non-gpu resource is not split",
  );
}

/** Moved from self-test.ts (10 limitMsbuildNodes assertions) through PluginHost.prepareCommand. */
function checkPrepareCommand(assert: AssertModule, host: PluginHost): void {
  const ctx = clientContext();
  const cases: [string, string][] = [
    ["dotnet test X.csproj && dotnet build", "dotnet test -m:6 X.csproj && dotnet build -m:6"],
    ["dotnet build-server shutdown", "dotnet build-server shutdown"],
    ["dotnet test -m:2", "dotnet test -m:2"],
    ["dotnet build -m:2 && dotnet test", "dotnet build -m:2 && dotnet test -m:6"],
    ["dotnet build --maxcpucount 4", "dotnet build --maxcpucount 4"],
    ["dotnet.exe build", "dotnet.exe build -m:6"],
    ['"C:\\Program Files\\dotnet\\dotnet.exe" test x', '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x'],
    ["dotnet run --project x", "dotnet run --project x"],
    ['dotnet test --filter "A|B" -m:2', 'dotnet test --filter "A|B" -m:2'],
    ["dotnet test bin/x.Tests.dll", "dotnet test bin/x.Tests.dll"],
  ];
  for (const [input, expected] of cases) {
    const prepared = host.prepareCommand(input, { jobId: 1, cpus: 6, nested: false }, ctx);
    assert.strictEqual(prepared.command, expected, input);
    assert.strictEqual(prepared.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:6");
    assert.strictEqual(prepared.note, "6 MSBuild nodes");
    // RequestCores follows the grant with or without -m, build/test/run alike: nodes executing + cores granted <= 6
    assert.strictEqual(prepared.env.MSBUILDCORELIMIT, "6", input);
    assert.strictEqual(prepared.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100", input);
  }
  // A value the caller exported is kept; the other one is still set
  const exported = { ...ctx, env: { MSBUILDCORELIMIT: "2" } };
  const kept = host.prepareCommand("dotnet build X", { jobId: 1, cpus: 6, nested: false }, exported);
  assert.strictEqual(kept.env.MSBUILDCORELIMIT, undefined, "exported MSBUILDCORELIMIT is not overridden");
  assert.strictEqual(kept.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100");
  // Any command gets the bound, so a script that starts dotnet itself passes it on
  for (const input of ["node scripts/human/test-stability.js --count 3", "echo hi"]) {
    const other = host.prepareCommand(input, { jobId: 1, cpus: 6, nested: false }, ctx);
    assert.strictEqual(other.env.MSBUILDCORELIMIT, "6", input);
    assert.strictEqual(other.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100", input);
  }
  const zero = host.prepareCommand("dotnet build X", { jobId: 1, cpus: 0, nested: false }, ctx);
  assert.strictEqual(zero.command, "dotnet build X");
  assert.strictEqual(zero.note, undefined);
  assert.strictEqual(zero.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, undefined);
  assert.strictEqual(zero.env.MSBUILDCORELIMIT, undefined);
}

/** QueuedRun + FakeScheduler + FakeHost: request/adopt keys, MSBuild args/note, and JOB_QUEUE_PLUGINS=0 omission. */
async function checkQueuedRunKeysAndEscape(assert: AssertModule, host: PluginHost): Promise<void> {
  const command = `dotnet build ${Cpu} -c Release`;
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  const prevMsbuildArgs = process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS;
  delete process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS;
  try {
    // 1. With dotnet plugin active: request sends keys, start adds -m / MSBUILD_ARGS / note, adopt repeats keys
    const fakeHost = new FakeHost();
    const notices: string[] = [];
    const write = process.stderr.write;
    process.stderr.write = ((chunk: string | Uint8Array) =>
      notices.push(String(chunk)) > 0) as typeof process.stderr.write;
    try {
      void new QueuedRun(
        baseRunOptions(command),
        { weight: 1, pin: null, before: null },
        false,
        false,
        fakeHost,
        host,
      ).run();
      await until("dotnet client connects", () => fakeHost.connects.length === 1);
      fakeHost.connects[0]?.resolve(await scheduler.connect());
      await until("scheduler accepts dotnet client", () => scheduler.accepted.length === 1);
      const first = scheduler.accepted[0];
      assert.ok(first !== undefined);
      await until("request arrives", () => first.messages.some((m) => m.type === "request"));
      const req = first.messages.find((m) => m.type === "request");
      const cwd = process.cwd();
      const worktree = resolveWorktree(cwd);
      const places = resolvePlaces(command, { cwd, worktree }, host);
      const expectedKeys = commandKeys(command, { cwd, worktree, places }, host);
      assert.deepStrictEqual(req?.keys, expectedKeys, "request carries plugin-computed keys");

      FakeScheduler.send(first, { type: "queued", id: 10, state: "waiting", ahead: 0, position: 1 });
      FakeScheduler.send(first, { type: "granted", id: 10, cpus: 3, capacity: 4, protocol: ProtocolVersion });
      await until("granted command starts", () => fakeHost.commands.length === 1);
      assert.strictEqual(fakeHost.commands[0]?.command, `dotnet build -m:3 ${Cpu} -c Release`);
      assert.strictEqual(fakeHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:3");
      assert.strictEqual(fakeHost.commands[0]?.env.MSBUILDCORELIMIT, "3");
      assert.ok(
        notices.some((n) => n.includes("(weight 1, 3 MSBuild nodes)")),
        "grant line uses MSBuild nodes note",
      );

      // Reconnect after scheduler restart -> adopt carries identical keys
      first.socket.destroy();
      await until("dotnet client reconnects", () => fakeHost.connects.length === 2);
      fakeHost.connects[1]?.resolve(await scheduler.connect());
      await until("scheduler accepts reconnect", () => scheduler.accepted.length === 2);
      const second = scheduler.accepted[1];
      assert.ok(second !== undefined);
      await until("adopt arrives", () => second.messages.some((m) => m.type === "adopt"));
      const adopt = second.messages.find((m) => m.type === "adopt");
      assert.deepStrictEqual(adopt?.keys, req?.keys, "adopt sends identical keys as request");

      fakeHost.commands[0]?.end({ code: 0, signaled: false });
      await until("done arrives", () => second.messages.some((m) => m.type === "done"));
      FakeScheduler.send(second, { type: "done-ok", id: 10 });
      await until("dotnet client exits", () => fakeHost.exits.length > 0);
      assert.deepStrictEqual(fakeHost.exits, [0]);
    } finally {
      process.stderr.write = write;
    }

    // 2. JOB_QUEUE_PLUGINS=0: request & adopt omit keys, command has no -m, env omits MSBUILD_ARGS, note uses cpus
    const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
    const disabledHost = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
      ...process.env,
      JOB_QUEUE_PLUGINS: "0",
    });
    assert.strictEqual(disabledHost.plugins.length, 0);
    const noPlugHost = new FakeHost();
    const noPlugNotices: string[] = [];
    const write2 = process.stderr.write;
    process.stderr.write = ((chunk: string | Uint8Array) =>
      noPlugNotices.push(String(chunk)) > 0) as typeof process.stderr.write;
    try {
      void new QueuedRun(
        baseRunOptions(command),
        { weight: 1, pin: null, before: null },
        false,
        false,
        noPlugHost,
        disabledHost,
      ).run();
      await until("disabled client connects", () => noPlugHost.connects.length === 1);
      noPlugHost.connects[0]?.resolve(await scheduler.connect());
      await until("scheduler accepts disabled client", () => scheduler.accepted.length === 3);
      const third = scheduler.accepted[2];
      assert.ok(third !== undefined);
      await until("disabled request arrives", () => third.messages.some((m) => m.type === "request"));
      const req = third.messages.find((m) => m.type === "request");
      assert.strictEqual(req !== undefined && "keys" in req, false, "JOB_QUEUE_PLUGINS=0 omits keys from request");

      FakeScheduler.send(third, { type: "queued", id: 11, state: "waiting", ahead: 0, position: 1 });
      FakeScheduler.send(third, { type: "granted", id: 11, cpus: 3, capacity: 4, protocol: ProtocolVersion });
      await until("disabled command starts", () => noPlugHost.commands.length === 1);
      assert.strictEqual(noPlugHost.commands[0]?.command, command, "JOB_QUEUE_PLUGINS=0 leaves -m out");
      assert.strictEqual(noPlugHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, undefined);
      assert.strictEqual(noPlugHost.commands[0]?.env.MSBUILDCORELIMIT, undefined);
      assert.ok(
        noPlugNotices.some((n) => n.includes("(weight 1, 3 cpus)")),
        "disabled grant line falls back to cpus",
      );

      third.socket.destroy();
      await until("disabled client reconnects", () => noPlugHost.connects.length === 2);
      noPlugHost.connects[1]?.resolve(await scheduler.connect());
      await until("scheduler accepts disabled reconnect", () => scheduler.accepted.length === 4);
      const fourth = scheduler.accepted[3];
      assert.ok(fourth !== undefined);
      await until("disabled adopt arrives", () => fourth.messages.some((m) => m.type === "adopt"));
      const adopt = fourth.messages.find((m) => m.type === "adopt");
      assert.strictEqual(adopt !== undefined && "keys" in adopt, false, "JOB_QUEUE_PLUGINS=0 omits keys from adopt");

      noPlugHost.commands[0]?.end({ code: 0, signaled: false });
      await until("disabled done arrives", () => fourth.messages.some((m) => m.type === "done"));
      FakeScheduler.send(fourth, { type: "done-ok", id: 11 });
      await until("disabled client exits", () => noPlugHost.exits.length > 0);
      assert.deepStrictEqual(noPlugHost.exits, [0]);
    } finally {
      process.stderr.write = write2;
    }

    // 3. Nested runBuildStep applies prepareCommand when JobVariable and CpusVariable are set
    const prevJob = process.env[JobVariable];
    const prevCpus = process.env[CpusVariable];
    process.env[JobVariable] = "99";
    process.env[CpusVariable] = "4";
    try {
      const nestedHost = new FakeHost();
      const pending = runBuildStep(
        baseRunOptions("dotnet build P"),
        { weight: 1, pin: null, before: null },
        "dotnet build P",
        nestedHost,
        "build, no GPU",
        host,
      );
      await until("nested build starts", () => nestedHost.commands.length === 1);
      assert.strictEqual(nestedHost.commands[0]?.command, "dotnet build -m:4 P");
      assert.strictEqual(nestedHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:4");
      assert.strictEqual(nestedHost.commands[0]?.env.MSBUILDCORELIMIT, "4");
      nestedHost.commands[0]?.end({ code: 0, signaled: false });
      assert.strictEqual(await pending, 0);
    } finally {
      if (prevJob === undefined) {
        delete process.env[JobVariable];
      } else {
        process.env[JobVariable] = prevJob;
      }
      if (prevCpus === undefined) {
        delete process.env[CpusVariable];
      } else {
        process.env[CpusVariable] = prevCpus;
      }
    }
  } finally {
    if (prevMsbuildArgs !== undefined) {
      process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS = prevMsbuildArgs;
    }
    scheduler.close();
  }
}

/** Unit checks for classify, shortName, and api ({ isDotnetTest, selectsCategory }). */
function checkClassifyShortNameAndApi(assert: AssertModule): void {
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet test App.csproj"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet vstest App.dll"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet tool run dotnet-stryker"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify('"C:\\dotnet\\dotnet.exe" test App'), { test: true });
  assert.strictEqual(dotnetPlugin.classify("dotnet build App.csproj"), undefined);
  assert.strictEqual(dotnetPlugin.classify("just test"), undefined);

  assert.strictEqual(
    dotnetPlugin.shortName("dotnet build -nodeReuse:false App", "dotnet build -nodeReuse:false App"),
    "build App",
  );
  assert.strictEqual(
    dotnetPlugin.shortName(
      "dotnet.exe test App -nodeReuse:false && dotnet build B",
      "dotnet.exe test App -nodeReuse:false && dotnet build B",
    ),
    "test App && build B",
  );
  assert.strictEqual(dotnetPlugin.shortName("dotnet build; dotnet test", "dotnet build; dotnet test"), "build ; test");
  assert.strictEqual(
    dotnetPlugin.shortName("dotnet.exe build; dotnet.exe test", "dotnet.exe build; dotnet.exe test"),
    "build ; test",
  );
  assert.strictEqual(dotnetPlugin.shortName("dotnet build;", "dotnet build;"), "build");
  assert.strictEqual(dotnetPlugin.shortName("just test", "just test"), undefined);
  assert.strictEqual(dotnetPlugin.shortName("dotnet", "dotnet"), undefined);
  assert.strictEqual(dotnetPlugin.shortName("-nodeReuse:false", "-nodeReuse:false"), undefined);
  // Without plugins, core generic stripping keeps dotnet and -nodeReuse:false
  assert.strictEqual(
    shortCommand("CI=true dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu -nodeReuse:false > a.log"),
    "dotnet test Kassyi.GSharp.Tests.Cpu -nodeReuse:false",
  );

  assert.strictEqual(isDotnetTest("dotnet test X"), true);
  assert.strictEqual(isDotnetTest("dotnet build X"), false);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category=Stress"', ["Stress", "Benchmark"]), true);
  assert.strictEqual(
    selectsCategory('dotnet test --filter "Category!=Stress&Category!=Benchmark"', ["Stress", "Benchmark"]),
    false,
  );
  assert.strictEqual(
    selectsCategory('dotnet test --filter "Category!=Stress|Category=GpuTrap"', ["Stress", "GpuTrap"]),
    true,
  );
  // A negative term drops one word: what follows it (a quote, a dotted rest) still selects, on the side of caution
  assert.strictEqual(selectsCategory('dotnet test --filter "Category!=Foo.Bar"', ["Bar"]), true);
  assert.strictEqual(selectsCategory("dotnet test --filter \"Category!='Stress'\"", ["Stress"]), true);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category!=Foo.Bar|Category=Stress"', ["Stress"]), true);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category=Stressful"', ["Stress"]), false);
  assert.strictEqual(selectsCategory("dotnet test", ["Stress"]), false);
  assert.strictEqual(selectsCategory("dotnet test Stress", []), false);
  // The same reading as the Stress budget of a test run (fixed pre-move characterization baseline)
  const expectedStress: [string, boolean][] = [
    ["Category=Stress", true],
    ["Category!=Stress", false],
    ["Category!~Benchmark|Category=Benchmark", true],
    ["Category != Stress&Category!=Benchmark", false],
    ["Category!='Stress'", true],
    ["Category!=Foo-Stress", true],
    ["FullyQualifiedName~Stressful", false],
    ["Category=Benchmark.Long", true],
  ];
  for (const [filter, expected] of expectedStress) {
    const command = `dotnet test App --filter "${filter}"`;
    assert.strictEqual(
      selectsCategory(command, ["Stress", "Benchmark"]),
      expected,
      `selectsCategory reads ${filter} as the budget does`,
    );
  }
}

function checkRemoteOffload(assert: AssertModule): void {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dotnet-remote-"));
  try {
    const wtA = path.join(tmp, "wt-main");
    const wtB = path.join(tmp, "wt-review");
    fs.mkdirSync(path.join(wtA, ".git"), { recursive: true });
    fs.mkdirSync(path.join(wtB, ".git"), { recursive: true });
    const projB = path.join(wtB, "tests", "App.Tests", "App.Tests.csproj");
    fs.mkdirSync(path.dirname(projB), { recursive: true });
    fs.writeFileSync(projB, "<Project />");

    const dotnetOnlyHost = new PluginHost([dotnetPlugin], [{}], wtA, () => {});
    const rules = dotnetOnlyHost.remoteOffload(clientContext("pwsh", wtA));

    const cmd = `dotnet test ${projB} --disable-build-servers --results-directory ${path.join(wtB, "tmp", "trx")} --filter 'FullyQualifiedName~SmokeTests'`;
    const plan = translateForRemote(cmd, {
      cwd: wtA,
      worktree: wtA,
      shell: "pwsh",
      places: { [projB]: { root: wtB, ignored: false } },
      cpus: 4,
      keepBuildServersWarm: true,
      env: {},
      remoteRules: rules,
    });
    assert.ok(plan !== null, "dotnet plugin translates cross-worktree dotnet test");
    assert.strictEqual(path.resolve(plan.worktree), path.resolve(wtB));
    assert.ok(!plan.bashCommand.includes("--disable-build-servers"), "warm servers strips --disable-build-servers");
    assert.ok(plan.bashCommand.includes("-m:4"), "injects remote -m:4");
    assert.ok(plan.bashCommand.includes("--blame"), "auto-attaches lightweight --blame");
    assert.strictEqual(plan.env.DiffEngine_Disabled, "true");
    assert.strictEqual(plan.env.MSBUILDCORELIMIT, "4");
    assert.strictEqual(plan.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:4");
    assert.strictEqual(plan.note, "4 MSBuild nodes");
    assert.deepStrictEqual(plan.collectPaths, [{ path: "tmp/trx", kind: "dir" }]);

    // Cold server opt-out preserves --disable-build-servers
    const coldPlan = translateForRemote(cmd, {
      cwd: wtA,
      worktree: wtA,
      shell: "pwsh",
      places: { [projB]: { root: wtB, ignored: false } },
      cpus: 4,
      keepBuildServersWarm: false,
      env: {},
      remoteRules: rules,
    });
    assert.ok(coldPlan !== null && coldPlan.bashCommand.includes("--disable-build-servers"));

    // Sync exclusions & failure artifact validation
    const syncRules = { excludePrefixes: rules.syncExcludePrefixes, excludeFindNames: rules.syncExcludeFindNames };
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs", syncRules), true);
    assert.strictEqual(isExcludedSyncPath("TestResults/a.trx", syncRules), true);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.verified.cs", syncRules), false);

    const frame = (rel: string, content: string): Buffer => {
      const body = Buffer.from(content, "utf8");
      return Buffer.concat([Buffer.from(`${rel}\0${body.length}\n`, "utf8"), body]);
    };
    fs.mkdirSync(path.join(wtB, "tests", "Snapshots"), { recursive: true });
    const writeRes = validateAndWriteArtifacts(
      Buffer.concat([
        frame("tests/Snapshots/Snap.Foo.received.txt", "actual"),
        frame("tests/Snapshots/Snap.Foo.verified.txt", "tamper"),
      ]),
      {
        worktree: wtB,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
        failureArtifacts: rules.failureArtifacts,
      },
    );
    assert.deepStrictEqual(writeRes.writtenFiles, ["tests/Snapshots/Snap.Foo.received.txt"]);
    assert.strictEqual(writeRes.rejectedFiles.length, 1);

    // Sequence_*.xml crash parser
    const xml =
      '<TestSequence><Test Name="A.Passed" Completed="True" /><Test Name="B.NativeCrash" Completed="False" /></TestSequence>';
    assert.deepStrictEqual(parseSequenceXmlInProgress(xml), ["B.NativeCrash"]);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  const configWarnings: string[] = [];
  const loaded = loadConfig({
    mode: "status",
    fromDirectory: path.dirname(EntryScript),
    warn: (m) => configWarnings.push(m),
  });
  assert.deepStrictEqual(configWarnings, []);
  const loadWarnings: string[] = [];
  const host = await PluginHost.load(loaded.config, loaded.configRoot, (m) => loadWarnings.push(m), {
    ...process.env,
    JOB_QUEUE_PLUGINS: undefined,
  });
  assert.deepStrictEqual(loadWarnings, []);
  assert.ok(
    host.plugins.some((p) => p.name === "dotnet"),
    "dotnet plugin loaded from repo config",
  );

  checkCharacterization(assert, host);
  checkDotnetEstimateAndPlaces(assert, host);
  checkSplitBeforeTurn(assert, host);
  checkPrepareCommand(assert, host);
  await quietly(() => checkQueuedRunKeysAndEscape(assert, host));
  checkClassifyShortNameAndApi(assert);
  checkRemoteOffload(assert);
}
