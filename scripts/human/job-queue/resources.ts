// The turn resources of the scheduler, with no I/O: a name, how many shared turns it allows at a time, and the
// decisions about who may take a turn (the counts are computed from the running jobs each time, never stored).

import { ProbeFreshMs } from "./constants.ts";

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
  sharedTurns: J[];
  exclusiveBusy: boolean;
}

const NamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const DefaultResource = "gpu";

/** The resource a GPU job uses: its own name when it has the form of one, else "gpu". */
export function resourceOf(job: Pick<ResourceJob, "resource">): string {
  const name = job.resource;
  return typeof name === "string" && NamePattern.test(name) ? name : DefaultResource;
}

/**
 * The use of resource `name`. A turn without share "shared" is exclusive. So is a holder that no running turn of the
 * resource started (turn null or absent, or a turn that has ended): it is the GPU lock taken by hand, by an older
 * client, or what remains of a turn whose client died.
 */
export function usageOf<J extends ResourceJob>(running: readonly J[], name: string): ResourceUsage<J> {
  const turns = running.filter((job) => job.gpuTurn === true && resourceOf(job) === name);
  const holders = running.filter((job) => job.gpu && job.gpuTurn !== true && resourceOf(job) === name);
  const tied = (holder: J): boolean => turns.some((turn) => turn.id === holder.turn);
  const sharedTurns = turns.filter((job) => job.share === "shared");
  const exclusiveBusy = turns.some((job) => job.share !== "shared") || holders.some((holder) => !tied(holder));
  return { turns, holders, sharedTurns, exclusiveBusy };
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
