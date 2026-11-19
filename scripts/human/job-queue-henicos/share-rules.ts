// Whether a --gpu run of this repository may share the GPU with another shared run: only a filtered test run that
// selects none of the categories that need the GPU alone (Stress, Benchmark, the real GPU faults of GpuTrap) and does
// not turn the trap tests on. Anything this cannot read is exclusive.

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

/** `dotnet test` of a project or solution (not a .dll or .exe passed directly) with a non-empty --filter. */
function isFilteredDotnetTest(words: readonly string[]): boolean {
  if (programOf(words[0] ?? "") !== "dotnet" || words[1] !== "test") {
    return false;
  }
  const rest = words.slice(2);
  if (rest.some((word) => /\.(dll|exe)$/i.test(word))) {
    return false;
  }
  // Without a filter a test run also runs Stress and Benchmark, which need the GPU alone
  return rest.some((word, i) => (word === "--filter" && (rest[i + 1] ?? "") !== "") || /^--filter=./.test(word));
}

/**
 * The recipes of sharedRecipes are the justfile's test, test-coverage and test-differential-cuda: each expands to
 * `{{queue}} --gpu -- dotnet test ... --filter "..."`, a filtered test run of the shared kind. `just <recipe>` alone,
 * with no just option or assignment before the recipe.
 */
function isSharedRecipe(words: readonly string[], recipes: readonly string[]): boolean {
  return programOf(words[0] ?? "") === "just" && recipes.includes(words[1] ?? "");
}

function isSharedStep(step: ReadStep, options: ShareOptions): boolean {
  return isFilteredDotnetTest(step.words) || isSharedRecipe(step.words, options.sharedRecipes);
}

/** A set variable counts unless it is empty or "0". */
function isSet(value: string | undefined): boolean {
  return value !== undefined && value !== "" && value !== "0";
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
  // 2. Every step that does work is a filtered dotnet test or a shared recipe, and there is one
  const work = line.steps.filter((step) => !isTrivialStep(step));
  if (work.length === 0 || !work.every((step) => isSharedStep(step, options))) {
    return "exclusive";
  }
  // 3. No exclusive category selected (the same reading as the Stress budget of a test run)
  if (selectsCategory(command, options.exclusiveCategories)) {
    return "exclusive";
  }
  // 4 and 5. No exclusive variable named in the command or set in the environment
  if (options.exclusiveEnv.some((name) => command.includes(name) || isSet(context.env[name]))) {
    return "exclusive";
  }
  return "shared";
}
