// Unit checks of the turn resources of the Scheduler (resources.ts), run by the self-test.

import type * as Assert from "node:assert";
import { ProbeFreshMs } from "./constants.ts";
import { resourceOf, usageOf, type ProbeResult, type ResourceSpec, type ShareMode } from "./resources.ts";
import { Scheduler, type SchedulerJob } from "./scheduler.ts";
import { job, times } from "./scheduler-checks.ts";

type AssertModule = typeof Assert;

/** A resource: `slots` shared turns at most, with a probe or none. */
const spec = (name: string, slots: number, probe: boolean): ResourceSpec => ({
  name,
  slots,
  probe: probe ? { argv: ["probe"], min: 3072, timeoutMs: 2000, settleMs: 15000 } : null,
});

/** A GPU turn: arrival order by id unless `extra` says otherwise; `share` absent unless given. */
function turn(id: number, share: ShareMode | undefined, extra: Partial<SchedulerJob> = {}): SchedulerJob {
  const result = Object.assign(job(id, 2, id), { gpuTurn: true }, extra);
  if (share !== undefined) {
    result.share = share;
  }
  return result;
}

/** A GPU holder (the command under the GPU lock): tied to the turn that started it, or to none. */
const holder = (id: number, turnId: number | null): SchedulerJob =>
  Object.assign(job(id, 1, id, true), { turn: turnId });

const fresh = (s: Scheduler<SchedulerJob>, name: string, at: number, over: Partial<ProbeResult> = {}): ProbeResult => ({
  ok: true,
  value: 4000,
  at,
  generation: s.stateOf(name).generation,
  ...over,
});

const scheduler = (specs: ResourceSpec[], policy: "fifo" | "hrrn" = "fifo"): Scheduler<SchedulerJob> =>
  new Scheduler<SchedulerJob>(2, 0, policy, Infinity, specs);

/** Two shared turns at slots 2: the first at once, the second only after a probe result; no probe asks nothing. */
export function checkSharedNeedsProbe(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, true)]);
  const [a, b] = [turn(1, "shared"), turn(2, "shared")];
  assert.deepStrictEqual(s.enqueue(a, 0), [a], "the first shared turn needs no probe");
  assert.deepStrictEqual(s.enqueue(b, 100), [], "the second waits for a probe result");
  assert.deepStrictEqual(s.grant(20000), [], "time alone does not grant it");
  s.recordProbe("gpu", fresh(s, "gpu", 20000));
  assert.deepStrictEqual(s.grant(20000), [b], "a valid probe result grants it");
  const open = scheduler([spec("gpu", 2, false)]);
  const [c, d] = [turn(1, "shared"), turn(2, "shared")];
  assert.deepStrictEqual([...open.enqueue(c, 0), ...open.enqueue(d, 0)], [c, d], "without a probe, slots decide");
}

/** A third shared turn waits while two run, whatever the probe says; with one slot the shared turns go one by one. */
export function checkSharedFull(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, true)]);
  const [a, b, c] = [turn(1, "shared"), turn(2, "shared"), turn(3, "shared")];
  assert.deepStrictEqual(s.enqueue(a, 0), [a]);
  s.add(b);
  s.recordProbe("gpu", fresh(s, "gpu", 20000));
  assert.deepStrictEqual(s.grant(20000), [b]);
  s.recordProbe("gpu", fresh(s, "gpu", 20000));
  assert.deepStrictEqual(s.enqueue(c, 20000), [], "two run: the third does not start, probe or not");
  assert.deepStrictEqual(s.remove(a, 30000), [], "a slot is free, but the probe result is stale");
  s.recordProbe("gpu", fresh(s, "gpu", 30000));
  assert.deepStrictEqual(s.grant(30000), [c], "it starts when a slot is free and a probe result is valid");
  const one = scheduler([spec("gpu", 1, false)]);
  const [d, e] = [turn(1, "shared"), turn(2, "shared")];
  assert.deepStrictEqual(one.enqueue(d, 0), [d]);
  assert.deepStrictEqual(one.enqueue(e, 0), [], "slots 1: shared turns still go one at a time");
  assert.deepStrictEqual(one.remove(d, 10), [e]);
}

