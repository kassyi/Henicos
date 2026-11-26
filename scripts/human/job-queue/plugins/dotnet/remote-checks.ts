// Remote CPU offload self-tests for the built-in dotnet plugin: cross-worktree translation, -m:<cpus> filter
// preservation, -d/--diag/--test-adapter-path translation, sync exclusions, *.received.* artifacts, and Sequence_*.xml.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { AssertModule } from "../../plugin-api.ts";
import type { PluginHost } from "../../plugin-host.ts";
import {
  ArtifactCollector,
  QuarantineFolderName,
  removeReadOnlyTree,
  validateAndWriteArtifacts,
} from "../../remote/artifact-collector.ts";
import { FakeRemoteTransport, makeFrame, makeRemote } from "../../remote/checks-support.ts";
import { translateForRemote } from "../../remote/command-translator.ts";
import { EmptyRemoteRules } from "../../remote/rules.ts";
import { isExcludedSyncPath } from "../../remote/syncer.ts";
import dotnetPlugin from "./plugin.ts";
import {
  DotnetCrashSummaryRule,
  DotnetReceivedArtifactRule,
  MaxSequenceXmlBytes,
  parseSequenceXmlInProgress,
} from "./remote.ts";

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
    assert.deepStrictEqual(plan.redirectedResultsDirs, ["tmp/trx"]);

    // A mixed-case Windows leading assignment of an allowlisted default takes its canonical spelling and wins
    const winMixedLead = translateForRemote(`$env:diffengine_disabled='false'; dotnet test ${projB}`, {
      cwd: wtA,
      worktree: wtA,
      shell: "pwsh",
      places: { [projB]: { root: wtB, ignored: false } },
      cpus: 4,
      keepBuildServersWarm: true,
      env: {},
      platform: "win32",
      remoteRules: rules,
    });
    assert.ok(winMixedLead !== null);
    assert.strictEqual(winMixedLead.env.DiffEngine_Disabled, "false");
    assert.strictEqual(winMixedLead.env.diffengine_disabled, undefined);
    assert.strictEqual(winMixedLead.env.MSBUILDCORELIMIT, "4");
    assert.strictEqual(winMixedLead.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100");

    // The core limits are local-only: written in the command (any case) they keep the job local like in the caller env
    for (const lead of ["$env:msbuildcorelimit='2'", "$env:msbuildnodecoreallocationweight='50'"]) {
      assert.strictEqual(
        translateForRemote(`${lead}; dotnet test ${projB}`, {
          cwd: wtA,
          worktree: wtA,
          shell: "pwsh",
          places: { [projB]: { root: wtB, ignored: false } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
          platform: "win32",
          remoteRules: rules,
        }),
        null,
        `${lead} stays local`,
      );
    }

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
    const qDotnet = path.join(tmp, "state", QuarantineFolderName, "700");
    const writeRes = validateAndWriteArtifacts(
      Buffer.concat([
        makeFrame("tests/Snapshots/Snap.Foo.received.txt", "actual"),
        makeFrame("tests/Snapshots/Snap.Foo.received.cs", "// received cs"),
        makeFrame("tests/Snapshots/Snap.Foo.received.c", "// received c"),
        makeFrame("tests/Snapshots/.received.txt", "empty-stem"),
        makeFrame("tests/Snapshots/.hidden.received.cs", "dotfile-received"),
        makeFrame("tests/Snapshots/Snap.Foo.verified.txt", "tamper"),
      ]),
      {
        quarantineDir: qDotnet,
        worktree: wtB,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
        failureArtifacts: rules.failureArtifacts,
      },
    );
    assert.deepStrictEqual(writeRes.writtenFiles, [
      "tests/Snapshots/Snap.Foo.received.txt",
      "tests/Snapshots/Snap.Foo.received.cs",
      "tests/Snapshots/Snap.Foo.received.c",
    ]);
    assert.strictEqual(writeRes.rejectedFiles.length, 3);
    assert.strictEqual(DotnetReceivedArtifactRule.allowPattern.test("tests/A/Foo.received.c"), true);
    assert.strictEqual(DotnetReceivedArtifactRule.allowPattern.test("tests/A/A-B.received.cs"), true);
    assert.strictEqual(DotnetReceivedArtifactRule.allowPattern.test("tests/A/.hidden.received.cs"), false);
    assert.strictEqual(DotnetReceivedArtifactRule.allowPattern.test("tests/Snapshots/.received.txt"), false);
    assert.ok(!fs.existsSync(path.join(wtB, "tests", "Snapshots", "Snap.Foo.received.txt")));
    assert.ok(!fs.existsSync(path.join(wtB, "tests", "Snapshots", "Snap.Foo.received.cs")));
    const receivedCsQuarantined = path.join(qDotnet, "tests", "Snapshots", "Snap.Foo.received.cs");
    assert.strictEqual(fs.readFileSync(receivedCsQuarantined, "utf8"), "// received cs");
    assert.strictEqual(fs.statSync(receivedCsQuarantined).mode & 0o777, 0o444);
    if (process.platform === "win32") {
      assert.strictEqual(
        fs.readFileSync(`${receivedCsQuarantined}:Zone.Identifier`, "utf8"),
        "[ZoneTransfer]\r\nZoneId=3\r\n",
      );
    }

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
    assert.deepStrictEqual(
      parseSequenceXmlInProgress(
        '<TestSequence Name="NotATest" Completed="False" /><Test Name="Open" Completed="False">',
      ),
      [],
      "ignores <TestSequence> and non-self-closing <Test>",
    );
    const redosStart = Date.now();
    assert.deepStrictEqual(parseSequenceXmlInProgress("<Test ".repeat(80000)), []);
    const elapsedMs = Date.now() - redosStart;
    assert.ok(elapsedMs < 500, `parseSequenceXmlInProgress 80000 unclosed tags took ${elapsedMs}ms (< 500ms)`);
    assert.deepStrictEqual(
      parseSequenceXmlInProgress(`<Test Name="Oversized.Crash" Completed="False" />${" ".repeat(MaxSequenceXmlBytes)}`),
      [],
      "rejects Sequence_*.xml exceeding MaxSequenceXmlBytes",
    );

    const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/repo" }));
    transport.binaryHandler = () => makeFrame(seqPath, xml);
    const collector = new ArtifactCollector(transport, { ...EmptyRemoteRules, ...rules });
    const stateDir = path.join(tmp, "state");
    const pulled = await collector.pull(wtB, true, new Set(), [], { stateDir, jobId: 701 });
    assert.deepStrictEqual(pulled.crashedTests, ["B.NativeCrash"]);
    assert.strictEqual(pulled.written, 0);
    assert.ok(!fs.existsSync(path.join(wtB, "tests", "Compiler")));
    assert.ok(!fs.existsSync(pulled.quarantineDir), "no empty quarantine dir when 0 files written");
  } finally {
    removeReadOnlyTree(tmp);
  }
}
