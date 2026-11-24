// Unit and offline E2E checks for transparent remote CPU job offload:
// scheduler remote slots & fallback, strict local vs remote history separation, syncer exclusions, SSH allowlist
// & config validation, FakeRemoteTransport E2E (sync -> exit-254 auto-heal retry -> crash attribution -> sentinel
// -> exit 124 -> AbortSignal -> plugin-driven remote-reset), plus merged artifact, translator, and executor checks.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { loadConfig, loadLocalRemotesConfig, LocalRemotesFileName, validateConfig } from "../config.ts";
import { RunHistory } from "../history.ts";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "../scheduler.ts";
import { RemoteArtifactChecks } from "./artifact-checks.ts";
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
import { buildSshArgs, fitsRemote } from "./spec.ts";
import {
  buildRemoteFindPruneExpr,
  diffManifests,
  formatSha256Manifest,
  isExcludedSyncPath,
  manifestDigest,
  parseRemoteSha256Output,
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
      assert.ok(execScript.includes("find tests -name '*.actual.txt' -delete"));
      assert.ok(execScript.includes("find CrashDir -name 'crash_*.txt' -delete"));
      assert.ok(execScript.includes("__EOF_TEST__"));

      const resetScript = buildResetScript("~/demo", true, rules);
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
      const stderrLog = await captureStderr(async () => {
        const res = await runner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
          () => {},
          localManifest,
        );
        assert.strictEqual(res.code, 1);
      });
      assert.strictEqual(interactiveAttempt, 2, "retried once after exit 254 integrity mismatch");
      assert.strictEqual(transport.uploadCalls.length, 2, "initial sync + clean resync after 254");
      assert.ok(
        transport.interactiveCalls.every((s) => s.includes("sha256sum --quiet -c .job-queue-manifest.sha256")),
        "verifyIntegrity remains true on exit-254 retry",
      );
      assert.strictEqual(fs.readFileSync(path.join(wt, "tests", "Snap", "Case.actual.txt"), "utf8"), "got-42");
      assert.ok(!fs.existsSync(path.join(wt, "CrashDir")), "crash summary parsed in memory, never written to disk");
      assert.ok(
        stderrLog.includes("test host crashed/aborted while running: Suite.AbortedTest"),
        "crash summary actually parsed and logged",
      );

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

      // Offline remote-reset via FakeRemoteTransport clears local manifest cache and parses RESET_OK
      transport.execHandler = () => ({ code: 0, stdout: "RESET_OK killed=3 mem_mb=4096\n", stderr: "" });
      const resetRes = await resetWorker(transport, true, stateDir, rules);
      assert.strictEqual(resetRes.ok, true);
      assert.strictEqual(resetRes.killed, "3");
      assert.strictEqual(resetRes.memMb, "4096");
      assert.ok(!fs.existsSync(path.join(stateDir, "remote-manifest-ubuntu.json")));
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
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

  ...RemoteArtifactChecks,
  ...RemoteTranslatorChecks,
  ...RemoteExecutorChecks,
  ...RemoteServerChecks,
};
