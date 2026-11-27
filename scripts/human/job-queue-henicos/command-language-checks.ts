// Property-based and fixed negative checks for the allow-listed GPU-share command language (design §9.4).
// Generates random commands in the grammar, runs them through real shells (bash, pwsh, powershell, cmd, sh) with a
// stub `dotnet` (copy of the node executable + `test` script in cwd), and asserts exact argv, env, and redirect match.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import type { ShellName } from "../job-queue/constants.ts";
import type { AssertModule } from "../job-queue/plugin-api.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import { gitBashPath } from "../job-queue/shell.ts";
import { AllowedPrefixEnvNames, parseSharedCommand, type AllowedPrefixEnvName } from "./command-language.ts";

/** Default generated commands per shell during `job-queue.js --self-test`. */
export const DefaultLanguageFuzzCount = 50;
/** Env variable passed by `job-queue.js --self-test --language-fuzz <n>`. */
export const LanguageFuzzEnvVariable = "GSHARP_JOB_QUEUE_LANGUAGE_FUZZ";

interface StubRecord {
  args: string[];
  env: Record<AllowedPrefixEnvName, string | null>;
}

interface ShellSpec {
  shell: ShellName;
  available: boolean;
  file: string;
  makeArgs: (command: string) => string[];
  verbatim: boolean;
}

