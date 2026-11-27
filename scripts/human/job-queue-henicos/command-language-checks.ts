// Exhaustive pairwise property checks and fixed negative table for the GPU-share command language.
// Exercises real shells via `CommandShell.invocation` against stub executables, asserting exact invocation count (1),
// exact argv, exact env values, exact redirect file content, and zero unexpected files created in cwd.

import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import type { ShellName } from "../job-queue/constants.ts";
import type { AssertModule } from "../job-queue/plugin-api.ts";
import { removeDirectory } from "../job-queue/self-test-support.ts";
import { CommandShell } from "../job-queue/shell.ts";
import {
  AllowedFilterKeys,
  AllowedPrefixEnvNames,
  AllowedPrefixEnvValues,
  MaxCommandLength,
  parseSharedCommand,
  type AllowedPrefixEnvName,
} from "./command-language.ts";

/** Env variable passed by `job-queue.js --self-test --language-fuzz <n>` (caps or extends pairwise count). */
export const LanguageFuzzEnvVariable = "GSHARP_JOB_QUEUE_LANGUAGE_FUZZ";

interface StubRecord {
  args: string[];
  env: Record<AllowedPrefixEnvName, string | null>;
}

interface ShellSpec {
  shell: ShellName;
  available: boolean;
}

const BoundaryWords = ["a", "A1", "a.b", "a_b", "..a", "123", "1e2", "0x10", "W".repeat(64)] as const;

const FilterOperators = ["=", "!=", "~", "!~"] as const;

const OptionChoices = [
  "--no-build",
  "-nodeReuse:false",
  "--disable-build-servers",
  "--nologo",
  "-c Debug",
  "-c Release",
  "--configuration Debug",
  "--configuration Release",
  "-v q",
  "-v minimal",
  "--verbosity normal",
  "--verbosity detailed",
  "--blame-crash",
  "--blame-crash-dump-type mini",
  "--blame-crash-dump-type full",
  "--blame-hang-timeout 30s",
  "--blame-hang-timeout 5m",
] as const;

const LoggerChoices = [
  null,
  '--logger "trx;LogFileName=run-1.trx"',
  '--logger "console;verbosity=quiet"',
  '--logger "console;verbosity=minimal"',
  '--logger "console;verbosity=normal"',
  '--logger "console;verbosity=detailed"',
] as const;

function detectShells(): ShellSpec[] {
  const candidates: ShellName[] =
    process.platform === "win32" ? ["bash", "pwsh", "powershell", "cmd"] : ["bash", "sh", "pwsh"];
  return candidates.map((shell) => {
    const inv = CommandShell.invocation(shell, "exit 0");
    const r = spawnSync(inv.file, inv.args, {
      windowsHide: true,
      windowsVerbatimArguments: inv.verbatim,
      timeout: 10_000,
    });
    return { shell, available: !r.error && r.status === 0 };
  });
}

function runInShell(
  shell: ShellName,
  command: string,
  cwd: string,
  env: NodeJS.ProcessEnv,
): Promise<{ status: number | null; stderr: string }> {
  return new Promise((resolve) => {
    const inv = CommandShell.invocation(shell, command);
    const child = spawn(inv.file, inv.args, {
      cwd,
      env,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
      windowsVerbatimArguments: inv.verbatim,
    });
    const errChunks: Buffer[] = [];
    child.stderr?.on("data", (chunk: Buffer) => errChunks.push(chunk));
    child.on("error", (err) => resolve({ status: null, stderr: err.message }));
    child.on("close", (code) => resolve({ status: code, stderr: Buffer.concat(errChunks).toString("utf8") }));
  });
}

function buildStubEnv(baseEnv: NodeJS.ProcessEnv, stubDir: string, logPath: string): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = { ...baseEnv };
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

/**
 * Generates a pairwise covering table over the closed grammar axes for `shell` (no grammar-copying RNG).
 * Includes every NAME x VALUE, every OPTION, every KEY x OP x boundary WORD, every LOGGER, redirect modes,
 * item orderings, and a near-4000-character boundary command.
 */
