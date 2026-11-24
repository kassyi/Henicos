// Characterization, estimate key, fallback, and path-place checks for the built-in dotnet plugin.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { commandKeys, pathArguments, type KeyContext } from "../../estimate-key.ts";
import { RunHistory } from "../../history.ts";
import { resolvePlaces } from "../../path-places.ts";
import type { AssertModule, StepKeys } from "../../plugin-api.ts";
import type { PluginHost } from "../../plugin-host.ts";

const Cpu = "tests/Compiler/App.Cpu";
const Cuda = "tests/Compiler/App.Cuda";
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

/** Exact pre-move { normalized, shape, kind } outputs for all dotnet commands tested before the move. */
export function checkCharacterization(assert: AssertModule, host: PluginHost): void {
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

/** Moved dotnet estimate, fallback, and place checks (exact expectations from estimate & place checks). */
export function checkDotnetEstimateAndPlaces(assert: AssertModule, host: PluginHost): void {
  const keysOf = (command: string, context: KeyContext = Windows) => commandKeys(command, context, host);

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
