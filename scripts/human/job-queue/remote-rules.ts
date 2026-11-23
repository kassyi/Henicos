// Validates and merges `QueuePlugin.remoteOffload` contributions into a single `ResolvedRemoteRules` object.

import * as path from "node:path";
import type {
  RemoteCommandGrant,
  RemoteCommandRewrite,
  RemoteCrashSummaryRule,
  RemoteFailureArtifactRule,
  RemoteOffloadContribution,
  RemoteStatementContext,
  RemoteStatementRewrite,
} from "./plugin-api.ts";

/** Aggregated remote CPU offload rules collected from all active plugins. */
export interface ResolvedRemoteRules {
  readonly allowedPrograms: ReadonlySet<string>;
  readonly pathValueOptions: ReadonlySet<string>;
  readonly nonPathValueOptions: ReadonlySet<string>;
  readonly pathExtensions: ReadonlySet<string>;
  readonly collectDirOptions: ReadonlySet<string>;
  readonly defaultEnv: Readonly<Record<string, string>>;
  readonly syncExcludePrefixes: readonly string[];
  readonly syncExcludeFindNames: readonly string[];
  readonly failureArtifacts: readonly RemoteFailureArtifactRule[];
  readonly crashSummaries: readonly RemoteCrashSummaryRule[];
  isOptionValuePath(program: string, subcommand: string, option: string): boolean | undefined;
  rewriteStatement(stmt: RemoteStatementContext): RemoteStatementRewrite;
  prepareRemoteCommand(
    command: string,
    grant: RemoteCommandGrant,
  ): { command: string; env: Record<string, string>; note?: string };
}

/** Empty remote offload rules used when no plugins are loaded. */
export const EmptyRemoteRules: ResolvedRemoteRules = {
  allowedPrograms: new Set(),
  pathValueOptions: new Set(),
  nonPathValueOptions: new Set(),
  pathExtensions: new Set(),
  collectDirOptions: new Set(),
  defaultEnv: Object.create(null) as Record<string, string>,
  syncExcludePrefixes: [],
  syncExcludeFindNames: [],
  failureArtifacts: [],
  crashSummaries: [],
  isOptionValuePath: () => undefined,
  rewriteStatement: (stmt) => ({ args: [...stmt.args], collectPaths: [] }),
  prepareRemoteCommand: (command) => ({ command, env: Object.create(null) as Record<string, string> }),
};

/** One plugin's raw `remoteOffload` return value paired with its host-isolated invoker. */
export interface RemoteContributionEntry {
  readonly contribution: Record<string, unknown>;
  call<T>(subHook: string, invoke: () => T): T | undefined;
}

const SafeRelPosixPattern = /^[A-Za-z0-9_./-]+$/;
const SafeFindNamePattern = /^[A-Za-z0-9_.*?-]+$/;
const SafeEnvKeyPattern = /^[A-Za-z_][A-Za-z0-9_]*$/;

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function normalizeSafeDir(value: unknown): string | null {
  if (typeof value !== "string" || value === "" || value.startsWith("/") || !SafeRelPosixPattern.test(value)) {
    return null;
  }
  const normalized = path.posix.normalize(value).replace(/\/+$/, "");
  return normalized !== "" && normalized !== "." && normalized !== ".." && !normalized.startsWith("../")
    ? normalized
    : null;
}

function normalizeSafeDirs(value: unknown): string[] | null {
  if (!Array.isArray(value) || value.length === 0) {
    return null;
  }
  const dirs: string[] = [];
  for (const item of value) {
    const dir = normalizeSafeDir(item);
    if (dir === null) {
      return null;
    }
    dirs.push(dir);
  }
  return dirs;
}

function isSafeFindName(value: unknown): value is string {
  return typeof value === "string" && value !== "" && SafeFindNamePattern.test(value);
}

function collectStrings(target: Set<string>, raw: unknown, normalize: (item: string) => string | null): void {
  if (!Array.isArray(raw)) {
    return;
  }
  for (const item of raw) {
    if (typeof item !== "string") {
      continue;
    }
    const normalized = normalize(item);
    if (normalized !== null) {
      target.add(normalized);
    }
  }
}

