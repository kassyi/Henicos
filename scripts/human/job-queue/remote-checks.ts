// Unit and in-process server checks for transparent remote CPU job offload:
// scheduler remote slots & fallback, strict local vs remote history separation, command translation & worktree
// boundaries, manifest diffing, and config validation.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { loadConfig, loadLocalRemotesConfig, LocalRemotesFileName, validateConfig } from "./config.ts";
import { RunHistory } from "./history.ts";
import type { AssertModule } from "./plugin-api.ts";
import { translateForRemote } from "./remote-command.ts";
import { RemoteStreamSanitizer, parseSequenceXmlInProgress, validateAndWriteArtifacts } from "./remote-output.ts";
import {
  diffManifests,
  formatSha256Manifest,
  isExcludedSyncPath,
  manifestDigest,
  parseRemoteSha256Output,
} from "./remote-sync.ts";
import { buildSshArgs, fitsRemote, type RemoteSpec } from "./remotes.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "./scheduler.ts";

function makeRemote(overrides: Partial<RemoteSpec> = {}): RemoteSpec {
  return {
    name: "ubuntu",
    host: "jobrunner@ubuntu-server.lan",
    workspace: "~/henicos",
    slots: 1,
    cpus: 4,
    memoryMb: 6144,
    mode: "prefer-remote",
    maxJobMinutes: 30,
    sshOptions: ["-o", "ConnectTimeout=5"],
    keepBuildServersWarm: true,
    probe: null,
    ...overrides,
  };
}

function makeJob(id: number, overrides: Partial<SchedulerJob> = {}): SchedulerJob {
  return {
    id,
    weight: 1,
    gpu: false,
    queuedAt: 1000 + id,
    grantedAt: 0,
    estimate: { typicalMs: 10_000, longestMs: 20_000 },
    pin: null,
    before: null,
    after: null,
    memoryMb: 2880,
    offload: "auto",
    ...overrides,
  };
}

