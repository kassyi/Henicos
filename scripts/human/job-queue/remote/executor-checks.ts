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
  captureStderr,
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

      // classifiedLocalReason takes precedence over memoryMb and surfaces a 1-line reason (and --remote error)
      const localReasonPref = resolveOffloadPreference({
        options: baseRunOpts("dotnet test", { remotes: [remote] }),
        inGpuLock: false,
        classifiedOffloadable: false,
        classifiedLocalReason: "whole-solution test run (Henicos.slnx) runs locally",
        memoryMb: 9920,
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
      });
      assert.deepStrictEqual(localReasonPref, {
        ok: true,
        offload: "never",
        reason: "running locally: whole-solution test run (Henicos.slnx) runs locally",
      });
      const localReasonRemote = failPref({
        classifiedOffloadable: false,
        classifiedLocalReason: "project X is marked local",
        memoryMb: 9920,
      });
      assert.strictEqual(localReasonRemote.ok, false);
      if (!localReasonRemote.ok) {
        assert.ok(localReasonRemote.error.includes("project X is marked local"));
      }

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

  async remoteExecutorEnvAllowlistAndFallbackReason(assert: AssertModule): Promise<void> {
    const remote = makeRemote({ memoryMb: 6144 });
    const envPlugin: QueuePlugin = {
      name: "env-exec-test",
      apiVersion: PluginApiVersion,
      classify: () => ({ offloadable: true }),
      remoteOffload: () => ({
        allowedPrograms: ["node"],
        allowedEnv: ["TEST_SEED"],
        localOnlyEnv: ["MY_LOCAL_VAR"],
      }),
    };

    const wtDir = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-env-exec-")));
    try {
      fs.mkdirSync(path.join(wtDir, "scripts"), { recursive: true });
      fs.writeFileSync(path.join(wtDir, "scripts", "a.js"), "console.log('ok');\n");
      spawnSync("git", ["init", "-q"], { cwd: wtDir, windowsHide: true });
      const host = new PluginHost([envPlugin], [undefined], wtDir, () => {});
      const rules = host.remoteOffload({
        cwd: wtDir,
        worktree: wtDir,
        configRoot: wtDir,
        shell: "pwsh",
        env: {},
        log: () => {},
      });

      const baseParams = {
        options: baseRunOpts("node scripts/a.js", { remotes: [remote] }),
        inGpuLock: false,
        classifiedOffloadable: true,
        memoryMb: 512,
        cwd: wtDir,
        worktree: wtDir,
        shell: "pwsh" as const,
        places: {},
        remoteRules: rules,
      };

      // Allowlisted non-path caller env keeps exact { ok: true, offload: "auto" } without reason
      assert.deepStrictEqual(resolveOffloadPreference({ ...baseParams, env: { TEST_SEED: "42" } }), {
        ok: true,
        offload: "auto",
      });

      // Lowercase offload disable flag applies case-insensitively on win32, not on linux
      assert.deepStrictEqual(
        resolveOffloadPreference({ ...baseParams, env: { gsharp_job_queue_offload: "0" }, platform: "win32" }),
        { ok: true, offload: "never" },
      );
      assert.deepStrictEqual(
        resolveOffloadPreference({ ...baseParams, env: { gsharp_job_queue_offload: "0" }, platform: "linux" }),
        { ok: true, offload: "auto" },
      );

      // localOnlyEnv forces local with 1-line reason, or fails closed under --remote
      const localOnlyPref = resolveOffloadPreference({
        ...baseParams,
        env: { MY_LOCAL_VAR: "1" },
        platform: "linux",
      });
      assert.strictEqual(localOnlyPref.ok, true);
      if (localOnlyPref.ok) {
        assert.strictEqual(localOnlyPref.offload, "never");
        assert.ok(localOnlyPref.reason !== undefined && !/[\r\n]/.test(localOnlyPref.reason));
        assert.ok(localOnlyPref.reason?.includes("MY_LOCAL_VAR"));
      }
      const localOnlyRemote = resolveOffloadPreference({
        ...baseParams,
        options: baseRunOpts("node scripts/a.js", { remote: true, remotes: [remote] }),
        env: { MY_LOCAL_VAR: "1" },
      });
      assert.strictEqual(localOnlyRemote.ok, false);
      if (!localOnlyRemote.ok) {
        assert.ok(localOnlyRemote.error.includes("MY_LOCAL_VAR"));
      }

      // Windows case-insensitive localOnlyEnv vs Linux exact case
      const winCaseLocal = resolveOffloadPreference({
        ...baseParams,
        env: { my_local_var: "1" },
        platform: "win32",
      });
      assert.strictEqual(winCaseLocal.ok, true);
      if (winCaseLocal.ok) {
        assert.strictEqual(winCaseLocal.offload, "never");
        assert.ok(winCaseLocal.reason?.includes("MY_LOCAL_VAR"));
      }
      assert.deepStrictEqual(
        resolveOffloadPreference({ ...baseParams, env: { my_local_var: "1" }, platform: "linux" }),
        { ok: true, offload: "auto" },
      );

      // Path-valued allowlisted env falls back locally with 1-line reason and fails --remote
      for (const pathVal of ["C:\\dumps\\out", "/tmp/out"]) {
        const pathPref = resolveOffloadPreference({ ...baseParams, env: { TEST_SEED: pathVal } });
        assert.strictEqual(pathPref.ok, true);
        if (pathPref.ok) {
          assert.strictEqual(pathPref.offload, "never");
          assert.ok(pathPref.reason !== undefined && !/[\r\n]/.test(pathPref.reason));
          assert.ok(pathPref.reason?.includes("TEST_SEED"));
          assert.ok(pathPref.reason?.includes("path value"));
        }
        const pathRemote = resolveOffloadPreference({
          ...baseParams,
          options: baseRunOpts("node scripts/a.js", { remote: true, remotes: [remote] }),
          env: { TEST_SEED: pathVal },
        });
        assert.strictEqual(pathRemote.ok, false);
        if (!pathRemote.ok) {
          assert.ok(pathRemote.error.includes("TEST_SEED"));
        }
      }

      // RemoteWithFallbackExecutor forwards context.env allowlisted vars and surfaces local-only reasons
      const execHost = new ServerTestHost(() => 0);
      const executor = new RemoteWithFallbackExecutor(execHost, () => {});
      const ctxBase = {
        cwd: wtDir,
        worktree: wtDir,
        configRoot: wtDir,
        shell: "pwsh" as const,
        log: () => {},
      };
      const execPending = executor.execute(
        {
          jobId: 12,
          cpus: 4,
          weight: 1,
          waitedSec: 0,
          cwd: wtDir,
          worktree: wtDir,
          shell: "pwsh",
          places: {},
          options: baseRunOpts("node scripts/a.js", { remotes: [remote] }),
          plugins: host,
          context: { ...ctxBase, env: { TEST_SEED: "42" } },
          remoteName: "ubuntu",
        },
        () => {},
      );
      await until("remote execution receives forwarded env", () => execHost.remoteCalls.length === 1);
      const call0 = execHost.remoteCalls[0];
      assert.ok(call0 !== undefined);
      assert.strictEqual(call0.plan.env.TEST_SEED, "42");
      assert.strictEqual(call0.plan.env[JobVariable], "12");
      assert.strictEqual(call0.plan.env[CpusVariable], "4");
      call0.end({ code: 0, signaled: false });
      assert.deepStrictEqual(await execPending, { code: 0, signaled: false });

      const fallbackStderr = await captureStderr(async () => {
        const fbRes = await executor.execute(
          {
            jobId: 13,
            cpus: 4,
            weight: 1,
            waitedSec: 0,
            cwd: wtDir,
            worktree: wtDir,
            shell: "pwsh",
            places: {},
            options: baseRunOpts("node scripts/a.js", { remotes: [remote] }),
            plugins: host,
            context: { ...ctxBase, env: { MY_LOCAL_VAR: "1" } },
            remoteName: "ubuntu",
          },
          () => {},
        );
        assert.deepStrictEqual(fbRes, { code: 255, signaled: false, fallbackToLocal: true });
      });
      assert.ok(fallbackStderr.includes("MY_LOCAL_VAR"));

      const remoteRefuseStderr = await captureStderr(async () => {
        const refRes = await executor.execute(
          {
            jobId: 14,
            cpus: 4,
            weight: 1,
            waitedSec: 0,
            cwd: wtDir,
            worktree: wtDir,
            shell: "pwsh",
            places: {},
            options: baseRunOpts("node scripts/a.js", { remote: true, remotes: [remote] }),
            plugins: host,
            context: { ...ctxBase, env: { MY_LOCAL_VAR: "1" } },
            remoteName: "ubuntu",
          },
          () => {},
        );
        assert.deepStrictEqual(refRes, { code: UsageExitCode, signaled: false });
      });
      assert.ok(remoteRefuseStderr.includes("MY_LOCAL_VAR"));
    } finally {
      fs.rmSync(wtDir, { recursive: true, force: true });
    }

    // QueuedRun E2E: unallowlisted local-only var prints 1-line [job-queue] notice and runs locally
    await withRemoteWorkspaceServer(async ({ port, wt }) => {
      const remoteSpec = makeRemote({ host: "jobrunner@ubuntu-server.lan" });
      const offloadPlugin: QueuePlugin = {
        name: "offload-env-e2e",
        apiVersion: PluginApiVersion,
        classify: () => ({ offloadable: true }),
        remoteOffload: () => ({
          allowedPrograms: ["node"],
          allowedEnv: ["TEST_SEED"],
          localOnlyEnv: ["JQ_TEST_LOCAL_ONLY"],
        }),
      };
      const hostPlugins = new PluginHost([offloadPlugin], [undefined], wt, () => {});
      const localHost = new ServerTestHost(() => port);
      const prev = process.env.JQ_TEST_LOCAL_ONLY;
      process.env.JQ_TEST_LOCAL_ONLY = "1";
      try {
        const stderr = await captureStderr(async () => {
          void new QueuedRun(
            baseRunOpts("node scripts/a.js", { port, remotes: [remoteSpec] }),
            { weight: 1, pin: null, before: null },
            false,
            false,
            localHost,
            hostPlugins,
          ).run();
          await until("local fallback runs locally", () => localHost.commands.length === 1);
          localHost.commands[0]?.end({ code: 0, signaled: false });
          await until("local fallback exits", () => localHost.exits.length === 1);
        });
        assert.strictEqual(localHost.remoteCalls.length, 0);
        assert.deepStrictEqual(localHost.exits, [0]);
        const reasonLines = stderr
          .split(/\r?\n/)
          .filter((line) => line.includes("running locally:") && line.includes("JQ_TEST_LOCAL_ONLY"));
        assert.strictEqual(reasonLines.length, 1, `expected exactly 1 local-fallback reason line, got: ${stderr}`);
      } finally {
        if (prev === undefined) {
          delete process.env.JQ_TEST_LOCAL_ONLY;
        } else {
          process.env.JQ_TEST_LOCAL_ONLY = prev;
        }
      }
    });

    // QueuedRun E2E: async checkOffload hook ("allow", "local" + 1-line reason, --remote fail-closed, --local skip, reconnect idempotence)
    await withRemoteWorkspaceServer(async ({ port, wt }) => {
      const remoteSpec = makeRemote({ host: "jobrunner@ubuntu-server.lan" });
      let checkOffloadCalls = 0;
      let nextAnswer: "allow" | { offload: "local"; reason: string } = "allow";
      const probePlugin: QueuePlugin = {
        name: "async-offload-probe",
        apiVersion: PluginApiVersion,
        classify: () => ({ offloadable: true }),
        checkOffload: () => {
          checkOffloadCalls++;
          return Promise.resolve(nextAnswer);
        },
        remoteOffload: () => ({ allowedPrograms: ["node"] }),
      };
      const hostPlugins = new PluginHost([probePlugin], [undefined], wt, () => {});

      // 1. --local and GSHARP_JOB_QUEUE_OFFLOAD=0 and inGpuLock must NEVER invoke checkOffload
      const skipCases: [Parameters<typeof baseRunOpts>[1], boolean, string | undefined][] = [
        [{ port, local: true, remotes: [remoteSpec] }, false, undefined],
        [{ port, remotes: [remoteSpec] }, true, undefined],
        [{ port, remotes: [remoteSpec] }, false, "0"],
      ];
      for (const [opts, inLock, envVal] of skipCases) {
        const skipHost = new ServerTestHost(() => port);
        const prevOffload = process.env.GSHARP_JOB_QUEUE_OFFLOAD;
        if (envVal !== undefined) {
          process.env.GSHARP_JOB_QUEUE_OFFLOAD = envVal;
        } else {
          delete process.env.GSHARP_JOB_QUEUE_OFFLOAD;
        }
        try {
          void new QueuedRun(
            baseRunOpts("node scripts/a.js", opts),
            { weight: 1, pin: null, before: null },
            inLock,
            false,
            skipHost,
            hostPlugins,
          ).run();
          await until("local short-circuit runs locally", () => skipHost.commands.length === 1);
          skipHost.commands[0]?.end({ code: 0, signaled: false });
          await until("local short-circuit exits", () => skipHost.exits.length === 1);
        } finally {
          if (prevOffload === undefined) {
            delete process.env.GSHARP_JOB_QUEUE_OFFLOAD;
          } else {
            process.env.GSHARP_JOB_QUEUE_OFFLOAD = prevOffload;
          }
        }
      }
      assert.strictEqual(checkOffloadCalls, 0, "--local / inGpuLock / OFFLOAD=0 must skip checkOffload");

      // 2. checkOffload returning "local" logs 1-line reason and runs locally
      nextAnswer = { offload: "local", reason: "filter matches 2 Platform=Windows tests" };
      const localHost = new ServerTestHost(() => port);
      const localStderr = await captureStderr(async () => {
        void new QueuedRun(
          baseRunOpts("node scripts/a.js", { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          localHost,
          hostPlugins,
        ).run();
        await until("checkOffload local runs locally", () => localHost.commands.length === 1);
        localHost.commands[0]?.end({ code: 0, signaled: false });
        await until("checkOffload local exits", () => localHost.exits.length === 1);
      });
      assert.strictEqual(checkOffloadCalls, 1);
      assert.strictEqual(localHost.remoteCalls.length, 0);
      assert.deepStrictEqual(localHost.exits, [0]);
      assert.strictEqual(
        localStderr.split(/\r?\n/).filter((l) => l.includes("running locally: filter matches 2 Platform=Windows tests"))
          .length,
        1,
      );

      // 3. checkOffload returning "local" with --remote fails closed before connecting
      const fakeRemoteHost = Object.assign(new FakeHost(), {
        connect: () => Promise.reject(new Error("must not connect when checkOffload refuses --remote")),
      });
      const remoteRefuseStderr = await captureStderr(async () => {
        await new QueuedRun(
          baseRunOpts("node scripts/a.js", { port, remote: true, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          fakeRemoteHost,
          hostPlugins,
        ).run();
      });
      assert.strictEqual(checkOffloadCalls, 2);
      assert.deepStrictEqual(fakeRemoteHost.exits, [UsageExitCode]);
      assert.strictEqual(fakeRemoteHost.connects.length, 0);
      assert.ok(remoteRefuseStderr.includes("--remote requested, but filter matches 2 Platform=Windows tests"));

      // 4. checkOffload returning "allow" sends offload: "auto" and is NOT re-invoked on scheduler reconnect
      nextAnswer = "allow";
      const scheduler = new FakeScheduler();
      await scheduler.listen();
      try {
        const allowHost = new FakeHost();
        void new QueuedRun(
          baseRunOpts("node scripts/a.js", { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          allowHost,
          hostPlugins,
        ).run();
        await until("allow client connects", () => allowHost.connects.length === 1);
        allowHost.connects[0]?.resolve(await scheduler.connect());
        await until("allow request arrives", () => (scheduler.accepted[0]?.messages.length ?? 0) > 0);
        const reqMsg = scheduler.accepted[0]?.messages[0];
        assert.strictEqual(reqMsg?.offload, "auto");
        assert.strictEqual(checkOffloadCalls, 3);
        scheduler.accepted[0]?.socket.destroy();
        await until("allow client reconnects", () => allowHost.connects.length === 2);
        allowHost.connects[1]?.resolve(await scheduler.connect());
        await until("second request arrives", () => (scheduler.accepted[1]?.messages.length ?? 0) > 0);
        assert.strictEqual(checkOffloadCalls, 3, "checkOffload must not re-run on reconnect");
        const conn2 = scheduler.accepted[1];
        assert.ok(conn2 !== undefined);
        FakeScheduler.send(conn2, { type: "queued", id: 51, state: "waiting", ahead: 0, position: 1 });
        FakeScheduler.send(conn2, { type: "granted", id: 51, cpus: 4, capacity: 2, protocol: ProtocolVersion });
        await until("command runs after reconnect", () => allowHost.commands.length === 1);
        allowHost.commands[0]?.end({ code: 0, signaled: false });
        await until("done sent", () => conn2.messages.some((m) => m.type === "done"));
        FakeScheduler.send(conn2, { type: "done-ok", id: 51 });
        await until("allow client exits", () => allowHost.exits.length === 1);
      } finally {
        scheduler.close();
      }
    });
  },
};
