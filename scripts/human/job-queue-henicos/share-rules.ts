// Whether a --gpu run of this repository may share the GPU with another shared run: only a command in the allow-listed
// command language (command-language.ts) whose filter keeps out every category that needs the GPU alone (Stress,
// Benchmark, the real GPU faults of GpuTrap) and that does not turn the trap tests on.

import * as fs from "node:fs";
import * as path from "node:path";
import { type ClientContext, type ShareMode } from "../job-queue/plugin-api.ts";
import { isValidFilter, parseSharedCommand, type MutatePhase, type SharedCommand } from "./command-language.ts";

/** Relative directory of each mutation testing phase's `stryker-config.json`. */
const MutatePhaseDirs: Readonly<Record<MutatePhase, string>> = {
  gpumath: "tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests",
  compiler: "tests/Compiler/Kassyi.GSharp.Tests.Cpu",
  "compiler-cuda": "tests/Compiler/Kassyi.GSharp.Tests.Cuda",
};

/** Allow-listed keys inside `stryker-config` (unknown keys such as `test-runner` fail closed to exclusive). */
const AllowedStrykerConfigKeys = new Set([
  "project",
  "test-projects",
  "mutate",
  "coverage-analysis",
  "test-case-filter",
  "concurrency",
  "reporters",
  "verbosity",
]);

/** The options of the henicos plugin entry that the share rules read (job-queue.config.json). */
export interface ShareOptions {
  /** Test categories a shared run must not select (a word outside a negative filter term). */
  exclusiveCategories: string[];
  /** Variables a shared run must neither name nor have set (other than empty or "0"). */
  exclusiveEnv: string[];
  /** just recipes that run a filtered dotnet test of the shared kind. */
  sharedRecipes: string[];
}

function names(value: unknown): string[] | null {
  return Array.isArray(value) && value.every((item) => typeof item === "string" && item !== "")
    ? (value as string[])
    : null;
}

/** The share options of the plugin entry; null when one is missing or not a list of names. */
export function shareOptions(options: Readonly<Record<string, unknown>>): ShareOptions | null {
  const exclusiveCategories = names(options.exclusiveCategories);
  const exclusiveEnv = names(options.exclusiveEnv);
  const sharedRecipes = names(options.sharedRecipes);
  return exclusiveCategories === null || exclusiveEnv === null || sharedRecipes === null
    ? null
    : { exclusiveCategories, exclusiveEnv, sharedRecipes };
}

/** Parses `FILTER` (already validated by `isValidFilter`) into `{ key, op, word }` terms. */
function filterTerms(filter: string): { key: string; op: string; word: string }[] {
  const result: { key: string; op: string; word: string }[] = [];
  for (const raw of filter.split(/[&|]/)) {
    const m = /^([A-Za-z]+)(!=|!~|=|~)([A-Za-z0-9_.]+)$/.exec(raw);
    if (m !== null) {
      result.push({ key: m[1] as string, op: m[2] as string, word: m[3] as string });
    }
  }
  return result;
}

/** True when any term selects an exclusive category (`Category=<cat>` or `Category~<cat>`, case-insensitive). */
export function selectsExclusiveCategory(filter: string, categories: readonly string[]): boolean {
  const lowerCats = categories.map((name) => name.toLowerCase());
  for (const term of filterTerms(filter)) {
    if (term.key.toLowerCase() !== "category") {
      continue;
    }
    const lowerWord = term.word.toLowerCase();
    if (term.op === "=" && lowerCats.some((cat) => lowerWord === cat || lowerWord.startsWith(`${cat}.`))) {
      return true;
    }
    if (term.op === "~" && lowerCats.some((cat) => lowerWord.includes(cat) || cat.includes(lowerWord))) {
      return true;
    }
  }
  return false;
}

/**
 * A filter that keeps every exclusive category out: no `|` (an OR term could bring a category back) and, among its `&`
 * terms, `Category!=<name>` for each exclusive category (case-insensitive).
 */
export function excludesCategories(filter: string, categories: readonly string[]): boolean {
  if (filter.includes("|")) {
    return false;
  }
  const negated = new Set<string>();
  for (const term of filterTerms(filter)) {
    if (term.key.toLowerCase() === "category" && term.op === "!=") {
      negated.add(term.word.toLowerCase());
    }
  }
  return categories.every((name) => negated.has(name.toLowerCase()));
}

