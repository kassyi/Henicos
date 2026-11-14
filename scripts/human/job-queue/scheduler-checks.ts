// Unit checks of Scheduler and RunHistory, run by the self-test (and loadable on their own by mutation probes).

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { HistoryRunsPerCommand, LongestRunMs, OverrunGuessMs, UnknownTypicalMs } from "./constants.ts";
import { EstimateChecks } from "./estimate-checks.ts";
import { RunHistory } from "./history.ts";
import { Scheduler, type Estimate, type SchedulerJob } from "./scheduler.ts";

type AssertModule = typeof Assert;

/** A job for the checks: id, weight, arrival, GPU, recorded run times. */
export function job(
  id: number,
  weight: number,
  queuedAt: number,
  gpu = false,
  estimate: Estimate | null = null,
): SchedulerJob {
  return { id, weight, queuedAt, gpu, estimate, grantedAt: 0, pin: null, before: null, after: null };
}

export const times = (typicalS: number, longestS: number): Estimate => ({
  typicalMs: typicalS * 1000,
  longestMs: longestS * 1000,
});

/** A job that already runs, for adopt (which reads a grant time of 0 as unset). */
const running = (id: number, weight: number, longestS: number): SchedulerJob =>
  Object.assign(job(id, weight, 0, false, times(longestS, longestS)), { grantedAt: 1 });

/** FIFO order, the head is never overtaken, GPU holders are granted at once, weights are clamped, the startup grace. */
export function checkBasics(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(4, 0, "fifo");
  const a = job(1, 2, 1);
  const b = job(2, 2, 2);
  const c = job(3, 4, 3);
  const d = job(4, 1, 4);
  assert.deepStrictEqual(s.enqueue(a), [a]);
  assert.deepStrictEqual(s.enqueue(b), [b]);
  assert.deepStrictEqual(s.enqueue(c), []);
  assert.deepStrictEqual(s.enqueue(d), [], "a light job must not overtake the waiting head");
  assert.strictEqual(c.weight, 4);
  const g = job(5, 9, 5, true);
  assert.deepStrictEqual(s.enqueue(g), [g], "a GPU holder is granted at once, over the capacity");
  assert.strictEqual(g.weight, 4, "weight is clamped to the capacity");
  assert.strictEqual(s.used, 8);
  assert.deepStrictEqual(s.remove(a), []);
  assert.deepStrictEqual(s.remove(g), []);
  assert.deepStrictEqual(s.remove(b), [c]);
  assert.deepStrictEqual(s.remove(c), [d]);
  s.adopt(job(6, 4, 0));
  assert.strictEqual(s.used, 5, "an adopted job counts even over the capacity");
  const held = new Scheduler<SchedulerJob>(2, 1000);
  const h = job(7, 1, 1);
  assert.deepStrictEqual(held.enqueue(h, 500), [], "nothing is granted during the startup grace");
  assert.deepStrictEqual(held.grant(1000), [h]);
}

/**
 * Backfilling: a later job starts only when it ends before the first job's reserved start or fits in what it leaves.
 */
