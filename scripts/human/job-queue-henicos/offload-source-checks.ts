// Offload source scan, build freshness, --arch / --os forwarding and list time budget checks for job-queue-henicos.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { AssertModule, ClientContext } from "../job-queue/plugin-api.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import type { ListResult } from "./name-filter.ts";
import {
  checkHenicosOffload,
  MinListMs,
  OffloadCheckMarginMs,
  runStoppableNode,
  type OffloadListLimits,
  type OffloadListRunner,
} from "./offload-probe.ts";
import {
  builtAssemblyTimes,
  directoryCompileItems,
  scanProjectSources,
  type OffloadSourceProbe,
  type SourceScan,
} from "./offload-sources.ts";

const NoMatch: ListResult = {
  exitCode: 0,
  output:
    "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\r\n" +
    "No test matches the given testcase filter `Platform=Windows` in X.dll\r\n",
};

function write(file: string, text: string, mtimeMs?: number): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
  if (mtimeMs !== undefined) {
    fs.utimesSync(file, mtimeMs / 1000, mtimeMs / 1000);
  }
}

function contextAt(cwd: string): ClientContext {
  return { cwd, worktree: cwd, configRoot: cwd, shell: "pwsh", env: {}, options: {}, log: () => {} };
}

/** Source scan of a temporary repository: marker, skipped folders, Directory.Build compile items, refusals, limits. */
function checkSourceScan(assert: AssertModule, repoRoot: string): void {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-offload-sources-"));
  try {
    fs.mkdirSync(path.join(root, ".git"));
    const projDir = path.join(root, "tests", "P");
    const proj = path.join(projDir, "P.csproj");
    write(proj, '<Project><!-- <Compile Include="..\\x.cs" /> --></Project>');
    write(path.join(projDir, "A.cs"), "class A { }");
    write(path.join(projDir, "bin", "Win.cs"), "[Platform(TestPlatform.Windows)]");
    write(path.join(projDir, "OBJ", "Win.cs"), "[Platform(TestPlatform.Windows)]");
    assert.deepStrictEqual(scanProjectSources(proj), { kind: "no-windows-tests" }, "bin / obj are not sources");

    write(path.join(projDir, "Sub", "W.cs"), "[Platform(TestPlatform.Windows)] class W { }", 2_000_000_000_000);
    const marked = scanProjectSources(proj);
    assert.ok(marked.kind === "windows-tests", "a marker in a sub folder counts");
    assert.ok(marked.kind === "windows-tests" && marked.newestInputMs >= 2_000_000_000_000, "newest source time");
    fs.rmSync(path.join(projDir, "Sub"), { recursive: true });

    // Directory.Build.* between the project and the root: compile items resolved and scanned, times counted
    const hook = path.join(root, "tests", "Shared", "Hooks", "Hook.cs");
    write(hook, "[Platform(TestPlatform.Windows)] class Hook { }");
    write(
      path.join(root, "tests", "Directory.Build.props"),
      "<Project><PropertyGroup><_Infra>$(MSBuildThisFileDirectory)Shared\\</_Infra>" +
        "<_Hook>$(_Infra)Hooks\\Hook.cs</_Hook></PropertyGroup>" +
        '<ItemGroup><Compile Include="$(_Hook)" Link="Hook.cs" /></ItemGroup></Project>',
    );
    assert.strictEqual(scanProjectSources(proj).kind, "windows-tests", "Directory.Build.props compile item scanned");
    write(hook, "class Hook { }");
    write(path.join(root, "Directory.Packages.props"), "<Project />", 2_100_000_000_000);
    assert.strictEqual(scanProjectSources(proj).kind, "no-windows-tests");
    write(path.join(projDir, "W.cs"), "TestPlatform.Windows");
    const withProps = scanProjectSources(proj);
    assert.ok(
      withProps.kind === "windows-tests" && withProps.newestInputMs >= 2_100_000_000_000,
      "the root Directory.Packages.props time counts",
    );
    fs.rmSync(path.join(projDir, "W.cs"));
    assert.deepStrictEqual(
      directoryCompileItems(path.join(root, "x.props"), '<Compile Include="$(Unknown)a.cs" />'),
      null,
    );
    assert.deepStrictEqual(directoryCompileItems(path.join(root, "x.props"), '<Compile Include="**\\*.cs" />'), null);
    write(
      path.join(root, "tests", "Directory.Build.targets"),
      '<Project><Compile Include="$(Nope)\\x.cs" /></Project>',
    );
    assert.strictEqual(scanProjectSources(proj).kind, "local", "an unresolved compile item is not judged");
    fs.rmSync(path.join(root, "tests", "Directory.Build.targets"));
    write(path.join(root, "tests", "Directory.Build.targets"), '<Project><Compile Include="Missing.cs" /></Project>');
    assert.strictEqual(scanProjectSources(proj).kind, "local", "a compile item that is not there is not judged");
    fs.rmSync(path.join(root, "tests", "Directory.Build.targets"));

    // Sources from outside the folder named by the project itself
    write(proj, '<Project><ItemGroup><Compile Include="..\\x.cs" /></ItemGroup></Project>');
    assert.strictEqual(scanProjectSources(proj).kind, "local", "Compile Include in the project");
    write(proj, '<Project><Import Project="..\\S\\S.projitems" Label="Shared" /></Project>');
    assert.strictEqual(scanProjectSources(proj).kind, "local", "shared project import");
    write(proj, "<Project />");

    // Limits on files and bytes (A.cs, B.cs and the Hook.cs of tests/Directory.Build.props)
    write(path.join(projDir, "B.cs"), "class B { }");
    assert.strictEqual(scanProjectSources(proj, { files: 3, bytes: 1_000_000 }).kind, "no-windows-tests");
    assert.strictEqual(scanProjectSources(proj, { files: 2, bytes: 1_000_000 }).kind, "local", "file limit");
    assert.strictEqual(scanProjectSources(proj, { files: 10, bytes: 15 }).kind, "local", "byte limit");

    // A link inside the folder (a junction on Windows) is not followed
    const outside = path.join(root, "outside");
    write(path.join(outside, "O.cs"), "class O { }");
    fs.symlinkSync(outside, path.join(projDir, "Linked"), process.platform === "win32" ? "junction" : "dir");
    assert.strictEqual(scanProjectSources(proj).kind, "local", "a link is not followed");
  } finally {
    removeDirectory(root);
  }

  // The walk up stops at the repository root: a Directory.Build.props above it is not this repository's
  const outer = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-offload-outer-"));
  try {
    write(path.join(outer, "Directory.Build.props"), '<Project><Compile Include="$(Elsewhere)x.cs" /></Project>');
    fs.mkdirSync(path.join(outer, "repo", ".git"), { recursive: true });
    write(path.join(outer, "repo", "P", "P.csproj"), "<Project />");
    write(path.join(outer, "repo", "P", "A.cs"), "class A { }");
    assert.strictEqual(scanProjectSources(path.join(outer, "repo", "P", "P.csproj")).kind, "no-windows-tests");
  } finally {
    removeDirectory(outer);
  }

  // The repository itself: the projects that carry Windows-only tests, the ones that do not, a shared project
  const project = (rel: string): string => path.join(repoRoot, rel, `${path.basename(rel)}.csproj`);
  assert.strictEqual(
    scanProjectSources(project("tests/Shared/Kassyi.GSharp.Tests.Infrastructure.Tests")).kind,
    "windows-tests",
  );
  assert.strictEqual(
    scanProjectSources(project("tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests")).kind,
    "no-windows-tests",
  );
  assert.strictEqual(scanProjectSources(project("tests/Compiler/Kassyi.GSharp.Tests.Cpu")).kind, "local");
}

