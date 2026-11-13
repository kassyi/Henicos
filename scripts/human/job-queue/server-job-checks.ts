// Unit checks of the job record decisions (server-job-state.ts), run by the self-test beside the scheduler checks.

import type * as Assert from "node:assert";
import { CancelledExitCode, ClientGraceMs } from "./constants.ts";
import {
  cancelTargets,
  isSameClient,
  outcomeOfEnd,
  planClose,
  planObservation,
  planRegistration,
  planReport,
  type ClientIdentity,
  type JobFacts,
} from "./server-job-state.ts";

type AssertModule = typeof Assert;

const client: ClientIdentity = { pid: 100, queuedAt: 1000, commandPid: 0, spawnedAt: 0 };

/** A record for the checks: attached, not done, with the client above. */
function facts(phase: JobFacts["phase"], extra: Partial<JobFacts> = {}): JobFacts {
  return {
    ...client,
    phase,
    stub: false,
    detached: false,
    detachedAt: 0,
    provisionalAt: 0,
    done: false,
    completed: false,
    exitCode: null,
    cancelPending: false,
    ...extra,
  };
}

const ended = (completed: boolean, exitCode: number | null, cancelled = false) => ({ completed, exitCode, cancelled });

/** Identity: the pid, the time it queued, and the command when both sides know it. */
export function checkIdentity(assert: AssertModule): void {
  const command = { ...client, commandPid: 7, spawnedAt: 70 };
  assert.strictEqual(isSameClient(client, client), true);
  assert.strictEqual(isSameClient(client, { ...client, pid: 101 }), false, "another pid");
  assert.strictEqual(isSameClient(client, { ...client, queuedAt: 2000 }), false, "a reused pid queued at another time");
  assert.strictEqual(isSameClient(client, { ...client, queuedAt: 0 }), true, "a protocol 2 client sends no queuedAt");
  assert.strictEqual(isSameClient(command, { ...command, commandPid: 8 }), false, "another command");
  assert.strictEqual(isSameClient(command, { ...command, spawnedAt: 71 }), false, "a command started at another time");
  assert.strictEqual(isSameClient(client, command), true, "the record does not know the command yet");
  assert.strictEqual(isSameClient(command, client), true, "the message does not name the command");
  assert.strictEqual(isSameClient(command, { ...command, queuedAt: 0, commandPid: 8 }), false, "no queuedAt: command");
}

/** The end of a job: the client's cancel, a cancel that the command's success crosses, a missing exit code. */
export function checkOutcomeOfEnd(assert: AssertModule): void {
  assert.strictEqual(outcomeOfEnd(ended(true, 0), false), "succeeded");
  assert.strictEqual(outcomeOfEnd(ended(true, 3), false), "failed");
  assert.strictEqual(outcomeOfEnd(ended(false, 0), false), "failed", "stopped early");
  assert.strictEqual(outcomeOfEnd(ended(true, null), false), "lost", "a protocol 2 done");
  assert.strictEqual(outcomeOfEnd(ended(false, CancelledExitCode, true), false), "cancelled", "the client says so");
  assert.strictEqual(outcomeOfEnd(ended(true, 0), true), "succeeded", "a success that crosses the cancel wins");
  assert.strictEqual(outcomeOfEnd(ended(true, 1), true), "cancelled", "the scheduler asked, the command failed");
  assert.strictEqual(outcomeOfEnd(ended(false, null), true), "cancelled");
}