export function checkBackfill(assert: AssertModule): void {
  for (const policy of ["backfill", "fifo"] as const) {
    const s = new Scheduler<SchedulerJob>(4, 0, policy);
    const a = job(1, 3, 0, false, times(80, 100));
    const b = job(2, 2, 1);
    const c = job(3, 1, 2, false, times(40, 50));
    const d = job(4, 1, 3, false, times(400, 500));
    assert.deepStrictEqual(s.enqueue(a, 0), [a]);
    assert.deepStrictEqual(s.enqueue(b, 1), []);
    const expected = policy === "fifo" ? [] : [c];
    assert.deepStrictEqual(s.enqueue(c, 2), expected, `${policy}: a job that ends before the first one can start`);
    assert.deepStrictEqual(s.enqueue(d, 3), [], `${policy}: no free slot left`);
    assert.deepStrictEqual(s.waiting[0], b, `${policy}: the first job stays first`);
  }
  {
    // The first job waits for 3 slots; when a 2-slot job ends, 4 are free: one slot to spare for a long job
    const s = new Scheduler<SchedulerJob>(4, 0, "backfill");
    const a = job(1, 2, 0, false, times(80, 100));
    const b = job(2, 3, 1);
    const c = job(3, 1, 2, false, times(400, 500));
    const d = job(4, 1, 3);
    s.enqueue(a, 0);
    s.enqueue(b, 1);
    assert.deepStrictEqual(s.enqueue(c, 2), [c], "a long job fits in the slot the first job leaves free");
    assert.deepStrictEqual(
      s.enqueue(d, 3),
      [],
      "the spare slot is taken; an unknown job could run past the reserved start",
    );
    assert.deepStrictEqual(s.remove(a, 100000), [b], "the first job starts at its reserved time");
  }
  {
    // Several jobs backfilled in one pass: the spare slot and the idle slots are each used once
    const s = new Scheduler<SchedulerJob>(4, 1000, "backfill");
    s.adopt(running(9, 2, 100), 500);
    const queued = [job(2, 3, 1), job(3, 1, 2, false, times(500, 500)), job(4, 1, 3, false, times(500, 500))];
    queued.forEach((j) => s.enqueue(j, 500));
    assert.deepStrictEqual(s.grant(1000), [queued[1]], "one spare slot: only one long job");
    const t = new Scheduler<SchedulerJob>(4, 1000, "backfill");
    t.adopt(running(9, 3, 100), 500);
    const short = [job(2, 4, 1), job(3, 1, 2, false, times(50, 50)), job(4, 1, 3, false, times(50, 50))];
    short.forEach((j) => t.enqueue(j, 500));
    assert.deepStrictEqual(t.grant(1000), [short[1]], "one idle slot: only one short job");
    const u = new Scheduler<SchedulerJob>(4, 1000, "backfill");
    u.adopt(running(9, 3, 100), 500);
    const late = [job(2, 4, 1), job(3, 1, 2, false, times(500, 500))];
    late.forEach((j) => u.enqueue(j, 500));
    assert.deepStrictEqual(u.grant(1000), [], "a job that would run past the reserved start, with no slot to spare");
    // Two running jobs: the one that ends first frees enough; the reservation leaves no slot to spare
    const v = new Scheduler<SchedulerJob>(4, 1000, "backfill");
    v.adopt(running(7, 1, 500), 500);
    v.adopt(running(8, 2, 100), 500);
    const behind = [job(2, 3, 1), job(3, 1, 2, false, times(1000, 1000))];
    behind.forEach((j) => v.enqueue(j, 500));
    assert.deepStrictEqual(v.grant(1000), [], "the ends are taken in time order, not in the order the jobs started");
  }
  {
    // A running job with no recorded time, or past its longest one: its end is unknown, nothing is backfilled,
    // not even into a slot that would be spare
    const s = new Scheduler<SchedulerJob>(4, 0, "backfill");
    s.enqueue(job(1, 2, 0), 0);
    s.enqueue(job(2, 3, 1), 1);
    assert.deepStrictEqual(s.enqueue(job(3, 1, 2, false, times(1, 1)), 2), [], "unknown end of a running job");
    const t = new Scheduler<SchedulerJob>(4, 0, "backfill");
    t.enqueue(job(1, 2, 0, false, times(5, 10)), 0);
    t.enqueue(job(2, 3, 1), 1);
    assert.deepStrictEqual(t.enqueue(job(3, 1, 20000, false, times(1, 1)), 20000), [], "running past its longest time");
  }
}

