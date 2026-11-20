// Whether a --gpu run of this repository may share the GPU with another shared run: only a test run whose filter keeps
// out every category that needs the GPU alone (Stress, Benchmark, the real GPU faults of GpuTrap) and that does not
// turn the trap tests on. Anything this cannot read is exclusive.

import * as fs from "node:fs";
import * as path from "node:path";
import {
  isTrivialStep,
  programOf,
  readCommandLine,
  type ClientContext,
  type ReadStep,
  type ShareMode,
} from "../job-queue/plugin-api.ts";
import { selectsCategory } from "../job-queue/plugins/dotnet/plugin.ts";

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
 * The values of the --filter options of a step (`--filter v`, `--filter=v`; "" for a --filter without a value), or null
 * when the step has another spelling of a filter that this does not read (`--filter:v`, any word that names a test
 * case filter: `/TestCaseFilter:v`, `-p:VSTestTestCaseFilter=v`, `RunConfiguration.TestCaseFilter=v`).
 */
function filtersOf(words: readonly string[]): string[] | null {
  const filters: string[] = [];
  for (let i = 0; i < words.length; i++) {
    const word = (words[i] as string).toLowerCase();
    if (word === "--filter") {
      filters.push(words[i + 1] ?? "");
      i++;
    } else if (word.startsWith("--filter=")) {
      filters.push((words[i] as string).slice("--filter=".length));
    } else if (word.startsWith("--filter") || word.includes("testcasefilter")) {
      return null;
    }
  }
  return filters;
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

/** Every --filter of the step keeps the exclusive categories out (true for a step without one). */
function filtersExclude(words: readonly string[], categories: readonly string[]): boolean {
  const filters = filtersOf(words);
  return filters !== null && filters.every((filter) => excludesCategories(filter, categories));
}

/**
 * `dotnet test` of a project or solution (not a .dll or .exe passed directly) with a --filter that keeps the exclusive
 * categories out: without one a test run also runs Stress and Benchmark, which need the GPU alone.
 */
function isSharedDotnetTest(words: readonly string[], categories: readonly string[]): boolean {
  if (programOf(words[0] ?? "") !== "dotnet" || words[1] !== "test") {
    return false;
  }
  const rest = words.slice(2);
  return !rest.some((word) => /\.(dll|exe)$/i.test(word)) && rest.some((word) => /^--filter(=|$)/i.test(word))
    ? filtersExclude(rest, categories)
    : false;
}

/**
 * The recipes of sharedRecipes are the justfile's test, test-coverage and test-differential-cuda: each expands to
 * `{{queue}} --gpu -- dotnet test ... --filter "...&Category!=Stress&Category!=Benchmark&Category!=GpuTrap"`, a shared
 * test run by the rule above (the henicos checks read the justfile to keep it so). `just <recipe>` alone, with no just
 * option or assignment before the recipe; a --filter among its arguments must keep the categories out as well.
 */
function isSharedRecipe(words: readonly string[], options: ShareOptions): boolean {
  return (
    programOf(words[0] ?? "") === "just" &&
    options.sharedRecipes.includes(words[1] ?? "") &&
    filtersExclude(words.slice(2), options.exclusiveCategories)
  );
}

function isSharedStep(step: ReadStep, options: ShareOptions): boolean {
  return isSharedDotnetTest(step.words, options.exclusiveCategories) || isSharedRecipe(step.words, options);
}

/**
 * A step of PowerShell that only assigns one value (`$env:A=1`, `$a = 'x'`). Any other step that starts with `$` runs
 * something: a program held in a variable (bash), or a command whose output is assigned (`$a = dotnet test X`).
 */
function isPlainAssignment(words: readonly string[]): boolean {
  return (words.length === 1 && words[0]?.includes("=") === true) || (words.length === 3 && words[1] === "=");
}

/** A word that assigns to a name a variable makes (`$V=1`, `${P}X=1`): which variable it sets is not known. */
function assignsHiddenName(word: string): boolean {
  return word.slice(0, Math.max(0, word.indexOf("="))).includes("$");
}

/**
 * A step whose words are not what runs (a variable the shell expands), that runs something a variable names, or that
 * sets a variable whose name a variable makes (`export $V=1`, also in a step that does no work).
 */
function isHidden(step: ReadStep, expands: boolean): boolean {
  if (step.words[0]?.startsWith("$") === true) {
    return !isPlainAssignment(step.words);
  }
  return (expands && !isTrivialStep(step)) || step.words.some(assignsHiddenName);
}

/** A set variable counts unless it is empty or "0"; names compare in any case, as Windows reads them. */
function isSet(env: Readonly<NodeJS.ProcessEnv>, name: string): boolean {
  const wanted = name.toLowerCase();
  return Object.entries(env).some(
    ([key, value]) => key.toLowerCase() === wanted && value !== undefined && value !== "" && value !== "0",
  );
}

/**
 * The share of a --gpu command: "shared" when every rule holds, "exclusive" when one fails, undefined (exclusive, the
 * core's default) when the plugin options are missing or malformed.
 */
export function shareOf(command: string, context: ClientContext): ShareMode | undefined {
  const options = shareOptions(context.options);
  if (options === null) {
    return undefined;
  }
  // 1. Readable, with no pipe outside quotes (readCommand leaves the stage after a pipe out)
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return "exclusive";
  }
  // 2. Every step that does work is a dotnet test whose filter keeps the exclusive categories out or a shared recipe,
  // there is one, and none expands a variable ($ outside single quotes; %...% of cmd): its words are not what runs
  const work = line.steps.filter((step) => !isTrivialStep(step));
  if (
    work.length === 0 ||
    line.steps.some((step, i) => isHidden(step, line.expands[i] === true)) ||
    (context.shell === "cmd" && command.includes("%")) ||
    !work.every((step) => isSharedStep(step, options))
  ) {
    return "exclusive";
  }
  // 3. No exclusive category selected (the same reading as the Stress budget of a test run), in any case
  const lower = command.toLowerCase();
  if (
    selectsCategory(
      lower,
      options.exclusiveCategories.map((name) => name.toLowerCase()),
    )
  ) {
    return "exclusive";
  }
  // 4 and 5. No exclusive variable named or set
  return usesExclusiveEnv(command, line.steps, options, context) ? "exclusive" : "shared";
}