/** Only the head of a resource's turns is looked at: an exclusive head holds back a shared turn behind it. */
export function checkHeadOnly(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, false)]);
  const [s1, x, s2] = [turn(1, "shared"), turn(2, "exclusive"), turn(3, "shared")];
  assert.deepStrictEqual(s.enqueue(s1, 0), [s1]);
  assert.deepStrictEqual(s.enqueue(x, 1), [], "an exclusive turn waits for the shared one");
  assert.deepStrictEqual(s.enqueue(s2, 2), [], "a shared turn does not pass the exclusive head");
  assert.deepStrictEqual(s.remove(s1, 10), [x], "the exclusive turn runs when the shared one ends");
  assert.deepStrictEqual(s.remove(x, 20), [s2], "and the shared turn after it");
}

/** A turn without `share` is exclusive: it is not granted beside a shared turn, and none is granted beside it. */
export function checkShareDefaultsToExclusive(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, false)]);
  const [s1, plain, s2] = [turn(1, "shared"), turn(2, undefined), turn(3, "shared")];
  assert.deepStrictEqual(s.enqueue(s1, 0), [s1]);
  assert.deepStrictEqual(s.enqueue(plain, 1), [], "no share: not beside a shared turn");
  assert.deepStrictEqual(s.enqueue(s2, 2), []);
  assert.deepStrictEqual(s.remove(s1, 10), [plain]);
  assert.deepStrictEqual(s.remove(plain, 20), [s2]);
  const t = scheduler([spec("gpu", 2, false)]);
  const [p, q] = [turn(1, undefined), turn(2, "shared")];
  assert.deepStrictEqual(t.enqueue(p, 0), [p]);
  assert.deepStrictEqual(t.enqueue(q, 1), [], "no shared turn beside a turn without share");
  const bogus = turn(3, "exclusive");
  Object.assign(bogus, { share: "SHARED" });
  assert.deepStrictEqual(t.enqueue(bogus, 2), [], 'only the exact string "shared" shares');
}

/** A holder that no running turn started is exclusive; one tied to a running shared turn does not block. */
export function checkHolderTies(assert: AssertModule): void {
  const loose = scheduler([spec("gpu", 2, false)]);
  const h = holder(10, null);
  assert.deepStrictEqual(loose.enqueue(h, 0), [h], "a holder is granted at once");
  const s0 = turn(1, "shared");
  assert.deepStrictEqual(loose.enqueue(s0, 1), [], "a holder with turn null holds the GPU");
  assert.deepStrictEqual(loose.remove(h, 5), [s0]);
  const tied = scheduler([spec("gpu", 2, false)]);
  const [s1, s2] = [turn(1, "shared"), turn(2, "shared")];
  assert.deepStrictEqual(tied.enqueue(s1, 0), [s1]);
  const own = holder(11, 1);
  assert.deepStrictEqual(tied.enqueue(own, 1), [own]);
  assert.deepStrictEqual(tied.enqueue(s2, 2), [s2], "the holder of a shared turn does not stop the second one");
  const elsewhere = holder(12, 99);
  tied.enqueue(elsewhere, 3);
  assert.strictEqual(usageOf(tied.running, "gpu").exclusiveBusy, true, "a holder of no running turn is exclusive");
}

