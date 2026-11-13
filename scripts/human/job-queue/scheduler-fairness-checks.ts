// Fairness checks of Scheduler: the bound on waiting, the round-robin across worktrees for the CPU slots and the GPU
// turns, and the long GPU job that short jobs of another worktree no longer starve. Run by the self-test.

import type * as Assert from "node:assert";
import { UnknownTypicalMs } from "./constants.ts";
import { Scheduler, type SchedulerJob } from "./scheduler.ts";
import { job, times } from "./scheduler-checks.ts";

type AssertModule = typeof Assert;

/** A job of a worktree, for the fairness checks: a GPU turn or a 1-slot job with a recorded run time. */
const of = (worktree: string, id: number, queuedAt: number, typicalS: number, gpuTurn = true): SchedulerJob =>
  Object.assign(job(id, 1, queuedAt, false, times(typicalS, typicalS)), { worktree, gpuTurn });

/**
 * Plays a scheduler forward from `from` in steps of `stepMs` (5 s, like RegrantMs): each job joins at its queuedAt and
 * ends its typical run time after its grant. Returns when each job was granted.
 */
function play(s: Scheduler<SchedulerJob>, arrivals: SchedulerJob[], from: number, until: number) {
  const started = new Map<SchedulerJob, number>();
  const pending = [...arrivals].sort((a, b) => a.queuedAt - b.queuedAt);
  for (let t = from; t <= until; t += 5000) {
    s.running
      .filter((other) => other.grantedAt + (other.estimate?.typicalMs ?? UnknownTypicalMs) <= t)
      .forEach((other) => s.detach(other));
    while (pending.length > 0 && (pending[0] as SchedulerJob).queuedAt <= t) {
      s.add(pending.shift() as SchedulerJob);
    }
    s.grant(t).forEach((granted) => started.set(granted, t));
  }
  return started;
}

/**
 * Fairness (hrrn): the round-robin across worktrees, for the GPU turns and the slots, and the bound on the wait, which
 * puts a job that has waited longer than max(OverdueMs, its typical time) ahead; manual places still go first.
 */
