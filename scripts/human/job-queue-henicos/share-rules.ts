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

/**
 * Reason a single step of work cannot share the GPU, or null when it may share. Single source of truth for
 * step-level shareability (`isSharedStep` delegates here).
 *
 * `dotnet test` of a project or solution (not a .dll or .exe passed directly) needs a readable --filter that keeps
 * every exclusive category out; without one a test run also runs Stress and Benchmark, which need the GPU alone.
 *
 * The recipes of sharedRecipes are the justfile's test, test-coverage and test-differential-cuda: each expands to
 * `{{queue}} --gpu -- dotnet test ... --filter "...&Category!=Stress&Category!=Benchmark&Category!=GpuTrap"`, a shared
 * test run by the rule above (the henicos checks read the justfile to keep it so). `just <recipe>` alone, with no just
 * option or assignment before the recipe; a --filter among its arguments must keep the categories out as well.
 */
function stepShareVeto(step: ReadStep, options: ShareOptions): string | null {
  const words = step.words;
  const program = programOf(words[0] ?? "");
  if (program === "dotnet" && words[1] === "test") {
    const rest = words.slice(2);
    if (rest.some((word) => /\.(dll|exe)$/i.test(word))) {
      return "binary-test";
    }
    const filters = filtersOf(rest);
    if (filters === null) {
      return "filter-unreadable";
    }
    if (filters.length === 0) {
      return "no-filter";
    }
    return filters.every((filter) => excludesCategories(filter, options.exclusiveCategories))
      ? null
      : "filter-categories";
  }
  if (program === "just") {
    if (!options.sharedRecipes.includes(words[1] ?? "")) {
      return "exclusive-recipe";
    }
    const filters = filtersOf(words.slice(2));
    if (filters === null) {
      return "filter-unreadable";
    }
    return filters.every((filter) => excludesCategories(filter, options.exclusiveCategories))
      ? null
      : "filter-categories";
  }
  return "not-test-command";
}

function isSharedStep(step: ReadStep, options: ShareOptions): boolean {
  return stepShareVeto(step, options) === null;
}

/**
 * A step of PowerShell that only assigns one value (`$env:A=1`, `$a = 'x'`, `$env:A ='x'`, `$env:A= 'x'`). Any other
 * step that starts with `$` runs something: a program held in a variable (bash), or a command whose output is assigned
 * (`$a = dotnet test X`).
 */