/** Response ratio order: a short job goes first, and a long one rises the longer it waits. */
export function checkResponseRatio(assert: AssertModule): void {
  {
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    const x = job(1, 1, 0);
    const long = job(2, 1, 0, false, times(600, 600));
    const short = job(3, 1, 10000, false, times(30, 30));
    s.enqueue(x, 0);
    s.enqueue(long, 0);
    s.enqueue(short, 10000);
    assert.deepStrictEqual(s.remove(x, 60000), [short], "the short job overtakes the long one");
  }
  {
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    const x = job(1, 1, 0);
    const long = job(2, 1, 0, false, times(600, 600));
    const short = job(3, 1, 1000000, false, times(30, 30));
    s.enqueue(x, 0);
    s.enqueue(long, 0);
    s.enqueue(short, 1000000);
    assert.deepStrictEqual(s.remove(x, 1010000), [long], "a long job that waited long enough goes first");
  }
  {
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    const x = job(1, 1, 0);
    const unknown = job(2, 1, 0);
    const known = job(3, 1, 0, false, times(90, 90));
    s.enqueue(x, 0);
    s.enqueue(known, 0);
    s.enqueue(unknown, 0);
    assert.deepStrictEqual(s.remove(x, 30000), [unknown], "a job with no record counts as UnknownTypicalMs");
  }
}

/** Forecast: grants played forward with the typical times; a job past its times is expected to end soon. */
export function checkForecast(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(2, 0, "fifo");
  const a = job(1, 2, 0, false, times(100, 300));
  const b = job(2, 1, 1, false, times(50, 50));
  const c = job(3, 1, 2, false, times(20, 20));
  const d = job(4, 2, 3);
  [a, b, c, d].forEach((j) => s.enqueue(j, 0));
  let f = s.forecast(10000);
  assert.deepStrictEqual(f.get(a), { startAt: 0, endAt: 100000 });
  assert.deepStrictEqual(f.get(b), { startAt: 100000, endAt: 150000 });
  assert.deepStrictEqual(f.get(c), { startAt: 100000, endAt: 120000 });
  assert.deepStrictEqual(f.get(d), { startAt: 150000, endAt: 150000 + UnknownTypicalMs });
  assert.strictEqual(s.waiting.length, 3, "the forecast does not change the scheduler");
  f = s.forecast(200000);
  assert.strictEqual(f.get(a)?.endAt, 300000, "past its typical time: its longest time");
  f = s.forecast(400000);
  assert.strictEqual(f.get(a)?.endAt, 400000 + OverrunGuessMs, "past its longest time: soon");
  // hrrn: a short job overtakes the waiting long one while slots are free, with no job ending in between
  const h = new Scheduler<SchedulerJob>(4, 0, "hrrn");
  const busy = job(1, 2, 0, false, times(900, 900));
  const wide = job(2, 4, -600000, false, times(3600, 3600)); // has waited 10 minutes
  const narrow = job(3, 2, 0, false, times(1200, 1200));
  h.enqueue(busy, 0);
  h.enqueue(wide, 0);
  h.enqueue(narrow, 0);
  assert.deepStrictEqual(h.waiting, [wide, narrow], "the wide job waited longer: it is first");
  // The ratios cross at 300 s: (300 + 600 + 3600) / 3600 = (300 + 1200) / 1200
  const expected = h.forecast(0).get(narrow)?.startAt ?? NaN;
  assert.ok(
    expected > 300000 && expected < 900000,
    `the forecast sees the overtaking before the running job ends (${expected})`,
  );
  assert.deepStrictEqual(h.grant(400000), [narrow], "once its ratio passed the wide one, it fits and starts");
  // During the startup grace the positions already follow the ratio
  const held = new Scheduler<SchedulerJob>(4, 1000, "hrrn");
  const long = job(4, 1, 0, false, times(600, 600));
  const short = job(5, 1, 1, false, times(10, 10));
  held.enqueue(long, 500);
  held.enqueue(short, 500);
  assert.deepStrictEqual(held.waiting, [short, long], "ordered while nothing is granted");
  // A replay that would take too many steps stops; the jobs it did not reach have no times
  const slow = new Scheduler<SchedulerJob>(8, 0, "hrrn");
  slow.enqueue(job(1, 2, 0, false, times(LongestRunMs / 1000, LongestRunMs / 1000)), 0);
  const far = job(2, 8, 0);
  slow.enqueue(far, 0);
  const started = Date.now();
  assert.strictEqual(slow.forecast(0).get(far), undefined, "not reached within ForecastSteps");
  assert.ok(Date.now() - started < 1000, "the forecast stays fast");
}

