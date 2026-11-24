// Offline E2E checks for RemoteWithFallbackExecutor and QueuedRun: scheduler-governed fallback-local,
// AbortSignal cancel during sync(), --remote fail-closed, and GSHARP_JOB_QUEUE_JOB / CPUS export.

import { spawnSync, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { QueuedRun } from "../client-queued-run.ts";
import { loadConfig } from "../config.ts";
import {
  CancelledExitCode,
  CpusVariable,
  EntryScript,
  JobVariable,
  ProtocolVersion,
  UsageExitCode,
} from "../constants.ts";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { FakeHost, FakeScheduler, quietly, until } from "../self-test-client.ts";
import {
  baseRunOpts,
  FakeRemoteTransport,
  makeRemote,
  ServerTestHost,
  withRemoteWorkspaceServer,
  type AssertModule,
} from "./checks-support.ts";
import type { RemoteCommandPlan } from "./command-translator.ts";
import { RemoteWithFallbackExecutor, resolveOffloadPreference } from "./executor.ts";
import { RemoteRunner, type RemoteRunResult } from "./runner.ts";
import { remoteUsageOf, type RemoteSpec } from "./spec.ts";

export const RemoteExecutorChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  async remoteExecutorFallbackLocalWithJobServer(assert: AssertModule): Promise<void> {
    await quietly(async () => {
      await withRemoteWorkspaceServer(async ({ server, port, wt }) => {
        const remoteSpec = makeRemote({ host: "jobrunner@ubuntu-server.lan" });
        const offloadPlugin: QueuePlugin = {
          name: "offload-test",
          apiVersion: PluginApiVersion,
          classify: () => ({ offloadable: true }),
        };
        const hostPlugins = new PluginHost([offloadPlugin], [undefined], wt, () => {});
        const localHost = new ServerTestHost(() => port);
        const remoteHost = new ServerTestHost(() => port);

        // Hold the single local CPU slot first
        void new QueuedRun(
          baseRunOpts("node scripts/holder.js", { port, local: true, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          localHost,
          hostPlugins,
        ).run();
        await until("local holder starts", () => localHost.commands.length === 1);
        assert.strictEqual(server.scheduler.used, 1);

        // Queue offloadable job: granted on remote while local slot is occupied
        void new QueuedRun(
          baseRunOpts("node scripts/a.js", { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          remoteHost,
          hostPlugins,
        ).run();
        await until("remote job starts on ubuntu", () => remoteHost.remoteCalls.length === 1);
        const remoteCall = remoteHost.remoteCalls[0];
        assert.ok(remoteCall !== undefined);
        const remoteJob = server.scheduler.running.find((j) => j.remote === "ubuntu");
        assert.ok(remoteJob !== undefined);
        assert.strictEqual(remoteCall.plan.env[JobVariable], String(remoteJob.id));
        assert.strictEqual(remoteCall.plan.env[CpusVariable], "4");

        // Remote transport fails before execution -> fallback-local waits for the busy local slot
        remoteCall.end({ code: 255, signaled: false, transportFailed: true });
        await until(
          "fallback job waits for local slot",
          () => server.find(remoteJob.id)?.phase === "waiting" && remoteJob.remote === undefined,
        );
        assert.strictEqual(remoteJob.offload, "never");
        assert.strictEqual(remoteHost.commands.length, 0, "must NOT execute unscheduled while local slot is full");
        assert.strictEqual(remoteUsageOf(server.scheduler.running, "ubuntu").jobs.length, 0);

        // Free local slot -> scheduler grants fallback job locally
        localHost.commands[0]?.end({ code: 0, signaled: false });
        await until("fallback job granted locally", () => remoteHost.commands.length === 1);
        remoteHost.commands[0]?.end({ code: 0, signaled: false });
        await until("both runs exit", () => localHost.exits.length === 1 && remoteHost.exits.length === 1);
        assert.deepStrictEqual(remoteHost.exits, [0]);
        assert.ok(server.history.estimate("node scripts/a.js") !== null, "fallback run recorded in local history");
        assert.strictEqual(
          server.remoteHistory("ubuntu").estimate("node scripts/a.js"),
          null,
          "fallback run must not pollute remote history",
        );
      });
    });
  },

  async remoteExecutorAbortSignalDuringSync(assert: AssertModule): Promise<void> {
    await quietly(async () => {
      // 1. RemoteRunner aborted mid-sync() returns signaled: true (never transportFailed)
      const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-abort-sync-"));
      try {
        const wt = path.join(tmp, "wt");
        const stateDir = path.join(tmp, "state");
        fs.mkdirSync(wt, { recursive: true });
        fs.mkdirSync(stateDir, { recursive: true });
        const transport = new FakeRemoteTransport(makeRemote());
        const controller = new AbortController();
        transport.execHandler = (_script, signal) =>
          new Promise((resolve) => {
            signal?.addEventListener("abort", () => resolve({ code: 1, stdout: "", stderr: "aborted" }), {
              once: true,
            });
            controller.abort();
          });
        const runner = new RemoteRunner(transport, stateDir);
        const res = await runner.execute(
          { worktree: wt, remoteCwdRel: "", bashCommand: "echo hi", env: {}, collectPaths: [] },
          () => {},
          { "a.txt": "a".repeat(64) },
          controller.signal,
        );
        assert.deepStrictEqual(res, { code: 1, signaled: true });
        assert.strictEqual(res.transportFailed, undefined);
        assert.strictEqual(transport.interactiveCalls.length, 0);

        // 2. RemoteWithFallbackExecutor returns CancelledExitCode when AbortSignal fires
        const execHost = new ServerTestHost(() => 0);
        const executor = new RemoteWithFallbackExecutor(execHost, () => {});
        const execAbort = new AbortController();
        const pending = executor.execute(
          {
            jobId: 7,
            cpus: 4,
            weight: 1,
            waitedSec: 0,
            cwd: wt,
            worktree: wt,
            shell: "pwsh",
            places: {},
            options: baseRunOpts("echo hi", { remotes: [makeRemote()] }),
            remoteName: "ubuntu",
          },
          () => {},
          execAbort.signal,
        );
        execAbort.abort();
        const execRes = await pending;
        assert.deepStrictEqual(execRes, { code: CancelledExitCode, signaled: true });
        assert.strictEqual(execRes.fallbackToLocal, undefined);
      } finally {
        fs.rmSync(tmp, { recursive: true, force: true });
      }

      // 3. QueuedRun receives "cancelled" during pre-child remote sync and cancels cleanly without local fallback
      const scheduler = new FakeScheduler();
      await scheduler.listen();
      const wtDir = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-q-abort-")));
      const prevCwd = process.cwd();
      try {
        fs.mkdirSync(path.join(wtDir, "scripts"), { recursive: true });
        fs.writeFileSync(path.join(wtDir, "scripts", "a.js"), "console.log(1);");
        spawnSync("git", ["init", "-q"], { cwd: wtDir, windowsHide: true });
        process.chdir(wtDir);
        const plugin: QueuePlugin = {
          name: "off",
          apiVersion: PluginApiVersion,
          classify: () => ({ offloadable: true }),
        };
        const hostPlugin = new PluginHost([plugin], [undefined], wtDir, () => {});
        let sawSignal: AbortSignal | undefined;
        const fakeHost = Object.assign(new FakeHost(), {
          runRemoteCommand: (
            _r: RemoteSpec,
            _p: RemoteCommandPlan,
            _onChild: (c: ChildProcess) => void,
            _rules?: unknown,
            signal?: AbortSignal,
          ): Promise<RemoteRunResult> => {
            sawSignal = signal;
            return new Promise((resolve) => {
              signal?.addEventListener("abort", () => resolve({ code: 1, signaled: true }), { once: true });
            });
          },
        });
        void new QueuedRun(
          baseRunOpts("node scripts/a.js", { remotes: [makeRemote()] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          fakeHost,
          hostPlugin,
        ).run();
        await until("client connects", () => fakeHost.connects.length === 1);
        fakeHost.connects[0]?.resolve(await scheduler.connect());
        await until("scheduler accepts", () => scheduler.accepted.length === 1);
        const conn = scheduler.accepted[0];
        assert.ok(conn !== undefined);
        await until("request arrives", () => conn.messages.some((m) => m.type === "request"));
        FakeScheduler.send(conn, { type: "queued", id: 30, state: "waiting", ahead: 0, position: 1 });
        FakeScheduler.send(conn, {
          type: "granted",
          id: 30,
          cpus: 4,
          capacity: 2,
          protocol: ProtocolVersion,
          remote: "ubuntu",
        });
        await until("remote execution entered", () => sawSignal !== undefined);
        FakeScheduler.send(conn, { type: "cancelled", id: 30, reason: "cancel requested" });
        await until("done sent after cancel", () => conn.messages.some((m) => m.type === "done"));
        FakeScheduler.send(conn, { type: "done-ok", id: 30 });
        await until("client exits", () => fakeHost.exits.length === 1);
        assert.strictEqual(sawSignal?.aborted, true);
        assert.deepStrictEqual(fakeHost.exits, [CancelledExitCode]);
        assert.strictEqual(fakeHost.commands.length, 0);
        const doneMsg = conn.messages.find((m) => m.type === "done");
        assert.deepStrictEqual([doneMsg?.completed, doneMsg?.cancelled], [false, true]);
      } finally {
        process.chdir(prevCwd);
        scheduler.close();
        fs.rmSync(wtDir, { recursive: true, force: true });
      }
    });
  },

  async remoteExecutorForceRemoteFailClosed(assert: AssertModule): Promise<void> {
    await quietly(async () => {
      const remote = makeRemote({ memoryMb: 6144 });
      const failPref = (overrides: Partial<Parameters<typeof resolveOffloadPreference>[0]>) =>
        resolveOffloadPreference({
          options: baseRunOpts("node scripts/a.js", { remote: true, remotes: [remote] }),
          inGpuLock: false,
          classifiedOffloadable: true,
          memoryMb: 1024,
          cwd: process.cwd(),
          worktree: process.cwd(),
          shell: "pwsh",
          places: {},
          remoteRules: new PluginHost([], [], process.cwd(), () => {}).remoteOffload({
            cwd: process.cwd(),
            worktree: process.cwd(),
            configRoot: process.cwd(),
            shell: "pwsh",
            env: {},
            log: () => {},
          }),
          ...overrides,
        });

      assert.strictEqual(failPref({ options: baseRunOpts("node a.js", { remote: true, remotes: [] }) }).ok, false);
      assert.strictEqual(
        failPref({ options: baseRunOpts("node a.js", { remote: true, remotes: [{ ...remote, mode: "off" }] }) }).ok,
        false,
      );
      assert.strictEqual(failPref({ memoryMb: 6720 }).ok, false, "--remote refuses when memoryMb exceeds remote");
      assert.strictEqual(failPref({ classifiedOffloadable: false }).ok, false);

      // JOB_QUEUE_PLUGINS=0 refuses --remote before connecting
      const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
      const disabledHost = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
        ...process.env,
        JOB_QUEUE_PLUGINS: "0",
      });
      const noPlugHost = new FakeHost();
      await new QueuedRun(
        baseRunOpts("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu", { remote: true, remotes: [remote] }),
        { weight: 1, pin: null, before: null },
        false,
        false,
        noPlugHost,
        disabledHost,
      ).run();
      assert.deepStrictEqual(noPlugHost.exits, [UsageExitCode]);
      assert.strictEqual(noPlugHost.connects.length, 0);

      // Tests.Cuda refuses --remote before connecting when running from repo root
      const realHost = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
        ...process.env,
        JOB_QUEUE_PLUGINS: undefined,
      });
      const prevCwd = process.cwd();
      process.chdir(loaded.configRoot);
      try {
        const cudaHost = new FakeHost();
        await new QueuedRun(
          baseRunOpts("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj", {
            remote: true,
            remotes: [remote],
          }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          cudaHost,
          realHost,
        ).run();
        assert.deepStrictEqual(cudaHost.exits, [UsageExitCode]);
        assert.strictEqual(cudaHost.connects.length, 0);
      } finally {
        process.chdir(prevCwd);
      }

      // Missing granted.remote refuses both in QueuedRun and in RemoteWithFallbackExecutor
      const scheduler = new FakeScheduler();
      await scheduler.listen();
      try {
        const missingHost = new FakeHost();
        const allowHost = new PluginHost(
          [{ name: "allow", apiVersion: PluginApiVersion, classify: () => ({ offloadable: true }) }],
          [undefined],
          loaded.configRoot,
          () => {},
        );
        void new QueuedRun(
          baseRunOpts("echo ok", { remote: true, remotes: [remote] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          missingHost,
          allowHost,
        ).run();
        await until("client connects", () => missingHost.connects.length === 1);
        missingHost.connects[0]?.resolve(await scheduler.connect());
        await until("accepted", () => scheduler.accepted.length === 1);
        const conn = scheduler.accepted[0];
        assert.ok(conn !== undefined);
        await until("request arrives", () => conn.messages.some((m) => m.type === "request"));
        FakeScheduler.send(conn, { type: "queued", id: 40, state: "waiting", ahead: 0, position: 1 });
        FakeScheduler.send(conn, { type: "granted", id: 40, cpus: 4, capacity: 2, protocol: ProtocolVersion });
        await until("done sent", () => conn.messages.some((m) => m.type === "done"));
        FakeScheduler.send(conn, { type: "done-ok", id: 40 });
        await until("client exits UsageExitCode", () => missingHost.exits.length === 1);
        assert.deepStrictEqual(missingHost.exits, [UsageExitCode]);
        assert.strictEqual(missingHost.commands.length, 0);
      } finally {
        scheduler.close();
      }

      const execHost = new ServerTestHost(() => 0);
      const executor = new RemoteWithFallbackExecutor(execHost, () => {});
      const noRemoteRes = await executor.execute(
        {
          jobId: 41,
          cpus: 4,
          weight: 1,
          waitedSec: 0,
          cwd: process.cwd(),
          worktree: process.cwd(),
          shell: "pwsh",
          places: {},
          options: baseRunOpts("echo ok", { remote: true, remotes: [remote] }),
          remoteName: undefined,
        },
        () => {},
      );
      assert.strictEqual(noRemoteRes.code, UsageExitCode);
      assert.strictEqual(noRemoteRes.fallbackToLocal, undefined);
      assert.strictEqual(execHost.commands.length, 0);
    });
  },
};
