// Whether a --gpu run of this repository may share the GPU with another shared run: only a command in the allow-listed
// command language (command-language.ts) whose filter keeps out every category that needs the GPU alone (Stress,
// Benchmark, the real GPU faults of GpuTrap) and that does not turn the trap tests on. Anything outside the language is
// exclusive ("not-language").

import * as fs from "node:fs";
import * as path from "node:path";
import { type ClientContext, type ShareMode } from "../job-queue/plugin-api.ts";
import { selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";
import { parseSharedCommand, type SharedCommand } from "./command-language.ts";

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

/**
 * A filter that keeps every exclusive category out: read in lower case and without blanks, it has no `|` (an or term
 * could bring a category back) and, among its `&` terms, `category!=<name>` for each exclusive category. A filter that
 * names tests alone (`FullyQualifiedName~Foo`) may still pick Stress or Benchmark tests, so it does not share.
 */
function excludesCategories(filter: string, categories: readonly string[]): boolean {
  const text = filter.toLowerCase().replace(/\s+/g, "");
  if (text.includes("|")) {
    return false;
  }
  const terms = new Set(text.split("&"));
  return categories.every((name) => terms.has(`category!=${name.toLowerCase()}`));
}

/** A set variable counts unless it is empty or "0"; names compare in any case, as Windows reads them. */
function isSet(env: Readonly<NodeJS.ProcessEnv>, name: string): boolean {
  const wanted = name.toLowerCase();
  return Object.entries(env).some(
    ([key, value]) => key.toLowerCase() === wanted && value !== undefined && value !== "" && value !== "0",
  );
}

/** Whether an exclusive variable is named in the parsed command or set in the environment, in any case. */
function usesExclusiveEnv(parsed: SharedCommand, options: ShareOptions, context: ClientContext): boolean {
  const parts = parsed.args.join(" ").toLowerCase();
  return options.exclusiveEnv.some((name) => parts.includes(name.toLowerCase()) || isSet(context.env, name));
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
    // Not a directory (or not readable): not a project the list can name
    return null;
  }
}

/** A .csproj, or a directory that holds exactly one .csproj and no solution (`dotnet test` takes either of them). */
function isOneProject(project: string, cwd: string, entries: DirectoryEntries): boolean {
  if (/\.csproj$/i.test(project)) {
    return true;
  }
  const names = entries(path.resolve(cwd, project));
  return (
    names !== null &&
    names.filter((name) => /\.csproj$/i.test(name)).length === 1 &&
    !names.some((name) => /\.(sln|slnx|slnf)$/i.test(name))
  );
}

// A word that can be put back into a command between double quotes in every shell
const SafeWord = /^[\w.+\-/\\:]+$/;
// Positive name terms only: no parentheses, `&` or `!`, and no character a shell reads inside double quotes
const NameTerm = /^(?:FullyQualifiedName|Name|DisplayName)[~=][^&|()!"$`%\\\r\n]+$/;
const FlagOptions =
  /^(?:--no-build|--no-restore|--nologo|--blame|--blame-crash|--disable-build-servers|-nodeReuse:(?:false|true))$/i;
const ValueOptions = new Set([
  "-c",
  "--configuration",
  "-f",
  "--framework",
  "-v",
  "--verbosity",
  "-l",
  "--logger",
  "--results-directory",
  "--blame-hang-timeout",
  "--blame-crash-dump-type",
  "--diag",
]);

function nameFilterFromParsed(
  parsed: SharedCommand,
  options: ShareOptions,
  context: ClientContext,
  entries: DirectoryEntries,
): NameFilterTest | null {
  if (parsed.kind !== "test" || parsed.filter === null || usesExclusiveEnv(parsed, options, context)) {
    return null;
  }
  const words = parsed.args;
  let noBuild = false;
  const positional: string[] = [];
  let configuration: string | undefined;
  let framework: string | undefined;
  for (let i = 0; i < words.length; i++) {
    const word = words[i] as string;
    const lower = word.toLowerCase();
    if (lower === "--filter") {
      i++;
    } else if (ValueOptions.has(lower)) {
      const value = words[++i];
      if (value === undefined || value.startsWith("-")) {
        return null;
      }
      if (lower === "-c" || lower === "--configuration") {
        configuration = value;
      } else if (lower === "-f" || lower === "--framework") {
        framework = value;
      }
    } else if (!FlagOptions.test(word)) {
      if (word.startsWith("-") || /\.(dll|exe|sln|slnx|slnf)$/i.test(word) || !SafeWord.test(word)) {
        return null;
      }
      positional.push(word);
    } else if (lower === "--no-build") {
      noBuild = true;
    }
  }
  const terms = parsed.filter.split("|").map((term) => term.trim());
  const project = positional[0];
  if (
    !noBuild ||
    positional.length !== 1 ||
    project === undefined ||
    !isOneProject(project, context.cwd, entries) ||
    !terms.every((term) => NameTerm.test(term)) ||
    [configuration, framework].some((value) => value !== undefined && !SafeWord.test(value))
  ) {
    return null;
  }
  return {
    project,
    filter: terms.join("|"),
    ...(configuration !== undefined ? { configuration } : {}),
    ...(framework !== undefined ? { framework } : {}),
  };
}

/**
 * The `dotnet test` of a command in the allow-listed language that has only positive name terms in its filter, or null.
 * The command must name one project or directory (not a .dll, .exe or a solution), pass `--no-build`, have a
 * `--filter` of `|`-joined `FullyQualifiedName`, `Name` or `DisplayName` terms with `~` or `=`, use only options this
 * knows do not change the selection, and neither name nor set an exclusive variable.
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
 * Single decision branch for a parsed command: returns null when the command may share, or the short veto reason token
 * when it must stay exclusive. Both the share decision and the reason come from the exact same branch.
 */
function commandShareVeto(parsed: SharedCommand, options: ShareOptions, context: ClientContext): string | null {
  if (usesExclusiveEnv(parsed, options, context)) {
    return "exclusive-env";
  }
  if (parsed.args.some((word) => word.toLowerCase().includes("testcasefilter"))) {
    return "filter-unreadable";
  }
  if (parsed.args.some((word) => /^[-/]{1,2}p(?:roperty)?:/i.test(word))) {
    return "msbuild-property";
  }
  const lowerCategories = options.exclusiveCategories.map((name) => name.toLowerCase());
  if (parsed.kind === "recipe") {
    if (!options.sharedRecipes.includes(parsed.recipe)) {
      return "exclusive-recipe";
    }
    if (parsed.filter !== null) {
      if (selectsCategory(parsed.filter.toLowerCase(), lowerCategories)) {
        return "exclusive-category";
      }
      if (!excludesCategories(parsed.filter, options.exclusiveCategories)) {
        return "filter-categories";
      }
    }
    return null;
  }
  if (parsed.args.some((word) => /\.(dll|exe)$/i.test(word))) {
    return "binary-test";
  }
  if (parsed.filter === null) {
    return "no-filter";
  }
  if (selectsCategory(parsed.filter.toLowerCase(), lowerCategories)) {
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
  return { share: "shared", reason: parsed.kind === "recipe" ? "shared-recipe" : "categories" };
}

/**
 * The share of a --gpu command: "shared" when every rule holds, "exclusive" when one fails (a veto: checkShare is not
 * asked), undefined (no opinion) when the plugin options are missing or malformed and for a command that
 * nameFilterCheck reads, which the test list judges.
 */
export function shareOf(command: string, context: ClientContext): ShareMode | undefined {
  return shareDecision(command, context)?.share;
}
