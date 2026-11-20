// Self-test checks of the name-only filter share: which commands nameFilterCheck reads, the list command, the verdict
// of the runner's output, and nameFilterShare with a fake list runner (the self-test never runs a real dotnet test).

import type { AssertModule, ClientContext, ShareMode } from "../job-queue/plugin-api.ts";
import { listCommandOf, listVerdict, nameFilterShare, type ListResult } from "./name-filter.ts";
import { nameFilterCheck } from "./share-rules.ts";

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
  ['dotnet test tests/X/X.csproj --filter "Name~Foo"', "tests/X/X.csproj", "Name~Foo"],
  ['DiffEngine_Disabled=true dotnet test tests/X --filter "Name~Foo" -v n --nologo', "tests/X", "Name~Foo"],
  ['dotnet.exe test tests/X --filter "Name~Foo" -nodeReuse:false', "tests/X", "Name~Foo"],
  ['cd tests && dotnet test X --filter "Name~Foo"', "X", "Name~Foo"],
];

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
];

/** Output samples of the test runner for the list command, as `dotnet test --list-tests` prints them. */
const NoMatch: ListResult = {
  exitCode: 0,
  output:
    "Test run for X.dll (.NETCoreApp,Version=v10.0)\r\n" +
    "VSTest version 18.0.0 (x64)\r\n\r\n" +
    "No test matches the given testcase filter `(Name~A)&(Category=Stress)` in X.dll\r\n",
};
const Listed: ListResult = {
  exitCode: 0,
  output:
    "Test run for X.dll (.NETCoreApp,Version=v10.0)\n" +
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
  ["text with no marks", { exitCode: 0, output: "Test run for X.dll\n" }, "exclusive", 0],
];

export async function checkNameFilter(assert: AssertModule): Promise<void> {
  const context = contextWith();
  for (const [command, project, filter] of Read) {
    assert.deepStrictEqual(nameFilterCheck(command, context), { project, filter }, `read: ${command}`);
  }
  for (const command of NotRead) {
    assert.strictEqual(nameFilterCheck(command, context), null, `not read: ${command}`);
  }
  const carried = nameFilterCheck(`${Plain} "Name~Foo" -c Release -f net10.0`, context);
  assert.deepStrictEqual(
    carried,
    { project: "tests/X", filter: "Name~Foo", configuration: "Release", framework: "net10.0" },
    "-c and -f are carried to the list",
  );
  assert.strictEqual(nameFilterCheck(`${Plain} "Name~Foo"`, contextWith({ GSHARP_CUDA_TRAP_TESTS: "1" })), null);
  assert.notStrictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, contextWith({ GSHARP_CUDA_TRAP_TESTS: "0" })),
    null,
    "0 leaves the trap tests off",
  );
  assert.strictEqual(
    nameFilterCheck(`${Plain} "Name~Foo"`, { ...context, options: {} }),
    null,
    "no share options: not read",
  );

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
    (result: ListResult | Error) =>
    (listCommand: string): Promise<ListResult> => {
      calls.push(listCommand);
      return result instanceof Error ? Promise.reject(result) : Promise.resolve(result);
    };
  const logging = contextWith({}, (text) => lines.push(text));
  const command = `${Plain} "FullyQualifiedName~PoolStressTests"`;
  assert.strictEqual(await nameFilterShare(command, logging, answer(NoMatch)), "shared");
  assert.strictEqual(calls.length, 1, "one list job");
  assert.match(calls[0] as string, /--list-tests --filter "\(FullyQualifiedName~PoolStressTests\)&\(Category=Stress\|/);
  assert.match(
    lines[0] as string,
    /^GPU share: shared \(list of exclusive tests: 0, /,
    "one line says what was decided",
  );
  assert.strictEqual(await nameFilterShare(command, logging, answer(Listed)), "exclusive");
  assert.match(lines[1] as string, /^GPU share: exclusive \(list of exclusive tests: 2, /);
  assert.strictEqual(await nameFilterShare(command, logging, answer(new Error("spawn failed"))), "exclusive");
  assert.match(lines[2] as string, /list failed: spawn failed/);
  const before = calls.length;
  assert.strictEqual(
    await nameFilterShare('dotnet test tests/X --filter "Name!~A"', logging, answer(NoMatch)),
    undefined,
  );
  assert.strictEqual(await nameFilterShare("echo hi", logging, answer(NoMatch)), undefined);
  assert.strictEqual(calls.length, before, "a command that is not read starts no list job");
}