/** A holder counts on the resource of the turn that started it: holders send no resource, which would read as "gpu". */
export function checkHolderResource(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, false), spec("x", 2, false)]);
  const onX = turn(1, "exclusive", { resource: "x" });
  assert.deepStrictEqual(s.enqueue(onX, 0), [onX]);
  const own = holder(2, 1);
  s.enqueue(own, 1);
  assert.deepStrictEqual(usageOf(s.running, "x").holders, [own], "the holder of a turn on x is x's");
  assert.deepStrictEqual(usageOf(s.running, "gpu").holders, [], "and not gpu's");
  const onGpu = turn(3, "shared");
  assert.deepStrictEqual(s.enqueue(onGpu, 2), [onGpu], "so a turn on gpu is granted beside it");
  s.remove(onX, 3);
  assert.deepStrictEqual(usageOf(s.running, "gpu").untied, [own], "once its turn ended it is an untied holder of gpu");
  const named = Object.assign(holder(4, null), { resource: "x" });
  s.enqueue(named, 4);
  assert.deepStrictEqual(usageOf(s.running, "x").untied, [named], "an untied holder that names a resource is its");
}

/** The turn is gone and its holder remains: the holder counts as exclusive. */
export function checkOrphanHolder(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, false)]);
  const t = turn(1, "shared");
  s.enqueue(t, 0);
  const own = holder(2, 1);
  s.enqueue(own, 1);
  assert.strictEqual(usageOf(s.running, "gpu").exclusiveBusy, false, "tied to the running turn");
  s.remove(t, 5);
  assert.strictEqual(usageOf(s.running, "gpu").exclusiveBusy, true, "the turn ended; the holder is exclusive");
  const next = turn(3, "shared");
  assert.deepStrictEqual(s.enqueue(next, 6), [], "nothing is granted beside it");
  assert.deepStrictEqual(s.remove(own, 7), [next]);
}

/** A probe result grants one turn, only when it is ok, fresh and of the current generation. */
export function checkProbeResults(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 3, true)]);
  const [a, b, c] = [turn(1, "shared"), turn(2, "shared"), turn(3, "shared")];
  s.enqueue(a, 0);
  s.add(b);
  s.add(c);
  const now = 20000;
  const generation = s.stateOf("gpu").generation;
  const bad: [string, ProbeResult][] = [
    ["not ok", fresh(s, "gpu", now, { ok: false, value: 100, why: "below min" })],
    ["6 seconds old", fresh(s, "gpu", now - ProbeFreshMs - 1000)],
    ["an earlier generation", fresh(s, "gpu", now, { generation: generation - 1 })],
  ];
  for (const [why, result] of bad) {
    s.recordProbe("gpu", result);
    assert.deepStrictEqual(s.grant(now), [], `a probe result that is ${why} grants nothing`);
  }
  s.recordProbe("gpu", fresh(s, "gpu", now - ProbeFreshMs));
  assert.deepStrictEqual(s.grant(now), [b], "a result exactly as old as the limit still counts, for one turn");
  assert.deepStrictEqual(s.grant(now), [], "the same result does not grant the next one");
  s.recordProbe("gpu", fresh(s, "gpu", now));
  assert.deepStrictEqual(s.grant(now), [c], "a new result grants it");
}

/** probeWanted names a resource only when its head waits for a probe and every running shared turn has settled. */
export function checkProbeWanted(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 3, true)]);
  const [a, b, c] = [turn(1, "shared"), turn(2, "shared"), turn(3, "shared")];
  assert.deepStrictEqual(s.probeWanted(0), [], "nothing waits");
  s.enqueue(a, 0);
  s.enqueue(b, 100);
  assert.deepStrictEqual(s.probeWanted(60000), [], "a turn whose command holds nothing yet has not settled");
  s.enqueue(holder(11, 1), 5000);
  assert.deepStrictEqual(s.probeWanted(19999), [], "counted from the holder's start, not from the turn's grant");
  assert.deepStrictEqual(s.probeWanted(20000), ["gpu"], "its command has used the GPU for settleMs");
  s.enqueue(holder(12, 1), 18000);
  assert.deepStrictEqual(s.probeWanted(20000), [], "a later holder of the same turn starts the count again");
  assert.deepStrictEqual(s.probeWanted(33000), ["gpu"]);
  s.recordProbe("gpu", fresh(s, "gpu", 33000));
  assert.deepStrictEqual(s.grant(33000), [b]);
  s.enqueue(holder(13, 2), 34000);
  s.add(c);
  assert.deepStrictEqual(s.probeWanted(40000), [], "the youngest running turn counts: b's command is 6 seconds old");
  assert.deepStrictEqual(s.probeWanted(49000), ["gpu"], "all have settled");
  const unsettled = scheduler([
    { ...spec("gpu", 2, true), probe: { argv: ["probe"], min: 1, timeoutMs: 1, settleMs: 0 } },
  ]);
  unsettled.enqueue(turn(1, "shared"), 0);
  unsettled.enqueue(turn(2, "shared"), 1);
  assert.deepStrictEqual(unsettled.probeWanted(1), ["gpu"], "settleMs 0 waits for no holder");
  const exclusive = scheduler([spec("gpu", 3, true)]);
  exclusive.enqueue(turn(1, "shared"), 0);
  exclusive.enqueue(turn(2, "exclusive"), 1);
  assert.deepStrictEqual(exclusive.probeWanted(60000), [], "an exclusive head needs no probe");
}

