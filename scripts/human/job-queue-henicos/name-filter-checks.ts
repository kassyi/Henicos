// Self-test checks of the name-only filter share: which commands nameFilterCheck reads, the list command, the verdict
// of the runner's output, and nameFilterShare with a fake list runner (the self-test never runs a real dotnet test).

import type { AssertModule, ClientContext, ShareMode } from "../job-queue/plugin-api.ts";
import * as path from "node:path";
import {
  assembliesOf,
  listCommandOf,
  listVerdict,
  nameFilterShare,
  type AssemblyFiles,
  type ListResult,
  type ListRun,
} from "./name-filter.ts";
import { nameFilterCheck, type DirectoryEntries } from "./share-rules.ts";

const Options = {
  exclusiveCategories: ["Stress", "Benchmark", "GpuTrap"],
  exclusiveEnv: ["GSHARP_CUDA_TRAP_TESTS"],
  sharedRecipes: ["test"],
};

function contextWith(env: NodeJS.ProcessEnv = {}, log: (text: string) => void = () => {}): ClientContext {
  return { cwd: ".", worktree: ".", configRoot: ".", shell: "bash", env, options: Options, log };
}

const Plain = "dotnet test tests/X --no-build --filter";

/** [command, project, filter] of every command that is read; the rest of the table is read as null. */
const Read: readonly [string, string, string][] = [
  [`${Plain} "FullyQualifiedName~Foo"`, "tests/X", "FullyQualifiedName~Foo"],
  [`${Plain} "Name~A|Name~B"`, "tests/X", "Name~A|Name~B"],
  [
    `${Plain} "Name~A | FullyQualifiedName=N.C.M | DisplayName~d"`,
    "tests/X",
    "Name~A|FullyQualifiedName=N.C.M|DisplayName~d",
  ],
  [`${Plain} "FullyQualifiedName=N.C.M"`, "tests/X", "FullyQualifiedName=N.C.M"],
  [`${Plain}="Name~Foo"`, "tests/X", "Name~Foo"],
  ['dotnet test tests/X/X.csproj --no-build --filter "Name~Foo"', "tests/X/X.csproj", "Name~Foo"],
  ['DiffEngine_Disabled=true dotnet test tests/X --no-build --filter "Name~Foo" -v n --nologo', "tests/X", "Name~Foo"],
  [`DOTNET_CLI_UI_LANGUAGE=en ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`DiffEngine_Disabled=true DOTNET_CLI_UI_LANGUAGE=en ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`DiffEngine_Disabled='true' ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`DiffEngine_Disabled="true" ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled="true"; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled=1; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DOTNET_CLI_UI_LANGUAGE=0; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled = 'true'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled = 'true' ; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled ='true'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled= 'true'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled='true';${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DOTNET_CLI_UI_LANGUAGE = 'en'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:diffengine_disabled='true'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled='true'; $env:DOTNET_CLI_UI_LANGUAGE='en'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled='a;b'; ${Plain} "Name~Foo"`, "tests/X", "Name~Foo"],
  [
    `$env:DiffEngine_Disabled='true'; C:/PROGRA~1/dotnet/dotnet.exe test tests/X --no-build --filter "Name~Foo"`,
    "tests/X",
    "Name~Foo",
  ],
  [`$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"; echo done`, "tests/X", "Name~Foo"],
  [`$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo" # rerun`, "tests/X", "Name~Foo"],
  ['dotnet.exe test tests/X --no-build --filter "Name~Foo" -nodeReuse:false', "tests/X", "Name~Foo"],
  [`${Plain} "Name~Foo"; echo done`, "tests/X", "Name~Foo"],
  ['C:/PROGRA~1/dotnet/dotnet.exe test tests/X --no-build --filter "Name~Foo"', "tests/X", "Name~Foo"],
  ['/usr/share/dotnet/dotnet test tests/X --no-build --filter "Name~Foo"', "tests/X", "Name~Foo"],
  [`${Plain} "Name~Foo" # rerun`, "tests/X", "Name~Foo"],
];

/** The directories the checks know: one project, a solution beside it, two projects, no project (others: missing). */
const Entries: DirectoryEntries = (directory) => {
  const name = directory.replaceAll("\\", "/");
  const known: Record<string, string[]> = {
    "tests/X": ["X.csproj", "Program.cs"],
    "tests/Sln": ["X.csproj", "All.slnx"],
    "tests/Two": ["A.csproj", "B.csproj"],
    "tests/None": ["a.cs"],
  };
  const key = Object.keys(known).find((each) => name.endsWith(`/${each}`));
  return key === undefined ? null : (known[key] ?? null);
};

/** Commands that stay with `shareOf`: a negated or mixed filter, a form that is not one project, anything unread. */
const NotRead: readonly string[] = [
  `${Plain} "Name!~Foo"`,
  `${Plain} "Name!=Foo"`,
  `${Plain} "FullyQualifiedName~A&Category=Gpu"`,
  `${Plain} "(Name~A)|Name~B"`,
  `${Plain} "Name~A|Category=Slow"`,
  `${Plain} "Category=Stress"`,
  `${Plain} "Name~"`,
  `${Plain} "Name~A!B"`,
  `${Plain} ""`,
  `${Plain} "Name~A" --filter "Name~B"`,
  "dotnet test tests/X --no-build",
  // The program: dotnet, dotnet.exe or an absolute path to one; no assignment or step hidden inside a path word
  'Configuration=Release;C:/PROGRA~1/dotnet/dotnet.exe test tests/X --no-build --filter "Name~Foo"',
  'DiffEngine_Disabled=true;Configuration=Release;C:/x/dotnet test tests/X --no-build --filter "Name~Foo"',
  'DiffEngine_Disabled=true;Configuration=Release dotnet test tests/X --no-build --filter "Name~Foo"',
  'tools/dotnet test tests/X --no-build --filter "Name~Foo"',
  './dotnet test tests/X --no-build --filter "Name~Foo"',
  'tools\\dotnet.exe test tests/X --no-build --filter "Name~Foo"',
  // Without --no-build the run builds inside the turn, after the list read the old assembly
  'dotnet test tests/X --filter "Name~Foo"',
  `dotnet test tests/X --filter "Name~Foo"; echo done`,
  `dotnet test tests/X --filter "Name~Foo" # rerun`,
  // A step or an assignment that changes the directory or the environment the list would read
  `cd tests && ${Plain} "Name~Foo"`,
  `export Configuration=Debug; ${Plain} "Name~Foo"`,
  `Configuration=Debug ${Plain.slice(0)} "Name~Foo"`,
  `Configuration=Debug DiffEngine_Disabled=true ${Plain} "Name~Foo"`,
  `Set-Location tests; ${Plain} "Name~Foo"`,
  `$env:Configuration='Debug'; ${Plain} "Name~Foo"`,
  `echo hi && ${Plain} "Name~Foo"`,
  `${Plain} "Name~Foo" && dotnet build tests/X`,
  `${Plain} "Name~Foo"; rm -rf tests/X/bin`,
  `${Plain} "Name~Foo" && cp a b`,
  `$CMD && ${Plain} "Name~Foo"`,
  `${Plain} "Name~Foo" --logger "$L"`,
  // A directory that is not exactly one project, braces and globs
  'dotnet test tests/Sln --no-build --filter "Name~Foo"',
  'dotnet test tests/Two --no-build --filter "Name~Foo"',
  'dotnet test tests/None --no-build --filter "Name~Foo"',
  'dotnet test tests/Missing --no-build --filter "Name~Foo"',
  `${Plain} "Name~{A,B}"`,
  `${Plain} "Name~F*o"`,
  `${Plain} "Name~F?o"`,
  `${Plain} "Name~F[1]"`,
  'dotnet test tests/{X,Two} --no-build --filter "Name~Foo"',
  'dotnet test tests/* --no-build --filter "Name~Foo"',
  'dotnet test bin/Tests.dll --filter "Name~Foo"',
  'dotnet test Henicos.slnx --filter "Name~Foo"',
  'dotnet test Henicos.sln --filter "Name~Foo"',
  'dotnet test tests/X tests/Y --filter "Name~Foo"',
  `${Plain} "Name~Foo" | tee log`,
  `${Plain} "Name~Foo" || echo failed`,
  `${Plain} "Name~Foo" -- RunConfiguration.TestCaseFilter=Name~Bar`,
  `${Plain} "Name~Foo" -p:Configuration=Release`,
  `${Plain} "Name~Foo" --settings x.runsettings`,
  `${Plain} "Name~Foo" --unknown-option`,
  `${Plain} "Name~F\\oo"`,
  `${Plain} "Name~F$oo"`,
  `${Plain} "$FILTER"`,
  'dotnet test $P --filter "Name~Foo"',
  'dotnet build tests/X && dotnet test tests/X --no-build --filter "Name~Foo" && dotnet test tests/Y --filter "Name~Foo"',
  'dotnet build tests/X --filter "Name~Foo"',
  'just test --filter "Name~Foo"',
  `GSHARP_CUDA_TRAP_TESTS=1 ${Plain} "Name~Foo"`,
  `${Plain} "Name~Foo" -e GSHARP_CUDA_TRAP_TESTS=1`,
  // Allowlisted env prefix rules: non-allowlisted names, expanded values, invalid syntax/order
  `$env:ROWBATCH_ROOTS='1'; ${Plain} "Name~Foo"`,
  `ROWBATCH_ROOTS=1 ${Plain} "Name~Foo"`,
  `$env:GSHARP_CUDA_TRAP_TESTS='1'; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true'; $env:ROWBATCH_ROOTS='1'; ${Plain} "Name~Foo"`,
  `$env:ROWBATCH_ROOTS='1'; $env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled_Extra='true'; ${Plain} "Name~Foo"`,
  `export DiffEngine_Disabled=true; ${Plain} "Name~Foo"`,
  `DiffEngine_Disabled=true; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled="$FOO"; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=$FOO; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=$1; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled="$(echo true)"; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled="a\`b"; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=a\`b; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=a;b; ${Plain} "Name~Foo"`,
  `DiffEngine_Disabled="$FOO" ${Plain} "Name~Foo"`,
  `DiffEngine_Disabled=$FOO ${Plain} "Name~Foo"`,
  `DiffEngine_Disabled="a\\b" ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true' ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true'\n${Plain} "Name~Foo"`,
  `DiffEngine_Disabled=true\n${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=hostname; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=cmd; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled=true; ${Plain} "Name~Foo"`,
  `${Plain} "Name~Foo"; $env:DiffEngine_Disabled='true'`,
  `${Plain} "Name~Foo"; $env:ROWBATCH_ROOTS='1'`,
  `$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"; $env:DiffEngine_Disabled='false'`,
  `$env:DiffEngine_Disabled='true'; echo hi; ${Plain} "Name~Foo"`,
  `echo hi; $env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true'; DOTNET_CLI_UI_LANGUAGE=en ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true'; Configuration=Debug ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled='true'; $env:GSHARP_CUDA_TRAP_TESTS='1'; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled="a\\"; ${Plain} "Name~Foo"`,
  `$env:DiffEngine_Disabled="a\\"; ${Plain} "Name~Foo"; echo " & calc # "`,
  `$env:DOTNET_CLI_UI_LANGUAGE="a\\"; ${Plain} "Name~Foo"`,
];

/** Output samples of the test runner for the list command, as `dotnet test --list-tests` prints them. */
const NoMatch: ListRun = {
  exitCode: 0,
  ended: "exit",
  output:
    "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\r\n" +
    "VSTest version 18.0.0 (x64)\r\n\r\n" +
    "No test matches the given testcase filter `(Name~A)&(Category=Stress)` in X.dll\r\n",
};
const Listed: ListRun = {
  exitCode: 0,
  ended: "exit",
  output:
    "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\n" +
    "The following Tests are available:\n    N.PoolStressTests.A\n    N.PoolStressTests.B\n",
};

const Verdicts: readonly [string, ListResult, ShareMode, number][] = [
  ["no match", NoMatch, "shared", 0],
  ["two listed", Listed, "exclusive", 2],
  ["listed even beside the no match line", { exitCode: 0, output: `${NoMatch.output}    N.T.A\n` }, "exclusive", 1],
  ["exit code 1", { exitCode: 1, output: NoMatch.output }, "exclusive", 0],
  ["no exit code (timeout)", { exitCode: null, output: NoMatch.output }, "exclusive", 0],
  ["empty output", { exitCode: 0, output: "" }, "exclusive", 0],
  ["no build: error text", { exitCode: 1, output: "error: The test source file does not exist.\n" }, "exclusive", 0],
  ["text with no marks", { exitCode: 0, output: "Test run for tests/X/bin/Debug/net10.0/X.dll\n" }, "exclusive", 0],
];

export async function checkNameFilter(assert: AssertModule): Promise<void> {
  const context = contextWith();
  for (const [command, project, filter] of Read) {
    assert.deepStrictEqual(nameFilterCheck(command, context, Entries), { project, filter }, `read: ${command}`);
  }
  for (const command of NotRead) {
    assert.strictEqual(nameFilterCheck(command, context, Entries), null, `not read: ${command}`);
  }
  const carried = nameFilterCheck(`${Plain} "Name~Foo" -c Release -f net10.0`, context, Entries);
  assert.deepStrictEqual(
    carried,
    { project: "tests/X", filter: "Name~Foo", configuration: "Release", framework: "net10.0" },
    "-c and -f are carried to the list",
  );
  assert.strictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, contextWith({ GSHARP_CUDA_TRAP_TESTS: "1" }), Entries),
    null,
  );
  assert.notStrictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, contextWith({ GSHARP_CUDA_TRAP_TESTS: "0" }), Entries),
    null,
    "0 leaves the trap tests off",
  );
  assert.strictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, { ...context, options: {} }, Entries),
    null,
    "no share options: not read",
  );
  const pwshContext = { ...context, shell: "pwsh" as const };
  assert.deepStrictEqual(
    nameFilterCheck(`$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"`, pwshContext, Entries),
    { project: "tests/X", filter: "Name~Foo" },
    "pwsh allows single-quoted allowlisted $env: prefix",
  );
  assert.deepStrictEqual(
    nameFilterCheck(`$env:DiffEngine_Disabled=1; ${Plain} "Name~Foo"`, pwshContext, Entries),
    { project: "tests/X", filter: "Name~Foo" },
    "pwsh allows numeric bare allowlisted $env: prefix",
  );
  assert.strictEqual(
    nameFilterCheck(`$env:DiffEngine_Disabled=hostname; ${Plain} "Name~Foo"`, pwshContext, Entries),
    null,
    "pwsh rejects bareword command in $env: RHS",
  );
  assert.strictEqual(
    nameFilterCheck(`$env:DiffEngine_Disabled="a\\"; ${Plain} "Name~Foo"`, pwshContext, Entries),
    null,
    "pwsh rejects backslash inside double-quoted $env: RHS",
  );
  assert.deepStrictEqual(
    nameFilterCheck(`$env:DiffEngine_Disabled='a\\b'; ${Plain} "Name~Foo"`, pwshContext, Entries),
    { project: "tests/X", filter: "Name~Foo" },
    "pwsh allows backslash inside single-quoted $env: RHS",
  );
  const cmdContext = { ...context, shell: "cmd" as const };
  assert.deepStrictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, cmdContext, Entries),
    { project: "tests/X", filter: "Name~Foo" },
    "cmd reads plain dotnet test without env prefix",
  );
  for (const cmdRejected of [
    `$env:DiffEngine_Disabled='a&hostname'; ${Plain} "Name~Foo"`,
    `DiffEngine_Disabled='a&hostname' ${Plain} "Name~Foo"`,
    `$env:DiffEngine_Disabled='true'; ${Plain} "Name~Foo"`,
    `DiffEngine_Disabled=true ${Plain} "Name~Foo"`,
    `${Plain} 'Name~Foo>out.txt'`,
    `${Plain} "Name~Foo" && echo 'x"' " & calc & "`,
    `${Plain} "Name~Foo" && echo '(a)'`,
    `dotnet test tests/X --no-build --logger 'a\nb' --filter "Name~Foo"`,
    `dotnet test tests/X --no-build --logger 'a\rb' --filter "Name~Foo"`,
    `dotnet test tests/X --no-build --logger "a\nb" --filter "Name~Foo"`,
    `dotnet test tests/X --no-build --logger "a\rb" --filter "Name~Foo"`,
  ]) {
    assert.strictEqual(
      nameFilterCheck(cmdRejected, cmdContext, Entries),
      null,
      `cmd rejects env prefix or single-quoted metacharacter: ${cmdRejected}`,
    );
  }

  const test = { project: "tests/X", filter: "Name~A|Name~B" };
  assert.strictEqual(
    listCommandOf(test, Options.exclusiveCategories),
    'dotnet test "tests/X" --no-build --list-tests --filter "(Name~A|Name~B)&(Category=Stress|Category=Benchmark|Category=GpuTrap)"',
    "the list command takes its categories from the options",
  );
  assert.strictEqual(
    listCommandOf({ ...test, configuration: "Release", framework: "net10.0" }, ["Stress"]),
    'dotnet test "tests/X" --no-build --list-tests -c Release -f net10.0 --filter "(Name~A|Name~B)&(Category=Stress)"',
  );
  assert.strictEqual(listCommandOf(test, []), null, "no categories: nothing to list");
  assert.strictEqual(listCommandOf(test, ['Stress"; x']), null, "a category that is not a plain word");

  for (const [name, result, share, listed] of Verdicts) {
    assert.deepStrictEqual(listVerdict(result), { share, listed }, `verdict: ${name}`);
  }

  const calls: string[] = [];
  const lines: string[] = [];
  const answer =
    (result: ListRun | Error) =>
    (listCommand: string): Promise<ListRun> => {
      calls.push(listCommand);
      return result instanceof Error ? Promise.reject(result) : Promise.resolve(result);
    };
  const logging = contextWith({}, (text) => lines.push(text));
  const command = `${Plain} "FullyQualifiedName~PoolStressTests"`;
  const files = fakeFiles({ [Dll]: ["aaa", 0] });
  const shared = await nameFilterShare(command, logging, answer(NoMatch), Entries, files);
  assert.deepStrictEqual([shared?.share, shared?.reason], ["shared", "list:0"]);
  assert.strictEqual(calls.length, 1, "one list job");
  assert.match(calls[0] as string, /--list-tests --filter "\(FullyQualifiedName~PoolStressTests\)&\(Category=Stress\|/);
  assert.match(
    lines[0] as string,
    /^GPU share: shared \(list of exclusive tests: 0, /,
    "one line says what was decided",
  );
  const listedAnswer = await nameFilterShare(command, logging, answer(Listed), Entries, files);
  assert.deepStrictEqual([listedAnswer?.share, listedAnswer?.reason], ["exclusive", "list:2"]);
  assert.match(lines[1] as string, /^GPU share: exclusive \(list of exclusive tests: 2, /);
  const failed = await nameFilterShare(command, logging, answer(new Error("spawn failed")), Entries, files);
  assert.deepStrictEqual([failed?.share, failed?.reason], ["exclusive", "list-failed"]);
  assert.match(lines[2] as string, /list failed: spawn failed/);
  const before = calls.length;
  assert.strictEqual(
    await nameFilterShare('dotnet test tests/X --filter "Name!~A"', logging, answer(NoMatch), Entries),
    undefined,
  );
  assert.strictEqual(await nameFilterShare("echo hi", logging, answer(NoMatch), Entries), undefined);
  assert.strictEqual(
    await nameFilterShare(`$env:ROWBATCH_ROOTS='1'; ${command}`, logging, answer(NoMatch), Entries, files),
    undefined,
  );
  assert.strictEqual(calls.length, before, "a command that is not read starts no list job");
  const envPrefixed = await nameFilterShare(
    `$env:DiffEngine_Disabled='true'; ${command}`,
    logging,
    answer(NoMatch),
    Entries,
    files,
  );
  assert.deepStrictEqual([envPrefixed?.share, envPrefixed?.reason], ["shared", "list:0"]);
  assert.strictEqual(calls.length, before + 1, "allowlisted $env: prefix starts a list job");

  // The fingerprint of the assemblies the list read: unchanged, rebuilt while the turn waited, gone
  assert.strictEqual(await shared?.stillValid?.(), true, "the same assembly: still valid");
  files.hashes.set(resolved(Dll), "bbb");
  assert.strictEqual(await shared?.stillValid?.(), false, "rebuilt while the turn waited");
  files.hashes.set(resolved(Dll), null);
  assert.strictEqual(await shared?.stillValid?.(), false, "gone while the turn waited");

  const named: ListRun = { exitCode: 0, ended: "exit", output: NoMatch.output };
  const verdictOf = async (output: ListRun, fake: ReturnType<typeof fakeFiles>) =>
    nameFilterShare(
      command,
      contextWith({}, (text) => lines.push(text)),
      answer(output),
      Entries,
      fake,
    );
  const noLine = await verdictOf(
    { exitCode: 0, ended: "exit", output: "No test matches the given testcase filter `x`\n" },
    fakeFiles({ [Dll]: ["aaa", 0] }),
  );
  assert.deepStrictEqual(
    [noLine?.share, noLine?.reason],
    ["exclusive", "no-assemblies"],
    "no Test run line: the assembly is unknown",
  );
  assert.match(lines.at(-1) as string, /named no assembly/);
  const during = await verdictOf(named, fakeFiles({ [Dll]: ["aaa", Date.now() + 60_000] }));
  assert.deepStrictEqual(
    [during?.share, during?.reason],
    ["exclusive", "hash-mismatch"],
    "written after the list started",
  );
  assert.match(lines.at(-1) as string, /or was written during it/);
  const beforeMissing = calls.length;
  const missing = await verdictOf(named, fakeFiles({}));
  assert.deepStrictEqual(
    [missing?.share, missing?.reason],
    ["exclusive", "no-snapshot"],
    "no built assembly: nothing to fingerprint",
  );
  assert.strictEqual(calls.length, beforeMissing, "and no list job is started for it");
  // MSBuild copies obj to bin and keeps the compile time: a copy that lands while the list runs has an old time
  const copied = fakeFiles({ [Dll]: ["old", 0] });
  const copiedDuring = await nameFilterShare(
    command,
    contextWith({}, (text) => lines.push(text)),
    (listCommand) => {
      copied.hashes.set(resolved(Dll), "new");
      return answer(named)(listCommand);
    },
    Entries,
    copied,
  );
  assert.deepStrictEqual(
    [copiedDuring?.share, copiedDuring?.reason],
    ["exclusive", "hash-mismatch"],
    "the file changed during the list, with an old time",
  );
  // An assembly the project does not build under that name (a custom AssemblyName) is not in the snapshot
  const other = fakeFiles({ [Dll]: ["aaa", 0], "tests/X/bin/Debug/net10.0/Other.dll": ["aaa", 0] });
  const named2: ListRun = { exitCode: 0, ended: "exit", output: named.output.replace("/X.dll (", "/Other.dll (") };
  const renamed = await verdictOf(named2, other);
  assert.deepStrictEqual(
    [renamed?.share, renamed?.reason],
    ["exclusive", "hash-mismatch"],
    "the listed assembly is not <project>.dll",
  );
  // How the list job ended names the reason: a stop on the time limit leaves exit code 1 on Windows (taskkill /T /F)
  // and none elsewhere, a job that never started has none either, and a job that ended by itself has its own code
  const ends: readonly [string, ListRun, string][] = [
    ["exit code 1", { exitCode: 1, ended: "exit", output: NoMatch.output }, "list-failed"],
    ["ended by a signal", { exitCode: null, ended: "exit", output: NoMatch.output }, "list-failed"],
    ["timeout, Windows", { exitCode: 1, ended: "timeout", output: NoMatch.output }, "list-timeout"],
    ["timeout, elsewhere", { exitCode: null, ended: "timeout", output: NoMatch.output }, "list-timeout"],
    ["not started", { exitCode: null, ended: "spawn-error", output: "" }, "list-spawn-error"],
  ];
  const ended: Awaited<ReturnType<typeof verdictOf>>[] = [];
  for (const [name, result, reason] of ends) {
    ended.push(await verdictOf(result, fakeFiles({ [Dll]: ["aaa", 0] })));
    assert.deepStrictEqual([ended.at(-1)?.share, ended.at(-1)?.reason], ["exclusive", reason], `list job end: ${name}`);
  }
  const noMarker = await verdictOf(
    {
      exitCode: 0,
      ended: "exit",
      output: "Test run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\n",
    },
    fakeFiles({ [Dll]: ["aaa", 0] }),
  );
  assert.deepStrictEqual([noMarker?.share, noMarker?.reason], ["exclusive", "list-no-marker"]);
  const unsupported = await nameFilterShare(
    command,
    { ...logging, options: { ...Options, exclusiveCategories: [] } },
    answer(NoMatch),
    Entries,
    fakeFiles({ [Dll]: ["aaa", 0] }),
  );
  assert.deepStrictEqual([unsupported?.share, unsupported?.reason], ["exclusive", "list-unsupported"]);
  for (const excl of [
    listedAnswer,
    failed,
    noLine,
    during,
    missing,
    copiedDuring,
    renamed,
    ...ended,
    noMarker,
    unsupported,
  ]) {
    assert.ok(excl?.reason !== undefined && /^[a-z0-9:_-]{1,64}$/.test(excl.reason) && !excl.reason.includes("shared"));
  }
  const two: ListRun = {
    exitCode: 0,
    ended: "exit",
    output:
      "Test run for tests/X/bin/Debug/net8.0/X.dll (.NETCoreApp,Version=v8.0)\nTest run for tests/X/bin/Debug/net10.0/X.dll (.NETCoreApp,Version=v10.0)\n" +
      "No test matches the given testcase filter `x`\n",
  };
  const both = fakeFiles({ [Dll8]: ["ccc", 0], [Dll]: ["aaa", 0] });
  const frameworks = await verdictOf(two, both);
  assert.strictEqual(frameworks?.share, "shared");
  assert.strictEqual(await frameworks?.stillValid?.(), true);
  both.hashes.set(resolved(Dll8), "ddd");
  assert.strictEqual(await frameworks?.stillValid?.(), false, "the second framework's assembly changed");
  assert.strictEqual(
    assembliesOf("Test run for X.dll (a)\nTest run for X.dll (b)\n", ".").length,
    1,
    "the same assembly of two lines counts once",
  );
}

/** The assemblies of the project tests/X that the fake files hold, for two target frameworks. */
const Dll = "tests/X/bin/Debug/net10.0/X.dll";
const Dll8 = "tests/X/bin/Debug/net8.0/X.dll";

/** The absolute path under which a fake assembly is kept. */
function resolved(name: string): string {
  return path.resolve(".", name);
}

/** Fake assembly files: name -> [hash, mtime ms]; a missing name is a file that is not there. */
function fakeFiles(entries: Record<string, [string, number]>): AssemblyFiles & {
  hashes: Map<string, string | null>;
} {
  const hashes = new Map<string, string | null>();
  const times = new Map<string, number>();
  for (const [name, [hash, time]] of Object.entries(entries)) {
    hashes.set(resolved(name), hash);
    times.set(resolved(name), time);
  }
  return {
    hashes,
    mtimeMs: (file) => times.get(file) ?? null,
    sha256: (file) => hashes.get(file) ?? null,
    findNamed: (directory, fileName) =>
      [...hashes.keys()].filter(
        (file) =>
          file.startsWith(path.resolve(directory)) && path.basename(file).toLowerCase() === fileName.toLowerCase(),
      ),
  };
}
