// Unit and offline E2E checks for transparent remote CPU job offload:
// scheduler remote slots & fallback, strict local vs remote history separation, syncer exclusions, SSH allowlist
// & config validation, FakeRemoteTransport E2E (sync -> exit-254 auto-heal retry -> crash attribution -> sentinel
// -> exit 124 -> AbortSignal -> plugin-driven remote-reset), plus merged artifact, translator, and executor checks.

import { spawnSync } from "node:child_process";
import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { loadConfig, loadLocalRemotesConfig, LocalRemotesFileName, validateConfig } from "../config.ts";
import { RunHistory } from "../history.ts";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "../scheduler.ts";
import { gitBashPath } from "../shell.ts";
import { RemoteArtifactChecks } from "./artifact-checks.ts";
import { MaxQuarantineDirs, QuarantineFolderName, removeReadOnlyTree } from "./artifact-collector.ts";
import {
  captureStderr,
  FakeRemoteTransport,
  makeFrame,
  makeJob,
  makeRemote,
  type AssertModule,
} from "./checks-support.ts";
import { findDangerousSshOption } from "./config.ts";
import { buildResetScript, resetWorker } from "./control.ts";
import { RemoteExecutorChecks } from "./executor-checks.ts";
import { buildExecScript, RemoteRunner } from "./runner.ts";
import { RemoteServerChecks } from "./server-checks.ts";
import {
  buildSshArgs,
  fitsRemote,
  isValidRelativeCacheDir,
  isValidRemoteWorkspace,
  RemoteMetadataFiles,
  RemoteWorkspaceMarkerFile,
} from "./spec.ts";
import {
  buildRemoteFindPruneExpr,
  diffManifests,
  formatSha256Manifest,
  isExcludedSyncPath,
  manifestDigest,
  parseRemoteSha256Output,
  WorkspaceSyncer,
} from "./syncer.ts";
import { RemoteTranslatorChecks } from "./translator-checks.ts";

function makeRulesHost(): PluginHost {
  const plugin: QueuePlugin = {
    name: "demo",
    apiVersion: PluginApiVersion,
    remoteOffload: () => ({
      failureArtifacts: [
        {
          searchDirs: ["tests"],
          findName: "*.actual.txt",
          allowPattern: /^tests\/[A-Za-z0-9_./-]+\.actual\.txt$/,
          requireKnownLocalDir: true,
          cleanBeforeRun: true,
        },
      ],
      crashSummaries: [
        {
          searchDirs: ["CrashDir"],
          findName: "crash_*.txt",
          pathPattern: /^CrashDir\/crash_[^/]+\.txt$/,
          cleanBeforeRun: true,
          parse: (content) => content.split(/\r?\n/).filter(Boolean),
        },
      ],
      resetProcessNames: ["demo-runner", "clang++", "g++"],
      resetCleanDirs: ["CrashDir"],
    }),
  };
  return new PluginHost([plugin], [{}], process.cwd(), () => {});
}