/** The assembly the list reads: by configuration under bin, or under -o / --artifacts-path. */
function checkAssemblyTimes(assert: AssertModule): void {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-offload-dll-"));
  try {
    const proj = path.join(root, "P", "P.csproj");
    write(proj, "<Project />");
    write(path.join(root, "P", "bin", "win", "Debug", "net10.0", "P.dll"), "", 1_000_000_000_000);
    write(path.join(root, "P", "bin", "win", "Release", "net10.0", "P.dll"), "", 1_500_000_000_000);
    write(path.join(root, "out", "P.dll"), "", 1_700_000_000_000);
    const query = { projectFile: proj, config: "debug", output: "", artifactsPath: "" };
    assert.deepStrictEqual(builtAssemblyTimes(query), [1_000_000_000_000]);
    assert.deepStrictEqual(builtAssemblyTimes({ ...query, config: "release" }), [1_500_000_000_000]);
    assert.deepStrictEqual(builtAssemblyTimes({ ...query, output: path.join(root, "out") }), [1_700_000_000_000]);
    assert.deepStrictEqual(builtAssemblyTimes({ ...query, config: "checked" }), []);
  } finally {
    removeDirectory(root);
  }
}

/** checkHenicosOffload: no marker skips the list, a stale or missing build stays local, a fresh one is listed. */
async function checkScanRouting(assert: AssertModule, repoRoot: string): Promise<void> {
  const ctx = contextAt(repoRoot);
  const req = { remoteForced: false, places: {} };
  const proj = "tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests";
  let calls = 0;
  const runner: OffloadListRunner = () => {
    calls++;
    return Promise.resolve(NoMatch);
  };
  const probeOf = (scan: SourceScan, times: number[]): OffloadSourceProbe => ({
    scan: () => scan,
    assemblyTimes: () => times,
  });

  const unmarked = await checkHenicosOffload(
    `dotnet test ${proj} --filter:Name~A`,
    req,
    ctx,
    runner,
    probeOf({ kind: "no-windows-tests" }, []),
  );
  assert.deepStrictEqual(unmarked, { offload: "allow" }, "no marker: no list, no filter check, no build needed");
  assert.strictEqual(calls, 0);

  const stale = await checkHenicosOffload(
    `dotnet test ${proj}`,
    req,
    ctx,
    runner,
    probeOf({ kind: "windows-tests", newestInputMs: 200 }, [300, 100]),
  );
  assert.strictEqual(stale.offload, "local");
  assert.ok(stale.offload === "local" && stale.reason?.startsWith("build output older than sources"));
  const unbuilt = await checkHenicosOffload(
    `dotnet test ${proj}`,
    req,
    ctx,
    runner,
    probeOf({ kind: "windows-tests", newestInputMs: 200 }, []),
  );
  assert.ok(unbuilt.offload === "local" && unbuilt.reason?.startsWith("no built assembly"));
  const unscanned = await checkHenicosOffload(
    `dotnet test ${proj}`,
    req,
    ctx,
    runner,
    probeOf({ kind: "local", reason: "cannot scan sources of X (why)" }, [300]),
  );
  assert.deepStrictEqual(unscanned, { offload: "local", reason: "cannot scan sources of X (why)" });
  assert.strictEqual(calls, 0, "stale, unbuilt and unscanned projects never list");
  const fresh = await checkHenicosOffload(
    `dotnet test ${proj}`,
    req,
    ctx,
    runner,
    probeOf({ kind: "windows-tests", newestInputMs: 200 }, [200, 300]),
  );
  assert.deepStrictEqual(fresh, { offload: "allow" });
  assert.strictEqual(calls, 1, "a fresh build is listed");

  // The real probe on a temporary project: fresh build listed, a source edited after the build stays local
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-offload-route-"));
  try {
    fs.mkdirSync(path.join(root, ".git"));
    write(path.join(root, "P", "P.csproj"), "<Project />", 1_000_000_000_000);
    write(path.join(root, "P", "WinTests.cs"), "[Platform(TestPlatform.Windows)]", 1_000_000_000_000);
    write(path.join(root, "P", "bin", "Debug", "net10.0", "P.dll"), "", 1_100_000_000_000);
    const tempCtx = contextAt(root);
    calls = 0;
    assert.deepStrictEqual(await checkHenicosOffload("dotnet test P", req, tempCtx, runner), { offload: "allow" });
    assert.strictEqual(calls, 1);
    fs.utimesSync(path.join(root, "P", "WinTests.cs"), 1_200_000_000, 1_200_000_000);
    const edited = await checkHenicosOffload("dotnet test P", req, tempCtx, runner);
    assert.deepStrictEqual(edited, { offload: "local", reason: "build output older than sources (P)" });
    assert.strictEqual(calls, 1);
  } finally {
    removeDirectory(root);
  }
}