/** Deterministic PRNG (mulberry32) so failures are 100% reproducible from their seed. */
function makeRng(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function pick<T>(rng: () => number, items: readonly T[]): T {
  return items[Math.floor(rng() * items.length)] as T;
}

function randInt(rng: () => number, min: number, max: number): number {
  return min + Math.floor(rng() * (max - min + 1));
}

function randomFromAlphabet(rng: () => number, alphabet: string, minLen: number, maxLen: number): string {
  const n = randInt(rng, minLen, maxLen);
  let out = "";
  for (let i = 0; i < n; i++) {
    out += alphabet.charAt(Math.floor(rng() * alphabet.length));
  }
  return out;
}

// Boundary-heavy alphabets so shell metacharacters allowed inside each production are exercised frequently
const ValueAlpha = "aA0_-1zZ9_-";
const FilterAlpha = "aA0_.~=!&|() -&|()!~=bB1";
const LoggerAlpha = "aA0_.;=-.;=-cC2";
const ArgLeadAlpha = "aA0-bB1-";
const BashArgAlpha = "aA0_./:=-:=-/._";
const WinArgAlpha = "aA0_./\\:=-\\:=-/._";
const SafePathChars = "aA0_.-123";

function generateTestCommand(rng: () => number, shell: ShellName, caseIndex: number): string {
  const posix = shell === "bash" || shell === "sh";
  const pwsh = shell === "pwsh" || shell === "powershell";
  const parts: string[] = [];

  // 1. Optional prefixes (0..2)
  const prefixCount = shell === "cmd" ? 0 : randInt(rng, 0, 2);
  let prefixStr = "";
  for (let i = 0; i < prefixCount; i++) {
    const name = pick(rng, AllowedPrefixEnvNames);
    const val = randomFromAlphabet(rng, ValueAlpha, 1, 8);
    if (pwsh) {
      const q = rng() < 0.5 ? "'" : '"';
      const trail = rng() < 0.7 ? " " : "";
      prefixStr += `$env:${name}=${q}${val}${q};${trail}`;
    } else if (posix) {
      prefixStr += `${name}=${val} `;
    }
  }

  parts.push(`${prefixStr}dotnet`);
  parts.push("test");

  // 2. Items (0..4)
  const itemCount = randInt(rng, 0, 4);
  let hasFilter = false;
  let hasLogger = false;
  for (let i = 0; i < itemCount; i++) {
    const roll = rng();
    if (!hasFilter && roll < 0.35) {
      hasFilter = true;
      const q = shell === "cmd" ? '"' : rng() < 0.5 ? '"' : "'";
      const f = randomFromAlphabet(rng, FilterAlpha, 1, 16);
      parts.push("--filter", `${q}${f}${q}`);
    } else if (!hasLogger && roll < 0.55) {
      hasLogger = true;
      const l = randomFromAlphabet(rng, LoggerAlpha, 1, 14);
      parts.push("--logger", `"${l}"`);
    } else {
      let arg = "";
      for (let attempt = 0; attempt < 20; attempt++) {
        const lead = randomFromAlphabet(rng, ArgLeadAlpha, 1, 1);
        const tail = randomFromAlphabet(rng, posix ? BashArgAlpha : WinArgAlpha, 0, 8);
        const candidate = lead + tail;
        if (/^--(?:filter|logger)(?:=|:|$)/i.test(candidate)) {
          continue;
        }
        if (pwsh && candidate.startsWith("-") && candidate.includes(".")) {
          continue;
        }
        if (
          posix &&
          candidate.includes("/") &&
          (candidate.includes("//") || candidate.includes("=") || candidate.includes(":"))
        ) {
          continue;
        }
        arg = candidate;
        break;
      }
      if (arg !== "") {
        parts.push(arg);
      }
    }
  }

  // 3. Optional redirects: at most one stdout redirect (> or >>) and at most one 2>&1
  const addStdout = rng() < 0.5;
  const addStderr = rng() < 0.4;
  const redirTokens: string[] = [];
  if (addStdout) {
    const op = rng() < 0.5 ? ">" : ">>";
    const sep = rng() < 0.5 ? " " : "";
    const stem = randomFromAlphabet(rng, SafePathChars, 1, 6);
    const useSub = rng() < 0.35;
    const slash = posix ? "/" : rng() < 0.5 ? "/" : "\\";
    const target = useSub ? `sub${slash}r${caseIndex}_${stem}.log` : `r${caseIndex}_${stem}.log`;
    redirTokens.push(`${op}${sep}${target}`);
  }
  if (addStderr) {
    redirTokens.push("2>&1");
  }
  if (redirTokens.length === 2 && rng() < 0.5) {
    redirTokens.reverse();
  }
  parts.push(...redirTokens);

  const padLeft = rng() < 0.2 ? " " : "";
  const padRight = rng() < 0.2 ? " " : "";
  return `${padLeft}${parts.join(" ")}${padRight}`;
}

function detectShells(): ShellSpec[] {
  const specs: ShellSpec[] = [];
  const probe = (
    shell: ShellName,
    file: string,
    probeArgs: string[],
    makeArgs: (c: string) => string[],
    verbatim: boolean,
  ): void => {
    const r = spawnSync(file, probeArgs, { windowsHide: true, windowsVerbatimArguments: verbatim, timeout: 10_000 });
    specs.push({
      shell,
      available: !r.error && r.status === 0,
      file,
      makeArgs,
      verbatim,
    });
  };

  const bashFile = gitBashPath();
  probe("bash", bashFile, ["-c", "exit 0"], (c) => ["-c", c], false);
  if (process.platform !== "win32") {
    probe("sh", "/bin/sh", ["-c", "exit 0"], (c) => ["-c", c], false);
  }
  probe(
    "pwsh",
    "pwsh",
    ["-NoProfile", "-NonInteractive", "-Command", "exit 0"],
    (c) => ["-NoProfile", "-NonInteractive", "-Command", c],
    false,
  );
  if (process.platform === "win32") {
    probe(
      "powershell",
      "powershell",
      ["-NoProfile", "-NonInteractive", "-Command", "exit 0"],
      (c) => ["-NoProfile", "-NonInteractive", "-Command", c],
      false,
    );
    const comspec = process.env.ComSpec || "cmd.exe";
    probe("cmd", comspec, ["/d", "/s", "/c", '"exit 0"'], (c) => ["/d", "/s", "/c", `"${c}"`], true);
  }
  return specs;
}

function runShellCase(
  spec: ShellSpec,
  command: string,
  cwd: string,
  env: NodeJS.ProcessEnv,
): Promise<{ status: number | null; stderr: string }> {
  return new Promise((resolve) => {
    const child = spawn(spec.file, spec.makeArgs(command), {
      cwd,
      env,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
      windowsVerbatimArguments: spec.verbatim,
    });
    const errChunks: Buffer[] = [];
    child.stderr?.on("data", (chunk: Buffer) => errChunks.push(chunk));
    child.on("error", (err) => resolve({ status: null, stderr: err.message }));
    child.on("close", (code) => resolve({ status: code, stderr: Buffer.concat(errChunks).toString("utf8") }));
  });
}

function buildStubEnv(baseEnv: NodeJS.ProcessEnv, stubDir: string, logPath: string): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = { ...baseEnv };
  // Remove any inherited allowed prefix vars (case-insensitively on Windows)
  for (const key of Object.keys(env)) {
    const lower = key.toLowerCase();
    if (lower === "diffengine_disabled" || lower === "dotnet_cli_ui_language") {
      delete env[key];
    }
  }
  const pathKey = Object.keys(env).find((k) => k.toLowerCase() === "path") ?? "PATH";
  env[pathKey] = `${stubDir}${path.delimiter}${env[pathKey] ?? ""}`;
  env.GSHARP_LANG_LOG = logPath;
  return env;
}

