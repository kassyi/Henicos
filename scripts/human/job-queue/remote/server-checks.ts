// JobServer remote offload integration checks: startNow --remote refusal, rebind() job.remote sync,
// QueuedRun adopt across scheduler restart, and exit 124 exclusion from remote history.

import * as fs from "node:fs";
import * as net from "node:net";
import * as path from "node:path";
import { status } from "../client-commands.ts";
import { QueuedRun } from "../client-queued-run.ts";
import { commandEnd } from "../client-run-host.ts";
import { KeptOutcomes } from "../constants.ts";
import { formatJobId } from "../job-id.ts";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { parseQuarantineSummary } from "../protocol.ts";
import { quietly, until } from "../self-test-client.ts";
import { connectRaw, registration } from "../self-test-support.ts";
import { JobServer } from "../server.ts";
import { QuarantineFolderName } from "./artifact-collector.ts";
import {
  baseRunOpts,
  makeRemote,
  ServerTestHost,
  withRemoteWorkspaceServer,
  type AssertModule,
} from "./checks-support.ts";
import { remoteUsageOf } from "./spec.ts";

async function captureStatusOutput(body: () => Promise<void>): Promise<{
  stdout: string;
  exitCode: number | string | undefined;
}> {
  let stdout = "";
  const origWrite = process.stdout.write;
  const prevExitCode = process.exitCode;
  process.exitCode = undefined;
  process.stdout.write = ((chunk: string | Uint8Array) => {
    stdout += typeof chunk === "string" ? chunk : Buffer.from(chunk).toString("utf8");
    return true;
  }) as typeof process.stdout.write;
  try {
    await body();
    return { stdout, exitCode: process.exitCode };
  } finally {
    process.stdout.write = origWrite;
    process.exitCode = prevExitCode;
  }
}