function isPlainAssignment(words: readonly string[]): boolean {
  return (
    (words.length === 1 && words[0]?.includes("=") === true) ||
    (words.length === 3 && words[1] === "=") ||
    (words.length === 2 &&
      words[0]?.toLowerCase().startsWith("$env:") === true &&
      (words[0].endsWith("=") || words[1]?.startsWith("=") === true))
  );
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

/** Result of shareDecision: the share mode and a short reason token (exclusive reasons never contain `"shared"`). */
export interface ShareDecision {
  share: ShareMode;
  reason: string;
}

const AnyPwshEnvValue = String.raw`(?:'[^'\r\n]*'|"[^"\r\n]*"|[^\s;&|<>()'"\r\n]+)`;
const LeadingPwshEnv = new RegExp(String.raw`^\s*\$env:[A-Za-z_]\w*\s*=\s*${AnyPwshEnvValue}\s*(?:;\s*|\r?\n\s*)`, "i");
const AnyBashEnvValue = String.raw`(?:'[^'\r\n]*'|"[^"\r\n]*"|[^\s;&|<>()'"\r\n]*)`;
const LeadingExportEnv = new RegExp(
  String.raw`^\s*export(?:[ \t]+[A-Za-z_]\w*(?:=${AnyBashEnvValue})?)+[ \t]*(?:;\s*|\r?\n\s*)`,
);
const LeadingBashEnv = new RegExp(String.raw`^\s*[A-Za-z_]\w*=${AnyBashEnvValue}(?:[ \t]+|[ \t]*;\s*|\r?\n\s*)`);

/**
 * Strips leading `$env:`, `export`, or `NAME=VALUE` prefixes so `shareDecision` can check whether leading env
 * prefixes alone kept a command out of `nameFilterCheck`.
 */
function stripLeadingEnvPrefixes(command: string): string {
  let rest = command;
  for (;;) {
    const match = LeadingPwshEnv.exec(rest) ?? LeadingExportEnv.exec(rest) ?? LeadingBashEnv.exec(rest);
    if (match === null || match[0].length === 0) {
      return rest;
    }
    rest = rest.slice(match[0].length);
  }
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
  // 0. A command that nameFilterCheck reads is judged by the test list (checkShare), not here: no opinion
  if (nameFilterCheck(command, context) !== null) {
    return undefined;
  }
  // 1. Readable, with no pipe outside quotes (readCommand leaves the stage after a pipe out)
  const line = readCommandLine(command);
  if (line === null) {
    return { share: "exclusive", reason: "unreadable" };
  }
  if (line.piped) {
    return { share: "exclusive", reason: "piped" };
  }
  // 2. Every step that does work is a dotnet test whose filter keeps the exclusive categories out or a shared recipe,
  // there is one, and none expands a variable ($ outside single quotes; %...% of cmd): its words are not what runs
  const work = line.steps.filter((step) => !isTrivialStep(step));
  if (work.length === 0) {
    return { share: "exclusive", reason: "no-work" };
  }
  if (
    line.steps.some((step, i) => isHidden(step, line.expands[i] === true)) ||
    (context.shell === "cmd" && (command.includes("%") || hasCmdSingleQuotedMeta(command)))
  ) {
    return { share: "exclusive", reason: "hidden" };
  }
  // 3. No exclusive variable named or set
  if (usesExclusiveEnv(command, line.steps, options, context)) {
    return { share: "exclusive", reason: "exclusive-env" };
  }
  const nonShared = work.find((step) => !isSharedStep(step, options));
  if (nonShared !== undefined) {
    const veto = stepShareVeto(nonShared, options);
    if (veto === "filter-categories") {
      const stripped = stripLeadingEnvPrefixes(command);
      if (stripped !== command && nameFilterCheck(stripped, context) !== null) {
        return { share: "exclusive", reason: "env-prefix" };
      }
    }
    if (veto !== null) {
      return { share: "exclusive", reason: veto };
    }
  }
  // 4. No exclusive category selected (the same reading as the Stress budget of a test run), in any case
  const lower = command.toLowerCase();
  if (
    selectsCategory(
      lower,
      options.exclusiveCategories.map((name) => name.toLowerCase()),
    )
  ) {
    return { share: "exclusive", reason: "exclusive-category" };
  }
  // 5. Shared: via a shared recipe or explicit category exclusions
  const reason = work.some((step) => programOf(step.words[0] ?? "") === "just") ? "shared-recipe" : "categories";
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

// The test step must start the command (after allowlisted DiffEngine_Disabled or DOTNET_CLI_UI_LANGUAGE prefixes
// in bash or PowerShell `$env:` form): any other leading assignment, `export`, `cd` or `$env:` changes what the list
// would read (the program: `dotnet`, `dotnet.exe` or an absolute path to one of them; no `;`, `&` or `|` inside a word).
// Double-quoted `$env:` values reject `$`, `` ` `` and `\`, and bare `$env:` values accept digits only (`\d+`) so pwsh
// cannot execute a bareword command or expand variables past isHidden, and bash cannot escape `"` with `\"`.
const AllowedEnvName = "(?:DiffEngine_Disabled|DOTNET_CLI_UI_LANGUAGE)";
const PwshEnvValue = String.raw`(?:'[^'\r\n]*'|"[^"$\`\r\n\\]*"|\d+)`;
const PwshEnvPrefix = String.raw`\$env:${AllowedEnvName}\s*=\s*${PwshEnvValue}\s*;\s*`;
const BashEnvValue = String.raw`(?:'[^'\r\n]*'|"[^"$\`\r\n\\]*"|[^\s;&|<>()$\`"'\r\n\\]*)`;
const BashEnvPrefix = String.raw`${AllowedEnvName}=${BashEnvValue}[ \t]+`;
const AllowedEnvPrefixPattern = String.raw`(?:(?:${PwshEnvPrefix})+|(?:${BashEnvPrefix})+)`;
const DotnetTestPrefix = String.raw`(?:(?:[A-Za-z]:[\\/]|[\\/])[^\s;&|]*[\\/])?dotnet(?:\.exe)?\s+test\s`;
const StartsWithPlainTest = new RegExp(String.raw`^\s*${DotnetTestPrefix}`, "i");
const StartsWithTest = new RegExp(String.raw`^\s*(?:${AllowedEnvPrefixPattern})?${DotnetTestPrefix}`, "i");
const AllowedEnvPrefixNames = new Set(["diffengine_disabled", "dotnet_cli_ui_language"]);

/**
 * In cmd.exe, single quotes do not quote shell metacharacters (including `"`, `(` and `)`) and CR/LF truncates the
 * command even inside quotes, unlike readCommandLine's shell-neutral quote pass.
 */
function hasCmdSingleQuotedMeta(command: string): boolean {
  let quote = "";
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    if (quote !== "") {
      if (ch === "\r" || ch === "\n") {
        return true;
      }
      if (ch === quote) {
        quote = "";
      } else if (quote === "'" && /[&|<>()^;"]/.test(ch)) {
        return true;
      }
    } else if (ch === '"' || ch === "'") {
      quote = ch;
    }
  }
  return false;
}

function isAllowedEnvStep(step: ReadStep): boolean {
  if (!isPlainAssignment(step.words)) {
    return false;
  }
  const first = step.words[0] ?? "";
  if (!first.toLowerCase().startsWith("$env:")) {
    return false;
  }
  const eq = first.indexOf("=");
  const name = (eq >= 0 ? first.slice("$env:".length, eq) : first.slice("$env:".length)).toLowerCase();
  return AllowedEnvPrefixNames.has(name);
}
// Unexpanded bash braces and globs: the shell would run other words than these
const GlobOrBrace = /[{}*?[\]]/;

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
  // before the program) or builds would make it read another assembly than the run, so only allowlisted `$env:`
  // prefixes may precede the dotnet test step and only echo may follow it
  const tests = line.steps.filter((each) => programOf(each.words[0] ?? "") === "dotnet");
  const step = tests[0];
  const stepIndex = step === undefined ? -1 : line.steps.indexOf(step);
  const startsWithAllowedTest =
    context.shell === "cmd" ? StartsWithPlainTest.test(command) : StartsWithTest.test(command);
  if (
    tests.length !== 1 ||
    step === undefined ||
    !startsWithAllowedTest ||
    line.steps.some((each, i) =>
      i < stepIndex ? !isAllowedEnvStep(each) : i > stepIndex && programOf(each.words[0] ?? "") !== "echo",
    ) ||
    line.steps.some((each, i) => isHidden(each, line.expands[i] === true)) ||
    (context.shell === "cmd" && (command.includes("%") || hasCmdSingleQuotedMeta(command))) ||
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
