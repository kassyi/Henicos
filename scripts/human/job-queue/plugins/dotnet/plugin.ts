// Built-in dotnet plugin: estimate keys, pre-turn build/test split, MSBuild node limits, test classification,
// short command labels, and pure dotnet helpers exposed via `api`.

import {
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
  const words: string[] = [];
  let atStart = true;
  for (const word of short.trim().split(/\s+/)) {
    if (word === "") {
      continue;
    }
    const separator = /^(&&|\|\||;|\|)$/.test(word) || word.endsWith(";");
    const token = word.endsWith(";") && word.length > 1 ? word.slice(0, -1) : word;
    if (token === "-nodeReuse:false") {
      // dropped
    } else if (atStart && /^dotnet(\.exe)?$/i.test(token)) {
      // dropped at step start
    } else if (!/^(&&|\|\||;|\|)$/.test(word)) {
      words.push(token);
      atStart = false;
    } else if (words.length > 0 && !/^(&&|\|\||;|\|)$/.test(words[words.length - 1] ?? "")) {
      words.push(word);
    }
    if (separator) {
      atStart = true;
      if (word.length > 1 && words.length > 0 && !/^(&&|\|\||;|\|)$/.test(words[words.length - 1] ?? "")) {
        words.push(";");
      }
    }
  }
  while (words.length > 0 && /^(&&|\|\||;|\|)$/.test(words[words.length - 1] ?? "")) {
    words.pop();
  }
  if (words.length === 0) {
    return undefined;
  }
  const next = words.join(" ");
  return next === short.trim() ? undefined : next;
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

  prepareCommand(command: string, grant: GrantInfo): PreparedCommand | undefined {
    if (grant.cpus <= 0) {
      return undefined;
    }
    return {
      command: limitMsbuildNodes(command, grant.cpus),
      env: { [MsbuildArgsVariable]: `-m:${grant.cpus}` },
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