/** A set variable counts unless it is empty or "0"; names compare in any case, as Windows reads them. */
function isSet(env: Readonly<NodeJS.ProcessEnv>, name: string): boolean {
  const wanted = name.toLowerCase();
  return Object.entries(env).some(
    ([key, value]) => key.toLowerCase() === wanted && value !== undefined && value !== "" && value !== "0",
  );
}

/** Whether an exclusive variable is set in the environment, in any case. */
function usesExclusiveEnv(options: ShareOptions, context: ClientContext): boolean {
  return options.exclusiveEnv.some((name) => isSet(context.env, name));
}

/** Result of shareDecision: the share mode and a short reason token (exclusive reasons never contain `"shared"`). */
export interface ShareDecision {
  share: ShareMode;
  reason: string;
}

/** A `dotnet test` that selects tests by name alone, as `nameFilterCheck` reads it. */
export interface NameFilterTest {
  /** The project or directory the test run names, as written. */
  project: string;
  /** The `--filter` value: positive name terms joined by `|`. */
  filter: string;
  configuration?: string;
  framework?: string;
}

/** The names in a directory, or null when it cannot be read; a parameter of nameFilterCheck so that a test stays offline. */
export type DirectoryEntries = (directory: string) => string[] | null;

export function readEntries(directory: string): string[] | null {
  try {
    return fs.readdirSync(directory);
  } catch {
    return null;
  }
}

/** A .csproj, or a directory that holds exactly one .csproj and no solution (`dotnet test` takes either of them). */
function isOneProject(project: string, cwd: string, entries: DirectoryEntries): boolean {
  if (/\.csproj$/i.test(project)) {
    return true;
  }
  const found = entries(path.resolve(cwd, project));
  return (
    found !== null &&
    found.filter((name) => /\.csproj$/i.test(name)).length === 1 &&
    !found.some((name) => /\.(sln|slnx|slnf)$/i.test(name))
  );
}

const PositiveNameKeys = new Set(["fullyqualifiedname", "name", "classname"]);

function nameFilterFromParsed(
  parsed: SharedCommand,
  options: ShareOptions,
  context: ClientContext,
  entries: DirectoryEntries,
): NameFilterTest | null {
  if (
    parsed.kind !== "test" ||
    parsed.filter === null ||
    parsed.project === null ||
    usesExclusiveEnv(options, context)
  ) {
    return null;
  }
  const hasNoBuild = parsed.options.some((opt) => opt.kind === "no-build");
  if (!hasNoBuild || /\.(dll|exe|sln|slnx|slnf)$/i.test(parsed.project)) {
    return null;
  }
  if (!isOneProject(parsed.project, context.cwd, entries)) {
    return null;
  }
  if (parsed.filter.includes("&")) {
    return null;
  }
  const terms = filterTerms(parsed.filter);
  if (
    terms.length === 0 ||
    !terms.every((t) => PositiveNameKeys.has(t.key.toLowerCase()) && (t.op === "~" || t.op === "="))
  ) {
    return null;
  }
  const cfgOpt = parsed.options.find(
    (opt): opt is Extract<typeof opt, { kind: "configuration" }> => opt.kind === "configuration",
  );
  return {
    project: parsed.project,
    filter: parsed.filter,
    ...(cfgOpt !== undefined ? { configuration: cfgOpt.value } : {}),
  };
}

/**
 * The `dotnet test` of a command in the allow-listed language that has only positive name terms in its filter, or null.
 */
export function nameFilterCheck(
  command: string,
  context: ClientContext,
  entries: DirectoryEntries = readEntries,
): NameFilterTest | null {
  const options = shareOptions(context.options);
  if (options === null) {
    return null;
  }
  const parsed = parseSharedCommand(command, context.shell);
  if (parsed === null) {
    return null;
  }
  return nameFilterFromParsed(parsed, options, context, entries);
}

/**
 * Reads `stryker-config.test-case-filter` for `phase` under `rootDir` (never following a link/junction outside it).
 * Rejects unknown top-level or `stryker-config` keys and filters that do not match the closed `FILTER` grammar.
 */
