// How a queued command runs: the shell that runs it, the quoting of words already split by the caller's shell, the
// default weight of a command and the MSBuild node limit added to its dotnet invocations.

import * as fs from "node:fs";
import * as path from "node:path";
import { spawn, type ChildProcess } from "node:child_process";
import { ShellVariable, isShellName, type ShellName } from "./constants.ts";
import { prefix } from "./system.ts";

// just options and variable assignments may stand between just and the recipe: just -f justfile test, just a=b test
const TestCommandPattern = new RegExp(
  [
    String.raw`\bdotnet(\.exe)?"?\s+(test|vstest|stryker)\b`,
    String.raw`\bdotnet-stryker\b`,
    String.raw`\bjust\b[^;&|\n]*?\s(test[\w-]*|coverage-diff|mutate|long-checks)(\s|$)`,
    String.raw`\b(test-stability|test-memory|test-sanitized|long-checks|mutate|coverage-diff)\.js\b`,
  ].join("|"),
);

/** 2 slots for a test run (dotnet test, just test*, the long test scripts), 1 for anything else. */
export function defaultWeight(command: string): number {
  return TestCommandPattern.test(command) ? 2 : 1;
}

const NodeLimitPattern = /(^|\s)(--?|\/)(m|maxcpucount|max-cpu-count)([:=\s]|$)/i;

/**
 * Adds -m:<cpus> after each dotnet build / test / msbuild / publish / pack of the command whose own arguments (up to
 * the
 * next &&, ||, ;, | or line break) set no -m. MSBuild ignores the processor count variables and the affinity of its
 * process; only the command line limits its nodes.
 */
export function limitMsbuildNodes(command: string, cpus: number): string {
  const invocation = /((?:"[^"]*[\\/])?\bdotnet(?:\.exe)?"?)(\s+)(build|test|msbuild|publish|pack)(?![-\w.])/gi;
  return command.replace(invocation, (match: string, program: string, space: string, verb: string, offset: number) => {
    const rest = invocationArguments(command, offset + match.length);
    // dotnet test <assembly>.dll runs VSTest without MSBuild
    const vstest = verb.toLowerCase() === "test" && /(^|\s)"?[^\s"]+\.(dll|exe)"?(\s|$)/i.test(rest);
    return NodeLimitPattern.test(rest) || vstest ? match : `${program}${space}${verb} -m:${cpus}`;
  });
}

/** The arguments of an invocation that starts at `start`: up to the next &&, ||, ;, | or line break outside quotes. */
function invocationArguments(command: string, start: number): string {
  let quote = "";
  for (let i = start; i < command.length; i++) {
    const ch = command[i];
    if (quote !== "") {
      quote = ch === quote ? "" : quote;
    } else if (ch === '"' || ch === "'") {
      quote = ch;
    } else if (ch === ";" || ch === "|" || ch === "\n" || ch === "\r" || (ch === "&" && command[i + 1] === "&")) {
      return command.slice(start, i);
    }
  }
  return command.slice(start);
}

/**
 * The shell that runs the command: --shell, else the one an outer run chose (GSHARP_JOB_QUEUE_SHELL), else the
 * caller's: Git Bash on Windows when MSYSTEM or SHELL is set (the Claude Code Bash tool), PowerShell otherwise on
 * Windows (the Claude Code PowerShell tool, Antigravity), /bin/sh elsewhere.
 */
export function resolveShell(requested: ShellName | ""): ShellName {
  if (requested !== "") {
    return requested;
  }
  const inherited = process.env[ShellVariable] ?? "";
  if (isShellName(inherited)) {
    return inherited;
  }
  if (process.platform !== "win32") {
    return "sh";
  }
  return process.env.MSYSTEM || process.env.SHELL ? "bash" : "pwsh";
}

/**
 * Windows: the bash.exe of Git for Windows. A bare `bash` on the PATH may be the WSL launcher (System32\bash.exe),
 * which would run the command in Linux.
 */
export function gitBashPath(): string {
  if (process.platform !== "win32") {
    return "bash";
  }
  const programs = process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, "Programs") : undefined;
  for (const root of [process.env.ProgramW6432, process.env.ProgramFiles, programs]) {
    if (root) {
      const candidate = path.join(root, "Git", "bin", "bash.exe");
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }
  return "bash";
}

