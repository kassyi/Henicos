// Remote CPU offload self-tests for the built-in dotnet plugin: cross-worktree translation, -m:<cpus> filter
// preservation, -d/--diag/--test-adapter-path translation, sync exclusions, *.received.* artifacts, and Sequence_*.xml.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { AssertModule } from "../../plugin-api.ts";
import type { PluginHost } from "../../plugin-host.ts";
import { ArtifactCollector, validateAndWriteArtifacts } from "../../remote/artifact-collector.ts";
import { FakeRemoteTransport, makeFrame, makeRemote } from "../../remote/checks-support.ts";
import { translateForRemote } from "../../remote/command-translator.ts";
import { EmptyRemoteRules } from "../../remote/rules.ts";
import { isExcludedSyncPath } from "../../remote/syncer.ts";
import dotnetPlugin from "./plugin.ts";
import { DotnetCrashSummaryRule, parseSequenceXmlInProgress } from "./remote.ts";

export async function checkDotnetRemoteOffload(assert: AssertModule, dotnetOnlyHost: PluginHost): Promise<void> {
  assert.strictEqual(dotnetOnlyHost.plugins[0]?.name, dotnetPlugin.name);
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-dotnet-remote-"));
  try {
    const wtA = path.join(tmp, "wt-main");
    const wtB = path.join(tmp, "wt-review");
    fs.mkdirSync(path.join(wtA, ".git"), { recursive: true });
    fs.mkdirSync(path.join(wtB, ".git"), { recursive: true });
    const projB = path.join(wtB, "tests", "App.Tests", "App.Tests.csproj");
    fs.mkdirSync(path.dirname(projB), { recursive: true });
    fs.writeFileSync(projB, "<Project />");

    const rules = dotnetOnlyHost.remoteOffload({
      cwd: wtA,
      worktree: wtA,
      configRoot: wtA,
      shell: "pwsh",
      env: {},
      log: () => {},
    });

    const resultsDir = path.join(wtB, "tmp", "trx");
    const cmd =
      `dotnet test ${projB} --disable-build-servers ` +
      `--results-directory ${resultsDir} --filter 'FullyQualifiedName~SmokeTests'`;
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

    // -m:<cpus> injection uses parsed AST args so quoted `--filter 'Name~dotnet test'` is not mutated
    const filterPlan = translateForRemote(`dotnet test ${projB} --filter 'Name~dotnet test'`, {
      cwd: wtA,
      worktree: wtA,
      shell: "pwsh",
      places: { [projB]: { root: wtB, ignored: false } },
      cpus: 4,
      keepBuildServersWarm: true,
      env: {},
      remoteRules: rules,
    });
    assert.ok(filterPlan !== null);
    assert.ok(filterPlan.bashCommand.startsWith("dotnet test -m:4 "), "injects -m:4 right after verb");
    assert.ok(filterPlan.bashCommand.includes("'Name~dotnet test'"), "-m:4 must not mutate quoted filter");
    assert.ok(!filterPlan.bashCommand.includes("dotnet test -m:4'"), "no -m:4 inside filter string");

    // -d / --diag / --test-adapter-path translate paths relative to target worktree (and fail closed outside it)
    const diagA = path.join(wtB, "tmp", "diag-a.log");
    const diagB = path.join(wtB, "tmp", "diag-b.log");
    const adapterDir = path.join(wtB, "tools", "adapters");
    const pathOptsCmd = `dotnet test ${projB} -d ${diagA} --diag=${diagB} --test-adapter-path ${adapterDir}`;
    const pathOptsPlan = translateForRemote(pathOptsCmd, {
      cwd: wtA,
      worktree: wtA,
      shell: "pwsh",
      places: { [projB]: { root: wtB, ignored: false } },
      cpus: 4,
      keepBuildServersWarm: true,
      env: {},
      remoteRules: rules,
    });
    assert.ok(pathOptsPlan !== null);
    assert.ok(pathOptsPlan.bashCommand.includes("-d tmp/diag-a.log"));
    assert.ok(pathOptsPlan.bashCommand.includes("--diag=tmp/diag-b.log"));
    assert.ok(pathOptsPlan.bashCommand.includes("--test-adapter-path tools/adapters"));

    const outsideDiag = path.join(wtA, "tmp", "outside.log");
    assert.strictEqual(
      translateForRemote(`dotnet test ${projB} -d ${outsideDiag}`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [projB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
        remoteRules: rules,
      }),
      null,
      "-d pointing to another worktree fails closed",
    );

    // Sync exclusions & failure artifact validation
    const syncRules = { excludePrefixes: rules.syncExcludePrefixes, excludeFindNames: rules.syncExcludeFindNames };
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs", syncRules), true);
    assert.strictEqual(isExcludedSyncPath("TestResults/a.trx", syncRules), true);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.verified.cs", syncRules), false);

    fs.mkdirSync(path.join(wtB, "tests", "Snapshots"), { recursive: true });
    const writeRes = validateAndWriteArtifacts(
      Buffer.concat([
        makeFrame("tests/Snapshots/Snap.Foo.received.txt", "actual"),
        makeFrame("tests/Snapshots/Snap.Foo.verified.txt", "tamper"),
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

    // Sequence_*.xml crash attribution & DotnetCrashSummaryRule path pattern
    const seqPath = "tests/Compiler/Kassyi.GSharp.Tests.Cpu/TestResults/abc/Sequence_123.xml";
    assert.strictEqual(DotnetCrashSummaryRule.pathPattern.test(seqPath), true);
    assert.strictEqual(DotnetCrashSummaryRule.pathPattern.test("TestResults/guid/Sequence_1.xml"), true);
    const xml = [
      "<TestSequence>",
      '  <Test Name="A.Passed" Completed="True" />',
      '  <Test Name="B.NativeCrash" Completed="False" />',
      "</TestSequence>",
    ].join("");
    assert.deepStrictEqual(parseSequenceXmlInProgress(xml), ["B.NativeCrash"]);

    const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/repo" }));
    transport.binaryHandler = () => makeFrame(seqPath, xml);
    const collector = new ArtifactCollector(transport, { ...EmptyRemoteRules, ...rules });
    const pulled = await collector.pull(wtB, true, new Set(), []);
    assert.deepStrictEqual(pulled.crashedTests, ["B.NativeCrash"]);
    assert.strictEqual(pulled.written, 0);
    assert.ok(!fs.existsSync(path.join(wtB, "tests", "Compiler")));
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
}