export const RemoteServerChecks: Record<string, (assert: AssertModule) => void | Promise<void>> = {
  async remoteStartNowRefusalAndServerStateChecks(assert: AssertModule): Promise<void> {
    await quietly(async () => {
      assert.deepStrictEqual(commandEnd({ code: 124, signaled: false, timedOut: true }, false), {
        exitCode: 124,
        completed: false,
        cancelled: false,
      });

      await withRemoteWorkspaceServer(async ({ server, port, wt, stateDir, configPath }) => {
        const remoteSpec = makeRemote({ host: "jobrunner@ubuntu-server.lan" });
        const offloadPlugin: QueuePlugin = {
          name: "offload-test",
          apiVersion: PluginApiVersion,
          classify: () => ({ offloadable: true }),
        };
        const hostPlugins = new PluginHost([offloadPlugin], [undefined], wt, () => {});

        // 1. ControlMessages.start refuses waiting --remote job when no healthy accepting remote is free
        const holder = await connectRaw(port);
        holder.send({ ...registration("node scripts/holder.js", 1, process.pid), offload: "force" });
        assert.strictEqual((await holder.until("granted"))?.remote, "ubuntu");
        const waiting = await connectRaw(port);
        waiting.send({ ...registration("node scripts/a.js", 1, process.pid), offload: "force" });
        const queued = await waiting.until("queued");
        const waitId = Number(queued?.id);
        const startClient = await connectRaw(port);
        startClient.send({ type: "start", id: waitId });
        const err = await startClient.until("error");
        assert.strictEqual(err?.code, "not-started", "start refuses --remote when remote is busy");
        assert.notStrictEqual(server.find(waitId)?.startNow, true);
        holder.send({ type: "done", completed: true, exitCode: 0 });
        await holder.until("done-ok");
        assert.strictEqual((await waiting.until("granted"))?.remote, "ubuntu");
        waiting.send({ type: "done", completed: true, exitCode: 0 });
        await waiting.until("done-ok");
        holder.close();
        waiting.close();
        startClient.close();

        // 2. rebind() syncs job.remote (remote->local adopt clears remote; ended->running adopt restores remote)
        const c1 = await connectRaw(port);
        const req1 = { ...registration("node scripts/a.js", 1, process.pid), offload: "auto" };
        c1.send(req1);
        const g1 = await c1.until("granted");
        const id1 = Number(g1?.id);
        assert.strictEqual(g1?.remote, "ubuntu");
        c1.close();
        await until("job1 detached", () => server.find(id1)?.detached === true);
        const b1 = await connectRaw(port);
        b1.send({ ...req1, type: "adopt", id: id1, commandPid: process.pid, spawnedAt: Date.now() });
        assert.strictEqual((await b1.until("adopted"))?.id, id1);
        assert.strictEqual(server.find(id1)?.remote, undefined, "rebind clears remote when adopt omits it");
        assert.strictEqual(server.scheduler.used, 1, "rebind accounts local slot after remote->local adopt");
        assert.strictEqual(remoteUsageOf(server.scheduler.running, "ubuntu").jobs.length, 0);
        b1.send({ type: "done", completed: true, exitCode: 0 });
        await b1.until("done-ok");
        b1.close();

        const c2 = await connectRaw(port);
        const req2 = { ...registration("node scripts/a.js", 1, process.pid), offload: "auto" };
        c2.send(req2);
        const g2 = await c2.until("granted");
        const id2 = Number(g2?.id);
        c2.close();
        await until("job2 detached", () => server.find(id2)?.detached === true);
        const j2 = server.find(id2);
        assert.ok(j2 !== undefined);
        server.freeSlots(j2, "test provisional");
        j2.remote = undefined;
        const b2 = await connectRaw(port);
        b2.send({ ...req2, type: "adopt", id: id2, remote: "ubuntu", commandPid: process.pid, spawnedAt: Date.now() });
        assert.strictEqual((await b2.until("adopted"))?.id, id2);
        assert.strictEqual(server.find(id2)?.remote, "ubuntu", "rebind restores remote on ended->running adopt");
        assert.strictEqual(server.scheduler.used, 0);
        assert.strictEqual(remoteUsageOf(server.scheduler.running, "ubuntu").jobs.length, 1);
        b2.send({ type: "done", completed: true, exitCode: 0 });
        await b2.until("done-ok");
        b2.close();

        // 3. Exit 124 (timedOut: true) is excluded from remote history; normal completion is recorded
        const timeoutCmd = "node scripts/a.js --timeout";
        const h1 = new ServerTestHost(() => port);
        void new QueuedRun(
          baseRunOpts(timeoutCmd, { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          h1,
          hostPlugins,
        ).run();
        await until("timeout run starts remotely", () => h1.remoteCalls.length === 1);
        h1.remoteCalls[0]?.end({ code: 124, signaled: false, timedOut: true });
        await until("timeout run exits", () => h1.exits.length === 1);
        assert.deepStrictEqual(h1.exits, [124]);
        assert.strictEqual(
          server.remoteHistory("ubuntu").estimate(timeoutCmd),
          null,
          "exit 124 timed-out run must not be recorded in remote history",
        );

        const normalCmd = "node scripts/a.js --normal";
        const h2 = new ServerTestHost(() => port);
        void new QueuedRun(
          baseRunOpts(normalCmd, { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          h2,
          hostPlugins,
        ).run();
        await until("normal run starts remotely", () => h2.remoteCalls.length === 1);
        h2.remoteCalls[0]?.end({ code: 0, signaled: false });
        await until("normal run exits", () => h2.exits.length === 1);
        assert.ok(server.remoteHistory("ubuntu").estimate(normalCmd) !== null);

        // 4. QueuedRun adopt carries remote across scheduler restart into fresh JobServer
        const server2 = new JobServer({
          slots: 1,
          memoryMb: 8192,
          policy: "fifo",
          port: 0,
          stateDir,
          configPath,
          log: () => {},
        });
        server2.scheduler.holdUntil = 0;
        await new Promise<void>((resolve) => server2.server.listen(0, "127.0.0.1", resolve));
        const port2 = (server2.server.address() as net.AddressInfo).port;
        try {
          let activePort = port;
          const h3 = new ServerTestHost(() => activePort);
          void new QueuedRun(
            baseRunOpts("node scripts/a.js", { port, remotes: [remoteSpec] }),
            { weight: 1, pin: null, before: null },
            false,
            false,
            h3,
            hostPlugins,
          ).run();
          await until("job3 starts remotely", () => h3.remoteCalls.length === 1);
          const running3 = server.scheduler.running.find((j) => j.remote === "ubuntu");
          assert.ok(running3 !== undefined);
          activePort = port2;
          running3.connection?.socket.destroy();
          await until("server2 adopts job3 on ubuntu", () => server2.find(running3.id)?.remote === "ubuntu");
          assert.strictEqual(server2.scheduler.used, 0);
          h3.remoteCalls[0]?.end({ code: 0, signaled: false });
          await until("job3 exits", () => h3.exits.length === 1);
          assert.deepStrictEqual(h3.exits, [0]);
        } finally {
          server2.stopping = true;
          for (const j of server2.records.values()) {
            j.connection?.socket.destroy();
          }
          server2.server.close();
        }
      });
    });
  },

  async remoteQuarantineStatusAndRestartPersistence(assert: AssertModule): Promise<void> {
    await quietly(async () => {
      // 0. parseQuarantineSummary rejects C1 controls, fractional bytes, and non-redirected resultsDirs
      assert.ok(
        parseQuarantineSummary({ dir: "q", files: 1, bytes: 1, resultsRedirected: false }) !== undefined,
        "valid baseline QuarantineSummary parses",
      );
      assert.strictEqual(
        parseQuarantineSummary({ dir: "q\u009b31m", files: 1, bytes: 1, resultsRedirected: false }),
        undefined,
        "rejects C1 control in dir",
      );
      assert.strictEqual(
        parseQuarantineSummary({
          dir: "q",
          files: 1,
          bytes: 1,
          resultsRedirected: true,
          resultsDirs: ["tmp\u009b[2J"],
        }),
        undefined,
        "rejects C1 control in resultsDirs",
      );
      assert.strictEqual(
        parseQuarantineSummary({ dir: "q", files: 1, bytes: 1.5, resultsRedirected: false }),
        undefined,
        "rejects fractional bytes",
      );
      assert.strictEqual(
        parseQuarantineSummary({
          dir: "q",
          files: 1,
          bytes: 1,
          resultsRedirected: false,
          resultsDirs: ["tmp/trx"],
        }),
        undefined,
        "rejects resultsDirs when resultsRedirected is false",
      );

      await withRemoteWorkspaceServer(async ({ server, port, wt, stateDir, configPath }) => {
        const remoteSpec = makeRemote({ host: "jobrunner@ubuntu-server.lan" });
        const offloadPlugin: QueuePlugin = {
          name: "offload-status-test",
          apiVersion: PluginApiVersion,
          classify: () => ({ offloadable: true }),
        };
        const hostPlugins = new PluginHost([offloadPlugin], [undefined], wt, () => {});

        // 1. Remote job completes with quarantine summary -> visible in `status <id>` & `status <id> --json`
        const qHost = new ServerTestHost(() => port);
        void new QueuedRun(
          baseRunOpts("node scripts/a.js --q", { port, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          qHost,
          hostPlugins,
        ).run();
        await until("quarantined remote job starts", () => qHost.remoteCalls.length === 1);
        const qJob = server.scheduler.running.find((j) => j.remote === "ubuntu");
        assert.ok(qJob !== undefined);
        const qDir = path.join(stateDir, QuarantineFolderName, String(qJob.id));
        qHost.remoteCalls[0]?.end({
          code: 0,
          signaled: false,
          quarantine: {
            dir: qDir,
            files: 2,
            bytes: 2048,
            resultsRedirected: true,
            resultsDirs: ["tmp/trx"],
          },
        });
        await until("quarantined job ended", () => server.outcomes.has(qJob.id));

        // Once retention removed the folder, the summary says so
        const prunedText = await captureStatusOutput(() => status(port, qJob.id, false));
        assert.ok(prunedText.stdout.includes(`${qDir} (pruned) (2 file(s)`), "a removed folder shows (pruned)");
        const prunedJson = JSON.parse((await captureStatusOutput(() => status(port, qJob.id, true))).stdout) as {
          pruned?: boolean;
        };
        assert.strictEqual(prunedJson.pruned, true);
        fs.mkdirSync(qDir, { recursive: true });

        const textRes = await captureStatusOutput(() => status(port, qJob.id, false));
        assert.ok(!textRes.stdout.includes("(pruned)"), "an existing folder is not marked pruned");
        assert.strictEqual(textRes.exitCode, undefined);
        assert.ok(textRes.stdout.includes(formatJobId(qJob.id)));
        assert.ok(textRes.stdout.includes("ended (succeeded)"));
        assert.ok(textRes.stdout.includes(qDir));
        assert.ok(textRes.stdout.includes("2 file(s)"));
        assert.ok(textRes.stdout.includes("--results-directory tmp/trx redirected here"));

        const jsonRes = await captureStatusOutput(() => status(port, qJob.id, true));
        assert.strictEqual(jsonRes.exitCode, undefined);
        const parsedJson = JSON.parse(jsonRes.stdout) as Record<string, unknown>;
        assert.strictEqual(parsedJson.id, qJob.id);
        assert.strictEqual(parsedJson.state, "ended");
        assert.strictEqual(parsedJson.outcome, "succeeded");
        assert.deepStrictEqual(parsedJson.quarantine, {
          dir: qDir,
          files: 2,
          bytes: 2048,
          resultsRedirected: true,
          resultsDirs: ["tmp/trx"],
        });

        // 2. Non-quarantined ended job and unknown job still return state: "unknown" and exitCode = 1
        const localHost = new ServerTestHost(() => port);
        void new QueuedRun(
          baseRunOpts("node scripts/a.js --local", { port, local: true, remotes: [remoteSpec] }),
          { weight: 1, pin: null, before: null },
          false,
          false,
          localHost,
          hostPlugins,
        ).run();
        await until("local job starts", () => localHost.commands.length === 1);
        const localJob = server.scheduler.running.find((j) => j.remote === undefined);
        assert.ok(localJob !== undefined);
        localHost.commands[0]?.end({ code: 0, signaled: false });
        await until("local job ended", () => server.outcomes.has(localJob.id));

        const nonQJson = await captureStatusOutput(() => status(port, localJob.id, true));
        assert.strictEqual(nonQJson.exitCode, 1);
        assert.deepStrictEqual(JSON.parse(nonQJson.stdout), { id: localJob.id, state: "unknown" });

        const unknownText = await captureStatusOutput(() => status(port, 999999, false));
        assert.strictEqual(unknownText.exitCode, 1);
        assert.ok(unknownText.stdout.includes("no job 999999"));

        // 3. `report` message (scheduler restart fallback) records quarantine metadata too
        const repClient = await connectRaw(port);
        const queuedAt = Date.now();
        repClient.send({
          ...registration("node scripts/a.js --rep", 1, process.pid),
          queuedAt,
          offload: "auto",
        });
        const repGranted = await repClient.until("granted");
        const repId = Number(repGranted?.id);
        assert.ok(repId > 0);
        repClient.close();
        await until("report job detached", () => server.find(repId)?.detached === true);
        const qReportDir = path.join(stateDir, QuarantineFolderName, String(repId));
        const reporter = await connectRaw(port);
        reporter.send({
          type: "report",
          id: repId,
          completed: true,
          exitCode: 1,
          cancelled: false,
          pid: process.pid,
          queuedAt,
          quarantine: { dir: qReportDir, files: 1, bytes: 512, resultsRedirected: false },
        });
        assert.strictEqual((await reporter.until("reported"))?.id, repId);
        reporter.close();
        await until("report job finalized", () => server.outcomes.get(repId) === "failed");

        const repStatus = await captureStatusOutput(() => status(port, repId, true));
        assert.strictEqual(repStatus.exitCode, undefined);
        const repParsed = JSON.parse(repStatus.stdout) as Record<string, unknown>;
        assert.strictEqual(repParsed.state, "ended");
        assert.strictEqual(repParsed.outcome, "failed");
        assert.deepStrictEqual(repParsed.quarantine, {
          dir: qReportDir,
          files: 1,
          bytes: 512,
          resultsRedirected: false,
        });

        // 4. JobStore.persist() / restore() preserves quarantines across scheduler restart & enforces KeptOutcomes cap
        server.store.persist();
        const serverRestarted = new JobServer({
          slots: 1,
          memoryMb: 8192,
          policy: "fifo",
          port: 0,
          stateDir,
          configPath,
          log: () => {},
        });
        assert.strictEqual(serverRestarted.store.restore(), "current");
        await new Promise<void>((resolve) => serverRestarted.server.listen(0, "127.0.0.1", resolve));
        const portRestarted = (serverRestarted.server.address() as net.AddressInfo).port;
        try {
          const restoredStatus = await captureStatusOutput(() => status(portRestarted, qJob.id, true));
          assert.strictEqual(restoredStatus.exitCode, undefined);
          const restoredParsed = JSON.parse(restoredStatus.stdout) as Record<string, unknown>;
          assert.strictEqual(restoredParsed.state, "ended");
          assert.strictEqual(restoredParsed.outcome, "succeeded");
          assert.deepStrictEqual(restoredParsed.quarantine, {
            dir: qDir,
            files: 2,
            bytes: 2048,
            resultsRedirected: true,
            resultsDirs: ["tmp/trx"],
          });

          // resolve still finds quarantined job by suffix after outcomes evicts >500 newer local jobs
          let pushedOutcomes = 0;
          for (let candidateId = 20000; pushedOutcomes < KeptOutcomes + 5; candidateId++) {
            if (candidateId % 10000 === qJob.id % 10000) {
              continue;
            }
            serverRestarted.recordOutcome(candidateId, "succeeded");
            pushedOutcomes++;
          }
          assert.strictEqual(serverRestarted.outcomes.has(qJob.id), false);
          assert.strictEqual(serverRestarted.quarantines.has(qJob.id), true);
          const qSuffix = String(qJob.id % 10000).padStart(4, "0");
          const resolveClient = await connectRaw(portRestarted);
          try {
            resolveClient.send({ type: "resolve", digits: qSuffix });
            const resolvedMsg = await resolveClient.until("resolved");
            assert.strictEqual(
              resolvedMsg?.id,
              qJob.id,
              "resolve finds quarantined job after outcomes evicts older entries",
            );
          } finally {
            resolveClient.close();
          }

          for (let i = 1000; i < 1000 + KeptOutcomes + 5; i++) {
            serverRestarted.recordQuarantine(i, {
              dir: path.join(stateDir, QuarantineFolderName, String(i)),
              files: 1,
              bytes: 10,
              resultsRedirected: false,
            });
          }
          assert.strictEqual(serverRestarted.quarantines.size, KeptOutcomes);
          assert.strictEqual(serverRestarted.quarantines.has(qJob.id), false);
        } finally {
          serverRestarted.stopping = true;
          serverRestarted.server.close();
        }
      });
    });
  },
};