/** request and adopt: every cell of the table in the design. */
export function checkRegistration(assert: AssertModule): void {
  const rebind = (to: "waiting" | "running", resendGrant = false, resendCancel = false) => ({
    kind: "rebind",
    to,
    resendGrant,
    resendCancel,
  });
  for (const type of ["request", "adopt"] as const) {
    assert.deepStrictEqual(planRegistration(type, undefined, undefined, client), { kind: "create" });
    assert.deepStrictEqual(planRegistration(type, undefined, "cancelled", client), { kind: "refuse-cancelled" });
    assert.deepStrictEqual(
      planRegistration(type, undefined, "succeeded", client),
      { kind: "create-as-new-id", finalize: false },
      "a final outcome is never overwritten",
    );
    assert.deepStrictEqual(planRegistration(type, facts("waiting", { stub: true }), undefined, client), {
      kind: "create",
    });
    assert.deepStrictEqual(
      planRegistration(type, facts("running"), undefined, { ...client, queuedAt: 2000 }),
      { kind: "create-as-new-id", finalize: false },
      "another client's record is not touched",
    );
  }
  assert.deepStrictEqual(planRegistration("request", facts("waiting"), undefined, client), rebind("waiting"));
  assert.deepStrictEqual(planRegistration("adopt", facts("waiting"), undefined, client), rebind("running"));
  assert.deepStrictEqual(planRegistration("request", facts("running"), undefined, client), rebind("running", true));
  assert.deepStrictEqual(planRegistration("adopt", facts("running"), undefined, client), rebind("running"));
  assert.deepStrictEqual(planRegistration("adopt", facts("ended"), undefined, client), rebind("running"));
  assert.deepStrictEqual(planRegistration("request", facts("ended"), undefined, client), {
    kind: "create-as-new-id",
    finalize: true,
  });
  assert.deepStrictEqual(
    planRegistration("adopt", facts("running", { cancelPending: true }), undefined, client),
    rebind("running", false, true),
    "a cancel the client may have missed is sent again",
  );
}

/** A closed connection ends a record only on evidence. */
export function checkClose(assert: AssertModule): void {
  assert.strictEqual(planClose(facts("running"), true, true), "ignore", "the stop ended the connection");
  assert.strictEqual(planClose(facts("waiting"), false, true), "detach", "a waiting job keeps its place");
  assert.deepStrictEqual(planClose(facts("waiting"), false, false), { end: "cancelled" }, "its client is gone");
  assert.strictEqual(planClose(facts("running"), false, false), "detach", "held, also without a known command");
  assert.strictEqual(planClose(facts("running", { commandPid: 7, spawnedAt: 70 }), false, false), "detach");
  assert.deepStrictEqual(planClose(facts("running", { cancelPending: true }), false, true), { end: "cancelled" });
  const done = facts("running", { done: true, completed: true, exitCode: 0 });
  assert.deepStrictEqual(planClose(done, false, true), { end: "succeeded" });
  assert.deepStrictEqual(planClose(done, true, true), { end: "succeeded" }, "a done reached the stopping scheduler");
  assert.deepStrictEqual(planClose({ ...done, cancelPending: true }, false, true), { end: "succeeded" });
  assert.deepStrictEqual(planClose(facts("waiting", { done: true, exitCode: 3 }), false, true), { end: "cancelled" });
  assert.strictEqual(planClose(facts("ended", { detached: true }), false, true), "ignore");
}

