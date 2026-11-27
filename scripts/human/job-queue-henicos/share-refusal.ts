// The refusal of a shared `--gpu` run whose command is outside the share language (command-language.ts). One
// definition for the client plugin (plugin.ts, which refuses the run) and the hook (hook-rules.js, which queues a plain
// command with --gpu-exclusive instead of a --gpu that the run would refuse).
//
// Usage: sharedTurnRefusal(command, shell, pluginOptions) returns the refusal text, or null when the run may ask to
// share (the share options are absent, or the command after the build split is in the language).

import { type ShellName } from "../job-queue/plugin-api.ts";
import { planBuildStep } from "../job-queue/plugins/dotnet/build-split.ts";
import { formatOutsideLanguageRefusal, parseSharedCommand } from "./command-language.ts";
import { shareOptions } from "./share-rules.ts";

/** The refusal text of a shared `--gpu` run of `command` in `shell`, or null when the share language accepts it. */
export function sharedTurnRefusal(
  command: string,
  shell: ShellName,
  pluginOptions: Readonly<Record<string, unknown>>,
): string | null {
  const options = shareOptions(pluginOptions);
  if (options === null) {
    return null;
  }
  const split = planBuildStep(command, shell);
  const turnCommand = split !== null ? split.tests : command;
  const parsed = parseSharedCommand(turnCommand, shell);
  if (parsed !== null && (parsed.kind !== "recipe" || options.sharedRecipes.includes(parsed.name))) {
    return null;
  }
  return formatOutsideLanguageRefusal(turnCommand, shell, {
    ...(split !== null ? { splitBuild: { build: split.build, originalCommand: command } } : {}),
    sharedRecipes: options.sharedRecipes,
  });
}
