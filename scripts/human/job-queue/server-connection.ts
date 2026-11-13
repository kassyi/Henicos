// One client connection of the scheduler (Connection): the job it registered, the dispatch of its messages to one
// handler per message type (server-job-messages.ts, server-control-messages.ts), and what its close means for the job.

import type * as net from "node:net";
import { AcceptedProtocolVersions, ProtocolVersion } from "./constants.ts";
import { onMessages, type Incoming, type ServerMessage } from "./protocol.ts";
import { ControlMessages } from "./server-control-messages.ts";
import type { ServerJob } from "./server-job.ts";
import { JobMessages } from "./server-job-messages.ts";
import { planClose } from "./server-job-state.ts";
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

  /** What the close means for the record bound to this connection (planClose): nothing ends on a close alone. */
  private closed(): void {
    const server = this.server;
    const job = this.job;
    if (job === null) {
      return; // no job, or its record was re-bound to a newer connection or has ended
    }
    const step = planClose(job, server.stopping, ProcessTable.isAlive(job.pid));
    if (step === "ignore") {
      return;
    }
    if (step === "detach") {
      // The client comes back (re-binds), reports, or the observation of its processes ends the record
      server.unbind(job);
      server.log(
        job.phase === "running"
          ? `client of #${job.id} is away; holding its slots${job.commandPid > 0 ? ` (command ${job.commandPid})` : ""}`
          : `client of #${job.id} is away; it keeps its place`,
      );
      return;
    }
    server.finish(job, step.end, "client closed");
  }
}