/** Nothing is granted while paused; the forecast lets two shared turns overlap where the real one needs a probe. */
export function checkPauseAndForecast(assert: AssertModule): void {
  const paused = scheduler([spec("gpu", 2, true)]);
  const first = turn(1, "shared");
  assert.deepStrictEqual(paused.enqueue(first, 0), [first]);
  paused.enqueue(holder(11, 1), 0);
  paused.paused = true;
  assert.deepStrictEqual(paused.enqueue(turn(2, "shared"), 1), [], "no turn while paused");
  paused.recordProbe("gpu", fresh(paused, "gpu", 60000));
  assert.deepStrictEqual(paused.grant(60000), [], "not even with a valid probe result");
  paused.recordProbe("gpu", fresh(paused, "gpu", 60000, { ok: false }));
  assert.deepStrictEqual(paused.probeWanted(60000), [], "and no probe is wanted while paused");
  paused.paused = false;
  assert.deepStrictEqual(paused.probeWanted(60000), ["gpu"], "it is wanted once the pause ends");
  const s = scheduler([spec("gpu", 2, true)]);
  const [a, b] = [turn(1, "shared", { estimate: times(60, 60) }), turn(2, "shared", { estimate: times(60, 60) })];
  s.add(a);
  s.add(b);
  const forecast = s.forecast(0);
  assert.strictEqual(forecast.get(a)?.startAt, 0);
  assert.strictEqual(forecast.get(b)?.startAt, 0, "the second shared turn is forecast beside the first");
  assert.strictEqual(forecast.get(b)?.endAt, 60000);
  assert.deepStrictEqual(s.grant(0), [a], "while the real one waits for a probe");
  const serial = scheduler([spec("gpu", 1, false)]);
  const [c, d] = [turn(1, "shared", { estimate: times(60, 60) }), turn(2, "shared", { estimate: times(60, 60) })];
  serial.add(c);
  serial.add(d);
  assert.strictEqual(serial.forecast(0).get(d)?.startAt, 60000, "slots 1: one after the other");
}

/** The rotation of each resource is its own: a grant on one does not advance another. */
export function checkRotationPerResource(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 1, false), spec("x", 1, false)], "hrrn");
  const on = (id: number, resource: string, worktree: string, queuedAt: number): SchedulerJob =>
    turn(id, undefined, { resource, worktree, queuedAt });
  const g = on(1, "gpu", "A", 0);
  assert.deepStrictEqual(s.enqueue(g, 0), [g]);
  s.remove(g, 10);
  const [xa, xb, xc, xd] = [on(2, "x", "A", 1), on(3, "x", "B", 2), on(4, "x", "A", 3), on(5, "x", "B", 4)];
  [xa, xb, xc, xd].forEach((j) => s.add(j));
  assert.deepStrictEqual(s.grant(20), [xa], "A was served on gpu, not on x: it still goes first on x");
  assert.deepStrictEqual(s.remove(xa, 30), [xb], "then the other worktree");
  assert.deepStrictEqual(s.remove(xb, 40), [xc]);
  assert.deepStrictEqual(s.remove(xc, 50), [xd]);
}

