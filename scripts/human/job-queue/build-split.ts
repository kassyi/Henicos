// The build step of run --gpu (planBuildStep): a command whose dotnet test builds first would hold the GPU turn for
// the whole build, while the GPU idles. The plan moves the builds out: the client runs them as a job of their own,
// without the GPU, and then the tests with --no-build under the GPU turn.
//
//   dotnet build A && dotnet test P -c Release --filter X
//   -> build: dotnet build A && dotnet build P -c Release
//   -> tests: dotnet test --no-build P -c Release --filter X
//
// Only a plain command is planned: segments joined by && only, each a bare dotnet build or dotnet test, every build
// before the first test, with no shell syntax outside quotes that could change where a segment or a word ends
// (substitutions, redirections, other separators, escapes, a line break), and every option of a test known to be
// either an option the build needs too (passed on: configuration, framework, runtime, properties, the MSBuild switches)
// or one only the test run reads (left out of the build). Anything else gives no plan, and the command runs as before:
// a test that names no project, a test of an assembly (.dll), an unknown option, a build after a test.

import type { ShellName } from "./constants.ts";

export interface BuildStep {
  /** The builds, joined by &&: the explicit builds before the first test, then one build per test that builds. */
  build: string;
  /** The command with --no-build added to each dotnet test that built before. */
  tests: string;
}

// Options of dotnet test that take the next word as their value
const BuildValueOptions = new Set([
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
]);
const TestValueOptions = new Set([
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
]);
// Switches without a value
const BuildFlags = new Set([
  "--no-restore",
  "--nologo",
  "-nologo",
  "--disable-build-servers",
  "--interactive",
  "--tl",
  "-tl",
]);
const TestFlags = new Set(["--blame", "--blame-crash", "--blame-hang", "--list-tests", "-t", "--no-build"]);
// MSBuild switches written with their value (-p:X=1, /p:X=1, -m:4, -nodeReuse:false): the build needs them too
const BuildInlineSwitches = new Set([
  "p",
  "property",
  "m",
  "maxcpucount",
  "nodereuse",
  "c",
  "configuration",
  "f",
  "framework",
  "r",
  "runtime",
  "a",
  "arch",
  "os",
  "o",
  "output",
  "v",
  "verbosity",
  "artifacts-path",
  "tl",
]);