function readMutateTestCaseFilter(phase: MutatePhase, rootDir: string): string | null {
  try {
    const realRoot = fs.realpathSync.native(rootDir);
    const configPath = path.resolve(realRoot, MutatePhaseDirs[phase], "stryker-config.json");
    const realConfig = fs.realpathSync.native(configPath);
    const rel = path.relative(realRoot, realConfig);
    if (rel.startsWith("..") || path.isAbsolute(rel)) {
      return null;
    }
    const raw = JSON.parse(fs.readFileSync(realConfig, "utf8")) as unknown;
    if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
      return null;
    }
    const topKeys = Object.keys(raw as Record<string, unknown>);
    if (topKeys.length !== 1 || topKeys[0] !== "stryker-config") {
      return null;
    }
    const cfg = (raw as Record<string, unknown>)["stryker-config"];
    if (cfg === null || typeof cfg !== "object" || Array.isArray(cfg)) {
      return null;
    }
    const cfgObj = cfg as Record<string, unknown>;
    if (!Object.keys(cfgObj).every((key) => AllowedStrykerConfigKeys.has(key))) {
      return null;
    }
    const filter = cfgObj["test-case-filter"];
    if (typeof filter !== "string" || !isValidFilter(filter, true)) {
      return null;
    }
    return filter;
  } catch {
    return null;
  }
}

/**
 * Single decision branch for a parsed command: returns null when the command may share, or the short veto reason token
 * when it must stay exclusive. Both the share decision and the reason come from the exact same branch.
 */
function commandShareVeto(parsed: SharedCommand, options: ShareOptions, context: ClientContext): string | null {
  if (usesExclusiveEnv(options, context)) {
    return "exclusive-env";
  }
  if (parsed.kind === "mutate") {
    const rootDir = context.worktree || context.cwd;
    const filter = readMutateTestCaseFilter(parsed.phase, rootDir);
    if (filter === null) {
      return "mutate-config-unreadable";
    }
    if (selectsExclusiveCategory(filter, options.exclusiveCategories)) {
      return "exclusive-category";
    }
    if (!excludesCategories(filter, options.exclusiveCategories)) {
      return "mutate-filter-categories";
    }
    return null;
  }
  if (parsed.kind === "recipe") {
    return options.sharedRecipes.includes(parsed.name) ? null : "exclusive-recipe";
  }
  if (parsed.project !== null && /\.(dll|exe)$/i.test(parsed.project)) {
    return "binary-test";
  }
  if (parsed.filter === null) {
    return "no-filter";
  }
  if (selectsExclusiveCategory(parsed.filter, options.exclusiveCategories)) {
    return "exclusive-category";
  }
  if (!excludesCategories(parsed.filter, options.exclusiveCategories)) {
    return "filter-categories";
  }
  return null;
}

/**
 * The share decision and short reason token of a --gpu command: "shared" when every rule holds, "exclusive" when one
 * fails (a veto: checkShare is not asked), undefined (no opinion) when the plugin options are missing or malformed and
 * for a command that nameFilterCheck reads, which the test list judges.
 */
export function shareDecision(command: string, context: ClientContext): ShareDecision | undefined {
  const options = shareOptions(context.options);
  if (options === null) {
    return undefined;
  }
  const parsed = parseSharedCommand(command, context.shell);
  if (parsed === null) {
    return { share: "exclusive", reason: "not-language" };
  }
  if (nameFilterFromParsed(parsed, options, context, readEntries) !== null) {
    return undefined;
  }
  const veto = commandShareVeto(parsed, options, context);
  if (veto !== null) {
    return { share: "exclusive", reason: veto };
  }
  const reason = parsed.kind === "mutate" ? "mutate-config" : parsed.kind === "recipe" ? "shared-recipe" : "categories";
  return { share: "shared", reason };
}

/**
 * The share of a --gpu command: "shared" when every rule holds, "exclusive" when one fails (a veto: checkShare is not
 * asked), undefined (no opinion) when the plugin options are missing or malformed and for a command that
 * nameFilterCheck reads, which the test list judges.
 */
export function shareOf(command: string, context: ClientContext): ShareMode | undefined {
  return shareDecision(command, context)?.share;
}