/** The forecast replays on copies: the rotation of each resource and the resource states stay as they were. */
export function checkForecastLeavesState(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, true)], "hrrn");
  const served = turn(1, undefined, { worktree: "A", estimate: times(60, 60) });
  assert.deepStrictEqual(s.enqueue(served, 0), [served]);
  s.remove(served, 10);
  const waiting = [turn(2, "shared", { worktree: "B" }), turn(3, "shared", { worktree: "A" })];
  waiting.forEach((j) => s.add(j));
  const snapshot = (): string =>
    JSON.stringify({
      turns: [...s.rotation.turns].map(([name, byWorktree]) => [name, [...byWorktree]]),
      slots: [...s.rotation.slots],
      grants: s.rotation.grants,
      gpu: s.stateOf("gpu"),
    });
  const before = snapshot();
  s.forecast(20);
  assert.strictEqual(snapshot(), before, "forecast changes neither the rotation nor the resource state");
}

/** A turn of a resource the configuration does not name gets one slot and no probe; no exception. */
export function checkUnknownResource(assert: AssertModule): void {
  const s = scheduler([spec("gpu", 2, false)]);
  const [t1, t2] = [turn(1, undefined, { resource: "nope" }), turn(2, undefined, { resource: "nope" })];
  assert.deepStrictEqual(s.enqueue(t1, 0), [t1]);
  assert.deepStrictEqual(s.enqueue(t2, 0), [], "one at a time");
  assert.deepStrictEqual(s.remove(t1, 5), [t2]);
  const [u1, u2] = [turn(3, "shared", { resource: "other" }), turn(4, "shared", { resource: "other" })];
  assert.deepStrictEqual(s.enqueue(u1, 6), [u1]);
  assert.deepStrictEqual(s.enqueue(u2, 6), [], "shared turns of an unknown resource go one at a time too");
  const gpu = turn(5, "shared");
  assert.deepStrictEqual(s.enqueue(gpu, 6), [gpu], "the resources do not hold each other");
}

/** The resource name of a job: its own when it has the form of a name, else "gpu". */
export function checkResourceName(assert: AssertModule): void {
  assert.strictEqual(resourceOf({}), "gpu");
  assert.strictEqual(resourceOf({ resource: "x-1" }), "x-1");
  assert.strictEqual(resourceOf({ resource: "Bad Name" }), "gpu");
  assert.strictEqual(resourceOf({ resource: "a".repeat(33) }), "gpu");
  assert.strictEqual(resourceOf({ resource: "a".repeat(32) }), "a".repeat(32));
  assert.strictEqual(resourceOf({ resource: 5 }), "gpu");
  assert.strictEqual(resourceOf({ resource: "" }), "gpu");
  const s = scheduler([spec("gpu", 1, false)]);
  const [t1, t2] = [turn(1, undefined), turn(2, undefined, { resource: "Bad Name" })];
  const numeric = turn(3, undefined, { resource: 5 as unknown as string });
  assert.deepStrictEqual(s.enqueue(t1, 0), [t1]);
  assert.deepStrictEqual(s.enqueue(t2, 0), [], "a malformed name is the gpu: it waits");
  assert.deepStrictEqual(s.enqueue(numeric, 0), [], "so does a number");
}

export const ResourceChecks: Record<string, (assert: AssertModule) => void> = {
  checkSharedNeedsProbe,
  checkSharedFull,
  checkHeadOnly,
  checkShareDefaultsToExclusive,
  checkHolderTies,
  checkHolderResource,
  checkOrphanHolder,
  checkProbeResults,
  checkProbeWanted,
  checkPauseAndForecast,
  checkRotationPerResource,
  checkForecastLeavesState,
  checkUnknownResource,
  checkResourceName,
};
