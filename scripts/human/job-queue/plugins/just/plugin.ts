// Built-in just plugin: estimate keys for just recipes (`stepKeys`) and short command labels (`shortName`).

import { dropShortWords, PluginApiVersion, type QueuePlugin, type ReadStep, type StepKeys } from "../../plugin-api.ts";

function programOf(word: string): string {
  const parts = word.split("\\").join("/").split("/");
  return (parts[parts.length - 1] as string).toLowerCase().replace(/\.exe$/, "");
}

function justShortName(_command: string, short: string): string | undefined {
  return dropShortWords(short, (word, atStart) => atStart && /^just(\.exe)?$/i.test(word));
}

const plugin = {
  name: "just",
  apiVersion: PluginApiVersion,

  stepKeys(step: ReadStep): StepKeys | undefined {
    const first = step.words[0];
    if (first === undefined || programOf(first) !== "just") {
      return undefined;
    }
    const recipe = step.words.slice(1).find((word) => !word.startsWith("-")) ?? "";
    return { normalized: step.words.join(" "), shape: `just:${recipe}`, kind: "just" };
  },

  shortName(command: string, short: string): string | undefined {
    return justShortName(command, short);
  },

  remoteOffload() {
    return { allowedPrograms: ["just"] };
  },
} satisfies QueuePlugin;

export default plugin;