function buildPairwiseCases(shell: ShellName, absRedirectPrefix: string): string[] {
  const cases: string[] = [];
  const pwsh = CommandShell.isPowerShell(shell);
  const posix = shell === "bash" || shell === "sh";

  const prefixes: string[] = [""];
  if (pwsh) {
    for (const name of AllowedPrefixEnvNames) {
      for (const val of AllowedPrefixEnvValues) {
        prefixes.push(`$env:${name}='${val}'; `);
      }
    }
    prefixes.push(`$env:DiffEngine_Disabled="true"; $env:DOTNET_CLI_UI_LANGUAGE='en-US'; `);
  } else if (posix) {
    for (const name of AllowedPrefixEnvNames) {
      for (const val of AllowedPrefixEnvValues) {
        prefixes.push(`${name}=${val} `);
      }
    }
    prefixes.push("DiffEngine_Disabled=true DOTNET_CLI_UI_LANGUAGE=en-US ");
  }

  const projects: (string | null)[] = [
    null,
    "tests/a",
    "tests/A1/a.b/a_b",
    "tests/..a/123/1e2/0x10.csproj",
    `tests/${"P".repeat(64)}`,
  ];

  // Build filter expressions covering every KEY x OP and boundary WORD
  const filters: (string | null)[] = [null];
  let wIdx = 0;
  for (const key of AllowedFilterKeys) {
    for (const op of FilterOperators) {
      const w1 = BoundaryWords[wIdx++ % BoundaryWords.length] as string;
      const w2 = BoundaryWords[wIdx++ % BoundaryWords.length] as string;
      const join = wIdx % 2 === 0 ? "&" : "|";
      const expr = `${key}${op}${w1}${join}Name~${w2}`;
      const q = shell === "cmd" ? '"' : wIdx % 3 === 0 ? "'" : '"';
      filters.push(`--filter ${q}${expr}${q}`);
    }
  }

  const redirectKinds = [
    "",
    " > tmp/out.log",
    " >> tmp/app.log",
    " > TestResults/out.log 2>&1",
    " >> TestResults/app.log 2>&1",
    " 2>&1",
    ...(absRedirectPrefix !== "" ? [` > ${absRedirectPrefix}/abs.log 2>&1`] : []),
  ];

  // Pairwise sweep: cover max axis length so every prefix, option, filter, logger, project, and redirect appears
  const count = Math.max(
    prefixes.length,
    OptionChoices.length,
    filters.length,
    LoggerChoices.length,
    projects.length,
    redirectKinds.length,
  );
  for (let i = 0; i < count; i++) {
    const pref = prefixes[i % prefixes.length] as string;
    const proj = projects[i % projects.length] as string | null;
    const opt1 = OptionChoices[i % OptionChoices.length] as string;
    const opt2 = OptionChoices[(i + 7) % OptionChoices.length] as string;
    const sameGroup =
      opt1 === opt2 ||
      (opt1.startsWith("-c") && opt2.startsWith("--configuration")) ||
      (opt2.startsWith("-c") && opt1.startsWith("--configuration")) ||
      (opt1.startsWith("-v") && opt2.startsWith("--verbosity")) ||
      (opt2.startsWith("-v") && opt1.startsWith("--verbosity"));
    const filt = filters[i % filters.length] as string | null;
    const logOpt = LoggerChoices[i % LoggerChoices.length] as string | null;
    const rawRedir = redirectKinds[i % redirectKinds.length] as string;
    const redir = rawRedir.replace("/abs.log", `/abs_${i}.log`);

    const items: string[] = [];
    if (i % 3 === 0) {
      if (proj !== null) items.push(proj);
      items.push(opt1);
      if (!sameGroup) items.push(opt2);
      if (filt !== null) items.push(filt);
      if (logOpt !== null) items.push(logOpt);
    } else if (i % 3 === 1) {
      items.push(opt1);
      if (proj !== null) items.push(proj);
      if (logOpt !== null) items.push(logOpt);
      if (filt !== null) items.push(filt);
    } else {
      if (filt !== null) items.push(filt);
      if (proj !== null) items.push(proj);
      items.push(opt1);
      if (logOpt !== null) items.push(logOpt);
    }
    cases.push(`${pref}dotnet test${items.length > 0 ? ` ${items.join(" ")}` : ""}${redir}`);
  }

  // Near-4000-character boundary command (long PROJECT + long FILTER WORDs, <= 4000 chars)
  const longProj = `tests/${"a".repeat(200)}/${"b".repeat(200)}`;
  const longTerm = `FullyQualifiedName~${"X".repeat(120)}`;
  const terms: string[] = [];
  let len = `dotnet test ${longProj} --no-build --filter ""`.length;
  while (len + longTerm.length + 1 <= MaxCommandLength) {
    terms.push(longTerm);
    len += longTerm.length + (terms.length > 1 ? 1 : 0);
  }
  const longCmd = `dotnet test ${longProj} --no-build --filter "${terms.join("|")}"`;
  cases.push(longCmd);

  return cases;
}