/** What the processes of a detached record say. */
export function checkObservation(assert: AssertModule): void {
  const now = 1_000_000;
  const detached = { detached: true, detachedAt: now - 1000, provisionalAt: now - 1000 };
  const late = { detached: true, detachedAt: now - ClientGraceMs, provisionalAt: now - ClientGraceMs };
  const alive = { clientAlive: true, commandAlive: true };
  const gone = { clientAlive: true, commandAlive: false };
  const seen = (clientAlive: boolean, commandAlive: boolean | null) => ({ clientAlive, commandAlive });
  // waiting
  assert.strictEqual(planObservation(facts("waiting", detached), alive, now), "keep");
  assert.deepStrictEqual(planObservation(facts("waiting", detached), seen(false, null), now), { finalize: "lost" });
  assert.deepStrictEqual(planObservation(facts("waiting", late), alive, now), { finalize: "lost" }, "grace over");
  assert.strictEqual(planObservation(facts("waiting", { ...detached, stub: true }), seen(false, null), now), "keep");
  assert.deepStrictEqual(planObservation(facts("waiting", { ...late, stub: true }), seen(false, null), now), {
    finalize: "lost",
  });
  // running with a known command
  const command = { commandPid: 7, spawnedAt: 70 };
  assert.strictEqual(planObservation(facts("running", { ...detached, ...command }), alive, now), "keep");
  assert.strictEqual(
    planObservation(facts("running", { ...late, ...command }), seen(false, null), now),
    "keep",
    "a process table that could not be read counts as alive",
  );
  assert.deepStrictEqual(
    planObservation(facts("running", { ...detached, ...command }), gone, now),
    { freeSlots: true, final: false },
    "the client may still report",
  );
  assert.deepStrictEqual(
    planObservation(facts("running", { ...detached, ...command }), seen(false, false), now),
    { freeSlots: true, final: true },
    "nobody can report any more",
  );
  // running without a known command
  assert.strictEqual(planObservation(facts("running", detached), seen(true, false), now), "keep");
  assert.deepStrictEqual(planObservation(facts("running", detached), seen(false, false), now), {
    freeSlots: true,
    final: true,
  });
  assert.deepStrictEqual(planObservation(facts("running", late), seen(true, false), now), {
    freeSlots: true,
    final: true,
  });
  // provisional
  assert.strictEqual(planObservation(facts("ended", detached), seen(true, false), now), "keep");
  assert.deepStrictEqual(planObservation(facts("ended", detached), seen(false, false), now), { finalize: "lost" });
  assert.deepStrictEqual(
    planObservation(facts("ended", late), seen(true, false), now),
    { finalize: "lost" },
    "a provisional outcome becomes final after the grace",
  );
}

/** A report ends a held or provisional record, never a final outcome or another client's record. */
export function checkReport(assert: AssertModule): void {
  const success = ended(true, 0);
  const reporter = { pid: client.pid, queuedAt: client.queuedAt };
  assert.strictEqual(planReport(undefined, "failed", reporter, success), "ignore", "final is final");
  assert.strictEqual(planReport(facts("ended"), "lost", reporter, success), "ignore");
  assert.deepStrictEqual(planReport(undefined, undefined, reporter, success), { end: "succeeded" });
  assert.deepStrictEqual(
    planReport(facts("ended", { detached: true }), undefined, reporter, success),
    { end: "succeeded" },
    "the report replaces a provisional lost",
  );
  assert.deepStrictEqual(planReport(facts("running", { detached: true }), undefined, reporter, success), {
    end: "succeeded",
  });
  assert.deepStrictEqual(planReport(facts("running", { cancelPending: true }), undefined, reporter, ended(true, 1)), {
    end: "cancelled",
  });
  assert.strictEqual(planReport(facts("waiting"), undefined, reporter, success), "ignore", "a job that never ran");
  assert.strictEqual(
    planReport(facts("running"), undefined, { pid: client.pid, queuedAt: 2000 }, success),
    "ignore",
    "another client's record",
  );
  assert.deepStrictEqual(
    planReport(facts("running"), undefined, { pid: 0, queuedAt: 0 }, ended(true, 2)),
    { end: "failed" },
    "an older client sends no identity",
  );
}

/** A cancel of a GPU holder reaches the --gpu run that started it. */
export function checkCancelTargets(assert: AssertModule): void {
  const turn = { id: 1, turn: null };
  const holder = { id: 2, turn: 1 };
  const other = { id: 3, turn: null };
  const older = { id: 4, turn: null }; // a holder of an older client names no turn
  const all = [turn, holder, other, older];
  assert.deepStrictEqual(cancelTargets([holder], all), [holder, turn]);
  assert.deepStrictEqual(cancelTargets([turn], all), [turn], "a turn alone adds nothing");
  assert.deepStrictEqual(cancelTargets([holder, turn], all), [holder, turn], "no job twice");
  assert.deepStrictEqual(cancelTargets([older], all), [older]);
}

export const JobStateChecks: Record<string, (assert: AssertModule) => void> = {
  checkIdentity,
  checkOutcomeOfEnd,
  checkRegistration,
  checkClose,
  checkObservation,
  checkReport,
  checkCancelTargets,
};
