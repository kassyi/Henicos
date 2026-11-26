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
export { hasEnvKey } from "./remote/env-policy.ts";
export { dropShortWords } from "./short-name.ts";
export type { CommandKeys, LegendEntry, PathPlace, ReadStep, ScreenLine, ShellName };
export type AssertModule = typeof Assert;

/** Plugin API version; the core accepts only plugins matching this version. */
export const PluginApiVersion = 1;

export type ShareMode = "shared" | "exclusive";

/**
 * The answer of checkShare. `stillValid` is for a "shared" answer that rests on something that can change while the
 * turn waits (an assembly that is rebuilt): the client calls it once the scheduler has granted the shared turn and
 * before the GPU lock is taken, and when it resolves to anything but true, throws or takes too long, the client gives
 * the grant back and queues the turn again as exclusive (it loses its place).
 */
export interface ShareAnswer {
  readonly share: ShareMode;
  readonly stillValid?: () => Promise<boolean>;
}

export type OffloadDecision = "allow" | "local";

/** Result of async `checkOffload`: "allow" permits remote offload; "local" forces local execution with an optional reason. */
export interface OffloadAnswer {
  readonly offload: OffloadDecision;
  readonly reason?: string;
}

/** Caller request metadata passed to `checkOffload`. */
export interface OffloadCheckRequest {
  readonly remoteForced: boolean;
  readonly places: Readonly<Record<string, PathPlace>>;
}

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
  /** True when the command is self-contained and eligible for transparent offload to a remote Linux CPU worker. */
  offloadable?: boolean;
  /** One-line reason why `offloadable` is false (surfaced on local fallback or `--remote` refusal). */
  localReason?: string;
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

/** Worktree-relative path specification to pull back after remote execution. */
export interface RemoteCollectPathSpec {
  readonly path: string;
  readonly kind: "file" | "dir";
}

/** Parsed statement context passed to plugin `rewriteStatement` during remote command translation. */
export interface RemoteStatementContext {
  readonly program: string;
  readonly subcommand: string;
  /** Argument tokens after the program name (already translated to relative POSIX paths, unquoted). */
  readonly args: readonly string[];
  readonly keepBuildServersWarm: boolean;
  readonly cpus: number;
}

/** Result of a plugin's statement rewrite for remote execution. */
export interface RemoteStatementRewrite {
  /** Rewritten argument tokens (after the program name, unquoted). */
  args?: string[];
  /** Additional worktree-relative paths to pull back after execution. */
  collectPaths?: RemoteCollectPathSpec[];
}

/** Grant information passed to plugin `prepareRemoteCommand`. */
export interface RemoteCommandGrant {
  readonly cpus: number;
  readonly keepBuildServersWarm: boolean;
  /** Current environment map (leading assignments + defaults); plugins may avoid overriding explicit keys. */
  readonly env: Readonly<Record<string, string>>;
  /** Target platform override for case-folding checks (defaults to `process.platform`). */
  readonly platform?: NodeJS.Platform;
}

/** Result of a plugin's post-translation bash command rewrite for remote execution. */
export interface RemoteCommandRewrite {
  command?: string;
  env?: Record<string, string>;
  note?: string;
}

/** Remote failure-artifact collection and local allowlist rule contributed by a plugin. */
export interface RemoteFailureArtifactRule {
  /** Worktree-relative directories to search on the remote worker (e.g. ["tests", "src"]). */
  readonly searchDirs: readonly string[];
  /** `find -name` glob pattern on the remote worker (e.g. "*.received.*"). */
  readonly findName: string;
  /** Allowlist regex tested against worktree-relative POSIX paths on the local side. */
  readonly allowPattern: RegExp;
  /** When true, the parent directory must exist in the local worktree manifest. */
  readonly requireKnownLocalDir?: boolean;
  /** When true, stale matching files are removed on the remote worker before each run. */
  readonly cleanBeforeRun?: boolean;
}

/** Remote crash-summary diagnostic rule parsed in memory on non-zero exit (never written to local disk). */
export interface RemoteCrashSummaryRule {
  /** Worktree-relative directories to search on the remote worker (e.g. ["TestResults"]). */
  readonly searchDirs: readonly string[];
  /** `find -name` glob pattern on the remote worker (e.g. "Sequence_*.xml"). */
  readonly findName: string;
  /** Regex matching the worktree-relative POSIX path of the diagnostic file. */
  readonly pathPattern: RegExp;
  /** When true, stale matching files are removed on the remote worker before each run. */
  readonly cleanBeforeRun?: boolean;
  /** Parses diagnostic file contents and returns crashed/aborted test names. */
  parse(content: string): string[];
}

