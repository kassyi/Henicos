// Unit and offline E2E checks for transparent remote CPU job offload:
// scheduler remote slots & fallback, strict local vs remote history separation, ShellDialect command translation,
// manifest diffing, 1-pass frame parsing, 6-gate Chain of Responsibility, config validation, and FakeRemoteTransport
// E2E (sync -> exit-254 auto-heal retry -> crash attribution -> artifact pull-back -> plugin-driven remote-reset).

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { loadConfig, loadLocalRemotesConfig, LocalRemotesFileName, validateConfig } from "../config.ts";
import { RunHistory } from "../history.ts";
import { PluginApiVersion, type AssertModule, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "../scheduler.ts";
import type { ShellResult } from "../shell.ts";
import { parseArtifactFrames, RemoteStreamSanitizer, validateAndWriteArtifacts } from "./artifact-collector.ts";
import { translateForRemote } from "./command-translator.ts";
import { buildResetScript, resetWorker } from "./control.ts";
import { buildExecScript, RemoteRunner } from "./runner.ts";
import { buildSshArgs, fitsRemote, type RemoteSpec } from "./spec.ts";
import {
  buildRemoteFindPruneExpr,
  diffManifests,
  formatSha256Manifest,
  isExcludedSyncPath,
  manifestDigest,
  parseRemoteSha256Output,
} from "./syncer.ts";
import type { CaptureResult, RemoteTransport, StreamCallbacks } from "./transport.ts";

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

function makeFrame(rel: string, content: string): Buffer {
  const body = Buffer.from(content, "utf8");
  return Buffer.concat([Buffer.from(`${rel}\0${body.length}\n`, "utf8"), body]);
}

/** In-memory `RemoteTransport` adapter for offline E2E verification without SSH. */
class FakeRemoteTransport implements RemoteTransport {
  readonly spec: RemoteSpec;
  readonly execCalls: string[] = [];
  readonly uploadCalls: { remoteCommand: string; files: string[] }[] = [];
  readonly interactiveCalls: string[] = [];
  execHandler: (script: string) => CaptureResult = () => ({ code: 0, stdout: "", stderr: "" });
  binaryHandler: (script: string) => Buffer | null = () => null;
  uploadHandler: (files: readonly string[]) => { code: number; stderr: string } = () => ({ code: 0, stderr: "" });
  interactiveHandler: (script: string, callbacks: StreamCallbacks) => ShellResult = () => ({
    code: 0,
    signaled: false,
  });

  constructor(spec: RemoteSpec) {
    this.spec = spec;
  }

  async exec(script: string): Promise<CaptureResult> {
    this.execCalls.push(script);
    return this.execHandler(script);
  }

  async execBinary(script: string, _maxBytes: number): Promise<Buffer | null> {
    return this.binaryHandler(script);
  }

  async streamUpload(
    remoteCommand: string,
    _headerChunks: readonly Buffer[],
    _worktree: string,
    files: readonly string[],
  ): Promise<{ code: number; stderr: string }> {
    this.uploadCalls.push({ remoteCommand, files: [...files] });
    return this.uploadHandler(files);
  }

  async spawnInteractive(script: string, callbacks: StreamCallbacks): Promise<ShellResult> {
    this.interactiveCalls.push(script);
    const result = this.interactiveHandler(script, callbacks);
    callbacks.onClose?.();
    return result;
  }
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

  remoteCommandTranslatesPowerShellAndCrossWorktreePaths(assert: AssertModule): void {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-rcmd-"));
    try {
      const wtA = path.join(tmp, "wt-main");
      const wtB = path.join(tmp, "wt-review");
      fs.mkdirSync(path.join(wtA, ".git"), { recursive: true });
      fs.mkdirSync(path.join(wtB, ".git"), { recursive: true });
      const scriptB = path.join(wtB, "scripts", "run-tests.js");
      fs.mkdirSync(path.dirname(scriptB), { recursive: true });
      fs.writeFileSync(scriptB, "console.log('ok');");

      const outB = path.join(wtB, "tmp", "out.json");
      const coreNodePlan = translateForRemote(`$env:CI='true'; node ${scriptB} --output ${outB}`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [scriptB]: { root: wtB, ignored: false }, [outB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
      });
      assert.ok(coreNodePlan !== null, "core translates cross-worktree node command without plugins");
      assert.strictEqual(path.resolve(coreNodePlan.worktree), path.resolve(wtB));
      assert.strictEqual(coreNodePlan.remoteCwdRel, "");
      assert.ok(coreNodePlan.bashCommand.includes("scripts/run-tests.js"));
      assert.strictEqual(coreNodePlan.env.CI, "true");
      assert.strictEqual(coreNodePlan.env.GSHARP_TEST_NO_CUDA, undefined, "core has no Henicos env hardcode");
      assert.strictEqual(coreNodePlan.env.MSBUILDCORELIMIT, undefined, "core has no MSBuild env hardcode");

      assert.strictEqual(
        translateForRemote("dotnet test App.csproj", {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: {},
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
        "core without plugins does not hardcode dotnet as an allowed program",
      );

      const cargoPlugin: QueuePlugin = {
        name: "cargo",
        apiVersion: PluginApiVersion,
        remoteOffload: () => ({
          allowedPrograms: ["cargo"],
          pathValueOptions: ["--manifest-path"],
          nonPathValueOptions: ["--filter"],
          pathExtensions: ["toml"],
          defaultEnv: { CARGO_TERM_COLOR: "always" },
          rewriteStatement: (stmt) =>
            stmt.program === "cargo" && stmt.subcommand === "test"
              ? { args: [...stmt.args, "--nocapture"] }
              : undefined,
          prepareRemoteCommand: (command, grant) => ({
            command,
            env: { CARGO_BUILD_JOBS: String(grant.cpus) },
            note: `${grant.cpus} cargo jobs`,
          }),
        }),
      };
      const cargoHost = new PluginHost([cargoPlugin], [{}], wtA, () => {});
      const cargoRules = cargoHost.remoteOffload({
        cwd: wtA,
        worktree: wtA,
        configRoot: wtA,
        shell: "pwsh",
        env: {},
        log: () => {},
      });
      const manifestB = path.join(wtB, "Cargo.toml");
      fs.writeFileSync(manifestB, "[package]\nname = 'demo'\n");
      const cargoPlan = translateForRemote(`cargo test --manifest-path ${manifestB} --filter foo`, {
        cwd: wtA,
        worktree: wtA,
        shell: "pwsh",
        places: { [manifestB]: { root: wtB, ignored: false } },
        cpus: 4,
        keepBuildServersWarm: true,
        env: {},
        remoteRules: cargoRules,
      });
      assert.ok(cargoPlan !== null, "plugin-contributed cargo command translates");
      assert.strictEqual(path.resolve(cargoPlan.worktree), path.resolve(wtB));
      assert.ok(cargoPlan.bashCommand.includes("--manifest-path Cargo.toml"));
      assert.ok(cargoPlan.bashCommand.includes("--nocapture"));
      assert.strictEqual(cargoPlan.env.CARGO_TERM_COLOR, "always");
      assert.strictEqual(cargoPlan.env.CARGO_BUILD_JOBS, "4");
      assert.strictEqual(cargoPlan.note, "4 cargo jobs");

      const scriptA = path.join(wtA, "scripts", "a.js");
      fs.mkdirSync(path.dirname(scriptA), { recursive: true });
      fs.writeFileSync(scriptA, "console.log('a');");
      assert.strictEqual(
        translateForRemote(`node ${scriptA} && node ${scriptB}`, {
          cwd: wtA,
          worktree: wtA,
          shell: "pwsh",
          places: {
            [scriptA]: { root: wtA, ignored: false },
            [scriptB]: { root: wtB, ignored: false },
          },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
        "multi-worktree command must not offload",
      );

      assert.strictEqual(
        translateForRemote(`node ${scriptB} | Select-Object -First 5`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [scriptB]: { root: wtB, ignored: false } },
          cpus: 4,
          keepBuildServersWarm: true,
          env: {},
        }),
        null,
      );
      assert.strictEqual(
        translateForRemote(`node ${scriptB}`, {
          cwd: wtB,
          worktree: wtB,
          shell: "pwsh",
          places: { [scriptB]: { root: wtB, ignored: true } },
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
    assert.strictEqual(isExcludedSyncPath(".claude/worktrees/w1/a.cs"), true);
    assert.strictEqual(isExcludedSyncPath("tmp/foo.txt"), true);
    assert.strictEqual(isExcludedSyncPath("src/debug.log"), true);
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Emit.cs"), false);
    assert.strictEqual(isExcludedSyncPath("src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/Linq.cs"), false);
    assert.strictEqual(isExcludedSyncPath("tests/Snap.received.cs"), false);

    const customRules = { excludePrefixes: ["target/", "generated/"], excludeFindNames: ["*.snap.new"] };
    assert.strictEqual(isExcludedSyncPath("target/debug/app", customRules), true);
    assert.strictEqual(isExcludedSyncPath("tests/foo.snap.new", customRules), true);
    const prune = buildRemoteFindPruneExpr(customRules, [".job-queue-slot.lock"]);
    assert.ok(prune.includes("-path './target'"));
    assert.ok(prune.includes("-path './generated'"));
    assert.ok(prune.includes("! -name '*.snap.new'"));

    const shaText = formatSha256Manifest({ "b.cs": "2".repeat(64), "a.cs": "1".repeat(64) });
    assert.strictEqual(shaText, `${"1".repeat(64)}  ./a.cs\n${"2".repeat(64)}  ./b.cs\n`);

    const parsed = parseRemoteSha256Output(
      `${"a".repeat(64)}  ./src/Emit.cs\n${"b".repeat(64)}  ./tests/bin/x.dll\n${"c".repeat(64)}  ./tests/A.snap.new\n`,
      customRules,
    );
    assert.deepStrictEqual(Object.keys(parsed), ["src/Emit.cs"]);
    assert.strictEqual(parsed["src/Emit.cs"], "a".repeat(64));
  },

  remoteStreamSanitizerStripsDangerousEscapes(assert: AssertModule): void {
    const san = new RemoteStreamSanitizer();
    const raw =
      "\x1b[31mFailed\x1b[0m MyTests.Crash\r\n" +
      "\x1b]52;c;ZXZpbA==\x07" +
      "\x1b]0;FakeTitle\x1b\\" +
      "\x1b[2A\x1b[2J" +
      "Error Message: テスト失敗\x08\x07\n" +
      "   at Foo.Bar() in /home/jobrunner/henicos/tests/Foo.cs:line 42\n";
    const buf = Buffer.from(raw, "utf8");
    let out = "";
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
    // 1-pass frame parser edge cases
    const truncated = parseArtifactFrames(Buffer.from("a.txt\x0099\nhi", "utf8"));
    assert.strictEqual(truncated.frames.length, 0);
    assert.strictEqual(truncated.errors.length, 1);

    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-art-"));
    try {
      const wt = path.join(tmp, "wt");
      fs.mkdirSync(path.join(wt, "tests", "Snapshots"), { recursive: true });
      const customFailureRule = {
        searchDirs: ["tests"],
        findName: "*.actual.txt",
        allowPattern: /^tests\/[A-Za-z0-9_./-]+\.actual\.txt$/,
        requireKnownLocalDir: true,
      };
      const payload = Buffer.concat([
        makeFrame("tests/Snapshots/Snap.Foo.actual.txt", "actual output"),
        makeFrame("tmp/trx/run1.trx", "<TestRun />"),
        makeFrame("../outside.txt", "escape"),
        makeFrame("/etc/passwd", "abs"),
        makeFrame(".git/config", "git hijack"),
        makeFrame("scripts/human/job-queue.js", "rce"),
        makeFrame("tests/Snapshots/Snap.Foo.verified.txt", "snapshot tamper"),
        makeFrame("tests/Snapshots/Evil.cs", "source tamper"),
        makeFrame("tests/UnknownDir/Snap.Foo.actual.txt", "untracked dir"),
        makeFrame("tmp/trx/crash.dmp", "huge dump"),
      ]);
      const result = validateAndWriteArtifacts(payload, {
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        failureArtifacts: [customFailureRule],
      });
      assert.deepStrictEqual(result.writtenFiles, ["tests/Snapshots/Snap.Foo.actual.txt", "tmp/trx/run1.trx"]);
      assert.strictEqual(result.rejectedFiles.length, 8, "blocks all 8 unapproved/attack paths");
      assert.strictEqual(
        fs.readFileSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.actual.txt"), "utf8"),
        "actual output",
      );
      assert.strictEqual(fs.readFileSync(path.join(wt, "tmp", "trx", "run1.trx"), "utf8"), "<TestRun />");
      assert.ok(!fs.existsSync(path.join(tmp, "outside.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "tests", "Snapshots", "Snap.Foo.verified.txt")));
      assert.ok(!fs.existsSync(path.join(wt, "scripts", "human", "job-queue.js")));

      const noPluginRules = validateAndWriteArtifacts(makeFrame("tests/Snapshots/Snap.Foo.received.txt", "x"), {
        worktree: wt,
        allowReceived: true,
        knownLocalDirs: new Set(["tests/Snapshots"]),
        collectPaths: [],
      });
      assert.deepStrictEqual(noPluginRules.writtenFiles, []);
      assert.strictEqual(noPluginRules.rejectedFiles.length, 1);

      const big = validateAndWriteArtifacts(makeFrame("tmp/trx/big.trx", "x".repeat(200)), {
        worktree: wt,
        allowReceived: false,
        knownLocalDirs: new Set(),
        collectPaths: [{ path: "tmp/trx", kind: "dir" }],
        maxSingleBytes: 100,
      });
      assert.deepStrictEqual(big.writtenFiles, []);
      assert.strictEqual(big.rejectedFiles.length, 1);
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  },

  async remoteRunnerAndControlOfflineE2E(assert: AssertModule): Promise<void> {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-runner-e2e-"));
    try {
      const wt = path.join(tmp, "wt");
      const stateDir = path.join(tmp, "state");
      fs.mkdirSync(path.join(wt, "tests", "Snap"), { recursive: true });
      fs.mkdirSync(stateDir, { recursive: true });

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
          resetProcessNames: ["demo-runner"],
          resetCleanDirs: ["CrashDir"],
        }),
      };
      const host = new PluginHost([plugin], [{}], wt, () => {});
      const rules = host.remoteOffload({
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
      assert.ok(resetScript.includes("demo-runner"));
      assert.ok(resetScript.includes('"$WS"/CrashDir'));
      assert.ok(!resetScript.includes("StackLinqEmbedded"), "core reset script has no Henicos hardcode");
      assert.ok(!resetScript.includes("VBCSCompiler"), "core reset script has no dotnet hardcode");

      // Offline E2E via FakeRemoteTransport: initial sync -> exit 254 integrity mismatch -> resync -> exit 1 + artifact pull
      const transport = new FakeRemoteTransport(makeRemote({ workspace: "~/demo" }));
      let interactiveAttempt = 0;
      transport.interactiveHandler = () => {
        interactiveAttempt++;
        return interactiveAttempt === 1 ? { code: 254, signaled: false } : { code: 1, signaled: false };
      };
      transport.binaryHandler = () =>
        Buffer.concat([
          makeFrame("CrashDir/crash_1.txt", "Suite.AbortedTest\n"),
          makeFrame("tests/Snap/Case.actual.txt", "got-42"),
        ]);

      const runner = new RemoteRunner(transport, stateDir, rules);
      const localManifest = { "tests/Snap/Case.cs": "a".repeat(64) };
      const res = await runner.execute(
        { worktree: wt, remoteCwdRel: "", bashCommand: "echo test", env: {}, collectPaths: [] },
        () => {},
        localManifest,
      );
      assert.strictEqual(res.code, 1);
      assert.strictEqual(interactiveAttempt, 2, "retried once after exit 254 integrity mismatch");
      assert.strictEqual(transport.uploadCalls.length, 2, "initial sync + clean resync after 254");
      assert.strictEqual(fs.readFileSync(path.join(wt, "tests", "Snap", "Case.actual.txt"), "utf8"), "got-42");
      assert.ok(!fs.existsSync(path.join(wt, "CrashDir")), "crash summary parsed in memory, never written to disk");

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

    assert.throws(() =>
      validateConfig({
        version: 1,
        entry: "scripts/human/job-queue.js",
        plugins: [],
        remotes: { ubuntu: { host: "-oProxyCommand=evil", workspace: "~/henicos", cpus: 4, memoryMb: 6144 } },
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

    const args = buildSshArgs(makeRemote(), "echo ok");
    assert.ok(args.includes("StrictHostKeyChecking=yes"));
    assert.ok(args.includes("ForwardAgent=no"));
    assert.ok(args.includes("ServerAliveInterval=5"));
    assert.strictEqual(args[args.length - 3], "--");

    const spec = makeRemote();
    assert.strictEqual(fitsRemote(makeJob(1, { gpu: true }), { spec, probe: null }, [], 1000), false);
    assert.strictEqual(fitsRemote(makeJob(1, { gpuTurn: true }), { spec, probe: null }, [], 1000), false);
  },
};