export const RemoteChecks: Record<string, (assert: AssertModule) => void> = {
  remoteSchedulerPrefersRemoteWithoutLocalUsage(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(2, 0, "hrrn", 8192, undefined, [remote]);
    const j1 = makeJob(1, { memoryMb: 2880, offload: "auto" });
    const granted1 = scheduler.enqueue(j1, 2000);
    assert.deepStrictEqual(granted1, [j1]);
    assert.strictEqual(j1.remote, "ubuntu", "first offloadable job goes to remote");
    assert.strictEqual(scheduler.used, 0, "remote job consumes 0 local CPU slots");
    assert.strictEqual(scheduler.memoryUsed, 0, "remote job consumes 0 local memory");

    // Second offloadable job while remote is full (1/1) spills over to local slots
    const j2 = makeJob(2, { memoryMb: 2880, offload: "auto" });
    const granted2 = scheduler.enqueue(j2, 2001);
    assert.deepStrictEqual(granted2, [j2]);
    assert.strictEqual(j2.remote, undefined, "second job spills over to local when remote is full");
    assert.strictEqual(scheduler.used, 1);
    assert.strictEqual(scheduler.memoryUsed, 2880);

    // Detaching remote job leaves local accounting intact
    scheduler.detach(j1);
    assert.strictEqual(scheduler.used, 1);
    assert.strictEqual(scheduler.memoryUsed, 2880);
  },

  remoteSchedulerRespectsMemoryCap(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(2, 0, "hrrn", 16384, undefined, [remote]);
    // Stress test (6720 MB > 6144 MB) must stay on local even when remote is completely idle
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
    const firstGrant = scheduler.enqueue(j1, 5000);
    assert.deepStrictEqual(firstGrant, [], "initial unprobed remote holds job briefly for probe");
    assert.strictEqual(scheduler.waitFor(j1, 5000), "remote");
    assert.deepStrictEqual(scheduler.remoteProbesWanted(5000), ["ubuntu"]);

    // Probe fails (server offline): auto job falls back to local immediately
    scheduler.recordRemoteProbe("ubuntu", { ok: false, value: null, at: 5100, generation: 0, why: "timeout" });
    const afterFail = scheduler.grant(5100);
    assert.deepStrictEqual(afterFail, [j1]);
    assert.strictEqual(j1.remote, undefined, "offline remote falls back to local for auto job");

    // Forced --remote job does NOT fall back to local when probe failed
    const forced = makeJob(2, { offload: "force" });
    const forcedGrant = scheduler.enqueue(forced, 5200);
    assert.deepStrictEqual(forcedGrant, [], "forced remote job never falls back to local");
    assert.strictEqual(scheduler.waitFor(forced, 5200), "remote");

    // Probe recovers: forced job now runs on ubuntu
    scheduler.recordRemoteProbe("ubuntu", { ok: true, value: 4096, at: 25000, generation: 0 });
    const recovered = scheduler.grant(25000);
    assert.deepStrictEqual(recovered, [forced]);
    assert.strictEqual(forced.remote, "ubuntu");
  },

  remoteSchedulerSeparatesLocalAndRemoteEstimates(assert: AssertModule): void {
    const remote = makeRemote({ slots: 1, memoryMb: 6144, mode: "prefer-remote" });
    const scheduler = new Scheduler<SchedulerJob>(1, 0, "hrrn", 8192, undefined, [remote]);
    const localEst: Estimate = { typicalMs: 10_000, longestMs: 15_000 };
    const remoteEst: Estimate = { typicalMs: 90_000, longestMs: 120_000 };
    const j1 = makeJob(1, {
      offload: "auto",
      estimate: localEst,
      remoteEstimates: new Map([["ubuntu", remoteEst]]),
    });
    const j2 = makeJob(2, {
      offload: "never",
      estimate: localEst,
      remoteEstimates: new Map([["ubuntu", remoteEst]]),
    });
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

  remoteCommandTranslatesPowerShellAndCrossWorktreePaths(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rcmd-"));
    try {
      const wtA = path.join(tmp, "wt-main");
      const wtB = path.join(tmp, "wt-review");
      fs.mkdirSync(path.join(wtA, ".git"), { recursive: true });
      fs.mkdirSync(path.join(wtB, ".git"), { recursive: true });
      const projB = path.join(wtB, "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Kassyi.GSharp.Tests.Cpu.csproj");
      fs.mkdirSync(path.dirname(projB), { recursive: true });
      fs.writeFileSync(projB, "<Project />");

      // Caller in wtA passes absolute path to wtB's csproj: must sync wtB, NOT wtA!
      const cmd = `$env:DiffEngine_Disabled='true'; dotnet test ${projB} --disable-build-servers --filter 'FullyQualifiedName~WarpStackModelTests'`;
      const plan = translateForRemote(cmd, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [projB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
      });
      assert.ok(plan !== null, "cross-worktree single-target command translates");
      assert.strictEqual(path.resolve(plan.worktree), path.resolve(wtB));
      assert.strictEqual(plan.remoteCwdRel, "");
      assert.ok(!plan.bashCommand.includes("--disable-build-servers"), "warm servers strips --disable-build-servers");
      assert.ok(plan.bashCommand.includes("-m:4"), "injects remote -m:4");
      assert.ok(
        plan.bashCommand.includes("tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj"),
        "rewrites Windows path to POSIX relative path",
      );
      assert.ok(plan.bashCommand.includes("FullyQualifiedName~WarpStackModelTests"), "keeps --filter intact");
      assert.ok(plan.bashCommand.includes("--blame"), "auto-attaches lightweight --blame for crash attribution");
      assert.strictEqual(plan.env.DiffEngine_Disabled, "true");
      assert.strictEqual(plan.env.GSHARP_TEST_NO_CUDA, "1");
      assert.strictEqual(plan.env.MSBUILDCORELIMIT, "4");

      // Opt-out of warm servers preserves --disable-build-servers
      const coldPlan = translateForRemote(cmd, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [projB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: false,
        env: {},
      });
      assert.ok(coldPlan !== null && coldPlan.bashCommand.includes("--disable-build-servers"));

      // Spanning two different worktrees must return null (fail-closed)
      const projA = path.join(wtA, "tests", "A.csproj");
      fs.mkdirSync(path.dirname(projA), { recursive: true });
      fs.writeFileSync(projA, "<Project />");
      const multi = translateForRemote(`dotnet test ${projA} && dotnet test ${projB}`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: {
          [projA]: { root: wtA, ignored: false },
          [projB]: { root: wtB, ignored: false },
        },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
      });
      assert.strictEqual(multi, null, "multi-worktree command must not offload");

      // Ignored path or pipe must return null
      assert.strictEqual(
        translateForRemote(`dotnet test ${projB} | Select-Object -First 5`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [projB]: { root: wtB, ignored: false } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
      );
      assert.strictEqual(
        translateForRemote(`dotnet test ${projB}`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [projB]: { root: wtB, ignored: true } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
      );
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
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/Linq.cs"), true);
    assert.strictEqual(isExcludedSyncPath(".claude/worktrees/w1/a.cs"), true);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs"), true);
    assert.strictEqual(isExcludedSyncPath("tmp/foo.txt"), true);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.verified.cs"), false);
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Emit.cs"), false);

    const shaText = formatSha256Manifest({ "b.cs": "2".repeat(64), "a.cs": "1".repeat(64) });
    assert.strictEqual(shaText, `${"1".repeat(64)}  ./a.cs\n${"2".repeat(64)}  ./b.cs\n`);

    const parsed = parseRemoteSha256Output(
      `${"a".repeat(64)}  ./src/Emit.cs\n${"b".repeat(64)}  ./tests/bin/x.dll\n${"c".repeat(64)}  ./tests/A.received.txt\n`,
    );
    assert.deepStrictEqual(Object.keys(parsed), ["src/Emit.cs"]);
    assert.strictEqual(parsed["src/Emit.cs"], "a".repeat(64));
  },

  remoteStreamSanitizerStripsDangerousEscapes(assert: AssertModule): void {
    const san = new RemoteStreamSanitizer();
    // Split OSC 52 clipboard injection, window title, cursor up, and valid SGR red + Japanese UTF-8 across chunks
    const raw =
      "\x1b[31mFailed\x1b[0m MyTests.Crash\r\n" +
      "\x1b]52;c;ZXZpbA==\x07" +
      "\x1b]0;FakeTitle\x1b\\" +
      "\x1b[2A\x1b[2J" +
      "Error Message: テスト失敗\x08\x07\n" +
      "   at Foo.Bar() in /home/jobrunner/henicos/tests/Foo.cs:line 42\n";
    const buf = Buffer.from(raw, "utf8");
    let out = "";
    // Feed in 3-byte slices to stress multi-byte UTF-8 and split escape sequences
    for (let i = 0; i < buf.length; i += 3) {
      out += san.push(buf.subarray(i, i + 3));
    }
    out += san.flush();
    assert.ok(out.includes("\x1b[31mFailed\x1b[0m MyTests.Crash"), "preserves SGR colors and test name");
    assert.ok(out.includes("Error Message: テスト失敗\n"), "preserves UTF-8 text and strips BS/BEL");
    assert.ok(out.includes("at Foo.Bar() in /home/jobrunner/henicos/tests/Foo.cs:line 42"), "preserves stack trace");
    assert.ok(!out.includes("52;c;"), "strips OSC 52 clipboard payload");
    assert.ok(!out.includes("FakeTitle"), "strips OSC 0 window title");
    assert.ok(!out.includes("\x1b[2A"), "strips CSI cursor movement");
    assert.ok(!out.includes("\x1b[2J"), "strips CSI screen clear");
  },

  remoteArtifactValidatorBlocksAttacksAndWritesAllowedFiles(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-art-"));
    try {
      const wt = path.join(tmp, "wt");
      fs.mkdirSync(path.join(wt, "tests", "Snapshots"), { recursive: true });
      const frame = (rel: string, content: string): Buffer => {
        const body = Buffer.from(content, "utf8");
        return Buffer.concat([Buffer.from(`${rel}\0${body.length}\n`, "utf8"), body]);
      };
      const payload = Buffer.concat([
        // Allowed: *.received.txt inside known test directory
        frame("tests/Snapshots/Snap.Foo.received.txt", "actual output"),
        // Allowed: .trx inside explicit --results-directory
        frame("tmp/trx/run1.trx", "<TestRun />"),
        // Attacks that MUST all be rejected
        frame("../outside.txt", "escape"),
        frame("/etc/passwd", "abs"),
        frame(".git/config", "git hijack"),
        frame("scripts/human/job-queue.js", "rce"),
        frame("tests/Snapshots/Snap.Foo.verified.txt", "snapshot tamper"),
        frame("tests/Snapshots/Evil.cs", "source tamper"),
        frame("tests/UnknownDir/Snap.Foo.received.txt", "untracked dir"),
        frame("tmp/trx/crash.dmp", "huge dump"),
      ]);
      const result = validateAndWriteArtifacts(payload, {
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
      });
      assert.deepStrictEqual(result.writtenFiles, ["tests/Snapshots/Snap.Foo.received.txt", "tmp/trx/run1.trx"]);
      assert.strictEqual(result.rejectedFiles.length, 8, "blocks all 8 unapproved/attack paths");
      assert.strictEqual(
        fs.readFileSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.received.txt"), "utf8"),
        "actual output",
      );
      assert.strictEqual(fs.readFileSync(path.join(wt, "tmp", "trx", "run1.trx"), "utf8"), "<TestRun />");
      assert.ok(!fs.existsSync(path.join(tmp, "outside.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.verified.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "scripts", "human", "job-queue.js")));

      // Quota enforcement rejects oversized single file
      const big = validateAndWriteArtifacts(frame("tmp/trx/big.trx", "x".repeat(200)), {
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        maxSingleBytes: 100,
      });
      assert.deepStrictEqual(big.writtenFiles, []);
      assert.strictEqual(big.rejectedFiles.length, 1);

      // Sequence_*.xml crash parser
      const xml =
        '<TestSequence><Test Name="A.Passed" Completed="True" /><Test Name="B.NativeCrash" Completed="False" /></TestSequence>';
      assert.deepStrictEqual(parseSequenceXmlInProgress(xml), ["B.NativeCrash"]);
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

    // Validate the tracked sample file remotes.local.example.json
    const samplePath = path.join(import.meta.dirname, "remotes.local.example.json");
    const sampleTmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-local-rem-"));
    try {
      fs.copyFileSync(samplePath, path.join(sampleTmp, LocalRemotesFileName));
      const loadedSample = loadLocalRemotesConfig(sampleTmp);
      assert.ok(loadedSample?.ubuntu !== undefined, "remotes.local.example.json validates cleanly");
      assert.strictEqual(loadedSample?.ubuntu?.host, "jobrunner@ubuntu-server.lan");

      // loadConfig ignores repo-level remotes when stateDir is given without explicit --config
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
      const loaded = loadConfig({
        mode: "status",
        fromDirectory: repoDir,
        stateDir: sampleTmp,
        warn: () => {},
      });
      assert.strictEqual(loaded.config.remotes?.evil, undefined, "untrusted repo remotes are ignored");
      assert.strictEqual(loaded.config.remotes?.ubuntu?.host, "jobrunner@ubuntu-server.lan");

      // Forwarding --config <repo>/job-queue.config.json (as serve does via WMI) still loads remotes.local.json
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

    // Reject unsafe SSH options, leading '-' host, and non-ssh probe
    assert.throws(() =>
      validateConfig({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        remotes: {
          ubuntu: { host: "-oProxyCommand=evil", workspace: "~/henicos", cpus: 4, memoryMb: 6144 },
        },
      }),
    );
    assert.throws(() =>
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
            sshOptions: ["-o", "ProxyCommand=evil"],
          },
        },
      }),
    );
    assert.throws(() =>
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
            sshOptions: ["-o", "StrictHostKeyChecking=no"],
          },
        },
      }),
    );
    assert.throws(() =>
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
            probe: { argv: ["powershell", "-c", "echo 9999"], min: 100 },
          },
        },
      }),
    );

    // buildSshArgs enforces mandatory security flags and '--' separator
    const args = buildSshArgs(makeRemote(), "echo ok");
    assert.ok(args.includes("StrictHostKeyChecking=yes"));
    assert.ok(args.includes("ForwardAgent=no"));
    assert.ok(args.includes("ServerAliveInterval=5"));
    assert.strictEqual(args[args.length - 3], "--");

    // fitsRemote rejects GPU jobs
    const spec = makeRemote();
    assert.strictEqual(fitsRemote(makeJob(1, { gpu: true }), { spec, probe: null }, [], 1000), false);
    assert.strictEqual(fitsRemote(makeJob(1, { gpuTurn: true }), { spec, probe: null }, [], 1000), false);
  },
};
