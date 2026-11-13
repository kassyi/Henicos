// One client connection of the scheduler (Connection): the job it registered, the dispatch of its messages to one
// handler per message type (server-job-messages.ts, server-control-messages.ts), and what its close means for the job.

import type * as net from "node:net";
import { AcceptedProtocolVersions, ClientGraceMs, ProtocolVersion } from "./constants.ts";
import { onMessages, type Incoming, type ServerMessage } from "./protocol.ts";
import { ControlMessages } from "./server-control-messages.ts";
import type { ServerJob } from "./server-job.ts";
import { JobMessages } from "./server-job-messages.ts";
import { outcomeOfEnd } from "./server-job-state.ts";
import type { JobServer } from "./server.ts";
import { ProcessTable } from "./system.ts";

/** Handles one message of a connection. The settle step after it is the connection's, not the handler's. */
export type MessageHandler = (connection: Connection, message: Incoming) => void;

/** The handler of each message type a client may send. */
const Handlers: ReadonlyMap<string, MessageHandler> = new Map(Object.entries({ ...JobMessages, ...ControlMessages }));

export class Connection {
  readonly server: JobServer;
  readonly socket: net.Socket;
  /** The job this connection registered (request, adopt); null before that, and once the job no longer needs it. */
  job: ServerJob | null = null;

  constructor(server: JobServer, socket: net.Socket) {
    this.server = server;
    this.socket = socket;
    socket.on("error", () => socket.destroy());
    // Every event ends with settle: the grants and the cascades follow the state the event left
    socket.on("close", () => {
      // A connection without a job (status, cancel, wait) changed nothing when it closed
      const hadJob = this.job !== null;
      this.closed();
      if (hadJob) {
        server.settle();
      }
    });
    onMessages(socket, (message) => {
      this.handle(message);
      if (message.type !== "status") {
        server.settle();
      }
    });
  }

  /** Sends the answer and ends the connection. */
  reply(message: ServerMessage): void {
    this.server.reply(this.socket, message);
  }

  private handle(message: Incoming): void {
    if (!AcceptedProtocolVersions.includes(Number(message.version))) {
      this.reply({
        type: "error",
        code: "version",
        message:
          `protocol ${String(message.version)} does not match the scheduler's ${ProtocolVersion}; ` +
          'run "job-queue.js stop" when no job runs',
      });
      return;
    }
    const handler = typeof message.type === "string" ? Handlers.get(message.type) : undefined;
    if (handler === undefined) {
      this.reply({ type: "error", message: `unknown message type ${String(message.type)}` });
      return;
    }
    handler(this, message);
  }

  private closed(): void {
    const server = this.server;
    const scheduler = server.scheduler;
    const job = this.job;
    if (job === null || job.cancelled) {
      return;
    }
    if (server.stopping && !job.done) {
      // The stop ended this connection: the client registers again with the next scheduler, so the job has not ended
      // (no outcome, no cascade to the jobs that wait for it)
      return;
    }
    if (job.cancelPending) {
      // As in "done": a command that succeeded before the cancel reached it (its report said so) stays succeeded
      const cancelled = job;
      this.job = null;
      const outcome = outcomeOfEnd({ ...cancelled, cancelled: false }, true);
      cancelled.cancelled = outcome === "cancelled";
      server.release(cancelled, "cancelled", outcome);
      return;
    }
    if (scheduler.waiting.includes(job) && !job.done && ProcessTable.isAlive(job.pid)) {
      // Only the connection dropped; the client queues again under the same id. It has not ended: no outcome, and
      // the jobs that wait for it keep waiting until it is back (or counts as lost after ClientGraceMs)
      const reconnecting = job;
      server.unbind(reconnecting);
      server.expect(reconnecting.id, Date.now() + ClientGraceMs);
      server.release(reconnecting, "its client reconnects", null);
      return;
    }
    const lost = job;
    server.unbind(lost);
    if (scheduler.running.includes(lost) && !lost.done && lost.commandPid > 0) {
      // The poll releases the slot once no process of the command is left.
      server.log(`client of #${lost.id} is gone; holding its slot while command ${lost.commandPid} runs`);
      return;
    }
    server.release(lost, "client closed", server.closedOutcome(lost));
  }
}