/** move: pinned jobs first, --before right in front of another job wherever it is, cycles refused, reset. */
export function checkPlacement(assert: AssertModule): void {
  const ids = (s: Scheduler<SchedulerJob>): number[] => s.waiting.map((j) => j.id);
  {
    const s = new Scheduler<SchedulerJob>(1, 0, "fifo");
    const x = job(1, 1, 0);
    const [a, b, c] = [job(2, 1, 1), job(3, 1, 2), job(4, 1, 3)] as [SchedulerJob, SchedulerJob, SchedulerJob];
    [x, a, b, c].forEach((j) => s.enqueue(j, 10));
    s.pinFirst(c);
    s.order(10);
    assert.deepStrictEqual(ids(s), [4, 2, 3], "--first goes ahead of the arrival order");
    s.pinFirst(b);
    s.order(10);
    assert.deepStrictEqual(ids(s), [3, 4, 2], "a later --first goes ahead of an earlier one");
    assert.deepStrictEqual(s.remove(x, 20), [b], "the pinned job is granted first");
    s.resetPlace(c);
    s.order(20);
    assert.deepStrictEqual(ids(s), [2, 4], "reset returns to the arrival order");
  }
  {
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    const x = job(1, 1, 0);
    const a = job(2, 1, 0, false, times(10, 10)); // short: first by ratio
    const b = job(3, 1, 0, false, times(600, 600));
    const c = job(4, 1, 0, false, times(3000, 3000));
    [x, a, b, c].forEach((j) => s.enqueue(j, 0));
    s.order(60000);
    assert.deepStrictEqual(ids(s), [2, 3, 4], "the response ratio order");
    assert.strictEqual(s.placeBefore(c, a), null);
    s.order(60000);
    assert.deepStrictEqual(ids(s), [4, 2, 3], "--before: right in front of the target");
    assert.strictEqual(s.placeBefore(b, c), null);
    s.order(60000);
    assert.deepStrictEqual(ids(s), [3, 4, 2], "a chain keeps its order");
    assert.match(s.placeBefore(a, b) ?? "", /already goes before/, "a cycle is refused");
    assert.match(s.placeBefore(a, a) ?? "", /itself/);
    assert.match(s.placeBefore(a, x) ?? "", /must be waiting/, "the target must wait");
    // A cycle brought back by a restart does not hang the order and keeps every job once
    // A cycle brought back by a restart: its jobs keep the policy order, a job placed before one of them still goes
    // right in front of it, and placeBefore still ends
    a.before = b.id;
    b.before = a.id;
    c.before = a.id;
    s.order(60000);
    assert.deepStrictEqual(ids(s), [4, 2, 3], "the jobs of a cycle keep the policy order");
    assert.strictEqual(s.placeBefore(c, b), null, "placeBefore ends when a cycle lies on the walk");
    const self = job(5, 1, 0, false, times(5, 5));
    s.enqueue(self, 60000);
    self.before = self.id;
    s.order(60000);
    assert.deepStrictEqual(ids(s), [5, 2, 4, 3], "a job placed before itself keeps the policy order");
  }
  {
    // Backfilling still reserves the start of a pinned first job: a long job that would delay it does not start
    const build = (): [Scheduler<SchedulerJob>, SchedulerJob, SchedulerJob] => {
      const s = new Scheduler<SchedulerJob>(4, 1000, "backfill");
      s.adopt(running(9, 2, 100), 500);
      const long = job(2, 1, 1, false, times(500, 500));
      const wide = job(3, 4, 2);
      s.enqueue(long, 500);
      s.enqueue(wide, 500);
      return [s, long, wide];
    };
    const [plain, long] = build();
    assert.deepStrictEqual(plain.grant(1000), [long], "by arrival the long job is first and fits");
    const [pinned, , wide] = build();
    pinned.pinFirst(wide);
    assert.deepStrictEqual(pinned.grant(1000), [], "pinned first, the wide job keeps its reserved start");
  }
}

