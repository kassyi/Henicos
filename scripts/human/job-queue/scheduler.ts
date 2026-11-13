// The scheduling decisions of the job queue, with no I/O: the order of the waiting jobs, the grants, backfilling and
// the forecast of each job's start and end. The server feeds it events; the self-test drives it directly.

import {
  ForecastSteps,
  OverrunGuessMs,
  LongestRunMs,
  UnknownTypicalMs,
  DefaultPolicy,
  type Policy,
} from "./constants.ts";

/** The run times of a command's earlier runs: the median and the longest. */
export interface Estimate {
  typicalMs: number;
  longestMs: number;
}

/** What the scheduler needs to know of a job. The server's job record extends it. */
export interface SchedulerJob {
  id: number;
  weight: number;
  gpu: boolean;
  queuedAt: number;
  /** When the job was granted (or, for an adopted job, when its command started); 0 while it waits. */
  grantedAt: number;
  estimate: Estimate | null;
  /** A manual place ahead of the policy order (move --first): lower goes first; null when not pinned. */
  pin: number | null;
  /** move --before: the id of the waiting job this one goes right before; ignored while that job is not waiting. */
  before: number | null;
  /** run --after: the id of the job that must end first; the job is not granted while that job runs or waits. */
  after: number | null;
  /**
   * run --gpu: the job waits for its turn at the GPU, not for CPU slots (it weighs nothing; the command it then starts
   * under the GPU lock registers its own slots as a GPU holder). One turn is given at a time, while no GPU holder runs.
   */
  gpuTurn?: boolean;
  /** run --keep-if-failed: the job runs after its predecessor whatever its outcome. */
  keepIfFailed?: boolean;
  /** The memory budget a test run takes from the memory pool, in MB; 0 or absent for a job that runs no tests. */
  memoryMb?: number;
  /**
   * start <id>: granted at once, over the slots and the memory pool, like a GPU holder, for a job a person wants to run
   * beside the running ones. It still waits for its predecessor (--after); a GPU turn never takes it.
   */
  startNow?: boolean;
  backfilled?: boolean;
  /** Its run started before this scheduler (re-registered after a restart): no run time is recorded for it. */
  adopted?: boolean;
  /**
   * Its client is away (the connection dropped, or the job was restored after a restart): a waiting job keeps its place
   * but is not granted, since nobody would receive the grant.
   */
  detached?: boolean;
}

/** When a job is expected to start and end. */
export interface Expected {
  startAt: number;
  endAt: number;
}

/**
 * The scheduler state. A job that holds the GPU lock is granted at once. The other waiting jobs are put in order (the
 * manual places first, then the policy), the jobs whose predecessor (--after) has not ended are set aside, and the
 * first
 * of the rest is granted while it fits:
 *   - fifo: arrival order; a later job never overtakes the first one.
 *   - backfill: arrival order, and a later job may start in the free slots when it does not delay the first one
 *     (EASY backfilling): it ends before the first job could start, or it fits in the slots the first job leaves free.
 *   - hrrn: the same backfilling, with the jobs ordered by response ratio (waited time + typical run time) / typical
 *     run time, highest first: a short job goes first, and a long one rises the longer it waits, so that none starves.
 * Backfilling reserves the start of the first job as if every running job took its longest recorded time, and
 * backfills nothing while a running job has no recorded time or has run longer than all of them. Nothing is granted
 * before `holdUntil` (re-registration after a restart).
 *
 * Test runs also take their memory budget from a memory pool: a test run is granted only while the budgets of the
 * running jobs and its own fit in `memoryCapacity`, or when no running job holds memory (a run larger than the pool
 * then runs alone). GPU holders are granted at once, over the pool as over the slots.
 */
export class Scheduler<J extends SchedulerJob> {
  capacity: number;
  holdUntil: number;
  policy: Policy;
  memoryCapacity: number;
  used = 0;
  memoryUsed = 0;
  running: J[] = [];
  waiting: J[] = [];

  constructor(capacity: number, holdUntil = 0, policy: Policy = DefaultPolicy, memoryCapacity = Infinity) {
    this.capacity = capacity;
    this.holdUntil = holdUntil;
    this.policy = policy;
    this.memoryCapacity = memoryCapacity;
  }

  /** The memory a job takes from the pool: none for a GPU turn (its command registers its own). */
  memoryOf(job: SchedulerJob): number {
    return job.gpuTurn ? 0 : Math.max(0, job.memoryMb ?? 0);
  }

