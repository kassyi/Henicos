// Built-in dotnet plugin: estimate keys, pre-turn build/test split, MSBuild node limits, test classification,
// short command labels, and pure dotnet helpers exposed via `api`.

import {
  dropShortWords,
  PluginApiVersion,
  type Classification,
  type ClientContext,
  type GrantInfo,
  type PreparedCommand,
  type QueuePlugin,
  type ReadStep,
  type ResourceRequest,
  type SplitPlan,
  type StepContext,
  type StepKeys,
} from "../../plugin-api.ts";
import { planBuildStep } from "./build-split.ts";
import { dotnetKeys } from "./estimate-keys.ts";
import { limitMsbuildNodes } from "./msbuild-nodes.ts";

const MsbuildArgsVariable = "GSHARP_JOB_QUEUE_MSBUILD_ARGS";

// IBuildEngine9.RequestCores is bounded by MSBUILDCORELIMIT, not by -m. The scheduler hands out
// MSBUILDCORELIMIT - max(1, executing requests * weight / 100) - cores already granted, so a limit of the grant with a
// weight of 100 keeps (requests executing + cores granted) within the grant when a task asks. The default weight of 0
// counts one implicit core only, which lets every other node run beside the explicit cores.
const CoreLimitVariable = "MSBUILDCORELIMIT";
const CoreWeightVariable = "MSBUILDNODECOREALLOCATIONWEIGHT";

const DotnetTestPattern = new RegExp(
  [String.raw`\bdotnet(\.exe)?"?\s+(test|vstest|stryker)\b`, String.raw`\bdotnet-stryker\b`].join("|"),
);

/** True when `command` invokes a dotnet test runner (`dotnet test`, `dotnet vstest`, `dotnet stryker`). */
export function isDotnetTest(command: string): boolean {
  return DotnetTestPattern.test(command);
}

/**
 * True when `command` names one of the categories `names` as a word, outside a negative filter term: the word after
 * `!=` or `!~` (Category!=Stress, as just test passes) does not select it. The same reading as the Stress budget of a
 * test run, so that a filter selects the same tests for the budget and for the GPU share.
 */
export function selectsCategory(command: string, names: readonly string[]): boolean {
  const words = names.filter((name) => name.length > 0).map((name) => name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
  return words.length > 0 && new RegExp(`\\b(?:${words.join("|")})\\b`).test(command.replace(/!(=|~)\s*\w+/g, ""));
}

function programOf(word: string): string {
  const parts = word.split("\\").join("/").split("/");
  return (parts[parts.length - 1] as string).toLowerCase().replace(/\.exe$/, "");
}

function dotnetShortName(_command: string, short: string): string | undefined {
  return dropShortWords(
    short,
    (word, atStart) => word === "-nodeReuse:false" || (atStart && /^dotnet(\.exe)?$/i.test(word)),
  );
}

const plugin = {
  name: "dotnet",
  apiVersion: PluginApiVersion,

  stepKeys(step: ReadStep, context: StepContext): StepKeys | undefined {
    const first = step.words[0];
    if (first === undefined || programOf(first) !== "dotnet") {
      return undefined;
    }
    return dotnetKeys(step.words, context);
  },

  splitBeforeTurn(command: string, resource: ResourceRequest, context: ClientContext): SplitPlan | undefined {
    if (resource.name !== "gpu") {
      return undefined;
    }
    const plan = planBuildStep(command, context.shell);
    if (plan === null) {
      return undefined;
    }
    return {
      before: plan.build,
      after: plan.tests,
      beforeRole: "build, no GPU",
      afterRole: "tests, GPU turn",
    };
  },

  prepareCommand(command: string, grant: GrantInfo, context: ClientContext): PreparedCommand | undefined {
    if (grant.cpus <= 0) {
      return undefined;
    }
    // A value the caller exported is kept; one set in the command itself (VAR=x dotnet ..., $env:VAR = ...) runs
    // after this env and wins in the shell. The plugin sees the command text and the client's environment only.
    const env: Record<string, string> = { [MsbuildArgsVariable]: `-m:${grant.cpus}` };
    if (context.env[CoreLimitVariable] === undefined) {
      env[CoreLimitVariable] = String(grant.cpus);
    }
    if (context.env[CoreWeightVariable] === undefined) {
      env[CoreWeightVariable] = "100";
    }
    return {
      command: limitMsbuildNodes(command, grant.cpus),
      env,
      note: `${grant.cpus} MSBuild nodes`,
    };
  },

  classify(command: string): Classification | undefined {
    return isDotnetTest(command) ? { test: true } : undefined;
  },

  shortName(command: string, short: string): string | undefined {
    return dotnetShortName(command, short);
  },

  api: {
    isDotnetTest,
    selectsCategory,
  },
} satisfies QueuePlugin;

export default plugin;