/**
 * --after: a job is granted on the recorded outcome of its predecessor only (succeeded, or any with --keep-if-failed),
 * never on its absence; it does not hold up the jobs behind it, and is forecast after the predecessor's end.
 */
export function checkDependencies(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(2, 0, "fifo");
  const outcomes = new Map<number, string>();
  s.outcomes = outcomes;
  const a = job(1, 1, 0, false, times(100, 100));
  const b = Object.assign(job(2, 1, 1, false, times(10, 10)), { after: 1 });
  const c = job(3, 1, 2);
  assert.deepStrictEqual(s.enqueue(a, 0), [a]);
  assert.deepStrictEqual(s.enqueue(b, 0), [], "a job waits for its predecessor although a slot is free");
  assert.ok(s.isBlocked(b));
  assert.deepStrictEqual(
    s.forecast(0).get(b),
    { startAt: 100000, endAt: 110000 },
    "forecast after the predecessor ends",
  );
  assert.deepStrictEqual(s.enqueue(c, 0), [c], "a blocked first job does not hold up the next one");
  assert.deepStrictEqual(s.remove(a, 5), [], "the predecessor left, but no outcome is recorded: still blocked");
  outcomes.set(1, "succeeded");
  assert.deepStrictEqual(s.grant(6), [b], "granted once the predecessor succeeded");
  const g = Object.assign(job(4, 1, 3, true), { after: 3 });
  assert.deepStrictEqual(s.enqueue(g, 7), [], "a GPU holder also waits for its predecessor");
  s.remove(c, 8);
  outcomes.set(3, "succeeded");
  assert.deepStrictEqual(s.grant(9), [g]);
  // The outcome decides, not the presence: unknown and failed block, --keep-if-failed runs after any outcome
  const later = Object.assign(job(5, 1, 4), { after: 99 });
  const keeping = Object.assign(job(6, 1, 4), { after: 99, keepIfFailed: true });
  assert.deepStrictEqual([s.isBlocked(later), s.isBlocked(keeping)], [true, true], "no outcome yet: blocked");
  outcomes.set(99, "failed");
  assert.deepStrictEqual(
    [s.isBlocked(later), s.isBlocked(keeping)],
    [true, false],
    "failed: only --keep-if-failed runs",
  );
  // Backfilling passes over a blocked job, however well it would fit
  const filling = new Scheduler<SchedulerJob>(4, 1000, "backfill");
  filling.adopt(running(9, 2, 100), 500);
  const wide = job(2, 4, 1);
  const blocked = Object.assign(job(3, 1, 2, false, times(10, 10)), { after: 9 });
  const free = job(4, 1, 3, false, times(10, 10));
  [wide, blocked, free].forEach((j) => filling.enqueue(j, 500));
  assert.deepStrictEqual(filling.grant(1000), [free], "a blocked job is not backfilled");
}

/**
 * A detached waiting job (its client is away) keeps its place but is not granted, also as a GPU holder or a GPU turn,
 * and does not hold up the jobs behind it; it is granted once its client is back.
 */