/** --arch / --os (both spellings) reach the list, which then reads the output of that target. */
async function checkTargetOptions(assert: AssertModule, repoRoot: string): Promise<void> {
  const ctx = contextAt(repoRoot);
  const listed: string[][] = [];
  const runner: OffloadListRunner = (args) => {
    listed.push([...args]);
    return Promise.resolve(NoMatch);
  };
  const marked: OffloadSourceProbe = {
    scan: () => ({ kind: "windows-tests", newestInputMs: 0 }),
    assemblyTimes: () => [1],
  };
  const proj = "tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests";
  const req = { remoteForced: false, places: {} };
  for (const [options, expected] of [
    ["-a x64 --os linux", ["-a", "x64", "--os", "linux"]],
    ["--arch arm64", ["--arch", "arm64"]],
    ["--arch=arm64 --os:win", ["--arch", "arm64", "--os", "win"]],
    ["-a:x86", ["-a", "x86"]],
  ] as const) {
    listed.length = 0;
    assert.deepStrictEqual(await checkHenicosOffload(`dotnet test ${proj} ${options}`, req, ctx, runner, marked), {
      offload: "allow",
    });
    const args = listed[0] ?? [];
    const start = args.indexOf(expected[0]);
    assert.ok(start > 0, `${options}: ${expected[0]} forwarded`);
    assert.deepStrictEqual(args.slice(start, start + expected.length), [...expected], `${options} forwarded`);
  }
}