function mergeSafeEnv(target: Record<string, string>, raw: unknown): void {
  if (!isPlainObject(raw)) {
    return;
  }
  for (const [key, value] of Object.entries(raw)) {
    if (SafeEnvKeyPattern.test(key) && typeof value === "string") {
      target[key] = value;
    }
  }
}

function collectFailureArtifacts(target: RemoteFailureArtifactRule[], raw: unknown): void {
  if (!Array.isArray(raw)) {
    return;
  }
  for (const item of raw) {
    if (!isPlainObject(item) || !isSafeFindName(item.findName) || !(item.allowPattern instanceof RegExp)) {
      continue;
    }
    const searchDirs = normalizeSafeDirs(item.searchDirs);
    if (searchDirs === null) {
      continue;
    }
    target.push({
      searchDirs,
      findName: item.findName,
      allowPattern: item.allowPattern,
      requireKnownLocalDir: item.requireKnownLocalDir === true,
      cleanBeforeRun: item.cleanBeforeRun === true,
    });
  }
}

function collectCrashSummaries(target: RemoteCrashSummaryRule[], raw: unknown, entry: RemoteContributionEntry): void {
  if (!Array.isArray(raw)) {
    return;
  }
  for (const item of raw) {
    if (
      !isPlainObject(item) ||
      !isSafeFindName(item.findName) ||
      !(item.pathPattern instanceof RegExp) ||
      typeof item.parse !== "function"
    ) {
      continue;
    }
    const searchDirs = normalizeSafeDirs(item.searchDirs);
    if (searchDirs === null) {
      continue;
    }
    const parseFn = item.parse as RemoteCrashSummaryRule["parse"];
    target.push({
      searchDirs,
      findName: item.findName,
      pathPattern: item.pathPattern,
      cleanBeforeRun: item.cleanBeforeRun === true,
      parse: (content: string) => {
        const parsed = entry.call("remoteOffload.crashSummaries.parse", () => parseFn(content));
        return Array.isArray(parsed)
          ? parsed.filter((name): name is string => typeof name === "string" && name !== "")
          : [];
      },
    });
  }
}

const asProgram = (s: string): string | null => (s.trim() !== "" ? s.trim().toLowerCase() : null);
const asOption = (s: string): string | null => (s.startsWith("-") ? s.toLowerCase() : null);
const asExtension = (s: string): string | null => (/^[A-Za-z0-9]+$/.test(s) ? s.toLowerCase() : null);
const asPrefix = (s: string): string | null => {
  const dir = normalizeSafeDir(s);
  return dir !== null ? `${dir}/` : null;
};
const asFindName = (s: string): string | null => (isSafeFindName(s) ? s : null);

type OptionPathFn = NonNullable<RemoteOffloadContribution["isOptionValuePath"]>;
type StatementRewriteFn = NonNullable<RemoteOffloadContribution["rewriteStatement"]>;
type CommandPrepareFn = NonNullable<RemoteOffloadContribution["prepareRemoteCommand"]>;

function composeOptionPathChecks(
  checks: readonly { entry: RemoteContributionEntry; fn: OptionPathFn }[],
): ResolvedRemoteRules["isOptionValuePath"] {
  return (program, subcommand, option) => {
    for (const { entry, fn } of checks) {
      const answer = entry.call("remoteOffload.isOptionValuePath", () => fn(program, subcommand, option));
      if (typeof answer === "boolean") {
        return answer;
      }
    }
    return undefined;
  };
}

function composeStatementRewriters(
  rewriters: readonly { entry: RemoteContributionEntry; fn: StatementRewriteFn }[],
): ResolvedRemoteRules["rewriteStatement"] {
  return (stmt) => {
    let args = [...stmt.args];
    const collectPaths: { path: string; kind: "file" | "dir" }[] = [];
    for (const { entry, fn } of rewriters) {
      const res = entry.call("remoteOffload.rewriteStatement", () => fn({ ...stmt, args }));
      if (!isPlainObject(res)) {
        continue;
      }
      if (Array.isArray(res.args) && res.args.every((a): a is string => typeof a === "string")) {
        args = [...res.args];
      }
      if (Array.isArray(res.collectPaths)) {
        for (const spec of res.collectPaths) {
          if (isPlainObject(spec) && typeof spec.path === "string" && (spec.kind === "file" || spec.kind === "dir")) {
            collectPaths.push({ path: spec.path, kind: spec.kind });
          }
        }
      }
    }
    return { args, collectPaths };
  };
}