export function checkFairness(assert: AssertModule): void {
  const minute = 60 * 1000;
  // One worktree queues a 2-minute GPU job every minute for an hour (more than the GPU can run)
  const stream = (worktree: string): SchedulerJob[] =>
    Array.from({ length: 60 }, (_, i) => of(worktree, 100 + i, i * minute, 120));
  {
    // A long GPU job of another worktree gets the next turn: the round-robin, well before the bound
    const long = of("b", 1, 30000, 1200);
    const started = play(new Scheduler<SchedulerJob>(4, 0, "hrrn"), [...stream("a"), long], 0, 60 * minute);
    assert.strictEqual(started.get(long), 2 * minute, "the other worktree's job takes the turn after the first one");
  }
  {
    // The same long job queued from the busy worktree: the round-robin cannot help, the bound does
    const long = of("a", 1, 30000, 1200);
    const started = play(new Scheduler<SchedulerJob>(4, 0, "hrrn"), [...stream("a"), long], 0, 60 * minute);
    const at = started.get(long) ?? Infinity;
    const bound = 30000 + 20 * minute; // its typical time, above OverdueMs
    assert.ok(at > bound && at <= bound + 2 * minute, `the turn at the first end past the bound (${at})`);
  }
  {
    // Slots: a worktree that queued five jobs and one that queued two take turns
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    const many = [0, 1, 2, 3, 4].map((i) => of("a", 1 + i, i * 1000, 60, false));
    const few = [5, 6].map((i) => of("b", 1 + i, i * 1000, 60, false));
    const started = play(s, [...many, ...few], 0, 10 * minute);
    const order = [...started.keys()].map((j) => j.worktree);
    assert.deepStrictEqual(order, ["a", "b", "a", "b", "a", "a", "a"], "one job per worktree per round");
  }
  {
    // The bound overrides the ratio, from max(OverdueMs, typical) on, oldest first; the clock is the wait since the
    // job was queued (the times are shifted by a day, so that no other clock can pass for it)
    const day = 24 * 60 * minute;
    const at = (now: number, waiting: SchedulerJob[]): number[] => {
      const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
      waiting.forEach((j) => s.add(Object.assign(j, { queuedAt: j.queuedAt + day })));
      s.order(now + day);
      return s.waiting.map((j) => j.id);
    };
    const long = (): SchedulerJob => of("a", 1, 0, 15 * 60, false); // its bound: 15 minutes
    const short = (): SchedulerJob => of("a", 2, 0, 30, false);
    assert.deepStrictEqual(at(14 * minute, [long(), short()]), [2, 1], "before its bound: the ratio");
    assert.deepStrictEqual(at(16 * minute, [long(), short()]), [1, 2], "past its bound: ahead of the short one");
    const brief = (): SchedulerJob => of("a", 1, 0, 120, false); // its bound: OverdueMs
    const briefer = (): SchedulerJob => of("a", 2, 2 * minute, 10, false); // not overdue at 11 minutes
    assert.deepStrictEqual(at(9 * minute, [brief(), briefer()]), [2, 1], "under OverdueMs: the ratio");
    assert.deepStrictEqual(at(11 * minute, [brief(), briefer()]), [1, 2], "past OverdueMs");
    const older = of("a", 3, 0, 300, false);
    const younger = of("a", 4, minute, 30, false);
    assert.deepStrictEqual(at(30 * minute, [younger, older]), [3, 4], "the overdue jobs: oldest first");
  }
  {
    // Manual places go ahead of the round-robin and of the bound
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    s.enqueue(of("x", 1, 0, 60, false), 0);
    const [a1, a2, b1] = [of("a", 2, 0, 60, false), of("a", 3, 0, 60, false), of("b", 4, 0, 60, false)];
    const overdue = of("c", 5, -60 * minute, 60, false);
    [a1, a2, b1, overdue].forEach((j) => s.add(j));
    s.rotation.slots.set("a", ++s.rotation.grants); // a was served last
    s.order(0);
    assert.deepStrictEqual(
      s.waiting.map((j) => j.id),
      [5, 4, 2, 3],
      "the overdue job, then the worktree served longest ago",
    );
    s.pinFirst(a2);
    s.order(0);
    assert.deepStrictEqual(
      s.waiting.map((j) => j.id),
      [3, 5, 4, 2],
      "a pinned job goes first all the same",
    );
    assert.deepStrictEqual(s.remove(s.running[0] as SchedulerJob, 0), [a2]);
  }
  {
    // The forecast plays the round-robin on from where the scheduler is, and the bound as the replay's time passes
    const s = new Scheduler<SchedulerJob>(1, 0, "hrrn");
    s.enqueue(of("a", 1, 0, 60, false), 0); // a is served now
    const slots = [of("a", 2, -10000, 60, false), of("a", 3, -10000, 60, false), of("b", 4, 0, 60, false)];
    // A GPU turn of 13 minutes, then a 12-minute turn that turns overdue while short turns wait behind it
    s.enqueue(of("c", 5, 0, 13 * 60), 0);
    const turns = [of("c", 6, 0, 12 * 60), of("c", 7, 0, 60), of("c", 8, 0, 60)];
    [...slots, ...turns].forEach((j) => s.add(j));
    s.grant(0);
    const forecast = s.forecast(0);
    const started = play(s, [], 0, 60 * minute);
    for (const j of [...slots, ...turns]) {
      assert.strictEqual(forecast.get(j)?.startAt, started.get(j), `#${j.id}: the forecast start is the grant`);
    }
    assert.strictEqual(started.get(slots[2] as SchedulerJob), minute, "the other worktree's job is next");
    assert.strictEqual(started.get(turns[0] as SchedulerJob), 13 * minute, "the overdue turn before the short ones");
  }
  {
    // fifo and backfill keep the arrival order: no round-robin, no bound
    const s = new Scheduler<SchedulerJob>(1, 0, "backfill");
    const many = [0, 1, 2].map((i) => of("a", 1 + i, i * 1000, 60, false));
    const other = of("b", 4, 5000, 60, false);
    const started = play(s, [...many, other], 0, 10 * minute);
    assert.deepStrictEqual(
      [...started.keys()].map((j) => j.id),
      [1, 2, 3, 4],
      "backfill: arrival order",
    );
  }
}

export const FairnessChecks: Record<string, (assert: AssertModule) => void> = {
  checkFairness,
};