  /** True when the job's memory fits beside `held` MB of running jobs (always when none holds any). */
  private fitsMemory(job: SchedulerJob, held = this.memoryUsed): boolean {
    const memory = this.memoryOf(job);
    return memory === 0 || held === 0 || held + memory <= this.memoryCapacity;
  }

  private start(job: J): void {
    this.running.push(job);
    this.used += job.weight;
    this.memoryUsed += this.memoryOf(job);
  }

  clampWeight(weight: number): number {
    return Math.min(Math.max(1, Math.trunc(weight) || 1), this.capacity);
  }

  /** The slots a job takes: none for a GPU turn, else its weight clamped to 1..capacity. */
  slotsOf(job: SchedulerJob): number {
    return job.gpuTurn ? 0 : this.clampWeight(job.weight);
  }

  /** Adds a waiting job and returns the jobs granted by it. */
  enqueue(job: J, now = Date.now()): J[] {
    this.add(job);
    return this.grant(now);
  }

  /** Adds a waiting job and grants nothing: the caller grants once the event is settled. */
  add(job: J): void {
    job.weight = this.slotsOf(job);
    this.waiting.push(job);
  }

  /** Registers a job that already runs (its client reconnected after the scheduler restarted). */
  adopt(job: J, now = Date.now()): void {
    job.weight = this.slotsOf(job);
    // The start the client reported, unless it is missing, in the future or older than any run can be
    job.grantedAt = job.grantedAt > now - LongestRunMs && job.grantedAt <= now ? job.grantedAt : now;
    job.adopted = true;
    this.start(job);
  }

  /** Removes a job, waiting or running, and returns the jobs granted by the freed slots. */
  remove(job: J, now = Date.now()): J[] {
    this.detach(job);
    return this.grant(now);
  }

  /** Removes a job, waiting or running, and grants nothing: the caller grants once the event is settled. */
  detach(job: J): void {
    const running = this.running.indexOf(job);
    if (running >= 0) {
      this.running.splice(running, 1);
      this.used -= job.weight;
      this.memoryUsed -= this.memoryOf(job);
    }
    const waiting = this.waiting.indexOf(job);
    if (waiting >= 0) {
      this.waiting.splice(waiting, 1);
    }
  }

  find(id: number): J | undefined {
    return this.running.find((job) => job.id === id) ?? this.waiting.find((job) => job.id === id);
  }

  /**
   * How the jobs that ended went, by id ("succeeded", "failed", "cancelled", "lost"): the server shares its record.
   * A job --after another is granted on this evidence only, never on the other one's absence.
   */
  outcomes: ReadonlyMap<number, string> = new Map();

  /**
   * True while the job may not be granted: its client is away (detached), or its predecessor blocks it
   * (awaitsPredecessor). Like any blocked job, it does not hold up the jobs behind it.
   */
  isBlocked(job: SchedulerJob): boolean {
    return job.detached === true || this.awaitsPredecessor(job);
  }

  /**
   * True until the job's predecessor (--after) has a recorded outcome that lets it run: "succeeded", or any outcome
   * with --keep-if-failed. A predecessor that runs, waits, is not back yet after a restart, or failed (without
   * --keep-if-failed) blocks it; the server cancels the jobs whose predecessor failed.
   */
  awaitsPredecessor(job: SchedulerJob): boolean {
    if (job.after === null) {
      return false;
    }
    const outcome = this.outcomes.get(job.after);
    return outcome === undefined || (outcome !== "succeeded" && job.keepIfFailed !== true);
  }

  /** move --first: ahead of every waiting job, the pinned ones included. */
  pinFirst(job: J): void {
    const pins = this.waiting
      .filter((other) => other !== job && other.pin !== null)
      .map((other) => other.pin as number);
    job.pin = pins.length === 0 ? 0 : Math.min(...pins) - 1;
    job.before = null;
  }

  /**
   * move --before: right before `target`, wherever the order puts it. Returns why it cannot, or null: both jobs must
   * wait, and the target must not already go before the job (directly or through others).
   */
  placeBefore(job: J, target: J): string | null {
    if (!this.waiting.includes(job) || !this.waiting.includes(target)) {
      return "both jobs must be waiting";
    }
    if (job === target) {
      return "a job cannot go before itself";
    }
    // The walk stops at a cycle among other jobs, which a restart can bring back (order() sets such jobs aside)
    const seen = new Set<J>();
    for (let next: J | undefined = target; next !== undefined && next.before !== null && !seen.has(next); ) {
      if (next.before === job.id) {
        return `#${target.id} already goes before #${job.id}`;
      }
      seen.add(next);
      next = this.waiting.find((other) => other.id === (next as J).before);
    }
    job.before = target.id;
    job.pin = null;
    return null;
  }

