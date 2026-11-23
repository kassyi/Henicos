// Unit and in-process server checks for transparent remote CPU job offload:
// scheduler remote slots & fallback, strict local vs remote history separation, command translation & worktree
// boundaries, manifest diffing, and config validation.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { validateConfig } from "./config.ts";
import { RunHistory } from "./history.ts";
import type { AssertModule } from "./plugin-api.ts";
import { translateForRemote } from "./remote-command.ts";
import { diffManifests, isExcludedSyncPath, manifestDigest, parseRemoteSha256Output } from "./remote-sync.ts";
import { fitsRemote, type RemoteSpec } from "./remotes.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "./scheduler.ts";

function makeRemote(overrides: Partial<RemoteSpec> = {}): RemoteSpec {
  return {
    name: "ubuntu",
    host: "ubuntu-server.lan",
    workspace: "~/henicos",
    slots: 1,
    cpus: 4,
    memoryMb: 6144,
    mode: "prefer-remote",
    sshOptions: ["-o", "BatchMode=yes"],
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
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs"), true);
    assert.strictEqual(isExcludedSyncPath("tmp/foo.txt"), true);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.verified.cs"), false);
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Emit.cs"), false);

    const parsed = parseRemoteSha256Output(
      `${"a".repeat(64)}  ./src/Emit.cs\n${"b".repeat(64)}  ./tests/bin/x.dll\n${"c".repeat(64)}  ./tests/A.received.txt\n`,
    );
    assert.deepStrictEqual(Object.keys(parsed), ["src/Emit.cs"]);
    assert.strictEqual(parsed["src/Emit.cs"], "a".repeat(64));
  },

  remoteConfigValidation(assert: AssertModule): void {
    const valid = validateConfig({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
      remotes: {
        ubuntu: {
          host: "ubuntu-server.lan",
          workspace: "~/henicos",
          slots: 1,
          cpus: 4,
          memoryMb: 6144,
          mode: "prefer-remote",
          keepBuildServersWarm: true,
        },
      },
    });
    assert.strictEqual(valid.remotes?.ubuntu?.host, "ubuntu-server.lan");

    assert.throws(() =>
      validateConfig({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        remotes: {
          ubuntu: { host: "ubuntu-server.lan", workspace: "~/henicos", cpus: 0, memoryMb: 6144 },
        },
      }),
    );
    assert.throws(() =>
      validateConfig({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        remotes: {
          ubuntu: { host: "ubuntu-server.lan", workspace: "~/henicos", cpus: 4, memoryMb: 6144, mode: "invalid" },
        },
      }),
    );

    // fitsRemote rejects GPU jobs
    const spec = makeRemote();
    assert.strictEqual(fitsRemote(makeJob(1, { gpu: true }), { spec, probe: null }, [], 1000), false);
    assert.strictEqual(fitsRemote(makeJob(1, { gpuTurn: true }), { spec, probe: null }, [], 1000), false);
  },
};
