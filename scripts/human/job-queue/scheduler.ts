// The scheduling decisions of the job queue, with no I/O: the order of the waiting jobs, the grants, backfilling and
// the forecast of each job's start and end. The server feeds it events; the self-test drives it directly.

import {
  ForecastSteps,
  OverrunGuessMs,
  OverdueMs,
  LongestRunMs,
  UnknownTypicalMs,
  DefaultPolicy,
  type Policy,
} from "./constants.ts";
import {
  resourceOf,
  sharedTurnsSettled,
  turnVerdict,
  usageOf,
  type ProbeResult,
  type ResourceSpec,
  type ResourceState,
  type ShareMode,
} from "./resources.ts";
import { formatJobId } from "./job-id.ts";

/** The run times of a command's earlier runs: the median and the longest. */
export interface Estimate {
  typicalMs: number;
  longestMs: number;
  /** Where it came from (estimate-fallback.ts); absent in hand-made estimates. */
  from?: string;
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
   * under the GPU lock registers its own slots as a GPU holder). How many turns run at once is the rule of turnVerdict.
   */
  gpuTurn?: boolean;
  /** The turn resource of a GPU turn or holder (resourceOf in resources.ts); absent means "gpu". */
  resource?: string;
  /** How a turn shares its resource; anything but "shared" is exclusive. */
  share?: ShareMode;
  /**
   * A GPU holder: the id of the turn job that started it (ServerJob.turn). A holder tied to no running turn is
   * exclusive.
   */
  turn?: number | null;
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
   * The worktree the job was queued from (the root of its directory): the key of the round-robin. Jobs without one
   * share the key "".
   */
  worktree?: string;
  /**
   * Its client is away (the connection dropped, or the job was restored after a restart): a waiting job keeps its place
   * but is not granted, since nobody would receive the grant.
   */
  detached?: boolean;
}

/**
 * The round-robin's memory: per kind of grant (CPU slots, and the turns of each resource), the grant count at which
 * each worktree was last served. Kept in memory only; a restarted scheduler starts the rotation afresh.
 */
export interface Rotation {
  grants: number;
  slots: Map<string, number>;
  /** By resource name, then by worktree. */
  turns: Map<string, Map<string, number>>;
}

/** When a job is expected to start and end. */
export interface Expected {
  startAt: number;
  endAt: number;
  /** The end if it takes its longest recorded run time; absent without a recorded time. */
  latestEndAt?: number;
}

/**
 * What a waiting job waits for, as the grant step sees it: its predecessor (after), its client (away), the startup
 * grace after a restart (restart), the pause (paused), a GPU turn (gpu) or the probe of the GPU's free memory
 * (gpu-memory), the CPU slots (slots), the memory pool of the test runs (memory), or nothing but the jobs that go
 * first (in-line).
 */