export function checkDetached(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(2, 0, "fifo");
  const away = Object.assign(job(1, 2, 0), { detached: true });
  const next = job(2, 1, 1);
  const holder = Object.assign(job(3, 1, 2, true), { detached: true });
  const turn = Object.assign(job(4, 1, 3), { gpuTurn: true, detached: true });
  assert.deepStrictEqual(s.enqueue(away, 0), [], "nobody would receive the grant");
  assert.deepStrictEqual(s.enqueue(next, 0), [next], "a detached first job does not hold up the next one");
  assert.deepStrictEqual([s.enqueue(holder, 0), s.enqueue(turn, 0)], [[], []], "nor a GPU holder or turn");
  assert.strictEqual(s.awaitsPredecessor(away), false, "it waits for its client, not for a predecessor");
  s.remove(next, 1);
  away.detached = false;
  assert.deepStrictEqual(s.grant(2), [away], "granted once its client is back");
}

/** GPU turns: one at a time, in the order of the slots, taking no slots, and never while a GPU holder runs. */
export function checkGpuTurns(assert: AssertModule): void {
  const turn = (id: number, queuedAt: number, estimate: Estimate | null = null): SchedulerJob =>
    Object.assign(job(id, 2, queuedAt, false, estimate), { gpuTurn: true });
  {
    const s = new Scheduler<SchedulerJob>(2, 0, "fifo");
    const [t1, t2, t3] = [turn(1, 0), turn(2, 1), turn(3, 2)];
    const cpu = job(4, 2, 3);
    assert.deepStrictEqual(s.enqueue(t1, 10), [t1]);
    assert.strictEqual(t1.weight, 0, "a turn takes no slots");
    assert.deepStrictEqual(s.enqueue(t2, 10), [], "one turn at a time");
    assert.deepStrictEqual(s.enqueue(t3, 10), []);
    assert.deepStrictEqual(s.enqueue(cpu, 10), [cpu], "the slots are free for other jobs");
    assert.deepStrictEqual(s.remove(t1, 20), [t2], "the next turn in order, not a later one");
    const holder = job(5, 1, 4, true);
    assert.deepStrictEqual(s.enqueue(holder, 20), [holder], "a GPU holder is granted at once");
    assert.deepStrictEqual(s.remove(t2, 30), [], "no turn while a GPU holder runs");
    assert.deepStrictEqual(s.remove(holder, 40), [t3]);
  }
  {
    const s = new Scheduler<SchedulerJob>(2, 0, "hrrn");
    const first = turn(1, 0);
    const long = turn(2, 0, times(600, 600));
    const short = turn(3, 10000, times(30, 30));
    [first, long, short].forEach((j) => s.enqueue(j, 10000));
    assert.deepStrictEqual(s.remove(first, 60000), [short], "the turns follow the response ratio");
    const blocked = Object.assign(turn(4, 0), { after: 2 });
    s.enqueue(blocked, 60000);
    assert.deepStrictEqual(s.remove(short, 70000), [long], "a turn waits for its predecessor");
    s.outcomes = new Map([[2, "succeeded"]]);
    assert.deepStrictEqual(s.remove(long, 80000), [blocked], "and gets the GPU once its predecessor succeeded");
  }
  {
    // A running turn with no recorded time holds no slots, so it does not stop backfilling
    const s = new Scheduler<SchedulerJob>(4, 1000, "backfill");
    s.adopt(turn(9, 0), 500);
    s.adopt(running(8, 2, 100), 500);
    const wide = job(2, 4, 1);
    const short = job(3, 1, 2, false, times(10, 10));
    [wide, short].forEach((j) => s.enqueue(j, 500));
    assert.deepStrictEqual(s.grant(1000), [short], "backfilled beside a running turn");
  }
}

/** A test run for the memory checks: weight 1 and a memory budget. */
const testRun = (id: number, memoryMb: number, queuedAt: number, estimate: Estimate | null = null): SchedulerJob =>
  Object.assign(job(id, 1, queuedAt, false, estimate), { memoryMb });

/**
 * The memory pool: test runs start while their budgets fit, a build passes a test run held back by memory, a run
 * larger than the pool runs alone, and backfilling leaves the first job's budget free.
 */
