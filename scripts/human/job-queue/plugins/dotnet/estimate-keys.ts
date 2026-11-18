// Estimate keys for dotnet steps: normalizes projects, options that change the work, and drop lists for options that
// do not. Moved verbatim from estimate-key.ts so the core has no dotnet-specific tables.

import type { StepContext, StepKeys } from "../../plugin-api.ts";

// What a dotnet command line says that does not change the work: dropped by name
const DroppedFlags = new Set(["--nologo", "-nologo", "--no-restore", "-tl", "--tl", "--interactive", "--blame"]);
const DroppedFlagPrefixes = ["--blame-"];
const DroppedValueOptions = new Set([
  "-l",
  "--logger",
  "--results-directory",
  "-v",
  "--verbosity",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "-s",
  "--settings",
  "--collect",
  "-d",
  "--diag",
  "-e",
  "--environment",
  "--test-adapter-path",
]);
// MSBuild switches written with a value (-m:4, -nodeReuse:false, -v:q), by name
const DroppedInline = new Set([
  "m",
  "maxcpucount",
  "nodereuse",
  "nr",
  "v",
  "verbosity",
  "l",
  "logger",
  "clp",
  "consoleloggerparameters",
  "tl",
  "nologo",
  "results-directory",
]);
// What changes the work, with its value as the next word, under one name
const KeptValueOptions = new Map([
  ["-c", "-c"],
  ["--configuration", "-c"],
  ["-f", "-f"],
  ["--framework", "-f"],
  ["-r", "-r"],
  ["--runtime", "-r"],
  ["--filter", "--filter"],
]);

type Where = "" | ":outside" | ":ignored";

/** A path in forward slashes, without a leading `./`. */
function slashes(text: string): string {
  const unified = text.split("\\").join("/");
  return unified.startsWith("./") ? unified.slice(2) : unified;
}

/** The name of a dotnet project or source file for the shape: its file name without the extension. */
function nameOf(argument: string): string {
  const parts = slashes(argument).split("/");
  return (parts[parts.length - 1] as string).replace(/\.(csproj|slnx?|cs)$/, "");
}

/** One dotnet step: the verb, the projects, the options that change the work. */
export function dotnetKeys(words: string[], context: StepContext): StepKeys {
  const verb = words[1] ?? "";
  const projects: string[] = [];
  let where: Where = "";
  const options: string[] = [];
  let extra: string[] = [];
  let noBuild = false;
  let filtered = false;
  for (let i = 2; i < words.length; i++) {
    const word = words[i] as string;
    const lower = word.toLowerCase();
    const inline = /^(?:--?|\/)([\w-]+)[:=](.*)$/.exec(word);
    if (word === "--") {
      extra = words.slice(i + 1);
      break;
    }
    if (inline !== null) {
      const name = (inline[1] as string).toLowerCase();
      if (name === "filter") {
        filtered = true;
        options.push(`--filter ${inline[2] ?? ""}`);
      } else if (!DroppedInline.has(name)) {
        options.push(word);
      }
    } else if (word.startsWith("-")) {
      const kept = KeptValueOptions.get(lower);
      if (kept !== undefined) {
        filtered = filtered || kept === "--filter";
        options.push(`${kept} ${words[++i] ?? ""}`);
      } else if (DroppedValueOptions.has(lower)) {
        i++;
      } else if (!DroppedFlags.has(lower) && !DroppedFlagPrefixes.some((prefix) => lower.startsWith(prefix))) {
        noBuild = noBuild || lower === "--no-build";
        options.push(word);
      }
    } else {
      const project = context.resolvePath(word);
      projects.push(project.text);
      where = where === "" ? project.where : where;
    }
  }
  const named = [...projects.map(nameOf)].sort().join("+");
  const work = ["dotnet", verb, ...projects, ...options.sort(), ...(extra.length > 0 ? ["--", ...extra] : [])];
  if (verb !== "build" && verb !== "test") {
    return { normalized: work.join(" "), shape: `dotnet:${verb}`, kind: `dotnet${where}` };
  }
  const flags = `${noBuild ? ":nb" : ""}${verb === "test" && filtered ? ":f" : ""}`;
  return { normalized: work.join(" "), shape: `${verb}:${named}${flags}`, kind: `${verb}${where}` };
}