export type WaitFor = "after" | "away" | "restart" | "paused" | "gpu" | "gpu-memory" | "slots" | "memory" | "in-line";

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
 *     Two fairness rules then reorder that list (fairOrder): a job that has waited longer than max(OverdueMs, its
 *     typical run time) goes ahead of every job that has not, oldest first; the others are taken worktree by worktree
 *     in turn (one job per worktree per round, the worktree served longest ago first), separately for the CPU slots
 *     and for the GPU turns.
 * The manual places (move --first, --before) go ahead of all of that, under every policy. Backfilling reserves the
 * start of the first job of that order as if every running job took its longest recorded time, and backfills nothing
 * while a running job has no recorded time or has run longer than all of them. Nothing is granted before `holdUntil`
 * (re-registration after a restart).
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
  /**
   * pause: nothing new is granted, neither slots nor GPU turns (grant). The jobs that run keep their slots, and the
   * jobs granted at once (GPU holders, start <id>) still are. pausedAt is when it was set (0 while not paused).
   */
  paused = false;
  pausedAt = 0;
  running: J[] = [];
  waiting: J[] = [];
  rotation: Rotation = { grants: 0, slots: new Map(), turns: new Map() };
  /** The turn resources by name: the configured ones, and those a turn named that the configuration does not. */
  private readonly resources = new Map<string, ResourceState>();

  /** `resources`: the turn resources (default: "gpu", one slot, no probe). */
  constructor(
    capacity: number,
    holdUntil = 0,
    policy: Policy = DefaultPolicy,
    memoryCapacity = Infinity,
    resources: ResourceSpec[] = [{ name: "gpu", slots: 1, probe: null }],
  ) {
    this.capacity = capacity;
    this.holdUntil = holdUntil;
    this.policy = policy;
    this.memoryCapacity = memoryCapacity;
    for (const spec of resources) {
      this.resources.set(spec.name, { spec, generation: 0, probe: null });
    }
  }

  /** The state of a turn resource; one the configuration does not name gets one slot and no probe (serialized). */
  stateOf(name: string): ResourceState {
    let state = this.resources.get(name);
    if (state === undefined) {
      state = { spec: { name, slots: 1, probe: null }, generation: 0, probe: null };
      this.resources.set(name, state);
    }
    return state;
  }

  /** Every turn resource, the configured ones first. */
  resourceStates(): ResourceState[] {
    return [...this.resources.values()];
  }

  /** Keeps the result of a probe of the resource; the grant rule reads it (turnVerdict). */
  recordProbe(name: string, result: ProbeResult): void {
    this.stateOf(name).probe = result;
  }

  /** The grantable first waiting turn of each resource that has one, in the order of the waiting jobs. */
  private headTurns(): Map<string, J> {
    const heads = new Map<string, J>();
    for (const job of this.waiting) {
      const name = resourceOf(job);
      if (job.gpuTurn === true && !this.isBlocked(job) && !heads.has(name)) {
        heads.set(name, job);
      }
    }
    return heads;
  }

  /**
   * The resources whose first waiting turn waits for a probe (turnVerdict "probe") while every shared turn that runs
   * has used the resource for settleMs (sharedTurnsSettled): the server measures them. Nothing while paused or before holdUntil, when nothing is granted.
   */
  probeWanted(now: number): string[] {
    if (this.paused || now < this.holdUntil) {
      return [];
    }
    const wanted: string[] = [];
    for (const [name, head] of this.headTurns()) {
      const state = this.stateOf(name);
      const usage = usageOf(this.running, name);
      const settleMs = state.spec.probe?.settleMs ?? 0;
      if (turnVerdict(head, usage, state, now) === "probe" && sharedTurnsSettled(usage, settleMs, now)) {
        wanted.push(name);
      }
    }
    return wanted;
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

  /**
   * What a waiting job waits for (WaitFor), by the same rules as grant: a turn waits for its resource, whose first
   * grantable turn waits for a probe ("gpu-memory") or for the turns that run ("gpu"); a job that takes slots waits for
   * slots, then for the memory pool, else for the jobs ordered before it. Undefined for a job that does not wait.
   */
  waitFor(job: J, now = Date.now()): WaitFor | undefined {
    if (!this.waiting.includes(job)) {
      return undefined;
    }
    if (this.awaitsPredecessor(job)) {
      return "after";
    }
    if (job.detached === true) {
      return "away";
    }
    if (now < this.holdUntil) {
      return "restart";
    }
    if (this.paused) {
      return "paused";
    }
    if (job.gpuTurn === true) {
      const name = resourceOf(job);
      const head = this.headTurns().get(name);
      const verdict = head === job ? turnVerdict(job, usageOf(this.running, name), this.stateOf(name), now) : "behind";
      return verdict === "probe" ? "gpu-memory" : "gpu";
    }
    if (this.used + job.weight > this.capacity) {
      return "slots";
    }
    return this.fitsMemory(job) ? "in-line" : "memory";
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
        return `${formatJobId(target.id)} already goes before ${formatJobId(job.id)}`;
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

  /** hrrn: true when the job has waited longer than max(OverdueMs, its typical run time). */
  isOverdue(job: SchedulerJob, now: number): boolean {
    const typical = job.estimate ? job.estimate.typicalMs : UnknownTypicalMs;
    return now - job.queuedAt > Math.max(OverdueMs, typical);
  }

  /**
   * hrrn's fairness on the ratio order: the overdue jobs first, oldest first; then each kind of job (CPU slots, GPU
   * turns) round-robin across worktrees, in the places that kind had. Within a worktree the ratio order holds; the
   * worktree served longest ago (or never) goes first in each round, ties by the place of its first job. The jobs
   * placed by hand keep their places here (order() moves them afterwards).
   */
  private fairOrder(now: number, manual: (job: J) => boolean): J[] {
    const arrival = (a: J, b: J): number => a.queuedAt - b.queuedAt || a.id - b.id;
    const overdue = this.waiting.filter((job) => !manual(job) && this.isOverdue(job, now)).sort(arrival);
    const rest = this.waiting.filter((job) => !overdue.includes(job));
    // "manual", "slots", or "turn:<resource>": the turns of each resource rotate on their own
    const kindOf = (job: J): string =>
      manual(job) ? "manual" : job.gpuTurn === true ? `turn:${resourceOf(job)}` : "slots";
    const kinds = new Map<string, J[]>();
    for (const job of rest) {
      kinds.set(kindOf(job), [...(kinds.get(kindOf(job)) ?? []), job]);
    }
    const lists = new Map<string, J[]>();
    for (const [kind, jobs] of kinds) {
      const served = kind === "slots" ? this.rotation.slots : this.rotation.turns.get(kind.slice("turn:".length));
      lists.set(kind, kind === "manual" ? jobs : this.rotate(jobs, served ?? new Map()));
    }
    return [...overdue, ...rest.map((job) => (lists.get(kindOf(job)) as J[]).shift() as J)];
  }

  /** One job per worktree per round, the worktrees ordered by when they were last served (never: first). */
  private rotate(jobs: J[], served: ReadonlyMap<string, number>): J[] {
    const groups = new Map<string, J[]>();
    for (const job of jobs) {
      const key = job.worktree ?? "";
      groups.set(key, [...(groups.get(key) ?? []), job]);
    }
    // A stable sort: the worktrees served at the same count keep the order of their first jobs
    const keys = [...groups.keys()].sort((a, b) => (served.get(a) ?? 0) - (served.get(b) ?? 0));
    const result: J[] = [];
    for (let round = 0; result.length < jobs.length; round++) {
      for (const key of keys) {
        const job = (groups.get(key) as J[])[round];
        if (job !== undefined) {
          result.push(job);
        }
      }
    }
    return result;
  }

  /** Records a grant from the order (not a GPU holder or a start by hand) for the round-robin. */
  private serve(job: J): void {
    let served = this.rotation.slots;
    if (job.gpuTurn === true) {
      const name = resourceOf(job);
      served = this.rotation.turns.get(name) ?? new Map();
      this.rotation.turns.set(name, served);
    }
    served.set(job.worktree ?? "", ++this.rotation.grants);
  }

  /**
   * Sorts the waiting jobs (also the positions the clients are told): the policy order (under hrrn with its fairness,
   * fairOrder), then the pinned jobs ahead of it by their pin, then each job placed --before another right in front of
   * that one.
   */
  order(now = Date.now()): void {
    const arrival = (a: J, b: J): number => a.queuedAt - b.queuedAt || a.id - b.id;
    const ids = new Map(this.waiting.map((job) => [job.id, job]));
    if (this.policy === "hrrn") {
      const ratio = (job: J): number => {
        const typical = job.estimate ? Math.max(job.estimate.typicalMs, 1000) : UnknownTypicalMs;
        return (Math.max(0, now - job.queuedAt) + typical) / typical;
      };
      const ratios = new Map(this.waiting.map((job) => [job, ratio(job)]));
      this.waiting.sort((a, b) => (ratios.get(b) as number) - (ratios.get(a) as number) || arrival(a, b));
      const manual = (job: J): boolean => job.pin !== null || (job.before !== null && ids.has(job.before));
      this.waiting.splice(0, this.waiting.length, ...this.fairOrder(now, manual));
    } else {
      this.waiting.sort(arrival);
    }
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

  /**
   * Grants GPU holders and the jobs started by hand at once (also while paused: nothing else is), then the turns (per
   * resource, the first waiting turn in order while turnVerdict says so: a turn behind it is never granted first), then
   * the first grantable waiting job while it fits, then backfills (unless fifo).
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
    if (this.paused) {
      return granted; // the places are told (order), but no waiting job starts
    }
    // Each resource goes to its first waiting turn, in the same order as the slots, so that none is passed for ever;
    // a turn that cannot be granted holds back the ones behind it
    for (const name of this.headTurns().keys()) {
      const state = this.stateOf(name);
      for (;;) {
        const head = this.headTurns().get(name);
        if (head === undefined || turnVerdict(head, usageOf(this.running, name), state, now) !== "grant") {
          break;
        }
        take(head);
        this.serve(head);
        state.generation++;
      }
    }
    for (;;) {
      const first = this.waiting.find((job) => !job.gpuTurn && !this.isBlocked(job));
      if (first === undefined || this.used + first.weight > this.capacity || !this.fitsMemory(first)) {
        break;
      }
      take(first);
      this.serve(first);
    }
    if (this.policy !== "fifo") {
      this.backfill(now).forEach((job) => {
        job.backfilled = true;
        take(job);
        this.serve(job);
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
   * (--after) starts no earlier than that one's expected end. latestEndAt is the end at the longest recorded run time,
   * from the same expected start.
   */
  forecast(now = Date.now()): Map<J, Expected> {
    const duration = (job: SchedulerJob): number => (job.estimate ? job.estimate.typicalMs : UnknownTypicalMs);
    // The end at its longest recorded run time, from the same start; none without a record
    const latest = (startAt: number, job: SchedulerJob): { latestEndAt?: number } =>
      job.estimate ? { latestEndAt: startAt + job.estimate.longestMs } : {};
    const specs = [...this.resources.values()].map((state) => state.spec);
    const simulation = new Scheduler<SchedulerJob>(this.capacity, 0, this.policy, this.memoryCapacity, specs);
    // The replay assumes every probe passes: an estimate, optimistic
    simulation.resources.forEach((state) => (state.assumeProbeOk = true));
    // The replay continues the round-robin where this scheduler is, on its own copy
    const { grants, slots, turns } = this.rotation;
    const turnsCopy = new Map([...turns].map(([name, served]) => [name, new Map(served)] as const));
    simulation.rotation = { grants, slots: new Map(slots), turns: turnsCopy };
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
      result.set(job, { startAt: job.grantedAt, endAt, ...latest(job.grantedAt, job) });
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
        result.set(original, { startAt: time, endAt: time + duration(copy), ...latest(time, copy) });
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