/**
 * Whether an exclusive variable is named in the command (its text, and its words without quotes and without the `\`
 * escapes of bash) or set in the environment, in any case.
 */
function usesExclusiveEnv(
  command: string,
  steps: readonly ReadStep[],
  options: ShareOptions,
  context: ClientContext,
): boolean {
  const lower = command.toLowerCase();
  const words = steps
    .flatMap((step) => step.words)
    .join(" ")
    .replaceAll("\\", "")
    .toLowerCase();
  const named = (name: string): boolean => lower.includes(name.toLowerCase()) || words.includes(name.toLowerCase());
  return options.exclusiveEnv.some((name) => named(name) || isSet(context.env, name));
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

// The test step must start the command (after DiffEngine_Disabled=... alone): any other leading assignment, `export`,
// `cd` or `$env:` changes what the list would read
const StartsWithTest = /^\s*(?:DiffEngine_Disabled=\S*\s+)*(?:\S*[\\/])?dotnet(?:\.exe)?\s+test\s/i;
// Unexpanded bash braces and globs: the shell would run other words than these
const GlobOrBrace = /[{}*?[\]]/;

/** The names in a directory, or null when it cannot be read; a parameter of nameFilterCheck so that a test stays offline. */
export type DirectoryEntries = (directory: string) => string[] | null;

function readEntries(directory: string): string[] | null {
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
const FlagOptions = /^(?:--no-build|--no-restore|--nologo|--blame|--blame-crash|-nodeReuse:(?:false|true))$/i;
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
  "--diag",
]);

/**
 * The `dotnet test` of a command that has only positive name terms in its filter, or null. The command must read
 * plainly (one step of work, no pipe, no expanded variable), name one project or directory (not a .dll, .exe or a
 * solution: their tests would have to be listed assembly by assembly), have one `--filter` that is `|`-joined
 * `FullyQualifiedName`, `Name` or `DisplayName` terms with `~` or `=`, use only options this knows do not change the
 * selection, and neither name nor set an exclusive variable. A negated term or one on another property is left to
 * `shareOf`: it stays exclusive.
 */
export function nameFilterCheck(
  command: string,
  context: ClientContext,
  entries: DirectoryEntries = readEntries,
): NameFilterTest | null {
  const options = shareOptions(context.options);
  const line = readCommandLine(command);
  if (options === null || line === null || line.piped) {
    return null;
  }
  // The list runs in the client's directory and environment: a step that changes them (cd, export, an assignment
  // before the program) or builds would make it read another assembly than the run, so only echo may stand beside the
  // dotnet test, which must follow nothing but DiffEngine_Disabled=...
  const tests = line.steps.filter((each) => programOf(each.words[0] ?? "") === "dotnet");
  const step = tests[0];
  if (
    tests.length !== 1 ||
    step === undefined ||
    !StartsWithTest.test(command) ||
    line.steps.some((each) => each !== step && programOf(each.words[0] ?? "") !== "echo") ||
    line.steps.some((each, i) => isHidden(each, line.expands[i] === true)) ||
    (context.shell === "cmd" && command.includes("%")) ||
    step.words[1] !== "test" ||
    step.words.some((word) => GlobOrBrace.test(word)) ||
    usesExclusiveEnv(command, line.steps, options, context)
  ) {
    return null;
  }
  const words = step.words.slice(2);
  let noBuild = false;
  const filters: string[] = [];
  const positional: string[] = [];
  let configuration: string | undefined;
  let framework: string | undefined;
  for (let i = 0; i < words.length; i++) {
    const word = words[i] as string;
    const lower = word.toLowerCase();
    if (lower === "--filter" || lower.startsWith("--filter=")) {
      filters.push(lower === "--filter" ? (words[++i] ?? "") : word.slice("--filter=".length));
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
  const filter = filters[0];
  const terms = filter?.split("|").map((term) => term.trim());
  const project = positional[0];
  if (
    !noBuild ||
    filters.length !== 1 ||
    positional.length !== 1 ||
    project === undefined ||
    !isOneProject(project, context.cwd, entries) ||
    filter === undefined ||
    terms === undefined ||
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