export function checkMemory(assert: AssertModule): void {
  {
    const s = new Scheduler<SchedulerJob>(8, 0, "fifo", 6000);
    const [a, b, c] = [testRun(1, 2880, 1), testRun(2, 2880, 2), testRun(3, 2880, 3)];
    assert.deepStrictEqual(s.enqueue(a), [a]);
    assert.deepStrictEqual(s.enqueue(b), [b]);
    assert.deepStrictEqual(s.enqueue(c), [], "a third budget does not fit in the pool");
    assert.strictEqual(s.memoryUsed, 5760);
    assert.deepStrictEqual(s.remove(a), [c]);
    assert.strictEqual(s.memoryUsed, 5760, "an ended run gives its budget back");
  }
  {
    const s = new Scheduler<SchedulerJob>(8, 0, "backfill", 6000);
    const a = testRun(1, 4000, 1, times(100, 100));
    const b = testRun(2, 4000, 2);
    const build = job(3, 1, 3);
    s.enqueue(a, 3);
    assert.deepStrictEqual(s.enqueue(b, 3), []);
    assert.deepStrictEqual(s.enqueue(build, 3), [build], "a build takes no memory and passes the held test run");
  }
  {
    const s = new Scheduler<SchedulerJob>(8, 0, "fifo", 1000);
    const [a, b] = [testRun(1, 2880, 1), testRun(2, 2880, 2)];
    assert.deepStrictEqual(s.enqueue(a), [a], "a run larger than the pool runs when it is alone");
    assert.deepStrictEqual(s.enqueue(b), []);
    assert.deepStrictEqual(s.remove(a), [b]);
  }
  {
    // The first job waits for slots; a long test run must not take the memory it will need, a build may run
    const s = new Scheduler<SchedulerJob>(4, 0, "backfill", 6000);
    const a = job(1, 3, 0, false, times(100, 100));
    const first = Object.assign(job(2, 2, 1), { memoryMb: 3000 });
    const long = testRun(3, 3500, 2, times(400, 500));
    const build = job(4, 1, 3, false, times(400, 500));
    s.enqueue(a, 0);
    s.enqueue(first, 1);
    assert.deepStrictEqual(s.enqueue(long, 2), [], "a backfilled run leaves the first job's budget free");
    assert.deepStrictEqual(s.enqueue(build, 3), [build]);
  }
  {
    // A short test run that would end in time is still not backfilled when its budget does not fit now
    const s = new Scheduler<SchedulerJob>(4, 0, "backfill", 6000);
    const a = Object.assign(job(1, 3, 0, false, times(100, 100)), { memoryMb: 4000 });
    const first = job(2, 2, 1);
    const short = testRun(3, 3000, 2, times(10, 10));
    s.enqueue(a, 0);
    s.enqueue(first, 1);
    assert.deepStrictEqual(s.enqueue(short, 2), [], "a backfilled run must fit in the pool now");
  }
  {
    const s = new Scheduler<SchedulerJob>(8, 0, "fifo", 6000);
    const turn = Object.assign(testRun(1, 2880, 1), { gpuTurn: true });
    s.enqueue(turn);
    assert.strictEqual(s.memoryUsed, 0, "a GPU turn takes no memory; its command registers its own");
    const [a, b] = [testRun(2, 4000, 2), testRun(3, 4000, 3)];
    s.enqueue(a, 3);
    s.enqueue(b, 3);
    const forecast = s.forecast(3);
    assert.strictEqual(forecast.get(b)?.startAt, forecast.get(a)?.endAt, "the forecast waits for the memory too");
  }
}