  /** move --reset: back to the policy order. */
  resetPlace(job: J): void {
    job.pin = null;
    job.before = null;
  }

  /**
   * Sorts the waiting jobs (also the positions the clients are told): the policy order, then the pinned jobs ahead of
   * it by their pin, then each job placed --before another right in front of that one.
   */
  order(now = Date.now()): void {
    const arrival = (a: J, b: J): number => a.queuedAt - b.queuedAt || a.id - b.id;
    if (this.policy === "hrrn") {
      const ratio = (job: J): number => {
        const typical = job.estimate ? Math.max(job.estimate.typicalMs, 1000) : UnknownTypicalMs;
        return (Math.max(0, now - job.queuedAt) + typical) / typical;
      };
      const ratios = new Map(this.waiting.map((job) => [job, ratio(job)]));
      this.waiting.sort((a, b) => (ratios.get(b) as number) - (ratios.get(a) as number) || arrival(a, b));
    } else {
      this.waiting.sort(arrival);
    }
    const ids = new Map(this.waiting.map((job) => [job.id, job]));
    const target = (job: J): J | undefined => (job.before !== null ? ids.get(job.before) : undefined);
    // The jobs on a cycle of --before (placeBefore refuses one, but a restart can bring it back) keep the policy order
    const cyclic = new Set<J>();
    for (const job of this.waiting) {
      const path: J[] = [];
      for (let next: J | undefined = job; next !== undefined && !cyclic.has(next); next = target(next)) {
        const at = path.indexOf(next);
        if (at >= 0) {
          path.slice(at).forEach((member) => cyclic.add(member));
          break;
        }
        path.push(next);
      }
    }
    const placed = (job: J): J | undefined => (cyclic.has(job) ? undefined : target(job));
    const pinned = this.waiting.filter((job) => job.pin !== null && placed(job) === undefined);
    pinned.sort((a, b) => (a.pin as number) - (b.pin as number) || arrival(a, b));
    const ordered = [...pinned, ...this.waiting.filter((job) => job.pin === null && placed(job) === undefined)];
    // A job placed before another follows that one wherever it is; chains (a before b before c) keep their order
    const pending = this.waiting.filter((job) => placed(job) !== undefined);
    const insert = (job: J): void => {
      if (ordered.includes(job)) {
        return;
      }
      const before = placed(job) as J;
      insert(before); // ends: the chain has no cycle, and its last job is already ordered
      ordered.splice(ordered.indexOf(before), 0, job);
    };
    pending.forEach(insert);
    this.waiting.splice(0, this.waiting.length, ...ordered);
  }

  /** True while a job holds the GPU: a GPU holder runs, or a GPU turn was given and has not ended. */
  gpuBusy(): boolean {
    return this.running.some((job) => job.gpu || job.gpuTurn === true);
  }

  /**
   * Grants GPU holders at once, then the GPU turn to the first waiting turn in order while the GPU is free, then the
   * first grantable waiting job while it fits, then backfills (unless fifo).
   */
  grant(now = Date.now()): J[] {
    const granted: J[] = [];
    if (now < this.holdUntil) {
      this.order(now);
      return granted;
    }
    const take = (job: J): void => {
      this.waiting.splice(this.waiting.indexOf(job), 1);
      job.grantedAt = now;
      this.start(job);
      granted.push(job);
    };
    // GPU holders and the jobs started by hand, over the pools
    this.waiting
      .filter((job) => (job.gpu || job.startNow === true) && !job.gpuTurn && !this.isBlocked(job))
      .forEach(take);
    this.order(now);
    // The GPU goes to the waiting turns one at a time, in the same order as the slots, so that none is passed for ever
    const turn = this.gpuBusy() ? undefined : this.waiting.find((job) => job.gpuTurn === true && !this.isBlocked(job));
    if (turn !== undefined) {
      take(turn);
    }
    for (;;) {
      const first = this.waiting.find((job) => !job.gpuTurn && !this.isBlocked(job));
      if (first === undefined || this.used + first.weight > this.capacity || !this.fitsMemory(first)) {
        break;
      }
      take(first);
    }
    if (this.policy !== "fifo") {
      this.backfill(now).forEach((job) => {
        job.backfilled = true;
        take(job);
      });
    }
    return granted;
  }