export const RemoteChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  remoteSchedulerPrefersRemoteWithoutLocalUsage(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(2, 0, "hrrn", 8192, undefined, [remote]);
    const j1 = makeJob(1, { memoryMb: 2880, offload: "auto" });
    const granted1 = scheduler.enqueue(j1, 2000);
    assert.deepStrictEqual(granted1, [j1]);
    assert.strictEqual(j1.remote, "ubuntu", "first offloadable job goes to remote");
    assert.strictEqual(scheduler.used, 0, "remote job consumes 0 local CPU slots");
    assert.strictEqual(scheduler.memoryUsed, 0, "remote job consumes 0 local memory");

    const j2 = makeJob(2, { memoryMb: 2880, offload: "auto" });
    const granted2 = scheduler.enqueue(j2, 2001);
    assert.deepStrictEqual(granted2, [j2]);
    assert.strictEqual(j2.remote, undefined, "second job spills over to local when remote is full");
    assert.strictEqual(scheduler.used, 1);
    assert.strictEqual(scheduler.memoryUsed, 2880);

    scheduler.detach(j1);
    assert.strictEqual(scheduler.used, 1);
    assert.strictEqual(scheduler.memoryUsed, 2880);
  },

  remoteSchedulerRespectsMemoryCap(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(2, 0, "hrrn", 16384, undefined, [remote]);
    const stress = makeJob(1, { memoryMb: 6720, offload: "auto" });
    const granted = scheduler.enqueue(stress, 2000);
    assert.deepStrictEqual(granted, [stress]);
    assert.strictEqual(stress.remote, undefined, "job exceeding remote memoryMb stays local");
    assert.strictEqual(scheduler.used, 1);
    assert.strictEqual(scheduler.memoryUsed, 6720);
  },

  remoteSchedulerProbeWaitAndOfflineFallback(assert: AssertModule): void {
    const remote = makeRemote({
      slots: 1,
      memoryMb: 6144,
      mode: "prefer-remote",
      probe: { argv: ["ssh", "ubuntu-server.lan", "echo 4096"], min: 1536, timeoutMs: 5000, settleMs: 0 },
    });
    const scheduler = new Scheduler<SchedulerJob>(2, 0, "hrrn", 8192, undefined, [remote]);
    const j1 = makeJob(1, { offload: "auto" });
    assert.deepStrictEqual(scheduler.enqueue(j1, 5000), [], "initial unprobed remote holds job briefly for probe");
    assert.strictEqual(scheduler.waitFor(j1, 5000), "remote");
    assert.deepStrictEqual(scheduler.remoteProbesWanted(5000), ["ubuntu"]);

    scheduler.recordRemoteProbe("ubuntu", { ok: false, value: null, at: 5100, generation: 0, why: "timeout" });
    assert.deepStrictEqual(scheduler.grant(5100), [j1]);
    assert.strictEqual(j1.remote, undefined, "offline remote falls back to local for auto job");

    const forced = makeJob(2, { offload: "force" });
    assert.deepStrictEqual(scheduler.enqueue(forced, 5200), [], "forced remote job never falls back to local");
    assert.strictEqual(scheduler.waitFor(forced, 5200), "remote");

    scheduler.recordRemoteProbe("ubuntu", { ok: true, value: 4096, at: 25000, generation: 0 });
    assert.deepStrictEqual(scheduler.grant(25000), [forced]);
    assert.strictEqual(forced.remote, "ubuntu");
  },

  remoteSchedulerSeparatesLocalAndRemoteEstimates(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(1, 0, "hrrn", 8192, undefined, [remote]);
    const localEst: Estimate = { typicalMs: 10_000, longestMs: 15_000 };
    const remoteEst: Estimate = { typicalMs: 90_000, longestMs: 120_000 };
    const j1 = makeJob(1, { offload: "auto", estimate: localEst, remoteEstimates: new Map([["ubuntu", remoteEst]]) });
    const j2 = makeJob(2, { offload: "never", estimate: localEst, remoteEstimates: new Map([["ubuntu", remoteEst]]) });
    scheduler.enqueue(j1, 1000);
    scheduler.enqueue(j2, 1000);
    assert.strictEqual(j1.remote, "ubuntu");
    assert.strictEqual(j2.remote, undefined);
    assert.strictEqual(Scheduler.estimateOf(j1)?.typicalMs, 90_000, "remote job uses remote estimate");
    assert.strictEqual(Scheduler.estimateOf(j2)?.typicalMs, 10_000, "local job uses local estimate");

    const fc = scheduler.forecast(1000);
    assert.strictEqual(fc.get(j1)?.endAt, 91_000, "forecast uses remote estimate for remote job");
    assert.strictEqual(fc.get(j2)?.endAt, 11_000, "forecast uses local estimate for local job");
  },

  remoteHistoryFilesAreStrictlySeparated(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-remote-hist-"));
    try {
      const localHist = new RunHistory(path.join(tmp, "history.json"), () => {});
      const remoteHist = new RunHistory(
        path.join(tmp, "history-remote-ubuntu.json"),
        () => {},
        path.join(tmp, "accuracy-remote-ubuntu.json"),
      );
      localHist.record("dotnet test A.csproj", 12_000, 1);
      localHist.accuracy.add("exact", 10_000, 12_000);
      remoteHist.record("dotnet test A.csproj", 75_000, 2);
      remoteHist.accuracy.add("exact", 70_000, 75_000);

      assert.strictEqual(localHist.estimate("dotnet test A.csproj")?.typicalMs, 12_000);
      assert.strictEqual(remoteHist.estimate("dotnet test A.csproj")?.typicalMs, 75_000);
      assert.ok(fs.existsSync(path.join(tmp, "accuracy.json")));
      assert.ok(fs.existsSync(path.join(tmp, "accuracy-remote-ubuntu.json")));
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },

  remoteSyncDiffAndExclusions(assert: AssertModule): void {
    const local = { "src/a.cs": "aaa", "src/b.cs": "bbb2", "src/c.cs": "ccc" };
    const remote = { "src/a.cs": "aaa", "src/b.cs": "bbb1", "src/old.cs": "old" };
    const diff = diffManifests(local, remote);
    assert.deepStrictEqual(diff.toUpload, ["src/b.cs", "src/c.cs"]);
    assert.deepStrictEqual(diff.toDelete, ["src/old.cs"]);
    assert.strictEqual(diff.localDigest, manifestDigest(local));

    assert.strictEqual(isExcludedSyncPath("src/Foo/bin/Debug/a.dll"), true);
    assert.strictEqual(isExcludedSyncPath("src/Foo/obj/linux/a.o"), true);
    assert.strictEqual(isExcludedSyncPath(".claude/worktrees/w1/a.cs"), true);
    assert.strictEqual(isExcludedSyncPath("tmp/foo.txt"), true);
    assert.strictEqual(isExcludedSyncPath("src/debug.log"), true);
    assert.strictEqual(isExcludedSyncPath("tmp"), false, "regular root file tmp is not excluded");
    assert.strictEqual(isExcludedSyncPath("scripts/bin"), false, "regular file scripts/bin is not excluded");
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Emit.cs"), false);
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/Linq.cs"), false);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs"), false);

    const customRules = { excludePrefixes: ["target/", "generated/"], excludeFindNames: ["*.snap.new"] };
    assert.strictEqual(isExcludedSyncPath("target/debug/app", customRules), true);
    assert.strictEqual(isExcludedSyncPath("tests/foo.snap.new", customRules), true);
    assert.strictEqual(isExcludedSyncPath("tests/FOO.SNAP.NEW", customRules), true, "case-insensitive glob parity");
    const prune = buildRemoteFindPruneExpr(customRules, [".job-queue-slot.lock"]);
    assert.ok(prune.includes("-type d \\( "));
    assert.ok(prune.includes("-path './target'"));
    assert.ok(prune.includes("-path './generated'"));
    assert.ok(prune.includes("! -iname '*.snap.new'"));
    assert.ok(prune.includes("! -iname '*.log'"));

    const shaText = formatSha256Manifest({ "b.cs": "2".repeat(64), "a.cs": "1".repeat(64) });
    assert.strictEqual(shaText, `${"1".repeat(64)}  ./a.cs\n${"2".repeat(64)}  ./b.cs\n`);

    const parsed = parseRemoteSha256Output(
      `${"a".repeat(64)}  ./src/Emit.cs\n${"b".repeat(64)}  ./tests/bin/x.dll\n${"c".repeat(64)}  ./tests/A.snap.new\n`,
      customRules,
    );
    assert.deepStrictEqual(Object.keys(parsed), ["src/Emit.cs", "tests/bin/x.dll", "tests/A.snap.new"]);
    assert.strictEqual(parsed["src/Emit.cs"], "a".repeat(64));
    assert.deepStrictEqual(diffManifests({ "src/Emit.cs": "a".repeat(64) }, parsed).toDelete, [
      "tests/A.snap.new",
      "tests/bin/x.dll",
    ]);
  },

  async remoteRunnerAndControlOfflineE2E(assert: AssertModule): Promise<void> {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-runner-e2e-"));
    try {
      const wt = path.join(tmp, "wt");
      const stateDir = path.join(tmp, "state");
      fs.mkdirSync(path.join(wt, "tests", "Snap"), { recursive: true });
      fs.mkdirSync(stateDir, { recursive: true });

      const rules = makeRulesHost().remoteOffload({
        cwd: wt,
        worktree: wt,
        configRoot: wt,
        shell: "bash",
        env: {},
        log: () => {},
      });

      // Pure script builders include plugin rules and omit hardcoded .NET/Henicos names
      const execScript = buildExecScript({
        workspace: "~/demo",
        maxJobMinutes: 10,
        plan: { worktree: wt, remoteCwdRel: "", bashCommand: "echo hi", env: { FOO: "1" }, collectPaths: [] },
        pruneExpr: "\\( -path './tmp' \\) -prune -o \\( -type f -print0 \\)",
        expectedDigest: "deadbeef",
        verifyIntegrity: true,
        remoteRules: rules,
        eofToken: "__EOF_TEST__",
      });
      assert.ok(execScript.includes("find -- ./tests -name '*.actual.txt' -delete"));
      assert.ok(execScript.includes("find -- ./CrashDir -name 'crash_*.txt' -delete"));
      assert.ok(execScript.includes("cat -- .job-queue-manifest.digest"));
      assert.ok(execScript.includes(`if [ ! -f "$WS/${RemoteWorkspaceMarkerFile}" ]; then exit 254; fi`));
      assert.ok(execScript.includes("__EOF_TEST__"));

      const resetScript = buildResetScript("~/demo", true, rules);
      assert.ok(resetScript.includes(`if [ -d "$WS" ] && [ -f "$WS/${RemoteWorkspaceMarkerFile}" ]; then`));
      assert.ok(resetScript.includes('find -- "$WS"'));
      assert.ok(resetScript.includes('find -- "$WS"/tests'));
      assert.ok(resetScript.includes('ENVIRON["PROC_REGEX"]'));
      assert.ok(resetScript.includes("demo-runner"));
      assert.ok(resetScript.includes("clang\\+\\+"));
      assert.ok(resetScript.includes("g\\+\\+"));
      assert.ok(resetScript.includes('fuser "$WS/.job-queue-slot.lock"'));
      assert.ok(resetScript.includes("{ matching_pids; lock_pids; }"));
      assert.ok(resetScript.includes('"$WS"/CrashDir'));
      assert.ok(!resetScript.includes("StackLinqEmbedded"), "core reset script has no Henicos hardcode");
      assert.ok(!resetScript.includes("VBCSCompiler"), "core reset script has no dotnet hardcode");
      const killPassIdx = resetScript.indexOf("kill -KILL");
      const secondTargetIdx = resetScript.lastIndexOf("all_target_pids");
      assert.ok(killPassIdx > 0 && secondTargetIdx > 0 && secondTargetIdx < killPassIdx);

      // Offline E2E: sync -> exit 254 (both attempts keep verifyIntegrity=true) -> exit 1 + crash attribution
      const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      let interactiveAttempt = 0;
      transport.interactiveHandler = (script, callbacks) => {
        interactiveAttempt++;
        const startToken = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
        if (interactiveAttempt === 1) {
          return { code: 254, signaled: false };
        }
        if (startToken !== undefined) {
          callbacks.onStdout(Buffer.from(`${startToken}\n`, "utf8"));
        }
        return { code: 1, signaled: false };
      };
      transport.binaryHandler = () =>
        Buffer.concat([
          makeFrame("CrashDir/crash_1.txt", "Suite.AbortedTest\n"),
          makeFrame("tests/Snap/Case.actual.txt", "got-42"),
        ]);

      const runner = new RemoteRunner(transport, stateDir, rules);
      const localManifest = { "tests/Snap/Case.cs": "a".repeat(64) };
      let execRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const stderrLog = await captureStderr(async () => {
        execRes = await runner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
          undefined,
          18739,
        );
        assert.strictEqual(execRes.code, 1);
      });
      assert.strictEqual(interactiveAttempt, 2, "retried once after exit 254 integrity mismatch");
      assert.strictEqual(transport.uploadCalls.length, 2, "initial sync + clean resync after 254");
      assert.ok(
        transport.uploadCalls.every((call) => call.remoteCommand.includes("tar -xmf -")),
        "sync extraction passes -m so extracted files receive current mtime",
      );
      assert.ok(
        transport.uploadCalls.every((call) => call.remoteCommand.includes('mkdir -p -- "$WS"')),
        "sync creates workspace with --",
      );
      assert.ok(
        transport.uploadCalls.every((call) => call.remoteCommand.includes(RemoteWorkspaceMarkerFile)),
        "sync enforces workspace marker file",
      );
      assert.ok(
        transport.interactiveCalls.every((s) => s.includes("sha256sum --quiet -c .job-queue-manifest.sha256")),
        "verifyIntegrity remains true on exit-254 retry",
      );
      const expectedQDir = path.join(stateDir, QuarantineFolderName, "18739");
      const quarantinedCase = path.join(expectedQDir, "tests", "Snap", "Case.actual.txt");
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snap", "Case.actual.txt")), "never writes artifact to worktree");
      assert.strictEqual(fs.readFileSync(quarantinedCase, "utf8"), "got-42");
      assert.strictEqual(fs.statSync(quarantinedCase).mode & 0o777, 0o444);
      if (process.platform === "win32") {
        assert.strictEqual(
          fs.readFileSync(`${quarantinedCase}:Zone.Identifier`, "utf8"),
          "[ZoneTransfer]\r\nZoneId=3\r\n",
        );
      }
      const expectedKb = Math.max(1, Math.round(6 / 1024));
      const summaryLines = stderrLog.trimEnd().split(/\r?\n/);
      assert.ok(
        summaryLines.some(
          (line) =>
            line.includes(`quarantined 1 file(s) (${expectedKb} KB) in ${expectedQDir}`) &&
            !line.includes("--results-directory"),
        ),
        `expected 1-line quarantine summary with (${expectedKb} KB) in stderr, got: ${stderrLog}`,
      );
      assert.deepStrictEqual(execRes?.quarantine, {
        dir: expectedQDir,
        files: 1,
        bytes: 6,
        resultsRedirected: false,
      });
      assert.ok(!fs.existsSync(path.join(wt, "CrashDir")), "crash summary parsed in memory, never written to disk");
      assert.ok(!fs.existsSync(path.join(expectedQDir, "CrashDir")), "crash summary not written to quarantine either");
      assert.ok(
        stderrLog.includes("test host crashed/aborted while running: Suite.AbortedTest"),
        "crash summary actually parsed and logged",
      );

      // --results-directory redirect summary (with files and with 0 files) vs plain redirection (no redirect notice)
      transport.interactiveHandler = (script, callbacks) => {
        const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
        if (token !== undefined) {
          callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
        }
        return { code: 0, signaled: false };
      };
      transport.binaryHandler = () => makeFrame("tmp/trx/run1.trx", "<TestRun />");
      let redirectRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const redirectStderr = await captureStderr(async () => {
        redirectRes = await runner.execute(
          {
            worktree: wt,
            remoteCwdRel: "",
            bashCommand: "dotnet test --results-directory tmp/trx",
            env: {},
            collectPaths: [{ path: "tmp/trx", kind: "dir" }],
            redirectedResultsDirs: ["tmp/trx"],
          },
          () => {},
          localManifest,
          undefined,
          18740,
        );
      });
      const redirectQDir = path.join(stateDir, QuarantineFolderName, "18740");
      const redirectKb = Math.max(1, Math.round("<TestRun />".length / 1024));
      const redirectLines = redirectStderr.trimEnd().split(/\r?\n/);
      assert.ok(
        redirectLines.some((line) =>
          line.includes(
            `quarantined 1 file(s) (${redirectKb} KB) in ${redirectQDir} (--results-directory tmp/trx redirected here, not local)`,
          ),
        ),
        `expected 1-line redirect summary with (${redirectKb} KB), got: ${redirectStderr}`,
      );
      assert.deepStrictEqual(redirectRes?.quarantine, {
        dir: redirectQDir,
        files: 1,
        bytes: "<TestRun />".length,
        resultsRedirected: true,
        resultsDirs: ["tmp/trx"],
      });

      transport.binaryHandler = () => null;
      let zeroRedirectRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const zeroRedirectStderr = await captureStderr(async () => {
        zeroRedirectRes = await runner.execute(
          {
            worktree: wt,
            remoteCwdRel: "",
            bashCommand: "dotnet test --results-directory tmp/trx",
            env: {},
            collectPaths: [{ path: "tmp/trx", kind: "dir" }],
            redirectedResultsDirs: ["tmp/trx"],
          },
          () => {},
          localManifest,
          undefined,
          18741,
        );
      });
      assert.ok(
        zeroRedirectStderr.includes("--results-directory tmp/trx redirected to quarantine") &&
          zeroRedirectStderr.includes("0 file(s) received"),
      );
      assert.deepStrictEqual(zeroRedirectRes?.quarantine, {
        dir: path.join(stateDir, QuarantineFolderName, "18741"),
        files: 0,
        bytes: 0,
        resultsRedirected: true,
        resultsDirs: ["tmp/trx"],
      });

      transport.binaryHandler = () => makeFrame("tmp/out.log", "plain redir");
      let plainRedirRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const plainRedirStderr = await captureStderr(async () => {
        plainRedirRes = await runner.execute(
          {
            worktree: wt,
            remoteCwdRel: "",
            bashCommand: "node scripts/a.js > tmp/out.log",
            env: {},
            collectPaths: [{ path: "tmp/out.log", kind: "file" }],
          },
          () => {},
          localManifest,
          undefined,
          18742,
        );
      });
      assert.ok(!plainRedirStderr.includes("--results-directory"));
      assert.strictEqual(plainRedirRes?.quarantine?.resultsRedirected, false);

      // RemoteRunner.execute invokes quarantine retention pruning after pull (keeping current jobId): with more than
      // MaxQuarantineDirs folders, the ones older than 7 days beyond the newest MaxQuarantineDirs go
      const qRoot = path.join(stateDir, QuarantineFolderName);
      const nowMs = Date.now();
      const oldCount = MaxQuarantineDirs + 10;
      for (let i = 1; i <= oldCount; i++) {
        const d = path.join(qRoot, String(i));
        fs.mkdirSync(d, { recursive: true });
        const f = path.join(d, "old.log");
        fs.writeFileSync(f, "old", { mode: 0o644 });
        fs.chmodSync(f, 0o444);
        const t = new Date(nowMs - 8 * 86_400_000 - (oldCount + 5 - i) * 1000);
        fs.utimesSync(d, t, t);
      }
      transport.binaryHandler = () => makeFrame("tmp/out.log", "fresh");
      await captureStderr(async () => {
        await runner.execute(
          {
            worktree: wt,
            remoteCwdRel: "",
            bashCommand: "node scripts/a.js > tmp/out.log",
            env: {},
            collectPaths: [{ path: "tmp/out.log", kind: "file" }],
          },
          () => {},
          localManifest,
          undefined,
          18743,
        );
      });
      assert.strictEqual(
        fs.readdirSync(qRoot).length,
        MaxQuarantineDirs,
        "RemoteRunner.execute prunes old quarantine dirs to cap",
      );
      assert.ok(fs.existsSync(path.join(qRoot, String(oldCount))), "the newest old folder stays");
      assert.ok(!fs.existsSync(path.join(qRoot, "1")), "oldest quarantine dir was pruned");
      assert.ok(fs.existsSync(path.join(qRoot, "18743", "tmp", "out.log")), "current jobId dir is retained");

      // RemoteRunner.execute under F5.13 (<wt>/tmp -> <wt>/tests): blocks artifact write, skips prune, and omits
      // runResult.quarantine + redirect-success notice even with --results-directory
      const f513Wt = path.join(tmp, "runner-f513-wt");
      const f513KeepFile = path.join(f513Wt, "tests", "job-queue", QuarantineFolderName, "1", "keep.txt");
      fs.mkdirSync(path.join(f513Wt, "tests", "Snapshots"), { recursive: true });
      fs.mkdirSync(path.dirname(f513KeepFile), { recursive: true });
      fs.writeFileSync(f513KeepFile, "keep-runner-f513", { mode: 0o644 });
      fs.chmodSync(f513KeepFile, 0o444);
      const f513OldTime = new Date(Date.now() - 30 * 86_400_000);
      fs.utimesSync(path.join(f513Wt, "tests", "job-queue", QuarantineFolderName, "1"), f513OldTime, f513OldTime);
      fs.symlinkSync(
        path.join(f513Wt, "tests"),
        path.join(f513Wt, "tmp"),
        process.platform === "win32" ? "junction" : "dir",
      );
      const f513StateDir = path.join(f513Wt, "tmp", "job-queue");
      const f513Rules = {
        ...rules,
        failureArtifacts: [
          {
            searchDirs: ["tests"],
            findName: "*.received.*",
            allowPattern: /^tests\/[A-Za-z0-9_./-]+\.received\.cs$/,
            requireKnownLocalDir: true,
            cleanBeforeRun: false,
          },
        ],
      };
      const f513Manifest = { "tests/Snapshots/Foo.cs": "b".repeat(64) };
      const f513Transport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      f513Transport.interactiveHandler = (script, callbacks) => {
        const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
        if (token !== undefined) {
          callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
        }
        return { code: 1, signaled: false };
      };
      f513Transport.binaryHandler = () => makeFrame("tests/Snapshots/Foo.received.cs", "// blocked");
      const f513Runner = new RemoteRunner(f513Transport, f513StateDir, f513Rules);
      let f513ExecRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const f513Stderr = await captureStderr(async () => {
        f513ExecRes = await f513Runner.execute(
          { worktree: f513Wt, remoteCwdRel: "", bashCommand: "dotnet test", env: {}, collectPaths: [] },
          () => {},
          f513Manifest,
          undefined,
          19003,
        );
      });
      assert.strictEqual(f513ExecRes?.code, 1);
      assert.strictEqual(
        f513ExecRes?.quarantine,
        undefined,
        "blocked quarantine must leave execRes.quarantine undefined",
      );
      assert.ok(f513Stderr.includes("symlink/junction"), "logs symlink/junction rejection reason");
      assert.ok(
        !fs.existsSync(path.join(f513Wt, "tests", "job-queue", QuarantineFolderName, "19003")),
        "Foo.received.cs must not be written through wt/tmp junction",
      );
      assert.ok(fs.existsSync(f513KeepFile), "RemoteRunner must not prune wt/tests via wt/tmp junction");

      // Same F5.13 junction with --results-directory tmp/trx (both with files and with 0 files)
      f513Transport.interactiveHandler = (script, callbacks) => {
        const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
        if (token !== undefined) {
          callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
        }
        return { code: 0, signaled: false };
      };
      f513Transport.binaryHandler = () => makeFrame("tmp/trx/run1.trx", "<TestRun />");
      let f513ResultsRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const f513ResultsStderr = await captureStderr(async () => {
        f513ResultsRes = await f513Runner.execute(
          {
            worktree: f513Wt,
            remoteCwdRel: "",
            bashCommand: "dotnet test --results-directory tmp/trx",
            env: {},
            collectPaths: [{ path: "tmp/trx", kind: "dir" }],
            redirectedResultsDirs: ["tmp/trx"],
          },
          () => {},
          f513Manifest,
          undefined,
          19004,
        );
      });
      assert.strictEqual(
        f513ResultsRes?.quarantine,
        undefined,
        "rejected --results-directory quarantine must leave execRes.quarantine undefined",
      );
      assert.ok(!f513ResultsStderr.includes("redirected to quarantine"));
      assert.ok(f513ResultsStderr.includes("could not be quarantined"));
      assert.ok(fs.existsSync(f513KeepFile));

      f513Transport.binaryHandler = () => null;
      let f513ZeroResultsRes: Awaited<ReturnType<RemoteRunner["execute"]>> | undefined;
      const f513ZeroResultsStderr = await captureStderr(async () => {
        f513ZeroResultsRes = await f513Runner.execute(
          {
            worktree: f513Wt,
            remoteCwdRel: "",
            bashCommand: "dotnet test --results-directory tmp/trx",
            env: {},
            collectPaths: [{ path: "tmp/trx", kind: "dir" }],
            redirectedResultsDirs: ["tmp/trx"],
          },
          () => {},
          f513Manifest,
          undefined,
          19005,
        );
      });
      assert.strictEqual(f513ZeroResultsRes?.quarantine, undefined);
      assert.ok(!f513ZeroResultsStderr.includes("redirected to quarantine"));
      assert.ok(f513ZeroResultsStderr.includes("could not be quarantined"));
      assert.ok(fs.existsSync(f513KeepFile));

      // Pre-exec sentinel: exit 255 before start token -> transportFailed; after start token -> not transportFailed
      const sentinelTransport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      const sentinelRunner = new RemoteRunner(sentinelTransport, stateDir, rules);
      sentinelTransport.interactiveHandler = () => ({ code: 255, signaled: false });
      await captureStderr(async () => {
        const beforeStart = await sentinelRunner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
        assert.strictEqual(beforeStart.code, 255);
        assert.strictEqual(beforeStart.transportFailed, true, "255 before sentinel is transport failure");

        sentinelTransport.interactiveHandler = (script, callbacks) => {
          const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
          if (token !== undefined) {
            callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
          }
          return { code: 255, signaled: false };
        };
        const afterStart = await sentinelRunner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
        assert.strictEqual(afterStart.code, 255);
        assert.strictEqual(afterStart.transportFailed, undefined, "255 after sentinel is command exit code");

        // Exit 124 -> timedOut: true (unless signaled)
        sentinelTransport.interactiveHandler = (script, callbacks) => {
          const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
          if (token !== undefined) {
            callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
          }
          return { code: 124, signaled: false };
        };
        const timedOutRes = await sentinelRunner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
        assert.strictEqual(timedOutRes.code, 124);
        assert.strictEqual(timedOutRes.timedOut, true);
      });

      // B2: exit 254 AFTER execStartToken is the command's own exit code (never retried or resynced)
      const uploadsBefore254 = sentinelTransport.uploadCalls.length;
      let postStart254Attempts = 0;
      sentinelTransport.interactiveHandler = (script, callbacks) => {
        postStart254Attempts++;
        if (postStart254Attempts === 1) {
          const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
          if (token !== undefined) {
            callbacks.onStdout(Buffer.from(`${token}\ncommand output before 254\n`, "utf8"));
          }
          return { code: 254, signaled: false };
        }
        return { code: 0, signaled: false };
      };
      let capturedOut = "";
      const origStdoutWrite = process.stdout.write;
      process.stdout.write = ((chunk: string | Uint8Array) => {
        capturedOut += typeof chunk === "string" ? chunk : Buffer.from(chunk).toString("utf8");
        return true;
      }) as typeof process.stdout.write;
      let postStart254Log = "";
      try {
        postStart254Log = await captureStderr(async () => {
          const afterStart254 = await sentinelRunner.execute(
            { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
            () => {},
            localManifest,
          );
          assert.strictEqual(afterStart254.code, 254, "254 after sentinel is command exit code, not retried");
          assert.strictEqual(afterStart254.transportFailed, undefined);
        });
      } finally {
        process.stdout.write = origStdoutWrite;
      }
      assert.strictEqual(postStart254Attempts, 1, "must not retry exit 254 after command started");
      assert.strictEqual(
        sentinelTransport.uploadCalls.length,
        uploadsBefore254,
        "must not resync after post-start 254",
      );
      assert.strictEqual(capturedOut, "command output before 254\n", "forwards command stdout before 254");
      assert.ok(!postStart254Log.includes("workspace integrity mismatch detected"));

      // NB1: crashedTests from remote crash summaries and sync errors are sanitized before terminal stderr output
      sentinelTransport.interactiveHandler = (script, callbacks) => {
        const token = /__JQ_START_[0-9a-f]+__/.exec(script)?.[0];
        if (token !== undefined) {
          callbacks.onStdout(Buffer.from(`${token}\n`, "utf8"));
        }
        return { code: 1, signaled: false };
      };
      sentinelTransport.binaryHandler = () =>
        makeFrame("CrashDir/crash_1.txt", "\x1b]52;c;ZXZpbA==\x07Suite.Evil\x1b[31m\n");
      const crashSanitizedLog = await captureStderr(async () => {
        const crashRes = await sentinelRunner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
        assert.strictEqual(crashRes.code, 1);
      });
      assert.ok(crashSanitizedLog.includes("Suite.Evil"));
      assert.ok(!crashSanitizedLog.includes("\x1b"));
      assert.ok(!crashSanitizedLog.includes("\x07"));
      assert.ok(!crashSanitizedLog.includes("52;c;"));

      const errSyncTransport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      errSyncTransport.uploadHandler = () => ({ code: 1, stderr: "\x1b]52;c;ZXZpbA==\x07sync failed\r\n" });
      const errSyncer = new WorkspaceSyncer(errSyncTransport, {}, stateDir);
      const syncErrLog = await captureStderr(async () => {
        await errSyncer.resyncClean(wt, localManifest, formatSha256Manifest(localManifest));
      });
      assert.ok(syncErrLog.includes("sync failed"));
      assert.ok(!syncErrLog.includes("\x1b") && !syncErrLog.includes("\x07") && !syncErrLog.includes("52;c;"));

      // Offline remote-reset via FakeRemoteTransport clears local manifest cache and parses RESET_OK
      transport.execHandler = () => ({ code: 0, stdout: "RESET_OK killed=3 mem_mb=4096\n", stderr: "" });
      const resetRes = await resetWorker(transport, true, stateDir, rules);
      assert.strictEqual(resetRes.ok, true);
      assert.strictEqual(resetRes.killed, "3");
      assert.strictEqual(resetRes.memMb, "4096");
      assert.ok(!fs.existsSync(path.join(stateDir, "remote-manifest-ubuntu.json")));
    } finally {
      removeReadOnlyTree(tmp);
    }
  },

  remoteConfigValidation(assert: AssertModule): void {
    const valid = validateConfig({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
      remotes: {
        ubuntu: {
          host: "jobrunner@ubuntu-server.lan",
          workspace: "~/henicos",
          slots: 1,
          cpus: 4,
          memoryMb: 6144,
          mode: "prefer-remote",
          maxJobMinutes: 30,
          keepBuildServersWarm: true,
        },
      },
    });
    assert.strictEqual(valid.remotes?.ubuntu?.host, "jobrunner@ubuntu-server.lan");

    const samplePath = path.join(import.meta.dirname, "..", "remotes.local.example.json");
    const sampleTmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-local-rem-"));
    try {
      fs.copyFileSync(samplePath, path.join(sampleTmp, LocalRemotesFileName));
      const loadedSample = loadLocalRemotesConfig(sampleTmp);
      assert.ok(loadedSample?.ubuntu !== undefined, "remotes.local.example.json validates cleanly");
      assert.strictEqual(loadedSample?.ubuntu?.host, "jobrunner@ubuntu-server.lan");

      const repoDir = path.join(sampleTmp, "repo");
      fs.mkdirSync(repoDir, { recursive: true });
      fs.writeFileSync(
        path.join(repoDir, "job-queue.config.json"),
        JSON.stringify({
          version: 1,
          entry: "scripts/human/job-queue.js",
          plugins: [],
          remotes: {
            evil: { host: "attacker.example", workspace: "~/x", cpus: 4, memoryMb: 4096 },
          },
        }),
      );
      const loaded = loadConfig({ mode: "status", fromDirectory: repoDir, stateDir: sampleTmp, warn: () => {} });
      assert.strictEqual(loaded.config.remotes?.evil, undefined, "untrusted repo remotes are ignored");
      assert.strictEqual(loaded.config.remotes?.ubuntu?.host, "jobrunner@ubuntu-server.lan");

      const loadedServe = loadConfig({
        mode: "serve",
        configPath: path.join(repoDir, "job-queue.config.json"),
        stateDir: sampleTmp,
        warn: () => {},
      });
      assert.strictEqual(loadedServe.config.remotes?.evil, undefined, "serve --config still ignores repo remotes");
      assert.strictEqual(loadedServe.config.remotes?.ubuntu?.host, "jobrunner@ubuntu-server.lan");
    } finally {
      fs.rmSync(sampleTmp, { recursive: true, force: true });
    }

    const rejectRemote = (remoteOverrides: Record<string, unknown>, label: string): void => {
      assert.throws(
        () =>
          validateConfig({
            version: 1,
            entry: "scripts/human/job-queue.js",
            plugins: [],
            remotes: {
              ubuntu: {
                host: "ubuntu-server.lan",
                workspace: "~/henicos",
                cpus: 4,
                memoryMb: 6144,
                ...remoteOverrides,
              },
            },
          }),
        `expected invalid remote config to throw: ${label}`,
      );
    };

    rejectRemote({ host: "-oProxyCommand=evil" }, "host leading dash");
    rejectRemote({ workspace: "-rf" }, "workspace leading dash");
    rejectRemote({ workspace: " --x" }, "workspace trimmed leading dash");
    for (const badWorkspace of [
      "~",
      "/",
      "~/",
      "///",
      "~user",
      "~user/",
      "~user/henicos",
      "~/.",
      "~/..",
      "~/./a",
      "~/a/.",
      "~/a/..",
      "~/../etc",
      "/.",
      "/..",
      "/a/..",
      "/tmp",
      "henicos",
      "~/a//b",
      "~/a$b",
      "~/a\\b",
      "~/henicos\n",
      "~/a/-rf",
    ]) {
      assert.strictEqual(isValidRemoteWorkspace(badWorkspace), false, `rejects ${JSON.stringify(badWorkspace)}`);
      rejectRemote({ workspace: badWorkspace }, `workspace ${JSON.stringify(badWorkspace)}`);
    }
    for (const goodWorkspace of [
      "~/henicos",
      "~/sub/henicos",
      "~/my..repo",
      "/srv/henicos",
      "/home/jobrunner/henicos",
    ]) {
      assert.strictEqual(isValidRemoteWorkspace(goodWorkspace), true, `accepts ${goodWorkspace}`);
    }
    for (const badProvisionerWorkspace of [
      "/etc/ssh",
      "/srv/henicos",
      "/home/ubuntu/ws",
      "/home/jobrunner",
      "/var/lib/jobrunner",
      "~/.ssh",
      "/opt/.hidden/ws",
      "/opt/containerd/bin",
      "/srv/ftp/pub",
      "~/henicos\u0085",
    ]) {
      assert.strictEqual(
        isValidRemoteWorkspace(badProvisionerWorkspace, { provisionerRunnerUser: "jobrunner" }),
        false,
        `provisioner mode rejects ${JSON.stringify(badProvisionerWorkspace)}`,
      );
    }
    for (const goodProvisionerWorkspace of [
      "~/henicos",
      "/srv/jobrunner/henicos",
      "/opt/jobrunner/henicos",
      "/home/jobrunner/henicos",
      "/var/lib/jobrunner/henicos",
    ]) {
      assert.strictEqual(
        isValidRemoteWorkspace(goodProvisionerWorkspace, { provisionerRunnerUser: "jobrunner" }),
        true,
        `provisioner mode accepts ${goodProvisionerWorkspace}`,
      );
    }
    for (const badCacheDir of [".", "./foo", "foo//bar", ".ssh", ".gnupg", ".cargo", ".nuget", "sub/.ssh"]) {
      assert.strictEqual(
        isValidRelativeCacheDir(badCacheDir),
        false,
        `rejects cacheDir ${JSON.stringify(badCacheDir)}`,
      );
    }
    for (const goodCacheDir of [".nuget/packages", ".cargo/registry", "caches/nuget"]) {
      assert.strictEqual(isValidRelativeCacheDir(goodCacheDir), true, `accepts cacheDir ${goodCacheDir}`);
    }
    const trimmedConfig = validateConfig({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
      remotes: {
        ubuntu: { host: "ubuntu-server.lan", workspace: "  ~/henicos  ", cpus: 4, memoryMb: 6144 },
      },
    });
    assert.strictEqual(trimmedConfig.remotes?.ubuntu?.workspace, "~/henicos");
    rejectRemote({ slots: 2 }, "slots: 2");
    for (const sshOptions of [
      ["-o", "ProxyCommand=evil"],
      ["-oProxyCommand=evil"],
      ["-o", "StrictHostKeyChecking=no"],
      ["-o", "BatchMode=no"],
      ["-nA"],
      ["-vX"],
      ["-i", "--"],
      ["--"],
      ["-I", "/tmp/evil.so"],
      ["-o", "PKCS11Provider=/tmp/evil.so"],
      ["-o", "ControlPath=/tmp/ctl"],
      ["-o", "Hostname=evil.example"],
      ["-o", "SendEnv=LD_PRELOAD"],
      ["-o", "SetEnv=FOO=bar"],
      ["-o", "UserKnownHostsFile=/dev/null"],
    ]) {
      rejectRemote({ sshOptions }, `sshOptions ${sshOptions.join(" ")}`);
    }
    assert.strictEqual(findDangerousSshOption(["-4", "-q", "-o", "ConnectTimeout=5", "-p", "2222"]), null);

    for (const argv of [
      ["powershell", "-c", "echo 9999"],
      ["ssh", "-o", "ProxyCommand=evil", "ubuntu-server.lan", "echo 1"],
      ["ssh", "-I", "/evil.so", "ubuntu-server.lan", "echo 1"],
      ["ssh", "ubuntu-server.lan", "-e"],
      ["ssh", "ubuntu-server.lan"],
      ["ssh", "-o", "ConnectTimeout=5"],
      ["ssh", "--", "-evil", "echo 1"],
    ]) {
      rejectRemote({ probe: { argv, min: 100 } }, `probe.argv ${argv.join(" ")}`);
    }
    validateConfig({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
      remotes: {
        ubuntu: {
          host: "ubuntu-server.lan",
          workspace: "~/henicos",
          cpus: 4,
          memoryMb: 6144,
          probe: { argv: ["ssh", "-o", "ConnectTimeout=5", "--", "ubuntu-server.lan", "free", "-m"], min: 100 },
        },
      },
    });

    const args = buildSshArgs(makeRemote(), "echo ok");
    assert.ok(args.includes("StrictHostKeyChecking=yes"));
    assert.ok(args.includes("ForwardAgent=no"));
    assert.ok(args.includes("ServerAliveInterval=5"));
    assert.strictEqual(args[args.length - 3], "--");

    const spec = makeRemote();
    assert.strictEqual(fitsRemote(makeJob(1, { gpu: true }), { spec, probe: null }, [], 1000), false);
    assert.strictEqual(fitsRemote(makeJob(1, { gpuTurn: true }), { spec, probe: null }, [], 1000), false);
  },

  async remoteWorkspaceMarkerAndIntegrityScriptChecks(assert: AssertModule): Promise<void> {
    assert.ok(RemoteMetadataFiles.includes(RemoteWorkspaceMarkerFile));
    const pruneWithMarker = buildRemoteFindPruneExpr({}, RemoteMetadataFiles);
    assert.ok(pruneWithMarker.includes(`-path './${RemoteWorkspaceMarkerFile}'`));

    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-marker-integ-"));
    try {
      const wt = path.join(tmp, "wt");
      const stateDir = path.join(tmp, "state");
      fs.mkdirSync(path.join(wt, "tests", "Snap"), { recursive: true });
      fs.mkdirSync(stateDir, { recursive: true });
      const caseContent = "class Case {}";
      const caseSha = crypto.createHash("sha256").update(caseContent, "utf8").digest("hex");
      const localManifest = { "tests/Snap/Case.cs": caseSha };

      const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      let capturedFetchScript = "";
      transport.execHandler = (script) => {
        capturedFetchScript = script;
        return { code: 0, stdout: "", stderr: "" };
      };
      const runner = new RemoteRunner(transport, stateDir);
      await captureStderr(async () => {
        await runner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo hi", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
      });
      assert.ok(capturedFetchScript.includes(RemoteWorkspaceMarkerFile));
      assert.ok(capturedFetchScript.includes('mkdir -p -- "$WS"'));
      const execScript = transport.interactiveCalls[0] ?? "";
      assert.ok(execScript.includes("sha256sum --quiet -c .job-queue-manifest.sha256"));

      const bashBin = gitBashPath();
      const canRunBash =
        (process.platform !== "win32" || bashBin !== "bash") &&
        spawnSync(bashBin, ["-c", "command -v sha256sum find awk cmp sort mktemp tr ls >/dev/null"], {
          windowsHide: true,
        }).status === 0;
      if (!canRunBash) {
        return;
      }

      // B4: Git Bash execution of buildResetScript & fetchRemoteManifest marker guards
      const markerHome = path.join(tmp, "marker-home");
      const unmarkedWs = path.join(markerHome, "demo");
      fs.mkdirSync(path.join(unmarkedWs, "bin"), { recursive: true });
      fs.writeFileSync(path.join(unmarkedWs, ".bashrc"), "# keep\n");
      fs.writeFileSync(path.join(unmarkedWs, "bin", "keep.txt"), "keep");
      const stubEnv = { ...process.env, HOME: markerHome.replace(/\\/g, "/") };
      const resetScript = buildResetScript("~/demo", true);
      const resetUnmarked = spawnSync(bashBin, ["-c", `ps() { :; }; fuser() { :; }\n${resetScript}`], {
        env: stubEnv,
        windowsHide: true,
      });
      assert.strictEqual(resetUnmarked.status, 0);
      assert.ok(fs.existsSync(path.join(unmarkedWs, ".bashrc")), "reset preserves .bashrc in unmarked workspace");
      assert.ok(fs.existsSync(path.join(unmarkedWs, "bin", "keep.txt")), "reset preserves bin/ in unmarked workspace");

      const fetchUnmarked = spawnSync(bashBin, ["-c", capturedFetchScript], { env: stubEnv, windowsHide: true });
      assert.notStrictEqual(fetchUnmarked.status, 0, "syncer refuses non-empty workspace without marker");
      assert.ok(fs.existsSync(path.join(unmarkedWs, ".bashrc")));
      assert.ok(fs.existsSync(path.join(unmarkedWs, "bin", "keep.txt")));

      fs.writeFileSync(path.join(unmarkedWs, RemoteWorkspaceMarkerFile), "");
      fs.writeFileSync(path.join(unmarkedWs, ".job-queue-manifest.digest"), "abc");
      const resetMarked = spawnSync(bashBin, ["-c", `ps() { :; }; fuser() { :; }\n${resetScript}`], {
        env: stubEnv,
        windowsHide: true,
      });
      assert.strictEqual(resetMarked.status, 0);
      assert.ok(!fs.existsSync(path.join(unmarkedWs, "bin", "keep.txt")), "reset cleans marked workspace");
      assert.ok(!fs.existsSync(path.join(unmarkedWs, ".job-queue-manifest.digest")));
      assert.ok(fs.existsSync(path.join(unmarkedWs, RemoteWorkspaceMarkerFile)), "reset keeps marker file");

      const freshHome = path.join(tmp, "fresh-home");
      fs.mkdirSync(freshHome, { recursive: true });
      const fetchFresh = spawnSync(bashBin, ["-c", capturedFetchScript], {
        env: { ...process.env, HOME: freshHome.replace(/\\/g, "/") },
        windowsHide: true,
      });
      assert.strictEqual(fetchFresh.status, 0, fetchFresh.stderr?.toString("utf8"));
      assert.ok(fs.existsSync(path.join(freshHome, "demo", RemoteWorkspaceMarkerFile)));

      // NB3b: execute real buildExecScript pre-exec integrity check under Git Bash (clean=0, extra/mod/del=254)
      const splitIdx = execScript.indexOf("\nset +e\n");
      assert.ok(splitIdx > 0, "execScript contains set +e boundary");
      const preExecScript = `flock() { :; }\n${execScript.slice(0, splitIdx)}\nexit 0\n`;

      const integHome = path.join(tmp, "integ-home");
      const wsDir = path.join(integHome, "demo");
      fs.mkdirSync(path.join(wsDir, "tests", "Snap", "bin", "Debug"), { recursive: true });
      fs.mkdirSync(path.join(wsDir, "tmp"), { recursive: true });
      fs.writeFileSync(path.join(wsDir, RemoteWorkspaceMarkerFile), "");
      fs.writeFileSync(path.join(wsDir, "tests", "Snap", "Case.cs"), caseContent, "utf8");
      fs.writeFileSync(path.join(wsDir, "tests", "Snap", "bin", "Debug", "Case.dll"), "dll", "utf8");
      fs.writeFileSync(path.join(wsDir, "tmp", "ignored.txt"), "ignored", "utf8");
      fs.writeFileSync(path.join(wsDir, "tests", "Snap", "run.log"), "log", "utf8");
      fs.writeFileSync(path.join(wsDir, ".job-queue-manifest.sha256"), formatSha256Manifest(localManifest), "utf8");
      fs.writeFileSync(path.join(wsDir, ".job-queue-manifest.digest"), `${manifestDigest(localManifest)}\n`, "utf8");

      const runPreExec = (): number | null =>
        spawnSync(bashBin, ["-c", preExecScript], {
          env: { ...process.env, HOME: integHome.replace(/\\/g, "/") },
          windowsHide: true,
        }).status;

      assert.strictEqual(runPreExec(), 0, "clean workspace with excluded build outputs passes integrity check");

      const extraFile = path.join(wsDir, "tests", "Snap", "Extra.cs");
      fs.writeFileSync(extraFile, "class Extra {}", "utf8");
      assert.strictEqual(runPreExec(), 254, "extra non-excluded file fails integrity check with 254");
      fs.unlinkSync(extraFile);
      assert.strictEqual(runPreExec(), 0);

      fs.writeFileSync(path.join(wsDir, "tests", "Snap", "Case.cs"), "class Tampered {}", "utf8");
      assert.strictEqual(runPreExec(), 254, "modified file fails integrity check with 254");
      fs.writeFileSync(path.join(wsDir, "tests", "Snap", "Case.cs"), caseContent, "utf8");

      fs.unlinkSync(path.join(wsDir, "tests", "Snap", "Case.cs"));
      assert.strictEqual(runPreExec(), 254, "deleted file fails integrity check with 254");
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },

  ...RemoteArtifactChecks,
  ...RemoteTranslatorChecks,
  ...RemoteExecutorChecks,
  ...RemoteServerChecks,
};
