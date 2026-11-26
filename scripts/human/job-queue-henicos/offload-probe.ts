// Henicos remote CPU offload target resolution and async `--list-tests` `&Platform=Windows` probe.
// Supersedes regex filter matching with structural `.csproj`/solution parsing and fail-closed VSTest probing.

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import * as fs from "node:fs";
import * as path from "node:path";
import { EntryScript } from "../job-queue/constants.ts";
import {
  errorMessage,
  isTrivialStep,
  programOf,
  readCommandLine,
  type ClientContext,
  type OffloadAnswer,
  type OffloadCheckRequest,
  type ShellName,
} from "../job-queue/plugin-api.ts";
import type { TargetDialectModule } from "../job-queue/lib/types.ts";
import { ProcessTable } from "../job-queue/system.ts";
import {
  checkProjectHeadlessCpuSafety,
  findTestProjectFile,
  implicitProjectOfStep,
  justPositionals,
  stepExcludesGpuExecution,
} from "./budgets.ts";
import { assembliesOf, ListTimeoutMs, type ListResult } from "./name-filter.ts";

const require = createRequire(import.meta.url);
const { dialectOf, remoteOffloadOf } = require("./target-dialect.js") as TargetDialectModule;

/** Runs a `--list-tests` command argv through `job-queue.js run --local`; replaceable in self-tests. */
export type OffloadListRunner = (listArgs: readonly string[], context: ClientContext) => Promise<ListResult>;

const DotnetStepValueOptions = new Set([
  "-c",
  "--configuration",
  "-f",
  "--framework",
  "-r",
  "--runtime",
  "-a",
  "--arch",
  "--os",
  "-o",
  "--output",
  "-v",
  "--verbosity",
  "--artifacts-path",
  "-p",
  "--property",
  "--filter",
  "-l",
  "--logger",
  "-s",
  "--settings",
  "--results-directory",
  "--collect",
  "-d",
  "--diag",
  "--blame-crash-dump-type",
  "--blame-hang-dump-type",
  "--blame-hang-timeout",
  "-e",
  "--environment",
  "--test-adapter-path",
  "--project",
  "--source",
  "--configfile",
]);

export interface DotnetBuildOrTestStep {
  verb: "build" | "test";
  words: string[];
  stepCwd: string;
  target: string;
  resolvedTargetPath: string;
  config: string;
  framework: string;
  runtime: string;
  output: string;
  artifactsPath: string;
  noBuild: boolean;
  listTests: boolean;
  filters: string[];
  unreadableFilter: boolean;
  forwardedBuildOpts: string[];
}

function resolveStepPath(rawTarget: string, stepCwd: string): string {
  const mapped = /^\/(?:mnt\/)?([A-Za-z])\/(.*)$/.exec(rawTarget);
  const adjusted = mapped !== null && process.platform === "win32" ? `${mapped[1]}:/${mapped[2]}` : rawTarget;
  return adjusted === "" ? path.resolve(stepCwd) : path.resolve(stepCwd, adjusted);
}