/** Fixed negative table: all review counterexamples and banned forms must return null. */
function checkFixedNegatives(assert: AssertModule): void {
  const allShells: readonly ShellName[] = ["bash", "sh", "pwsh", "powershell", "cmd"];
  const rejectedEverywhere: readonly [string, string][] = [
    ["empty", ""],
    ["spaces only", "   "],
    ["newline LF", 'dotnet test tests/X --filter "Name~A"\necho pwn'],
    ["newline CRLF", 'dotnet test tests/X --filter "Name~A"\r\necho pwn'],
    ["tab", 'dotnet test\ttests/X --filter "Name~A"'],
    // Over 4000 chars (B1: Git Bash ~8 KiB truncation)
    ["4001 chars", `dotnet test tests/${"a".repeat(3990)}`],
    ["8200 chars", `dotnet test tests/X --filter "Name~${"a".repeat(8200)}"`],
    // Rev 1 review (#2361): PowerShell bare value & smart quotes
    ["ps bare value + command", '$env:DiffEngine_Disabled= hostname; dotnet test tests/X --filter "Name~A"'],
    ["ps bare value", '$env:DiffEngine_Disabled=true; dotnet test tests/X --filter "Name~A"'],
    ["smart single quote prefix", "$env:DiffEngine_Disabled=\u2018true\u2019; dotnet test tests/X"],
    ["smart double quote prefix", "$env:DiffEngine_Disabled=\u201Ctrue\u201D; dotnet test tests/X"],
    ["smart double quote filter", "dotnet test tests/X --filter \u201CName~A\u201D"],
    ["smart single quote filter", "dotnet test tests/X --filter \u2018Name~A\u2019"],
    ["non-ASCII in project", 'dotnet test tests/日本語 --filter "Name~A"'],
    // Rev 2 review (#2373): B2, F1, F2, F3, F4
    ["B2: Category=Stress outside filter", 'dotnet test tests/X Category=Stress --filter "Name~A"'],
    ["B2/F2: pwsh colon arg injecting --filter", "dotnet test tests/X -a: -x:--filter Category=Stress"],
    ["F1: MSYS -a/b arg", "dotnet test -a/b"],
    ["F1: MSYS --a/b arg", "dotnet test --a/b"],
    ["F1: MSYS -I/a arg", "dotnet test -I/a"],
    ["F2: pwsh trailing colon -a:", "dotnet test -a: -b:"],
    ["F2: pwsh leading-dash dot -a.b", "dotnet test -a.b"],
    [
      "F3: just recipe with --filter arg",
      'just test --filter "Category!=Stress&Category!=Benchmark&Category!=GpuTrap"',
    ],
    ["F3: just recipe with extra arg", "just test --no-build"],
    ["F4: -p X=Y", "dotnet test tests/X -p Configuration=Release"],
    ["F4: -p:X=Y", "dotnet test tests/X -p:Configuration=Release"],
    ["F4: --property=X", "dotnet test tests/X --property=Configuration=Release"],
    ["F4: /p:X=Y", "dotnet test tests/X /p:Configuration=Release"],
    // Unquoted filter and parenthesized filter (intentionally excluded in rev 2.1)
    ["unquoted filter", "dotnet test tests/X --filter FullyQualifiedName~Foo"],
    ["parenthesized filter", 'dotnet test tests/X --filter "(Name~A|Name~B)"'],
    ["space inside filter", 'dotnet test tests/X --filter "Name~A | Name~B"'],
    ["unknown filter key", 'dotnet test tests/X --filter "Trait=Slow"'],
    ["duplicate --filter", 'dotnet test tests/X --filter "Name~A" --filter "Name~B"'],
    [
      "duplicate --logger",
      'dotnet test tests/X --logger "console;verbosity=normal" --logger "console;verbosity=quiet"',
    ],
    ["duplicate --no-build", "dotnet test tests/X --no-build --no-build"],
    ["duplicate -c and --configuration", "dotnet test tests/X -c Debug --configuration Release"],
    ["duplicate project", "dotnet test tests/A tests/B"],
    // Banned shell metacharacters
    ["dollar var", 'dotnet test tests/X --filter "$FILTER"'],
    ["semicolon chain", 'dotnet test tests/X --filter "Name~A"; echo hi'],
    ["and-and chain", 'dotnet build tests/X && dotnet test tests/X --no-build --filter "Name~A"'],
    ["pipe", 'dotnet test tests/X --filter "Name~A" | tee log'],
    ["caret escape", 'dotnet test tests/X --filter ^"Name~A^"'],
    ["percent var", 'dotnet test tests/X --filter "Name~A" %EXTRA%'],
    ["backtick", 'dotnet test `cat p` --filter "Name~A"'],
    ["backslash in project", "dotnet test tests\\X"],
    ["backslash in redirect", "dotnet test tests/X > C:\\x.log"],
    // Redirect restrictions
    ["2> alone", "dotnet test tests/X 2> tmp/err.log"],
    ["parent traversal redirect", "dotnet test tests/X > ../x.log"],
    ["dot segment in project", "dotnet test tests/../X"],
    ["Windows device NUL", "dotnet test tests/X > NUL"],
    ["Windows device nul.txt", "dotnet test tests/X > tmp/nul.txt"],
    ["Windows device CON", "dotnet test tests/X > CON"],
    ["Windows device PRN", "dotnet test tests/X > tmp/PRN.log"],
    ["Windows device AUX", "dotnet test tests/X > aux"],
    ["Windows device COM1", "dotnet test tests/X > tmp/com1.log"],
    ["Windows device LPT1", "dotnet test tests/X > tmp/LPT1"],
    ["redirect without space", "dotnet test tests/X >tmp/out.log"],
    ["redirect followed by arg", "dotnet test tests/X > tmp/out.log --no-build"],
    // Disallowed env prefix names / values
    ["disallowed bash prefix", 'ROWBATCH_ROOTS=1 dotnet test tests/X --filter "Name~A"'],
    ["disallowed pwsh prefix", "$env:ROWBATCH_ROOTS='1'; dotnet test tests/X --filter \"Name~A\""],
    ["disallowed prefix value", "DiffEngine_Disabled=maybe dotnet test tests/X"],
    ["Set-Item Env", "Set-Item Env:DiffEngine_Disabled true; dotnet test tests/X"],
    ["cmd set prefix", "set DiffEngine_Disabled=true && dotnet test tests/X"],
    // Disallowed logger / heads
    ["single-quoted logger", "dotnet test tests/X --logger 'console;verbosity=normal'"],
    ["unknown logger", 'dotnet test tests/X --logger "html;LogFileName=a.html"'],
    ["dotnet build", "dotnet build tests/X"],
    ["dotnet.exe", 'dotnet.exe test tests/X --filter "Name~A"'],
    ["just with flag", "just -f justfile test"],
    ["just with bash prefix", "DiffEngine_Disabled=true just test"],
    ["just with pwsh prefix", "$env:DiffEngine_Disabled='true'; just test"],
  ];

  for (const [label, command] of rejectedEverywhere) {
    for (const shell of allShells) {
      assert.strictEqual(
        parseSharedCommand(command, shell),
        null,
        `must reject (${label}) on ${shell}: ${JSON.stringify(command.slice(0, 120))}`,
      );
    }
  }

  // Shell-specific rejections
  assert.strictEqual(parseSharedCommand("dotnet test tests/X --filter 'Name~A'", "cmd"), null, "cmd rejects ' filter");
  assert.strictEqual(parseSharedCommand("DiffEngine_Disabled=true dotnet test tests/X", "pwsh"), null);
  assert.strictEqual(parseSharedCommand("DiffEngine_Disabled=true dotnet test tests/X", "cmd"), null);
  assert.strictEqual(parseSharedCommand("$env:DiffEngine_Disabled='true'; dotnet test tests/X", "bash"), null);
  assert.strictEqual(parseSharedCommand("$env:DiffEngine_Disabled='true'; dotnet test tests/X", "cmd"), null);
}