/** Run history: median and longest of the last runs per command, kept on disk. */
export function checkRunHistory(assert: AssertModule): void {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-history-"));
  try {
    const file = path.join(directory, "history.json");
    const history = new RunHistory(file, () => {});
    assert.strictEqual(history.estimate("dotnet build"), null);
    [10, 30, 20].forEach((ms) => history.record("dotnet  build ", ms));
    assert.deepStrictEqual(history.estimate("dotnet build"), { typicalMs: 20, longestMs: 30 }, "spaces do not matter");
    history.record("dotnet build", 40);
    assert.deepStrictEqual(history.estimate("dotnet build"), { typicalMs: 25, longestMs: 40 });
    for (let i = 0; i < HistoryRunsPerCommand; i++) {
      history.record("dotnet build", 5);
    }
    assert.deepStrictEqual(
      new RunHistory(file, () => {}).estimate("dotnet build"),
      { typicalMs: 5, longestMs: 5 },
      "only the last runs count",
    );
    // A damaged file loses its bad entries, never the scheduler
    fs.writeFileSync(
      file,
      JSON.stringify({ nextId: -3, commands: { a: { runs: 5 }, b: 7, c: null, d: { runs: [1, "x", -2, 3] } } }),
    );
    const damaged = new RunHistory(file, () => {});
    assert.strictEqual(damaged.nextId, 1, "a negative next id is ignored");
    assert.deepStrictEqual(damaged.estimate("d"), { typicalMs: 2, longestMs: 3 }, "the valid runs of an entry stay");
    ["a", "b", "c"].forEach((key) => damaged.record(key, 10));
    assert.deepStrictEqual(damaged.estimate("a"), { typicalMs: 10, longestMs: 10 });
    fs.writeFileSync(file, "[1, 2");
    assert.strictEqual(new RunHistory(file, () => {}).estimate("a"), null, "unreadable JSON starts empty");
    // Command texts that are names of Object members are plain keys
    const plain = new RunHistory(file, () => {});
    ["constructor", "toString", "__proto__"].forEach((key) => plain.record(key, 7));
    assert.deepStrictEqual(new RunHistory(file, () => {}).estimate("__proto__"), { typicalMs: 7, longestMs: 7 });
    assert.strictEqual(new RunHistory(file, () => {}).estimate("hasOwnProperty"), null);
    // Times beyond any real run are damaged input
    fs.writeFileSync(file, JSON.stringify({ commands: { huge: { runs: [1e15, 5] } } }));
    assert.deepStrictEqual(new RunHistory(file, () => {}).estimate("huge"), { typicalMs: 5, longestMs: 5 });
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}

/** start <id>: granted at once over the slots and the memory pool, never as a GPU turn or before its predecessor. */
export function checkStartNow(assert: AssertModule): void {
  const s = new Scheduler<SchedulerJob>(2, 0, "hrrn", 1000);
  const a = Object.assign(job(1, 2, 1), { memoryMb: 800 });
  const b = Object.assign(job(2, 1, 2), { memoryMb: 800 });
  assert.deepStrictEqual(s.enqueue(a), [a]);
  assert.deepStrictEqual(s.enqueue(b), [], "no free slot");
  b.startNow = true;
  assert.deepStrictEqual(s.grant(), [b], "started by hand: granted over the slots and the memory pool");
  assert.strictEqual(s.used, 3);
  assert.strictEqual(s.memoryUsed, 1600);
  const turn = Object.assign(job(3, 1, 3), { gpuTurn: true, startNow: true });
  const holder = job(4, 1, 4, true);
  s.enqueue(holder);
  assert.deepStrictEqual(s.enqueue(turn), [], "a GPU turn waits for the GPU even when started by hand");
  const blocked = Object.assign(job(5, 1, 5), { after: 99, startNow: true });
  assert.deepStrictEqual(s.enqueue(blocked), [], "and so does a job whose predecessor has not ended");
}

export const SchedulerChecks: Record<string, (assert: AssertModule) => void> = {
  ...EstimateChecks,
  checkBasics,
  checkBackfill,
  checkResponseRatio,
  checkForecast,
  checkPlacement,
  checkDependencies,
  checkDetached,
  checkGpuTurns,
  checkMemory,
  checkStartNow,
  checkRunHistory,
};
