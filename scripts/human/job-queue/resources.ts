// The turn resources of the scheduler, with no I/O: a name, how many shared turns it allows at a time, and the
// decisions about who may take a turn (the counts are computed from the running jobs each time, never stored).

import type { QueueConfig } from "./config.ts";
import { ProbeFreshMs, ProbeTimeoutMs } from "./constants.ts";
import type { ResourceStatus } from "./protocol.ts";

/** How a turn shares its resource: only the exact string "shared" shares; anything else is exclusive. */
export type ShareMode = "shared" | "exclusive";

/** One turn resource, as the scheduler reads it from the configuration at start. */
export interface ResourceSpec {
  name: string;
  /** The most shared turns that run at a time. */
  slots: number;
  /** What to measure before a second shared turn starts; null for no measurement. */
  probe: { argv: string[]; min: number; timeoutMs: number; settleMs: number } | null;
}

/** One measurement. */
export interface ProbeResult {
  ok: boolean;
  /** The number read; null when none could be read. */
  value: number | null;
  at: number;
  /** The ResourceState.generation when the measurement began; a result of another generation is not used. */
  generation: number;
  /** Why it is not ok (for the log and the view). */
  why?: string;
}

export interface ResourceState {
  spec: ResourceSpec;
  /** Grows with every turn granted on the resource: a probe result serves one grant only. */
  generation: number;
  probe: ProbeResult | null;
  /** The forecast replay: every probe result counts as valid. */
  assumeProbeOk?: boolean;
}

/** What a turn decision needs of a job (the scheduler's job record has these fields). */
export interface ResourceJob {
  id: number;
  gpu: boolean;
  gpuTurn?: boolean;
  /** The name of the resource; absent or malformed means "gpu". */
  resource?: unknown;
  share?: ShareMode;
  /** A holder: the id of the turn job that started it. */
  turn?: number | null;
  grantedAt: number;
}

/** Why a turn may or may not be granted now. */
export type TurnVerdict =
  | "grant"
  | "exclusive-busy" // an exclusive user runs
  | "shared-busy" // the turn is exclusive and shared turns run
  | "full" // slots shared turns run
  | "probe"; // a second or later shared turn without a valid probe result

/** The use of one resource, computed from the running jobs. */
export interface ResourceUsage<J extends ResourceJob> {
  turns: J[];
  holders: J[];
  /** The holders that no running turn of the resource started. */
  untied: J[];
  sharedTurns: J[];
  exclusiveBusy: boolean;
}

const NamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const DefaultResource = "gpu";

/** Whether a received value has the form of a resource name. */
export function isResourceName(value: unknown): value is string {
  return typeof value === "string" && NamePattern.test(value);
}

/** The resource a GPU job uses: its own name when it has the form of one, else "gpu". */
export function resourceOf(job: Pick<ResourceJob, "resource">): string {
  return isResourceName(job.resource) ? job.resource : DefaultResource;
}

/**
 * The use of resource `name`. A turn without share "shared" is exclusive. So is a holder that no running turn started
 * (turn null or absent, or a turn that has ended): it is the GPU lock taken by hand, by an older client, or what
 * remains of a turn whose client died. A holder belongs to the resource of the running turn that started it (holders
 * send no resource of their own); an untied one to its own resource, "gpu" when it names none.
 */
export function usageOf<J extends ResourceJob>(running: readonly J[], name: string): ResourceUsage<J> {
  const allTurns = running.filter((job) => job.gpuTurn === true);
  const turnOf = (holder: J): J | undefined => allTurns.find((turn) => turn.id === holder.turn);
  const turns = allTurns.filter((job) => resourceOf(job) === name);
  const holders = running.filter((job) => job.gpu && job.gpuTurn !== true && resourceOf(turnOf(job) ?? job) === name);
  const untied = holders.filter((holder) => turnOf(holder) === undefined);
  const sharedTurns = turns.filter((job) => job.share === "shared");
  const exclusiveBusy = turns.some((job) => job.share !== "shared") || untied.length > 0;
  return { turns, holders, untied, sharedTurns, exclusiveBusy };
}

/**
 * Whether every shared turn of `usage` has used the resource for settleMs: counted from the latest start of the
 * holders it started (its command under the lock), not from its grant, since the command may start well after the
 * grant. A turn whose command holds nothing yet has not settled. With settleMs 0 every turn has settled.
 */
export function sharedTurnsSettled<J extends ResourceJob>(
  usage: ResourceUsage<J>,
  settleMs: number,
  now: number,
): boolean {
  if (settleMs <= 0) {
    return true;
  }
  return usage.sharedTurns.every((turn) => {
    const tied = usage.holders.filter((holder) => holder.turn === turn.id);
    return tied.length > 0 && tied.every((holder) => now - holder.grantedAt >= settleMs);
  });
}

/**
 * What status tells of a resource: its slots, the shared turns that run, the jobs that use it exclusively (exclusive
 * turns and holders no running turn started; a holder of a running turn is counted by its turn), and the last probe.
 */
export function describeResource<J extends ResourceJob>(state: ResourceState, running: readonly J[]): ResourceStatus {
  const usage = usageOf(running, state.spec.name);
  const probe = state.probe;
  return {
    name: state.spec.name,
    slots: state.spec.slots,
    shared: usage.sharedTurns.map((job) => job.id),
    exclusive: usage.turns
      .filter((job) => job.share !== "shared")
      .concat(usage.untied)
      .map((job) => job.id),
    ...(probe !== null
      ? {
          probe: {
            ok: probe.ok,
            value: probe.value,
            at: probe.at,
            ...(probe.why !== undefined ? { why: probe.why } : {}),
          },
        }
      : {}),
  };
}

/**
 * The resources of the scheduler from the configuration (data only): the slots (`gpuSlots` replaces those of "gpu" when
 * given) and the probe, with the default timeout and no settling time. "gpu" is always there.
 */
export function specsFromConfig(config: QueueConfig, gpuSlots = 0): ResourceSpec[] {
  const specs = Object.entries(config.resources ?? {}).map(([name, resource]): ResourceSpec => {
    const probe = resource.probe;
    return {
      name,
      slots: name === DefaultResource && gpuSlots > 0 ? gpuSlots : (resource.slots ?? 1),
      probe:
        probe === undefined
          ? null
          : {
              argv: [...probe.argv],
              min: probe.min,
              timeoutMs: probe.timeoutMs ?? ProbeTimeoutMs,
              settleMs: probe.settleMs ?? 0,
            },
    };
  });
  if (!specs.some((spec) => spec.name === DefaultResource)) {
    specs.push({ name: DefaultResource, slots: gpuSlots > 0 ? gpuSlots : 1, probe: null });
  }
  return specs;
}

/** Whether `job`, the first waiting turn of the resource, may be granted now. */
export function turnVerdict<J extends ResourceJob>(
  job: ResourceJob,
  usage: ResourceUsage<J>,
  state: ResourceState,
  now: number,
): TurnVerdict {
  if (usage.exclusiveBusy) {
    return "exclusive-busy";
  }
  if (job.share !== "shared") {
    return usage.sharedTurns.length > 0 ? "shared-busy" : "grant";
  }
  if (usage.sharedTurns.length >= state.spec.slots) {
    return "full";
  }
  if (usage.sharedTurns.length === 0 || state.spec.probe === null) {
    return "grant";
  }
  const probe = state.probe;
  const usable =
    state.assumeProbeOk === true ||
    (probe !== null && probe.ok && probe.generation === state.generation && now - probe.at <= ProbeFreshMs);
  return usable ? "grant" : "probe";
}
