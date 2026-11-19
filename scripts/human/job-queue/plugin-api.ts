// The plugin API contract (PluginApiVersion = 1): the single module that plugins import for types and pure helpers.

import type * as Assert from "node:assert";
import type { ReadStep } from "./command-reader.ts";
import type { CommandKeys, PathPlace } from "./estimate-key.ts";
import type { ShellName } from "./constants.ts";
import type { LegendEntry, ScreenLine } from "./screen-line.ts";

export {
  bare,
  rawWords,
  readCommand,
  readCommandLine,
  segmentParts,
  segmentsOf,
  variablePrefix,
} from "./command-reader.ts";
export { isTrivialStep, programOf } from "./estimate-key.ts";
export { errorCode, errorMessage } from "./history.ts";
export { dropShortWords } from "./short-name.ts";
export type { CommandKeys, LegendEntry, PathPlace, ReadStep, ScreenLine, ShellName };
export type AssertModule = typeof Assert;

/** Plugin API version; the core accepts only plugins matching this version. */
export const PluginApiVersion = 1;

export type ShareMode = "shared" | "exclusive";

/** Read-only client context passed to client/view hooks. */
export interface ClientContext {
  /** Working directory of the job (process.cwd()). */
  readonly cwd: string;
  /** Worktree root containing cwd (resolveWorktree). Data files are read from here. */
  readonly worktree: string;
  /** Directory containing job-queue.config.json (same checkout as this client). */
  readonly configRoot: string;
  /** Shell running the command. */
  readonly shell: ShellName;
  readonly env: Readonly<NodeJS.ProcessEnv>;
  /** Plugin-specific options from config plugins[].options (or {}). */
  readonly options: Readonly<Record<string, unknown>>;
  /** Writes a single [job-queue]-prefixed line to stderr. */
  log(text: string): void;
}

/** Resource requested by the user (e.g. run --gpu). */
export interface ResourceRequest {
  /** Resource name (e.g. "gpu"). */
  readonly name: string;
  /** True when --gpu-exclusive was requested; overrides plugin share answers. */
  readonly forcedExclusive: boolean;
}

/** Result of classify; each field uses the first non-undefined value across plugins. */
export interface Classification {
  /** True if this command runs tests. */
  test?: boolean;
  /** Memory pool budget in MB (>= 0). */
  memoryMb?: number;
  /** Share mode for the requested resource; defaults to "exclusive". */
  share?: ShareMode;
}

/** Result of check; the first refuse stops execution, hints are collected in order. */
export interface CheckResult {
  refuse?: string;
  hints?: string[];
}

/** Two-step build/turn split plan for commands requesting a turn resource. */
export interface SplitPlan {
  before: string;
  after: string;
  beforeRole: string;
  afterRole: string;
}

/** Grant information passed to prepareCommand. */
export interface GrantInfo {
  readonly jobId: number;
  readonly cpus: number;
  readonly nested: boolean;
}

/** Result of prepareCommand; command chains sequentially, env merges later-wins, note joins with ", ". */
export interface PreparedCommand {
  command?: string;
  env?: Record<string, string>;
  note?: string;
}

/** Context for a single step passed to stepKeys. */
export interface StepContext {
  /** Directory the step executes in after tracking cd / Set-Location / Push-Location. */
  readonly cwd: string;
  /** Resolves a project or script argument relative to its worktree root. */
  resolvePath(argument: string): { text: string; where: "" | ":outside" | ":ignored" };
}

export interface StepKeys {
  normalized: string;
  shape: string;
  kind: string;
}

/** Lower panel rendered in watch view; the first plugin returning a non-undefined panel wins. */
export interface ViewPanel {
  /** Called periodically; returns null when unavailable. */
  read(): unknown | null;
  render(data: unknown, jobs: readonly PanelJob[], columns: number, rows: number, now: number): ScreenLine[];
  summary(data: unknown): string;
  help(data: unknown): LegendEntry[];
}

/** A digest of a worktree's content (contentDigest) and how long it took. */
export interface ContentDigest {
  digest: string;
  ms: number;
}

export interface PanelJob {
  readonly id: number;
  readonly worktree: string;
  readonly cwd?: string;
  readonly session?: string;
}

export interface QueuePlugin {
  /** Plugin name matching config plugins[].name. */
  readonly name: string;
  readonly apiVersion: typeof PluginApiVersion;

  /** [all] Validates options/command; resource is null when no turn resource is requested. */
  check?(command: string, resource: ResourceRequest | null, context: ClientContext): CheckResult | undefined;

  /** [first] Splits out a pre-turn step to run without the resource. */
  splitBeforeTurn?(command: string, resource: ResourceRequest, context: ClientContext): SplitPlan | undefined;

  /** [first per-field] Classifies whether the command is a test, its memory budget, and its share mode. */
  classify?(command: string, resource: ResourceRequest | null, context: ClientContext): Classification | undefined;

  /** [first] Computes estimate keys for one step. */
  stepKeys?(step: ReadStep, context: StepContext): StepKeys | undefined;

  /** [first] Returns the agent chat session id for the job. */
  session?(context: ClientContext): string | undefined;

  /** [sequential] Rewrites the command, injects env vars, and adds grant-line notes. */
  prepareCommand?(command: string, grant: GrantInfo, context: ClientContext): PreparedCommand | undefined;

  /** [sequential] Shortens a command label for the watch view. */
  shortName?(command: string, short: string): string | undefined;

  /**
   * [first, asynchronous] A digest of the content of context.worktree, taken when a job is queued and again when it
   * starts: when the two differ, the job says that its worktree changed after it was queued. Undefined: no digest (not
   * a worktree this plugin knows, or it failed); the job then says nothing.
   */
  contentDigest?(context: ClientContext): Promise<ContentDigest | undefined>;

  /** [first] Provides the lower panel for the watch view. */
  panel?(context: ClientContext): ViewPanel | undefined;

  /** Pure helper functions exposed to repository plugins. */
  readonly api?: Readonly<Record<string, unknown>>;
}