/** Fixed negative table: commands that must NEVER be accepted by parseSharedCommand (design §9.1 & §9.4). */
function checkFixedNegatives(assert: AssertModule): void {
  const allShells: readonly ShellName[] = ["bash", "sh", "pwsh", "powershell", "cmd"];
  const rejectedEverywhere: readonly [string, string][] = [
    ["empty", ""],
    ["spaces only", "   "],
    ["newline LF", 'dotnet test X --filter "Name~A"\necho pwn'],
    ["newline CRLF", 'dotnet test X --filter "Name~A"\r\necho pwn'],
    ["tab", 'dotnet test\tX --filter "Name~A"'],
    // §9.1 PowerShell bare-value command execution hole
    ["ps bare value + command", '$env:DiffEngine_Disabled= hostname; dotnet test X --no-build --filter "Name~A"'],
    ["ps bare value", '$env:DiffEngine_Disabled=true; dotnet test X --no-build --filter "Name~A"'],
    // §9.1 PowerShell smart quotes (U+2018..U+201E)
    ["smart single quote prefix", '$env:DiffEngine_Disabled=\u2018true\u2019; dotnet test X --filter "Name~A"'],
    ["smart double quote prefix", '$env:DiffEngine_Disabled=\u201Ctrue\u201D; dotnet test X --filter "Name~A"'],
    ["smart double quote filter", "dotnet test X --filter \u201CName~A\u201D"],
    ["smart single quote filter", "dotnet test X --filter \u2018Name~A\u2019"],
    ["non-ASCII in arg", 'dotnet test 日本語 --filter "Name~A"'],
    // cmd ^ escape, %VAR%, set
    ["cmd caret quote", 'dotnet test X --filter ^"Name~A^"'],
    ["cmd percent var", 'dotnet test X --filter "Name~A" %EXTRA%'],
    ["cmd set prefix", 'set DiffEngine_Disabled=true && dotnet test X --filter "Name~A"'],
    // Separators, pipes, substitutions, backticks, globs, braces
    ["semicolon chain", 'dotnet test X --filter "Name~A"; echo hi'],
    ["and-and chain", 'dotnet build X && dotnet test X --no-build --filter "Name~A"'],
    ["or-or chain", 'dotnet test X --filter "Name~A" || echo fail'],
    ["pipe", 'dotnet test X --filter "Name~A" | tee log'],
    ["command substitution", 'dotnet test $(cat p) --filter "Name~A"'],
    ["backtick", 'dotnet test `cat p` --filter "Name~A"'],
    ["variable expansion", 'dotnet test X --filter "$FILTER"'],
    ["glob star", 'dotnet test tests/* --filter "Name~A"'],
    ["glob question", 'dotnet test tests/? --filter "Name~A"'],
    ["brace expansion", 'dotnet test tests/{A,B} --filter "Name~A"'],
    ["input redirect", 'dotnet test X --filter "Name~A" < in.txt'],
    ["ps all-stream redirect", "dotnet test X --filter 'Name~A' *> out.log"],
    ["redirect without leading space", "dotnet test 2>out.log"],
    ["redirect attached to arg", "dotnet test X>out.log"],
    // Disallowed env prefixes
    ["disallowed bash prefix", 'ROWBATCH_ROOTS=1 dotnet test X --no-build --filter "Name~A"'],
    ["disallowed pwsh prefix", "$env:ROWBATCH_ROOTS='1'; dotnet test X --no-build --filter \"Name~A\""],
    ["Set-Item Env", 'Set-Item Env:DiffEngine_Disabled true; dotnet test X --filter "Name~A"'],
    // Filter / logger form restrictions
    ["unquoted filter", "dotnet test X --filter Name~A"],
    ["equals filter", 'dotnet test X --filter="Name~A"'],
    ["duplicate filter", 'dotnet test X --filter "Name~A" --filter "Name~B"'],
    ["single-quoted logger", "dotnet test X --logger 'console;verbosity=detailed'"],
    ["duplicate logger", 'dotnet test X --logger "a" --logger "b"'],
    ["unbalanced quote", 'dotnet test X --filter "Name~A'],
    ["trailing text after quote", 'dotnet test X --filter "Name~A"extra'],
    ["arg after redirect", "dotnet test X > out.log --no-build"],
    ["duplicate stdout redirect", "dotnet test X > a.log >> b.log"],
    ["duplicate stderr merge", "dotnet test X 2>&1 2>&1"],
    // Non-dotnet-test / non-just-recipe heads
    ["dotnet build", "dotnet build X"],
    ["dotnet.exe", 'dotnet.exe test X --filter "Name~A"'],
    ["just with flag before recipe", "just -f justfile test"],
    ["just with bash env prefix", "DiffEngine_Disabled=true just test"],
    ["just with pwsh env prefix", "$env:DiffEngine_Disabled='true'; just test"],
  ];

  for (const [label, command] of rejectedEverywhere) {
    for (const shell of allShells) {
      assert.strictEqual(
        parseSharedCommand(command, shell),
        null,
        `must reject (${label}) on ${shell}: ${JSON.stringify(command)}`,
      );
    }
  }

  // Shell-specific rejections
  assert.strictEqual(
    parseSharedCommand("dotnet test X --filter 'Name~A'", "cmd"),
    null,
    "cmd rejects single-quoted --filter",
  );
  assert.strictEqual(
    parseSharedCommand("DiffEngine_Disabled=true dotnet test X", "pwsh"),
    null,
    "pwsh rejects bash-style env prefix",
  );
  assert.strictEqual(
    parseSharedCommand("DiffEngine_Disabled=true dotnet test X", "cmd"),
    null,
    "cmd rejects bash-style env prefix",
  );
  assert.strictEqual(
    parseSharedCommand("$env:DiffEngine_Disabled='true'; dotnet test X", "bash"),
    null,
    "bash rejects PowerShell env prefix",
  );
  assert.strictEqual(
    parseSharedCommand("$env:DiffEngine_Disabled='true'; dotnet test X", "cmd"),
    null,
    "cmd rejects PowerShell env prefix",
  );
  assert.strictEqual(parseSharedCommand("dotnet test tests\\X", "bash"), null, "bash rejects backslash in ARG");
  assert.strictEqual(
    parseSharedCommand("dotnet test X > tmp\\out.log", "bash"),
    null,
    "bash rejects backslash in redirect PATH",
  );
  assert.strictEqual(
    parseSharedCommand("dotnet test -a.b", "pwsh"),
    null,
    "pwsh rejects leading-dash arg containing dot",
  );
  assert.strictEqual(
    parseSharedCommand("dotnet test -a.b", "powershell"),
    null,
    "powershell rejects leading-dash arg containing dot",
  );
  assert.strictEqual(
    parseSharedCommand("dotnet test a//b", "bash"),
    null,
    "bash rejects // in ARG (MSYS2 path conversion)",
  );
  assert.strictEqual(
    parseSharedCommand("dotnet test -x=/a", "bash"),
    null,
    "bash rejects = with / in ARG (MSYS2 path conversion)",
  );
}