/** Remote CPU offload rules and hooks contributed by a plugin. */
export interface RemoteOffloadContribution {
  /** Additional program basenames allowed in remote offload commands (e.g. ["dotnet"], ["just"]). */
  readonly allowedPrograms?: readonly string[];
  /** Options whose next token is a filesystem path (translated to relative POSIX). */
  readonly pathValueOptions?: readonly string[];
  /** Options whose next token is NOT a filesystem path (kept verbatim). */
  readonly nonPathValueOptions?: readonly string[];
  /** Program/subcommand-specific override for whether an option's value is a filesystem path. */
  isOptionValuePath?(program: string, subcommand: string, option: string): boolean | undefined;
  /** Additional file extensions (without leading dot) recognized as path-like tokens. */
  readonly pathExtensions?: readonly string[];
  /** Options whose translated path value should be collected back as a directory after execution. */
  readonly collectDirOptions?: readonly string[];
  /** Default environment variables set on the remote worker unless already set by the command. */
  readonly defaultEnv?: Readonly<Record<string, string>>;
  /** Safe non-path caller environment variable names allowed to forward to the remote worker. */
  readonly allowedEnv?: readonly string[];
  /** Recognized build/test/path/secret environment variable names that force local execution when set in caller env. */
  readonly localOnlyEnv?: readonly string[];
  /** Additional worktree-relative POSIX directory prefixes excluded from remote source sync. */
  readonly syncExcludePrefixes?: readonly string[];
  /** Additional `find -name` glob patterns excluded from remote source sync (e.g. ["*.received.*", "*.user"]). */
  readonly syncExcludeFindNames?: readonly string[];
  /** Per-statement argument rewriter (runs after path translation, before bash quoting). */
  rewriteStatement?(stmt: RemoteStatementContext): RemoteStatementRewrite | undefined;
  /** Post-translation bash command & env rewriter (e.g. build tool parallelism flags & env vars). */
  prepareRemoteCommand?(command: string, grant: RemoteCommandGrant): RemoteCommandRewrite | undefined;
  /** Failure artifact rules pulled back on non-zero exit through the local 6-gate validator. */
  readonly failureArtifacts?: readonly RemoteFailureArtifactRule[];
  /** Crash summary rules parsed in memory on non-zero exit. */
  readonly crashSummaries?: readonly RemoteCrashSummaryRule[];
  /** Process command names (Linux `comm`) to terminate during `remote-reset` (e.g. ["dotnet", "VBCSCompiler"]). */
  readonly resetProcessNames?: readonly string[];
  /** Worktree-relative directories to wipe during `remote-reset --clean` (e.g. ["TestResults"]). */
  readonly resetCleanDirs?: readonly string[];
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

  /**
   * [first, async] Asked only for a GPU turn to which no plugin's classify gave a share (an explicit "shared" or
   * "exclusive" stands, so a plugin's "exclusive" is a veto) and that did not ask for --gpu-exclusive or set --after,
   * once, before the turn is first registered: may take a measurement that classify cannot (it runs before the turn
   * registers, so a slow answer delays the registration). "shared" or "exclusive" ends the asking; undefined passes it
   * to the next plugin. A throw, a time out or any other value takes the GPU exclusively. A plugin answers "shared" only
   * for what it measured, and answers undefined from classify for the shapes it will measure. A plain ShareMode is
   * the same as a ShareAnswer without `stillValid`.
   */
  checkShare?(command: string, context: ClientContext): Promise<ShareMode | ShareAnswer | undefined>;

  /**
   * [first, async] Asked after synchronous offload gates pass when a command is still eligible for remote CPU
   * offload: "allow" or "local" ends the asking; undefined passes it to the next plugin. A throw, timeout, or invalid
   * value fails closed to "local".
   */
  checkOffload?(
    command: string,
    request: OffloadCheckRequest,
    context: ClientContext,
  ): Promise<OffloadDecision | OffloadAnswer | undefined>;

  /** [first] Computes estimate keys for one step. */
  stepKeys?(step: ReadStep, context: StepContext): StepKeys | undefined;

  /**
   * [all] Names of environment variables whose values differ from run to run by nature (a chat session id), left out
   * of the environment hash the scheduler logs per job.
   */
  volatileEnv?(context: ClientContext): readonly string[] | undefined;

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

  /** [all] Contributes remote CPU offload rules, command rewriters, sync exclusions, and failure artifact handlers. */
  remoteOffload?(context: ClientContext): RemoteOffloadContribution | undefined;

  /** Pure helper functions exposed to repository plugins. */
  readonly api?: Readonly<Record<string, unknown>>;
}