/** The lists of several test steps share the host's limit; the signal reaches the runner; the runner stops. */
async function checkListBudget(assert: AssertModule, repoRoot: string): Promise<void> {
  const ctx = contextAt(repoRoot);
  const marked: OffloadSourceProbe = {
    scan: () => ({ kind: "windows-tests", newestInputMs: 0 }),
    assemblyTimes: () => [1],
  };
  const seen: OffloadListLimits[] = [];
  const slowRunner: OffloadListRunner = async (_args, _context, limits) => {
    seen.push(limits);
    await new Promise((resolve) => setTimeout(resolve, 700));
    return NoMatch;
  };
  const controller = new AbortController();
  const twoSteps =
    "dotnet test tests/Compiler/Kassyi.GSharp.Compiler.Generators.Tests && dotnet test tests/ZeroAlloc/Kassyi.ZeroAlloc.Linq.Tests";
  const budgetMs = 1_500;
  const res = await checkHenicosOffload(
    twoSteps,
    { remoteForced: false, places: {}, timeoutMs: OffloadCheckMarginMs + budgetMs, signal: controller.signal },
    ctx,
    slowRunner,
    marked,
  );
  assert.deepStrictEqual(res, { offload: "local", reason: "Platform=Windows test list ran out of time" });
  assert.strictEqual(seen.length, 1, "the second list is not started without time left");
  const first = seen[0];
  assert.ok(first !== undefined && first.timeoutMs <= budgetMs && first.timeoutMs > budgetMs - 500, "remaining time");
  assert.strictEqual(first?.signal, controller.signal, "the host's signal reaches the runner");
  assert.ok(MinListMs > budgetMs - 700, "the fixture leaves less than MinListMs for the second step");

  // The runner stops its process on the time limit and on the signal, and does not start on an aborted one
  const sleeper = ["-e", "setTimeout(() => {}, 60000)"];
  let started = performance.now();
  const timedOut = await runStoppableNode(sleeper, repoRoot, process.env, { timeoutMs: 300 });
  assert.ok(timedOut.exitCode !== 0 && performance.now() - started < 20_000, "stopped on the time limit");
  const stopper = new AbortController();
  started = performance.now();
  setTimeout(() => stopper.abort(), 300);
  const aborted = await runStoppableNode(sleeper, repoRoot, process.env, { timeoutMs: 60_000, signal: stopper.signal });
  assert.ok(aborted.exitCode !== 0 && performance.now() - started < 20_000, "stopped on the signal");
  const notStarted = await runStoppableNode(["-e", "process.stdout.write('ran')"], repoRoot, process.env, {
    timeoutMs: 60_000,
    signal: stopper.signal,
  });
  assert.deepStrictEqual(notStarted, { exitCode: null, output: "" }, "an aborted signal starts nothing");
}

export async function checkHenicosOffloadSources(assert: AssertModule, repoRoot: string): Promise<void> {
  checkSourceScan(assert, repoRoot);
  checkAssemblyTimes(assert);
  await checkScanRouting(assert, repoRoot);
  await checkTargetOptions(assert, repoRoot);
  await checkListBudget(assert, repoRoot);
}