interface Invocation {
  file: string;
  args: string[];
  verbatim: boolean;
}

/** The program and arguments that run `command` in `shell`, keeping the command's exit code. */
function shellInvocation(shell: ShellName, command: string): Invocation {
  switch (shell) {
    case "bash":
      return { file: gitBashPath(), args: ["-c", command], verbatim: false };
    case "sh":
      return { file: "/bin/sh", args: ["-c", command], verbatim: false };
    case "cmd":
      return { file: process.env.ComSpec || "cmd.exe", args: ["/d", "/s", "/c", `"${command}"`], verbatim: true };
    case "pwsh":
    case "powershell": {
      // -Command exits with 0 or 1 only; pass the exit code of the last native program on.
      const script =
        `${command}\n$gsharpOk = $?; ` + "if ($LASTEXITCODE) { exit $LASTEXITCODE }; if (-not $gsharpOk) { exit 1 }";
      return { file: shell, args: ["-NoProfile", "-NonInteractive", "-Command", script], verbatim: false };
    }
  }
}

export interface ShellResult {
  code: number;
  /** A signal ended the command (Unix; on Windows `taskkill /F` gives exit code 1, which looks like a failure). */
  signaled: boolean;
}

/** Runs the command in the shell with the given environment; `onChild` receives the shell process once it started. */
export function runShell(
  command: string,
  shell: ShellName,
  env: NodeJS.ProcessEnv,
  onChild: (child: ChildProcess) => void,
): Promise<ShellResult> {
  return new Promise((resolve) => {
    const start = (name: ShellName): void => {
      const { file, args, verbatim } = shellInvocation(name, command);
      const child = spawn(file, args, {
        stdio: "inherit",
        env: { ...env, [ShellVariable]: shell },
        windowsVerbatimArguments: verbatim,
        // Unix: the command leads its own process group, so the scheduler can watch and stop it after this client
        // dies.
        detached: process.platform !== "win32",
      });
      let replaced = false;
      child.on("error", (error: NodeJS.ErrnoException) => {
        if (error.code === "ENOENT" && name === "pwsh") {
          // Windows PowerShell when PowerShell 7 is not installed. The failed child still emits 'close'; that close
          // must not end the run while the replacement runs.
          replaced = true;
          start("powershell");
          return;
        }
        prefix(`could not start the command with ${file}: ${error.message}`);
        resolve({ code: 1, signaled: false });
      });
      if (child.pid !== undefined) {
        onChild(child);
      }
      child.on("close", (code, signal) => {
        if (!replaced) {
          // A shell reports a command that signal n killed as 128 + n (Unix)
          const bySignal =
            signal !== null || (process.platform !== "win32" && code !== null && code > 128 && code < 160);
          resolve({ code: code === null ? 1 : code, signaled: bySignal });
        }
      });
    };
    start(shell);
  });
}

/** Quotes one argument for a shell, so that the shell passes it on as one unchanged word. */
export function quoteArgument(shell: ShellName, word: string): string {
  const powerShell = shell === "pwsh" || shell === "powershell";
  // PowerShell splits -Name.Part=1 at the dot and a word with a leading or trailing comma into an array; bash and sh
  // read \ as an escape outside quotes (C:\dir\file). cmd needs no quotes for \ in a path.
  const needsQuotes =
    (powerShell && ((/^-/.test(word) && word.includes(".")) || /^,|,$/.test(word))) ||
    ((shell === "bash" || shell === "sh") && word.includes("\\"));
  if (/^[\w+=:,./\\-]+$/.test(word) && !needsQuotes) {
    return word;
  }
  if (shell === "cmd") {
    return `"${word.replace(/"/g, '""')}"`;
  }
  if (powerShell) {
    return `'${word.replace(/'/g, "''")}'`;
  }
  return `'${word.replace(/'/g, "'\\''")}'`;
}

/**
 * Joins words that the caller's shell already split into one command for `shell`, each quoted to reach the program
 * unchanged. PowerShell runs a quoted first word only through the call operator.
 */
export function joinWords(shell: ShellName, words: readonly string[]): string {
  const quoted = words.map((word) => quoteArgument(shell, word));
  const powerShell = shell === "pwsh" || shell === "powershell";
  return powerShell && quoted[0] !== words[0] ? `& ${quoted.join(" ")}` : quoted.join(" ");
}