  /** The grantable waiting jobs after the first that may start now without delaying the first one's reserved start. */
  backfill(now: number): J[] {
    const grantable = this.waiting.filter((job) => !job.gpuTurn && !this.isBlocked(job));
    if (grantable.length < 2) {
      return [];
    }
    const first = grantable[0] as J;
    // When the running jobs end, at the latest; unknown when one has no recorded time or ran past all of them
    const ends: { end: number; weight: number }[] = [];
    for (const job of this.running.filter((other) => other.weight > 0)) {
      // A GPU turn holds no slots, so its end frees none
      const end = job.estimate ? job.grantedAt + job.estimate.longestMs : NaN;
      if (!(end > now)) {
        return [];
      }
      ends.push({ end, weight: job.weight });
    }
    ends.sort((a, b) => a.end - b.end);
    // The reserved start: the first time enough slots are free for the first job, and what it leaves free then
    let free = this.capacity - this.used;
    let reserved = now;
    for (const { end, weight } of ends) {
      if (free >= first.weight) {
        break;
      }
      free += weight;
      reserved = end;
    }
    // Once every running job has ended, all slots are free: the first job (weight clamped to the capacity) fits
    let spare = free - first.weight;
    let idle = this.capacity - this.used;
    // Memory: a job that does not end in time also leaves the first job's budget free in the pool
    let held = this.memoryUsed;
    const firstMemory = this.memoryOf(first);
    const chosen: J[] = [];
    for (const job of grantable.slice(1)) {
      const memory = this.memoryOf(job);
      if (job.gpu || job.weight > idle || !this.fitsMemory(job, held)) {
        continue;
      }
      const endsInTime = job.estimate !== null && now + job.estimate.longestMs <= reserved;
      const leavesMemory = memory === 0 || firstMemory === 0 || held + memory + firstMemory <= this.memoryCapacity;
      if (endsInTime || (job.weight <= spare && leavesMemory)) {
        if (!endsInTime) {
          spare -= job.weight;
        }
        idle -= job.weight;
        held += memory;
        chosen.push(job);
      }
    }
    return chosen;
  }

  /**
   * When each job is expected to start and end: the grants of this scheduler played forward on copies of the jobs,
   * each taking its typical run time (UnknownTypicalMs without a record). A running job past its typical time is
   * expected to end at its longest recorded time, and past that, OverrunGuessMs from now. A job that waits for another
   * (--after) starts no earlier than that one's expected end.
   */
  forecast(now = Date.now()): Map<J, Expected> {
    const duration = (job: SchedulerJob): number => (job.estimate ? job.estimate.typicalMs : UnknownTypicalMs);
    const simulation = new Scheduler<SchedulerJob>(this.capacity, 0, this.policy, this.memoryCapacity);
    // The replay counts every job it ends as succeeded, so that the jobs after it are forecast to start then
    const replayed = new Map(this.outcomes);
    simulation.outcomes = replayed;
    const originals = new Map<SchedulerJob, J>();
    const ends = new Map<SchedulerJob, number>();
    const result = new Map<J, Expected>();
    for (const job of this.running) {
      const copy: SchedulerJob = { ...job };
      let endAt = job.grantedAt + duration(job);
      if (endAt <= now && job.estimate) {
        endAt = job.grantedAt + job.estimate.longestMs;
      }
      if (endAt <= now) {
        endAt = now + OverrunGuessMs;
      }
      simulation.start(copy);
      ends.set(copy, endAt);
      result.set(job, { startAt: job.grantedAt, endAt });
    }
    for (const job of this.waiting) {
      const copy: SchedulerJob = { ...job };
      originals.set(copy, job);
      simulation.waiting.push(copy);
    }
    let time = now;
    for (let step = 0; step < ForecastSteps && simulation.waiting.length > 0; step++) {
      for (const copy of simulation.grant(time)) {
        const original = originals.get(copy) as J;
        ends.set(copy, time + duration(copy));
        result.set(original, { startAt: time, endAt: time + duration(copy) });
      }
      if (simulation.waiting.length === 0 || simulation.running.length === 0) {
        break;
      }
      // The next end, or with free slots under hrrn the next re-grant (RegrantMs; a minute here, a coarser step that
      // keeps the replay short), when a waiting job may have overtaken the first one
      const regrant = this.policy === "hrrn" && simulation.used < this.capacity ? time + 60 * 1000 : Infinity;
      time = Math.max(time, Math.min(regrant, ...simulation.running.map((copy) => ends.get(copy) as number)));
      for (const copy of simulation.running.filter((other) => (ends.get(other) as number) <= time)) {
        simulation.detach(copy);
        replayed.set(copy.id, "succeeded");
      }
    }
    return result;
  }

  isIdle(): boolean {
    return this.running.length === 0 && this.waiting.length === 0;
  }
}