function listFilesRelative(dir: string): string[] {
  const out: string[] = [];
  const walk = (cur: string, rel: string): void => {
    for (const entry of fs.readdirSync(cur, { withFileTypes: true })) {
      const nextRel = rel === "" ? entry.name : `${rel}/${entry.name}`;
      if (entry.isDirectory()) {
        walk(path.join(cur, entry.name), nextRel);
      } else {
        out.push(nextRel);
      }
    }
  };
  walk(dir, "");
  return out.sort();
}

/**
 * Runs the pairwise property check across all installed shells using `CommandShell.invocation`, verifying exact
 * invocation count (1), argv, prefix env values, redirect file contents, and no unexpected files created.
 */
export async function checkCommandLanguage(assert: AssertModule, maxPerShell?: number): Promise<void> {
  checkFixedNegatives(assert);

  const envCap = Number(process.env[LanguageFuzzEnvVariable]);
  const limit = maxPerShell ?? (Number.isInteger(envCap) && envCap > 0 ? envCap : undefined);

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
      'process.stdout.write("STUB_OUT\\n");',
      'process.stderr.write("STUB_ERR\\n");',
      "",
    ].join("\n");

    const concurrency = Math.min(8, Math.max(2, os.cpus().length));

    for (const spec of available) {
      const absBase = path.join(root, `abs-${spec.shell}`);
      fs.mkdirSync(absBase, { recursive: true });
      let absPrefix = "";
      const normAbs = absBase.replace(/\\/g, "/");
      if (process.platform === "win32" && /^[A-Za-z]:\//.test(normAbs)) {
        absPrefix = spec.shell === "bash" ? `/${normAbs[0]?.toLowerCase()}/${normAbs.slice(3)}` : normAbs;
      }

      const allCases = buildPairwiseCases(spec.shell, absPrefix);
      const selected = limit !== undefined ? allCases.slice(0, limit) : allCases;

      let cursor = 0;
      const worker = async (): Promise<void> => {
        while (cursor < selected.length) {
          const index = cursor++;
          const command = selected[index] as string;
          const parsed = parseSharedCommand(command, spec.shell);
          assert.ok(
            parsed !== null && parsed.kind === "test",
            `generated command must parse (shell=${spec.shell}, case=${index}): ${command.slice(0, 140)}`,
          );

          const caseDir = path.join(root, `c-${spec.shell}-${index}`);
          fs.mkdirSync(path.join(caseDir, "tmp"), { recursive: true });
          fs.mkdirSync(path.join(caseDir, "TestResults"), { recursive: true });
          fs.writeFileSync(path.join(caseDir, "test"), testScriptBody);
          if (parsed.redirect.stdout !== null && parsed.redirect.append) {
            const existingTarget = resolveRedirectTarget(parsed.redirect.stdout, caseDir);
            fs.writeFileSync(existingTarget, "PRIOR\n");
          }
          const logPath = path.join(root, `log-${spec.shell}-${index}.jsonl`);
          const env = buildStubEnv(process.env, stubDir, logPath);

          const res = await runInShell(spec.shell, command, caseDir, env);
          const diag = `shell=${spec.shell} case=${index} cmd=${JSON.stringify(command.slice(0, 140))}`;
          // Windows PowerShell 5.1 turns native stderr under `2>&1` into a non-terminating ErrorRecord ($? = $false),
          // causing CommandShell's wrapper to exit 1 even when the native process exited 0.
          if (spec.shell === "powershell" && parsed.redirect.mergeStderr) {
            assert.ok(res.status === 0 || res.status === 1, `exit 0/1 on ps5.1 2>&1 (${diag}): stderr=${res.stderr}`);
          } else {
            assert.strictEqual(res.status, 0, `exit 0 (${diag}): stderr=${res.stderr}`);
          }
          assert.ok(fs.existsSync(logPath), `stub ran (${diag})`);
          const lines = fs
            .readFileSync(logPath, "utf8")
            .split(/\r?\n/)
            .filter((line) => line.length > 0);
          assert.strictEqual(lines.length, 1, `dotnet invoked exactly once (${diag}), got ${lines.length}`);
          const actual = JSON.parse(lines[0] as string) as StubRecord;
          assert.deepStrictEqual(actual.args, parsed.argv, `argv mismatch (${diag})`);

          const expectedEnv: Record<AllowedPrefixEnvName, string | null> = {
            DiffEngine_Disabled: null,
            DOTNET_CLI_UI_LANGUAGE: null,
          };
          for (const binding of parsed.env) {
            expectedEnv[binding.name] = binding.value;
          }
          assert.deepStrictEqual(actual.env, expectedEnv, `env mismatch (${diag})`);

          // Verify redirect target content and that no unexpected files were created in caseDir
          const expectedFiles = ["test"];
          if (parsed.redirect.stdout !== null) {
            const fullTarget = resolveRedirectTarget(parsed.redirect.stdout, caseDir);
            assert.ok(fs.existsSync(fullTarget), `redirect target created (${diag})`);
            const rawBuf = fs.readFileSync(fullTarget);
            // Windows PowerShell 5.1 writes UTF-16LE for `>` / `>>` (even when appending to a UTF-8 file)
            const content =
              rawBuf.length >= 2 && rawBuf[0] === 0xff && rawBuf[1] === 0xfe
                ? rawBuf.subarray(2).toString("utf16le")
                : rawBuf.includes(0)
                  ? rawBuf.toString("latin1").replace(/\0/g, "")
                  : rawBuf.toString("utf8");
            assert.ok(content.includes("STUB_OUT"), `redirect file contains STUB_OUT (${diag})`);
            if (parsed.redirect.append) {
              assert.ok(content.includes("PRIOR"), `append redirect preserves prior content (${diag})`);
            }
            if (parsed.redirect.mergeStderr) {
              assert.ok(content.includes("STUB_ERR"), `2>&1 merges STUB_ERR into redirect file (${diag})`);
            }
            const rel = path.relative(caseDir, fullTarget).replace(/\\/g, "/");
            if (!rel.startsWith("..") && !path.isAbsolute(rel)) {
              expectedFiles.push(rel);
            }
          }
          assert.deepStrictEqual(
            listFilesRelative(caseDir),
            expectedFiles.sort(),
            `no unexpected files created in cwd (${diag})`,
          );
        }
      };

      await Promise.all(Array.from({ length: concurrency }, () => worker()));
    }
  } finally {
    removeDirectory(root);
  }
}

function resolveRedirectTarget(target: string, caseDir: string): string {
  if (process.platform === "win32" && /^\/[a-z]\//.test(target)) {
    return `${target[1]?.toUpperCase()}:/${target.slice(3)}`;
  }
  return path.resolve(caseDir, target);
}
