// JobServer remote offload integration checks: startNow --remote refusal, rebind() job.remote sync,
// QueuedRun adopt across scheduler restart, and exit 124 exclusion from remote history.

import * as net from "node:net";
import { QueuedRun } from "../client-queued-run.ts";
import { commandEnd } from "../client-run-host.ts";
import { PluginApiVersion, type QueuePlugin } from "../plugin-api.ts";
import { PluginHost } from "../plugin-host.ts";
import { quietly, until } from "../self-test-client.ts";
import { connectRaw, registration } from "../self-test-support.ts";
import { JobServer } from "../server.ts";
import {
  baseRunOpts,
  makeRemote,
  ServerTestHost,
  withRemoteWorkspaceServer,
  type AssertModule,
} from "./checks-support.ts";
import { remoteUsageOf } from "./spec.ts";

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
};