/** The words of one segment as written (quotes kept), or null when it holds syntax a plain command does not. */
function rawWords(segment: string, shell: ShellName): string[] | null {
  const powerShell = shell === "pwsh" || shell === "powershell";
  // Outside quotes: separators, redirections, groups, substitutions, escapes and comments end the plain form
  const special = shell === "cmd" ? /[&|<>()^\n\r]/ : powerShell ? /[&|<>(){};$@`#\n\r]/ : /[&|<>(){};$`\\#*?[\n\r]/;
  const list: string[] = [];
  let word = "";
  let quote = "";
  for (let i = 0; i < segment.length; i++) {
    const ch = segment.charAt(i);
    if (quote !== "") {
      word += ch;
      if (ch === "\n" || ch === "\r" || (quote === '"' && (ch === "`" || ch === "\\") && shell !== "cmd")) {
        return null; // an escape inside double quotes: keep to the plain form
      }
      if (ch === quote) {
        quote = "";
      }
    } else if (ch === '"' || (ch === "'" && shell !== "cmd")) {
      quote = ch;
      word += ch;
    } else if (ch === " " || ch === "\t") {
      if (word !== "") {
        list.push(word);
        word = "";
      }
    } else if (special.test(ch)) {
      return null;
    } else {
      word += ch;
    }
  }
  if (quote !== "") {
    return null;
  }
  if (word !== "") {
    list.push(word);
  }
  return list;
}

/** The segments of the command at && outside quotes (rawWords then refuses any other syntax in them). */
function segmentsOf(command: string, shell: ShellName): string[] {
  const segments: string[] = [];
  let quote = "";
  let start = 0;
  for (let i = 0; i < command.length; i++) {
    const ch = command.charAt(i);
    if (quote !== "") {
      quote = ch === quote ? "" : quote;
    } else if (ch === '"' || (ch === "'" && shell !== "cmd")) {
      quote = ch;
    } else if (ch === "&" && command[i + 1] === "&") {
      segments.push(command.slice(start, i));
      start = i + 2;
      i++;
    }
  }
  segments.push(command.slice(start));
  return segments.map((segment) => segment.trim());
}

/** A word without its quotes, for reading an option name or a file extension. */
function bare(word: string): string {
  return word.replace(/["']/g, "");
}

/** The build a dotnet test needs: its project and the options the build reads too; "keep" or null (no plan). */
function testBuild(words: string[]): { build: string } | "keep" | null {
  const args = words.slice(2);
  const build: string[] = [];
  let project = "";
  let noBuild = false;
  for (let i = 0; i < args.length; i++) {
    const word = args[i] ?? "";
    const name = bare(word).toLowerCase();
    if (name === "--") {
      break; // RunSettings for the test run
    }
    if (!/^[-/]/.test(name) || (name.startsWith("/") && !/^\/\w+:/.test(name))) {
      if (project !== "") {
        return null; // two positional words: not a form this plan reads
      }
      project = word;
      continue;
    }
    const inline = /^(?:--?|\/)([\w-]+)[:=]/.exec(name);
    if (inline !== null) {
      const switchName = inline[1] ?? "";
      if (BuildInlineSwitches.has(switchName)) {
        build.push(word);
      } else if (!TestValueOptions.has(`--${switchName}`) && !TestValueOptions.has(`-${switchName}`)) {
        return null;
      }
      continue;
    }
    if (name === "--no-build") {
      noBuild = true;
    } else if (BuildFlags.has(name)) {
      build.push(word);
    } else if (BuildValueOptions.has(name) && i + 1 < args.length) {
      build.push(word, args[++i] ?? "");
    } else if (TestValueOptions.has(name) && i + 1 < args.length) {
      i++;
    } else if (!TestFlags.has(name)) {
      return null; // an option this plan does not know: it cannot tell whether a value follows
    }
  }
  if (noBuild) {
    return "keep";
  }
  if (project === "" || /\.(dll|exe)$/i.test(bare(project))) {
    return null; // no project to build (the one in the directory is left to the test), or a test of an assembly
  }
  return { build: [words[0], "build", project, ...build].join(" ") };
}

/**
 * The build step of a run --gpu command in `shell`: { build, tests } when it builds before its tests (an explicit
 * dotnet build, or a dotnet test without --no-build), else null (the command runs as it is).
 */
export function planBuildStep(command: string, shell: ShellName): BuildStep | null {
  if (/--%/.test(command)) {
    return null;
  }
  const builds: string[] = [];
  const tests: string[] = [];
  for (const segment of segmentsOf(command.trim(), shell)) {
    const words = rawWords(segment, shell);
    if (words === null || words.length < 2 || !/^dotnet(\.exe)?$/i.test(words[0] ?? "")) {
      return null;
    }
    if (words[1] === "build" && tests.length === 0) {
      builds.push(segment);
      continue;
    }
    if (words[1] !== "test") {
      return null;
    }
    const needed = testBuild(words);
    if (needed === null) {
      return null;
    }
    if (needed === "keep") {
      tests.push(segment);
      continue;
    }
    if (!builds.includes(needed.build)) {
      builds.push(needed.build);
    }
    tests.push([words[0], "test", "--no-build", ...words.slice(2)].join(" "));
  }
  if (tests.length === 0 || builds.length === 0) {
    return null; // no test, or nothing builds (every test runs with --no-build and no build comes before them)
  }
  return { build: builds.join(" && "), tests: tests.join(" && ") };
}