function composeCommandPreparers(
  preparers: readonly { entry: RemoteContributionEntry; fn: CommandPrepareFn }[],
): ResolvedRemoteRules["prepareRemoteCommand"] {
  return (command, grant) => {
    let current = command;
    const env = Object.create(null) as Record<string, string>;
    const notes: string[] = [];
    for (const { entry, fn } of preparers) {
      const res: RemoteCommandRewrite | undefined = entry.call("remoteOffload.prepareRemoteCommand", () =>
        fn(current, { ...grant, env: { ...grant.env, ...env } }),
      );
      if (!isPlainObject(res)) {
        continue;
      }
      if (typeof res.command === "string" && res.command.length > 0) {
        current = res.command;
      }
      mergeSafeEnv(env, res.env);
      if (typeof res.note === "string" && res.note.trim() !== "") {
        notes.push(res.note);
      }
    }
    return notes.length > 0 ? { command: current, env, note: notes.join(", ") } : { command: current, env };
  };
}

/** Validates and merges all plugin `remoteOffload` contributions into one `ResolvedRemoteRules` instance. */
export function resolveRemoteRules(entries: readonly RemoteContributionEntry[]): ResolvedRemoteRules {
  if (entries.length === 0) {
    return EmptyRemoteRules;
  }
  const allowedPrograms = new Set<string>();
  const pathValueOptions = new Set<string>();
  const nonPathValueOptions = new Set<string>();
  const pathExtensions = new Set<string>();
  const collectDirOptions = new Set<string>();
  const defaultEnv = Object.create(null) as Record<string, string>;
  const syncExcludePrefixes = new Set<string>();
  const syncExcludeFindNames = new Set<string>();
  const failureArtifacts: RemoteFailureArtifactRule[] = [];
  const crashSummaries: RemoteCrashSummaryRule[] = [];
  const optionChecks: { entry: RemoteContributionEntry; fn: OptionPathFn }[] = [];
  const statementRewriters: { entry: RemoteContributionEntry; fn: StatementRewriteFn }[] = [];
  const commandPreparers: { entry: RemoteContributionEntry; fn: CommandPrepareFn }[] = [];

  for (const entry of entries) {
    const c = entry.contribution;
    collectStrings(allowedPrograms, c.allowedPrograms, asProgram);
    collectStrings(pathValueOptions, c.pathValueOptions, asOption);
    collectStrings(nonPathValueOptions, c.nonPathValueOptions, asOption);
    collectStrings(pathExtensions, c.pathExtensions, asExtension);
    collectStrings(collectDirOptions, c.collectDirOptions, asOption);
    mergeSafeEnv(defaultEnv, c.defaultEnv);
    collectStrings(syncExcludePrefixes, c.syncExcludePrefixes, asPrefix);
    collectStrings(syncExcludeFindNames, c.syncExcludeFindNames, asFindName);
    collectFailureArtifacts(failureArtifacts, c.failureArtifacts);
    collectCrashSummaries(crashSummaries, c.crashSummaries, entry);
    if (typeof c.isOptionValuePath === "function") {
      optionChecks.push({ entry, fn: c.isOptionValuePath as OptionPathFn });
    }
    if (typeof c.rewriteStatement === "function") {
      statementRewriters.push({ entry, fn: c.rewriteStatement as StatementRewriteFn });
    }
    if (typeof c.prepareRemoteCommand === "function") {
      commandPreparers.push({ entry, fn: c.prepareRemoteCommand as CommandPrepareFn });
    }
  }

  return {
    allowedPrograms,
    pathValueOptions,
    nonPathValueOptions,
    pathExtensions,
    collectDirOptions,
    defaultEnv,
    syncExcludePrefixes: [...syncExcludePrefixes],
    syncExcludeFindNames: [...syncExcludeFindNames],
    failureArtifacts,
    crashSummaries,
    isOptionValuePath: composeOptionPathChecks(optionChecks),
    rewriteStatement: composeStatementRewriters(statementRewriters),
    prepareRemoteCommand: composeCommandPreparers(commandPreparers),
  };
}