function normalizeTargetPath(rawTarget: string, stepCwd: string): string {
  const resolved = resolveStepPath(rawTarget, stepCwd);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function dotnetTargetsMatch(buildTarget: string, testTarget: string): boolean {
  if (buildTarget === testTarget) {
    return true;
  }
  const isProj = (p: string): boolean => /\.(?:csproj|fsproj|vbproj)$/i.test(p);
  if (isProj(testTarget) && path.dirname(testTarget) === buildTarget) {
    return true;
  }
  return isProj(buildTarget) && path.dirname(buildTarget) === testTarget;
}

interface MutableDotnetStep {
  verb: "build" | "test";
  config: string | undefined;
  framework: string;
  runtime: string;
  output: string;
  artifactsPath: string;
  projectOpt: string | undefined;
  noBuild: boolean;
  listTests: boolean;
  filters: string[];
  unreadableFilter: boolean;
  forwardedBuildOpts: string[];
  positional: string[];
}

function setStepConfig(state: MutableDotnetStep, value: string): boolean {
  const next = value.toLowerCase();
  if (next === "" || (state.config !== undefined && state.config !== next)) {
    return false;
  }
  state.config = next;
  return true;
}

function applyInlineDotnetOpt(state: MutableDotnetStep, word: string, stepCwd: string): "matched" | "invalid" | "none" {
  const lower = word.toLowerCase();
  if (lower.includes("testcasefilter")) {
    state.unreadableFilter = true;
  }
  const inlineFilter = /^--filter=(.*)$/i.exec(word);
  if (inlineFilter !== null) {
    const val = inlineFilter[1] ?? "";
    if (val.trim() === "") {
      state.unreadableFilter = true;
    } else {
      state.filters.push(val);
    }
    return "matched";
  }
  if (/^--filter:/i.test(word)) {
    state.unreadableFilter = true;
    return "matched";
  }
  const inlineCfg = /^(?:-c|--configuration)[:=](.+)$/i.exec(word);
  if (inlineCfg !== null) {
    const val = inlineCfg[1] ?? "";
    if (!setStepConfig(state, val)) {
      return "invalid";
    }
    state.forwardedBuildOpts.push("-c", val);
    return "matched";
  }
  const propCfg = /^(?:--|[-/])(?:p|property):configuration=(.+)$/i.exec(word);
  if (propCfg !== null) {
    const val = propCfg[1] ?? "";
    if (!setStepConfig(state, val)) {
      return "invalid";
    }
    state.forwardedBuildOpts.push(word);
    return "matched";
  }
  if (/^(?:--|[-/])(?:p|property):/i.test(word)) {
    state.forwardedBuildOpts.push(word);
    return "matched";
  }
  const inlineProj = /^--project[:=](.+)$/i.exec(word);
  if (inlineProj !== null) {
    if (state.projectOpt !== undefined) {
      return "invalid";
    }
    state.projectOpt = inlineProj[1] ?? "";
    return "matched";
  }
  const inlineFw = /^(?:-f|--framework)[:=](.+)$/i.exec(word);
  if (inlineFw !== null) {
    const val = inlineFw[1] ?? "";
    state.framework = val.toLowerCase();
    state.forwardedBuildOpts.push("-f", val);
    return "matched";
  }
  const inlineRt = /^(?:-r|--runtime)[:=](.+)$/i.exec(word);
  if (inlineRt !== null) {
    const val = inlineRt[1] ?? "";
    state.runtime = val.toLowerCase();
    state.forwardedBuildOpts.push("-r", val);
    return "matched";
  }
  const inlineOut = /^(?:-o|--output)[:=](.+)$/i.exec(word);
  if (inlineOut !== null) {
    const resolved = resolveStepPath(inlineOut[1] ?? "", stepCwd);
    state.output = process.platform === "win32" ? resolved.toLowerCase() : resolved;
    state.forwardedBuildOpts.push("-o", resolved);
    return "matched";
  }
  const inlineArt = /^--artifacts-path[:=](.+)$/i.exec(word);
  if (inlineArt !== null) {
    const resolved = resolveStepPath(inlineArt[1] ?? "", stepCwd);
    state.artifactsPath = process.platform === "win32" ? resolved.toLowerCase() : resolved;
    state.forwardedBuildOpts.push("--artifacts-path", resolved);
    return "matched";
  }
  return /^(?:--?|\/)[\w-]+[:=]/.test(word) ? "matched" : "none";
}

function applySpaceDotnetOpt(state: MutableDotnetStep, lower: string, val: string, stepCwd: string): boolean {
  if (val.toLowerCase().includes("testcasefilter")) {
    state.unreadableFilter = true;
  }
  if (lower === "--filter") {
    if (val.trim() === "") {
      state.unreadableFilter = true;
    } else {
      state.filters.push(val);
    }
    return true;
  }
  if (lower === "-c" || lower === "--configuration") {
    if (!setStepConfig(state, val)) {
      return false;
    }
    state.forwardedBuildOpts.push("-c", val);
    return true;
  }
  if (lower === "-f" || lower === "--framework") {
    state.framework = val.toLowerCase();
    state.forwardedBuildOpts.push("-f", val);
  } else if (lower === "-r" || lower === "--runtime") {
    state.runtime = val.toLowerCase();
    state.forwardedBuildOpts.push("-r", val);
  } else if (lower === "-o" || lower === "--output") {
    const resolved = resolveStepPath(val, stepCwd);
    state.output = process.platform === "win32" ? resolved.toLowerCase() : resolved;
    state.forwardedBuildOpts.push("-o", resolved);
  } else if (lower === "--artifacts-path") {
    const resolved = resolveStepPath(val, stepCwd);
    state.artifactsPath = process.platform === "win32" ? resolved.toLowerCase() : resolved;
    state.forwardedBuildOpts.push("--artifacts-path", resolved);
  } else if (lower === "--project") {
    if (state.projectOpt !== undefined) {
      return false;
    }
    state.projectOpt = val;
  } else if (lower === "-p" || lower === "--property") {
    const m = /^configuration=(.+)$/i.exec(val);
    if (m !== null && !setStepConfig(state, m[1] ?? "")) {
      return false;
    }
    state.forwardedBuildOpts.push("-p", val);
  }
  return true;
}

export function parseDotnetBuildOrTestStep(words: readonly string[], stepCwd: string): DotnetBuildOrTestStep | null {
  const verb = (words[1] ?? "").toLowerCase();
  if (verb !== "build" && verb !== "test") {
    return null;
  }
  const state: MutableDotnetStep = {
    verb,
    config: undefined,
    framework: "",
    runtime: "",
    output: "",
    artifactsPath: "",
    projectOpt: undefined,
    noBuild: false,
    listTests: false,
    filters: [],
    unreadableFilter: false,
    forwardedBuildOpts: [],
    positional: [],
  };
  let afterDoubleDash = false;
  for (let i = 2; i < words.length; i++) {
    const word = words[i] ?? "";
    const lower = word.toLowerCase();
    if (afterDoubleDash) {
      if (lower.includes("testcasefilter")) {
        state.unreadableFilter = true;
      }
      continue;
    }
    if (word === "--") {
      afterDoubleDash = true;
      continue;
    }
    if (lower.includes("testcasefilter")) {
      state.unreadableFilter = true;
    }
    if (lower === "--no-build") {
      state.noBuild = true;
      continue;
    }
    if (lower === "--list-tests" || lower === "-t") {
      state.listTests = true;
      continue;
    }
    const inlineStatus = applyInlineDotnetOpt(state, word, stepCwd);
    if (inlineStatus === "invalid") {
      return null;
    }
    if (inlineStatus === "matched") {
      continue;
    }
    if (DotnetStepValueOptions.has(lower)) {
      const val = words[++i];
      if (val === undefined || !applySpaceDotnetOpt(state, lower, val, stepCwd)) {
        return null;
      }
      continue;
    }
    if (!word.startsWith("-") && !/^\/[A-Za-z]/.test(word)) {
      state.positional.push(word);
    }
  }
  if (state.positional.length > 1 || (state.projectOpt !== undefined && state.positional.length > 0)) {
    return null;
  }
  const rawTarget = state.projectOpt ?? state.positional[0] ?? "";
  return {
    verb: state.verb,
    words: [...words],
    stepCwd,
    target: normalizeTargetPath(rawTarget, stepCwd),
    resolvedTargetPath: resolveStepPath(rawTarget, stepCwd),
    config: state.config ?? "debug",
    framework: state.framework,
    runtime: state.runtime,
    output: state.output,
    artifactsPath: state.artifactsPath,
    noBuild: state.noBuild,
    listTests: state.listTests,
    filters: state.filters,
    unreadableFilter: state.unreadableFilter,
    forwardedBuildOpts: state.forwardedBuildOpts,
  };
}

function buildMatchesTest(build: DotnetBuildOrTestStep, test: DotnetBuildOrTestStep): boolean {
  return (
    dotnetTargetsMatch(build.target, test.target) &&
    build.config === test.config &&
    build.framework === test.framework &&
    build.runtime === test.runtime &&
    build.output === test.output &&
    build.artifactsPath === test.artifactsPath
  );
}

export type ResolvedDotnetTarget =
  | { kind: "project"; name: string; projectFile: string }
  | { kind: "solution"; label: string }
  | { kind: "assembly"; label: string }
  | { kind: "ambiguous"; label: string }
  | { kind: "missing"; label: string };

/** Resolves a `dotnet test` target path without building: `.csproj`, directory, `.sln`/`.slnx`/`.slnf`, or `.dll`/`.exe`. */
export function resolveDotnetTarget(resolvedTargetPath: string): ResolvedDotnetTarget {
  const base = path.basename(resolvedTargetPath) || resolvedTargetPath;
  if (/\.(?:sln|slnx|slnf)$/i.test(resolvedTargetPath)) {
    return { kind: "solution", label: base };
  }
  if (/\.(?:dll|exe)$/i.test(resolvedTargetPath)) {
    return { kind: "assembly", label: base };
  }
  if (/\.csproj$/i.test(resolvedTargetPath)) {
    if (!fs.existsSync(resolvedTargetPath)) {
      return { kind: "missing", label: base };
    }
    return { kind: "project", name: path.basename(resolvedTargetPath, ".csproj"), projectFile: resolvedTargetPath };
  }
  let entries: string[];
  try {
    entries = fs.readdirSync(resolvedTargetPath);
  } catch {
    return { kind: "missing", label: base };
  }
  const sln = entries.find((name) => /\.(?:sln|slnx|slnf)$/i.test(name));
  if (sln !== undefined) {
    return { kind: "solution", label: sln };
  }
  const csprojs = entries.filter((name) => /\.csproj$/i.test(name));
  if (csprojs.length === 1) {
    const csproj = csprojs[0] as string;
    return {
      kind: "project",
      name: path.basename(csproj, ".csproj"),
      projectFile: path.join(resolvedTargetPath, csproj),
    };
  }
  if (csprojs.length > 1) {
    return { kind: "ambiguous", label: base };
  }
  return { kind: "missing", label: base };
}

type ParsedOffloadCommand =
  | { kind: "just-sanitized"; args: string[] }
  | { kind: "just-other"; recipe: string | undefined; words: string[] }
  | { kind: "dotnet"; steps: DotnetBuildOrTestStep[] }
  | { kind: "ineligible" };

function parseOffloadCommand(command: string, cwd: string): ParsedOffloadCommand {
  const line = readCommandLine(command);
  if (line === null || line.piped) {
    return { kind: "ineligible" };
  }
  const cdCmds = new Set(["cd", "chdir", "set-location", "sl"]);
  let currentCwd = path.resolve(cwd);
  const workSteps: { words: string[]; cwd: string }[] = [];
  for (const step of line.steps) {
    const prog = programOf(step.words[0] ?? "");
    if (cdCmds.has(prog)) {
      if (workSteps.length > 0) {
        return { kind: "ineligible" };
      }
      const target = step.words.slice(1).find((w) => !w.startsWith("-"));
      if (target === undefined) {
        return { kind: "ineligible" };
      }
      currentCwd = resolveStepPath(target, currentCwd);
      continue;
    }
    if (!isTrivialStep(step)) {
      workSteps.push({ words: step.words, cwd: currentCwd });
    }
  }
  if (workSteps.length === 0) {
    return { kind: "ineligible" };
  }
  if (workSteps.length === 1 && programOf(workSteps[0]?.words[0] ?? "") === "just") {
    const words = workSteps[0]?.words ?? [];
    const positional = justPositionals(words);
    const recipe = positional[0];
    if (recipe === "test-sanitized") {
      return { kind: "just-sanitized", args: positional.slice(1) };
    }
    return { kind: "just-other", recipe, words };
  }
  const parsedSteps: DotnetBuildOrTestStep[] = [];
  const builtSteps: DotnetBuildOrTestStep[] = [];
  let sawTest = false;
  for (const step of workSteps) {
    if (programOf(step.words[0] ?? "") !== "dotnet") {
      return { kind: "ineligible" };
    }
    const parsed = parseDotnetBuildOrTestStep(step.words, step.cwd);
    if (parsed === null) {
      return { kind: "ineligible" };
    }
    if (parsed.verb === "build") {
      builtSteps.push(parsed);
    } else {
      if (parsed.listTests) {
        return { kind: "ineligible" };
      }
      if (parsed.noBuild && !builtSteps.some((b) => buildMatchesTest(b, parsed))) {
        return { kind: "ineligible" };
      }
      sawTest = true;
    }
    parsedSteps.push(parsed);
  }
  if (!sawTest) {
    return { kind: "ineligible" };
  }
  return { kind: "dotnet", steps: parsedSteps };
}

function checkBuildStepOffload(step: DotnetBuildOrTestStep): { offloadable: boolean; localReason?: string } {
  const target = resolveDotnetTarget(step.resolvedTargetPath);
  if (target.kind === "solution") {
    return {
      offloadable: false,
      localReason: `whole-solution build (${target.label}) runs locally`,
    };
  }
  if (target.kind === "assembly") {
    return {
      offloadable: false,
      localReason: `direct assembly build (${target.label}) runs locally`,
    };
  }
  if (target.kind === "ambiguous") {
    return {
      offloadable: false,
      localReason: `ambiguous build directory (${target.label}) runs locally`,
    };
  }
  if (target.kind === "missing") {
    return { offloadable: false };
  }
  if (remoteOffloadOf(target.projectFile) === "local") {
    return {
      offloadable: false,
      localReason: `project ${target.name} is marked local (GSharpRemoteOffload=local)`,
    };
  }
  if (target.name === "Kassyi.GSharp.Tests.Integration") {
    return {
      offloadable: false,
      localReason: `project ${target.name} runs locally`,
    };
  }
  if (dialectOf(target.projectFile) === "cuda") {
    return {
      offloadable: false,
      localReason: `CUDA project build (${target.name}) runs locally`,
    };
  }
  return { offloadable: true };
}

/**
 * Synchronous offload eligibility classification for Henicos (`classify` hook).
 * Marks `<GSharpRemoteOffload>local</GSharpRemoteOffload>` projects and whole-solution `dotnet test` commands
 * `offloadable: false` with a 1-line `localReason`. Keeps `just test-sanitized` eligible for `checkOffload`
 * (Option A: `auto` runs locally on Windows; `--remote` opts into Linux ASan+UBSan).
 */
export function classifyOffloadCommand(
  command: string,
  testsDir: string,
  cwd: string,
  log: (text: string) => void,
): { offloadable: boolean; localReason?: string } {
  const parsed = parseOffloadCommand(command, cwd);
  if (parsed.kind === "just-sanitized") {
    return { offloadable: true };
  }
  if (parsed.kind === "just-other") {
    const projectName = implicitProjectOfStep(parsed.words);
    if (projectName !== null) {
      const projectFile = findTestProjectFile(projectName, testsDir, log);
      if (projectFile !== null && remoteOffloadOf(projectFile) === "local") {
        return {
          offloadable: false,
          localReason: `project ${projectName} is marked local (GSharpRemoteOffload=local)`,
        };
      }
    }
    return { offloadable: false };
  }
  if (parsed.kind !== "dotnet") {
    return { offloadable: false };
  }
  for (const step of parsed.steps) {
    if (step.verb === "build") {
      const buildSafety = checkBuildStepOffload(step);
      if (!buildSafety.offloadable) {
        return buildSafety;
      }
      continue;
    }
    const target = resolveDotnetTarget(step.resolvedTargetPath);
    if (target.kind === "solution") {
      return {
        offloadable: false,
        localReason: `whole-solution test run (${target.label}) runs locally`,
      };
    }
    if (target.kind === "assembly") {
      return {
        offloadable: false,
        localReason: `direct assembly test run (${target.label}) runs locally`,
      };
    }
    if (target.kind === "ambiguous") {
      return {
        offloadable: false,
        localReason: `ambiguous test directory (${target.label}) runs locally`,
      };
    }
    if (target.kind === "missing") {
      return { offloadable: false };
    }
    const safety = checkProjectHeadlessCpuSafety(target.projectFile, target.name, stepExcludesGpuExecution(step.words));
    if (!safety.safe) {
      return safety.localReason !== undefined
        ? { offloadable: false, localReason: safety.localReason }
        : { offloadable: false };
    }
  }
  return { offloadable: true };
}

/**
 * Combines a test step's `--filter` expression with `Platform=Windows`, parenthesizing the original expression so
 * `&` precedence never splits an `A|B` OR-clause. Returns null when the filter cannot be safely combined.
 */
export function combineFilterWithWindowsPlatform(filters: readonly string[], unreadableFilter: boolean): string | null {
  if (unreadableFilter || filters.length > 1) {
    return null;
  }
  if (filters.length === 0) {
    return "Platform=Windows";
  }
  const trimmed = (filters[0] ?? "").trim();
  if (trimmed === "") {
    return null;
  }
  return `(${trimmed})&Platform=Windows`;
}

/** Builds the `job-queue.js run --local` argv for `--list-tests` (`--local` prevents recursive `checkOffload`). */
export function buildOffloadListJobArgs(listArgs: readonly string[], shell: ShellName): string[] {
  return [EntryScript, "run", "--local", "--shell", shell, "--", ...listArgs];
}

/** Runs `dotnet test --no-build --list-tests` via `job-queue.js run --local --shell <shell> -- ...`. */
export const runOffloadListJob: OffloadListRunner = (listArgs, context) =>
  new Promise((resolve) => {
    const args = buildOffloadListJobArgs(listArgs, context.shell);
    const child = spawn(process.execPath, args, {
      cwd: context.cwd,
      env: { ...context.env, DOTNET_CLI_UI_LANGUAGE: "en" },
      stdio: ["ignore", "pipe", "ignore"],
      detached: process.platform !== "win32",
    });
    const chunks: Buffer[] = [];
    child.stdout.on("data", (chunk: Buffer) => chunks.push(chunk));
    const timer = setTimeout(() => ProcessTable.killTree(child.pid), ListTimeoutMs);
    const end = (exitCode: number | null): void => {
      clearTimeout(timer);
      resolve({ exitCode, output: Buffer.concat(chunks).toString("utf8") });
    };
    child.on("error", () => end(null));
    child.on("close", (code) => end(code));
  });

/**
 * Fail-closed verdict for `Platform=Windows` `--list-tests` probe output:
 * allows offload ONLY on exit 0 with `"No test matches the given testcase filter"`, no `"No test is available"`,
 * zero listed test lines, and at least one `Test run for <assembly>` line.
 */
export function offloadListVerdict(
  result: ListResult,
  cwd: string,
): { offload: "allow" } | { offload: "local"; reason: string } {
  const listed = result.output.split(/\r?\n/).filter((line) => /^ {4}\S/.test(line)).length;
  const noMatches = result.output.includes("No test matches the given testcase filter");
  const noAvailable = result.output.includes("No test is available");
  const assemblies = assembliesOf(result.output, cwd);
  if (result.exitCode === 0 && noMatches && !noAvailable && listed === 0 && assemblies.length > 0) {
    return { offload: "allow" };
  }
  if (listed > 0) {
    return {
      offload: "local",
      reason: `filter matches ${listed} Platform=Windows test${listed === 1 ? "" : "s"}`,
    };
  }
  if (noAvailable) {
    return { offload: "local", reason: "Platform=Windows check found no test available in assembly" };
  }
  if (result.exitCode === null) {
    return { offload: "local", reason: "Platform=Windows test list timed out or failed to start" };
  }
  if (result.exitCode !== 0) {
    return { offload: "local", reason: `Platform=Windows test list exited with code ${result.exitCode}` };
  }
  if (assemblies.length === 0) {
    return { offload: "local", reason: "Platform=Windows test list named no test assembly" };
  }
  return { offload: "local", reason: "Platform=Windows test list did not confirm zero matching tests" };
}

function hasCustomSanitizedArgs(args: readonly string[]): boolean {
  return args.some((arg) => {
    const lower = arg.toLowerCase();
    return lower === "--no-build" || lower.startsWith("--filter") || lower.includes("testcasefilter");
  });
}

/**
 * Async `checkOffload` implementation for Henicos:
 * - Resolves `just test-sanitized` (Option A): local on `auto`, allowed on `--remote` (unless custom `--filter` / `--no-build` is passed).
 * - For eligible `dotnet test` commands, probes `dotnet test <csproj> --no-build --list-tests --filter "(<expr>)&Platform=Windows"`
 *   via `job-queue.js run --local` and fails closed on any matching Windows test, unreadable filter, unbuilt assembly, or error.
 */
export async function checkHenicosOffload(
  command: string,
  request: OffloadCheckRequest,
  context: ClientContext,
  run: OffloadListRunner = runOffloadListJob,
): Promise<OffloadAnswer> {
  const baseCwd = context.cwd !== "" ? context.cwd : context.worktree;
  const parsed = parseOffloadCommand(command, baseCwd);
  if (parsed.kind === "just-sanitized") {
    if (!request.remoteForced) {
      return {
        offload: "local",
        reason:
          "just test-sanitized targets Kassyi.GSharp.Tests.Cpu (marked local); pass --remote for Linux ASan+UBSan",
      };
    }
    if (hasCustomSanitizedArgs(parsed.args)) {
      return {
        offload: "local",
        reason: "just test-sanitized with custom filter or --no-build runs locally",
      };
    }
    return { offload: "allow" };
  }
  if (parsed.kind !== "dotnet") {
    return { offload: "local", reason: "command is not eligible for Platform=Windows offload check" };
  }
  for (const step of parsed.steps) {
    if (step.verb === "build") {
      const buildSafety = checkBuildStepOffload(step);
      if (!buildSafety.offloadable) {
        return {
          offload: "local",
          reason: buildSafety.localReason ?? "build target runs locally",
        };
      }
    }
  }
  const testSteps = parsed.steps.filter((s) => s.verb === "test");
  if (testSteps.length === 0) {
    return { offload: "local", reason: "command runs no dotnet test step" };
  }
  for (const step of testSteps) {
    const target = resolveDotnetTarget(step.resolvedTargetPath);
    if (target.kind !== "project") {
      return { offload: "local", reason: `target (${target.label}) runs locally` };
    }
    if (remoteOffloadOf(target.projectFile) === "local") {
      return {
        offload: "local",
        reason: `project ${target.name} is marked local (GSharpRemoteOffload=local)`,
      };
    }
    const windowsFilter = combineFilterWithWindowsPlatform(step.filters, step.unreadableFilter);
    if (windowsFilter === null) {
      return { offload: "local", reason: "cannot safely combine --filter with Platform=Windows check" };
    }
    const relProject = path.relative(baseCwd, target.projectFile);
    const projectArg = relProject !== "" && !relProject.startsWith("..") ? relProject : target.projectFile;
    const listArgs = [
      "dotnet",
      "test",
      projectArg,
      "--no-build",
      "--list-tests",
      ...step.forwardedBuildOpts,
      "--filter",
      windowsFilter,
    ];
    let result: ListResult;
    try {
      result = await run(listArgs, context);
    } catch (error) {
      return {
        offload: "local",
        reason: `Platform=Windows test list failed: ${errorMessage(error)}`,
      };
    }
    const verdict = offloadListVerdict(result, baseCwd);
    if (verdict.offload === "local") {
      return verdict;
    }
  }
  return { offload: "allow" };
}