/**
 * Property-based check: generates `countPerShell` commands per available shell, executes each against the stub
 * `dotnet` executable, and verifies exact invocation count (1), exact argv, exact env, and redirect file creation.
 */
export async function checkCommandLanguage(assert: AssertModule, countPerShell?: number): Promise<void> {
  checkFixedNegatives(assert);

  const envCount = Number(process.env[LanguageFuzzEnvVariable]);
  const perShell = countPerShell ?? (Number.isInteger(envCount) && envCount > 0 ? envCount : DefaultLanguageFuzzCount);

  const shells = detectShells();
  const available = shells.filter((s) => s.available);
  for (const skipped of shells.filter((s) => !s.available)) {
    process.stdout.write(`command-language property test: skipping ${skipped.shell} (not installed)\n`);
  }
  assert.ok(available.length > 0, "at least one shell must be available for command-language property tests");

  const root = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-lang-prop-"));
  try {
    const stubDir = path.join(root, "bin");
    fs.mkdirSync(stubDir, { recursive: true });
    const stubName = process.platform === "win32" ? "dotnet.exe" : "dotnet";
    const stubExe = path.join(stubDir, stubName);
    try {
      fs.linkSync(process.execPath, stubExe);
    } catch {
      fs.copyFileSync(process.execPath, stubExe);
    }
    const testScriptBody = [
      'const fs = require("node:fs");',
      "const log = process.env.GSHARP_LANG_LOG;",
      "const record = {",
      "  args: process.argv.slice(2),",
      "  env: {",
      "    DiffEngine_Disabled: process.env.DiffEngine_Disabled ?? null,",
      "    DOTNET_CLI_UI_LANGUAGE: process.env.DOTNET_CLI_UI_LANGUAGE ?? null,",
      "  },",
      "};",
      'fs.appendFileSync(log, JSON.stringify(record) + "\\n");',
      'process.stdout.write("ok\\n");',
      "",
    ].join("\n");

    const baseSeed = 0x20261127;
    const concurrency = Math.min(8, Math.max(2, os.cpus().length));

    for (let sIndex = 0; sIndex < available.length; sIndex++) {
      const spec = available[sIndex] as ShellSpec;
      const seed = (baseSeed + sIndex * 7919) >>> 0;
      const rng = makeRng(seed);
      const cases: { index: number; command: string }[] = [];
      for (let i = 0; i < perShell; i++) {
        cases.push({ index: i, command: generateTestCommand(rng, spec.shell, i) });
      }

      let cursor = 0;
      const worker = async (): Promise<void> => {
        while (cursor < cases.length) {
          const item = cases[cursor++] as { index: number; command: string };
          const parsed = parseSharedCommand(item.command, spec.shell);
          assert.ok(
            parsed !== null && parsed.kind === "test",
            `generated command must parse (shell=${spec.shell}, seed=${seed}, case=${item.index}): ${item.command}`,
          );
          const caseDir = path.join(root, `c-${spec.shell}-${item.index}`);
          fs.mkdirSync(path.join(caseDir, "sub"), { recursive: true });
          fs.writeFileSync(path.join(caseDir, "test"), testScriptBody);
          const logPath = path.join(root, `log-${spec.shell}-${item.index}.jsonl`);
          const env = buildStubEnv(process.env, stubDir, logPath);

          const res = await runShellCase(spec, item.command, caseDir, env);
          const diag = `shell=${spec.shell} seed=${seed} case=${item.index} cmd=${JSON.stringify(item.command)}`;
          assert.strictEqual(res.status, 0, `exit 0 (${diag}): stderr=${res.stderr}`);
          assert.ok(fs.existsSync(logPath), `stub ran (${diag})`);
          const lines = fs
            .readFileSync(logPath, "utf8")
            .split(/\r?\n/)
            .filter((line) => line.length > 0);
          assert.strictEqual(lines.length, 1, `dotnet invoked exactly once (${diag}), got ${lines.length}`);
          const actual = JSON.parse(lines[0] as string) as StubRecord;
          assert.deepStrictEqual(actual.args, parsed.args, `argv mismatch (${diag})`);

          const expectedEnv: Record<AllowedPrefixEnvName, string | null> = {
            DiffEngine_Disabled: null,
            DOTNET_CLI_UI_LANGUAGE: null,
          };
          for (const binding of parsed.env) {
            expectedEnv[binding.name] = binding.value;
          }
          assert.deepStrictEqual(actual.env, expectedEnv, `env mismatch (${diag})`);

          for (const target of parsed.redirects) {
            const fullTarget = path.resolve(caseDir, target);
            assert.ok(fs.existsSync(fullTarget), `redirect target created (${target}) (${diag})`);
          }
        }
      };

      await Promise.all(Array.from({ length: concurrency }, () => worker()));
    }
  } finally {
    removeDirectory(root);
  }
}
